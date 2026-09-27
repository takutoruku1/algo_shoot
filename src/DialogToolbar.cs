using Godot;

// DialogToolbar : 会話ボックス上辺のボタン列「AUTO / SKIP / LOG / MENU」（2026-09-27 作者指示）。
//   ノベルゲーム定番の「テキスト枠の上辺・右寄せに小さなボタンが横一列」を本作の会話ボックスに付ける。
//   出る場所：Hud が描く全ての会話ボックス（戦闘中の会話バー・ナレーション枠・カットシーン（CinematicMode：
//   StoryFilm の回想/アフター、BossDraftScene、MinaPhaseScene））と、会話枠を自前で描く4画面
//   （Prologue／Final／Epilogue のカットシーンと Hub の返信会話）。後者は Hud を通らないので、
//   状態・入力・描画をこの DialogToolbar（ノードではない部品）に切り出し、各画面が1つずつ持って
//   Tick（_Process の先頭）と Draw／DrawLatchMark（_Draw の最後・設計座標）を呼ぶ。Hud も同じ部品を持つ（下の partial）。
//
//   ボタン    | 動作                                                        | KB | パッド                  | マウス
//   AUTO ▶    | 自動送りの切替（GameManager.AutoAdvanceDialog＝設定の「オート会話送り」と同じ値・同じ保存先） | A | Y | クリック
//   SKIP ▶▶   | 既読スキップのラッチ（ON の間 SkipHeld が真）                   | S  | RB 押し離し（長押しは従来の押しっぱなし） | クリック
//   LOG 横線  | 会話ログ（Backlog）                                          | L  | View                    | クリック
//   MENU 歯車 | ポーズメニュー（PauseMenu）                                   | M  | Menu(≡)                 | クリック
//
//   ・見た目（2026-09-27 作者指示「ボタンの UI がださい」→ 参考のノベルゲーム風に作り直し）：文字ラベルは無く、
//     30×26 の角丸ボタンにベクタのアイコン（▶／▶▶／横線3本／歯車）だけを描く（フォント文字は使わない）。
//     キー文字はボタンにも吹き出しにも載せない（作者指示。キーの案内は「あそびかた」＝HowToPlay だけが担う）。
//     名前はマウスを乗せたときの吹き出しで出す。ON（AUTO 有効／SKIP ラッチ・早送り中）は
//     地を Info で塗りつぶし、グリフを濃色に反転する。キー操作とクリックは上の表のとおり効く。
//   ・LOG／MENU のキー（L／Tab／View、M／Start）は Backlog／PauseMenu が全画面で自前に読んでいる＝ここでは読まない
//     （読むと同じ押下で二度開く）。ここが足すのはクリックだけ。
//     例外：カットシーン（Prologue／Final／Epilogue）のパッド Start は「長押しでさいしょから」（RetryHold）で、
//     PauseMenu はそこでは Start を読まない。そこだけ RB と同じ作法で「Start の押し離し（短押し）＝MENU」を
//     ここが読む（startTapOpensMenu）。長押しは従来どおりリトライのまま。
//   ・A／S は戦闘中はロックオンの前／次だが、会話ボックス表示中（BubblePaused）は自機が止まっている
//     ので衝突しない。ボタンは**ボックス表示中だけ**反応する。さらに出た直後 Grace 秒は無視する
//     ＝避けながら A/S を叩いている最中に会話が割り込んでも、その押下でトグルしない。
//   ・SKIP ラッチは画面が切り替わっても保つ（2026-09-27 作者指示「画面が切り替わる度にスキップモードが外れる」）。
//     ステージ→回想→戦闘→ハブ→次のステージと移っても、既読の会話が出るたびに入力なしで早送りされる。
//     切れるのは次の3つだけ：未読行に当たった（呼び出し側が unreadLine で渡す。戦闘内回想＝BattleMemoryTempo は
//     Hud 側で未読に数えない）／選択肢（ChoiceOverlay）が出た／SKIP をもう一度押した。
//     会話ボックスが出ていない間もラッチが立っていれば、画面右上に小さな「▶▶」の印（DrawLatchMark）を出す。
//     AUTO は設定値なので会話が終わっても保持する。
//   ・ボタン列にカーソルが乗っている間の左クリックは会話送りに数えない（Pad.CaptureMouse → Pad.AdvanceHeld）。
//     各画面は Tick を _Process の先頭（Pad.AdvanceHeld を読む前）で呼ぶ。
//   ・当たり判定は Pad.MousePos()（設計座標 1280×720）と描画と同じ矩形（ButtonRect）を直接突き合わせる。
//     UiKit のホットスポット登録は使わない（ChoiceOverlay 等と同フレームで潰し合う。PauseMenu.HintClickable 参照）。
//   ・座標はすべて設計座標。384×216 の世界座標で枠を描く画面（UiKit.CutBox）は、枠の右上を
//     UiKit.Scale で割って渡し、Draw は UiKit.BeginDesign の下で呼ぶ。
public sealed class DialogToolbar
{
    // マウスを乗せたときの吹き出し（見た目はアイコンだけなので、名前はここで言葉にする）。
    private static readonly string[] Tips = { "自動送り", "既読スキップ", "ログ", "メニュー" };
    public const int Auto = 0, Skip = 1, Log = 2, Menu = 3;
    private const int Count = 4;
    public const float W = 30f, H = 26f;       // ボタンの寸法（設計座標）
    private const float Radius = 6f;           // 角丸
    private const float Gap = 6f;              // ボタン同士の間隔
    private const float Bite = 10f;            // ボックス上辺へ食い込ませる量
    private const float Inset = 18f;           // ボックス右端からの引っ込み
    private const double Grace = 0.3;          // ボックスが出てからキー入力を受け付けるまで
    private const double TapMax = 0.3;         // RB／Start をこれより短く押し離したらボタン扱い（長押しは従来の意味）
    private const float MarkH = 18f;           // ラッチ中の右上の印「▶▶」の高さ
    private const float MarkAlpha = 0.7f;
    private const double BreathPeriod = 2.4;   // ON の地の呼吸（α 0.85〜1.0）の周期
    private const int TipSize = 11;            // 吹き出しの字（ZenBold）

    // 色（既存トークンから）。OFF＝暗い地・淡い枠・明るいグリフ ／ ON＝Info の塗りに濃色のグリフ。
    private static readonly Color OffBg = new(0.06f, 0.07f, 0.10f, 0.85f);
    private static readonly Color OnInk = new(0.06f, 0.09f, 0.12f);

    private bool _autoHeld = true, _skipHeld = true, _rbHeld = true, _startHeld = true;   // 起動時の押しっぱなしをエッジにしない
    private bool _rbArmed, _startArmed;   // RB／Start をボックス表示中に押し始めたか（押し離しの切替はそのときだけ）
    private double _rbT, _startT;         // RB／Start を押している長さ
    private double _shownT;               // ボックスが出てからの経過

    // ホバー中のボタン（-1＝無し）。描画のハイライトと QA が読む。
    public int Hover { get; private set; } = -1;
    // ラッチ中の右上の印を出すか（ボックスが出ておらず、ポーズメニュー・ログも開いていない）。描画と QA が読む。
    public bool LatchMarkVisible { get; private set; }
    // 直近に描いた印の矩形（設計座標。描かなかったフレームは空）。QA 用。
    public Rect2 LatchMarkDrawnRect { get; private set; }


    // i 番目のボタンの矩形（右寄せで AUTO→MENU の順に左から並ぶ）。anchor＝ボックスの右上（設計座標）。
    //   描画・当たり判定・QA が同じ1本を通る。
    public static Rect2 ButtonRect(Vector2 anchor, int i)
    {
        float right = anchor.X - Inset - (Count - 1 - i) * (W + Gap);
        return new Rect2(right - W, anchor.Y - H + Bite, W, H);
    }

    // ラッチ中の印の矩形。topRight＝印の右上（盤面なら (Field.DRight-16, 14)、全画面なら (UiKit.DesignW-16, 14)）。
    //   右向き三角 2 つ（高さ MarkH）を突き合わせて並べた外接矩形。
    public static Rect2 LatchMarkRect(Vector2 topRight)
    {
        float w = MarkH * 0.8f * 2f;
        return new Rect2(topRight.X - w, topRight.Y, w, MarkH);
    }

    // 毎フレーム1回（ボックスが出ていないフレームも）呼ぶ。
    //   owner       … 呼び出し元のノード（Pad.UiBlocked の主体・ツリー参照）
    //   shown       … いま会話ボックスが画面に出ているか（＝ボタン列を出す／受け付ける条件）
    //   anchor      … ボックスの右上（設計座標）
    //   unreadLine  … 表示中の行が「表示時点で未読」か（真なら SKIP ラッチを即切る）
    //   startTapOpensMenu … パッド Start の短押しで MENU を開く（カットシーン専用。上の注記）
    public void Tick(Node owner, double delta, bool shown, Vector2 anchor, bool unreadLine, bool startTapOpensMenu = false)
    {
        bool blocked = Pad.UiBlocked(owner);

        // キーの押下は表示の有無に関係なく毎フレーム追う＝ボックスが出る前からの押しっぱなしをエッジにしない。
        bool a = Input.IsKeyPressed(Key.A) || Pad.Pressed(JoyButton.Y);
        bool s = Input.IsKeyPressed(Key.S);
        bool aEdge = a && !_autoHeld; _autoHeld = a;
        bool sEdge = s && !_skipHeld; _skipHeld = s;
        bool rbTap = TapEdge(Pad.Pressed(JoyButton.RightShoulder), shown, delta, ref _rbHeld, ref _rbArmed, ref _rbT);
        bool startTap = TapEdge(Pad.Pressed(JoyButton.Start), shown, delta, ref _startHeld, ref _startArmed, ref _startT);

        if (shown) _shownT += delta; else _shownT = 0;

        // ホバー（描画のハイライトと、送りへのクリック漏れ止め）。
        Hover = -1;
        if (shown && !blocked)
        {
            var m = Pad.MousePos();
            for (int i = 0; i < Count; i++)
                if (ButtonRect(anchor, i).HasPoint(m)) { Hover = i; break; }
            if (Hover >= 0) Pad.CaptureMouse();
        }

        bool live = shown && !blocked && _shownT >= Grace;
        int click = live && Hover >= 0 && Pad.MouseClick() ? Hover : -1;
        if (live)
        {
            if (aEdge || click == Auto) ToggleAutoAdvance(owner.GetNodeOrNull<GameManager>("/root/Game"));
            if (sEdge || rbTap || click == Skip)
            {
                Hud.SkipLatched = !Hud.SkipLatched;
                Audio.Instance?.PlayUiMove();
            }
            if (click == Log) owner.GetNodeOrNull<Backlog>("/root/Backlog")?.Open();
            if (click == Menu || (startTapOpensMenu && startTap)) OpenPauseFromToolbar(owner);
        }

        // SKIP ラッチの自動解除（進めなくなったら切る）。会話ボックスが消えた・画面が替わった、では切らない
        //   （ラッチは static＝シーンを跨いで残り、次の画面の既読の会話もそのまま早送りする）。
        if (Hud.SkipLatched)
        {
            bool unread = shown && unreadLine;
            bool choice = owner.GetTree().GetFirstNodeInGroup("choice_overlay") != null;
            if (unread || choice) Hud.SkipLatched = false;
        }
        LatchMarkVisible = Hud.SkipLatched && !shown && !blocked && !OverlayOpen(owner);
    }

    // ポーズメニュー・会話ログが開いているか（その間は右上の印を出さない）。
    private static bool OverlayOpen(Node n) =>
        n.GetNodeOrNull<PauseMenu>("/root/PauseMenu")?.IsOpen == true
        || n.GetNodeOrNull<Backlog>("/root/Backlog")?.IsOpen == true;

    // ボタンの短押し（押し離し）エッジ。ボックス表示中に押し始め、TapMax 以内に離したときだけ真。
    private static bool TapEdge(bool down, bool shown, double delta, ref bool held, ref bool armed, ref double t)
    {
        bool tap = false;
        if (down)
        {
            if (!held) { t = 0; armed = shown; }
            else t += delta;
        }
        else if (held && armed && t <= TapMax) tap = true;
        held = down;
        return tap;
    }

    // AUTO：設定の「オート会話送り」と同じ値を反転し、同じ user://settings.json の "auto" へ保存する
    //   （Settings.Save と同じファイル・同じキー。他キーは保ったままマージ書きする）。
    public static void ToggleAutoAdvance(GameManager? game)
    {
        if (game == null) return;
        game.AutoAdvanceDialog = !game.AutoAdvanceDialog;
        Audio.Instance?.PlayUiMove();
        const string path = "user://settings.json";
        var data = new Godot.Collections.Dictionary();
        if (FileAccess.FileExists(path))
        {
            using var rf = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (rf != null)
            {
                var json = new Json();
                if (json.Parse(rf.GetAsText()) == Error.Ok && json.Data.VariantType == Variant.Type.Dictionary)
                    data = json.Data.AsGodotDictionary();
            }
        }
        data["auto"] = game.AutoAdvanceDialog;
        using var wf = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        wf?.StoreString(Json.Stringify(data));
    }

    // MENU のクリック（カットシーンでは Start の短押しも）：M／Start と同じポーズメニューを開く。
    public static void OpenPauseFromToolbar(Node from)
    {
        var pm = from.GetNodeOrNull<PauseMenu>("/root/PauseMenu");
        if (pm == null || pm.IsOpen) return;
        pm.Open();
    }

    // ボタン列の描画（設計座標・呼び出し側が BeginDesign 済み）。ボックスが出ている間だけ呼ぶ。
    //   autoOn … AUTO の点灯（自動送りが有効） ／ skipOn … SKIP の点灯（ラッチ中、または押しっぱなしの早送り中）。
    public void Draw(CanvasItem ci, Vector2 anchor, bool autoOn, bool skipOn)
    {
        // ON の地の呼吸（控えめ。α 0.85〜1.0）。
        float breath = 0.925f + 0.075f * Mathf.Sin((float)(Time.GetTicksMsec() / 1000.0 / BreathPeriod) * Mathf.Tau);
        for (int i = 0; i < Count; i++)
        {
            bool on = i switch
            {
                Auto => autoOn,
                Skip => skipOn,
                _ => false,
            };
            DrawButton(ci, ButtonRect(anchor, i), i, on, Hover == i, breath);
        }
        if (Hover >= 0) DrawTip(ci, ButtonRect(anchor, Hover), Tips[Hover]);
    }

    // ラッチ中の印「▶▶」（設計座標・呼び出し側が BeginDesign 済み）。毎フレーム呼んでよい（出す条件はここで見る）。
    //   会話ボックスが出たらボタン列の SKIP が点くので印は消える。
    public void DrawLatchMark(CanvasItem ci, Vector2 topRight)
    {
        if (!LatchMarkVisible || !Hud.SkipLatched) { LatchMarkDrawnRect = default; return; }
        var r = LatchMarkRect(topRight);
        var c = new Color(UiKit.Info, MarkAlpha);
        float tw = r.Size.X / 2f;
        // 半透明なので重ねない（重ねると重なりだけ濃くなる）＝突き合わせて並べる。
        Triangle(ci, r.Position, tw, MarkH, c, false);
        Triangle(ci, r.Position + new Vector2(tw, 0f), tw, MarkH, c, false);
        LatchMarkDrawnRect = r;
    }

    private static void DrawButton(CanvasItem ci, Rect2 r, int i, bool on, bool hover, float breath)
    {
        Color bg = on ? new Color(UiKit.Info, breath) : OffBg;
        if (hover) bg = new Color(bg.Lerp(Colors.White, 0.10f), bg.A);
        Color border = on ? UiKit.Info.Lightened(0.3f) : hover ? UiKit.Text3 : UiKit.Text4;
        Color ink = on ? OnInk : hover ? Colors.White : UiKit.Text2;
        UiKit.Box(ci, r, bg, Radius, border, 1f);

        // グリフはボタンの中心に置く（キー文字は載せない＝アイコンだけ）。
        var c = r.GetCenter();
        switch (i)
        {
            case Auto:
            {
                // 右向き三角 1 つ（高さ 11）。三角の重心は底辺から幅の 1/3 なので、底辺を中心の少し左に置く。
                float h = 11f, w = h * 0.866f;
                Triangle(ci, new Vector2(c.X - w * 0.4f, c.Y - h / 2f), w, h, ink, true);
                break;
            }
            case Skip:
            {
                // 右向き三角 2 つ（各 高さ 10）、横に 1px 重ねて並べる。
                float h = 10f, w = h * 0.866f, x0 = c.X - (w * 2f - 1f) / 2f;
                Triangle(ci, new Vector2(x0, c.Y - h / 2f), w, h, ink, true);
                Triangle(ci, new Vector2(x0 + w - 1f, c.Y - h / 2f), w, h, ink, true);
                break;
            }
            case Log:
                // 横線 3 本（幅 12・太さ 1.6・間隔 3.5）＝「ログ＝一覧」。
                for (int k = -1; k <= 1; k++)
                    ci.DrawLine(new Vector2(c.X - 6f, c.Y + k * 3.5f), new Vector2(c.X + 6f, c.Y + k * 3.5f), ink, 1.6f, true);
                break;
            default:
                Gear(ci, c, ink);
                break;
        }
    }

    // 右向き三角（topLeft＝外接矩形の左上）。aa＝縁に細い AA 線を足す（不透明のグリフだけ。半透明だと縁が濃くなる）。
    private static void Triangle(CanvasItem ci, Vector2 topLeft, float w, float h, Color col, bool aa)
    {
        var pts = new[] { topLeft, new Vector2(topLeft.X + w, topLeft.Y + h / 2f), new Vector2(topLeft.X, topLeft.Y + h) };
        ci.DrawColoredPolygon(pts, col);
        if (aa) ci.DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[0] }, col, 0.6f, true);
    }

    // 歯車（外径 12・歯 8・穴 径 4。指定は径 3 だが、等倍では AA で埋まって穴に見えなかったので 1px 広げた）。胴は太い円弧 1 周＝穴が本当に抜ける（地の色で上塗りしない＝半透明の地でも透けない）。
    //   歯は胴の外周に台形を 8 本立てる。
    private static void Gear(CanvasItem ci, Vector2 c, Color ink)
    {
        const float rOut = 6f, rBody = 4.6f, rHole = 2f;
        ci.DrawArc(c, (rBody + rHole) / 2f, 0f, Mathf.Tau, 28, ink, rBody - rHole, true);
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.Tau / 8f + Mathf.Pi / 8f;
            var pts = new[]
            {
                c + Vector2.FromAngle(a - 0.36f) * (rBody - 0.6f),
                c + Vector2.FromAngle(a - 0.21f) * rOut,
                c + Vector2.FromAngle(a + 0.21f) * rOut,
                c + Vector2.FromAngle(a + 0.36f) * (rBody - 0.6f),
            };
            ci.DrawColoredPolygon(pts, ink);
            ci.DrawPolyline(new[] { pts[1], pts[2] }, ink, 0.6f, true);
        }
    }

    // ホバー中のボタンの名前（ボタンの真上に小さな暗い箱）。
    private static void DrawTip(CanvasItem ci, Rect2 btn, string tip)
    {
        var f = UiKit.ZenBold;
        float h = 20f, w = UiKit.TextW(f, tip, TipSize) + 14f;
        float x = Mathf.Min(btn.GetCenter().X - w / 2f, UiKit.DesignW - 4f - w);
        var r = new Rect2(x, btn.Position.Y - 5f - h, w, h);
        UiKit.Box(ci, r, new Color(0.03f, 0.035f, 0.05f, 0.94f), 5f, new Color(UiKit.Text4, 0.7f), 1f);
        float top = r.Position.Y + (h - f.GetHeight(TipSize)) / 2f;
        UiKit.Text(ci, f, new Vector2(r.Position.X + 7f, top), tip, TipSize, UiKit.Text2);
    }
}

// Hud 側の配線：戦闘の会話バー・ナレ枠・CinematicMode の会話ボックスに DialogToolbar を載せる。
public partial class Hud
{
    // SKIP ボタンのラッチ。立て下ろしは DialogToolbar（各画面の部品）だけが行う。static＝シーンを跨いで残る
    //   （2026-09-27：画面が切り替わっても保つ。各ステージで Hud が作り直されても、新しい Hud がそのまま読む）。
    public static bool SkipLatched { get; internal set; }

    private readonly DialogToolbar _toolbar = new();

    // ボックスが出ているか（＝ボタン列を出す／受け付ける条件）。
    public bool DialogToolbarVisible => _dlgText.Length > 0 && _messageTimer > 0;
    // QA 用：ホバー中のボタン（-1＝無し）／ラッチ中の右上の印（出すか・直近に描いた矩形）。
    public int DialogToolbarHover => _toolbar.Hover;
    public bool SkipLatchMarkVisible => _toolbar.LatchMarkVisible;
    public Rect2 SkipLatchMarkDrawnRect => _toolbar.LatchMarkDrawnRect;

    // 今のボックスの右上（設計座標）。DrawDialog の各分岐の矩形と一致させる。
    //   シネマ（吹き出し）… 112,530,1056×166 ／ シネマ（帯）… StoryFilm の会話パネル 72,526,1136×174
    //   （BossDraftScene の帯は y516 から全幅なので同じ位置で収まる）
    //   ナレ … NarrBox（y590） ／ 戦闘の会話バー … DlgBox（y520）
    private Vector2 ToolbarAnchor() =>
        CinematicMode ? (_cinematicBubble ? new Vector2(1168f, 530f) : new Vector2(1208f, 526f))
        : !_dlgIsDialog ? new Vector2(NarrBoxX + NarrBoxW, 590f)
        : new Vector2(DlgBoxX + DlgBoxW, 520f);

    public Rect2 ToolbarRect(int i) => DialogToolbar.ButtonRect(ToolbarAnchor(), i);

    // ラッチ中の印の右上：戦闘は盤面の右上（ボスの体力カードが無くなって空いた所）、カットシーンは画面の右上。
    public Vector2 LatchMarkAnchor => CinematicMode ? new Vector2(UiKit.DesignW - 16f, 14f) : new Vector2(Field.DRight - 16f, 14f);

    // 未読行＝_dlgReadBefore が偽で BattleMemoryTempo（戦闘内回想の早送り許可）でもない。
    private void TickDialogToolbar(double delta) =>
        _toolbar.Tick(this, delta, DialogToolbarVisible, ToolbarAnchor(),
            unreadLine: !_dlgReadBefore && !BattleMemoryTempo);

    // ボタン列の描画（HudCanvas＝最前面。設計座標）。ボックスが出ている間はボタン列、出ていない間はラッチの印。
    private void DrawDialogToolbar(CanvasItem ci)
    {
        if (DialogToolbarVisible) _toolbar.Draw(ci, ToolbarAnchor(), AutoAdvance, SkipLatched || FastForwarding);
        _toolbar.DrawLatchMark(ci, LatchMarkAnchor);
    }
}
