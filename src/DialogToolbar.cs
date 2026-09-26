using Godot;

// DialogToolbar : 会話ボックス上辺のボタン列「AUTO / SKIP / LOG / MENU」（Hud の partial・2026-09-27 作者指示）。
//   ノベルゲーム定番の「テキスト枠の上辺・右寄せに小さなボタンが横一列」を本作の会話ボックスに付ける。
//   Hud が描く全ての会話ボックスに出る＝戦闘中の会話バー・ナレーション枠・カットシーン（CinematicMode：
//   StoryFilm の回想/アフター、BossDraftScene、MinaPhaseScene）。Prologue／Final／Epilogue と Hub は
//   会話枠を自前で描いていて Hud を通らないので、ここの対象外（出していない）。
//
//   ボタン | 動作                                                        | KB | パッド                  | マウス
//   AUTO   | 自動送りの切替（GameManager.AutoAdvanceDialog＝設定の「オート会話送り」と同じ値・同じ保存先） | A | Y | クリック
//   SKIP   | 既読スキップのラッチ（ON の間 SkipHeld が真）                   | S  | RB 押し離し（長押しは従来の押しっぱなし） | クリック
//   LOG    | 会話ログ（Backlog）                                          | L  | View                    | クリック
//   MENU   | ポーズメニュー（PauseMenu）                                   | M  | Menu(≡)                 | クリック
//
//   ・LOG／MENU のキー（L／Tab／View、M／Start）は Backlog／PauseMenu が全画面で自前に読んでいる＝ここでは読まない
//     （読むと同じ押下で二度開く）。ここが足すのはクリックだけ。表示するキー名は同じ割り当てを出す。
//   ・A／S は戦闘中はロックオンの前／次だが、会話ボックス表示中（BubblePaused）は自機が止まっている
//     ので衝突しない。ボタンは**ボックス表示中だけ**反応する。さらに出た直後 TbGrace 秒は無視する
//     ＝避けながら A/S を叩いている最中に会話が割り込んでも、その押下でトグルしない。
//   ・SKIP ラッチは「進めなくなったら自動で切れる」：未読行に当たった（_dlgReadBefore が偽で BattleMemoryTempo
//     でもない）／選択肢（ChoiceOverlay）が出た／会話の場が閉じた（戦闘再開・シーン遷移）。
//     AUTO は設定値なので会話が終わっても保持する。
//   ・ボタン列にカーソルが乗っている間の左クリックは会話送りに数えない（Pad.CaptureMouse → Pad.AdvanceHeld）。
//   ・当たり判定は Pad.MousePos()（設計座標 1280×720）と描画と同じ矩形（ToolbarRect）を直接突き合わせる。
//     UiKit のホットスポット登録は使わない（ChoiceOverlay 等と同フレームで潰し合う。PauseMenu.HintClickable 参照）。
public partial class Hud
{
    public static bool SkipLatched { get; private set; }

    private static readonly string[] TbLabels = { "AUTO", "SKIP", "LOG", "MENU" };
    private const int TbAuto = 0, TbSkip = 1, TbLog = 2, TbMenu = 3;
    private const float TbH = 24f;             // ボタンの高さ（設計座標）
    private const float TbGap = 8f;            // ボタン同士の間隔
    private const float TbBite = 10f;          // ボックス上辺へ食い込ませる量
    private const float TbInset = 18f;         // ボックス右端からの引っ込み
    private const double TbGrace = 0.3;        // ボックスが出てからキー入力を受け付けるまで
    private const double RbTapMax = 0.3;       // RB をこれより短く押し離したら SKIP の切替（長押しは従来のスキップ）
    private const double LatchGoneGrace = 0.2; // 会話の場が消えてからラッチを切るまで（行の差し替えの隙間を吸う）
    private static UiKit.TextStyle TbLabelStyle => new(UiKit.ZenBold, UiKit.FontSmall, 1f, 1f);

    private bool _tbAutoHeld = true, _tbSkipHeld = true, _tbRbHeld = true;   // 起動時の押しっぱなしをエッジにしない
    private bool _tbRbArmed;          // RB をボックス表示中に押し始めたか（押し離しの切替はそのときだけ）
    private double _tbRbT;            // RB を押している長さ
    private double _tbShownT;         // ボックスが出てからの経過
    private double _tbGoneT;          // 会話の場（ボックス／カットシーン）が無くなってからの経過
    private int _tbHover = -1;

    // ボックスが出ているか（＝ボタン列を出す／受け付ける条件）。
    public bool DialogToolbarVisible => _dlgText.Length > 0 && _messageTimer > 0;
    // QA 用：ホバー中のボタン（-1＝無し）。
    public int DialogToolbarHover => _tbHover;

    // 操作子トークン（直近デバイスに追従。マウス時は KB 表記）。キーの実判定は TickDialogToolbar／Backlog／PauseMenu。
    private static string TbToken(int i) => i switch
    {
        TbAuto => Pad.UsingPad ? Pad.Face(JoyButton.Y) : "A",
        TbSkip => Pad.UsingPad ? Pad.Face(JoyButton.RightShoulder) : "S",
        TbLog  => Pad.UsingPad ? Pad.Face(JoyButton.Back) : "L",
        _      => Pad.UsingPad ? Pad.Face(JoyButton.Start) : "M",
    };

    // 今のボックスの右上（設計座標）。DrawDialog の各分岐の矩形と一致させる。
    //   シネマ（吹き出し）… 112,530,1056×166 ／ シネマ（帯）… StoryFilm の会話パネル 72,526,1136×174
    //   （BossDraftScene の帯は y516 から全幅なので同じ位置で収まる）
    //   ナレ … NarrBox（y590） ／ 戦闘の会話バー … DlgBox（y520）
    private Vector2 ToolbarAnchor() =>
        CinematicMode ? (_cinematicBubble ? new Vector2(1168f, 530f) : new Vector2(1208f, 526f))
        : !_dlgIsDialog ? new Vector2(NarrBoxX + NarrBoxW, 590f)
        : new Vector2(DlgBoxX + DlgBoxW, 520f);

    private static float TbKeyW(string tok) => UiKit.TrackedW(UiKit.SmallLabel, tok) + 10f;
    private static float TbButtonW(int i) => 3f + TbKeyW(TbToken(i)) + 7f + UiKit.TrackedW(TbLabelStyle, TbLabels[i]) + 10f;

    // i 番目のボタンの矩形（右寄せで AUTO→MENU の順に左から並ぶ）。描画・当たり判定・QA が同じ1本を通る。
    public Rect2 ToolbarRect(int i)
    {
        var a = ToolbarAnchor();
        float right = a.X - TbInset, y = a.Y - TbH + TbBite;
        for (int k = TbLabels.Length - 1; k > i; k--) right -= TbButtonW(k) + TbGap;
        float w = TbButtonW(i);
        return new Rect2(right - w, y, w, TbH);
    }

    private void TickDialogToolbar(double delta)
    {
        bool shown = DialogToolbarVisible;
        bool blocked = Pad.UiBlocked(this);

        // キーの押下は表示の有無に関係なく毎フレーム追う＝ボックスが出る前からの押しっぱなしをエッジにしない。
        bool a = Input.IsKeyPressed(Key.A) || Pad.Pressed(JoyButton.Y);
        bool s = Input.IsKeyPressed(Key.S);
        bool rb = Pad.Pressed(JoyButton.RightShoulder);
        bool aEdge = a && !_tbAutoHeld; _tbAutoHeld = a;
        bool sEdge = s && !_tbSkipHeld; _tbSkipHeld = s;
        bool rbTap = false;
        if (rb)
        {
            if (!_tbRbHeld) { _tbRbT = 0; _tbRbArmed = shown; }
            else _tbRbT += delta;
        }
        else if (_tbRbHeld && _tbRbArmed && _tbRbT <= RbTapMax) rbTap = true;
        _tbRbHeld = rb;

        if (shown) _tbShownT += delta; else _tbShownT = 0;
        // 会話の場＝ボックスが出ている／カットシーン中（StoryFilm の時制見出しの一拍などボックスが空になる間も続く）。
        if (shown || CinematicMode) _tbGoneT = 0; else _tbGoneT += delta;

        // ホバー（描画のハイライトと、送りへのクリック漏れ止め）。
        _tbHover = -1;
        if (shown && !blocked)
        {
            var m = Pad.MousePos();
            for (int i = 0; i < TbLabels.Length; i++)
                if (ToolbarRect(i).HasPoint(m)) { _tbHover = i; break; }
            if (_tbHover >= 0) Pad.CaptureMouse();
        }

        bool live = shown && !blocked && _tbShownT >= TbGrace;
        int click = live && _tbHover >= 0 && Pad.MouseClick() ? _tbHover : -1;
        if (live)
        {
            if (aEdge || click == TbAuto) ToggleAutoAdvance();
            if (sEdge || rbTap || click == TbSkip)
            {
                SkipLatched = !SkipLatched;
                Audio.Instance?.PlayUiMove();
            }
            if (click == TbLog) GetNodeOrNull<Backlog>("/root/Backlog")?.Open();
            if (click == TbMenu) OpenPauseFromToolbar();
        }

        // SKIP ラッチの自動解除（進めなくなったら切る）。
        if (SkipLatched)
        {
            bool unread = shown && !_dlgReadBefore && !BattleMemoryTempo;
            bool choice = GetTree().GetFirstNodeInGroup("choice_overlay") != null;
            if (unread || choice || _tbGoneT >= LatchGoneGrace) SkipLatched = false;
        }
    }

    // AUTO：設定の「オート会話送り」と同じ値を反転し、同じ user://settings.json の "auto" へ保存する
    //   （Settings.Save と同じファイル・同じキー。他キーは保ったままマージ書き＝Pad.SetDisplayAndSave と同じ作法）。
    private void ToggleAutoAdvance()
    {
        if (_game == null) return;
        _game.AutoAdvanceDialog = !_game.AutoAdvanceDialog;
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
        data["auto"] = _game.AutoAdvanceDialog;
        using var wf = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        wf?.StoreString(Json.Stringify(data));
    }

    // MENU のクリック：M／Start と同じポーズメニューを開く。PauseMenu.Open は非公開なので Godot の Call で呼ぶ
    //   （M／Start のキー自体は PauseMenu が自前で読む）。
    private void OpenPauseFromToolbar()
    {
        var pm = GetNodeOrNull<PauseMenu>("/root/PauseMenu");
        if (pm == null || pm.IsOpen) return;
        pm.Call("Open");
    }

    // ボタン列の描画（HudCanvas＝最前面。設計座標）。ボックスが出ている間だけ描く。
    private void DrawDialogToolbar(CanvasItem ci)
    {
        if (!DialogToolbarVisible) return;
        for (int i = 0; i < TbLabels.Length; i++)
        {
            bool on = i switch
            {
                TbAuto => AutoAdvance,
                TbSkip => SkipLatched || FastForwarding,   // 押しっぱなしの早送り中も点ける（旧「▶▶」チップの代わり）
                _ => false,
            };
            DrawToolbarButton(ci, ToolbarRect(i), TbToken(i), TbLabels[i], on, _tbHover == i);
        }
    }

    private static void DrawToolbarButton(CanvasItem ci, Rect2 r, string tok, string label, bool on, bool hover)
    {
        var baseBg = new Color(0.06f, 0.05f, 0.10f);
        Color bg = on ? baseBg.Lerp(UiKit.Info, 0.34f) : baseBg;
        if (hover) bg = bg.Lerp(Colors.White, 0.10f);
        bg.A = 0.94f;
        Color border = on ? new Color(UiKit.Info, 0.95f) : new Color(hover ? UiKit.Text3 : UiKit.Text4, hover ? 0.9f : 0.6f);
        Color ink = on ? UiKit.PurifyHi : hover ? UiKit.Text2 : UiKit.Text3;
        UiKit.Box(ci, r, bg, 6f, border, 1f);

        // キー枠（ボタン左端の小さなキャップ）。ON の間は塗りつぶして反転させる。
        float keyW = TbKeyW(tok), keyH = TbH - 8f;
        var kr = new Rect2(r.Position.X + 3f, r.Position.Y + 4f, keyW, keyH);
        UiKit.Box(ci, kr, on ? new Color(UiKit.Info, 0.92f) : new Color(1f, 1f, 1f, 0.05f), 4f,
            on ? null : new Color(ink, 0.55f), 1f);
        // 英大文字の見た目の中心（ベースラインから字高の約 0.36 上）をボタンの縦中央に合わせる。
        float capTop = r.Position.Y + TbH / 2f + UiKit.FontSmall * 0.36f - UiKit.ZenBold.GetAscent(UiKit.FontSmall);
        UiKit.Draw(ci, UiKit.SmallLabel, new Vector2(kr.Position.X + 5f, capTop), tok, on ? UiKit.BgDeep : ink);
        UiKit.Draw(ci, TbLabelStyle, new Vector2(kr.End.X + 7f, capTop), label, ink);
    }
}
