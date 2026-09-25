using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

// MemoryTempoQa : ボス戦の途中（HP 閾値）に挟む回想（memory）の**尺**を実測して表にする。
//   2026-09-26 作者指摘「まだ戦っている最中なのに長すぎる。テンポが悪い」の根拠と、詰めたあとの回帰確認。
//   対象 13 本＝ミナ潜行 3本（Akari/Koharu/ReiStoryFilm の memory）＋FINAL（MinaStoryFilm の memory）
//   ＋他ジョブ潜行 9本（CharacterStory.Memory＝CharacterStoryTalk の吹き出し）。
//   ボスを経由せず（HP を削らず）回想だけを直接起こし、3通りの送り方で完走までの秒を測る
//   （秒＝ゲーム時間。_Process の delta 積算なので --speed で早回ししても値は変わらない）:
//     auto  … 自動送り（AutoAdvanceDialog=true・文字送り 48cps・無入力）＝自動で読ませたときの尺
//     brisk … ページが出切ってから 0.35 秒で Z を押す＝読みながら速く送る手動プレイ
//     ff    … 初見のまま最初から Ctrl 押しっぱなし＝早送り。効かなければ timeout（FfCap 秒）
//   起動: qa_memory_tempo.tscn -- [--only akari|koharu|rei] [--speed N] [--survey]
//     --only   … その面だけ（mina の FINAL 回想は akari に同居）。既定は 3 面すべて
//     --speed  … Engine.TimeScale（既定 1）。早回し用
//     --survey … 計測だけして合否を出さない（詰める前の現状記録用）
//   合否（--survey 無し）: 13 本すべて ff が FfBudget 秒以内に完走＝「押しっぱなしで早送り」が初見でも効く。
//   結果は [Tempo] ROW 行（面／操作キャラ／種別／行数／auto／brisk／ff）。
public partial class MemoryTempoQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const double FfCap = 40;        // ff が効いていないときに諦める秒（ゲーム時間）
    private const double FfBudget = 15;     // ff の合格ライン（秒）
    private const double BriskReact = 0.35; // brisk の反応時間（ページが出切ってから押すまで）

    private static T Read<T>(object obj, string field, Type? type = null)
        => (T)(type ?? obj.GetType()).GetField(field, Private)!.GetValue(obj)!;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print($"[Tempo] PASS {message}");
    }

    private GameManager _game = null!;
    private Hud? _hud;
    private double _clock;          // 計測中のゲーム時間
    private bool _ticking;
    private CharacterStoryTalk? _talk;
    private bool _brisk, _zDown;
    private double _briskT;
    private Func<int>? _lineOf;     // いま表示中の行番号（per-line ログ用）
    private Func<int, string>? _textOf;
    private int _lastLine = -1;
    private double _lastLineAt;
    private bool _perLine;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _ = Run();
    }

    private async Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string only = "";
        double speed = 1;
        bool survey = Array.IndexOf(args, "--survey") >= 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--only" && i + 1 < args.Length) only = args[i + 1];
            if (args[i] == "--speed" && i + 1 < args.Length) speed = double.Parse(args[i + 1]);
        }
        var rows = new List<string>();
        bool allFf = true;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated user data");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            Engine.TimeScale = speed;
            _game = GetNode<GameManager>("/root/Game");
            _game.AutoSaveEnabled = false;
            _game.MsgCharsPerSec = 48;   // 実プレイの既定（GameManager）と同じ
            _game.AutoAdvanceDialog = false;
            _game.SelectedEntry = GameManager.StageEntry.Boss;
            foreach (string stageName in new[] { "Akari", "Koharu", "Rei" })
            {
                string stageId = stageName.ToLowerInvariant();
                if (only.Length > 0 && only != stageId) continue;
                _game.SelectedJob = Job.Tank;
                var root = GD.Load<PackedScene>($"res://{stageName}.tscn").Instantiate<Node2D>();
                await Frames(1);
                GetTree().Root.AddChild(root);
                GetTree().CurrentScene = root;
                var hud = root.GetNode<Hud>("Hud");
                var world = root.GetNode<Node2D>("World");
                var stage = root.GetNode<Node>($"Stage{stageName}");
                var player = world.GetNode<Player>("Player");
                root.SetProcess(false);
                stage.SetProcess(false);
                player.SetPhysicsProcess(false);
                _hud = hud;
                await Frames(5);
                hud.HideBubble();
                Hud.ClearBacklog();

                // ── ミナ潜行＝この面のボスのフィルム（＋FINAL のミナ回想は STAGE1 の枠で測る）──
                var films = new List<(string label, string job, Action<Action> play)>
                {
                    ($"{stageId}_memory (film)", "mina", done =>
                    {
                        if (stageName == "Akari") AkariStoryFilm.Play(hud, world, false, done);
                        else if (stageName == "Koharu") KoharuStoryFilm.Play(hud, world, false, done);
                        else ReiStoryFilm.Play(hud, world, false, done);
                    }),
                };
                if (stageName == "Akari")
                    films.Add(("mina_memory (film, FINAL)", "mina", done => MinaStoryFilm.Play(hud, world, false, done)));
                foreach (var (label, jobId, play) in films)
                {
                    var r = new Result { Stage = stageId, Job = jobId, Label = label };
                    foreach (string mode in new[] { "ff", "brisk", "auto" })
                    {
                        _game.SelectedJob = Job.Tank;
                        r.Set(mode, await Measure(mode, done =>
                        {
                            play(done);
                            var film = (StoryFilm)GetTree().GetFirstNodeInGroup("storyfilm");
                            var lines = (Array)typeof(StoryFilm).GetField("_lines", Private)!.GetValue(film)!;
                            var textProp = lines.GetType().GetElementType()!.GetProperty("Text")!;
                            _lineOf = () => IsInstanceValid(film) ? Read<int>(film, "_line", typeof(StoryFilm)) : -1;
                            _textOf = i => (string)textProp.GetValue(lines.GetValue(i))!;
                            return lines.Length;
                        }, r));
                        await Frames(20);
                    }
                    allFf &= r.FfOk;
                    rows.Add(r.Row());
                }

                // ── 他ジョブ潜行＝吹き出しだけの回想（CharacterStoryTalk）──
                string bossName = stageName == "Akari" ? "あかり" : stageName == "Koharu" ? "こはる" : "レイ";
                string bossFace = stageName == "Akari" ? "res://char/v3/akari_face.png"
                    : stageName == "Koharu" ? "res://char/v3/koharu_face.png" : CompanionDialogue.ReiAvatarPortrait;
                foreach (var job in new[] { Job.Melee, Job.Heal, Job.Magic })
                {
                    string id = Jobs.Get(job).CharacterId;
                    var r = new Result { Stage = stageId, Job = id, Label = $"talk {id}x{stageId}" };
                    foreach (string mode in new[] { "ff", "brisk", "auto" })
                    {
                        _game.SelectedJob = job;
                        r.Set(mode, await Measure(mode, done =>
                        {
                            var lines = CharacterStory.Memory(job, stageId);
                            // 各 Boss*.ShowStoryLine と同じ引き当て（Other の face 空欄＝このボスの既定顔）。
                            _talk = CharacterStoryTalk.Start(lines, () => _hud, (h, who, text, face) =>
                            {
                                var kind = (Hud.LineKind)who;
                                string portrait = kind == Hud.LineKind.Other && string.IsNullOrEmpty(face) ? bossFace : face;
                                h.ShowDialog(kind, text, portrait, otherName: bossName);
                            }, done);
                            _lineOf = () => _talk is { Active: true } ? Read<int>(_talk, "_line") : -1;
                            _textOf = i => lines[i].text;
                            return lines.Length;
                        }, r));
                        await Frames(20);
                    }
                    allFf &= r.FfOk;
                    rows.Add(r.Row());
                }
                _hud = null;
                root.QueueFree();
                await Frames(8);
            }
            Engine.TimeScale = 1;
            GD.Print("[Tempo] TABLE | stage | job | kind | lines | auto | brisk | ff |");
            foreach (var row in rows) GD.Print(row);
            if (!survey) Check(allFf, $"every memory fast-forwards to completion within {FfBudget}s when Ctrl is held from the start");
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
                if (child is AudioStreamPlayer audio) { audio.Stop(); audio.Stream = null; }
            await Frames(5);
            GD.Print(survey ? "[Tempo] SURVEY DONE" : "[Tempo] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            Engine.TimeScale = 1;
            foreach (var row in rows) GD.Print(row);
            GD.PushError($"[Tempo] FAIL {ex}");
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private sealed class Result
    {
        public string Stage = "", Job = "", Label = "";
        public int Lines;
        public double Auto, Brisk, Ff;
        public bool FfOk;
        public void Set(string mode, (double sec, int lines, bool ok) m)
        {
            Lines = m.lines;
            if (mode == "auto") Auto = m.sec;
            else if (mode == "brisk") Brisk = m.sec;
            else { Ff = m.sec; FfOk = m.ok && m.sec <= FfBudget; }
        }
        public string Row() => $"[Tempo] ROW | {Stage} | {Job} | {Label} | {Lines} | {Auto:0.0}s | {Brisk:0.0}s | "
                               + (FfOk ? $"{Ff:0.0}s" : Ff >= FfCap ? "timeout" : $"{Ff:0.0}s (over budget)") + " |";
    }

    // 回想を1本、指定の送り方で完走させて秒を返す。ff が効かない（FfCap 秒経過）ときは Z 連打で畳んで ok=false。
    private async Task<(double sec, int lines, bool ok)> Measure(string mode, Func<Action, int> start, Result r)
    {
        Read<HashSet<string>>(_game, "_readLines").Clear();   // 毎回「初見」から測る（ff の既読条件を外す）
        bool done = false;
        _game.AutoAdvanceDialog = mode == "auto";
        _perLine = mode == "auto";
        _lastLine = -1;
        _clock = 0;
        _ticking = true;
        if (mode == "ff") KeyEvent(Key.Ctrl, true);
        int lines = start(() => done = true);
        _brisk = mode == "brisk";
        GD.Print($"[Tempo] start {r.Label} mode={mode} lines={lines}");
        bool ok = true;
        while (!done)
        {
            await Frames(1);
            if (mode == "ff" && _clock >= FfCap && !_brisk)
            {
                ok = false;
                KeyEvent(Key.Ctrl, false);
                GD.Print($"[Tempo]   ff had no effect after {FfCap}s (line {_lineOf?.Invoke()}), finishing with Z");
                _brisk = true;   // 早送りが効かないので手動送りで畳む（次の計測へ進むため）
            }
        }
        double sec = _clock;
        _ticking = false;
        _brisk = false;
        if (mode == "ff") KeyEvent(Key.Ctrl, false);
        if (_zDown) { KeyEvent(Key.Z, false); _zDown = false; }
        _game.AutoAdvanceDialog = false;
        _talk = null;
        _lineOf = null; _textOf = null;
        GD.Print($"[Tempo]   {r.Label} mode={mode} -> {sec:0.00}s{(ok ? "" : " (timeout)")}");
        return (sec, lines, ok);
    }

    public override void _Process(double delta)
    {
        if (_ticking) _clock += delta;
        // 他ジョブ潜行の回想は呼び元（Boss*._Process）が毎フレーム Update を叩く契約＝ここで代行する。
        _talk?.Update(delta);
        if (_perLine && _lineOf != null && _textOf != null)
        {
            int line = _lineOf();
            if (line != _lastLine)
            {
                if (_lastLine >= 0)
                    GD.Print($"[Tempo]   line {_lastLine + 1:00} {_textOf(_lastLine).Length,3}ch {_clock - _lastLineAt:0.00}s");
                _lastLine = line;
                _lastLineAt = _clock;
            }
        }
        if (!_brisk || _hud == null) return;
        if (_zDown) { KeyEvent(Key.Z, false); _zDown = false; return; }
        // ページが出切ってから BriskReact 秒で押す（時制見出しの一拍中も、前の行が出たままなら押す）。
        string page = (string)typeof(Hud).GetProperty("CurPageText", Private)!.GetValue(_hud)!;
        bool pageDone = Read<string>(_hud, "_dlgText").Length > 0 && Read<float>(_hud, "_dlgRevealed") >= page.Length;
        bool filmFading = GetTree().GetFirstNodeInGroup("storyfilm") is StoryFilm f
                          && !Read<bool>(f, "_started", typeof(StoryFilm));
        _briskT = pageDone ? _briskT + delta : 0;
        if (pageDone && !filmFading && _briskT >= BriskReact) { KeyEvent(Key.Z, true); _zDown = true; _briskT = 0; }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void KeyEvent(Key key, bool pressed)
        => Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = pressed });
}
