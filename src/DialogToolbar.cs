using Godot;

// DialogToolbar : 会話ボックス上辺のボタン列「AUTO / SKIP / LOG / MENU」（2026-09-27 作者指示）。
//   ノベルゲーム定番の「テキスト枠の上辺・右寄せに小さなボタンが横一列」を本作の会話ボックスに付ける。
//   出る場所：Hud が描く全ての会話ボックス（戦闘中の会話バー・ナレーション枠・カットシーン（CinematicMode：
//   StoryFilm の回想/アフター、BossDraftScene、MinaPhaseScene））と、会話枠を自前で描く4画面
//   （Prologue／Final／Epilogue のカットシーンと Hub の返信会話）。後者は Hud を通らないので、
//   状態・入力・描画をこの DialogToolbar（ノードではない部品）に切り出し、各画面が1つずつ持って
//   Tick（_Process の先頭）と Draw（_Draw の最後・設計座標）を呼ぶ。Hud も同じ部品を持つ（下の partial）。
//
//   ボタン | 動作                                                        | KB | パッド                  | マウス
//   AUTO   | 自動送りの切替（GameManager.AutoAdvanceDialog＝設定の「オート会話送り」と同じ値・同じ保存先） | A | Y | クリック
//   SKIP   | 既読スキップのラッチ（ON の間 SkipHeld が真）                   | S  | RB 押し離し（長押しは従来の押しっぱなし） | クリック
//   LOG    | 会話ログ（Backlog）                                          | L  | View                    | クリック
//   MENU   | ポーズメニュー（PauseMenu）                                   | M  | Menu(≡)                 | クリック
//
//   ・LOG／MENU のキー（L／Tab／View、M／Start）は Backlog／PauseMenu が全画面で自前に読んでいる＝ここでは読まない
//     （読むと同じ押下で二度開く）。ここが足すのはクリックだけ。表示するキー名は同じ割り当てを出す。
//     例外：カットシーン（Prologue／Final／Epilogue）のパッド Start は「長押しでさいしょから」（RetryHold）で、
//     PauseMenu はそこでは Start を読まない。そこだけ RB と同じ作法で「Start の押し離し（短押し）＝MENU」を
//     ここが読む（startTapOpensMenu）。長押しは従来どおりリトライのまま。
//   ・A／S は戦闘中はロックオンの前／次だが、会話ボックス表示中（BubblePaused）は自機が止まっている
//     ので衝突しない。ボタンは**ボックス表示中だけ**反応する。さらに出た直後 Grace 秒は無視する
//     ＝避けながら A/S を叩いている最中に会話が割り込んでも、その押下でトグルしない。
//   ・SKIP ラッチは「進めなくなったら自動で切れる」：未読行に当たった（呼び出し側が unreadLine で渡す）／
//     選択肢（ChoiceOverlay）が出た／会話の場が閉じた（ボックスが LatchGoneGrace 秒出ていない）。
//     AUTO は設定値なので会話が終わっても保持する。
//   ・ボタン列にカーソルが乗っている間の左クリックは会話送りに数えない（Pad.CaptureMouse → Pad.AdvanceHeld）。
//     各画面は Tick を _Process の先頭（Pad.AdvanceHeld を読む前）で呼ぶ。
//   ・当たり判定は Pad.MousePos()（設計座標 1280×720）と描画と同じ矩形（ButtonRect）を直接突き合わせる。
//     UiKit のホットスポット登録は使わない（ChoiceOverlay 等と同フレームで潰し合う。PauseMenu.HintClickable 参照）。
//   ・座標はすべて設計座標。384×216 の世界座標で枠を描く画面（UiKit.CutBox）は、枠の右上を
//     UiKit.Scale で割って渡し、Draw は UiKit.BeginDesign の下で呼ぶ。
public sealed class DialogToolbar
{
    private static readonly string[] Labels = { "AUTO", "SKIP", "LOG", "MENU" };
    public const int Auto = 0, Skip = 1, Log = 2, Menu = 3;
    private const float H = 24f;               // ボタンの高さ（設計座標）
    private const float Gap = 8f;              // ボタン同士の間隔
    private const float Bite = 10f;            // ボックス上辺へ食い込ませる量
    private const float Inset = 18f;           // ボックス右端からの引っ込み
    private const double Grace = 0.3;          // ボックスが出てからキー入力を受け付けるまで
    private const double TapMax = 0.3;         // RB／Start をこれより短く押し離したらボタン扱い（長押しは従来の意味）
    private const double LatchGoneGrace = 0.2; // 会話の場が消えてからラッチを切るまで（行の差し替えの隙間を吸う）
    private static UiKit.TextStyle LabelStyle => new(UiKit.ZenBold, UiKit.FontSmall, 1f, 1f);

    private bool _autoHeld = true, _skipHeld = true, _rbHeld = true, _startHeld = true;   // 起動時の押しっぱなしをエッジにしない
    private bool _rbArmed, _startArmed;   // RB／Start をボックス表示中に押し始めたか（押し離しの切替はそのときだけ）
    private double _rbT, _startT;         // RB／Start を押している長さ
    private double _shownT;               // ボックスが出てからの経過
    private double _goneT;                // 会話の場が無くなってからの経過

    // ホバー中のボタン（-1＝無し）。描画のハイライトと QA が読む。
    public int Hover { get; private set; } = -1;

    // 操作子トークン（直近デバイスに追従。マウス時は KB 表記）。キーの実判定は Tick／Backlog／PauseMenu。
    private static string Token(int i) => i switch
    {
        Auto => Pad.UsingPad ? Pad.Face(JoyButton.Y) : "A",
        Skip => Pad.UsingPad ? Pad.Face(JoyButton.RightShoulder) : "S",
        Log  => Pad.UsingPad ? Pad.Face(JoyButton.Back) : "L",
        _    => Pad.UsingPad ? Pad.Face(JoyButton.Start) : "M",
    };

    private static float KeyW(string tok) => UiKit.TrackedW(UiKit.SmallLabel, tok) + 10f;
    private static float ButtonW(int i) => 3f + KeyW(Token(i)) + 7f + UiKit.TrackedW(LabelStyle, Labels[i]) + 10f;

    // i 番目のボタンの矩形（右寄せで AUTO→MENU の順に左から並ぶ）。anchor＝ボックスの右上（設計座標）。
    //   描画・当たり判定・QA が同じ1本を通る。
    public static Rect2 ButtonRect(Vector2 anchor, int i)
    {
        float right = anchor.X - Inset, y = anchor.Y - H + Bite;
        for (int k = Labels.Length - 1; k > i; k--) right -= ButtonW(k) + Gap;
        float w = ButtonW(i);
        return new Rect2(right - w, y, w, H);
    }

    // 毎フレーム1回（ボックスが出ていないフレームも）呼ぶ。
    //   owner       … 呼び出し元のノード（Pad.UiBlocked の主体・ツリー参照）
    //   shown       … いま会話ボックスが画面に出ているか（＝ボタン列を出す／受け付ける条件）
    //   anchor      … ボックスの右上（設計座標）
    //   unreadLine  … 表示中の行が「表示時点で未読」か（真なら SKIP ラッチを即切る）
    //   sceneHeld   … ボックスが一瞬空になっても会話の場は続いている（Hud の CinematicMode）
    //   startTapOpensMenu … パッド Start の短押しで MENU を開く（カットシーン専用。上の注記）
    public void Tick(Node owner, double delta, bool shown, Vector2 anchor, bool unreadLine,
        bool sceneHeld = false, bool startTapOpensMenu = false)
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
        if (shown || sceneHeld) _goneT = 0; else _goneT += delta;

        // ホバー（描画のハイライトと、送りへのクリック漏れ止め）。
        Hover = -1;
        if (shown && !blocked)
        {
            var m = Pad.MousePos();
            for (int i = 0; i < Labels.Length; i++)
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

        // SKIP ラッチの自動解除（進めなくなったら切る）。
        if (Hud.SkipLatched)
        {
            bool unread = shown && unreadLine;
            bool choice = owner.GetTree().GetFirstNodeInGroup("choice_overlay") != null;
            if (unread || choice || _goneT >= LatchGoneGrace) Hud.SkipLatched = false;
        }
    }

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
    //   （Settings.Save と同じファイル・同じキー。他キーは保ったままマージ書き＝Pad.SetDisplayAndSave と同じ作法）。
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
    //   autoOn … AUTO の点灯（自動送りが有効） ／ skipOn … SKIP の点灯（ラッチ中、または押しっぱなしの
    //   早送り中＝旧「▶▶」チップの代わり）。
    public void Draw(CanvasItem ci, Vector2 anchor, bool autoOn, bool skipOn)
    {
        for (int i = 0; i < Labels.Length; i++)
        {
            bool on = i switch
            {
                Auto => autoOn,
                Skip => skipOn,
                _ => false,
            };
            DrawButton(ci, ButtonRect(anchor, i), Token(i), Labels[i], on, Hover == i);
        }
    }

    private static void DrawButton(CanvasItem ci, Rect2 r, string tok, string label, bool on, bool hover)
    {
        var baseBg = new Color(0.06f, 0.05f, 0.10f);
        Color bg = on ? baseBg.Lerp(UiKit.Info, 0.34f) : baseBg;
        if (hover) bg = bg.Lerp(Colors.White, 0.10f);
        bg.A = 0.94f;
        Color border = on ? new Color(UiKit.Info, 0.95f) : new Color(hover ? UiKit.Text3 : UiKit.Text4, hover ? 0.9f : 0.6f);
        Color ink = on ? UiKit.PurifyHi : hover ? UiKit.Text2 : UiKit.Text3;
        UiKit.Box(ci, r, bg, 6f, border, 1f);

        // キー枠（ボタン左端の小さなキャップ）。ON の間は塗りつぶして反転させる。
        float keyW = KeyW(tok), keyH = H - 8f;
        var kr = new Rect2(r.Position.X + 3f, r.Position.Y + 4f, keyW, keyH);
        UiKit.Box(ci, kr, on ? new Color(UiKit.Info, 0.92f) : new Color(1f, 1f, 1f, 0.05f), 4f,
            on ? null : new Color(ink, 0.55f), 1f);
        // 英大文字の見た目の中心（ベースラインから字高の約 0.36 上）をボタンの縦中央に合わせる。
        float capTop = r.Position.Y + H / 2f + UiKit.FontSmall * 0.36f - UiKit.ZenBold.GetAscent(UiKit.FontSmall);
        UiKit.Draw(ci, UiKit.SmallLabel, new Vector2(kr.Position.X + 5f, capTop), tok, on ? UiKit.BgDeep : ink);
        UiKit.Draw(ci, LabelStyle, new Vector2(kr.End.X + 7f, capTop), label, ink);
    }
}

// Hud 側の配線：戦闘の会話バー・ナレ枠・CinematicMode の会話ボックスに DialogToolbar を載せる（挙動は切り出し前と同じ）。
public partial class Hud
{
    // SKIP ボタンのラッチ。立て下ろしは DialogToolbar（各画面の部品）と Hud._ExitTree だけが行う。
    public static bool SkipLatched { get; internal set; }

    private readonly DialogToolbar _toolbar = new();

    // ボックスが出ているか（＝ボタン列を出す／受け付ける条件）。
    public bool DialogToolbarVisible => _dlgText.Length > 0 && _messageTimer > 0;
    // QA 用：ホバー中のボタン（-1＝無し）。
    public int DialogToolbarHover => _toolbar.Hover;

    // 今のボックスの右上（設計座標）。DrawDialog の各分岐の矩形と一致させる。
    //   シネマ（吹き出し）… 112,530,1056×166 ／ シネマ（帯）… StoryFilm の会話パネル 72,526,1136×174
    //   （BossDraftScene の帯は y516 から全幅なので同じ位置で収まる）
    //   ナレ … NarrBox（y590） ／ 戦闘の会話バー … DlgBox（y520）
    private Vector2 ToolbarAnchor() =>
        CinematicMode ? (_cinematicBubble ? new Vector2(1168f, 530f) : new Vector2(1208f, 526f))
        : !_dlgIsDialog ? new Vector2(NarrBoxX + NarrBoxW, 590f)
        : new Vector2(DlgBoxX + DlgBoxW, 520f);

    public Rect2 ToolbarRect(int i) => DialogToolbar.ButtonRect(ToolbarAnchor(), i);

    // 未読行＝_dlgReadBefore が偽で BattleMemoryTempo（戦闘内回想の早送り許可）でもない。
    //   会話の場＝ボックスが出ている／カットシーン中（StoryFilm の時制見出しの一拍などボックスが空になる間も続く）。
    private void TickDialogToolbar(double delta) =>
        _toolbar.Tick(this, delta, DialogToolbarVisible, ToolbarAnchor(),
            unreadLine: !_dlgReadBefore && !BattleMemoryTempo, sceneHeld: CinematicMode);

    // ボタン列の描画（HudCanvas＝最前面。設計座標）。ボックスが出ている間だけ描く。
    private void DrawDialogToolbar(CanvasItem ci)
    {
        if (!DialogToolbarVisible) return;
        _toolbar.Draw(ci, ToolbarAnchor(), AutoAdvance, SkipLatched || FastForwarding);
    }
}
