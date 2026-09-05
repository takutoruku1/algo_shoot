using Godot;

// TitleMenu : スタート画面。RefrainHTML/Refrain Title.dc.html を忠実移植（非ピクセル・滑らかUI）。
//   深い夜グラデ背景＋浮遊する言葉＋漂う弾＋光のオーブ＋グラデ大見出し＋シアン選択メニュー＋Xティッカー。
//   ↑↓ で選択・Z で決定。設計座標 1280×720 のまま UiKit.BeginDesign で描く。
public partial class TitleMenu : Node2D
{
    private GameManager _game = null!;

    private enum Item { NewGame, Continue, HowToPlay, Tutorial, Settings, Credits, Quit }
    private static readonly (Item item, string jp, string en)[] Items =
    {
        (Item.NewGame,   "はじめから",       "NEW GAME"),
        (Item.Continue,  "つづきから",       "CONTINUE"),
        (Item.HowToPlay, "あそびかた",       "HOW TO PLAY"),
        (Item.Tutorial,  "チュートリアル",   "TUTORIAL"),
        (Item.Settings,  "設定",             "SETTINGS"),
        (Item.Credits,   "クレジット",       "CREDITS"),
        (Item.Quit,      "おわる",           "QUIT"),
    };

    private static readonly (string h, string t)[] Ticker =
    {
        ("@rei_0w0", "ごめん、もう無理かも。"),
        ("@kako__",  "どうせ、とどかない"),
        ("@nobody_7","もういない"),
    };

    // ── ミナのひとこと（小話5）docs/小話集_v1.md §5。Ticker（投稿形式）とは別枠。画面下に薄く添えるだけで、
    //    メニュー操作には一切関与しない（Hotspot登録もクリック判定も無し）。
    private static readonly string[] BootTalk =
    {
        "……おはようございます。今日も、来てくださったんですね。",
        "起動、確認しました。ご主人様、お加減はいかがです。",
        "はい、ミナです。ご用でしたら、下からどうぞ。",
        "また来たんですか。……よろこんでますよ、わたくし。",
        "準備はできております。あとは、押すだけです。",
        "電源、ちゃんと落として寝ましたか。……疑っております。",
    };

    private static readonly string[] IdleTalk =
    {
        "……お迷いですか。急がなくて結構ですよ。",
        "眺めているだけの時間も、悪くありませんね。",
        "そこ、ずっと同じところですよ。矢印、ちゃんと動きます?",
        "決めかねているなら、上から順で構いません。",
        "…………まだ、いらっしゃいますか。",
        "画面の前で固まらないでください。心配になります。",
        "ここは、はじまりの前です。何度でも、ここに戻ってこられます。",
        "お茶でも淹れてきては。冷める前に戻ってきてくださいね。",
    };

    private int _sel;
    private bool _navHeld, _zHeld, _backHeld, _hasSave, _picking;
    private int _pick; // つづきから：選択中スロット(0..2)

    // 「はじめから」後の操作表示モード3択（毎回必ず通す）。
    private bool _choosingDisplay;
    private int _dispSel; // 0=キーボード / 1=コントローラ(PS) / 2=コントローラ(Xbox)
    private static readonly (Pad.DisplayMode mode, string jp, string en)[] DisplayChoices =
    {
        (Pad.DisplayMode.Keyboard,       "キーボード",          "KEYBOARD"),
        (Pad.DisplayMode.PadPlayStation, "コントローラ（PS）",  "GAMEPAD / PS"),
        (Pad.DisplayMode.PadXbox,        "コントローラ（Xbox）","GAMEPAD / XBOX"),
    };
    private double _t, _toastT;
    private string _toast = "";
    private bool _autoplay, _dived;

    // ── 小話5（タイトルのひとこと）の表示・トリガー状態 ──
    //   起動時に BootTalk から1つ、以後は無操作が続くたび IdleTalk から1つ。表示は DrawTalk() が画面下に薄く出す。
    private const double TalkShowSec = 6.0;   // 1回の表示秒数
    private const double IdleTalkSec = 10.0;  // これだけ無操作（上下/決定/戻る/クリック無し）が続いたら「放置」とみなす
    private string _talk = "";
    private double _talkT;
    private double _idleTimer;    // 無操作の継続秒数。メニュー操作のいずれかで0にリセット。
    private bool _idleTalkFired;  // このアイドル継続中に一度でも出したか（連呼防止。操作が戻るまで再発火しない）。

    // タイトルの背景＝bg2 の層（夜の街 L1_far ＋ 暖かい光 L4_light_warm ＋ ミナの立ち絵 mina_kv）。
    //   ★顔・表情は一切動かさない（過去に笑顔モーフ/合成が"不気味"と強NG）。
    //     よって各層は「完全静止の Sprite2D」を最背面に敷くだけ。
    //     モーフ・呼吸・髪なびき・パララックスは全廃。顔は1pxも動かない。
    //   「世界が動く」演出は _Draw 側で層の上に “流れる光の粒”（データの川）を
    //     加算で重ねて作る（顔矩形は除外＝顔に光は被せない）。継ぎ目リスクゼロ。
    //   層が読めなければ旧 KV（char/title_kv.png）の1枚絵へ落ちる＝画面が真っ黒にならない。
    private Texture2D? _kvTex;
    private Sprite2D? _kvSprite;
    private bool _hasBgLayers;   // bg2 の層を敷けたか（_Draw のフォールバック夜グラデを出すかの判定）

    // bg2 タイトル層のパスと敷き方。座標は設計座標(1280×720)で書き、置くときに UiKit.Scale(0.3) を掛ける
    // ＝ロゴ・メニューを描く _Draw（BeginDesign 下）と同じ座標系で位置を考えられる。
    //   L1_far        : 夜の街。無彩色寄りの素材なので藍 (0.42,0.48,0.80) を Modulate で掛ける。
    //   L4_light_warm : 上端中央の暖かい光。加算で足す（ロゴの背後がほのかに明るむ）。
    //   mina_kv       : 282×720 の立ち絵。ロゴとメニューは左半分なので右寄りに置く。
    //                   高さは画面の 0.94 倍に留めて 44px 下げる＝頭がティッカー帯(上端90px)に切られない。
    private const string TitleFarPath = "res://char/bg2/title/L1_far.png";
    private const string TitleWarmPath = "res://char/bg2/title/L4_light_warm.png";
    private const string TitleMinaPath = "res://char/bg2/title/mina_kv.png";
    //   Modulate は乗算なので、既に暗い夜景に藍 (0.42,0.48,0.80) をそのまま掛けると街灯まで潰れる。
    //   色相はそのままに最大チャンネルを 1.0 へ正規化して掛ける（AkariRoot の rainBlue と同じ作法）。
    private static readonly Color TitleFarTint = new Color(0.525f, 0.60f, 1.00f);
    // 立ち絵の左上と高さ（設計座標）。幅は高さ比で決まる（282×720 なら 0.94 倍で 265×677）。
    private static readonly Vector2 TitleMinaPos = new Vector2(880f, 44f);
    private const float TitleMinaH = UiKit.DesignH * 0.94f;

    // ── データの川を流れる光の粒（顔は不動・世界だけ動く）──
    //   KV の青いデータストリームは画面左下→右上奥の消失点へ斜行する。
    //   粒はその川筋に沿って奥（右上）へゆっくり吸い込まれる。速度は控えめ＝上品。
    //   流域は右側〜下縁に限定し、顔の矩形(FaceRect)には絶対に入れない。
    private struct LightMote
    {
        public Vector2 Pos;     // 設計座標(1280×720)
        public float Speed;     // px/s（川に沿う進行）
        public float Drift;     // 川筋からの直交ぶれ係数（束をばらす）
        public float Size;      // 芯半径(設計px)
        public float Phase;     // 明滅の位相
        public float Life, MaxLife; // フェードイン/アウト管理
        public Color Col;       // 芯色（Purify / PurifyHi / Light）
    }
    private LightMote[] _motes = System.Array.Empty<LightMote>();
    private LightLayer? _lightLayer;           // 加算合成で粒/グロウを描く子レイヤ
    private const int MoteCount = 22;          // 同時表示（控えめ）
    private static readonly Vector2 RiverDir = new Vector2(0.62f, -0.78f).Normalized(); // 左下→右上奥
    // 顔・キャラ主部の矩形（設計座標）。ここには粒・グロウを一切置かない。
    //   立ち絵 mina_kv を TitleMinaPos に置いた実寸(282×720)に合わせ、左右に 12px の余白を足して
    //   髪の輪郭にも粒が乗らないようにする。
    private static readonly Rect2 FaceRect = new Rect2(
        TitleMinaPos.X - 12f, TitleMinaPos.Y, 265f + 24f, TitleMinaH);
    // 金オーブのおおよその位置（立ち絵の手元あたり）。ここだけグロウを脈動させる。
    private static readonly Vector2 OrbPos = new Vector2(TitleMinaPos.X + 132f, UiKit.DesignH * 0.60f);
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        // bg2 の層で敷く。1枚も読めなければ旧 KV の1枚絵へ落ちる。
        if (!BuildBgLayers())
        {
            _kvTex = ResourceLoader.Exists("res://char/title_kv.png")
                ? ResourceLoader.Load<Texture2D>("res://char/title_kv.png") : null;
            BuildKvSprite();
        }
        InitMotes();
        BuildLightLayer();
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        _hasSave = _game.SlotExists(0) || _game.SlotExists(1) || _game.SlotExists(2) || _game.SlotExists(3);
        var uargs = OS.GetCmdlineUserArgs();
        for (int i = 0; i < uargs.Length; i++)
        {
            if (uargs[i] == "--demo" || uargs[i] == "--qa") _autoplay = true;
        }
        _sel = _hasSave ? 1 : 0;

        // 小話5：起動時に1つ、ミナのひとことを画面下へ薄く出す（_rng は InitMotes() で Randomize 済み）。
        _talk = BootTalk[_rng.RandiRange(0, BootTalk.Length - 1)];
        _talkT = TalkShowSec;
    }

    // bg2 のタイトル層を敷く（奥→手前に 夜の街 → 暖かい光（加算）→ ミナの立ち絵）。
    //   全層とも完全静止の Sprite2D（顔は1pxも動かない）。設計座標で置いて UiKit.Scale を掛ける。
    //   夜の街が読めなければ false を返し、呼び出し側が旧 KV の1枚絵へ落ちる。
    private bool BuildBgLayers()
    {
        if (!ResourceLoader.Exists(TitleFarPath)) return false;
        var far = ResourceLoader.Load<Texture2D>(TitleFarPath);
        if (far == null || far.GetHeight() <= 0) return false;
        float s = UiKit.Scale;

        // 夜の街：設計解像度いっぱいに引き伸ばし、藍で色掛けする。
        AddChild(new Sprite2D
        {
            Name = "BgFar", Texture = far, Centered = false,
            ZIndex = -12, ZAsRelative = false,
            Scale = new Vector2(UiKit.DesignW / far.GetWidth() * s, UiKit.DesignH / far.GetHeight() * s),
            Modulate = TitleFarTint,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        });

        // 暖かい光：加算で足す（ロゴの背後がほのかに明るむ）。
        var warm = ResourceLoader.Exists(TitleWarmPath) ? ResourceLoader.Load<Texture2D>(TitleWarmPath) : null;
        if (warm != null && warm.GetHeight() > 0)
            AddChild(new Sprite2D
            {
                Name = "BgWarm", Texture = warm, Centered = false,
                ZIndex = -11, ZAsRelative = false,
                Scale = new Vector2(UiKit.DesignW / warm.GetWidth() * s, UiKit.DesignH / warm.GetHeight() * s),
                Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            });

        // ミナの立ち絵：高さフィット(画面の 0.94 倍)で右寄りに置く。
        var mina = ResourceLoader.Exists(TitleMinaPath) ? ResourceLoader.Load<Texture2D>(TitleMinaPath) : null;
        if (mina != null && mina.GetHeight() > 0)
        {
            float ms = TitleMinaH / mina.GetHeight() * s;
            AddChild(new Sprite2D
            {
                Name = "BgMina", Texture = mina, Centered = false,
                ZIndex = -10, ZAsRelative = false,
                Scale = new Vector2(ms, ms),
                Position = TitleMinaPos * s,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            });
        }
        _hasBgLayers = true;
        return true;
    }

    // KV を完全静止の Sprite2D（最背面・アスペクト維持カバー）として敷く。
    //   シェーダ無し・モーフ無し・呼吸無し＝顔は1pxも動かない。
    private void BuildKvSprite()
    {
        if (_kvTex == null) return; // KV が無ければフォールバック（_Draw 側で夜グラデ）。
        float s = UiKit.Scale;       // 設計1280→内部384 の倍率(0.3)
        float W = UiKit.DesignW, H = UiKit.DesignH;
        float tw = _kvTex.GetWidth(), th = _kvTex.GetHeight();
        float cover = Mathf.Max(W / tw, H / th); // 画面いっぱいに覆う（端を露出させない）
        _kvSprite = new Sprite2D
        {
            Texture = _kvTex,
            Centered = true,
            ZIndex = -10,            // UI(_Draw)より必ず背面
            ZAsRelative = false,
            Scale = new Vector2(cover * s, cover * s),
            Position = new Vector2(W * s / 2f, H * s / 2f),
        };
        AddChild(_kvSprite);
    }

    // 加算合成の光レイヤを作る（KVの上・UIの下＝ZIndex -5）。粒/オーブグロウを描く。
    private void BuildLightLayer()
    {
        _lightLayer = new LightLayer
        {
            Host = this,
            ZIndex = -5,        // 静止KV(-10)の上、UI(_Draw=0)の下
            ZAsRelative = false,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        AddChild(_lightLayer);
    }

    // 光の粒を初期化（川の各所にばらまく＝起動直後から流れている状態にする）。
    private void InitMotes()
    {
        _rng.Randomize();
        _motes = new LightMote[MoteCount];
        for (int i = 0; i < _motes.Length; i++)
            _motes[i] = SpawnMote(true);
    }

    // 1粒を生成。spread=true なら川全体に分散配置（初期化用）、false なら川の上流端から。
    //   流域＝右側〜下縁。顔矩形に入る座標は採用せず再抽選する。
    private LightMote SpawnMote(bool spread)
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        Vector2 pos = Vector2.Zero;
        for (int tries = 0; tries < 8; tries++)
        {
            float x, y;
            if (spread)
            {
                // 川全体（右側〜下縁）に散らす。
                x = _rng.RandfRange(W * 0.42f, W * 1.02f);
                y = _rng.RandfRange(H * 0.40f, H * 1.02f);
            }
            else
            {
                // 上流端＝画面の下〜左下寄りから流し始める（右上奥へ向かう）。
                x = _rng.RandfRange(W * 0.40f, W * 0.95f);
                y = _rng.RandfRange(H * 0.78f, H * 1.06f);
            }
            pos = new Vector2(x, y);
            if (!FaceRect.HasPoint(pos)) break; // 顔矩形は避ける
        }

        // 色：シアン主、白寄りを芯に少々、オーブ近傍だけ金をごく僅か混ぜる。
        Color col;
        float roll = _rng.Randf();
        if (pos.DistanceTo(OrbPos) < 180f && roll < 0.45f) col = UiKit.Light;       // オーブの漏れ光
        else if (roll < 0.30f) col = UiKit.PurifyHi;                                  // 白寄りの芯
        else col = UiKit.Purify;                                                      // 主色シアン

        return new LightMote
        {
            Pos = pos,
            Speed = _rng.RandfRange(22f, 40f),     // 控えめ＝上品（画面横断に~30秒）
            Drift = _rng.RandfRange(-0.14f, 0.14f),// 川筋からの微小ぶれ（束をばらす）
            Size = _rng.RandfRange(0.8f, 2.2f),
            Phase = _rng.RandfRange(0f, Mathf.Tau),
            Life = spread ? _rng.RandfRange(0.4f, 1f) : 0f, // 初期は途中から、以後0からフェードイン
            MaxLife = _rng.RandfRange(7f, 13f),
            Col = col,
        };
    }

    // 光の粒を毎フレーム流す（顔は不動・世界だけ動く）。KV スプライトには触れない。
    private void UpdateMotes(double delta)
    {
        float dt = (float)delta;
        float W = UiKit.DesignW, H = UiKit.DesignH;
        // 川筋に直交するベクトル（ぶれ方向）。
        Vector2 perp = new Vector2(-RiverDir.Y, RiverDir.X);
        for (int i = 0; i < _motes.Length; i++)
        {
            ref LightMote m = ref _motes[i];
            m.Life += dt;
            Vector2 vel = (RiverDir + perp * m.Drift) * m.Speed;
            m.Pos += vel * dt;
            // 寿命切れ／画面外（右上奥へ抜けた）／顔矩形へ侵入したら、上流端から再生成。
            bool gone = m.Life >= m.MaxLife
                || m.Pos.X > W * 1.05f || m.Pos.Y < -H * 0.05f
                || FaceRect.HasPoint(m.Pos);
            if (gone) m = SpawnMote(false);
        }
    }

    // 1粒の不透明度（フェードイン→巡航→フェードアウト）。端の硬さを殺す。
    private static float MoteAlpha(in LightMote m)
    {
        float fadeIn = Mathf.Clamp(m.Life / 1.2f, 0f, 1f);
        float fadeOut = Mathf.Clamp((m.MaxLife - m.Life) / 1.5f, 0f, 1f);
        return fadeIn * fadeOut;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_toastT > 0) _toastT -= delta;
        if (_talkT > 0) _talkT -= delta;
        UpdateMotes(delta);
        _lightLayer?.QueueRedraw(); // 流れる光を毎フレーム描き直す（顔/KVは静止のまま）
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

        // 「はじめから」後の操作表示モード3択中：←→/↑↓で選び Z=決定（＝ゲーム開始）/ X=やめる。
        if (_choosingDisplay)
        {
            int n = DisplayChoices.Length;
            bool nu = Input.IsActionPressed("ui_up")   || Input.IsActionPressed("ui_left");
            bool nd = Input.IsActionPressed("ui_down") || Input.IsActionPressed("ui_right");
            if ((nu || nd) && !_navHeld)
            {
                if (nu) _dispSel = (_dispSel + n - 1) % n;
                if (nd) _dispSel = (_dispSel + 1) % n;
                Audio.Instance?.PlayUiMove();
            }
            _navHeld = nu || nd;
            // マウス：各選択肢の行矩形にホバー＝カーソル移動、クリック＝決定（＝ゲーム開始）。
            for (int i = 0; i < n; i++) UiKit.Hotspot(DisplayPickerRowRect(i), i);
            int hov = UiKit.HoveredId();
            if (Pad.UsingMouse && hov >= 0 && hov != _dispSel) { _dispSel = hov; Audio.Instance?.PlayUiMove(); }
            int clk = UiKit.ClickedId(click);
            if (zEdge || clk >= 0)
            {
                if (clk >= 0) _dispSel = clk;
                Audio.Instance?.PlayUiConfirm();
                Pad.SetDisplayAndSave(DisplayChoices[_dispSel].mode); // 反映＋永続化
                _game.ResetPersistent();                              // はじめから＝まっさらスタート
                Go("res://Prologue.tscn");
            }
            else if (backEdge) { Audio.Instance?.PlayUiCancel(); _choosingDisplay = false; }
            _zHeld = z; _backHeld = back;
            QueueRedraw();
            return;
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
            int hov = UiKit.HoveredId();
            if (Pad.UsingMouse && hov >= 0 && hov != _pick) { _pick = hov; Audio.Instance?.PlayUiMove(); }
            int clk = UiKit.ClickedId(click);
            if (clk >= 0) _pick = clk;
            bool loadNow = (zEdge || clk >= 0) && _game.SlotExists(_pick);
            if (loadNow) { Audio.Instance?.PlayUiConfirm(); _game.LoadFromSlot(_pick); Go("res://Hub.tscn"); }
            else if (clk >= 0) Audio.Instance?.PlayUiDeny(); // 空きスロットをクリック
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

    // ── クリック領域（描画レイアウトと同じ式で矩形を作る。ホットスポット登録に使う）──
    // メインメニュー行（DrawMenu と同じ x=88, top=324, rowH=41, gap=3, w=360）。
    private static Rect2 MenuRowRect(int i)
    {
        float x = 88f, top = 324f, rowH = 41f, gap = 3f, w = 360f;
        return new Rect2(x, top + i * (rowH + gap), w, rowH);
    }

    // スロット選択ダイアログの行（DrawSlotPicker と同じジオメトリ：内側パディング分を差し引いた行帯）。
    private static Rect2 SlotPickerRowRect(int i, int n)
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        float w = 560, rowH = 56, h = 100 + n * rowH, x = (W - w) / 2f, y = (H - h) / 2f;
        float top = y + 64;
        return new Rect2(x + 28, top + i * rowH, w - 56, 46);
    }

    // 操作表示3択ダイアログの行（DrawDisplayPicker と同じジオメトリ）。
    private static Rect2 DisplayPickerRowRect(int i)
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        int n = DisplayChoices.Length;
        float w = 600, rowH = 60, h = 132 + n * rowH, x = (W - w) / 2f, y = (H - h) / 2f;
        float top = y + 80;
        return new Rect2(x + 28, top + i * rowH, w - 56, 50);
    }

    private void Confirm()
    {
        switch (Items[_sel].item)
        {
            case Item.NewGame:
                // はじめから＝まず操作表示モードを必ず選ばせる（決定でリセット＋プロローグへ）。
                // 既存の選択があればそれを初期カーソルに（無ければキーボード）。
                _choosingDisplay = true;
                _dispSel = Pad.Display == Pad.DisplayMode.Auto ? 0 : Pad.DisplayToInt(Pad.Display);
                break;
            case Item.Continue:
                if (_hasSave) { _picking = true; _pick = FirstSlot(); }
                else Toast("セーブデータがありません");
                break;
            case Item.HowToPlay:
                // 「あそびかた」＝いつでも引ける静的な操作説明オーバーレイ（シーン遷移しない）。
                GetNodeOrNull<HowToPlay>("/root/HowTo")?.Open();
                break;
            case Item.Tutorial:
                // 「チュートリアル」＝独立ステージ0（完全チュートリアル）を再生（既読フラグは変えない）。
                // DiffSelect を通らないので難易度は Easy に固定（直前の選択を引き継がせない）。
                _game.Difficulty = GameManager.Diff.Easy;
                Go("res://Stage0.tscn");
                break;
            case Item.Settings: Go("res://Settings.tscn"); break;
            // 「クレジット」＝BGM等フリー素材の表記義務を満たす画面（内容は config/credits.ini）。
            case Item.Credits:  Go("res://Credits.tscn"); break;
            case Item.Quit:     GetTree().Quit(); break;
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
        float W = UiKit.DesignW, H = UiKit.DesignH;
        float t = (float)_t;

        // ── 背景は最背面の静止 Sprite2D が描く（_Ready の BuildBgLayers / BuildKvSprite。顔は不動）。
        //    ここでは直貼りしない。層も KV も無い時だけ夜グラデで黒画面を回避する。
        if (!_hasBgLayers && _kvSprite == null)
        {
            UiKit.VGradient(this, new Rect2(0, 0, W, H),
                new[] { new Color("0e1834"), new Color("0a1126"), new Color("070a16") },
                new[] { 0f, 0.55f, 1f });
        }

        // ── 流れる光の粒＋オーブ脈動は専用の加算レイヤ（_lightLayer・最背面KVの上）が描く。
        //    ここ（通常合成のUIレイヤ）では描かない＝文字/メニューに加算が掛からない。

        // ── 可読性スクリム（KVの上・UIの下）──
        // 左を暗くする横グラデ（左=半透明ダーク→右=透明）。タイトル文字とメニューのコントラストを保証。
        UiKit.HGradient(this, new Rect2(0, 0, W * 0.62f, H),
            new Color(6 / 255f, 9 / 255f, 20 / 255f, 0.74f), new Color(6 / 255f, 9 / 255f, 20 / 255f, 0f));
        // 下端の薄いスクリム（プロンプト・バージョン表記の足元を沈める）。
        UiKit.VGradient(this, new Rect2(0, H - 150f, W, 150f),
            new[] { new Color(6 / 255f, 9 / 255f, 20 / 255f, 0f), new Color(6 / 255f, 9 / 255f, 20 / 255f, 0.55f) },
            new[] { 0f, 1f });
        // 上端の薄いスクリム（ティッカー帯の可読性）。
        UiKit.VGradient(this, new Rect2(0, 0, W, 90f),
            new[] { new Color(6 / 255f, 9 / 255f, 20 / 255f, 0.45f), new Color(6 / 255f, 9 / 255f, 20 / 255f, 0f) },
            new[] { 0f, 1f });

        // ── 漂う光の弾（KVに溶け込む空気・脇役）──
        float kegPulse = 0.5f + 0.5f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.Pi * 2f / 2.4f));
        DrawDanmaku(new Vector2(W * 0.60f, H * 0.34f), 4f, new Color(150 / 255f, 200 / 255f, 1f, kegPulse));

        // ── スキャンライン（控えめ・画面の質感を統一）──
        for (float y = 0; y < H; y += 6f)
            DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.07f));

        DrawTitleBlock();
        DrawMenu();
        DrawPrompt();

        // ── バージョン（右下）──
        UiKit.Text(this, UiKit.Mono, new Vector2(W - 230, H - 48), "ver 0.3.0 — 体験版", UiKit.FontSmall, UiKit.Text4, HorizontalAlignment.Right, 204);
        UiKit.Text(this, UiKit.Mono, new Vector2(W - 230, H - 30), "© 2026 takutoruku1", UiKit.FontSmall, UiKit.Text4, HorizontalAlignment.Right, 204);

        DrawTicker();
        DrawTalk();
        DrawToast();
        if (_picking) DrawSlotPicker();
        if (_choosingDisplay) DrawDisplayPicker();
        UiKit.EndDesign(this);
    }

    // 「つづきから」スロット選択ダイアログ（オート＋3スロット・空きはグレー）。
    private void DrawSlotPicker()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.6f)); // 暗幕
        int n = GameManager.SlotCount + 1;
        float w = 560, rowH = 56, h = 100 + n * rowH, x = (W - w) / 2f, y = (H - h) / 2f;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.98f), 16f, new Color(UiKit.Purify, 0.7f), 1.4f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + 26), "つづきから — スロットを選ぶ", UiKit.FontHeading, UiKit.White, HorizontalAlignment.Center, w);
        float top = y + 64;
        for (int i = 0; i < n; i++)
        {
            float ry = top + i * rowH;
            bool on = i == _pick;
            bool exists = _game.SlotExists(i);
            if (on)
            {
                UiKit.Box(this, new Rect2(x + 28, ry, w - 56, 46), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 10f, new Color(UiKit.Purify, 0.45f), 1f);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 44, ry + 14), "▸", UiKit.FontBody, UiKit.Purify);
            }
            Color nameCol = exists ? (on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f)) : UiKit.Text4;
            string name = i == 0 ? "オートセーブ" : $"スロット {i}";
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 70, ry + 12), name, UiKit.FontSpeaker, nameCol);
            UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 220, ry + 15), exists ? "セーブあり" : "—— 空き ——", UiKit.FontLabel,
                exists ? UiKit.Info : UiKit.Text4, HorizontalAlignment.Right, 192);
        }
        UiKit.Text(this, UiKit.Mono, new Vector2(x, y + h - 28), "Z 決定    X 戻る", UiKit.FontSmall, UiKit.Text3, HorizontalAlignment.Center, w);
    }

    // 「はじめから」後の操作表示モード3択ダイアログ（キーボード / コントローラPS / コントローラXbox）。
    // ここで選ぶのはヒント表記の初期値とパッド表記スタイル（PS/Xbox）。以降のKB⇔パッドの出し分けは
    // 直近に使ったデバイスへ自動追従する（Pad.PollDevice。入力自体はどのデバイスも常に有効）。
    private void DrawDisplayPicker()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.6f)); // 暗幕
        int n = DisplayChoices.Length;
        float w = 600, rowH = 60, h = 132 + n * rowH, x = (W - w) / 2f, y = (H - h) / 2f;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.98f), 16f, new Color(UiKit.Purify, 0.7f), 1.4f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + 24), "操作表示を選ぶ", UiKit.FontHeading, UiKit.White, HorizontalAlignment.Center, w);
        UiKit.Text(this, UiKit.Zen, new Vector2(x, y + 50), "ヒントの表記を選びます（持ち替えたデバイスに自動で切り替わります）", UiKit.FontLabel,
            UiKit.Text3, HorizontalAlignment.Center, w);
        float top = y + 80;
        for (int i = 0; i < n; i++)
        {
            float ry = top + i * rowH;
            bool on = i == _dispSel;
            if (on)
            {
                UiKit.Box(this, new Rect2(x + 28, ry, w - 56, 50), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 10f, new Color(UiKit.Purify, 0.45f), 1f);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 44, ry + 16), "▸", UiKit.FontBody, UiKit.Purify);
            }
            Color nameCol = on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 70, ry + 13), DisplayChoices[i].jp, UiKit.FontSpeaker, nameCol);
            UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 230, ry + 18), DisplayChoices[i].en, UiKit.FontLabel,
                on ? UiKit.Info : UiKit.Text4, HorizontalAlignment.Right, 200);
        }
        UiKit.Text(this, UiKit.Mono, new Vector2(x, y + h - 30), "↑↓ / ←→ えらぶ    Z はじめる    X もどる", UiKit.FontSmall,
            UiKit.Text3, HorizontalAlignment.Center, w);
    }

    private void DrawDanmaku(Vector2 c, float r, Color col)
    {
        UiKit.RadialGlow(this, c, r * 2.4f, col, 0.6f);
        DrawCircle(c, r, col);
        DrawCircle(c - new Vector2(r * 0.3f, r * 0.35f), r * 0.4f, new Color(1, 1, 1, 0.9f));
    }

    // 流れる光の粒＋オーブ脈動を描く加算レイヤ（TitleMenu の子 Node2D）。
    //   CanvasItemMaterial=Add をノードに載せる＝この層の描画だけが加算合成になり、
    //   親(_Draw)の文字/メニューには加算が掛からない（DrawSetBlendMode は _Draw に存在しないため）。
    //   親が時刻 t と粒配列を渡し、毎フレーム QueueRedraw する。
    public partial class LightLayer : Node2D
    {
        public TitleMenu? Host;
        public override void _Draw()
        {
            if (Host == null) return;
            UiKit.BeginDesign(this);
            float t = (float)Host._t;

            // ── データの川を流れる光の粒：芯（明るい小円）＋外周グロウ（低アルファ）──
            foreach (var m in Host._motes)
            {
                float a = MoteAlpha(m);
                if (a <= 0.01f) continue;
                // ゆっくりした明滅（個体ごとの位相）で“息づく”質感。
                float twinkle = 0.7f + 0.3f * (0.5f + 0.5f * Mathf.Sin(t * 1.6f + m.Phase));
                float alpha = Mathf.Min(0.5f, a * twinkle * 0.5f);
                var core = new Color(m.Col.R, m.Col.G, m.Col.B, alpha);
                var halo = new Color(m.Col.R, m.Col.G, m.Col.B, alpha * 0.45f);
                UiKit.RadialGlow(this, m.Pos, m.Size * 3.4f, halo); // 外周グロウ
                DrawCircle(m.Pos, m.Size, core);                    // 芯
            }

            // ── 金オーブの脈動グロウ（世界の鼓動・1点だけ・周期3.6s・振幅控えめ）──
            //    顔の下に位置するので顔は無傷。
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.Pi * 2f / 3.6f);
            float oa = 0.10f + 0.06f * pulse;   // 0.10〜0.16
            float orad = 78f + 10f * pulse;
            UiKit.RadialGlow(this, OrbPos, orad, new Color(UiKit.Light, oa));

            UiKit.EndDesign(this);
        }
    }

    private void DrawTitleBlock()
    {
        float x = 88f;
        UiKit.Text(this, UiKit.Mono, new Vector2(x, 92), "A L G O :", UiKit.FontBody, UiKit.Info);
        // 大見出し（白→シアン→紫のグラデを2行の色分けで近似）＋落ち影。
        //   タイトルロゴは全画面で唯一の最大表示＝FontDisplay(52) より大きい別格サイズを意図して残す（機械的に潰さない）。
        const int logo = 62;
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x + 2, 122), "Refrain", logo, new Color(0.08f, 0.06f, 0.16f, 0.6f));
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, 120), "Refrain", logo, UiKit.PurifyHi);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x + 2, 190), "of Light", logo, new Color(0.08f, 0.06f, 0.16f, 0.6f));
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, 188), "of Light", logo, new Color(155 / 255f, 183 / 255f, 232 / 255f));
        // 区切り＋サブ
        DrawRect(new Rect2(x, 270, 34f, 2f), UiKit.Purify);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 46, 262), "心象シューティング", UiKit.FontBody, UiKit.Text2);
        UiKit.Text(this, UiKit.Zen, new Vector2(x, 286), "— その痛みに、光を届けに。", UiKit.FontBody, UiKit.Text3);
    }

    private void DrawMenu()
    {
        // 7項目（クレジット追加）でもプロンプト（y=656）に食い込まない行高に詰める。
        float x = 88f, top = 324f, rowH = 41f, gap = 3f, w = 360f;
        for (int i = 0; i < Items.Length; i++)
        {
            float ry = top + i * (rowH + gap);
            bool on = i == _sel;
            bool disabled = Items[i].item == Item.Continue && !_hasSave;

            if (on)
            {
                UiKit.Box(this, new Rect2(x, ry, w, rowH), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 12f,
                    new Color(UiKit.Purify, 0.45f), 1f);
            }
            // ▸ カーソル
            if (on) UiKit.Text(this, UiKit.Mono, new Vector2(x + 18, ry + 9), "▸", UiKit.FontSpeaker, UiKit.Purify);
            // 名前
            var nameFont = on ? UiKit.ZenBlack : UiKit.ZenBold;
            Color nameCol = disabled ? UiKit.Text4 : (on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f));
            UiKit.Text(this, nameFont, new Vector2(x + 42, ry + 7), Items[i].jp, UiKit.FontHeading, nameCol);
            // EN ラベル（右）
            UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 130, ry + 12), Items[i].en, UiKit.FontSmall,
                on ? UiKit.Info : UiKit.Text4, HorizontalAlignment.Right, 120);
        }
    }

    private void DrawPrompt()
    {
        float x = 88f, y = 656f;
        float blink = 0.55f + 0.45f * Mathf.Sin((float)_t * Mathf.Pi * 2f / 1.6f);
        UiKit.Key(this, new Vector2(x, y), "Z", new Color(UiKit.Purify, 0.14f * blink + 0.06f), new Color(UiKit.Info, 0.5f), UiKit.PurifyHi);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 36, y + 4), "けってい", UiKit.FontLabel, UiKit.Info);
        UiKit.Key(this, new Vector2(x + 130, y), "↑↓", new Color(1, 1, 1, 0.06f), new Color(1, 1, 1, 0.16f), UiKit.Text2);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + 178, y + 4), "えらぶ", UiKit.FontLabel, UiKit.Text3);
    }

    private void DrawTicker()
    {
        float W = UiKit.DesignW, barH = 34f;
        UiKit.VGradient(this, new Rect2(0, 0, W, barH),
            new[] { new Color(10 / 255f, 8 / 255f, 16 / 255f, 0.55f), new Color(10 / 255f, 8 / 255f, 16 / 255f, 0f) },
            new[] { 0f, 1f });
        // DIVING ラベル
        DrawCircle(new Vector2(20, barH / 2f), 5f, UiKit.Purify);
        UiKit.Text(this, UiKit.Mono, new Vector2(34, barH / 2f - 6), "DIVING", UiKit.FontSmall, UiKit.Text3);

        // スクロールするツイート
        float startX = 120f, gap = 60f;
        // 1ブロックの幅を概算して周期スクロール
        float block = 0f;
        foreach (var (h, tx) in Ticker) block += UiKit.TextW(UiKit.Mono, h, UiKit.FontSmall) + 6 + UiKit.TextW(UiKit.Zen, tx, UiKit.FontSmall) + gap;
        float scroll = ((float)_t * 60f) % block;
        float cx = startX - scroll;
        for (int rep = 0; rep < 3; rep++)
        {
            foreach (var (h, txt) in Ticker)
            {
                if (cx > 80 && cx < W)
                {
                    UiKit.Text(this, UiKit.Mono, new Vector2(cx, barH / 2f - 6), h, UiKit.FontSmall, UiKit.Text4);
                    float hw = UiKit.TextW(UiKit.Mono, h, UiKit.FontSmall) + 6;
                    UiKit.Text(this, UiKit.Zen, new Vector2(cx + hw, barH / 2f - 7), txt, UiKit.FontSmall, new Color(UiKit.Text2, 0.5f));
                }
                cx += UiKit.TextW(UiKit.Mono, h, UiKit.FontSmall) + 6 + UiKit.TextW(UiKit.Zen, txt, UiKit.FontSmall) + gap;
            }
        }
    }

    // 小話5：ミナのひとことを画面下・中央帯に薄く1行だけ添える（ver/©表示・プロンプト・メニュー行の
    //   いずれとも重ならない x≈290〜990 の空き帯）。Hotspot登録も無く、クリック判定・操作性には無関係。
    private void DrawTalk()
    {
        if (_talkT <= 0 || string.IsNullOrEmpty(_talk)) return;
        float W = UiKit.DesignW, H = UiKit.DesignH;
        UiKit.Text(this, UiKit.Zen, new Vector2(W * 0.5f - 350f, H - 30f), _talk, UiKit.FontSmall,
            new Color(UiKit.Mina, 0.40f), HorizontalAlignment.Center, 700f);
    }

    private void DrawToast()
    {
        if (_toastT <= 0) return;
        float W = UiKit.DesignW;
        float w = UiKit.TextW(UiKit.ZenBold, _toast, UiKit.FontBody) + 48;
        float x = (W - w) / 2f;
        UiKit.Box(this, new Rect2(x, 600, w, 44), new Color(0.06f, 0.05f, 0.10f, 0.96f), 12f, new Color(UiKit.Purify, 0.7f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, 612), _toast, UiKit.FontBody, UiKit.Text2, HorizontalAlignment.Center, w);
    }
}
