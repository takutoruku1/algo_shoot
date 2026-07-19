using Godot;

// AreaStrike : 範囲攻撃テレグラフ（RefrainArenaHTML 移植）。ボスの範囲技と道中ドローンのビームで共用。
//   予兆（点滅輪郭＋範囲を囲う）→ 充填（範囲が満ちる）→ 着弾（白熱フラッシュ・このフレームのみ判定）の3段。
//   形状＝予測線（横/縦/任意向きビーム）・予測エリア（円/矩形）。色はキャラの心象色（tint）＋着弾の明色（hot）。
//   弾の下・背景の上（ZIndex -10）の“床マーカー”として描き、弾の視認を妨げない。
//   予兆中は当たり判定なし、着弾の瞬間にだけ範囲内の自機を被弾させる（必ず予告＝§7 理不尽回避）。
//
// 使い方（軸ビーム/円/矩形）：
//   var z = new AreaStrike();
//   z.Configure(AreaStrike.Shape.BeamH, halfW, halfH, warn, tint, hot);
//   world.AddChild(z); z.GlobalPosition = center;
//
// 使い方（任意向きビーム＝ドローンのロックオンビーム等）：自機方向へ伸びる線分。
//   var z = new AreaStrike();
//   z.ConfigureBeam(dir, length, halfThick, warn, tint, hot);
//   world.AddChild(z); z.GlobalPosition = origin;

// QA用の観測インターフェース：QaPilot が「今この点は正規の範囲攻撃に覆われているか」を
// 型に依存せず走査できるようにする（"aoe" グループと対。AreaStrike / CorridorRun が実装）。
// ゲーム本編では誰も参照しない＝被弾分類（AOE被弾を suspicious-hit にしない）専用。
public interface IAoeHazard
{
    bool IsStriking { get; }          // 今まさに致死判定が生きているか
    bool CoversPoint(Vector2 p);      // 点 p が被弾域内か
}

public partial class AreaStrike : Node2D, IAoeHazard
{
    public enum Shape { BeamH, BeamV, Circle, Rect, BeamSeg, Fullscreen }

    // 危険形状（円/矩形/ビーム）の判定マージン：負値＝描画縁より 1.5px 内側までしか当たらない
    //（＝縁ギリギリは安全。旧 +2.5f は縁より外まで当たり「見た目を信じて避けたのに被弾」の理不尽。
    //   sakurai 2026-07 週次：床マーカーの見た目＝真実、を徹底する）。
    private const float PlayerHit = -1.5f;
    // 全画面AOEの安置(セーフゾーン)判定は現状据え置き（縁+2.5pxまで安全＝自機半径ぶんの許し）。
    private const float SafeHit = 2.5f;
    private const double StrikeFlash = 0.20;  // 着弾フラッシュの尺
    private const float W = 384f, H = 216f;   // 全画面AOEの画面寸法

    // Fullscreen（全画面AOE）専用：画面全体を被弾域にし、安置(セーフゾーン)円だけをくり抜く。
    // _safeR<=0 なら安置なし＝全面（避けられない＝予告で必ず逃げ切れる短い警告と併用）。
    private Vector2 _safeCenter;
    private float _safeR;

    private Shape _shape;
    private float _hw, _hh;                    // 矩形/ビームの半幅・半高（円は _hw を半径に使う）
    private float Radius => _hw;
    private double _warn = 1.2;
    private Color _tint = new Color(1f, 0.34f, 0.30f);
    private Color _hot = new Color(1f, 0.92f, 0.7f);

    // BeamSeg（任意向きビーム）専用：原点(=GlobalPosition)から _segDir 方向へ _segLen 伸びる線分。
    // 半太さ _hh で当たり判定／描画する（線分なので原点から片方向に伸びる＝矩形ビームの軸非依存版）。
    private Vector2 _segDir = new Vector2(-1, 0);
    private float _segLen;

    private double _t;
    private bool _struck;

    // 発生源（任意）。設定すると、着弾前に発生源が消滅/浄化された時点で予測線ごとキャンセルする。
    //   ＝「予兆中に倒せば攻撃も消える」。道中ザコのロックオンビーム＋ボスの範囲技(AreaSpellCaster)で使う。
    //   ボスは改心(IsPurified)した瞬間に出現済みの予兆まで自滅させ、「攻撃が終わったのに後から着弾する」残留を断つ。
    private Node2D? _owner;
    private bool _cancelOnOwnerLoss;
    public void SetOwner(Node2D owner) { _owner = owner; _cancelOnOwnerLoss = true; }

    // QA用の観測フック：QaPilot が被弾分類（AOE被弾を suspicious-hit にしない）に使う。
    //   IsStriking … 着弾済み（着弾フラッシュ0.2sの間 true のまま残る）
    //   CoversPoint … 点 p が被弾域内か（安置くり抜き含む Inside と同一判定）
    public bool IsStriking => _struck;
    public bool CoversPoint(Vector2 p) => Inside(p);

    // 全形状共通で "aoe" グループに入れる（QaPilot が走査する）。ゲーム本編では誰も参照しない。
    public override void _Ready() => AddToGroup("aoe");

    public void Configure(Shape shape, float halfW, float halfH, double warn, Color tint, Color hot)
    {
        _shape = shape; _hw = halfW; _hh = halfH;
        _warn = Mathf.Max(0.35, warn);
        _tint = tint; _hot = hot;
        ZIndex = -10; ZAsRelative = false;
    }

    // 全画面AOE。画面全体が被弾域で、安置(セーフゾーン)円(safeCenter/safeR)だけが安全。
    // safeR<=0 で安置なしの全面型。位置は画面基準で固定するので GlobalPosition=Zero で AddChild する。
    // 弾の下・背景の上に描く他形状と違い、全面tintは弾より上にも欲しいので ZIndex を少し上げる。
    public void ConfigureFullscreen(Vector2 safeCenter, float safeR, double warn, Color tint, Color hot)
    {
        _shape = Shape.Fullscreen;
        _safeCenter = safeCenter;
        _safeR = Mathf.Max(0f, safeR);
        _warn = Mathf.Max(0.35, warn);
        _tint = tint; _hot = hot;
        ZIndex = 5; ZAsRelative = false; // 弾(0)より上・自機(10)より下で画面を満たす
    }

    // 任意向きビーム（線分）。dir 方向へ length 伸び、半太さ halfThick で判定する。
    // 予兆中は予測線（細い危険色ライン）を出すだけで当たらず、着弾フレームだけ線分上の自機を被弾させる。
    // 位置は他形状と同じく AddChild 後に GlobalPosition=発射源 を設定して使う。
    public void ConfigureBeam(Vector2 dir, float length, float halfThick,
        double warn, Color tint, Color hot)
    {
        _shape = Shape.BeamSeg;
        _segDir = dir.LengthSquared() > 0.0001f ? dir.Normalized() : new Vector2(-1, 0);
        _segLen = Mathf.Max(8f, length);
        _hh = Mathf.Max(1f, halfThick);
        _hw = _segLen; // 描画/便宜用（未使用経路の保険）
        _warn = Mathf.Max(0.35, warn);
        _tint = tint; _hot = hot;
        ZIndex = -10; ZAsRelative = false;
    }

    public override void _Process(double delta)
    {
        // 発生源が着弾前に消えた／浄化されたら、予測線ごとキャンセル（倒せば攻撃も消える）。
        if (!_struck && _cancelOnOwnerLoss
            && (_owner == null || !IsInstanceValid(_owner) || (_owner is Enemy e && e.IsPurified)))
        {
            QueueFree();
            return;
        }

        // 会話中（吹き出し表示中）は弾と同じく時間を止める＝動けない自機に着弾させない。
        if (Hud.BubblePaused) return;

        _t += delta;
        if (!_struck && _t >= _warn) { _struck = true; Strike(); }
        if (_t >= _warn + StrikeFlash) { QueueFree(); return; }
        QueueRedraw();
    }

    private bool Inside(Vector2 p)
    {
        // 全画面AOE：安置(セーフゾーン)円の内側だけ安全。安置外（または安置なし）は全員被弾。
        // p は画面座標そのまま（このノードは GlobalPosition=Zero で置く）。
        if (_shape == Shape.Fullscreen)
        {
            if (_safeR <= 0f) return true; // 安置なし＝全面
            return p.DistanceTo(_safeCenter) > _safeR + SafeHit; // 安置外なら被弾
        }
        Vector2 d = p - GlobalPosition;
        if (_shape == Shape.Circle) return d.Length() <= Radius + PlayerHit;
        // 任意向きビーム：原点→_segLen の線分への最短距離が（半太さ＋自機半径）以内なら被弾。
        if (_shape == Shape.BeamSeg)
        {
            float along = Mathf.Clamp(d.Dot(_segDir), 0f, _segLen); // 線分上に投影（端でクランプ）
            Vector2 nearest = _segDir * along;
            return (d - nearest).Length() <= _hh + PlayerHit;
        }
        return Mathf.Abs(d.X) <= _hw + PlayerHit && Mathf.Abs(d.Y) <= _hh + PlayerHit;
    }

    // 着弾：範囲内に自機がいれば被弾（無敵判定は Player 側）。閃光＋軽いシェイク。
    // QAの --god（無敵進行テスト）中だけ被弾をスキップする。GodClear は敵弾しか消せず、
    // 範囲攻撃で assist 走行が削られてしまうため。通常プレイでは QaPilot.GodActive は常に false。
    private void Strike()
    {
        if (!QaPilot.GodActive && GetTree().GetFirstNodeInGroup("player") is Player p && Inside(p.GlobalPosition))
            p.TakeHit();
        // 全画面AOEは画面全体の着弾＝強めに揺らす（他形状は従来どおり軽く）。
        if (_shape == Shape.Fullscreen) GameCamera.Instance?.Shake(6.5f, 0.22f);
        else GameCamera.Instance?.Shake(3.4f, 0.16f);
    }

    public override void _Draw()
    {
        if (!_struck) DrawTelegraph();
        else DrawStrike();
    }

    // 予兆＋充填（HTML準拠）：破線のマーチング輪郭＋面のベタ塗り（着弾へ濃く）＋警告マーカーで“範囲”を明示。
    private void DrawTelegraph()
    {
        float k = Mathf.Clamp((float)(_t / _warn), 0f, 1f);    // 0→1（着弾へのカウントダウン）
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_t * 9f);
        float phase = (float)_t * 46f;                          // 破線のマーチング
        Color fill = new Color(_tint.R, _tint.G, _tint.B, 0.12f + 0.26f * k); // 面のベタ塗り＝範囲を面で示す
        // 縁の明滅の下限を引き上げ（0.6→0.75）＋暗色の下縁取り：台所（こはる面）の暖色ランプ等、
        // 明るい背景でも輪郭が沈まない。危険色そのものは変えず“影”で読ませる（視認性の底上げ）。
        Color edge = new Color(_tint.R, _tint.G, _tint.B, 0.75f + 0.25f * pulse);
        Color under = new Color(0.10f, 0.04f, 0.03f, 0.55f + 0.25f * k);
        Color core = new Color(_hot.R, _hot.G, _hot.B, 0.08f + 0.14f * k);

        if (_shape == Shape.Fullscreen) { DrawFullscreenTelegraph(k, pulse); return; }

        if (_shape == Shape.Circle)
        {
            DrawCircle(Vector2.Zero, Radius, fill);
            DrawCircle(Vector2.Zero, Radius * Mathf.Lerp(0.12f, 1f, k), core); // 中心から満ちる白熱核
            DashedRing(Radius, 28, under, 3.8f, phase * 0.012f);               // 暗色の下縁取り
            DashedRing(Radius, 28, edge, 2f, phase * 0.012f);
            DrawWarn(Vector2.Zero, k);
            return;
        }

        if (_shape == Shape.BeamSeg)
        {
            Vector2 tip = _segDir * _segLen;
            DrawLine(Vector2.Zero, tip, fill, _hh * 2f);                        // 帯（面）
            DashedLine(Vector2.Zero, tip, under, 3.2f + 1.2f * k, 7f, 5f, phase); // 暗色の下縁取り
            DashedLine(Vector2.Zero, tip, edge, 1.6f + 1.2f * k, 7f, 5f, phase); // 破線の中心ガイド
            DrawCircle(Vector2.Zero, 3.2f, new Color(_tint.R, _tint.G, _tint.B, 0.85f * pulse)); // 発射源
            DrawWarn(_segDir * (_segLen * 0.5f), k);
            return;
        }

        var r = new Rect2(-_hw, -_hh, _hw * 2f, _hh * 2f);
        if (_shape == Shape.Rect)
        {
            // 矩形は角丸（他UIと同じ Clean Glass 調＝角ばらせない）。
            float rad = Mathf.Min(7f, Mathf.Min(_hw, _hh) * 0.8f);
            RoundFill(r, rad, fill);
            RoundOutline(r, rad, under, 3.8f); // 暗色の下縁取り
            RoundOutline(r, rad, edge, 2f);
            DrawWarn(Vector2.Zero, k);
            return;
        }
        // ビーム：細い帯＋両端の予測線（破線）。線に沿って警告バッジ。
        DrawRect(r, fill);
        DashedBoxBorder(under, 3.8f, phase); // 暗色の下縁取り
        DashedBoxBorder(edge, 2f, phase);
        if (_shape == Shape.BeamH)
        { DrawWarn(new Vector2(-_hw * 0.5f, 0), k); DrawWarn(new Vector2(_hw * 0.5f, 0), k); }
        else
        { DrawWarn(new Vector2(0, -_hh * 0.5f), k); DrawWarn(new Vector2(0, _hh * 0.5f), k); }
    }

    // 着弾：白熱フラッシュ（短く強く）＋輪郭バースト。
    private void DrawStrike()
    {
        float st = Mathf.Clamp((float)((_t - _warn) / StrikeFlash), 0f, 1f);
        float f = 1f - st;
        Color core = new Color(_hot.R, _hot.G, _hot.B, 0.6f * f);
        Color rim = new Color(1f, 1f, 1f, f);
        if (_shape == Shape.Fullscreen)
        {
            // 全画面着弾：画面全体を白フラッシュ。安置だけは抜く（そこにいた自機は無傷の余韻）。
            DrawRect(new Rect2(0, 0, W, H), new Color(1f, 1f, 1f, 0.85f * f));
            if (_safeR > 0f) DrawCircle(_safeCenter, _safeR, new Color(0.4f, 0.95f, 0.6f, 0.25f * f));
            return;
        }
        if (_shape == Shape.Circle)
        {
            DrawCircle(Vector2.Zero, Radius, core);
            DrawArc(Vector2.Zero, Radius, 0f, Mathf.Tau, 56, rim, 2.6f);
            DrawArc(Vector2.Zero, Radius * (1f + 0.55f * st), 0f, Mathf.Tau, 48,
                new Color(_hot.R, _hot.G, _hot.B, 0.5f * f), 2f); // 広がるバースト
            return;
        }
        if (_shape == Shape.BeamSeg)
        {
            Vector2 tip = _segDir * _segLen;
            DrawLine(Vector2.Zero, tip, core, _hh * 2f);
            DrawLine(Vector2.Zero, tip, rim, 2.6f);
            return;
        }
        var rr = new Rect2(-_hw, -_hh, _hw * 2f, _hh * 2f);
        if (_shape == Shape.Rect)
        {
            float rad = Mathf.Min(7f, Mathf.Min(_hw, _hh) * 0.8f);
            RoundFill(rr, rad, core);
            RoundOutline(rr, rad, rim, 2.6f);
            return;
        }
        DrawRect(rr, core);
        DrawBoxBorder(rim, 2.6f);
    }

    // 全画面AOEの予兆：画面全体を濁桃tintで満たし、安置(セーフゾーン)だけα0でくり抜く＋緑の脈動リング。
    //   くり抜きは「画面矩形に円の穴を空けたキーホール多角形」で実現（穴の内側は塗られない＝真にα0）。
    //   tintは着弾へ向け濃く（k）。終了直前(k>0.82)に白フレームを内側へ収束させて着弾を予告する。
    private void DrawFullscreenTelegraph(float k, float pulse)
    {
        // 画面を満たす濁桃tint（安置を穴として抜く）。弾の視認のため α は中程度までに抑える。
        Color danger = new Color(_tint.R, _tint.G, _tint.B, 0.18f + 0.30f * k);
        if (_safeR > 0f)
            DrawColoredPolygon(ScreenWithHole(_safeCenter, _safeR), danger);
        else
            DrawRect(new Rect2(0, 0, W, H), danger); // 安置なし＝全面

        if (_safeR > 0f)
        {
            // 安置：淡い緑のフィル＋脈動する緑リング（ここが安全だと一目で分かる色）。
            var green = new Color(0.4f, 0.95f, 0.6f);
            DrawCircle(_safeCenter, _safeR, new Color(green.R, green.G, green.B, 0.10f + 0.06f * pulse));
            DrawArc(_safeCenter, _safeR, 0f, Mathf.Tau, 48, new Color(green.R, green.G, green.B, 0.55f + 0.45f * pulse), 2f);
            DrawArc(_safeCenter, _safeR + 3f, 0f, Mathf.Tau, 48, new Color(green.R, green.G, green.B, 0.20f * pulse), 1f);
        }

        // 着弾予告：終了直前に画面外周から白フレームが収束（“来る”の合図）。
        if (k > 0.82f)
        {
            float c = (k - 0.82f) / 0.18f; // 0→1
            float inset = Mathf.Lerp(0f, 10f, c);
            var white = new Color(1f, 1f, 1f, 0.5f * c);
            DrawRect(new Rect2(inset, inset, W - inset * 2f, H - inset * 2f), white, false, 2.5f);
        }
    }

    // 画面矩形に circle(中心 c・半径 r)の穴を空けたキーホール多角形を返す（穴の内側は塗られない）。
    private static Vector2[] ScreenWithHole(Vector2 c, float r)
    {
        const int seg = 36;
        var pts = new System.Collections.Generic.List<Vector2>(seg + 8);
        // 外周（左上→右上→右下→左下）。最後に左上付近へ戻り、橋を渡して円へ。
        pts.Add(new Vector2(0, 0));
        pts.Add(new Vector2(W, 0));
        pts.Add(new Vector2(W, H));
        pts.Add(new Vector2(0, H));
        pts.Add(new Vector2(0, 0));
        // 橋：外周(左上)→円の最上点へ。
        Vector2 bridge = new Vector2(c.X, c.Y - r);
        pts.Add(bridge);
        // 円を一周（CW）して穴を作る。
        for (int i = 0; i <= seg; i++)
        {
            float a = -Mathf.Pi / 2f - Mathf.Tau * i / seg; // 上から時計回り
            pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
        }
        // 橋を戻して外周へ閉じる。
        pts.Add(bridge);
        pts.Add(new Vector2(0, 0));
        return pts.ToArray();
    }

    // ビーム＝範囲の両端を線で囲う（予測線）／矩形＝四辺。
    private void DrawBoxBorder(Color col, float w)
    {
        float l = -_hw, rgt = _hw, t = -_hh, b = _hh;
        if (_shape == Shape.BeamH)
        {
            DrawLine(new Vector2(l, t), new Vector2(rgt, t), col, w);
            DrawLine(new Vector2(l, b), new Vector2(rgt, b), col, w);
        }
        else if (_shape == Shape.BeamV)
        {
            DrawLine(new Vector2(l, t), new Vector2(l, b), col, w);
            DrawLine(new Vector2(rgt, t), new Vector2(rgt, b), col, w);
        }
        else // Rect
        {
            DrawLine(new Vector2(l, t), new Vector2(rgt, t), col, w);
            DrawLine(new Vector2(rgt, t), new Vector2(rgt, b), col, w);
            DrawLine(new Vector2(rgt, b), new Vector2(l, b), col, w);
            DrawLine(new Vector2(l, b), new Vector2(l, t), col, w);
        }
    }

    // 警告マーカー：危険色の丸バッジ＋白い「!」（角丸UIに合わせて丸く・小さく）。
    private void DrawWarn(Vector2 p, float k)
    {
        float s = 3.4f + 1.2f * k;
        DrawCircle(p, s, new Color(_tint.R, _tint.G, _tint.B, 0.5f));
        DrawCircle(p, s * 0.66f, new Color(_tint.R, _tint.G, _tint.B, 0.75f));
        DrawLine(p + new Vector2(0, -s * 0.45f), p + new Vector2(0, s * 0.12f), new Color(1f, 1f, 1f, 0.95f), 1.2f);
        DrawCircle(p + new Vector2(0, s * 0.45f), 0.8f, new Color(1f, 1f, 1f, 0.95f));
    }

    // ───── 角丸矩形（他UIと同じ Clean Glass 調。塗りと輪郭で共用）─────
    private static Vector2[] RoundRectPoints(Rect2 r, float rad)
    {
        rad = Mathf.Max(0.5f, Mathf.Min(rad, Mathf.Min(r.Size.X, r.Size.Y) * 0.5f));
        var pts = new System.Collections.Generic.List<Vector2>();
        Vector2[] cen =
        {
            new(r.Position.X + rad, r.Position.Y + rad),       // 左上
            new(r.End.X - rad, r.Position.Y + rad),            // 右上
            new(r.End.X - rad, r.End.Y - rad),                 // 右下
            new(r.Position.X + rad, r.End.Y - rad),            // 左下
        };
        float[] start = { Mathf.Pi, -Mathf.Pi / 2f, 0f, Mathf.Pi / 2f };
        const int seg = 4;
        for (int c = 0; c < 4; c++)
            for (int i = 0; i <= seg; i++)
            {
                float a = start[c] + (Mathf.Pi / 2f) * (i / (float)seg);
                pts.Add(cen[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad);
            }
        return pts.ToArray();
    }
    private void RoundFill(Rect2 r, float rad, Color col) => DrawColoredPolygon(RoundRectPoints(r, rad), col);
    private void RoundOutline(Rect2 r, float rad, Color col, float w)
    {
        var p = RoundRectPoints(r, rad);
        var closed = new Vector2[p.Length + 1];
        p.CopyTo(closed, 0); closed[p.Length] = p[0];
        DrawPolyline(closed, col, w);
    }

    // ───── 破線描画（マーチングする予兆輪郭。HTMLの dashed border 相当）─────
    private void DashedLine(Vector2 a, Vector2 b, Color col, float w, float dash, float gap, float phase)
    {
        Vector2 d = b - a; float len = d.Length();
        if (len < 0.001f) return;
        Vector2 dir = d / len; float step = dash + gap; float o = phase % step;
        for (float s = -o; s < len; s += step)
        {
            float s0 = Mathf.Max(0f, s), s1 = Mathf.Min(len, s + dash);
            if (s1 > s0) DrawLine(a + dir * s0, a + dir * s1, col, w);
        }
    }

    private void DashedRing(float r, int dashes, Color col, float w, float phase)
    {
        float seg = Mathf.Tau / dashes, fill = seg * 0.58f;
        for (int i = 0; i < dashes; i++)
        {
            float a0 = i * seg + phase;
            DrawArc(Vector2.Zero, r, a0, a0 + fill, 5, col, w);
        }
    }

    private void DashedBoxBorder(Color col, float w, float phase)
    {
        float l = -_hw, rgt = _hw, t = -_hh, b = _hh;
        const float dash = 8f, gap = 6f;
        if (_shape == Shape.BeamH)
        {
            DashedLine(new Vector2(l, t), new Vector2(rgt, t), col, w, dash, gap, phase);
            DashedLine(new Vector2(l, b), new Vector2(rgt, b), col, w, dash, gap, phase);
        }
        else if (_shape == Shape.BeamV)
        {
            DashedLine(new Vector2(l, t), new Vector2(l, b), col, w, dash, gap, phase);
            DashedLine(new Vector2(rgt, t), new Vector2(rgt, b), col, w, dash, gap, phase);
        }
        else
        {
            DashedLine(new Vector2(l, t), new Vector2(rgt, t), col, w, dash, gap, phase);
            DashedLine(new Vector2(rgt, t), new Vector2(rgt, b), col, w, dash, gap, phase);
            DashedLine(new Vector2(rgt, b), new Vector2(l, b), col, w, dash, gap, phase);
            DashedLine(new Vector2(l, b), new Vector2(l, t), col, w, dash, gap, phase);
        }
    }
}
