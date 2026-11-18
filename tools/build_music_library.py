import argparse
import json
import re
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'assets/music'
QA = ROOT / 'build/qa_story/music_app_20261103'
THEMES = ROOT / 'BGM/suno_character_themes_20261103'
ARRANGEMENTS = ROOT / 'BGM/suno_boss_arrangements_20261103'
VOCAL_REVISIONS = ROOT / 'BGM/suno_vocal_revisions_20261118'
STAGES = ROOT / 'BGM/suno_stage_themes_20261104'
CHARACTERS = {'opening': 'Refrain', 'akari': 'あかり', 'koharu': 'こはる', 'rei': 'レイ', 'mina': 'ミナ'}
COVERS = {
    'opening': 'char/bg2/title/title_mina_v3.png',
    'akari': 'assets/music/art/akari_v1.png',
    'koharu': 'assets/music/art/koharu_v1.png',
    'rei': 'assets/music/art/rei_v1.png',
    'mina': 'assets/music/art/mina_v1.png',
}


def read_track(path, character, category, copy=False, copy_name=None):
    metadata = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-show_entries',
        'format=duration:format_tags', '-of', 'json', str(path)], encoding='utf-8'))['format']
    tags = metadata['tags']
    comment = tags.get('comment', '')
    assert 'made with suno' in comment.lower(), path
    source_id = re.search(r'id=([0-9a-f-]{36})', comment).group(1)
    lyrics = tags.get('lyrics-eng', '')
    if copy:
        destination = OUT / 'audio' / (copy_name or path.name)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, destination)
        path = destination
    return dict(id=source_id, title=tags['title'], character=character, artist=CHARACTERS[character],
        provider='Suno', source=f'https://suno.com/song/{source_id}', category=category,
        audio='res://' + path.relative_to(ROOT).as_posix(), duration=float(metadata['duration']),
        cover='res://' + COVERS[character], video=f'res://assets/music/pv/{character}.ogv',
        lyrics=lyrics, cues=[])


def make_library():
    tracks = [read_track(ROOT / 'audio/bgm_opening.mp3', 'opening', 'vocal')]
    tracks.append(read_track(ROOT / 'audio/bgm_boss_akari.mp3', 'akari', 'vocal'))
    tracks.append(read_track(VOCAL_REVISIONS / 'koharu_55e6f695.mp3', 'koharu', 'vocal', True))
    tracks.append(read_track(ROOT / 'audio/bgm_boss_rei.mp3', 'rei', 'vocal'))
    tracks.append(read_track(ARRANGEMENTS / 'mina_vocal_f973e5ff.mp3', 'mina', 'vocal', True))
    for character, choice in [('mina', 'B'), ('akari', 'B'), ('koharu', 'A'), ('rei', 'B')]:
        tracks.append(read_track(THEMES / f'{character}_{choice}.mp3', character, 'theme', True))
    for character in ('akari', 'koharu', 'rei', 'mina'):
        for version in ('instrumental', 'remix'):
            path = next(ARRANGEMENTS.glob(f'{character}_{version}_*.mp3'))
            tracks.append(read_track(path, character, version, True))
    for item in json.loads((STAGES / 'creation_plan.json').read_text(encoding='utf-8'))['tracks']:
        character = 'mina' if item['key'] == 'tutorial' else item['key']
        tracks.append(read_track(STAGES / item['file'], character, 'stage', True, f"stage_{item['file']}"))
    return tracks


def build_pvs(tracks, manifest_path):
    from imageio_ffmpeg import get_ffmpeg_exe

    # The installed FFmpeg 8.1.1 build emits corrupt Theora packets; use the bundled 7.1 encoder.
    theora_encoder = get_ffmpeg_exe()
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    clips = manifest['clips']
    if not clips or any(clip.get('review') != 'accepted' for clip in clips):
        raise ValueError('Every source clip must pass visual review before building the PVs.')
    destination = OUT / 'pv'
    destination.mkdir(parents=True, exist_ok=True)
    for track in (track for track in tracks if track['category'] == 'vocal'):
        character = track['character']
        selected = [clip for clip in clips if character == 'opening' or clip['character'] == character]
        if not selected:
            raise ValueError(f'No reviewed footage for {character}')
        if character == 'opening':
            selected.sort(key=lambda clip: (int(clip['id'].split('_')[1]), list(CHARACTERS).index(clip['character'])))
        sequence = manifest_path.parent / f'{character}_concat.txt'
        sequence.write_text(''.join(f"file '{(manifest_path.parent / (clip['id'] + '.mp4')).as_posix()}'\n"
                                    for clip in selected), encoding='utf-8')
        output = destination / f'{character}.ogv'
        subprocess.run([theora_encoder, '-y', '-hide_banner', '-loglevel', 'warning',
            '-f', 'concat', '-safe', '0', '-i', str(sequence),
            '-map', '0:v:0', '-an', '-vf', 'scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,setsar=1',
            '-c:v', 'libtheora', '-q:v', '7', '-g', '24', '-pix_fmt', 'yuv420p', str(output)], check=True)
        subprocess.run(['ffmpeg', '-v', 'error', '-xerror', '-i', str(output), '-f', 'null', '-'], check=True)
        subtitles = manifest_path.parent / f'{character}_lyrics.ass'
        def timestamp(seconds):
            centiseconds = round(seconds * 100)
            return f'{centiseconds // 360000}:{centiseconds // 6000 % 60:02}:{centiseconds // 100 % 60:02}.{centiseconds % 100:02}'
        subtitles.write_text(
            '[Script Info]\nScriptType: v4.00+\nPlayResX: 1280\nPlayResY: 720\nWrapStyle: 0\n'
            '[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, '
            'Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n'
            'Style: Lyrics,Zen Kaku Gothic New,30,&H00FFFFFF,&H00FFFFFF,&H00101018,&H80000000,-1,0,0,0,100,100,0,0,1,2,1,2,30,30,65,1\n'
            '[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n' +
            ''.join(f"Dialogue: 0,{timestamp(cue['start'])},{timestamp(cue['end'])},Lyrics,,0,0,0,,{cue['text']}\n"
                    for cue in track['cues']), encoding='utf-8')
        subtitle_filter = subtitles.relative_to(ROOT).as_posix()
        subprocess.run(['ffmpeg', '-y', '-hide_banner', '-loglevel', 'warning', '-stream_loop', '-1',
            '-f', 'concat', '-safe', '0', '-i', str(sequence), '-i', str(ROOT / track['audio'][6:]),
            '-t', str(track['duration']), '-map', '0:v:0', '-map', '1:a:0',
            '-vf', f"scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1,ass='{subtitle_filter}':fontsdir=assets/fonts",
            '-c:v', 'libx264', '-preset', 'fast', '-crf', '20', '-pix_fmt', 'yuv420p',
            '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart',
            str(manifest_path.parent / f'{character}_lyric_pv.mp4')], cwd=ROOT, check=True)
        print(f"PV: {character}, {track['duration']:.1f}s, {len(selected)} source clips", flush=True)


def transcribe(tracks):
    from faster_whisper import WhisperModel
    model = WhisperModel('small', device='cpu', compute_type='int8', cpu_threads=6,
                         download_root=str(QA / 'models'))
    for track in tracks:
        if not track['lyrics']:
            continue
        target = QA / f"{track['id']}_words.json"
        if target.exists():
            continue
        prompt = re.sub(r'\[[^]]*\]', '', track['lyrics'])[:450]
        segments, info = model.transcribe(str(ROOT / track['audio'][6:]), language='ja',
            word_timestamps=True, beam_size=5, condition_on_previous_text=False,
            initial_prompt=prompt, vad_filter=False)
        result = []
        for segment in segments:
            result.append(dict(start=segment.start, end=segment.end, text=segment.text,
                words=[dict(start=word.start, end=word.end, text=word.word, probability=word.probability)
                       for word in segment.words]))
            print(track['character'], round(segment.end, 1), flush=True)
        target.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    intro = QA / 'opening_intro.json'
    if not intro.exists():
        segments, _ = model.transcribe(str(ROOT / 'audio/bgm_opening.mp3'), language='ja',
            word_timestamps=True, clip_timestamps='0,38', no_speech_threshold=None,
            condition_on_previous_text=False, beam_size=5)
        result = [dict(start=s.start, end=s.end, text=s.text,
            words=[dict(start=w.start, end=w.end, text=w.word, probability=w.probability) for w in s.words]) for s in segments]
        intro.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')


def align_lyrics(tracks):
    from pykakasi import kakasi
    converter = kakasi()

    def reading(text):
        return ''.join(c for part in converter.convert(text) for c in part['hira'].lower() if c.isalnum() or c == 'ー')

    reports = []
    for track in tracks:
        if not track['lyrics']:
            continue
        segments = json.loads((QA / f"{track['id']}_words.json").read_text(encoding='utf-8'))
        if track['character'] == 'opening':
            intro = json.loads((QA / 'opening_intro.json').read_text(encoding='utf-8'))
            segments = intro + [s for s in segments if s['start'] >= 38]
        lines = [line.strip() for line in track['lyrics'].splitlines() if line.strip() and not line.startswith('[')]
        targets = [reading(line) for line in lines]
        expected = ''.join(targets)
        heard, times = '', []
        for segment in segments:
            raw, raw_times = '', []
            for word in segment['words']:
                raw += word['text']
                raw_times.extend([(word['start'], word['end'])] * len(word['text']))
            offset = 0
            for part in converter.convert(raw):
                text = reading(part['hira'])
                end = offset + len(part['orig'])
                start_time, end_time = raw_times[offset][0], raw_times[end - 1][1]
                heard += text
                times.extend([(start_time + (end_time - start_time) * i / len(text),
                               start_time + (end_time - start_time) * (i + 1) / len(text)) for i in range(len(text))])
                offset = end
        width = len(heard) + 1
        directions = [bytearray(width) for _ in range(len(expected) + 1)]
        previous = list(range(width))
        for i, char in enumerate(expected, 1):
            row = [i] + [0] * len(heard)
            for j, other in enumerate(heard, 1):
                costs = (previous[j - 1] + (char != other), previous[j] + 1, row[j - 1] + 1)
                direction = min(range(3), key=costs.__getitem__)
                row[j] = costs[direction]
                directions[i][j] = direction
            previous = row
        matches, aligned_pairs = {}, {}
        i, j = len(expected), len(heard)
        while i and j:
            direction = directions[i][j]
            if direction == 0:
                aligned_pairs[i - 1] = j - 1
                if expected[i - 1] == heard[j - 1]:
                    matches[i - 1] = j - 1
                i -= 1
                j -= 1
            elif direction == 1:
                i -= 1
            else:
                j -= 1
        cues, details, offset = [], [], 0
        for line, target in zip(lines, targets):
            aligned = [aligned_pairs[p] for p in range(offset, offset + len(target)) if p in aligned_pairs]
            coverage = sum(p in matches for p in range(offset, offset + len(target))) / len(target)
            if not aligned:
                raise ValueError(f"Unaligned lyric: {track['character']}: {line}")
            start = max(0, times[aligned[0]][0] - .12)
            end = min(track['duration'], times[aligned[-1]][1] + .35)
            cues.append(dict(start=round(start, 3), end=round(end, 3), text=line))
            details.append(dict(line=line, coverage=round(coverage, 3), start=round(start, 3), end=round(end, 3)))
            offset += len(target)
        # First-word timestamps included the instrumental lead-in; bounded re-transcription resolves these onsets.
        first_onsets = {'89bb719a-4405-44d6-beda-7c0e45ce54d8': 10.92,
                        'bf2d12f7-d616-4e57-9558-c83966577950': 1.56,
                        'ca698606-e56e-4ba6-bc62-a1d867ce89d2': 8.56,
                        'f973e5ff-b068-4b96-8ede-b8b1f018c680': 8.54}
        if track['id'] in first_onsets:
            cues[0]['start'] = first_onsets[track['id']]
            details[0]['start'] = cues[0]['start']
        for cue, next_cue in zip(cues, cues[1:]):
            cue['end'] = min(cue['end'], next_cue['start'])
        track['cues'] = cues
        reports.append(dict(character=track['character'], matched=round(len(matches) / len(expected), 3), lines=details))
    (QA / 'lyric_alignment.json').write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--transcribe', action='store_true')
    parser.add_argument('--align', action='store_true')
    parser.add_argument('--build-pv', type=Path)
    options = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    QA.mkdir(parents=True, exist_ok=True)
    tracks = make_library()
    print(f'Library: {len(tracks)} full-length Suno tracks', flush=True)
    if options.transcribe:
        transcribe(tracks)
    if options.align:
        align_lyrics(tracks)
    elif (OUT / 'library.json').exists():
        previous = {track['id']: track for track in json.loads((OUT / 'library.json').read_text(encoding='utf-8'))}
        videos = {track['character']: track['video'] for track in previous.values() if track.get('video')}
        for track in tracks:
            saved = previous.get(track['id'], {})
            track['cues'] = saved.get('cues', [])
            track['video'] = saved.get('video', videos.get(track['character'], track['video']))
    if options.build_pv:
        build_pvs(tracks, options.build_pv.resolve())
    (OUT / 'library.json').write_text(json.dumps(tracks, ensure_ascii=False, indent=2), encoding='utf-8')
