using Godot;

// ControlCard : 初見チュートリアル中だけ盤面の中央に出す「操作の割り当て表」ウィンドウ（2026-09-17）。
//   従来はミナのセリフ本文にキー名を羅列していた（「移動——キーボードは、矢印かWASD。コントローラーは……」）。
//   3機種を一行へ詰めるので読みにくく、会話の流れも止まる。そこで
//     ・ミナのセリフ＝「何ができるか・なぜするか」だけ（StageTutorial.Route）
//     ・このカード  ＝「どのボタンか」（キーボード／コントローラー／マウスを横に並べて一望）
//   と役割を割った。会話の行が進むたび StageTutorial.SyncCard が話題（Topic）を差し替える。
//
//   ■ 置き場所と重なり
//     盤面（設計座標 x 400..1280 / Field.DLeft..DRight）の中央上寄せ。y 150..452 に収め、
//     会話ボックス（Hud.DrawDialog の y 520..690）とも、ボスバー（y 24..68）とも重ならない。
//     道中チュートリアルが流れる step==1 はザコの湧き（Step_Midwave*）より前で敵はまだ居らず、
//     会話中は Hud.BubblePaused で弾も自機も止まっている＝隠す対象が原理的に無い。
//     それでも背景を潰さないよう、板は α0.93 のダークガラス＋ミナ色の細枠に留める。
//
//   ■ 出し方・消し方（プレイヤーを待たせない）
//     フェードイン 0.22s / 差し替え 0.16s のクロスフェード / フェードアウト 0.22s。いずれも
//     会話送りをブロックしない（このノードは入力を一切取らない＝MouseFilter=Ignore・_Process は
//     見た目だけ進める）。話題の無い行になれば自動で消え、会話が終われば Hide() で畳む。
//
//   ■ 表記の出典
//     キー名は「あそびかた」画面（HowToPlay.DrawPageControls・3タブ表）と同じ文字列を使う。
//     実装が正典なので、ここで新しいキー名を発明しない。パッド表記は Pad.Face（Xbox 固定）経由。
public partial class ControlCard : Control
{
    // 話題（会話の行に対応）。None = カードを出さない行。
    //   Dodge / Charge（2026-09-22）はスキル説明（StageTutorial.SkillDodge / SkillCharge）用。
    //   Dodge は習得後、Charge は最初から使えるので最初の面の冒頭で出る（2026-09-25）。
    public enum Topic { None, Move, Shot, Lock, LockClear, Bomb, Dodge, Charge }

    private Topic _topic = Topic.None;   // いま出している話題
    private Topic _next = Topic.None;    // 差し替え先（クロスフェード中のみ _topic と異なる）
    private float _alpha;                // 0..1（フェード）
    private bool _fadingOut;             // 退場中（_alpha を 0 へ落とし切ったら _topic を _next にする）
    private double _t;                   // 明滅用の時間

    private const float FadeIn = 0.22f, Swap = 0.16f, FadeOut = 0.22f;

    // 盤面中央のカード矩形（設計座標）。会話ボックス（y520..690）の上に余白 68px を残す。
    private const float CardW = 812f, CardH = 302f, CardY = 150f;
    private static float CardX => Field.DCenterX - CardW / 2f;

    // Hud（CanvasLayer）の子として生成する。ChoiceOverlay と同じ流儀＝全画面 Control に重ねて描く。
    public static ControlCard Attach(Node parent)
    {
        var c = new ControlCard { Name = "ControlCard" };
        parent.AddChild(c);
        return c;
    }

    public override void _Ready()
    {
        // 実画面(384×216)全域。描画は UiKit.BeginDesign で設計座標(1280×720)へ引き伸ばす。
        Size = new Vector2(384f, 216f);
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;   // 会話中は木が止まる場面があるので常時進める
        ZIndex = 1;
    }

    // 話題を差し替える（同じ話題なら何もしない＝行が変わってもカードは静止したまま）。
    //   Topic.None を渡すと退場する。
    public void Show(Topic topic)
    {
        if (topic == _next) return;
        _next = topic;
        if (_topic == Topic.None && topic != Topic.None) { _topic = topic; _fadingOut = false; }  // 初回は素直に入場
        else if (topic == Topic.None || topic != _topic) _fadingOut = true;                        // 既存を一度引っ込めてから入替
    }

    // 畳む（Topic.None へ）。CanvasItem.Hide() を隠さないよう別名にしてある（可視性は触らない）。
    public void Dismiss() => Show(Topic.None);

    public override void _Process(double delta)
    {
        _t += delta;
        float d = (float)delta;
        // 会話バブルが閉じたら自動で畳む（Step_Lines が流し切って HideBubble したとき／ポーズ→
        // タイトルへ戻ったとき等）。ステージ側に「消してね」の呼び出しを足さずに済ませるための保険。
        if (_next != Topic.None && !Hud.BubblePaused) Dismiss();
        if (_fadingOut)
        {
            _alpha -= d / FadeOut;
            if (_alpha <= 0f)
            {
                _alpha = 0f;
                _fadingOut = false;
                _topic = _next;                     // 入替（None ならこのまま消えたまま）
            }
        }
        else if (_topic != Topic.None)
        {
            float rate = _alpha > 0f ? Swap : FadeIn;   // 一度出たあとの復帰は少し速く
            _alpha = Mathf.Min(1f, _alpha + d / rate);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_topic == Topic.None || _alpha <= 0.004f) return;
        UiKit.BeginDesign(this);
        DrawCard();
        UiKit.EndDesign(this);
    }

    // ───────── 話題 → 見出し・一行説明・3機種の割り当て ─────────
    //   tok は HowToPlay.DrawPageControls（3タブ表）と同じ文字列。note は機種ごとの補足（空可）。
    private readonly record struct Row(string device, string tok, string note);

    private (string title, string lead, Color accent, Row[] rows) Spec(Topic t) => t switch
    {
        Topic.Move => ("移動", "盤面のどこへでも動けます", UiKit.Info, new[]
        {
            new Row("キーボード",     "矢印 / WASD",            "上下左右に動く"),
            new Row("コントローラー", "L スティック / 十字キー", "上下左右に動く"),
            new Row("マウス",         "カーソル",                "カーソルの位置へ寄っていく"),
        }),
        Topic.Shot => ("撃つ", "光は自動で出ます。撃つボタンはありません", UiKit.Purify, new[]
        {
            new Row("キーボード",     "オート", "押さなくていい"),
            new Row("コントローラー", "オート", "押さなくていい"),
            new Row("マウス",         "オート", "押さなくていい"),
        }),
        Topic.Lock => ("ロックオン送り", "押すたび近い敵から順に狙う。移動は少し遅くなる", UiKit.Purify, new[]
        {
            new Row("キーボード",     "F",                        "押すたび次の敵へ"),
            new Row("コントローラー", Pad.Face(JoyButton.RightShoulder), "押すたび次の敵へ"),
            new Row("マウス",         "左クリック",               "短く押すたび次の敵へ"),
        }),
        // 解除は送りとは別ボタン（2026-09-17 追加）。話題を分けて、送りの直後の行で差し替える。
        Topic.LockClear => ("ロックオン解除", "狙いを外す。倒せば次の敵へ勝手に移る", UiKit.Purify, new[]
        {
            new Row("キーボード",     "G",                            "送りの F のとなり"),
            new Row("コントローラー", Pad.Face(JoyButton.RightStick),  "右スティック押し込み"),
            new Row("マウス",         "右クリック",                    "回避と同じボタン"),
        }),
        Topic.Bomb => ("ボム", "画面の弾を消し短時間無敵。残数ぶん", UiKit.Mina, new[]
        {
            new Row("キーボード",     "X",            "残数は左の BOMB 欄"),
            new Row("コントローラー", Pad.Face(JoyButton.X), "残数は左の BOMB 欄"),
            new Row("マウス",         "中クリック",   "残数は左の BOMB 欄"),
        }),
        // 回避（ショップ「回避」で覚える）。割り当ては Player.cs の回避入力＝Alt / L3 / 右クリック（HowToPlay と同表記）。
        //   マウスの右クリックはロックオン解除と兼用＝解除カードの注記と対にする。
        Topic.Dodge => ("回避", "一瞬だけ駆け抜ける。そのあいだは何も当たらない", UiKit.Gold, new[]
        {
            new Row("キーボード",     "Alt",                          "移動方向へ。無ければその場"),
            new Row("コントローラー", Pad.Face(JoyButton.LeftStick),   "左スティック押し込み"),
            new Row("マウス",         "右クリック",                    "カーソル方向へ。ロック解除と兼用"),
        }),
        // 溜め打ち（最初から使える。2026-09-25）。割り当ては Player.cs の溜め入力＝C / Y / 左クリックの長押し。
        //   マウスは短押しがロックオン送り・長押しが溜め（同じ左クリック）＝注記で分ける。
        Topic.Charge => ("溜め打ち", $"満ちたら離す。{GameManager.Instance!.JobDef.ChargeDescription}", UiKit.Gold, new[]
        {
            new Row("キーボード",     "C 長押し",                            "満ちたら離す"),
            new Row("コントローラー", Pad.Face(JoyButton.Y) + " 長押し",      "満ちたら離す"),
            new Row("マウス",         "左クリック 長押し",                    "短く押すとロックオン送り"),
        }),
        _ => ("", "", UiKit.White, System.Array.Empty<Row>()),
    };

    private void DrawCard()
    {
        var (title, lead, accent, rows) = Spec(_topic);
        if (rows.Length == 0) return;

        float a = _alpha;
        // 入場は下から 14px せり上がる（予備動作→本動作。派手にしない）。
        float rise = (1f - a) * 14f;
        float x = CardX, y = CardY + rise, w = CardW, h = CardH;

        // 板：ダークガラス＋ミナ色の細枠。背景（ステージ絵）を完全には潰さない。
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.04f, 0.04f, 0.09f, 0.93f * a), 16f,
                  new Color(UiKit.Mina, 0.55f * a), 1.4f);
        // 上辺のアクセント帯（話題の色）＝「いま何の話か」が枠の色で分かる。
        this.DrawRect(new Rect2(x + 16f, y + 1.5f, w - 32f, 3f), new Color(accent, 0.85f * a));

        // ── ヘッダ ──
        UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x + 28f, y + 20f), "CONTROLS", new Color(UiKit.Info, 0.85f * a));
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x + 28f, y + 38f), title, UiKit.FontTitle, new Color(UiKit.White, a));
        UiKit.Text(this, UiKit.Zen, new Vector2(x + 28f, y + 78f), lead, UiKit.FontBody, new Color(UiKit.Text2, a),
                   HorizontalAlignment.Left, w - 56f);
        this.DrawRect(new Rect2(x + 28f, y + 108f, w - 56f, 1f), new Color(1f, 1f, 1f, 0.10f * a));

        // ── 3機種の列（横並び）──
        float colW = (w - 56f - 24f) / 3f, colH = 148f, colY = y + 124f;
        for (int i = 0; i < 3 && i < rows.Length; i++)
        {
            float cx = x + 28f + i * (colW + 12f);
            UiKit.Box(this, new Rect2(cx, colY, colW, colH), new Color(accent, 0.07f * a), 12f,
                      new Color(accent, 0.30f * a), 1.1f);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(cx, colY + 14f), rows[i].device, UiKit.FontLabel,
                       new Color(UiKit.Text3, a), HorizontalAlignment.Center, colW);
            // キーバッジ（列の中央・HowToPlay.KeyBadge と同じ意匠／可変幅）。
            float bw = UiKit.TextW(UiKit.Mono, rows[i].tok, UiKit.FontBody) + 24f;
            float bx = cx + (colW - bw) / 2f, by = colY + 48f;
            // 今の話題だけ、バッジの縁をゆっくり明滅させて視線を集める。
            float pulse = 0.6f + 0.4f * Mathf.Sin((float)_t * 3.2f);
            UiKit.Box(this, new Rect2(bx, by, bw, 38f), new Color(0.10f, 0.09f, 0.16f, 0.95f * a), 8f,
                      new Color(accent, (0.55f + 0.35f * pulse) * a), 1.3f);
            UiKit.Text(this, UiKit.Mono, new Vector2(bx, by + 9f), rows[i].tok, UiKit.FontBody,
                       new Color(accent, a), HorizontalAlignment.Center, bw);
            if (rows[i].note.Length > 0)
                UiKit.Text(this, UiKit.Zen, new Vector2(cx + 10f, colY + 102f), rows[i].note, UiKit.FontSmall,
                           new Color(UiKit.Text4, a), HorizontalAlignment.Center, colW - 20f);
        }

        // 足元の一行（全カード共通の念押し）。「あそびかた」で後からいつでも見られることを伝える。
        UiKit.Text(this, UiKit.Zen, new Vector2(x + 28f, y + h - 30f),
                   "この表は " + Pad.PauseToken + " →「あそびかた」でいつでも見られます", UiKit.FontSmall,
                   new Color(UiKit.Text4, 0.9f * a), HorizontalAlignment.Center, w - 56f);
    }
}
