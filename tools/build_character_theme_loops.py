import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

import librosa
import numpy as np
import soundfile as sf


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "BGM/suno_character_themes_20261103"
WORK = ROOT / "build/qa_story/character_theme_audio"
TARGET_TEMPO = {"mina": 96, "akari": 108, "koharu": 112, "rei": 124}
STAGE = False


def decode(path, rate=16000, channels=1):
    raw = subprocess.check_output([
        "ffmpeg", "-v", "error", "-i", str(path), "-map", "0:a:0",
        "-ar", str(rate), "-ac", str(channels), "-f", "f32le", "-",
    ])
    return np.frombuffer(raw, np.float32).reshape(-1, channels).copy()


def loudness(path):
    result = subprocess.run([
        "ffmpeg", "-hide_banner", "-i", str(path), "-map", "0:a:0",
        "-af", "loudnorm=I=-19:TP=-2:LRA=11:print_format=json", "-f", "null", "-",
    ], capture_output=True, text=True, encoding="utf-8", check=True)
    return json.JSONDecoder().raw_decode(result.stderr[result.stderr.rfind("{\n"):])[0]


def analyze(path):
    y = decode(path)[:, 0]
    sr, hop = 16000, 256
    spectrum = np.abs(librosa.stft(y, n_fft=2048, hop_length=hop))
    harmonic, percussive = librosa.decompose.hpss(spectrum)
    chroma = librosa.feature.chroma_stft(S=harmonic ** 2, sr=sr, hop_length=hop)
    onset = librosa.onset.onset_strength(S=librosa.amplitude_to_db(spectrum), sr=sr, hop_length=hop)
    tempo, beat_frames = librosa.beat.beat_track(
        onset_envelope=onset, sr=sr, hop_length=hop,
        start_bpm=TARGET_TEMPO[path.stem.split("_")[0]], tightness=100,
    )
    beats = librosa.frames_to_time(beat_frames, sr=sr, hop_length=hop)
    rms = librosa.feature.rms(S=spectrum, frame_length=2048)[0]
    centroid = librosa.feature.spectral_centroid(S=spectrum, sr=sr)[0]
    candidates = []
    phrase_beats = 3 if not STAGE and path.stem.startswith("akari_") else 16
    for i, start in enumerate(beats):
        if not 8 <= start <= 45 or i < 8:
            continue
        for j in range(i + phrase_beats, len(beats) - 8, phrase_beats):
            end = beats[j]
            if not 105 <= end <= len(y) / sr - 5 or end - start < 90:
                continue
            width = 8
            a = np.stack([chroma[:, beat_frames[k]:beat_frames[k + 1]].mean(axis=1)
                          for k in range(i - width, i + width)])
            b = np.stack([chroma[:, beat_frames[k]:beat_frames[k + 1]].mean(axis=1)
                          for k in range(j - width, j + width)])
            similarity = float(np.mean(np.sum(a * b, axis=1) /
                               (np.linalg.norm(a, axis=1) * np.linalg.norm(b, axis=1) + 1e-9)))
            a_rms = float(np.mean(rms[beat_frames[i - 2]:beat_frames[i + 2]]))
            b_rms = float(np.mean(rms[beat_frames[j - 2]:beat_frames[j + 2]]))
            level_gap = float(abs(20 * np.log10((a_rms + 1e-8) / (b_rms + 1e-8))))
            a_bright = float(np.mean(centroid[beat_frames[i - 2]:beat_frames[i + 2]]))
            b_bright = float(np.mean(centroid[beat_frames[j - 2]:beat_frames[j + 2]]))
            score = similarity - 0.012 * level_gap - 0.03 * abs(np.log(a_bright / b_bright))
            candidates.append({"start": float(start), "end": float(end),
                               "crossfade": float(min(1.5, start - beats[i - 2])),
                               "beats": j - i, "chroma_similarity": similarity,
                               "level_gap_db": level_gap, "score": score})
    candidates.sort(key=lambda x: x["score"], reverse=True)
    return {"file": path.name, "phrase_beats": phrase_beats, "tempo_estimate": float(np.asarray(tempo).item()),
            "duration": len(y) / sr, "rms_dynamic_range_db": float(20 * np.log10(np.percentile(rms, 90) / np.percentile(rms, 20))),
            "spectral_centroid_hz": float(np.median(centroid)),
            "percussive_energy_fraction": float(np.sum(percussive ** 2) / np.sum(spectrum ** 2)),
            "loudness": loudness(path), "loop_candidates": candidates[:8],
            "auditory_review": "not_performed"}


def build(selection):
    path = SOURCE / selection["file"]
    y = decode(path, 48000, 2)
    start, end, overlap = [round(selection[k] * 48000) for k in ("start", "end", "crossfade")]
    assert 0 < overlap < start < end <= len(y)
    output = y[:end].copy()
    weight = (0.5 - 0.5 * np.cos(np.linspace(0, np.pi, overlap)))[:, None]
    output[end - overlap:end] = y[end - overlap:end] * (1 - weight) + y[start - overlap:start] * weight
    output[:480] *= np.linspace(0, 1, 480)[:, None]
    stem = path.stem.split("_")[0]
    wav = WORK / (stem + "_loop.wav")
    sf.write(wav, output, 48000, subtype="FLOAT")
    measured = loudness(wav)
    gain = min(-19 - float(measured["input_i"]), -2 - float(measured["input_tp"]))
    destination = ROOT / selection.get("output", "audio/bgm_theme_" + stem + ".ogg")
    subprocess.run([
        "ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(wav),
        "-af", f"volume={gain:.8f}dB", "-c:a", "libvorbis", "-q:a", "6",
        "-metadata", "ARTIST=Refrain / Suno", "-metadata", f"TITLE={selection['title']}",
        "-metadata", f"LOOPSTART={start}", "-metadata", f"LOOPEND={end}", str(destination),
    ], check=True)
    decoded = decode(destination, 48000, 2)
    seam = decoded[start] - decoded[-1]
    local_steps = np.diff(decoded[start - 2400:start + 2400], axis=0)
    window = 2400
    before = np.sqrt(np.mean(decoded[-window:] ** 2))
    after = np.sqrt(np.mean(decoded[start:start + window] ** 2))
    review = np.concatenate([decoded[-8 * 48000:], decoded[start:start + 12 * 48000]])
    sf.write(WORK / (stem + "_seam_review.wav"), review, 48000)
    result = dict(selection, output=str(destination.relative_to(ROOT)),
                  output_sha256=hashlib.sha256(destination.read_bytes()).hexdigest(),
                  source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                  loop_offset_seconds=start / 48000, duration_seconds=len(decoded) / 48000,
                  applied_gain_db=gain, loudness=loudness(destination),
                  boundary_step_peak=float(np.max(np.abs(seam))),
                  nearby_step_p99=float(np.percentile(np.abs(local_steps), 99)),
                  boundary_50ms_level_gap_db=float(abs(20 * np.log10(before / after))),
                  auditory_review="not_performed")
    assert result["boundary_step_peak"] < max(0.01, result["nearby_step_p99"] * 2)
    assert float(result["loudness"]["input_tp"]) <= -1.5
    resource = "res://" + destination.relative_to(ROOT).as_posix()
    imported = "res://.godot/imported/" + destination.name + "-" + hashlib.md5(resource.encode()).hexdigest() + ".oggvorbisstr"
    config_path = Path(str(destination) + ".import")
    if config_path.exists():
        config = config_path.read_text(encoding="utf-8")
        config = re.sub(r"(?m)^loop=.*$", "loop=true", config)
        config = re.sub(r"(?m)^loop_offset=.*$", f"loop_offset={start / 48000}", config)
    else:
        config = (f'[remap]\n\nimporter="oggvorbisstr"\ntype="AudioStreamOggVorbis"\npath="{imported}"\n'
                  f'\n[deps]\n\nsource_file="{resource}"\ndest_files=["{imported}"]\n'
                  f'\n[params]\n\nloop=true\nloop_offset={start / 48000}\nbpm=0\nbeat_count=0\nbar_beats=4\n')
    config_path.write_text(config, encoding="utf-8")
    return result


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["analyze", "build"])
    parser.add_argument("--stage", action="store_true")
    args = parser.parse_args()
    if args.stage:
        STAGE = True
        SOURCE = ROOT / "BGM/suno_stage_themes_20261104"
        WORK = ROOT / "build/qa_story/stage_music_20261104/audio"
        TARGET_TEMPO = {"mina": 144, "akari": 148, "koharu": 154, "rei": 156, "tutorial": 128}
    WORK.mkdir(parents=True, exist_ok=True)
    if args.mode == "analyze":
        results = []
        for path in sorted(SOURCE.glob("*_[AB].mp3")):
            cache = WORK / (path.stem + "_analysis.json")
            result = analyze(path)
            cache.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
            results.append(result)
            print(json.dumps({k: v for k, v in result.items() if k not in ("loudness", "loop_candidates")}, ensure_ascii=False), flush=True)
        (SOURCE / "audio_analysis.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    else:
        selections = json.loads((SOURCE / "loop_selection.json").read_text(encoding="utf-8"))
        results = [build(selection) for selection in selections]
        (SOURCE / "loop_verification.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps([{k: result[k] for k in ("file", "duration_seconds", "loop_offset_seconds", "boundary_step_peak")}
                          for result in results], ensure_ascii=False, indent=2))
