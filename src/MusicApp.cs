using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class MusicApp : Node2D
{
    public const string ScenePath = "res://MusicApp.tscn";
    private static readonly Color Accent = new("e9a7c9"), Surface = new("171c2a");
    private readonly Dictionary<string, Texture2D> _textures = new();
    private MusicLibrary.Track[] _tracks = null!;
    private int[] _visible = null!;
    private AudioStreamPlayer _player = null!;
    private VideoStreamPlayer _video = null!;
    private int _selected, _current = -1, _filter, _scroll;
    private bool _paused, _ended, _repeat, _fullscreen, _leaving, _armed;
    private readonly Dictionary<Key, bool> _held = new();
    private bool _acceptHeld = true, _backHeld = true, _fullHeld = true, _repeatHeld = true, _previousHeld, _nextHeld;
    internal MusicLibrary.Track[] Tracks => _tracks;
    internal int CurrentIndex => _current;
    internal bool IsPaused => _paused;
    internal bool IsEnded => _ended;
    internal bool IsFullscreen => _fullscreen;
    internal double PlaybackTime => _current < 0 ? 0 : _ended ? Current.Duration : _player.GetPlaybackPosition();
    private MusicLibrary.Track Current => _tracks[_current >= 0 ? _current : _selected];
    private double VideoTime(double seconds) => Current.IsVocal
        ? Math.Min(seconds, _video.GetStreamLength() - 1.0 / 30)
        : seconds % _video.GetStreamLength();
    private Rect2 Picture => _fullscreen ? new(0, 0, 1280, 720) : new(382, 126, 842, 474);
    private Rect2 SeekRect => _fullscreen ? new(80, 100, 1120, 24) : new(414, 604, 778, 24);
    private static Rect2 Row(int row) => new(32, 160 + row * 66, 326, 60);
    private static Rect2 Filter(int index) => new(32 + index * 109, 111, 103, 34);
    private Rect2 Button(int index) => _fullscreen ? new(396 + index * 105, 134, 96, 42) : new(579 + index * 105, 644, 96, 42);
    private static Rect2 BackRect => new(32, 29, 94, 42);
    private static Rect2 FullRect => new(1082, 29, 142, 42);

    public override void _Ready()
    {
        _tracks = MusicLibrary.Load();
        if (!GetNode<GameManager>("/root/Game").FinalBossRevealed)
            foreach (var track in _tracks)
                if (track.Character == "mina" && track.Category is "instrumental" or "remix" or "stage")
                    track.Title = track.Title.Split('—')[0].Trim();
        SetFilter(0);
        Pad.ReleaseMouseFollow();
        Audio.Instance?.StopMusic(0);
        Audio.Instance?.StopVoice();
        _player = new AudioStreamPlayer { Name = "FullTrack", Bus = "Music" };
        AddChild(_player);
        _player.Finished += OnFinished;
        _video = new VideoStreamPlayer { Name = "CharacterPV", Visible = false, Volume = 0, Loop = true };
        AddChild(_video);
    }

    private void SetFilter(int filter)
    {
        _filter = filter;
        _visible = Enumerable.Range(0, _tracks.Length).Where(i => filter == 0 || _tracks[i].IsVocal == (filter == 1)).ToArray();
        if (!_visible.Contains(_selected)) _selected = _visible[0];
        _scroll = 0;
        RevealSelection();
    }

    private Texture2D Texture(string path)
    {
        if (!_textures.TryGetValue(path, out var texture)) _textures[path] = texture = GD.Load<Texture2D>(path);
        return texture;
    }

    internal void PlayTrack(int index)
    {
        _player.Stop();
        _video.Stop();
        _current = _selected = index;
        var stream = (AudioStreamMP3)GD.Load<AudioStreamMP3>(_tracks[index].Audio).Duplicate();
        stream.Loop = false;
        _player.Stream = stream;
        _player.PitchScale = 1;
        _paused = _ended = false;
        _player.StreamPaused = false;
        _video.Stream = GD.Load<VideoStream>(Current.Video);
        _video.Loop = !Current.IsVocal;
        _video.Paused = false;
        _video.Play();
        _player.Play();
        RevealSelection();
    }

    internal void TogglePlayback()
    {
        if (_current < 0 || _ended) { PlayTrack(_selected); return; }
        _paused = !_paused;
        _player.StreamPaused = _paused;
        _video.Paused = _paused;
    }

    internal void Seek(double seconds)
    {
        if (_current < 0) PlayTrack(_selected);
        seconds = Math.Clamp(seconds, 0, Current.Duration);
        if (seconds >= Current.Duration) { _player.Stop(); _video.Paused = true; _ended = true; return; }
        if (_ended) _player.Play((float)seconds);
        else _player.Seek((float)seconds);
        _ended = false;
        _player.StreamPaused = _paused;
        _video.StreamPosition = VideoTime(seconds);
        _video.Paused = _paused;
    }

    private void OnFinished()
    {
        if (_repeat) PlayTrack(_current);
        else { _ended = true; _video.Paused = true; }
    }

    private void Next(int direction)
    {
        int at = Array.IndexOf(_visible, _current >= 0 ? _current : _selected);
        if (at < 0) { PlayTrack(_selected); return; }
        PlayTrack(_visible[(at + direction + _visible.Length) % _visible.Length]);
    }

    private void RevealSelection()
    {
        int at = Array.IndexOf(_visible, _selected);
        if (at >= 0) _scroll = Math.Clamp(at < _scroll ? at : at >= _scroll + 7 ? at - 6 : _scroll, 0, Math.Max(0, _visible.Length - 7));
    }

    private bool Pressed(Key key)
    {
        bool down = Input.IsPhysicalKeyPressed(key);
        bool edge = down && !_held.GetValueOrDefault(key);
        _held[key] = down;
        return edge;
    }

    private void Back()
    {
        if (_fullscreen) { _fullscreen = false; return; }
        _leaving = true;
        _player.Stop();
        _video.Stop();
        Audio.Instance?.PlayUiCancel();
        GameManager.FadeToScene(this, "res://Hub.tscn");
    }

    public override void _Input(InputEvent input)
    {
        if (_fullscreen || Pad.UiBlocked(this) || input is not InputEventMouseButton { Pressed: true } mouse) return;
        if (mouse.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelUp && Pad.MousePos().X < 365)
            _scroll = Math.Clamp(_scroll + (mouse.ButtonIndex == MouseButton.WheelDown ? 1 : -1), 0, Math.Max(0, _visible.Length - 7));
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
        if (_leaving) return;
        bool blocked = Pad.UiBlocked(this);
        _player.StreamPaused = _paused || blocked;
        _video.Paused = _paused || blocked || _ended;
        if (_current >= 0 && !_video.Paused)
        {
            double position = VideoTime(PlaybackTime);
            if (Math.Abs(_video.StreamPosition - position) > .15) _video.StreamPosition = position;
        }
        _player.VolumeDb = Audio.Instance?.Muted == true ? -80 : -10;
        bool accept = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Pad.Pressed(JoyButton.A);
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool full = Input.IsPhysicalKeyPressed(Key.F) || Pad.Pressed(JoyButton.Y);
        bool repeat = Input.IsPhysicalKeyPressed(Key.R) || Pad.Pressed(JoyButton.X);
        bool previous = Input.IsPhysicalKeyPressed(Key.Pageup) || Pad.Pressed(JoyButton.LeftShoulder);
        bool next = Input.IsPhysicalKeyPressed(Key.Pagedown) || Pad.Pressed(JoyButton.RightShoulder);
        if (blocked || !_armed)
        {
            _acceptHeld = accept; _backHeld = back; _fullHeld = full; _repeatHeld = repeat;
            _previousHeld = previous; _nextHeld = next;
            _armed = !blocked && !accept && !back;
            return;
        }
        bool confirm = accept && !_acceptHeld, cancel = back && !_backHeld;
        if (full && !_fullHeld) _fullscreen = !_fullscreen;
        if (repeat && !_repeatHeld) _repeat = !_repeat;
        _acceptHeld = accept; _backHeld = back; _fullHeld = full; _repeatHeld = repeat;
        if (cancel || Pad.MouseRightClick()) { Back(); return; }
        if (Pressed(Key.Tab) && !_fullscreen) SetFilter((_filter + 1) % 3);
        if (Pressed(Key.Space)) TogglePlayback();
        if (Pressed(Key.Left) || Input.IsActionJustPressed("ui_left")) Seek(PlaybackTime - 10);
        if (Pressed(Key.Right) || Input.IsActionJustPressed("ui_right")) Seek(PlaybackTime + 10);
        if (previous && !_previousHeld) Next(-1);
        if (next && !_nextHeld) Next(1);
        _previousHeld = previous; _nextHeld = next;
        int navigation = Pressed(Key.Up) || Input.IsActionJustPressed("ui_up") ? -1
            : Pressed(Key.Down) || Input.IsActionJustPressed("ui_down") ? 1 : 0;
        if (navigation != 0 && !_fullscreen)
        {
            _selected = _visible[(Array.IndexOf(_visible, _selected) + navigation + _visible.Length) % _visible.Length];
            RevealSelection();
            Audio.Instance?.PlayUiMove();
        }
        UiKit.BeginHotspots(Pad.MousePos());
        UiKit.Hotspot(BackRect, 200);
        UiKit.Hotspot(FullRect, 201);
        UiKit.Hotspot(SeekRect, 202);
        for (int i = 0; i < 4; i++) UiKit.Hotspot(Button(i), 210 + i);
        if (!_fullscreen)
        {
            for (int i = 0; i < 3; i++) UiKit.Hotspot(Filter(i), 220 + i);
            for (int row = 0; row < 7 && _scroll + row < _visible.Length; row++) UiKit.Hotspot(Row(row), _visible[_scroll + row]);
        }
        int clicked = UiKit.ClickedId(Pad.MouseClick());
        if (clicked == 200) { Back(); return; }
        if (clicked == 201) _fullscreen = !_fullscreen;
        if (clicked == 202) Seek(Math.Clamp((Pad.MousePos().X - SeekRect.Position.X) / SeekRect.Size.X, 0, 1) * Current.Duration);
        if (clicked == 210) Next(-1);
        if (clicked == 211) TogglePlayback();
        if (clicked == 212) Next(1);
        if (clicked == 213) _repeat = !_repeat;
        if (clicked >= 220 && clicked <= 222) SetFilter(clicked - 220);
        if (clicked >= 0 && clicked < _tracks.Length) { if (_current == clicked) TogglePlayback(); else PlayTrack(clicked); }
        else if (confirm) { if (_fullscreen || _current == _selected) TogglePlayback(); else PlayTrack(_selected); }
    }

    private void Label(Rect2 rect, string text, bool active = false)
    {
        UiKit.Box(this, rect, active ? new Color("51394c") : Surface, 12,
            active || rect.HasPoint(Pad.MousePos()) ? Accent : new Color("31394b"), 1);
        UiKit.Text(this, UiKit.Zen, rect.Position + new Vector2(0, 9), text, 16,
            active ? Accent : UiKit.White, HorizontalAlignment.Center, rect.Size.X);
    }

    private void Art(Texture2D texture, Rect2 rect)
    {
        Vector2 size = texture.GetSize();
        float ratio = Math.Max(rect.Size.X / size.X, rect.Size.Y / size.Y);
        Vector2 crop = rect.Size / ratio;
        DrawTextureRectRegion(texture, rect, new Rect2((size - crop) * new Vector2(.5f, .45f), crop));
    }

    private static string Clock(double seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    private void FitText(Vector2 at, string text, int size, float width, Color color)
    {
        while (size > 12 && UiKit.Zen.GetStringSize(text, fontSize: size).X > width) size--;
        UiKit.Text(this, UiKit.Zen, at, text, size, color);
    }

    public override void _Draw()
    {
        if (_tracks == null) return;
        UiKit.BeginDesign(this);
        DrawRect(new Rect2(0, 0, 1280, 720), new Color("0c111d"));
        var track = Current;
        double time = _current < 0 ? 0 : PlaybackTime;
        var picture = Picture;
        Art(_current < 0 ? Texture(track.Cover) : _video.GetVideoTexture(), picture);
        if (_fullscreen)
        {
            UiKit.VGradient(this, new Rect2(0, 0, 1280, 210), new[] { new Color(0, 0, 0, .9f), Colors.Transparent }, new[] { 0f, 1f });
        }
        else
        {
            UiKit.Text(this, UiKit.ZenBlack, new Vector2(148, 24), "ミュージック", 31, UiKit.White);
            UiKit.Text(this, UiKit.Mono, new Vector2(150, 67), $"REFRAIN / { _tracks.Length } TRACKS", 12, Accent);
            for (int i = 0; i < 3; i++) Label(Filter(i), new[] { "すべて", "歌詞PV", "BGM" }[i], i == _filter);
            for (int row = 0; row < 7 && row + _scroll < _visible.Length; row++)
            {
                int index = _visible[row + _scroll];
                var item = _tracks[index]; var rect = Row(row);
                bool selected = index == _selected;
                UiKit.Box(this, rect, selected ? new Color("2b2638") : Surface, 12,
                    index == _current ? Accent : selected ? new Color("68576f") : Colors.Transparent, 1);
                Art(Texture(item.Cover), new Rect2(rect.Position + new Vector2(8, 8), new Vector2(44, 44)));
                string title = item.Title.Split('—')[0].Trim();
                FitText(rect.Position + new Vector2(63, 8), title, 17, 246, UiKit.White);
                FitText(rect.Position + new Vector2(63, 34), $"{item.Artist}  ·  {item.Edition}", 12, 200, UiKit.Text2);
                UiKit.Text(this, UiKit.Mono, rect.Position + new Vector2(275, 35), Clock(item.Duration), 12, UiKit.Text2);
            }
            UiKit.Text(this, UiKit.Zen, new Vector2(36, 633), $"{Math.Min(_scroll + 1, _visible.Length)}–{Math.Min(_scroll + 7, _visible.Length)} / {_visible.Length}曲", 13, UiKit.Text3);
            UiKit.Text(this, UiKit.Zen, new Vector2(36, 672), "↑↓ 選曲  Z 再生  Tab 絞込", 13, UiKit.Text2);
        }
        Label(BackRect, _fullscreen ? "縮小" : "ホーム");
        Label(FullRect, _fullscreen ? "F  縮小表示" : "F  全画面");
        FitText(new Vector2(_fullscreen ? 154 : 384, _fullscreen ? 31 : 88), track.Title,
            _fullscreen ? 24 : 20, _fullscreen ? 880 : 836, UiKit.White);
        float progress = (float)Math.Clamp(time / track.Duration, 0, 1);
        var seek = SeekRect;
        UiKit.Box(this, new Rect2(seek.Position + new Vector2(0, 10), new Vector2(seek.Size.X, 4)), new Color("414758"), 2);
        if (progress > 0) UiKit.Box(this, new Rect2(seek.Position + new Vector2(0, 10), new Vector2(seek.Size.X * progress, 4)), Accent, 2);
        DrawCircle(new Vector2(seek.Position.X + seek.Size.X * progress, seek.Position.Y + 12), 5, UiKit.White);
        UiKit.Text(this, UiKit.Mono, new Vector2(seek.Position.X, seek.End.Y + 16), Clock(time), 15, UiKit.Text2);
        UiKit.Text(this, UiKit.Mono, new Vector2(seek.End.X - 55, seek.End.Y + 16), Clock(track.Duration), 15, UiKit.Text2);
        Label(Button(0), "前の曲"); Label(Button(1), _current >= 0 && !_paused && !_ended ? "一時停止" : "再生", true);
        Label(Button(2), "次の曲"); Label(Button(3), "リピート", _repeat);
        UiKit.EndDesign(this);
    }

    public override void _ExitTree()
    {
        _player.Stop();
        _player.Stream = null;
        _video.Stop();
        _video.Stream = null;
    }
}
