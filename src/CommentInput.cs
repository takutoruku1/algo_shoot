using Godot;

// CommentInput : S2-4「配信画面の下のコメント入力欄」専用の描画オーバーレイ。
//   仮台本 wiki/08_仮台本/07_粗い台本_案C_2_こはるとレイ.md（ユーザー承認済み・2026-09-05）の S2-4。
//   台本の who=3（システム表示）2行は、これまで Hud のナレ用中央テロップで代用していたが、
//   場面の中身は「配信画面の下にあるコメント入力欄」そのものなので、その姿で見せる。
//
// 見せるもの（新規画像は作らない＝UiKit の部品と文字だけで組む）:
//   ・配信画面の下端に貼りつく横長のコメント欄（角丸ボックス＋薄いガラス＋左に丸アバター＋右に送信ボタン）。
//   ・入力中の一行が、1文字ずつ現れる（文字送り）。カーソル「|」は末尾で明滅する。
//   ・消す行では、末尾から1文字ずつ削れていく（06/07 の「消した一行」）。
//   ・送る行は打ち終えたあと送信ボタンが一度だけ灯る（送られた、が伝わるだけの小さな灯り）。
//
// 使い方（StageKoharu.Step_InputField 参照）:
//   _input = CommentInput.Show(Hud);
//   _input.Type("レイちゃんが");   … 打つ（Done が立ったら次へ）
//   _input.Erase();                … いま入っている文字を末尾から消す（Done が立ったら次へ）
//   _input.Type("今日も来ました", send: true); … 打って送る
//   ... 場面が終わったら QueueFree する（後始末は呼び出し側の責務。ChoiceOverlay と同じ流儀）。
//
// 台本の行そのものは StageKoharu.InputField から変えない（who=3 の text をここへ渡すだけ）。
// 文中のカーソル記号「|」は表示側で描くので、渡す前に呼び出し側が落とす。
public partial class CommentInput : Control
{
    // いまの動作が完了したか（打ち終わり／消し終わり）。呼び出し側はこれを見て次の行へ進む。
    public bool Done { get; private set; } = true;

    private string _target = "";     // 打ち切ったときの全文
    private string _shown = "";      // いま欄に入っている文字列
    private bool _erasing;           // 消し中（末尾から削る）
    private bool _sending;           // 打ち終えたら送る行か
    private double _t;               // 出現からの経過（枠のフェードイン・カーソル明滅）
    private double _step;            // 1文字ぶんの溜め
    private double _sendGlow;        // 送信ボタンが灯っている残り秒

    // 欄の位置（設計座標 1280×720）。会話バー（y=520〜690）とナレ用テロップ（y=590〜686）の上に置く＝
    //   ミナの観測行を出したまま欄が読める。配信画面の下端に貼りついて見える高さ。
    private const float BoxX = 232f, BoxY = 424f, BoxW = 816f, BoxH = 64f;
    private const float FadeIn = 0.35f;      // 枠の出現
    private const float TypeInterval = 0.075f;   // 1文字打つ間隔（会話のタイプ送りより少し遅い＝手で打っている）
    private const float EraseInterval = 0.11f;   // 1文字消す間隔（打つより遅い＝ためらいながら消す）
    private const float SendGlowDur = 0.9f;      // 送信ボタンが灯る時間

    // 配信画面の琥珀（PostBullets のこはる面アクセントと同じ色）。欄の縁と送信ボタンに使う。
    private static readonly Color Amber = new Color(0.85f, 0.60f, 0.44f);

    public static CommentInput Show(Node parent)
    {
        var c = new CommentInput { Name = "CommentInput" };
        parent.AddChild(c);
        return c;
    }

    public override void _Ready()
    {
        // 実画面(384x216)全域に重ねる。描画は UiKit.BeginDesign で設計座標(1280x720)へ換算して行う。
        Size = new Vector2(384f, 216f);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    // 打つ。send=true なら打ち終えたあと送信ボタンを一度だけ灯す。
    //   いま欄に入っている文字が目標の途中でない（＝別の一行が残っている）ときは、
    //   先に末尾まで消してから打ち直す＝会話の送りが速くて消しが途中でも「消してから打つ」順は崩れない。
    public void Type(string text, bool send = false)
    {
        _target = text ?? "";
        _sending = send;
        _erasing = !_target.StartsWith(_shown);
        _step = 0;
        Done = _shown == _target;
    }

    // いま入っている文字を末尾から1文字ずつ消す。
    public void Erase()
    {
        _target = "";
        _erasing = true;
        _sending = false;
        _step = 0;
        Done = _shown.Length == 0;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_sendGlow > 0) _sendGlow -= delta;
        QueueRedraw();
        if (Done) return;

        // 枠が出そろうまでは打ち始めない（予備動作）。
        if (_t < FadeIn) return;

        _step += delta;
        double interval = _erasing ? EraseInterval : TypeInterval;
        while (_step >= interval && !Done)
        {
            _step -= interval;
            if (_erasing)
            {
                if (_shown.Length > 0) _shown = _shown.Substring(0, _shown.Length - 1);
                // 消し切ったら：Erase() で来たならここで完了、Type() の前段の消しなら打ちへ移る。
                if (_target.StartsWith(_shown)) { _erasing = false; Done = _shown == _target; }
            }
            else
            {
                if (_shown.Length < _target.Length) _shown = _target.Substring(0, _shown.Length + 1);
                if (_shown == _target)
                {
                    Done = true;
                    if (_sending) _sendGlow = SendGlowDur;   // 送られた合図（灯りひとつ）
                }
            }
            // 手で打つ／消す音は付けない（この場面は無音のまま消える一行が要点）。
        }
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        float a = Mathf.Clamp((float)(_t / FadeIn), 0f, 1f);

        // 欄そのもの：配信画面の下端に貼りつく横長の暗い角丸ボックス。縁は配信画面の琥珀を薄く。
        var box = new Rect2(BoxX, BoxY, BoxW, BoxH);
        UiKit.Box(this, box, new Color(0.05f, 0.045f, 0.075f, 0.90f * a), 10f, new Color(Amber, 0.42f * a), 1.4f);
        // 上辺の一本線＝ここから上が配信画面、という区切り（画面の下端に居ることを示す）。
        DrawRect(new Rect2(BoxX, BoxY - 3f, BoxW, 1f), new Color(Amber, 0.22f * a));

        // 左の丸アバター（顔は無い＝名前を出さない。誰が打っているかは画面が語る）。
        float cy = BoxY + BoxH * 0.5f;
        UiKit.Avatar(this, new Vector2(BoxX + 30f, cy), 15f, new Color(Amber, 0.55f * a), "");

        // 本文：入力中の一行。文字送り／末尾から削る、のどちらもこの一行の中で起きる。
        float textX = BoxX + 58f;
        int size = UiKit.FontBody;
        UiKit.Text(this, UiKit.Zen, new Vector2(textX, cy - size * 0.72f), _shown, size,
            new Color(0.92f, 0.92f, 0.96f, a));
        // カーソル：末尾で明滅（打ち終えても消し終えても、欄に居るあいだは点いている）。
        float cx = textX + UiKit.TextW(UiKit.Zen, _shown, size) + 2f;
        if (((int)(_t * 2.2)) % 2 == 0)
            DrawRect(new Rect2(cx, cy - size * 0.66f, 1.6f, size * 1.3f), new Color(1f, 1f, 1f, 0.80f * a));

        // 右の送信ボタン。ふだんは沈んでいて、送られた瞬間だけ一度灯る。
        var btn = new Rect2(BoxX + BoxW - 92f, cy - 15f, 72f, 30f);
        float glow = _sendGlow > 0 ? (float)(_sendGlow / SendGlowDur) : 0f;
        UiKit.Box(this, btn, new Color(Amber, (0.14f + 0.46f * glow) * a), 7f, new Color(Amber, (0.35f + 0.45f * glow) * a), 1f);
        string label = "送信";
        float lw = UiKit.TextW(UiKit.ZenBold, label, UiKit.FontLabel);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(btn.Position.X + (btn.Size.X - lw) * 0.5f, cy - UiKit.FontLabel * 0.72f),
            label, UiKit.FontLabel, new Color(1f, 1f, 1f, (0.45f + 0.5f * glow) * a));
        if (glow > 0f)
            UiKit.RadialGlow(this, btn.Position + btn.Size * 0.5f, 70f, Amber, 0.22f * glow * a);

        UiKit.EndDesign(this);
    }
}
