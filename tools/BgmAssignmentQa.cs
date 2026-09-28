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
        //    FINAL 道中は専用曲が来るまで bgm_boss_hikage.ogg を流用している（ヒカゲ戦は
        //    TutorialEnabled=false で到達不能＝再配置）。これだけは既知の例外として許す。
        bool finalRouteAdopted = ResourceLoader.Exists("res://audio/bgm_final_route.ogg");
        foreach (var (path, users) in byPath.Where(kv => kv.Value.Count > 1))
        {
            bool known = !finalRouteAdopted && path == "res://audio/bgm_boss_hikage.ogg"
                         && users.Count == 2 && users.Contains("BgmFinalRoute") && users.Contains("BgmBossHikage");
            Check($"重複していない: {path}", known, $"使っているのは {string.Join(" / ", users)}");
            if (known) GD.Print($"[bgmqa] --  {path} は BgmFinalRoute が流用中（専用曲を置けば解消）");
        }
        if (finalRouteAdopted)
            Check("BgmFinalRoute は専用曲を読んでいる",
                  _audio.BgmFinalRoute.ResourcePath == "res://audio/bgm_final_route.ogg",
                  $"読んでいるのは {_audio.BgmFinalRoute.ResourcePath}");

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

        // ⑦ 設定画面は**タイトルの曲を継続する**（薄いサブ画面は親の曲を引き継ぐ設計）。
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
