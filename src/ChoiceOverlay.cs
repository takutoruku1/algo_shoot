using Godot;
using System.Collections.Generic;

// ChoiceOverlay : 会話中の2択選択UI（「言いかけの言葉」演出版）。
//   旧メニュー箱（暗幕＋UiKit.Box＋▸カーソル）を廃し、選択肢を「ミナが言いかけている言葉の断片」として
//   戦場（QuietVeil の鈍色のまま）に直接浮かべる。Hud(CanvasLayer) にぶら下げて全画面に描く。
//
// 使い方（StageKoharu.Step_MidChoice 参照）── 外部APIは旧版と不変:
//   _choice = ChoiceOverlay.Show(Hud, new[] { "選択肢A", "選択肢B" }, defaultSel: 1);
//   ... _choice.Decided が立ったら _choice.Selected を読み、QueueFree する（後始末は呼び出し側の責務）。
//
// 演出タイムライン（予備動作→本動作→余韻。yoshida style §4）:
//   出現 0〜0.55s … 光の粒が集まりながら1文字ずつ滲んで現れる（アンティシペーション）。完了まで決定不可。
//   待機         … 断片は自機の左上／左下でゆっくり上下に漂う（言葉弾と同じ静かな浮遊）。
//                  選択中＝明るく・わずかに拡大・呼吸のようにゆっくり明滅。非選択＝輝度40%。
//   決定 0〜0.5s … 選ばれた断片が吹き出しの方へふわりと昇って光に溶ける（＝彼女の言葉になる連続性）。
//                  選ばれなかった断片は淡い光の粒に散る（浄化と同じ語彙）。解散が終わってから Decided を立てる。
//   音           … カーソル移動＝SfxUiMove を小さく低く／決定＝会話送りと同じミナのタイプ音（TypMina）。
//                  メニュー確認音（PlayUiConfirm）は使わない＝これはメニューではなく「言葉を選ぶ」行為。
//
// 操作ヒント: 表示直後は出さない。無入力が2秒続いたら下部に小さくフェードイン、入力があれば再び隠す。
// 沈黙も選択: 無入力が14秒続くと「ひきさがる」（末尾の選択肢）がひとりでに柔らかく灯りはじめ、
//   20秒で自動的にそれが選ばれる（演出は通常決定と同じ・少し静かに）。ツリーポーズ中は _Process ごと
//   止まるのでタイマーも停止する。
//
// 仕様（docs/20260831/会話選択_層2_プロト仕様.md）:
//   ・X キャンセルは付けない＝必ずどちらかを選ばせる（収束型2択）。
//   ・パッドは十字↑↓＋A（ui_up/ui_down は十字キーにマップ済み）。
//   ・マウス（Settings/Hub と同じ流儀）: UiKit.BeginHotspots＋Hotspot＋HoveredId＋Pad.MouseClick。
//     断片のテキスト矩形ホバーで選択移動（Pad.UsingMouse 中のみ）、その上で左クリック＝決定。
//     矩形外のクリックは何もしない。0.25s ゲート＝表示直後の押下（会話送りの残り）を拾わない。
//   ・提示中は呼び出し側が会話バブルを保持（HoldBubble）＝Hud.BubblePaused 継続で弾・敵は止まったまま。
//
// 自動プレイ互換（--qa/--demo）: QaPilot/DemoPilot は Hud.BubblePaused 中に Z をパルスし続ける。
//   出現完了（0.55s）までの Z は無視されるが、パルスは続くので直後の1発で確定し、解散演出（0.5s）後に
//   Decided が立って先へ進む＝ソフトロックしない（沈黙タイマーの自動決定はその遥か手前で無関係）。
public partial class ChoiceOverlay : Control
{
    public bool Decided { get; private set; }   // 決定済みか（解散演出が終わってから立つ。立ったら Selected を読む）
    public int Selected { get; private set; }   // 現在カーソル／確定した選択肢の添字

    private string[] _choices = System.Array.Empty<string>();
    private string[] _disp = System.Array.Empty<string>();   // 表示用（鉤括弧付き）。文言そのものは変えない
    private Vector2[] _pos = System.Array.Empty<Vector2>();  // 各断片のテキスト左上（設計座標・浮遊前）
    private float[] _w = System.Array.Empty<float>();        // 各断片の基本テキスト幅（FontSize 時）

    private double _t;
    // 生成フレームからの押しっぱなしをエッジ扱いしない（直前の会話送りZで即決させない）。
    private bool _navHeld = true;
    private bool _zHeld = true;

    // 決定後の解散演出（この間に Decided はまだ立てない）。
    private bool _deciding;
    private double _decideT;
    private bool _quiet;          // 沈黙の自動決定＝少し静かに（音を絞り・粒を減らす）
    private float _trailAcc;

    // 操作ヒント（無入力2秒でフェードイン）と沈黙タイマー（14秒で灯り・20秒で自動決定）。
    private double _idleT, _silenceT;
    private float _hintA;
    private Vector2 _lastMouse;

    // 光の粒（解散の散り・昇りのトレイル）。設計座標で保持・描画。
    private struct Mote { public Vector2 P, V; public float Life, Max, R; public Color C; }
    private readonly List<Mote> _motes = new();
    private readonly RandomNumberGenerator _rng = new();

    private const float AppearDur = 0.55f;    // 出現（粒の集合＋1文字ずつの滲み）
    private const float CharStagger = 0.04f;  // 文字ごとの出現ずらし
    private const float CharFade = 0.12f;     // 1文字の滲み時間
    private const float DissolveDur = 0.5f;   // 決定→解散（この後に Decided）
    private const double HintDelay = 2.0;
    private const double SilenceWarm = 14.0;  // ここから「ひきさがる」が灯りはじめる
    private const double SilenceAuto = 20.0;  // 自動決定
    private const int FontSize = 20;
    private static readonly Vector2 BubbleTarget = new(640f, 535f); // 吹き出し（Hud.DrawDialog: 40,520,1200,170）の上辺中央

    public static ChoiceOverlay Show(Node parent, string[] choices, int defaultSel)
    {
        var c = new ChoiceOverlay { Name = "ChoiceOverlay", _choices = choices, Selected = defaultSel };
        parent.AddChild(c);
        return c;
    }

    public override void _Ready()
    {
        // 実画面(384x216)全域に重ねる。描画は UiKit.BeginDesign で設計座標(1280x720)に変換して行う。
        Size = new Vector2(384f, 216f);
        MouseFilter = MouseFilterEnum.Ignore;
        _rng.Randomize();
        _lastMouse = Pad.MousePos();

        // 断片の配置：自機（会話中は固定）の左上／左下あたり。自機が取れなければ画面中央左を既定に。
        //   自機はワールド座標なので、カメラ込みのキャンバス変換（384系）→ UiKit.Scale で設計座標へ。
        Vector2 anchor = new(430f, 380f);
        if (GetTree()?.GetFirstNodeInGroup("player") is Node2D pl)
            anchor = pl.GetGlobalTransformWithCanvas().Origin / UiKit.Scale;

        int n = _choices.Length;
        _disp = new string[n]; _pos = new Vector2[n]; _w = new float[n];
        for (int i = 0; i < n; i++)
        {
            _disp[i] = "「" + _choices[i] + "」";
            _w[i] = UiKit.TextW(UiKit.ZenBold, _disp[i], FontSize);
            // 先頭＝左上、以降＝左下へ。画面端（と下部の吹き出し）に切れない位置へクランプ。
            float ox = -_w[i] - (i == 0 ? 60f : 40f);
            float oy = i == 0 ? -160f : 70f + 95f * (i - 1);
            var p = anchor + new Vector2(ox, oy);
            p.X = Mathf.Clamp(p.X, 40f, UiKit.DesignW - _w[i] - 40f);
            p.Y = i == 0 ? Mathf.Clamp(p.Y, 100f, 330f)
                         : Mathf.Clamp(p.Y, 370f + 58f * (i - 1), 470f);
            _pos[i] = p;
        }
    }

    public override void _Process(double delta)
    {
        _t += delta;
        QueueRedraw();
        if (Decided) return;
        float dt = (float)delta;
        UpdateMotes(dt);

        // ── 解散演出中：入力は受けず、選ばれた断片のトレイルを撒きながら完了を待つ ──
        if (_deciding)
        {
            _decideT += delta;
            _trailAcc += dt;
            float step = _quiet ? 0.05f : 0.025f;
            while (_trailAcc >= step) { _trailAcc -= step; SpawnTrail(); }
            if (_decideT >= DissolveDur) Decided = true; // 解散が終わってから決定を通知（連続性の担保）
            return;
        }

        bool appeared = _t >= AppearDur; // 出現完了までは決定を受け付けない（アンティシペーション）

        // ↑↓（十字含む）で2択トグル。移動音は小さく柔らかく（SfxUiMove を絞って低く）。
        bool nav = Input.IsActionPressed("ui_up") || Input.IsActionPressed("ui_down");
        if (nav && !_navHeld)
        {
            Selected = (Selected + 1) % _choices.Length;
            if (Audio.Instance is { } au1) au1.Se(au1.SfxUiMove, volDb: -27f, pitch: 0.8f);
        }
        _navHeld = nav;

        // マウス：断片のテキスト矩形をホットスポット登録し、ホバーで選択追従（Pad.UsingMouse 中のみ）。
        //   座標系は RowRect＝設計座標(1280×720)で、Pad.MousePos() も設計座標を返すため換算不要。
        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < _choices.Length; i++)
            UiKit.Hotspot(RowRect(i), i);
        int hov = UiKit.HoveredId();
        if (Pad.UsingMouse && hov >= 0 && hov != Selected)
        {
            Selected = hov;
            if (Audio.Instance is { } au2) au2.Se(au2.SfxUiMove, volDb: -27f, pitch: 0.8f);
        }
        // 断片の上で左クリック＝決定。外のクリックは何もしない（誤爆防止）。0.25s ゲート維持。
        bool click = Pad.MouseClick();
        if (click && hov >= 0 && _t >= 0.25 && appeared)
        {
            Selected = hov;
            StartDissolve(quiet: false);
            return;
        }

        // Z/Enter/Pad A で決定（会話送りと違いマウス左クリックは含めない＝上の矩形クリック経路に分離）。
        // X キャンセルは意図的に無し（必ず選ばせる）。ポーズ中はツリーポーズで本 _Process ごと止まる。
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter)
                 || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;
        if (zEdge && _t >= 0.25 && appeared)
        {
            StartDissolve(quiet: false);
            return;
        }

        // ── 操作ヒント（無入力2秒でフェードイン・入力で隠す）と沈黙タイマー ──
        var mp = Pad.MousePos();
        bool mouseMoved = (mp - _lastMouse).Length() > 6f;
        _lastMouse = mp;
        if (nav || z || click || mouseMoved) { _idleT = 0; _silenceT = 0; }
        else { _idleT += delta; _silenceT += delta; }
        float target = _idleT >= HintDelay ? 1f : 0f;
        _hintA = Mathf.MoveToward(_hintA, target, dt * (target > 0.5f ? 2.8f : 6f));

        // 沈黙も選択：20秒で「ひきさがる」（末尾）を自動決定。演出は通常決定と同じ・少し静かに。
        if (_silenceT >= SilenceAuto)
        {
            Selected = _choices.Length - 1;
            StartDissolve(quiet: true);
        }
    }

    // ── 決定→解散を開始。決定音は会話送りと同じミナのタイプ音（メニュー確認音は使わない）──
    private void StartDissolve(bool quiet)
    {
        _deciding = true;
        _decideT = 0;
        _quiet = quiet;
        _trailAcc = 0;
        if (Audio.Instance is { } au)
        {
            if (quiet) au.VoiceSe(au.TypMina, volDb: -26f);       // 沈黙の自動決定＝さらに静かに
            else au.PlayType(Hud.LineKind.Mina);                  // 会話送り（ミナの声＝ガラス）と同じ音
        }
        // 選ばれなかった断片＝淡い光の粒に散る（浄化と同じ語彙）。粒はすべて DissolveDur 内に消える寿命。
        int per = quiet ? 1 : 2;
        for (int i = 0; i < _choices.Length; i++)
        {
            if (i == Selected) continue;
            var basePos = FragBasePos(i);
            for (int j = 0; j < _disp[i].Length; j++)
            {
                float cx = basePos.X + UiKit.TextW(UiKit.ZenBold, _disp[i].Substring(0, j), FontSize) + 8f;
                for (int m = 0; m < per; m++)
                {
                    float ang = _rng.RandfRange(-Mathf.Pi * 0.85f, -Mathf.Pi * 0.15f); // 上方向へ散る
                    float spd = _rng.RandfRange(26f, 70f);
                    _motes.Add(new Mote
                    {
                        P = new Vector2(cx, basePos.Y + 14f),
                        V = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd,
                        Life = _rng.RandfRange(0.28f, 0.46f),
                        Max = 0.46f,
                        R = _rng.RandfRange(1.6f, 3.4f),
                        C = UiKit.PurifyHi.Lerp(UiKit.Mina, _rng.Randf() * 0.5f),
                    });
                }
            }
        }
    }

    // 選ばれた断片の昇りに沿って光の粒を1つ撒く（トレイル＝言葉が光へほどけていく余韻）。
    private void SpawnTrail()
    {
        float d = Mathf.Clamp((float)(_decideT / DissolveDur), 0f, 1f);
        var p = ChosenPos(d);
        _motes.Add(new Mote
        {
            P = p + new Vector2(_rng.RandfRange(0f, _w[Selected]), _rng.RandfRange(4f, 24f)),
            V = new Vector2(_rng.RandfRange(-8f, 8f), _rng.RandfRange(-24f, -10f)),
            Life = _rng.RandfRange(0.20f, 0.34f),
            Max = 0.34f,
            R = _rng.RandfRange(1.4f, 2.8f),
            C = UiKit.PurifyHi,
        });
    }

    private void UpdateMotes(float dt)
    {
        for (int k = _motes.Count - 1; k >= 0; k--)
        {
            var m = _motes[k];
            m.Life -= dt;
            if (m.Life <= 0f) { _motes.RemoveAt(k); continue; }
            m.V *= Mathf.Exp(-2.6f * dt);   // 減衰
            m.V.Y -= 18f * dt;              // 光はゆっくり浮き上がる（浄化の語彙）
            m.P += m.V * dt;
            _motes[k] = m;
        }
    }

    // 断片のテキスト矩形（設計座標）。_Draw と _Process のホットスポット登録で共有＝座標系ずれを防ぐ。
    // 浮遊（±4px）はパディング内に収まるので矩形は固定でよい。
    private Rect2 RowRect(int i)
        => new(_pos[i].X - 16f, _pos[i].Y - 10f, _w[i] + 32f, 48f);

    // 浮遊込みの基本位置（言葉弾と同じ語彙の、静かな上下ドリフト。周期≒3.3s・±4px）。
    private Vector2 FragBasePos(int i)
        => _pos[i] + new Vector2(0f, 4f * Mathf.Sin((float)_t * 1.9f + i * 2.6f));

    // 決定後の選ばれた断片の位置：ふわりと持ち上がり（+30px の山）、吹き出しの方へ滑らかに寄りながら溶ける。
    // 全行程は移動しきらず 55% 付近で光に溶け切る＝「届く途中で言葉になる」余韻。
    private Vector2 ChosenPos(float d)
    {
        var p = FragBasePos(Selected);
        var target = new Vector2(BubbleTarget.X - _w[Selected] * 0.5f, BubbleTarget.Y);
        float k = d * d; // ease-in：ゆっくり離れて、光に吸われるように加速
        p = p.Lerp(target, k * 0.55f);
        p.Y -= 30f * Mathf.Sin(Mathf.Min(1f, d * 1.3f) * Mathf.Pi);
        return p;
    }

    // 出現の進捗：文字 j の滲みα（0=未出現〜1=定着）。
    private float CharAppear(int j)
        => Mathf.Clamp(((float)_t - (0.06f + j * CharStagger)) / CharFade, 0f, 1f);

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        float dis = _deciding ? Mathf.Clamp((float)(_decideT / DissolveDur), 0f, 1f) : 0f;

        for (int i = 0; i < _choices.Length; i++)
        {
            bool sel = i == Selected;
            bool chosen = _deciding && sel;
            bool dropped = _deciding && !sel;

            // 位置と全体α
            Vector2 p = chosen ? ChosenPos(dis) : FragBasePos(i);
            float alpha = 1f;
            if (chosen) alpha = 1f - Mathf.SmoothStep(0.25f, 1f, dis);            // 後半で光に溶ける
            if (dropped) alpha = Mathf.Clamp(1f - (float)_decideT / 0.22f, 0f, 1f); // 散る側は素早く淡く（粒が引き継ぐ）
            if (alpha <= 0f) continue;

            // 色：ミナの台詞色。選択中は明るく＋呼吸のようにゆっくり明滅、非選択は輝度40%へ沈む。
            float breath = 0.5f + 0.5f * Mathf.Sin((float)_t * 2.5f);
            Color col = sel ? UiKit.Mina.Lerp(UiKit.White, 0.40f + 0.20f * breath)
                            : UiKit.Mina.Darkened(0.60f);
            // 沈黙の灯り：14秒から末尾の断片（ひきさがる）がひとりでに柔らかく灯りはじめる。
            float warm = 0f;
            if (!sel && !_deciding && i == _choices.Length - 1 && _silenceT > SilenceWarm)
            {
                warm = Mathf.Clamp((float)((_silenceT - SilenceWarm) / (SilenceAuto - SilenceWarm)), 0f, 1f);
                col = col.Lerp(UiKit.Light, warm * 0.55f);
            }

            // サイズ：選択中はわずかに拡大。選ばれた断片は昇りながらさらに少し伸びる（光に近づく）。
            int size = sel ? FontSize + 1 : FontSize;
            if (chosen) size = FontSize + 1 + (int)(3f * dis);
            float wNow = UiKit.TextW(UiKit.ZenBold, _disp[i], size);
            var center = new Vector2(p.X + wNow * 0.5f, p.Y + 14f);

            // やわらかい発光（ボックスの代わりの「気配」）。選択中は呼吸、決定中は溶ける光へ膨らむ。
            float appearK = Mathf.Clamp((float)_t / AppearDur, 0f, 1f);
            float glowA = sel ? 0.10f + 0.05f * breath : 0.05f;
            if (chosen) glowA = 0.10f + 0.30f * dis;
            UiKit.RadialGlow(this, center, wNow * 0.75f + 24f, UiKit.Mina, glowA * alpha * appearK);
            if (warm > 0f)
                UiKit.RadialGlow(this, center, wNow * 0.65f, UiKit.Light, 0.10f * warm);

            // 1文字ずつ：出現中は光の粒が集まりながら滲む。定着後は影＋本体（生テキスト・箱なし）。
            for (int j = 0; j < _disp[i].Length; j++)
            {
                float a = CharAppear(j);
                float cx = p.X + UiKit.TextW(UiKit.ZenBold, _disp[i].Substring(0, j), size);
                var cc = new Vector2(cx + 9f, p.Y + 14f);

                // 集まる粒：文字の定着前後だけ、周囲から渦を巻いて寄ってくる（決定論ハッシュ＝ちらつかない）。
                if (a < 1f && _t < AppearDur + 0.2)
                {
                    float pre = Mathf.Clamp(((float)_t - (0.06f + j * CharStagger - 0.16f)) / 0.16f, 0f, 1f);
                    for (int m = 0; m < 3; m++)
                    {
                        float h1 = Hash(i * 131 + j * 17 + m * 7);
                        float h2 = Hash(i * 57 + j * 29 + m * 13 + 999);
                        float ang = h1 * Mathf.Tau + (float)_t * (0.6f + h2);
                        float rad = (1f - a) * (14f + 12f * h2) + 2f;
                        var mpnt = cc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                        float ma = pre * (1f - a) * 0.6f;
                        if (ma > 0.01f)
                            DrawCircle(mpnt, 1.6f, new Color(UiKit.PurifyHi, ma * alpha));
                    }
                }
                if (a <= 0f) continue;

                string ch = _disp[i][j].ToString();
                float ca = a * a * alpha; // 滲み＝ゆっくり濃くなる
                // 影（戦場の上に直乗せする生テキストの可読性を担保）
                UiKit.Text(this, UiKit.ZenBold, new Vector2(cx + 1.2f, p.Y + 1.2f), ch, size,
                    new Color(0f, 0f, 0f, 0.55f * ca));
                UiKit.Text(this, UiKit.ZenBold, new Vector2(cx, p.Y), ch, size, new Color(col, ca));
            }
        }

        // 光の粒（散り・トレイル）
        foreach (var m in _motes)
        {
            float ma = Mathf.Clamp(m.Life / m.Max, 0f, 1f) * 0.8f;
            DrawCircle(m.P, m.R, new Color(m.C, ma));
        }

        // 操作ヒント：無入力2秒でだけ、下部に小さくフェードイン（表示直後は出さない）。
        if (_hintA > 0.01f && !_deciding)
            UiKit.Text(this, UiKit.Mono, new Vector2(0f, 692f),
                "↑↓ / マウス えらぶ　" + Pad.ConfirmToken + " けってい", UiKit.FontSmall,
                new Color(UiKit.Text3, 0.9f * _hintA), HorizontalAlignment.Center, UiKit.DesignW);

        UiKit.EndDesign(this);
    }

    // 決定論ハッシュ（0〜1）。出現の集合粒を毎フレーム同じ軌道で描くために使う。
    private static float Hash(int a)
    {
        unchecked
        {
            uint x = (uint)a * 2654435761u;
            x ^= x >> 13; x *= 1274126177u; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777215f;
        }
    }
}
