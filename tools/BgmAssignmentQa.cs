using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// BgmAssignmentQa : 「どの場面でどの曲が鳴るか」の自動検証（2026-09-29）。
//   耳で確かめられないもの（音色・良し悪し）は見ない。**配線として正しいか**だけを機械的に見る:
//     ① audio/bgm_*.ogg が実在し、Audio の各スロットが**合成フォールバックに落ちていない**
//        （インポート漏れ・ファイル名の打ち間違いは、実行すると無警告で合成音に化けるので必ず検出する）
//     ② ループ設定（エンディング映像からロールまでの専用曲だけ Loop false）
//     ③ StageBgm() / StoryBgm() のマップが意図どおり（FINAL の "mina" を含む）
//     ④ 同じ主題を予定した場面で共有し、それ以外の曲を誤って流用していないか
//     ⑤ FINAL の道中（残響の三波）で曲が鳴っている（2026-09-27 に 7 秒→70 秒へ延びた区間）
//     ⑥ ルナティックでもボステーマが**こもったままにならない**
//        （投稿の割り込みを畳む難易度なので、札の数で開くローパスが開かずに固定される事故）
//
//   実行: APPDATA=<repo>/build/qa_story/bgm_appdata
//         godot --headless --path <repo> res://tools/qa_bgm_assignment.tscn
//   ※ --qa は付けないこと（Audio.Muted=true になって Music() が何もしなくなり、判定が空振りする）。
public partial class BgmAssignmentQa : Node
{
    private const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo CurrentMusicField = typeof(Audio).GetField("_currentMusic", Priv)!;

    private int _fail;
    private Audio _audio = null!;

    // 合成のままで正しいスロット（設計意図。BGM/acquisition_list.md §1 の #12）。
    private static readonly string[] SynthSlots = { "BgmStage", "BgmBoss" };
    // ループしない曲（.import が loop=false）。
    private static readonly string[] OneShotSlots = { "BgmEndingFilm" };

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        await Frames(2);
        var self = GetTree().CurrentScene;
        if (self == this) GetTree().CurrentScene = null;

        _audio = GetNode<Audio>("/root/Audio");
        Check("Audio が常駐している", _audio != null, "オートロードが無い");
        Check("Audio.Muted=false（--qa を付けていない）", !_audio!.Muted, "Muted だと Music() が働かず判定できない");

        StaticChecks();
        await SceneChecks();

        // 終了前に畳み切る（QueueFree の実処理は次フレーム。待たずに Quit すると解放漏れの警告が出る）。
        //   Audio は常駐なので、クロスフェード用の2つのプレイヤーが最後に鳴らした ogg を掴んだままになる。
        //   合成BGMを2回続けて張って両方のプレイヤーを合成音に置き換えてから止める＝ogg の参照を手放す。
        Engine.TimeScale = 1;
        _audio.Music(_audio.BgmStage, 0);
        await Frames(4);
        _audio.Music(_audio.BgmBoss, 0);
        await Frames(4);
        _audio.StopMusic(0);
        await Frames(12);
        GD.Print(_fail == 0 ? "[bgmqa] ALL OK" : $"[bgmqa] FAILED: {_fail}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // ───────── ① 〜 ④ シーンを立てずに見られるもの ─────────
    private void StaticChecks()
    {
        var slots = typeof(Audio).GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(f => typeof(AudioStream).IsAssignableFrom(f.FieldType) && f.Name.StartsWith("Bgm"))
            .OrderBy(f => f.Name).ToArray();
        Check("BGM スロットを列挙できた", slots.Length >= 25, $"見つかったのは {slots.Length} 枠");

        var byPath = new Dictionary<string, List<string>>();
        foreach (var f in slots)
        {
            var stream = f.GetValue(_audio) as AudioStream;
            if (stream == null)
            {
                Check($"{f.Name}: 曲がある", false, "null（曲が読めていない）");
                continue;
            }

            if (SynthSlots.Contains(f.Name))
            {
                Check($"{f.Name}: 合成のまま（設計意図）", stream is AudioStreamWav,
                      $"実音源になっている: {stream.ResourcePath}");
                continue;
            }

            // 実音源スロット。合成（AudioStreamWav）に落ちていたら「ローダーのフォールバックが
            //   働いた＝ファイルが無い/インポートされていない」＝無警告で音が別物になっている。
            bool real = stream is AudioStreamOggVorbis || stream is AudioStreamMP3;
            Check($"{f.Name}: 実音源を読めている", real,
                  $"合成フォールバックに落ちている（型={stream.GetType().Name}）");
            if (!real) continue;

            string path = stream.ResourcePath;
            Check($"{f.Name}: ファイルが実在（{path}）", !string.IsNullOrEmpty(path) && ResourceLoader.Exists(path),
                  "ResourcePath が空か存在しない");

            if (stream is AudioStreamOggVorbis ogg)
            {
                bool wantLoop = !OneShotSlots.Contains(f.Name);
                Check($"{f.Name}: ループ設定 {(wantLoop ? "true" : "false")}", ogg.Loop == wantLoop,
                      $"Loop={ogg.Loop}");
            }

            if (!string.IsNullOrEmpty(path))
            {
                if (!byPath.TryGetValue(path, out var users)) byPath[path] = users = new List<string>();
                users.Add(f.Name);
            }
        }

        var sharedThemes = new Dictionary<string, string[]>
        {
            ["res://audio/bgm_menu_mina.ogg"] = new[] { "BgmMenu", "BgmShop" },
            ["res://audio/bgm_prologue.ogg"] = new[] { "BgmPrologue", "BgmFinalCutscene" },
            ["res://audio/bgm_final_resolve.ogg"] = new[] { "BgmFinalResolve", "BgmStoryAkariAfter", "BgmStoryKoharuAfter", "BgmStoryReiAfter" },
            ["res://audio/bgm_gameover.ogg"] = new[] { "BgmGameOver", "BgmStoryMina" },
            ["res://audio/bgm_stage_akari.ogg"] = new[] { "BgmStageAkari", "BgmTraining" },
        };
        foreach (var (path, users) in byPath.Where(kv => kv.Value.Count > 1))
            Check($"主題を意図した場面だけで共有する: {path}",
                  sharedThemes.TryGetValue(path, out var expected) && new HashSet<string>(users).SetEquals(expected),
                  $"使用枠={string.Join(" / ", users)}");
        foreach (var (path, expected) in sharedThemes)
            Check($"主題の再登場先がそろっている: {path}",
                  byPath.TryGetValue(path, out var users) && new HashSet<string>(users).SetEquals(expected),
                  $"予定枠={string.Join(" / ", expected)}");

        // ③ ステージ→道中曲のマップ。
        CheckSame("StageBgm(rei)", _audio.StageBgm("rei"), _audio.BgmStageRei);
        CheckSame("StageBgm(akari)", _audio.StageBgm("akari"), _audio.BgmStageAkari);
        CheckSame("StageBgm(koharu)", _audio.StageBgm("koharu"), _audio.BgmStageKoharu);
        Check("FINAL 道中は専用の戦闘曲を使う", _audio.BgmFinalRoute.ResourcePath == "res://audio/bgm_final_route.ogg",
              $"再生対象={_audio.BgmFinalRoute.ResourcePath}");
        CheckSame("StageBgm(tutorial)", _audio.StageBgm("tutorial"), _audio.BgmStageW0);
        CheckSame("StageBgm(mina)＝FINAL 道中", _audio.StageBgm("mina"), _audio.BgmFinalRoute);
        CheckSame("StageBgm(未知)＝合成へ", _audio.StageBgm("???"), _audio.BgmStage);

        // ③ 回想→曲のマップ（ミナの aftermath だけ意図的に無音）。
        CheckSame("StoryBgm(rei,memory)", _audio.StoryBgm("rei", false), _audio.BgmStoryRei);
        CheckSame("StoryBgm(rei,after)", _audio.StoryBgm("rei", true), _audio.BgmStoryReiAfter);
        CheckSame("StoryBgm(akari,memory)", _audio.StoryBgm("akari", false), _audio.BgmStoryAkari);
        CheckSame("StoryBgm(akari,after)", _audio.StoryBgm("akari", true), _audio.BgmStoryAkariAfter);
        CheckSame("StoryBgm(koharu,memory)", _audio.StoryBgm("koharu", false), _audio.BgmStoryKoharu);
        CheckSame("StoryBgm(koharu,after)", _audio.StoryBgm("koharu", true), _audio.BgmStoryKoharuAfter);
        CheckSame("StoryBgm(mina,memory)", _audio.StoryBgm("mina", false), _audio.BgmStoryMina);
        Check("StoryBgm(mina,after)＝意図的な無音", _audio.StoryBgm("mina", true) == null, "曲が入っている");
    }

    // ───────── ⑤ ⑥ 実際にシーンを立てて確かめるもの ─────────
    private async Task SceneChecks()
    {
        var game = GetNode<GameManager>("/root/Game");
        // ゲーム内時間を速める（タイトルカードやボス登場の待ちを実時間で縮める。挙動は変えない）。
        Engine.TimeScale = 8;
        typeof(QaPilot).GetProperty("GodActive")!.SetValue(null, true);   // 判定中に自機が落ちない

        // ⑤ FINAL の道中（残響の三波）で曲が鳴るか。
        //    ルナティックで入れば F1 導入の会話を踏まないので、タイトルカードの後すぐ道中（_step=2）に入る。
        {
            var (root, stage) = await OpenStage("res://MinaBattle.tscn", game,
                GameManager.Diff.Lunatic, GameManager.StageEntry.Start, skipMinaBanner: true);
            bool reached = await Until(() => ReadInt(stage, "_step") >= 2, 4000);
            Check("FINAL: 道中（_step=2）まで進む", reached, $"_step={ReadInt(stage, "_step")}");
            await Frames(20);
            var now = Now();
            Check("FINAL 道中に曲が鳴っている", now != null, "無音のまま（2026-09-27 に 70 秒へ延びた区間）");
            CheckSame("FINAL 道中の曲＝BgmFinalRoute", now, _audio.BgmFinalRoute);
            // 導入（_step=1）の沈黙は残っているか＝道中に入る前は無音だったことの裏取りはできないので、
            //   代わりに「道中で鳴り始めた曲が数十フレームで消えない（誰かに止められない）」を見る。
            await Frames(40);
            CheckSame("FINAL 道中の曲が途切れない", Now(), _audio.BgmFinalRoute);
            await CloseStage(root);
        }

        // ⑥ ルナティックのボステーマ：投稿の割り込みを畳んでもローパスが開き切っているか。
        //    こはる／レイ／ミナ＝BossPostSequence（PostMusicDepth）、あかり＝専用実装（AkariMusicDepth）。
        {
            var (root, stage) = await OpenStage("res://MinaBattle.tscn", game,
                GameManager.Diff.Lunatic, GameManager.StageEntry.Boss, skipMinaBanner: true);
            bool spawned = await Until(() => Now() == _audio.BgmBossMina, 4000);
            Check("FINAL: ミナ戦の曲に切り替わる", spawned, $"再生中={Describe(Now())}");
            Check("ルナティック: ミナ戦の曲が開き切っている（depth=5）", _audio.PostMusicDepth == 5,
                  $"depth={_audio.PostMusicDepth}（0 のままだと 2400Hz のローパスでこもり続ける）");
            await CloseStage(root);
        }
        {
            var (root, stage) = await OpenStage("res://MinaBattle.tscn", game,
                GameManager.Diff.Normal, GameManager.StageEntry.Boss, skipMinaBanner: true);
            await Until(() => Now() == _audio.BgmBossMina, 4000);
            Check("通常難易度: ミナ戦は札 0 枚＝こもりから始まる（depth=0）", _audio.PostMusicDepth == 0,
                  $"depth={_audio.PostMusicDepth}");
            await CloseStage(root);
        }
        {
            var (root, stage) = await OpenStage("res://Akari.tscn", game,
                GameManager.Diff.Lunatic, GameManager.StageEntry.Boss, skipMinaBanner: false);
            bool spawned = await Until(() => Now() == _audio.BgmBossAkari, 4000);
            Check("あかり: ボス曲に切り替わる", spawned, $"再生中={Describe(Now())}");
            Check("ルナティック: あかり戦の曲が開き切っている（depth=5）", _audio.AkariMusicDepth == 5,
                  $"depth={_audio.AkariMusicDepth}（0 のままだと 1800Hz のローパスでこもり続ける）");
            await CloseStage(root);
        }
        {
            var (root, stage) = await OpenStage("res://Akari.tscn", game,
                GameManager.Diff.Normal, GameManager.StageEntry.Boss, skipMinaBanner: false);
            await Until(() => Now() == _audio.BgmBossAkari, 4000);
            Check("通常難易度: あかり戦は札 0 枚＝こもりから始まる（depth=0）", _audio.AkariMusicDepth == 0,
                  $"depth={_audio.AkariMusicDepth}");
            await CloseStage(root);
        }

        foreach (var id in new[] { "koharu", "rei" })
        {
            foreach (var difficulty in new[] { GameManager.Diff.Normal, GameManager.Diff.Lunatic })
            {
                var expected = id == "koharu" ? _audio.BgmBossKoharu : _audio.BgmBossRei;
                string scene = id == "koharu" ? "res://Koharu.tscn" : "res://Rei.tscn";
                var (root, stage) = await OpenStage(scene, game, difficulty, GameManager.StageEntry.Boss, false);
                bool spawned = await Until(() => Now() == expected, 4000);
                Check($"{id}/{difficulty}: ボス曲に切り替わる", spawned, $"再生中={Describe(Now())}");
                var boss = Field(stage, "_boss") as Node;
                var posts = boss == null ? null : Field(boss, "_posts") as BossPostSequence;
                Check($"{id}/{difficulty}: 投稿の音楽制御がある", posts != null, "BossPostSequence が無い");
                if (posts != null)
                {
                    _audio.Music(_audio.StoryBgm(id, false), 0);
                    posts.ResumeMusic();
                    CheckSame($"{id}/{difficulty}: 回想後の曲の復帰", Now(), expected);
                }
                await CloseStage(root);
            }
        }

        await MeasureFinalCutscene();
        await EndingMusicChecks();

        // ⑧ 設定画面は**タイトルの曲を継続する**（薄いサブ画面は親の曲を引き継ぐ設計）。
        //    タイトル → 設定 で曲が入れ替わらないこと。
        {
            _audio.StopMusic(0);
            await Frames(2);
            var title = GD.Load<PackedScene>("res://TitleMenu.tscn").Instantiate();
            GetTree().Root.AddChild(title);
            GetTree().CurrentScene = title;
            await Frames(10);
            CheckSame("タイトルの曲＝BgmTitle", Now(), _audio.BgmTitle);
            GetTree().CurrentScene = null;
            title.QueueFree();
            await Frames(6);
            var settings = GD.Load<PackedScene>("res://Settings.tscn").Instantiate();
            GetTree().Root.AddChild(settings);
            GetTree().CurrentScene = settings;
            await Frames(10);
            CheckSame("設定画面はタイトルの曲を継続する", Now(), _audio.BgmTitle);
            GetTree().CurrentScene = null;
            settings.QueueFree();
            await Frames(24);
        }

        Engine.TimeScale = 1;
    }

    private async Task MeasureFinalCutscene()
    {
        var game = GetNode<GameManager>("/root/Game");
        _audio.StopMusic(0);
        await Frames(2);
        double savedScale = Engine.TimeScale;
        Engine.TimeScale = 12;
        game.AutoAdvanceDialog = true;
        game.MsgCharsPerSec = 48;

        var final = GD.Load<PackedScene>("res://Final.tscn").Instantiate();
        GetTree().Root.AddChild(final);
        GetTree().CurrentScene = final;
        await Frames(4);
        CheckSame("F4 の頭の曲＝BgmFinalCutscene", Now(), _audio.BgmFinalCutscene);
        Check("F4 の曲が実音源", Now() is AudioStreamOggVorbis or AudioStreamMP3,
              $"再生中={Describe(Now())}");

        double toSilence = await RunUntil(() => ReadBool(final, "_cueSilenceDone"), 4000);
        Check("F4: 選択前の無音キューに到達する", toSilence >= 0, "到達しない");
        Check("F4: 選択前に無音になる", Now() == null, $"再生中={Describe(Now())}");
        double toChoice = await RunUntil(() => Field(final, "_choice") != null, 4000);
        Check("F4: 下書き選択まで進む", toChoice >= 0, "選択が出ない");
        await Frames(30);
        Check("F4: 選択待ちの間は無音を保つ", Now() == null, $"再生中={Describe(Now())}");

        // AUTO は選択肢を確定しないため、通常の決定入力で解決曲への遷移を検証する。
        Input.ActionPress("ui_accept");
        await Frames(2);
        Input.ActionRelease("ui_accept");
        double choiceOpen = await RunUntil(() => Field(final, "_choice") == null, 600);
        Check("F4: 決定入力で選択を確定する", choiceOpen >= 0, "確定しない");
        double toResolve = await RunUntil(() => ReadBool(final, "_cueResolveDone"), 4000);
        Check("F4: 解決曲のキューに到達する", toResolve >= 0, "到達しない");
        await Frames(4);
        CheckSame("F4: 無音のあと挿入曲（BgmFinalResolve）が立つ", Now(), _audio.BgmFinalResolve);

        GetTree().CurrentScene = null;
        final.QueueFree();
        Engine.TimeScale = savedScale;
        await Frames(24);
        game.AutoAdvanceDialog = false;
        _audio.StopMusic(0);
        await Frames(4);
    }

    private async Task EndingMusicChecks()
    {
        var game = GetNode<GameManager>("/root/Game");
        game.AutoAdvanceDialog = false;
        _audio.Music(_audio.BgmFinalResolve, 0);
        await Frames(12);
        var before = ActiveMusicPlayer();
        double position = before.GetPlaybackPosition();
        var epilogue = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Epilogue>();
        GetTree().Root.AddChild(epilogue);
        GetTree().CurrentScene = epilogue;
        await Frames(4);
        CheckSame("帰還後も回復の主題を継続する", Now(), _audio.BgmFinalResolve);
        Check("帰還時に曲を頭へ巻き戻さない", ActiveMusicPlayer() == before
              && before.GetPlaybackPosition() + 0.02 >= position, "再生プレイヤーまたは再生位置が戻った");
        Check("帰還後の会話は読む長さによらずループできる",
              _audio.BgmFinalResolve is AudioStreamOggVorbis { Loop: true }, "ループしない");

        typeof(Epilogue).GetMethod("StartFilm", Priv)!.Invoke(epilogue, null);
        await Frames(4);
        CheckSame("エンディング映像で専用曲を立ち上げる", Now(), _audio.BgmEndingFilm);
        var filmPlayer = ActiveMusicPlayer();
        var film = (EndingFilm)Field(epilogue, "_film")!;
        var statics = BindingFlags.Static | BindingFlags.NonPublic;
        float lineHeight = (float)typeof(Epilogue).GetField("RollLineH", statics)!.GetValue(null)!;
        float rollSpeed = (float)typeof(Epilogue).GetField("RollSpeed", statics)!.GetValue(null)!;
        double rollDuration = (216 + ((string[])Field(epilogue, "_roll")!).Length * lineHeight + 24) / rollSpeed;
        double sequenceDuration = EndingFilm.Duration + rollDuration;
        GD.Print($"[bgmqa] 映像＋ロール={sequenceDuration:0.00}s / 曲={_audio.BgmEndingFilm.GetLength():0.00}s");
        Check("映像とスタッフロールを曲の長さが覆う", _audio.BgmEndingFilm.GetLength() >= sequenceDuration,
              "曲がロール途中で終わる");
        GetTree().Paused = true;
        await Frames(6);
        Check("ポーズや会話ログでは映像と一緒に曲を止める", filmPlayer.StreamPaused,
              "映像を止めても曲だけ進んでしまう");
        GetTree().Paused = false;
        await Frames(6);
        Check("ポーズを閉じると同じ演奏を再開する", !filmPlayer.StreamPaused && filmPlayer.Playing
              && ActiveMusicPlayer() == filmPlayer, "再生が再開しない、または再生先が変わった");
        typeof(EndingFilm).GetMethod("Complete", Priv)!.Invoke(film, null);
        await Frames(4);
        Check("映像終了後にスタッフロールへ進む", ReadInt(epilogue, "_phase") == 2, "PhRoll ではない");
        CheckSame("スタッフロールで同じ演奏を継続する", Now(), _audio.BgmEndingFilm);
        Check("スタッフロールで曲を再スタートしない", ActiveMusicPlayer() == filmPlayer, "再生先が変わった");

        typeof(Epilogue).GetField("_t", Priv)!.SetValue(epilogue, rollDuration + 0.1);
        await Frames(4);
        Check("ロール終了後に最後の会話へ進む", ReadInt(epilogue, "_phase") == 3, "PhEnd ではない");
        CheckSame("最後の会話で回復の主題が戻る", Now(), _audio.BgmFinalResolve);
        Check("一度再生の終了通知を解除する", Field(_audio, "_oncePlayer") == null, "接続が残った");
        filmPlayer.EmitSignal(AudioStreamPlayer.SignalName.Finished);
        CheckSame("前の曲の終了通知で最後の会話を無音にしない", Now(), _audio.BgmFinalResolve);
        await CloseStage(epilogue);
    }

    private AudioStreamPlayer ActiveMusicPlayer()
        => (AudioStreamPlayer)Field(_audio, (bool)Field(_audio, "_useA")! ? "_musicA" : "_musicB")!;

    private static object? Field(Node node, string name)
        => node.GetType().GetField(name, Priv)?.GetValue(node);

    private static bool ReadBool(Node node, string name) => Field(node, name) is true;

    // 条件が立つまで回し、**ゲーム内秒**を返す（立たなければ -1）。
    private async Task<double> RunUntil(Func<bool> cond, int maxFrames)
    {
        double t = 0;
        for (int i = 0; i < maxFrames; i++)
        {
            if (cond()) return t;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += GetProcessDeltaTime();
        }
        return cond() ? t : -1;
    }

    // ───────── 足回り ─────────
    private async Task<(Node root, Node stage)> OpenStage(string path, GameManager game,
        GameManager.Diff diff, GameManager.StageEntry entry, bool skipMinaBanner)
    {
        _audio.StopMusic(0);
        await Frames(2);
        game.Difficulty = diff;
        game.SelectedEntry = entry;
        var root = GD.Load<PackedScene>(path).Instantiate();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        var stage = FindStage(root);
        Check($"{path}: ステージノードが取れた", stage != null, "見つからない");
        // FINAL の 5.2 秒タイトルカードは _Process ごと進行を止める。演出の確認はここの目的ではないので、
        //   最初の _Process が走る前に「もう出した」ことにして飛ばす（曲の配線は一切変えない）。
        if (skipMinaBanner && stage is StageMina)
        {
            stage.GetType().GetField("_startBannerShown", Priv)?.SetValue(stage, true);
            stage.GetType().GetField("_titleThump", Priv)?.SetValue(stage, true);
        }
        return (root, stage!);
    }

    private async Task CloseStage(Node root)
    {
        // ステージが撒いた弾・敵を先に掃いてから畳む（残したまま抜けると終了時に解放漏れが出る）。
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        if (IsInstanceValid(root)) { GetTree().CurrentScene = null; root.QueueFree(); }
        await Frames(12);
        _audio.StopMusic(0);
        await Frames(4);
    }

    private static Node? FindStage(Node root)
    {
        foreach (var child in root.GetChildren())
            if (child is StageMina or StageAkari or StageKoharu or StageRei) return child;
        return null;
    }

    private AudioStream? Now() => CurrentMusicField.GetValue(_audio) as AudioStream;

    private static string Describe(AudioStream? s)
        => s == null ? "(無音)" : string.IsNullOrEmpty(s.ResourcePath) ? $"合成:{s.GetType().Name}" : s.ResourcePath;

    private static int ReadInt(Node node, string field)
        => node.GetType().GetField(field, Priv)?.GetValue(node) is int i ? i : -1;

    private void CheckSame(string label, AudioStream? actual, AudioStream? expected)
        => Check(label, ReferenceEquals(actual, expected),
                 $"期待={Describe(expected)} 実際={Describe(actual)}");

    private void Check(string label, bool ok, string detail)
    {
        if (!ok) _fail++;
        GD.Print($"[bgmqa] {(ok ? "OK  " : "NG  ")}{label}{(ok ? "" : $" — {detail}")}");
    }

    private async Task<bool> Until(Func<bool> cond, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (cond()) return true;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        return cond();
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
