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
    {
        "……おはようございます。今日も、来てくださったんですね。",
        "起動、確認しました。ご主人様、お加減はいかがです。",
        "はい、ミナです。今日も、おそばにおります。",
        "また来たんですか。……よろこんでますよ、わたくし。",
        "準備はできております。ご一緒いたしますね。",
        "電源、ちゃんと落として寝ましたか。……疑っております。",
    };

    private static readonly string[] IdleTalk =
    {
        "……お迷いですか。急がなくて結構ですよ。",
        "眺めているだけの時間も、悪くありませんね。",
        "……こんなに静かな夜も、あるんですね。",
        "もう少し、ここにいましょうか。",
        "…………まだ、いらっしゃいますか。",
        "画面の前で固まらないでください。心配になります。",
        "ここは、はじまりの前です。何度でも、ここに戻ってこられます。",
        "お茶でも淹れてきては。冷める前に戻ってきてくださいね。",
    };

    private int _sel, _pick;
    private bool _navHeld, _zHeld, _backHeld, _hasSave, _picking;
    private double _t, _toastT;
    private string _toast = "";
    private bool _autoplay, _dived;
    private const double TalkShowSec = 6.0;
    private const double IdleTalkSec = 10.0;
    private string _talk = "";
    private double _talkT, _idleTimer;
    private bool _idleTalkFired;
    private readonly RandomNumberGenerator _rng = new();
    private FontFile _titleFont = null!, _menuFont = null!;
    private float _selectionY;
    private Sprite2D _illustration = null!;
    private ShaderMaterial _illustrationMaterial = null!;
    private float _illustrationScale;
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
        _titleFont = (FontFile)GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-Italic.ttf").Duplicate();
        _menuFont = (FontFile)GD.Load<FontFile>("res://assets/fonts/ShipporiMincho-SemiBold.ttf").Duplicate();
        foreach (var font in new[] { _titleFont, _menuFont })
        {
            font.Oversampling = 2;
            font.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        }
        BuildKeyVisual();
        _rng.Randomize();
        Audio.Instance?.Music(Audio.Instance.BgmTitle);
        _hasSave = _game.SlotExists(0) || _game.SlotExists(1) || _game.SlotExists(2) || _game.SlotExists(3);
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg == "--demo" || arg == "--qa") _autoplay = true;
        _sel = _hasSave ? 1 : 0;
        _selectionY = MenuRowRect(_sel).GetCenter().Y;
        _talk = BootTalk[_rng.RandiRange(0, BootTalk.Length - 1)];
        _talkT = TalkShowSec;
    }

    private void BuildKeyVisual()
    {
        var texture = GD.Load<Texture2D>("res://char/bg2/title/title_mina_v2.png");
        float cover = Mathf.Max(UiKit.DesignW / texture.GetWidth(), UiKit.DesignH / texture.GetHeight());
        _illustrationScale = cover * UiKit.Scale;
        _illustrationMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/title_kv.gdshader") };
        _illustration = new Sprite2D
        {
            Name = "TitleIllustration",
            Texture = texture,
            Material = _illustrationMaterial,
            ZIndex = -10,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        };
        AddChild(_illustration);
        UpdateIllustration(0);
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
        _illustration.Position = (new Vector2(640, 360 * zoom - 3.5f) + drift + _parallax * new Vector2(1, 0.55f)) * UiKit.Scale;
        _illustration.Scale = Vector2.One * _illustrationScale * zoom;
        _illustrationMaterial.SetShaderParameter("time_sec", time);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        UpdateIllustration(delta);
        _selectionY = Mathf.Lerp(_selectionY, MenuRowRect(_sel).GetCenter().Y, 1 - Mathf.Exp(-18 * (float)delta));
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
            if (loadNow) { Audio.Instance?.PlayUiConfirm(); _game.LoadFromSlot(_pick); Go("res://Hub.tscn"); }
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

    private static Rect2 MenuRowRect(int i) => new(96, 355 + i * 53, 344, 47);

    private static Rect2 SlotPickerRowRect(int i, int n)
    {
        float h = 100 + n * 56;
        return new Rect2(388, (UiKit.DesignH - h) / 2 + 64 + i * 56, 504, 46);
    }

    private static Rect2 SlotPickerCloseRect(int n) => new(864, (UiKit.DesignH - 100 - n * 56) / 2 + 10, 44, 44);

    private void Confirm()
    {
        switch (Items[_sel].item)
        {
            case Item.NewGame:
                // はじめから＝まっさらスタートしてそのままプロローグへ（操作表示の3択は 2026-09-13 に削除）。
                _game.ResetPersistent();
                Go("res://Prologue.tscn");
                break;
            case Item.Continue:
                if (_hasSave) { _picking = true; _pick = FirstSlot(); }
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
    private void Go(string scene) { if (_dived) return; _dived = true; GetTree().ChangeSceneToFile(scene); }
    private int FirstSlot()
    {
        for (int i = 0; i <= GameManager.SlotCount; i++) // 0(オート)..3
            if (_game.SlotExists(i)) return i;
        return 0;
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        UiKit.HGradient(this, new Rect2(0, 0, 750, UiKit.DesignH),
            new Color(Ink, 0.8f), new Color(Ink, 0));
        UiKit.VGradient(this, new Rect2(0, 545, UiKit.DesignW, 175),
            new[] { new Color(Ink, 0), new Color(Ink, 0.62f), new Color(Ink, 0.88f) }, new[] { 0f, 0.58f, 1f });

        DrawShards();
        DrawTitleBlock();
        DrawMenu();
        DrawTalk();
        UiKit.Text(this, UiKit.Mono, new Vector2(98, 679), "ver 2.017", UiKit.FontSmall, new Color(Muted, 0.7f * Reveal(0.6f, 0.7f)));
        DrawRect(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH), new Color(Ink, 0.8f * (1 - Reveal(0, 0.9f))));
        DrawToast();
        if (_picking) DrawSlotPicker();
        UiKit.EndDesign(this);
    }

    private void DrawTitleBlock()
    {
        float reveal = Reveal(0.12f, 1.25f);
        float offset = 14 * (1 - reveal);
        var position = new Vector2(88, 114 + offset);
        UiKit.Text(this, _titleFont, position + new Vector2(1, 3), "Refrain", 184, new Color(Ink, 0.3f * reveal));
        UiKit.Text(this, _titleFont, position, "Refrain", 184, new Color(Paper, reveal));

        float trace = Reveal(0.38f, 1.4f);
        DrawLine(new Vector2(102, 113), new Vector2(134, 113), new Color(Paper, 0.5f * trace), 1, true);
        for (int i = 0; i < ShardColors.Length; i++)
            DrawDiamond(new Vector2(149 + i * 17, 113), 3, 5, new Color(ShardColors[i], trace));
        DrawLine(new Vector2(215, 113), new Vector2(215 + 48 * trace, 113), new Color(Paper, 0.3f * trace), 1, true);

        float lineY = 308;
        UiKit.HGradient(this, new Rect2(102, lineY, 406 * trace, 1), new Color(Accent, trace * 0.9f), new Color(Paper, 0));
        DrawDiamond(new Vector2(98, lineY + 0.5f), 3, 4, new Color(Accent, trace));
        float gleam = (float)(_t % 7) / 7;
        float alpha = Mathf.Sin(gleam * Mathf.Pi) * 0.5f * trace;
        DrawLine(new Vector2(102 + gleam * 380, lineY), new Vector2(114 + gleam * 380, lineY), new Color(Paper, alpha), 1, true);
    }

    private void DrawMenu()
    {
        float selectionAlpha = Reveal(0.2f, 0.65f);
        UiKit.HGradient(this, new Rect2(96, _selectionY - 23, 330, 46), new Color(Accent, 0.11f * selectionAlpha), new Color(Accent, 0));
        DrawDiamond(new Vector2(103, _selectionY), 4, 6, new Color(Accent, selectionAlpha));
        DrawLine(new Vector2(103, _selectionY - 16), new Vector2(103, _selectionY - 10), new Color(Accent, 0.6f * selectionAlpha), 1, true);
        DrawLine(new Vector2(103, _selectionY + 10), new Vector2(103, _selectionY + 16), new Color(Accent, 0.6f * selectionAlpha), 1, true);
        for (int i = 0; i < Items.Length; i++)
        {
            Rect2 row = MenuRowRect(i);
            bool on = i == _sel;
            bool disabled = Items[i].item == Item.Continue && !_hasSave;
            float reveal = Reveal(0.18f + i * 0.07f, 0.65f);
            Color color = disabled ? Disabled : on ? Paper : Muted;
            UiKit.Text(this, _menuFont, row.Position + new Vector2(33, 6), Items[i].jp, 27, new Color(color, reveal));
            if (on)
            {
                float width = UiKit.TextW(_menuFont, Items[i].jp, 27);
                UiKit.HGradient(this, new Rect2(row.Position.X + 33, row.End.Y - 3, 265, 1), new Color(Accent, reveal * 0.55f), new Color(Accent, 0));
                float x = row.Position.X + 55 + width;
                float y = row.GetCenter().Y + 1;
                DrawLine(new Vector2(x, y), new Vector2(x + 19, y), new Color(Accent, reveal), 1, true);
                DrawLine(new Vector2(x + 14, y - 4), new Vector2(x + 19, y), new Color(Accent, reveal), 1, true);
                DrawLine(new Vector2(x + 14, y + 4), new Vector2(x + 19, y), new Color(Accent, reveal), 1, true);
            }
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
            float alpha = Mathf.Sin(phase * Mathf.Pi) * 0.48f * Reveal(0.45f, 1.1f);
            DrawDiamond(new Vector2(x, y), 1.2f + i % 2 * 0.6f, 2.5f + i % 3, new Color(ShardColors[i % 4], alpha));
        }
    }

    private void DrawSlotPicker()
    {
        DrawRect(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH), new Color(0, 0, 0, 0.65f));
        int n = GameManager.SlotCount + 1;
        float w = 560, h = 100 + n * 56, x = (UiKit.DesignW - w) / 2, y = (UiKit.DesignH - h) / 2;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(Ink, 0.98f), 8, new Color(Accent, 0.6f), 1);
        UiKit.Text(this, _menuFont, new Vector2(x + 28, y + 18), "つづきから", 27, Paper);
        Rect2 close = SlotPickerCloseRect(n);
        Color closeColor = close.HasPoint(Pad.MousePos()) ? Accent : Muted;
        Vector2 c = close.GetCenter();
        DrawLine(c + new Vector2(-6, -6), c + new Vector2(6, 6), closeColor, 2, true);
        DrawLine(c + new Vector2(6, -6), c + new Vector2(-6, 6), closeColor, 2, true);
        for (int i = 0; i < n; i++)
        {
            Rect2 row = SlotPickerRowRect(i, n);
            bool on = i == _pick;
            bool exists = _game.SlotExists(i);
            if (on)
            {
                DrawRect(row, new Color(Accent, 0.12f));
                DrawLine(row.Position + new Vector2(1, 10), row.Position + new Vector2(1, 36), Accent, 2, true);
            }
            UiKit.Text(this, _menuFont, row.Position + new Vector2(18, 10),
                i == 0 ? "オートセーブ" : $"スロット {i}", 22, exists ? Paper : Disabled);
            UiKit.Text(this, UiKit.Zen, row.Position + new Vector2(300, 13),
                exists ? "セーブあり" : "空き", UiKit.FontBody, exists ? Accent : Disabled, HorizontalAlignment.Right, 184);
        }
    }

    private void DrawTalk()
    {
        if (_talkT <= 0 || string.IsNullOrEmpty(_talk)) return;
        float alpha = Mathf.Min(1, (float)_talkT / 0.6f) * Reveal(0.8f, 0.8f);
        DrawLine(new Vector2(642, 613), new Vector2(671, 613), new Color(Accent, alpha * 0.65f), 1, true);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(683, 600), "ミナ", UiKit.FontLabel, new Color(Accent, alpha));
        UiKit.Multi(this, _menuFont, new Vector2(642, 632), _talk, 19, new Color(Paper, alpha), 542);
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
