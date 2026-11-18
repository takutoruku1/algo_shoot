using Godot;

public partial class TitleMenu : Node2D
{
    private GameManager _game = null!;

    private enum Item { NewGame, Continue, Tutorial, Settings, Credits }
    // 「クレジット」は設定の下＝据え置き機のタイトルメニューと同じ並び（遊ぶ項目→設定→表記）。
    //   BGM 素材の表記義務（MusMus / PeriTune CC BY 4.0）を果たす唯一の導線なので、常に出す
    //   （チュートリアルのような出し分けはしない）。
    private static readonly (Item item, string jp)[] AllItems =
    {
        (Item.NewGame,   "はじめから"),
        (Item.Continue,  "つづきから"),
        (Item.Tutorial,  "チュートリアル"),
        (Item.Settings,  "設定"),
        (Item.Credits,   "クレジット"),
    };
    private static readonly (Item item, string jp)[] Items =
        System.Array.FindAll(AllItems, e => GameManager.TutorialEnabled || e.item != Item.Tutorial);

    private static readonly string[] BootTalk =
    { "……おはようございます。今日も、来てくださったんですね。", "起動、確認しました。ご主人様、お加減はいかがです。", "はい、ミナです。今日も、おそばにおります。", "また来たんですか。……よろこんでますよ、わたくし。", "準備はできております。ご一緒いたしますね。", "電源、ちゃんと落として寝ましたか。……疑っております。" };

    private static readonly string[] IdleTalk =
    { "……お迷いですか。急がなくて結構ですよ。", "眺めているだけの時間も、悪くありませんね。", "……こんなに静かな夜も、あるんですね。", "もう少し、ここにいましょうか。", "…………まだ、いらっしゃいますか。", "画面の前で固まらないでください。心配になります。", "ここは、はじまりの前です。何度でも、ここに戻ってこられます。", "お茶でも淹れてきては。冷める前に戻ってきてくださいね。" };

    private int _sel, _pick;
    private bool _navHeld, _zHeld, _backHeld, _hasSave, _picking;
    private GameManager.SaveSlotInfo[] _slotInfos = System.Array.Empty<GameManager.SaveSlotInfo>();
    private double _t, _toastT;
    private string _toast = "";
    private bool _autoplay, _dived;
    private const double TalkShowSec = 6.0;
    private const double IdleTalkSec = 10.0;
    private string _talk = "";
    private double _talkT, _idleTimer;
    private bool _idleTalkFired;
    private readonly RandomNumberGenerator _rng = new();
    private FontFile _titleFont = null!, _menuFont = null!, _talkFont = null!;
    private TitleVisuals _visuals = null!;
    private float _selectionY, _selectionWidth;
    private Transform2D _illustrationTransform;
    private readonly Vector2[] _messageThread = new Vector2[49];
    private Vector2 _parallax;

    private static readonly Color Ink = new("15151e");
    private static readonly Color Paper = new("fff9f5");
    private static readonly Color Muted = new("d7d9e2");
    private static readonly Color Accent = new("f5b6ab");
    private static readonly Color Disabled = new("91929e");
    private static readonly Color[] ShardColors =
    {
        new("bde9f2"), new("ffc6a3"), new("b4ddd0"), new("e5c7eb"),
    };

    public override void _Ready()
    {
        _game = GetNode<GameManager>("/root/Game");
        TextureFilter = TextureFilterEnum.Linear;
        // タイトルを眺めている間に、会話本文のかな・英数・約物の字形を作っておく（GlyphWarmer 冒頭のコメント）。
        GlyphWarmer.WarmBase(this);
        _titleFont = (FontFile)GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-Italic.ttf").Duplicate();
        _titleFont.Oversampling = 2;
        _titleFont.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        _menuFont = (FontFile)GD.Load<FontFile>("res://assets/fonts/ZenKakuGothicNew-Regular.ttf").Duplicate();
        _menuFont.Oversampling = 2;
        _menuFont.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        _talkFont = (FontFile)GD.Load<FontFile>("res://assets/fonts/ShipporiMincho-SemiBold.ttf").Duplicate();
        _talkFont.Oversampling = 2;
        _talkFont.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        _visuals = new TitleVisuals { Name = "TitleVisuals", LogoWidth = UiKit.TextW(_titleFont, "Refrain", 176) };
        AddChild(_visuals);
        UpdateIllustration(0);
        _rng.Randomize();
        Audio.Instance?.Music(Audio.Instance.BgmTitle);
        RefreshSlotInfos();
        _hasSave = _game.SlotExists(0) || _game.SlotExists(1) || _game.SlotExists(2) || _game.SlotExists(3);
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg == "--demo" || arg == "--qa") _autoplay = true;
        _sel = _hasSave ? 1 : 0;
        _selectionY = MenuRowRect(_sel).GetCenter().Y;
        _selectionWidth = UiKit.TrackedW(_menuFont, Items[_sel].jp, 25, 4);
        _talk = BootTalk[_rng.RandiRange(0, BootTalk.Length - 1)];
        _talkT = TalkShowSec;
    }

    private void UpdateIllustration(double delta)
    {
        float time = (float)_t;
        Vector2 target = Vector2.Zero;
        if (Pad.UsingMouse && !_picking)
        {
            var mouse = Pad.MousePos();
            target = new Vector2(Mathf.Clamp(mouse.X / 640 - 1, -1, 1) * -3.5f,
                Mathf.Clamp(mouse.Y / 360 - 1, -1, 1) * -2f);
        }
        _parallax = _parallax.Lerp(target, 1 - Mathf.Exp(-2.2f * (float)delta));
        // Top anchoring protects the headdress while overscan covers the moving edges.
        float zoom = 1.034f + 0.018f * (1 - Reveal(0, 4.2f)) + 0.002f * Mathf.Sin(time * 0.23f);
        Vector2 drift = new(Mathf.Sin(time * 0.19f) * 2.8f, Mathf.Sin(time * 0.27f) * 0.8f);
        float cover = Mathf.Max(UiKit.DesignW / _visuals.ArtSize.X, UiKit.DesignH / _visuals.ArtSize.Y);
        _illustrationTransform = new Transform2D(0, Vector2.One * cover * zoom, 0,
            new Vector2(640, 360 * zoom - 3.5f) + drift + _parallax * new Vector2(1, .55f));
        UpdatePostMotion(time);
        _visuals.AnimateMina(time);
    }

    private void UpdatePostMotion(float time) => _visuals.Update(_illustrationTransform, time, _parallax);

    public override void _Process(double delta)
    {
        _t += delta;
        UpdateIllustration(delta);
        _selectionY = Mathf.Lerp(_selectionY, MenuRowRect(_sel).GetCenter().Y, 1 - Mathf.Exp(-18 * (float)delta));
        _selectionWidth = Mathf.Lerp(_selectionWidth, UiKit.TrackedW(_menuFont, Items[_sel].jp, 25, 4),
            1 - Mathf.Exp(-18 * (float)delta));
        if (_toastT > 0) _toastT -= delta;
        if (_talkT > 0) _talkT -= delta;
        if (_dived) { QueueRedraw(); return; }
        if (_autoplay) { if (_t > 0.3) Go("res://Hub.tscn"); QueueRedraw(); return; }
        // 操作説明オーバーレイが開いている間／閉じた直後フレーム(UiBlocked)はタイトル側の入力を止める
        //（閉じた Z/X/Esc の同じ押下が決定/戻るとして二重処理されないよう、既押し扱いで食う）。
        if (GetNodeOrNull<HowToPlay>("/root/HowTo") is { IsOpen: true } || Pad.UiBlocked(this))
        {
            _zHeld = _backHeld = _navHeld = true;
            QueueRedraw();
            return;
        }

        // マウス：フレーム頭でホットスポットをクリア（このシーンがアクティブな時だけ登録＝競合しない）。
        // ポーズは全画面で開くがタイトルでは CanOpenHere=false で開かないため、ここが常に唯一の登録者。
        UiKit.BeginHotspots(Pad.MousePos());
        bool click = Pad.MouseClick();

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B)
                    || Pad.MouseRightClick(); // 右クリック＝もどる/キャンセル
        bool backEdge = back && !_backHeld;

        // 小話5：無操作検知（上下/左右/決定/戻る/クリックのいずれかで即リセット＝メニュー操作の邪魔をしない）。
        //   放置が IdleTalkSec 続いたら一度だけ発火。連呼を防ぐため、操作が戻って再びリセットされるまで再発火しない。
        bool navAny = Input.IsActionPressed("ui_up") || Input.IsActionPressed("ui_down")
            || Input.IsActionPressed("ui_left") || Input.IsActionPressed("ui_right");
        if (navAny || z || back || click)
        {
            _idleTimer = 0;
            _idleTalkFired = false;
        }
        else
        {
            _idleTimer += delta;
            if (_idleTimer >= IdleTalkSec && !_idleTalkFired)
            {
                _idleTalkFired = true;
                _talk = IdleTalk[_rng.RandiRange(0, IdleTalk.Length - 1)];
                _talkT = TalkShowSec;
            }
        }

        // 「つづきから」スロット選択中：↑↓で選び Z=ロード / X=やめる（0=オートセーブ）
        if (_picking)
        {
            int n = GameManager.SlotCount + 1; // 0=オート + 1..3=手動
            bool pu = Input.IsActionPressed("ui_up"), pd = Input.IsActionPressed("ui_down");
            if ((pu || pd) && !_navHeld)
            {
                if (pu) _pick = (_pick + n - 1) % n;
                if (pd) _pick = (_pick + 1) % n;
                Audio.Instance?.PlayUiMove();
            }
            _navHeld = pu || pd;
            // マウス：スロット行にホバー＝カーソル移動、クリック＝ロード（存在するスロットのみ）。
            for (int i = 0; i < n; i++) UiKit.Hotspot(SlotPickerRowRect(i, n), i);
            UiKit.Hotspot(SlotPickerCloseRect(n), n);
            int hov = UiKit.HoveredId();
            if (Pad.UsingMouse && hov >= 0 && hov < n && hov != _pick) { _pick = hov; Audio.Instance?.PlayUiMove(); }
            int clk = UiKit.ClickedId(click);
            if (clk == n)
            {
                Audio.Instance?.PlayUiCancel();
                _picking = false;
                _zHeld = z; _backHeld = back;
                QueueRedraw();
                return;
            }
            if (clk >= 0) _pick = clk;
            bool loadNow = (zEdge || clk >= 0) && _game.SlotExists(_pick);
            if (loadNow)
            {
                if (_game.LoadFromSlot(_pick)) { Audio.Instance?.PlayUiConfirm(); Go("res://Hub.tscn"); }
                else { Audio.Instance?.PlayUiDeny(); Toast("セーブデータを読み込めませんでした"); }
            }
            else if (zEdge || clk >= 0) Audio.Instance?.PlayUiDeny();
            else if (backEdge) { Audio.Instance?.PlayUiCancel(); _picking = false; }
            _zHeld = z; _backHeld = back;
            QueueRedraw();
            return;
        }

        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld)
        {
            if (up) _sel = (_sel - 1 + Items.Length) % Items.Length;
            if (down) _sel = (_sel + 1) % Items.Length;
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = up || down;

        // マウス：メニュー行にホバー＝カーソル移動（KBカーソルと同じハイライト）、クリック＝決定。
        for (int i = 0; i < Items.Length; i++) UiKit.Hotspot(MenuRowRect(i), i);
        int mhov = UiKit.HoveredId();
        if (Pad.UsingMouse && mhov >= 0 && mhov != _sel) { _sel = mhov; Audio.Instance?.PlayUiMove(); }
        int mclk = UiKit.ClickedId(click);
        if (mclk >= 0 && _t > 0.2) { _sel = mclk; Audio.Instance?.PlayUiConfirm(); Confirm(); }
        else if (zEdge && _t > 0.2) { Audio.Instance?.PlayUiConfirm(); Confirm(); }
        _zHeld = z; _backHeld = back;

        QueueRedraw();
    }

    internal static Rect2 MenuRowRect(int i) => new(96, 360 + i * 61, 332, 47);

    private static Rect2 SlotPickerRowRect(int i, int n) => SaveSlotView.RowRect(i, n);

    private static Rect2 SlotPickerCloseRect(int n) => SaveSlotView.CloseRect;

    private void Confirm()
    {
        switch (Items[_sel].item)
        {
            case Item.NewGame:
                _game.ResetPersistent();
                Go("res://Prologue.tscn");
                break;
            case Item.Continue:
                if (_hasSave)
                {
                    RefreshSlotInfos();
                    _picking = true;
                    _pick = FirstSlot();
                }
                else Toast("セーブデータがありません");
                break;
            case Item.Tutorial:
                // 「チュートリアル」＝独立ステージ0（完全チュートリアル）を再生（既読フラグは変えない）。
                // DiffSelect を通らないので難易度は Easy に固定（直前の選択を引き継がせない）。
                // ※練習面の非表示中(GameManager.TutorialEnabled==false)は Items から落ちるのでここには来ない。
                _game.Difficulty = GameManager.Diff.Easy;
                Go("res://Stage0.tscn");
                break;
            case Item.Settings: Go("res://Settings.tscn"); break;
            // クレジット＝素材表記の画面（X/Esc・右クリックでここへ戻る）。専用曲「帰り道」は Credits 側で鳴る。
            case Item.Credits: Go("res://Credits.tscn"); break;
        }
    }

    private void Toast(string msg) { _toast = msg; _toastT = 2.0; }
    private void Go(string scene) { if (_dived) return; _dived = true; GameManager.FadeToScene(this, scene); }
    private int FirstSlot()
    {
        for (int i = 0; i <= GameManager.SlotCount; i++) // 0(オート)..3
            if (_game.SlotExists(i)) return i;
        return 0;
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        _visuals.Draw(this);
        DrawMessageThread();
        UiKit.HGradient(this, new Rect2(0, 0, 900, UiKit.DesignH),
            new Color(Ink, 0.92f), new Color(Ink, 0));
        UiKit.VGradient(this, new Rect2(0, 545, UiKit.DesignW, 175),
            new[] { new Color(Ink, 0), new Color(Ink, 0.62f), new Color(Ink, 0.88f) }, new[] { 0f, 0.58f, 1f });

        DrawShards();
        DrawTitleBlock();
        DrawMenu();
        DrawTalk();
        UiKit.Text(this, UiKit.Mono, new Vector2(98, 679), $"ver {Startup.ReleaseVersion}", UiKit.FontSmall, new Color(Muted, 0.7f * Reveal(0.6f, 0.7f)));
        DrawRect(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH), new Color(Ink, 0.8f * (1 - Reveal(0, 0.9f))));
        if (_picking) DrawSlotPicker();
        DrawToast();
        UiKit.EndDesign(this);
    }

    private void DrawMessageThread()
    {
        Vector2 start = _visuals.ThreadStart;
        Vector2 end = _illustrationTransform * (new Vector2(871, 619) - _visuals.ArtSize / 2);
        Vector2 span = end - start;
        Vector2 first = start + span * 0.34f + new Vector2(0, -14);
        Vector2 second = end - span * 0.32f + new Vector2(0, 12);
        float time = (float)_t;
        for (int i = 0; i < _messageThread.Length; i++)
        {
            float p = i / (float)(_messageThread.Length - 1), q = 1 - p;
            _messageThread[i] = q * q * q * start + 3 * q * q * p * first
                + 3 * q * p * p * second + p * p * p * end
                + new Vector2(0, Mathf.Sin(p * Mathf.Tau * 1.5f - time * 0.7f) * Mathf.Sin(p * Mathf.Pi) * 3);
        }
        var gold = new Color("f5d4a0");
        DrawPolyline(_messageThread, new Color(gold, 0.07f), 6, true);
        DrawPolyline(_messageThread, new Color(gold, 0.14f), 3, true);
        DrawPolyline(_messageThread, new Color(gold, 0.52f), 1, true);
        float travel = Mathf.PosMod(time / 6.5f, 1);
        for (int i = 1; i < _messageThread.Length; i++)
        {
            float p = i / (float)(_messageThread.Length - 1);
            float light = Mathf.Exp(-Mathf.Pow((p - travel) / 0.09f, 2)) * Mathf.Sin(travel * Mathf.Pi);
            DrawLine(_messageThread[i - 1], _messageThread[i], new Color(1, 0.94f, 0.78f, light * 0.85f), 1.8f, true);
        }
        for (int i = 0; i < 7; i++)
        {
            float p = Mathf.PosMod(time * 0.15f + i * 0.137f, 1);
            float index = p * (_messageThread.Length - 1);
            int at = Mathf.Min((int)index, _messageThread.Length - 2);
            Vector2 point = _messageThread[at].Lerp(_messageThread[at + 1], index - at)
                + new Vector2(0, Mathf.Sin(time * 0.6f + i * 2.1f) * 9 * Mathf.Sin(p * Mathf.Pi));
            DrawLine(point, point + new Vector2(2, -1), new Color(gold, Mathf.Sin(p * Mathf.Pi) * 0.55f), 1, true);
        }
    }

    private void DrawTitleBlock()
    {
        float reveal = Reveal(.12f, 1.25f);
        _visuals.DrawLogo(this, reveal);
        float width = _visuals.LogoWidth;
        float subtitleAlpha = Reveal(.4f, 1.1f);
        float subtitleWidth = UiKit.TrackedW(_menuFont, "リフレイン", 16, 7);
        float subtitleX = 88 + (width - subtitleWidth) / 2;
        UiKit.Tracked(this, _menuFont, new Vector2(subtitleX, 320), "リフレイン", 16,
            new Color(Muted, subtitleAlpha * .8f), 7);
        var silver = new Color("c5dee7");
        DrawLine(new Vector2(102, 332), new Vector2(subtitleX - 22, 332),
            new Color(silver, subtitleAlpha * .2f), 1, true);
        DrawLine(new Vector2(subtitleX + subtitleWidth + 22, 332), new Vector2(88 + width - 14, 332),
            new Color(silver, subtitleAlpha * .2f), 1, true);
    }

    private void DrawMenu()
    {
        float alpha = Reveal(.25f, .7f);
        float lineY = _selectionY + 19.5f;
        var silver = new Color("c5dee7");
        UiKit.HGradient(this, new Rect2(129, lineY, _selectionWidth, 1),
            new Color(silver, alpha * .75f), new Color(silver, alpha * .16f));
        for (int i = 0; i < Items.Length; i++)
        {
            Rect2 row = MenuRowRect(i);
            bool on = i == _sel;
            bool disabled = Items[i].item == Item.Continue && !_hasSave;
            float reveal = Reveal(.18f + i * .07f, .65f);
            Color color = disabled ? Disabled : on ? Paper : Muted;
            Vector2 label = row.Position + new Vector2(33, 7);
            UiKit.Tracked(this, _menuFont, label, Items[i].jp, 25, new Color(color, reveal), 4);
        }
    }
    private float Reveal(float delay, float duration)
    {
        float p = Mathf.Clamp(((float)_t - delay) / duration, 0, 1);
        return 1 - Mathf.Pow(1 - p, 3);
    }

    private void DrawDiamond(Vector2 center, float halfWidth, float halfHeight, Color color)
    {
        DrawColoredPolygon(new[] { center + new Vector2(0, -halfHeight), center + new Vector2(halfWidth, 0),
            center + new Vector2(0, halfHeight), center + new Vector2(-halfWidth, 0) }, color);
    }

    private void DrawShards()
    {
        for (int i = 0; i < 14; i++)
        {
            float phase = Mathf.PosMod((float)_t * (0.035f + i % 3 * 0.006f) + i * 0.137f, 1);
            float x = 28 + (i * 97 % 605) + Mathf.Sin(phase * 4 + i) * 12;
            float y = 744 - phase * 580;
            float alpha = Mathf.Sin(phase * Mathf.Pi) * 0.24f * Reveal(0.45f, 1.1f);
            DrawDiamond(new Vector2(x, y), 1.2f + i % 2 * 0.6f, 2.5f + i % 3, new Color(ShardColors[i % 4], alpha));
        }
    }

    private void DrawSlotPicker()
    {
        SaveSlotView.Draw(this, _slotInfos, 0, _pick, false,
            Pad.UsingMouse && SaveSlotView.CloseRect.HasPoint(Pad.MousePos()));
    }

    private void RefreshSlotInfos()
    {
        _slotInfos = new GameManager.SaveSlotInfo[GameManager.SlotCount + 1];
        for (int i = 0; i < _slotInfos.Length; i++) _slotInfos[i] = _game.ReadSlotInfo(i);
    }

    private void DrawTalk()
    {
        if (_talkT <= 0 || string.IsNullOrEmpty(_talk)) return;
        float alpha = Mathf.Min(1, (float)_talkT / 0.6f) * Reveal(0.8f, 0.8f);
        DrawLine(new Vector2(642, 613), new Vector2(671, 613), new Color(Accent, alpha * 0.65f), 1, true);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(683, 600), "ミナ", UiKit.FontLabel, new Color(Accent, alpha));
        UiKit.Multi(this, _talkFont, new Vector2(642, 632), _talk, 19, new Color(Paper, alpha), 542);
    }

    private void DrawToast()
    {
        if (_toastT <= 0) return;
        float w = UiKit.TextW(UiKit.ZenBold, _toast, UiKit.FontBody) + 48;
        float x = (UiKit.DesignW - w) / 2;
        UiKit.Box(this, new Rect2(x, 34, w, 44), new Color(Ink, 0.96f), 6, new Color(Accent, 0.7f), 1);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, 45), _toast, UiKit.FontBody, Paper, HorizontalAlignment.Center, w);
    }
}
