using Godot;

// FilmSkip : 「一度見たムービーは2回目以降スキップできる」の共通部品（ユーザー要望 2026-09-22）。
//
// ── なぜ「一度見たら」なのか ──────────────────────────────────────────────
//   回想（StoryFilm 8本）とFINALのフェーズ間（MinaPhaseScene 4本）は、これまで**スキップが無かった**。
//   物語の要なので初回は見てほしい／しかし周回・リトライで毎回同じ12分を読まされるのは苦行、という
//   両立が要る。そこで「初回は最後まで見せる・見終えた時点で記録し、2回目以降だけスキップを開く」。
//   ・OpeningFilm / EndingFilm は**もともと初回からスキップできる**（自前の SkipRect と 0.65 秒長押し）。
//     タイトル前／スタッフロール前で「本編の進行を飛ばさない」位置なので、そちらの既存挙動は変えない。
//     ＝スキップ不可だったものにだけ、既読を条件に開く（既存の見え方を壊さない）。
//
// ── 記録の単位 ─────────────────────────────────────────────────────────
//   ムービー1本＝キー1つ（SeenKey() が組む "once_film_<id>"）。まとめて1本のフラグにすると
//   「あかりの回想は見たのにこはるの回想まで飛ばせる」＝見ていない話が消える。
//   保存は GameManager._idleDialogSeen（MarkIdleDialogSeen / IsIdleDialogSeen）に相乗りする。
//   "once_" 接頭辞なので ResetIdleDialogSeen（小話プールの戻し）では消えず、セーブに載って永続する
//   ＝StageTutorial の once_tutorial_route と同じ流儀。新しい保存項目は増やさない。
//
// ── 発動の作法 ─────────────────────────────────────────────────────────
//   RetryHold（0.45 秒の長押し）。本作の長押し標準で、Epilogue のスタッフロールのスキップと同型。
//   押している間だけ充填チップが出て、離せば取り消せる。
//   さらに「シーンに入った瞬間すでに押されていたら、一度離すまで武装しない」（_armed）で、
//   直前の画面で押していたキーの押しっぱなしが持ち越して即スキップになるのを防ぐ。
//
//   キーは **X / パッド B**。他の候補が全部ふさがっていて、消去法でここに落ちる:
//   ・Z / Enter / ui_accept / A / 左クリック … 会話送り（Pad.AdvanceHeld）。連打で話が飛ぶ。
//   ・M / Start … **ポーズメニューが開く**（PauseMenu._Process。回想はステージシーンの上で起きるので
//     CanOpenHere が true＝実際に開く）。回想中に M を押すとポーズが乗って ConsumeUi を取られ、
//     フィルムの _Process ごと止まる。スキップに使うとポーズと二重発火するので**使えない**。
//     （2026-09-26 に開くキーが Esc → M へ移った。Esc は全画面で「一つ前へもどる」だが、戦闘シーン上では
//       誰も読まない＝回想中の Esc は何もしない。それでも「もどる」の語感をスキップに流用はしない。）
//     ※OpeningFilm / EndingFilm が Esc/Start をスキップに使えているのは、あちらが Prologue/Epilogue
//       ＝CanOpenHere の除外シーンに居てポーズが開かないから。回想には同じ手が使えない。
//   ・Ctrl / RB … 既読スキップ（Hud.SkipHeld）。
//   X は戦闘中のボムだが、フィルム中は World が Disabled で Player が動かない＝誤爆しない。
public class FilmSkip
{
    // 一度見たムービーの記録キー。id は "akari_memory" / "mina_phase_3" のような一意名。
    public static string SeenKey(string id) => $"once_film_{id}";

    public static bool Seen(GameManager? game, string id) => game != null && game.IsIdleDialogSeen(SeenKey(id));
    public static void MarkSeen(GameManager? game, string id) => game?.MarkIdleDialogSeen(SeenKey(id));

    // 撮影モード（--shot）か。スキップを完全に止めるためだけに見る（Epilogue / ChoiceOverlay と同じ作法）。
    //   自動プレイ（--demo/--qa）は送りキー（Z/A）を叩くので、X/B のスキップには巻き込まれない。
    //   --shot は撮影のために画面を保持したいので、既読セーブで撮り直すときでも飛ばさない。
    private static bool _shotHold, _shotChecked;
    private static bool ShotHold
    {
        get
        {
            if (_shotChecked) return _shotHold;
            _shotChecked = true;
            foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--shot") { _shotHold = true; break; }
            return _shotHold;
        }
    }

    private readonly RetryHold _hold = new();
    private bool _armed;

    public bool Available { get; private set; }   // 既読＝スキップを開いてよい（ヒント表示の条件でもある）
    public float Progress => Available ? _hold.Progress : 0f;

    // シーンの _Ready で一度呼ぶ。既読なら以降スキップ可、未読ならこのシーンでは一切働かない。
    public void Begin(GameManager? game, string id)
    {
        Available = Seen(game, id) && !ShotHold;
        _armed = !Pressed();   // 入った時点で押されていたら、離すまで武装しない（持ち越し誤爆の防止）
    }

    // 毎フレーム呼ぶ。長押しが満ちたフレームだけ true（呼び元がフィルムを畳む）。
    public bool Update(double delta)
    {
        if (!Available) return false;
        bool pressed = Pressed();
        if (!pressed) _armed = true;
        return _hold.Update(delta, _armed && pressed);
    }

    // スキップの押下判定。会話送りともポーズとも重ならないのは X / パッド B だけ（冒頭のコメント参照）。
    private static bool Pressed() => Input.IsKeyPressed(Godot.Key.X) || Pad.Pressed(JoyButton.B);

    // 画面下のヒント／充填チップ。既読のときだけ出す（初回は存在ごと見せない＝「飛ばせる」と思わせない）。
    //   Hud.DrawRetryHoldChip と同じ体裁だが、フィルムは Hud の上に被さるので自前で描く。
    public void Draw(CanvasItem ci)
    {
        if (!Available) return;
        string label = Pad.UsingPad ? "B 長押しでスキップ" : "X 長押しでスキップ";
        float tw = UiKit.TextW(UiKit.ZenBold, label, 14);
        const float barW = 74f, gap = 10f, h = 30f;
        float w = 14f + tw + gap + barW + 14f;
        // 会話欄（y=516〜）と送りの▼（1136,664）を避け、額の上辺（y=0〜64 の黒帯）の右端に置く。
        float x = 1280 - w - 24f, y = 17f;
        float frac = Progress;
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, frac > 0 ? 0.92f : 0.5f), 9f,
            new Color(UiKit.Info, frac > 0 ? 0.55f : 0.25f), 1.2f);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 14f, y + 7f), label, 14,
            new Color(UiKit.Text2, frac > 0 ? 1f : 0.7f));
        float bx = x + 14f + tw + gap, by = y + h / 2f - 3f;
        ci.DrawRect(new Rect2(bx, by, barW, 6f), new Color(1, 1, 1, 0.14f));
        if (frac > 0) ci.DrawRect(new Rect2(bx, by, barW * frac, 6f), UiKit.Info);
    }
}
