using Godot;

// DialogToolbar : 会話ボックス内のボタン列「AUTO / SKIP / LOG / MENU」（2026-09-27 作者指示）。
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
//     キー文字はボタンにも吹き出しにも載せない（作者指示。キーの案内は「あそびかた」＝HowToPlay だけが担う）。
//     名前はマウスを乗せたときの吹き出しで出す。ON（AUTO 有効／SKIP ラッチ・早送り中）は
//     専用のミント色／琥珀色イラストへ切り替え、枠も点灯する。キー操作とクリックは上の表のとおり効く。
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
//   ・座標はすべて設計座標。Draw は UiKit.BeginDesign の下で呼ぶ。
public sealed class DialogToolbar
{
    // マウスを乗せたときの吹き出し（見た目はアイコンだけなので、名前はここで言葉にする）。
    private static readonly string[] Tips = { "自動送り", "既読スキップ", "ログ", "メニュー" };
    public const int Auto = 0, Skip = 1, Log = 2, Menu = 3;
    private const int Count = 4;
    public const float W = 36f, H = 30f;       // ボタンの寸法（設計座標）
    private const float Gap = 4f;              // ボタン同士の間隔
    private const float Top = 12f;
    private const float Inset = DialogueBox.Padding;
    private const double Grace = 0.3;          // ボックスが出てからキー入力を受け付けるまで
    private const double TapMax = 0.3;         // RB／Start をこれより短く押し離したらボタン扱い（長押しは従来の意味）
    private const float MarkH = 18f;           // ラッチ中の右上の印「▶▶」の高さ
    private const float MarkAlpha = 0.7f;
    private const double BreathPeriod = 2.4;   // ON の地の呼吸（α 0.85〜1.0）の周期
    private const int TipSize = 11;            // 吹き出しの字（ZenBold）

    private static readonly Color Active = new("a4e9d9");
    private static readonly Color SkipActive = new("f4bf62");
    private static readonly string[] IconPaths = {
        "res://char/ui/dialog_auto_v1.png", "res://char/ui/dialog_skip_v1.png",
        "res://char/ui/dialog_log_v1.png", "res://char/ui/dialog_menu_v1.png",
    };
    // Source bounds exclude transparent margins; scale with the importer's size limit.
    private static readonly Rect2[] IconRegions = {
        new(224, 156, 820, 944), new(104, 312, 1068, 648),
        new(244, 136, 784, 992), new(76, 88, 1104, 1080),
    };
    private static readonly CanvasTexture?[] Icons = new CanvasTexture?[Count];
    private static readonly string[] ActiveIconPaths = {
        "res://char/ui/dialog_auto_on_v1.png", "res://char/ui/dialog_skip_on_v1.png",
    };
    private static readonly CanvasTexture?[] ActiveIcons = new CanvasTexture?[2];

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
        return new Rect2(right - W, anchor.Y + Top, W, H);
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
        DrawDock(ci, anchor);
        // ON の地の呼吸（控えめ。α 0.85〜1.0）。
        float breath = 0.925f + 0.075f * Mathf.Sin((float)(Time.GetTicksMsec() / 1000.0 / BreathPeriod) * Mathf.Tau);
        for (int i = 0; i < Count; i++)
        {
            bool on = i switch
            {
                Auto => autoOn,
                Skip => skipOn,
                Log => ci.GetNodeOrNull<Backlog>("/root/Backlog")?.IsOpen == true,
                Menu => ci.GetNodeOrNull<PauseMenu>("/root/PauseMenu")?.IsOpen == true,
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
        DrawIcon(ci, r, Skip, new Color(1, 1, 1, MarkAlpha), active: true);
        LatchMarkDrawnRect = r;
    }

    private static void DrawDock(CanvasItem ci, Vector2 anchor)
    {
        var first = ButtonRect(anchor, Auto);
        float divider = ButtonRect(anchor, Skip).End.X + Gap / 2f;
        ci.DrawLine(new Vector2(divider, first.Position.Y + 8), new Vector2(divider, first.End.Y - 8),
            new Color(DialogueBox.Border, 0.5f), 1, true);
    }

    public static Rect2 FilmSkipRect => ButtonRect(DialogueBox.Anchor(DialogueBox.FullScreen), Menu);

    public static void DrawFilmSkip(CanvasItem ci, float progress, bool hover, float alpha = 1)
    {
        var rect = FilmSkipRect;
        if (hover || progress > 0)
            UiKit.Box(ci, rect, new Color(SkipActive, 0.12f * alpha), 4, new Color(SkipActive, 0.6f * alpha), 1);
        DrawIcon(ci, new Rect2(rect.Position + new Vector2(6, 4), new Vector2(24, 20)), Skip,
            new Color(Colors.White, (hover ? 1 : 0.78f) * alpha), active: progress > 0);
        if (progress > 0)
            ci.DrawLine(new Vector2(rect.Position.X, rect.End.Y), new Vector2(rect.Position.X + rect.Size.X * progress, rect.End.Y),
                new Color(SkipActive, alpha), 2, true);
        if (hover && progress == 0) DrawTip(ci, rect, "映像をスキップ");
    }

    private static void DrawButton(CanvasItem ci, Rect2 r, int i, bool on, bool hover, float breath)
    {
        Color tint = i == Skip ? SkipActive : Active;
        bool pressed = hover && Input.IsMouseButtonPressed(MouseButton.Left);
        if (on || hover)
        {
            UiKit.Box(ci, r.Grow(-1), new Color(tint, pressed ? 0.28f : on ? 0.13f * breath : 0.07f),
                3f, new Color(tint, on ? 0.8f * breath : 0.35f), on ? 1.5f : 1f);
        }
        Color ink = i >= Log && (on || hover) ? tint : Colors.White;
        ink.A = on || hover ? 1f : 0.78f;
        DrawIcon(ci, new Rect2(r.Position + new Vector2(6, 4 + (pressed ? 1 : 0)), new Vector2(24, 20)), i,
            ink, active: on && i <= Skip);
    }

    internal static void DrawIcon(CanvasItem ci, Rect2 bounds, int i, Color modulate, bool active = false)
    {
        var icons = active ? ActiveIcons : Icons;
        var paths = active ? ActiveIconPaths : IconPaths;
        var texture = icons[i] ??= new CanvasTexture {
            DiffuseTexture = GD.Load<Texture2D>(paths[i]),
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
        };
        var region = IconRegions[i];
        float importScale = texture.GetWidth() / 1254f;
        region.Position *= importScale;
        region.Size *= importScale;
        var size = region.Size * Mathf.Min(bounds.Size.X / region.Size.X, bounds.Size.Y / region.Size.Y);
        ci.DrawTextureRectRegion(texture, new Rect2(bounds.GetCenter() - size / 2f, size), region, modulate);
    }

    // ホバー中のボタンの名前（ボタンの真上に小さな暗い箱）。
    private static void DrawTip(CanvasItem ci, Rect2 btn, string tip)
    {
        var f = UiKit.ZenBold;
        float h = 20f, w = UiKit.TextW(f, tip, TipSize) + 14f;
        float x = Mathf.Min(btn.GetCenter().X - w / 2f, UiKit.DesignW - 4f - w);
        var r = new Rect2(x, btn.Position.Y - 5f - h, w, h);
        UiKit.Box(ci, r, new Color(DialogueBox.Surface, 0.98f), 2f, new Color(DialogueBox.Border, 0.7f), 1f);
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

    private Vector2 ToolbarAnchor() => DialogueBox.Anchor(DialogRect);

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
