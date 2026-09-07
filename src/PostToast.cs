using Godot;

// PostToast : Ｘ（SNS）の投稿を「通知が来た」姿で画面中央に出すオーバーレイ。
//
// なぜ要るか（2026-09-07 ユーザー指示）:
//   Hud.LineKind.Post（who=4）は会話バーに文字が流れるだけで、「Ｘ の投稿を見ている」場面に見えなかった。
//   会話バーの経路は**残す**（背景で流したいだけの投稿に使う）。ここは「見せたい投稿」専用の見せ方
//   ＝小さく跳ねて現れ、数秒で消える通知カードを画面中央に出す。
//
// 見た目の語彙は既存から借りる（新しい意匠は作らない）:
//   ・src/Hub.cs の feed カード … 角丸の暗いカード／丸アイコン／表示名＋認証バッジ＋@ハンドル＋「· 時刻」／本文
//   ・src/StageImagery.cs の背景板 … エンゲージ行は「点4つ＋薄い数字」（返信・リポスト・いいね・閲覧の順・図像は描かない）
//   どちらも読むだけで編集していない（別の担当が触っている）。
//
// 使い方（Prologue.P4 参照）:
//   _toast = PostToast.Show(this, "表示名", "@handle", "· 2時間", "本文", verified: true);
//   ... _toast.Gone が立ったら（または場面を抜けるとき）QueueFree する（後始末は呼び出し側の責務＝ChoiceOverlay と同じ流儀）。
//
//   打って消す演出（『たすけて』）を出すときは本文を後から差し替える:
//   _toast = PostToast.ShowComposing(this, name, handle, time);   // 本文が空の「書きかけ」で出す
//   _toast.Type("たすけて、、、");  … 1文字ずつ打つ（Done が立ったら次へ）
//   _toast.Erase();                 … 末尾から1文字ずつ消す（Done が立ったら次へ）
//   _toast.Type("元気です。", send: true); … 打って送る（送信ボタンが一度灯る）
//   打つ／消すの速度は src/CommentInput.cs（こはる面の入力欄）と同値：打つ 0.075 秒/字・消す 0.11 秒/字
//   （消す方を遅くして迷いを出す）＝同じ「書いて消す」行為が、画面が変わっても同じ手つきに見える。
public partial class PostToast : Control
{
    // 通知として出しっぱなしにする時間（Dwell）を過ぎたら自分から引く。0以下＝自分から消えない（Composing）。
    private const float PopIn = 0.32f;      // 跳ねて現れる（ぴこん）
    private const float PopOut = 0.28f;     // 引く
    private const float DwellDefault = 3.4f;// 出しっぱなし（読める長さ）

    // 打つ／消す速度は CommentInput と同値（同じ行為＝同じ手つき）。
    private const float TypeInterval = 0.075f;
    private const float EraseInterval = 0.11f;
    // CommentInput は 0.9 秒だが、あちらは会話の送りが被さって次へ流れる。ここは送信のあとに余韻
    //   （Prologue.SentHold=2.2 秒）を置いて見せる場面なので、灯りが余韻より先に落ちないよう伸ばす。
    private const float SendGlowDur = 1.8f;

    // カードの寸法・位置（設計座標 1280×720）。画面中央（横）の、会話バーより上。
    //   会話バー（Prologue は生座標 y=158 上端＝設計 527）と立ち絵の顔（設計 y≈113〜300）の上に重ねる
    //   ＝通知が「場面の上に」出た、という見え方。
    private const float CardW = 640f, CardH = 176f;
    private static readonly Vector2 CardCenter = new(1280f * 0.5f, 250f);

    // 配色：Hub の feed カードと同じ紫黒の面＋Purify の縁。通知の縁だけ少し強く灯す。
    private static readonly Color Face = new(22 / 255f, 18 / 255f, 34 / 255f, 0.96f);

    public bool Gone { get; private set; }            // 引き終わった（呼び出し側が QueueFree してよい）
    public bool Done { get; private set; } = true;    // 打ち終わり／消し終わり（Type/Erase の完了）

    private string _name = "", _handle = "", _relT = "";
    private bool _verified;
    private int _icon = 1;                            // char/v3/icons/mob_XX.png の番号
    private Texture2D? _iconTex;

    private string _body = "";      // いま出ている本文
    private string _target = "";    // 打ち切ったときの全文
    private bool _erasing, _sending, _composing, _caret;
    private double _t, _step, _sendGlow, _dwell = DwellDefault;
    private double _outT = -1;      // 引き始めてからの経過（負＝まだ引いていない）

    // エンゲージ行の数字（返信・リポスト・いいね・閲覧）。書きかけの投稿には出さない。
    private int _replies, _reposts, _likes, _views;

    // ── 出す：読ませるだけの通知（数秒で自分から消える）──
    public static PostToast Show(Node parent, string name, string handle, string relT, string body,
        bool verified = false, int icon = 1, int replies = 0, int reposts = 0, int likes = 0, int views = 0,
        float dwell = DwellDefault)
    {
        var t = Make(parent, name, handle, relT, verified, icon);
        t._body = body; t._target = body; t._dwell = dwell;
        t._replies = replies; t._reposts = reposts; t._likes = likes; t._views = views;
        return t;
    }

    // ── 出す：書きかけ（本文が空。Type/Erase で打って消す）──
    //   自分からは消えない＝呼び出し側が Dismiss() するまで居座る。カーソルが末尾で明滅する。
    public static PostToast ShowComposing(Node parent, string name, string handle, string relT,
        bool verified = false, int icon = 1)
    {
        var t = Make(parent, name, handle, relT, verified, icon);
        t._composing = true; t._caret = true; t._dwell = -1f;
        return t;
    }

    private static PostToast Make(Node parent, string name, string handle, string relT, bool verified, int icon)
    {
        var t = new PostToast
        {
            Name = "PostToast",
            _name = name, _handle = handle, _relT = relT, _verified = verified, _icon = icon,
        };
        parent.AddChild(t);
        // 通知音（新規音源は足さない）。Ｘの「ぴこん」に一番近い既存 SE ＝購入成功音（短い上向きの二音）。
        Audio.Instance?.PlayUiBuy();
        return t;
    }

    public override void _Ready()
    {
        // 実画面(384x216)全域に重ねる。描画は UiKit.BeginDesign で設計座標(1280x720)へ換算して行う。
        Size = new Vector2(384f, 216f);
        MouseFilter = MouseFilterEnum.Ignore;
        // アイコンは SnsVoices の表と同じ番号→パス（Hub の埋め草カードと同じ顔が付く）。
        //   未生成なら null＝下の無地シルエットに落ちる（Hub.DrawFillerAvatar と同じ作法）。
        string ip = SnsVoices.IconPath(_icon);
        _iconTex = ResourceLoader.Exists(ip) ? ResourceLoader.Load<Texture2D>(ip) : null;
    }

    // 打つ。send=true なら打ち終えたあと送信ボタンを一度だけ灯す（CommentInput.Type と同じ作法）。
    public void Type(string text, bool send = false)
    {
        _target = text ?? "";
        _sending = send;
        _erasing = !_target.StartsWith(_body);
        _step = 0;
        Done = _body == _target;
    }

    // いま入っている本文を末尾から1文字ずつ消す。
    public void Erase()
    {
        _target = "";
        _erasing = true;
        _sending = false;
        _step = 0;
        Done = _body.Length == 0;
    }

    // 自分から引かせる（Composing で出したときの後始末）。引き終わると Gone が立つ。
    public void Dismiss() { if (_outT < 0) _outT = 0; }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_sendGlow > 0) _sendGlow -= delta;
        QueueRedraw();

        // 引き（PopOut）中／引き終わり。
        if (_outT >= 0)
        {
            _outT += delta;
            if (_outT >= PopOut) Gone = true;
            return;
        }
        // 読ませるだけの通知は Dwell を過ぎたら自分から引く。
        if (_dwell > 0 && _t >= PopIn + _dwell) { _outT = 0; return; }

        if (Done) return;
        if (_t < PopIn) return;   // カードが出そろうまでは打ち始めない（予備動作）

        _step += delta;
        double interval = _erasing ? EraseInterval : TypeInterval;
        while (_step >= interval && !Done)
        {
            _step -= interval;
            if (_erasing)
            {
                if (_body.Length > 0) _body = _body.Substring(0, _body.Length - 1);
                // 消し切ったら：Erase() で来たならここで完了、Type() の前段の消しなら打ちへ移る。
                if (_target.StartsWith(_body)) { _erasing = false; Done = _body == _target; }
            }
            else
            {
                if (_body.Length < _target.Length) _body = _target.Substring(0, _body.Length + 1);
                if (_body == _target)
                {
                    Done = true;
                    if (_sending) _sendGlow = SendGlowDur;   // 送られた合図（灯りひとつ）
                }
            }
            // 打つ／消す音は付けない（CommentInput と同じ：無音で消える一行が要点）。
        }
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);

        // ── 出入りの動き：小さく跳ねて現れ（overshoot）、すっと引く ──
        float a, scale, dy;
        if (_outT >= 0)
        {
            float k = Mathf.Clamp((float)(_outT / PopOut), 0f, 1f);
            a = 1f - k; scale = 1f - 0.06f * k; dy = -10f * k;
        }
        else
        {
            float k = Mathf.Clamp((float)(_t / PopIn), 0f, 1f);
            a = k;
            // ぴこん＝行き過ぎて戻る（back-out）。0.9 → 1.04 → 1.0。
            float e = 1f - Mathf.Pow(1f - k, 3f);
            scale = 0.9f + 0.14f * e - 0.04f * Mathf.Pow(e, 6f);
            dy = 14f * (1f - e);
        }
        if (a <= 0.004f) { UiKit.EndDesign(this); return; }

        float w = CardW * scale, h = CardH * scale;
        float x = CardCenter.X - w * 0.5f, y = CardCenter.Y - h * 0.5f + dy;

        // ① 背面の淡い灯り＝通知が「点いた」感じ。ぴこんの瞬間だけ強い。
        float pop = Mathf.Clamp(1f - (float)(_t / (PopIn * 2.2f)), 0f, 1f);
        UiKit.RadialGlow(this, new Vector2(x + w * 0.5f, y + h * 0.5f), w * 0.62f, UiKit.Purify, (0.05f + 0.10f * pop) * a);

        // ② カード本体（Hub の feed カードと同じ角丸16・紫黒の面・淡い縁）。
        UiKit.Box(this, new Rect2(x, y, w, h), Face with { A = Face.A * a }, 16f, new Color(UiKit.Purify, 0.42f * a), 1.6f);

        // 中身は縮小中に潰れて読めなくなるので、レイアウトは等倍で組んでカード中心に合わせて置く
        //   （＝跳ねるのはカードの枠だけ。文字は動かない方が読める）。
        float ix = CardCenter.X - CardW * 0.5f, iy = CardCenter.Y - CardH * 0.5f + dy;
        float pad = 26f;

        // ③ アイコン（丸）。char/v3/icons のモブアイコンがあればそれ、無ければアカウント色の無地円。
        float av = 27f, ax = ix + pad + av, ay = iy + pad + av;
        DrawIcon(new Vector2(ax, ay), av, a);

        // ④ 頭の行：表示名（太）→ 認証バッジ → @ハンドル · 時刻（Hub の feed カードと同じ並び・同じ書体）。
        float tx = ix + pad + av * 2f + 16f;
        UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, iy + pad + 2f), _name, UiKit.FontSpeaker, new Color(UiKit.White, a));
        float mx = tx + UiKit.TextW(UiKit.ZenBold, _name, UiKit.FontSpeaker) + 12f;
        if (_verified) { UiKit.VerifiedBadge(this, new Vector2(mx + 7f, iy + pad + 12f), 7f, UiKit.Purify, a); mx += 22f; }
        UiKit.Text(this, UiKit.Mono, new Vector2(mx, iy + pad + 8f), _handle, UiKit.FontLabel, new Color(UiKit.Text3, a));
        float hw = UiKit.TextW(UiKit.Mono, _handle, UiKit.FontLabel);
        UiKit.Text(this, UiKit.Mono, new Vector2(mx + hw + 8f, iy + pad + 8f), _relT, UiKit.FontLabel, new Color(UiKit.Text4, a));

        // ⑤ 本文（2行まで。読めることが第一なので本文だけは大きく）。
        float bx = tx, by = iy + pad + 44f, bw = CardW - (bx - ix) - pad;
        var ink = new Color(232 / 255f, 224 / 255f, 240 / 255f, a);
        UiKit.Multi(this, UiKit.Zen, new Vector2(bx, by), _body, UiKit.FontBody, ink, bw, 2);
        // 書きかけ：本文の末尾でカーソルが明滅する（CommentInput と同じ「まだ点いている」印）。
        if (_caret && _outT < 0)
        {
            // 折り返し後の末尾行に合わせる（本文は最大2行）。
            var lines = UiKit.WrapLines(UiKit.Zen, _body, UiKit.FontBody, bw);
            int li = Mathf.Max(0, lines.Count - 1);
            string tail = lines.Count > 0 ? lines[li] : "";
            float cx = bx + UiKit.TextW(UiKit.Zen, tail, UiKit.FontBody) + 2f;
            float cy = by + li * (UiKit.FontBody * 1.55f);
            if (((int)(_t * 2.2)) % 2 == 0)
                DrawRect(new Rect2(cx, cy + 3f, 2f, UiKit.FontBody * 1.15f), new Color(1f, 1f, 1f, 0.82f * a));
        }

        // ⑥ 下の行：書きかけなら送信ボタン、出来上がった投稿ならエンゲージ行（点4つ＋薄い数字）。
        float ry = iy + CardH - pad - 14f;
        if (_composing) DrawSendButton(ix, ry, a);
        else DrawEngagement(bx, ry, bw, a);

        UiKit.EndDesign(this);
    }

    // 丸アイコン。char/v3/icons のモブアイコンがあればそれ、無ければ無地円＋人影
    //   （Hub.DrawFillerAvatar と同じ落とし方＝アイコンが未生成でも「誰か」に見える）。
    private void DrawIcon(Vector2 c, float r, float a)
    {
        if (_iconTex != null)
        {
            UiKit.FaceAvatar(this, c, r, _iconTex, new Color(UiKit.Text4, 0.55f), false, 0f, a * 0.95f);
            return;
        }
        DrawCircle(c, r, new Color(0.16f, 0.15f, 0.21f, 0.92f * a));
        DrawArc(c, r, 0f, Mathf.Tau, 28, new Color(1, 1, 1, 0.10f * a), 1f);
        var sil = new Color(UiKit.Text4, 0.55f * a);
        DrawCircle(new Vector2(c.X, c.Y - r * 0.21f), r * 0.29f, sil);                              // 頭
        DrawColoredPolygon(new[] { new Vector2(c.X - r * 0.46f, c.Y + r * 0.54f), new Vector2(c.X + r * 0.46f, c.Y + r * 0.54f),
                                   new Vector2(c.X + r * 0.33f, c.Y + r * 0.17f), new Vector2(c.X - r * 0.33f, c.Y + r * 0.17f) }, sil); // 肩
    }

    // 送信ボタン（CommentInput と同じ：ふだん沈んでいて、送られた瞬間だけ一度灯る）。
    private void DrawSendButton(float ix, float ry, float a)
    {
        var btn = new Rect2(ix + CardW - 26f - 104f, ry - 12f, 104f, 40f);
        float glow = _sendGlow > 0 ? (float)(_sendGlow / SendGlowDur) : 0f;
        UiKit.Box(this, btn, new Color(UiKit.Purify, (0.14f + 0.46f * glow) * a), 10f,
            new Color(UiKit.Purify, (0.35f + 0.45f * glow) * a), 1.2f);
        const string label = "ポストする";
        float lw = UiKit.TextW(UiKit.ZenBold, label, UiKit.FontLabel);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(btn.Position.X + (btn.Size.X - lw) * 0.5f, btn.Position.Y + 12f),
            label, UiKit.FontLabel, new Color(1f, 1f, 1f, (0.45f + 0.5f * glow) * a));
        if (glow > 0f) UiKit.RadialGlow(this, btn.Position + btn.Size * 0.5f, 150f, UiKit.Purify, 0.22f * glow * a);
    }

    // エンゲージ行：点4つ＋薄い数字（返信・リポスト・いいね・閲覧の順）。図像は描かない
    //   ＝src/StageImagery.cs の背景板と同じ作法（同じ画面言語で揃える）。
    private void DrawEngagement(float bx, float ry, float bw, float a)
    {
        var dot = new Color(UiKit.Text3, 0.75f * a);
        string[] nums = { _replies.ToString(), _reposts.ToString(), _likes.ToString(), Abbrev(_views) };
        float step = bw / 4f;
        for (int k = 0; k < 4; k++)
        {
            float dx = bx + k * step;
            DrawCircle(new Vector2(dx + 4f, ry + 6f), 3.4f, dot);
            UiKit.Text(this, UiKit.Mono, new Vector2(dx + 14f, ry), nums[k], UiKit.FontSmall, new Color(UiKit.Text3, 0.85f * a));
        }
    }

    // 閲覧数は大きくなりがちなので 1.2万 / 980 のように省略（StageImagery.FmtCount と同じ表記）。
    private static string Abbrev(int n) => n >= 10000 ? $"{n / 1000 / 10f:0.#}万" : n.ToString();
}
