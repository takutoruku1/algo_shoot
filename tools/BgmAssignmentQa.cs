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
//     ② ループ設定（ループする曲＝Loop true／エピローグのオルゴールだけ Loop false）
//     ③ StageBgm() / StoryBgm() のマップが意図どおり（FINAL の "mina" を含む）
//     ④ 同じ音源ファイルを2つ以上のスロットが使っていないか（＝「重複」の検出）
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
    // 曲が無ければ null で通してよいスロット（無い曲を代わりの音で埋めない枠）。
    private static readonly string[] NullableSlots = { "BgmEpilogueWalk" };
    // ループしない曲（.import が loop=false）。
    private static readonly string[] OneShotSlots = { "BgmEpilogueWalk" };

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
            bool nullable = NullableSlots.Contains(f.Name);
            if (stream == null)
            {
                Check($"{f.Name}: 曲がある", nullable, "null（曲が読めていない）");
                if (nullable) GD.Print($"[bgmqa] --  {f.Name} は null（未調達を許す枠）");
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

        // ④ 同じ音源を複数スロットが使っていないか（ユーザー指摘の「重複」）。
        //    専用曲が来るまで既存曲を流用している2枠だけは既知の例外として許す。どちらも
        //    「到達不能になった曲の再配置」か「合成プレースホルダの置き換え」で、専用曲を置けば自動で解消する。
        var reuse = new (string slot, string borrowed, string own)[]
        {
            // FINAL 道中 ← ヒカゲ戦（TutorialEnabled=false で到達不能＝再配置）
            ("BgmFinalRoute", "res://audio/bgm_boss_hikage.ogg", "res://audio/bgm_final_route.ogg"),
            // F4 カットシーン ← プロローグ（作品の両端で呼応させる意図的な再来）
            ("BgmFinalCutscene", "res://audio/bgm_prologue.ogg", "res://audio/bgm_final_cutscene.ogg"),
        };
        foreach (var (path, users) in byPath.Where(kv => kv.Value.Count > 1))
        {
            var hit = reuse.FirstOrDefault(r => r.borrowed == path && users.Contains(r.slot)
                                                && !ResourceLoader.Exists(r.own));
            bool known = hit.slot != null && users.Count == 2;
            Check($"重複していない: {path}", known, $"使っているのは {string.Join(" / ", users)}");
            if (known) GD.Print($"[bgmqa] --  {path} は {hit.slot} が流用中（{hit.own} を置けば解消）");
        }
        foreach (var (slot, _, own) in reuse)
        {
            if (!ResourceLoader.Exists(own)) continue;
            var stream = typeof(Audio).GetField(slot)!.GetValue(_audio) as AudioStream;
            Check($"{slot} は専用曲を読んでいる", stream?.ResourcePath == own,
                  $"読んでいるのは {Describe(stream)}");
        }

        // ③ ステージ→道中曲のマップ。
        CheckSame("StageBgm(rei)", _audio.StageBgm("rei"), _audio.BgmStageRei);
        CheckSame("StageBgm(akari)", _audio.StageBgm("akari"), _audio.BgmStageAkari);
        CheckSame("StageBgm(koharu)", _audio.StageBgm("koharu"), _audio.BgmStageKoharu);
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

        // ⑦ F4（Final のカットシーン）＝作品の頂点。ここが合成の短いループのままになっていないか。
        //    あわせて「頭 → 絶句（CueSilenceLine）」の尺を実測する。
        //    ★測り方の仮定（読む人へ）: 手動送りの場面なので、**AUTO 送り**（全文表示後 1.0 秒で次へ）と
        //      既定の文字送り 48 字/秒を仮定した値。人間が読むと必ずこれより長い＝下限値である。
        //      下書き選択は AUTO の対象外なので、沈黙 20 秒の自動送信がそのまま入る（台本どおり）。
        await MeasureFinalCutscene();

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

    // ───────── ⑦ F4 カットシーンの尺と曲 ─────────
    //   合成 BgmBoss（BuildBgmBoss＝1.6秒×4小節）の周回数を数字で出すための実測。
    private const double SynthBossLoopSec = 6.4;

    private async Task MeasureFinalCutscene()
    {
        var game = GetNode<GameManager>("/root/Game");
        _audio.StopMusic(0);
        await Frames(2);
        double savedScale = Engine.TimeScale;
        Engine.TimeScale = 12;                       // 実時間を縮めるだけ（delta は素直に 12 倍される）
        game.AutoAdvanceDialog = true;               // 手動送りの代わり＝全文表示後 1.0 秒で次へ
        game.MsgCharsPerSec = 48;                    // 既定の文字送り

        var final = GD.Load<PackedScene>("res://Final.tscn").Instantiate();
        GetTree().Root.AddChild(final);
        GetTree().CurrentScene = final;
        await Frames(4);

        CheckSame("F4 の頭の曲＝BgmFinalCutscene", Now(), _audio.BgmFinalCutscene);
        Check("F4 の曲が合成プレースホルダではない",
              Now() is AudioStreamOggVorbis or AudioStreamMP3,
              $"合成のまま: {Describe(Now())}");

        double toChoice = await RunUntil(() => Field(final, "_choice") != null, 60000);
        Check("F4: 下書き選択まで進む", toChoice >= 0, "選択が出ない");
        double choiceOpen = await RunUntil(() => Field(final, "_choice") == null, 60000);
        Check("F4: 選択が決まる（沈黙20秒の自動送信）", choiceOpen >= 0, "決まらない");
        double toSilence = await RunUntil(() => ReadBool(final, "_cueSilenceDone"), 60000);
        Check("F4: 絶句（CueSilenceLine）まで到達する", toSilence >= 0, "到達しない");

        // 落差の検証：絶句で「完全無音」になり、次の行で挿入曲が立つ。
        Check("F4: 絶句で完全無音になる", Now() == null, $"鳴っている: {Describe(Now())}");
        double toResolve = await RunUntil(() => ReadBool(final, "_cueResolveDone"), 60000);
        Check("F4: 解決の行まで到達する", toResolve >= 0, "到達しない");
        await Frames(4);
        CheckSame("F4: 無音のあと挿入曲（BgmFinalResolve）が立つ", Now(), _audio.BgmFinalResolve);

        double withChoice = toChoice + choiceOpen + toSilence;
        double instant = toChoice + toSilence;   // 選択を即決した場合の下限
        GD.Print($"[bgmqa] 実測: F4 の頭 → 絶句 = {withChoice:0.0}s"
                 + $"（うち下書き選択 {choiceOpen:0.0}s／選択を即決すると {instant:0.0}s）"
                 + $" ＝ 旧 合成 BgmBoss（{SynthBossLoopSec}s ループ）なら"
                 + $" {withChoice / SynthBossLoopSec:0.0} 周（即決でも {instant / SynthBossLoopSec:0.0} 周）");
        GD.Print("[bgmqa] 仮定: AUTO 送り（全文表示後 1.0 秒）・文字送り 48 字/秒。"
                 + "人間の手動送りでは必ずこれより長い＝下限値。");

        GetTree().CurrentScene = null;
        final.QueueFree();
        Engine.TimeScale = savedScale;   // 畳む処理は等速で回す（極端な delta のまま解放しない）
        await Frames(24);
        game.AutoAdvanceDialog = false;
        _audio.StopMusic(0);
        await Frames(4);
    }

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
