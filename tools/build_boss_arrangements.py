import hashlib
import json
import subprocess
import sys
from pathlib import Path

import librosa
import numpy as np

from build_character_theme_loops import decode, loudness


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "BGM/suno_boss_arrangements_20261103"
WORK = ROOT / "build/qa_story/boss_arrangements"
TRACKS = {
    "akari": (168, 192, "84f9ebd4", "1a31c5b4"),
    "koharu": (172, 196, "fb7efea2", "3a8a6f1c"),
    "rei": (160, 184, "010acfe5", "60366234"),
    "mina": (136, 164, "f762d891", "162727bd"),
}


def inspect(path, expected):
    y = decode(path)[:, 0]
    sr, hop = 16000, 256
    onset = librosa.onset.onset_strength(y=y, sr=sr, hop_length=hop)
    tempo, frames = librosa.beat.beat_track(
        onset_envelope=onset, sr=sr, hop_length=hop, start_bpm=expected,
        tightness=100,
    )
    tempo = float(np.asarray(tempo).item())
    while tempo < expected * 0.65:
        tempo *= 2
    while tempo > expected * 1.5:
        tempo /= 2
    return tempo, len(y) / sr


def build(id, role, expected, clip):
    source = SOURCE / f"{id}_{role}_{clip}.mp3"
    measured, duration = inspect(source, expected)
    ratio = expected / measured
    normalized = WORK / f"{id}_{role}_tempo.wav"
    subprocess.run([
        "ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(source),
        "-af", f"atempo={ratio:.9f}", "-ar", "48000", "-c:a", "pcm_f32le", str(normalized),
    ], check=True)
    y = decode(normalized)[:, 0]
    sr, hop = 16000, 256
    spectrum = np.abs(librosa.stft(y, n_fft=2048, hop_length=hop))
    chroma = librosa.feature.chroma_stft(S=spectrum ** 2, sr=sr, hop_length=hop)
    onset = librosa.onset.onset_strength(S=librosa.amplitude_to_db(spectrum), sr=sr, hop_length=hop)
    _, frames = librosa.beat.beat_track(onset_envelope=onset, sr=sr, hop_length=hop,
                                      bpm=expected, tightness=100)
    beats = librosa.frames_to_time(frames, sr=sr, hop_length=hop)
    rms = librosa.feature.rms(S=spectrum, frame_length=2048)[0]
    candidates = []
    for i, start in enumerate(beats):
        if not 8 <= start <= 40 or i < 4:
            continue
        for j in range(i + 16, len(beats) - 8, 16):
            end = beats[j]
            if end - start < 100 or end > len(y) / sr - 8:
                continue
            a, b = chroma[:, frames[i - 2]:frames[i + 2]].mean(axis=1), chroma[:, frames[j - 2]:frames[j + 2]].mean(axis=1)
            similarity = float(np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b) + 1e-9))
            gap = float(abs(20 * np.log10((rms[frames[i]] + 1e-8) / (rms[frames[j]] + 1e-8))))
            candidates.append((similarity - gap * .015 + (end - start) * .0003, start, end, similarity, gap))
    _, start, end, similarity, gap = max(candidates)
    overlap = 60 / expected * 2
    pcm = decode(normalized, 48000, 2)
    first, last, count = (round(t * 48000) for t in (start, end, overlap))
    output = pcm[:last].copy()
    weight = (0.5 - 0.5 * np.cos(np.linspace(0, np.pi, count)))[:, None]
    output[-count:] = output[-count:] * (1 - weight) + pcm[first - count:first] * weight
    output[:480] *= np.linspace(0, 1, 480)[:, None]
    import soundfile as sf
    loop = WORK / f"{id}_{role}_loop.wav"
    sf.write(loop, output, 48000, subtype="FLOAT")
    level = loudness(loop)
    gain = min(-17 - float(level["input_i"]), -2 - float(level["input_tp"]))
    destination = ROOT / f"audio/bgm_boss_{id}_{role}.ogg"
    subprocess.run([
        "ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(loop),
        "-af", f"volume={gain:.8f}dB", "-c:a", "libvorbis", "-q:a", "6",
        "-metadata", "ARTIST=Refrain / Suno", "-metadata", f"LOOPSTART={first}",
        "-metadata", f"LOOPEND={last}", str(destination),
    ], check=True)
    resource = "res://" + destination.relative_to(ROOT).as_posix()
    imported = "res://.godot/imported/" + destination.name + "-" + hashlib.md5(resource.encode()).hexdigest() + ".oggvorbisstr"
    Path(str(destination) + ".import").write_text(
        f'[remap]\n\nimporter="oggvorbisstr"\ntype="AudioStreamOggVorbis"\npath="{imported}"\n'
        f'\n[deps]\n\nsource_file="{resource}"\ndest_files=["{imported}"]\n'
        f'\n[params]\n\nloop=true\nloop_offset={first / 48000}\nbpm={expected}\nbeat_count=0\nbar_beats=4\n', encoding="utf-8")
    decoded = decode(destination, 48000, 2)
    step = float(np.max(np.abs(decoded[first] - decoded[-1])))
    nearby = float(np.percentile(np.abs(np.diff(decoded[first - 2400:first + 2400], axis=0)), 99))
    result = {
        "id": id, "role": role, "source": str(source.relative_to(ROOT)),
        "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "source_tempo_estimate": measured, "target_bpm": expected,
        "pitch_preserving_tempo_ratio": ratio, "source_duration": duration,
        "output": str(destination.relative_to(ROOT)), "duration": len(decoded) / 48000,
        "loop_offset": first / 48000, "loop_chroma_similarity": similarity,
        "loop_level_gap_db": gap, "applied_gain_db": gain,
        "boundary_step_peak": step, "nearby_step_p99": nearby,
        "loudness": loudness(destination), "auditory_review": "not_performed",
        "melody_identity_and_vocal_absence": "requires_listening",
    }
    assert step < max(.01, nearby * 2), result
    assert float(result["loudness"]["input_tp"]) <= -1.5, result
    sf.write(WORK / f"{id}_{role}_seam_review.wav",
             np.concatenate([decoded[-8 * 48000:], decoded[first:first + 12 * 48000]]), 48000)
    return result


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    WORK.mkdir(parents=True, exist_ok=True)
    results = []
    for id, (normal, remix, normal_clip, remix_clip) in TRACKS.items():
        for role, bpm, clip in [("instrumental", normal, normal_clip), ("remix", remix, remix_clip)]:
            result = build(id, role, bpm, clip)
            results.append(result)
            (SOURCE / "audio_verification.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
            print(json.dumps(result, ensure_ascii=False), flush=True)
