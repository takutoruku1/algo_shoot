using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// SkillIntroQa : 習得スキル説明（StageTutorial の ⑤・2026-09-22）の発火とカード同期の自動検証。
//   AnkerIntroQa と同じ流儀で各ステージを実際に立てて _playerIntro を読み、以下を判定する：
//     1) 未習得（HasDodge=false / HasChargeShot=false）では出ない・once も消費しない
//     2) 習得済みかつ未見のときだけ出る（回避だけ／溜め打ちだけ／両方）。once はセーブ単位で消費
//     3) 2回目（同じセーブ）は出ない
//     4) 他ジョブ潜行中は出ない・once も消費しない
//     5) 3ステージ（あかり／こはる／レイ）のどこでも出る
//     6) 順序：イントロ → （道中チュートリアル①）→ 回避 → 溜め打ち → アンチャー紹介③
//     7) 操作カード：回避 2〜4 行目=Dodge／溜め打ち 2〜4 行目=Charge／各 1 行目と紹介の行=畳む。
//        道中チュートリアルの「移動」行が従来どおり Move を引けること（ブロックを増やしても壊れない）
//   実行: Godot --headless --path . res://tools/qa_skill_intro.tscn -- --qa-skill
//   スクショ: Godot --path . res://tools/qa_skill_intro.tscn -- --qa-skill --skill-shot-out <絶対パス>
//     （ウィンドウ必須。カード（回避／溜め打ち）と あそびかた（未習得の薄表示／習得後／強化アイテム）を撮る）
//   ※セーブを書くので APPDATA を隔離した状態で走らせること。
public partial class SkillIntroQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private const string DodgeHead = "集めていただいた欠片で、わたくしの足が、変わりました。——「回避」。身体が、覚えています。";
    private const string DodgeLast = "ただし。一度抜けると、しばらく、次は出ません。……抜けた先に、立てる場所を。";
    private const string ChargeHead = "拾っていただいた欠片が、ひとつ、かたちになりました。——「溜め打ち」。お伝えします。";
    private const string ChargeLast = "溜めているあいだ、いつもの光は止まります。満ちる前に離せば、重い一発は出ず、いつもの光に戻ります。";
    // 道中チュートリアル①の「移動」行と末尾行（順序と回帰の確認用）。
    private const string RouteMove = "操作を、お伝えします。まず、移動を。……この盤面のどこへでも、お連れします。";
    private const string RouteLast = "では。オペレータのお仕事を、よろしくお願いいたします。……ご主人様。";
    private const int SkillLines = 4;

    // (scene, 表示名, その面のアンチャー紹介③の先頭行)。
    private static readonly (string scene, string name, string ankerHead)[] Cases =
    {
        ("res://Akari.tscn",  "あかり", "この階のアンチャーは、間合いを詰めてまいります。落ちてくるもの、途中から速くなるもの。"),
        ("res://Koharu.tscn", "こはる", "ここのアンチャーは、一度に、たくさん寄越してきます。構えたと思った次の瞬間には、もう来ています。"),
        ("res://Rei.tscn",    "レイ",   "この枠のアンチャーは、正面から来ません。上から、まわりから、背中から。"),
    };

    private string? _shotDir;   // --skill-shot-out <dir> でスクショモード
    private int _fail;

    public override async void _Ready()
    {
        var user = OS.GetCmdlineUserArgs();
        for (int i = 0; i < user.Length; i++)
            if (user[i] == "--skill-shot-out" && i + 1 < user.Length) _shotDir = user[i + 1];

        await Frames(2);   // root が子のセットアップ中は AddChild できない＝1フレーム待ってから始める
        var game = GetNode<GameManager>("/root/Game");
        game.AutoSaveEnabled = false;   // 直書きした所持をディスクへ漏らさない

        try
        {
            if (_shotDir != null) { await ShotRun(game); return; }

            foreach (var (scene, node, ankerHead) in Cases)
            {
                // ── 1) 未習得＝出ない・once 未消費 ──
                var none = await IntroOf(scene, () => Fresh(game));
                Check($"{node} 未習得で回避が出ない", IndexOf(none, DodgeHead) < 0, "回避の先頭行が混ざっている");
                Check($"{node} 未習得で溜め打ちが出ない", IndexOf(none, ChargeHead) < 0, "溜め打ちの先頭行が混ざっている");
                Check($"{node} 未習得で once 未消費",
                    !game.IsIdleDialogSeen(StageTutorial.SkillDodgeSeenKey) && !game.IsIdleDialogSeen(StageTutorial.SkillChargeSeenKey),
                    "消費されている");

                // ── 2) 回避だけ（チュートリアル既読＝通常進行の2面以降に相当）＝イントロ直後・紹介③の直前 ──
                var dodgeOnly = await IntroOf(scene, () => { Fresh(game); game.MarkIdleDialogSeen(StageTutorial.RouteSeenKey); GrantDodge(game); });
                int d = IndexOf(dodgeOnly, DodgeHead);
                Check($"{node} 回避のみ 出る", d >= 0, "回避の先頭行が無い");
                Check($"{node} 回避のみ 4行", d >= 0 && d + SkillLines - 1 < dodgeOnly.Length && dodgeOnly[d + SkillLines - 1].text == DodgeLast,
                    $"末尾行='{At(dodgeOnly, d + SkillLines - 1)}'");
                Check($"{node} 回避のみ 溜め打ちは出ない", IndexOf(dodgeOnly, ChargeHead) < 0, "溜め打ちが混ざっている");
                Check($"{node} 回避のみ 順序(回避→紹介③)", d >= 0 && At(dodgeOnly, d + SkillLines) == ankerHead,
                    $"回避の直後='{At(dodgeOnly, d + SkillLines)}'");
                Check($"{node} 回避のみ チュートリアル既読なら①は出ない", IndexOf(dodgeOnly, RouteLast) < 0, "①が混ざっている");
                Check($"{node} 回避のみ once 消費", game.IsIdleDialogSeen(StageTutorial.SkillDodgeSeenKey), "未消費");
                Check($"{node} 回避のみ 溜め打ちの once 未消費", !game.IsIdleDialogSeen(StageTutorial.SkillChargeSeenKey), "消費されている");

                // ── 3) 2回目（同じセーブ）＝出ない ──
                var second = await IntroOf(scene, null);
                Check($"{node} 回避 2回目 出ない", IndexOf(second, DodgeHead) < 0, "2回目にも出ている");

                // ── 4) 溜め打ちだけ ──
                var chargeOnly = await IntroOf(scene, () => { Fresh(game); game.MarkIdleDialogSeen(StageTutorial.RouteSeenKey); game.TrainingSetUpgrade("n_charge", true); });
                int c = IndexOf(chargeOnly, ChargeHead);
                Check($"{node} 溜め打ちのみ 出る", c >= 0, "溜め打ちの先頭行が無い");
                Check($"{node} 溜め打ちのみ 4行", c >= 0 && At(chargeOnly, c + SkillLines - 1) == ChargeLast,
                    $"末尾行='{At(chargeOnly, c + SkillLines - 1)}'");
                Check($"{node} 溜め打ちのみ 回避は出ない", IndexOf(chargeOnly, DodgeHead) < 0, "回避が混ざっている");
                Check($"{node} 溜め打ちのみ 順序(溜め打ち→紹介③)", c >= 0 && At(chargeOnly, c + SkillLines) == ankerHead,
                    $"溜め打ちの直後='{At(chargeOnly, c + SkillLines)}'");
                Check($"{node} 溜め打ちのみ once 消費", game.IsIdleDialogSeen(StageTutorial.SkillChargeSeenKey), "未消費");
                Check($"{node} 溜め打ちのみ 回避の once 未消費", !game.IsIdleDialogSeen(StageTutorial.SkillDodgeSeenKey), "消費されている");
                var second2 = await IntroOf(scene, null);
                Check($"{node} 溜め打ち 2回目 出ない", IndexOf(second2, ChargeHead) < 0, "2回目にも出ている");

                // ── 5) 両方＋チュートリアル未読（新規セーブで --stage 直行に相当）＝ ① → 回避 → 溜め打ち → 紹介③ ──
                var both = await IntroOf(scene, () => { Fresh(game); GrantDodge(game); game.TrainingSetUpgrade("n_charge", true); });
                int r = IndexOf(both, RouteLast), bd = IndexOf(both, DodgeHead), bc = IndexOf(both, ChargeHead), ba = IndexOf(both, ankerHead);
                Check($"{node} 両方 順序(①→回避)", r >= 0 && bd == r + 1, $"①末尾={r} 回避={bd}");
                Check($"{node} 両方 順序(回避→溜め打ち)", bd >= 0 && bc == bd + SkillLines, $"回避={bd} 溜め打ち={bc}");
                Check($"{node} 両方 順序(溜め打ち→紹介③)", bc >= 0 && ba == bc + SkillLines, $"溜め打ち={bc} 紹介={ba}");
                Check($"{node} 両方 once 消費",
                    game.IsIdleDialogSeen(StageTutorial.SkillDodgeSeenKey) && game.IsIdleDialogSeen(StageTutorial.SkillChargeSeenKey), "未消費");

                // ── 7) 操作カードの同期（両方あり） ──
                if (r >= 0 && bd >= 0 && bc >= 0 && ba >= 0) await CardCheck(node, both, bd, bc, ba);

                // ── 4) 他ジョブ潜行＝出ない・once も消費しない ──
                var other = await IntroOf(scene, () =>
                {
                    Fresh(game); GrantDodge(game); game.TrainingSetUpgrade("n_charge", true);
                    game.SelectedJob = Job.Melee;   // 結び手以外＝キャラ別ストーリー潜行中
                });
                Check($"{node} 他ジョブで回避が出ない", IndexOf(other, DodgeHead) < 0, "回避が混ざっている");
                Check($"{node} 他ジョブで溜め打ちが出ない", IndexOf(other, ChargeHead) < 0, "溜め打ちが混ざっている");
                Check($"{node} 他ジョブで once 未消費",
                    !game.IsIdleDialogSeen(StageTutorial.SkillDodgeSeenKey) && !game.IsIdleDialogSeen(StageTutorial.SkillChargeSeenKey),
                    "消費されている");
            }
        }
        catch (Exception e)
        {
            _fail++;
            GD.PrintErr($"[skillqa] EXCEPTION {e}");
        }

        GD.Print(_fail == 0 ? "[skillqa] ALL OK" : $"[skillqa] FAILED: {_fail}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // 操作カードの同期確認。各行で ShowLine を呼び、カードの Topic が期待どおり動くか見る。
    private async Task CardCheck(string node, (int who, string text, string face)[] lines, int d, int c, int a)
    {
        var stage = StageOf();
        var hud = (Hud)_live!.GetType().GetProperty("Hud")!.GetValue(_live)!;
        int mv = IndexOf(lines, RouteMove);
        var want = new List<(int line, ControlCard.Topic topic, string label)>
        {
            (mv,    ControlCard.Topic.Move,   "①移動(回帰)"),
            (d,     ControlCard.Topic.None,   "回避1行目(畳む)"),
            (d + 1, ControlCard.Topic.Dodge,  "回避2行目"),
            (d + 2, ControlCard.Topic.Dodge,  "回避3行目"),
            (d + 3, ControlCard.Topic.Dodge,  "回避4行目"),
            (c,     ControlCard.Topic.None,   "溜め打ち1行目(畳む)"),
            (c + 1, ControlCard.Topic.Charge, "溜め打ち2行目"),
            (c + 2, ControlCard.Topic.Charge, "溜め打ち3行目"),
            (c + 3, ControlCard.Topic.Charge, "溜め打ち4行目"),
            (a,     ControlCard.Topic.None,   "紹介③1行目(畳む)"),
        };
        foreach (var (line, topic, label) in want)
        {
            if (line < 0 || line >= lines.Length) { Check($"{node} カード話題 {label}", false, $"行 {line} が範囲外"); continue; }
            SetLine(stage, line);
            await Frames(4);
            var card = hud.GetNodeOrNull<ControlCard>("ControlCard");
            var got = card == null ? ControlCard.Topic.None
                : (ControlCard.Topic)typeof(ControlCard).GetField("_next", Private)!.GetValue(card)!;
            Check($"{node} カード話題 {label}", got == topic, $"期待={topic} 実際={got}");
        }
    }

    // ═══════ スクショモード ═══════
    //   カード（回避／溜め打ち）＝あかり面の該当行へ飛ばして撮る。あそびかた＝ハブ相当の素の状態で開いて撮る。
    private async Task ShotRun(GameManager game)
    {
        DirAccess.MakeDirRecursiveAbsolute(_shotDir!);
        var lines = await IntroOf("res://Akari.tscn", () => { Fresh(game); game.MarkIdleDialogSeen(StageTutorial.RouteSeenKey); GrantDodge(game); game.TrainingSetUpgrade("n_charge", true); });
        var stage = StageOf();
        int d = IndexOf(lines, DodgeHead), c = IndexOf(lines, ChargeHead);
        if (d < 0 || c < 0) { GD.PushWarning($"[skillqa] 本文が見つからない d={d} c={c}"); GetTree().Quit(1); return; }
        (int line, string name)[] shots =
        {
            (d,     "skill_00_dodge_intro"),    // 回避 1行目＝カード無し
            (d + 1, "skill_01_dodge_card"),     // 回避 2行目＝カード Dodge
            (c + 1, "skill_02_charge_card"),    // 溜め打ち 2行目＝カード Charge
            (c + 3, "skill_03_charge_last"),    // 溜め打ち 4行目＝カード据え置き
        };
        foreach (var (line, name) in shots)
        {
            SetLine(stage, line);
            await Frames(60);   // フェード（0.22s）＋入替（0.16s）が終わるまで待つ
            Capture(name);
        }

        // あそびかた：ポーズ経由で開く（ステージ内）。習得後の状態なので回避／溜め打ちが強調行になる。
        var pause = GetNode<PauseMenu>("/root/PauseMenu");
        var how = GetNode<HowToPlay>("/root/HowTo");
        GetTree().CurrentScene = _live;   // PauseMenu.CanOpenHere はカレントシーンのパスを見る
        pause.GetType().GetMethod("Open", Private)!.Invoke(pause, null);
        await Frames(4);
        Capture("howto_10_pause_stage_rows");
        pause.GetType().GetMethod("Activate", Private)!.Invoke(pause, new object[] { 3 });   // 「あそびかた」行
        await Frames(6);
        SetPage(how, 0); await Frames(4); Capture("howto_11_kb_owned");
        SetPage(how, HowToPlay.PageItems); await Frames(4); Capture("howto_12_items");
        how.GetType().GetMethod("Close", Private)!.Invoke(how, null);
        await Frames(4);
        Capture("howto_13_pause_after_close");
        pause.GetType().GetMethod("Close", Private)!.Invoke(pause, null);
        await Frames(4);

        // 未習得の薄表示：所持を落としてから開き直す（キーボード／マウスの2タブ）。
        Fresh(game);
        how.Open();
        await Frames(4);
        SetPage(how, 0); await Frames(4); Capture("howto_14_kb_locked");
        SetPage(how, 2); await Frames(4); Capture("howto_15_mouse_locked");
        how.GetType().GetMethod("Close", Private)!.Invoke(how, null);
        await Frames(2);

        GD.Print("[skillqa] shots done.");
        GetTree().Quit();
    }

    private static void SetPage(HowToPlay how, int page)
        => how.GetType().GetMethod("SetPage", Private)!.Invoke(how, new object[] { page });

    private void Capture(string name)
    {
        var img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null) { GD.PushWarning($"[skillqa] viewport image unavailable for {name}"); return; }
        string path = $"{_shotDir!.TrimEnd('/')}/{name}.png";
        GD.Print($"[skillqa] saved {path} ({img.GetWidth()}x{img.GetHeight()}) err={img.SavePng(path)}");
    }

    // ═══════ 内部ヘルパ ═══════

    // 新規セーブ相当（所持・既読・回避をすべて落とし、結び手へ）。
    private static void Fresh(GameManager game)
    {
        game.ResetPersistent();
        game.SelectedJob = Job.Tank;
    }

    // 回避を持たせる。ショップ品目 n_dodge（2026-09-22 変更）が正典。定義がまだ無い／HasDodge が別経路の間は
    //   旧経路 GrantDodge（1面クリア報酬）で立てる＝どちらの実装でも「HasDodge=true」にできる。
    private static void GrantDodge(GameManager game)
    {
        game.TrainingSetUpgrade("n_dodge", true);
        if (!game.HasDodge) typeof(GameManager).GetMethod("GrantDodge", BindingFlags.Instance | BindingFlags.Public)?.Invoke(game, null);
        if (!game.HasDodge) GD.PushWarning("[skillqa] HasDodge を立てられない（n_dodge も GrantDodge も無効）");
    }

    private static int IndexOf((int who, string text, string face)[] a, string text)
        => Array.FindIndex(a, l => l.text == text);
    private static string At((int who, string text, string face)[] a, int i)
        => i >= 0 && i < a.Length ? a[i].text : "(範囲外)";

    private static void SetLine(Node stage, int index)
    {
        var lines = stage.GetType().GetField("_playerIntro", Private)!.GetValue(stage)!;
        stage.GetType().GetField("_introLine", Private)!.SetValue(stage, index);
        stage.GetType().GetMethod("ShowLine", Private)!.Invoke(stage, new[] { lines });
    }

    // ステージを立てて _playerIntro（_Ready で確定済み）を読む。setup は前のステージを解放したあと・立てる前に呼ぶ
    //   （GrantDodge が生きている HUD へバナーを出そうとしないように）。
    private Node? _live;
    private Node StageOf() => (Node)_live!.GetType().GetProperty("Stage")!.GetValue(_live)!;

    private async Task<(int who, string text, string face)[]> IntroOf(string scene, Action? setup)
    {
        if (_live != null && IsInstanceValid(_live))
        {
            GetTree().Root.RemoveChild(_live);
            _live.QueueFree();
            _live = null;
            await Frames(2);
        }
        setup?.Invoke();
        _live = GD.Load<PackedScene>(scene).Instantiate();
        GetTree().Root.AddChild(_live);
        await Frames(30);
        var stage = StageOf();
        var raw = (ValueTuple<int, string, string>[])
            stage.GetType().GetField("_playerIntro", Private)!.GetValue(stage)!;
        return raw.Select(t => (t.Item1, t.Item2, t.Item3)).ToArray();
    }

    private void Check(string label, bool ok, string detail)
    {
        if (!ok) _fail++;
        GD.Print($"[skillqa] {(ok ? "OK  " : "NG  ")}{label}{(ok ? "" : $" — {detail}")}");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
