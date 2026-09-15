using Godot;

public partial class TitleMenu : Node2D
{
    private GameManager _game = null!;

    private enum Item { NewGame, Continue, Tutorial, Settings }
    private static readonly (Item item, string jp)[] AllItems =
    {
        (Item.NewGame,   "はじめから"),
        (Item.Continue,  "つづきから"),
        (Item.Tutorial,  "チュートリアル"),
        (Item.Settings,  "設定"),
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

    private static readonly Color Ink = new("111d21");
    private static readonly Color Paper = new("fff8f2");
    private static readonly Color Muted = new("d3dfdf");
    private static readonly Color Accent = new("f4c3a9");
    private static readonly Color Disabled = new("9daaaa");

    public override void _Ready()
    {
        _game = GetNode<GameManager>("/root/Game");
        BuildKeyVisual();
        _rng.Randomize();
        Audio.Instance?.Music(Audio.Instance.BgmTitle);
        _hasSave = _game.SlotExists(0) || _game.SlotExists(1) || _game.SlotExists(2) || _game.SlotExists(3);
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg == "--demo" || arg == "--qa") _autoplay = true;
        _sel = _hasSave ? 1 : 0;
        _talk = BootTalk[_rng.RandiRange(0, BootTalk.Length - 1)];
        _talkT = TalkShowSec;
    }

    private void BuildKeyVisual()
    {
        var texture = GD.Load<Texture2D>("res://char/bg2/title/title_mina_v2.png");
        float cover = Mathf.Max(UiKit.DesignW / texture.GetWidth(), UiKit.DesignH / texture.GetHeight());
        // 顔のモーフは使わず、一枚絵の表情と輪郭を保つ。
        AddChild(new Sprite2D
        {
            Name = "TitleIllustration",
            Texture = texture,
            Position = new Vector2(UiKit.DesignW, UiKit.DesignH) * (UiKit.Scale / 2),
            Scale = Vector2.One * cover * UiKit.Scale,
            ZIndex = -10,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        });
    }

    public override void _Process(double delta)
    {
        _t += delta;
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

    private static Rect2 MenuRowRect(int i) => new(72, 334 + i * 49, 350, 44);

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
        UiKit.HGradient(this, new Rect2(0, 0, 650, UiKit.DesignH),
            new Color(Ink, 0.86f), new Color(Ink, 0));
        UiKit.VGradient(this, new Rect2(0, 570, UiKit.DesignW, 150),
            new[] { new Color(Ink, 0), new Color(Ink, 0.68f), new Color(Ink, 0.94f) }, new[] { 0f, 0.46f, 1f });

        DrawTitleBlock();
        DrawMenu();
        DrawTalk();
        UiKit.Text(this, UiKit.Mono, new Vector2(72, 686), "ver 0.3.0", UiKit.FontSmall, Muted);
        DrawToast();
        if (_picking) DrawSlotPicker();
        UiKit.EndDesign(this);
    }

    private void DrawTitleBlock()
    {
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(74, 135), "Refrain", 82, new Color(Ink, 0.8f));
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(72, 133), "Refrain", 82, Paper);
        DrawLine(new Vector2(76, 255), new Vector2(120, 255), Accent, 2, true);
    }

    private void DrawMenu()
    {
        for (int i = 0; i < Items.Length; i++)
        {
            Rect2 row = MenuRowRect(i);
            bool on = i == _sel;
            bool disabled = Items[i].item == Item.Continue && !_hasSave;
            if (on)
            {
                UiKit.HGradient(this, row, new Color(Accent, 0.17f), new Color(Accent, 0));
                DrawLine(row.Position + new Vector2(1, 12), row.Position + new Vector2(1, 32), Accent, 3, true);
                DrawLine(row.Position + new Vector2(24, 43), row.Position + new Vector2(240, 43), new Color(Accent, 0.65f), 1, true);
            }
            var font = on ? UiKit.ZenBlack : UiKit.ZenBold;
            UiKit.Text(this, font, row.Position + new Vector2(24, 5), Items[i].jp, 23,
                disabled ? Disabled : on ? Paper : Muted);
        }
    }

    private void DrawSlotPicker()
    {
        DrawRect(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH), new Color(0, 0, 0, 0.65f));
        int n = GameManager.SlotCount + 1;
        float w = 560, h = 100 + n * 56, x = (UiKit.DesignW - w) / 2, y = (UiKit.DesignH - h) / 2;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(Ink, 0.98f), 8, new Color(Accent, 0.6f), 1);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 28, y + 22), "つづきから", UiKit.FontHeading, Paper);
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
            UiKit.Text(this, UiKit.ZenBold, row.Position + new Vector2(18, 10),
                i == 0 ? "オートセーブ" : $"スロット {i}", UiKit.FontSpeaker, exists ? Paper : Disabled);
            UiKit.Text(this, UiKit.Zen, row.Position + new Vector2(300, 13),
                exists ? "セーブあり" : "空き", UiKit.FontBody, exists ? Accent : Disabled, HorizontalAlignment.Right, 184);
        }
    }

    private void DrawTalk()
    {
        if (_talkT <= 0 || string.IsNullOrEmpty(_talk)) return;
        float alpha = Mathf.Min(1, (float)_talkT / 0.6f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(640, 615), "ミナ", UiKit.FontLabel, new Color(Accent, alpha));
        UiKit.Multi(this, UiKit.ZenBold, new Vector2(640, 641), _talk, UiKit.FontBody, new Color(Paper, alpha), 568);
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
