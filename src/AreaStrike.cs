using Godot;

// AreaStrike : 範囲攻撃テレグラフ（RefrainArenaHTML 移植）。ボスの範囲技と道中ドローンのビームで共用。
//   固定輪郭の予兆 → 内側の進行線 → キャラクター別の着弾（このフレームのみ判定）の3段。
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

    public enum Motif { None, Stream, Rain, Screen, Data }
    private Motif _motif = Motif.None;

    public enum Art { None, ReadReceipt, ClipLine }
    private Art _art = Art.None;
    public void SetArt(Art art) => _art = art;

    private static Texture2D? _messageTex, _readDotsTex;
    private static Texture2D MessageTex => _messageTex ??= GD.Load<Texture2D>("res://char/v3/fx/rei/bubble_empty_1.png");
    private static Texture2D ReadDotsTex => _readDotsTex ??= GD.Load<Texture2D>("res://char/v3/fx/akari/read_dots.png");
    private static readonly System.Collections.Generic.Dictionary<Motif, Texture2D[]> MotifTextures = new();
    private Texture2D[] _motifArt = System.Array.Empty<Texture2D>();
    private Vector2[][] _dangerCorners = System.Array.Empty<Vector2[]>();

    private void SetMotif(Motif motif)
    {
        _motif = motif;
        if (motif == Motif.None) return;
        if (!MotifTextures.TryGetValue(motif, out _motifArt!))
        {
            string[] paths = motif switch
            {
                Motif.Rain => new[] { "char/v3/fx/akari/card_unsent_1.png", "char/v3/bullets/akari_envelope.png" },
                Motif.Screen => new[] { "char/v3/fx/akari/phone_screen.png", "char/v3/fx/koharu/eye_cross.png" },
                Motif.Stream => new[] { "char/v3/fx/rei/bubble_empty_1.png", "char/v3/fx/rei/frame_star.png" },
                _ => new[] { "char/player/mina/mina_core_v1.png", "char/v3/fx/akari/card_unsent_1.png",
                    "char/v3/fx/rei/bubble_empty_1.png", "char/v3/fx/rei/crack.png" },
            };
            _motifArt = new Texture2D[paths.Length];
            for (int i = 0; i < paths.Length; i++) _motifArt[i] = GD.Load<Texture2D>("res://" + paths[i]);
            MotifTextures[motif] = _motifArt;
        }
    }

    // 危険形状（円/矩形/ビーム）の判定マージン：負値＝描画縁より 1.5px 内側までしか当たらない
    //（＝縁ギリギリは安全。旧 +2.5f は縁より外まで当たり「見た目を信じて避けたのに被弾」の理不尽。
    //   sakurai 2026-07 週次：床マーカーの見た目＝真実、を徹底する）。
    private const float PlayerHit = -1.5f;
    // 全画面AOEの安置(セーフゾーン)判定は現状据え置き（縁+2.5pxまで安全＝自機半径ぶんの許し）。
    private const float SafeHit = 2.5f;
    private const double StrikeFlash = 0.20;  // 着弾フラッシュの尺
    // 全画面AOEが覆う矩形＝盤面（Field）。Fullscreen は GlobalPosition=0 に置かれ、ここを画面座標のまま描く。
    private const float W = Field.Width, H = Field.Height;
    private const float L = Field.Left, T = Field.Top;

    // Fullscreen（全画面AOE）専用：画面全体を被弾域にし、安置(セーフゾーン)円だけをくり抜く。
    // _safeR<=0 は「安置なし＝回避不能」になるため本編では使わない（描画/判定の経路だけ保険で残す）。
    // 実際の発行元 AreaSpellCaster は常に _safeR>0 を渡す（§7 理不尽回避）。
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
    // QA検証ログのワンショット（集中モードで遅くなったことを、この予兆につき一度だけ出す）。
    private bool _slowLogged;
    private bool _sparked;      // 着弾スパーク（FxLayer）を撒いたか＝1回だけ

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

    public void Configure(Shape shape, float halfW, float halfH, double warn, Color tint, Color hot, Motif motif = Motif.None)
    {
        _shape = shape; _hw = halfW; _hh = halfH;
        _warn = Mathf.Max(0.35, warn);
        _tint = tint; _hot = hot;
        SetMotif(motif);
        ZIndex = -10; ZAsRelative = false;
    }

    // 全画面AOE。画面全体が被弾域で、安置(セーフゾーン)円(safeCenter/safeR)だけが安全。
    // safeR<=0 で安置なしの全面型。位置は画面基準で固定するので GlobalPosition=Zero で AddChild する。
    // 弾の下・背景の上に描く他形状と違い、全面tintは弾より上にも欲しいので ZIndex を少し上げる。
    public void ConfigureFullscreen(Vector2 safeCenter, float safeR, double warn, Color tint, Color hot, Motif motif = Motif.None)
    {
        _shape = Shape.Fullscreen;
        _safeCenter = safeCenter;
        _safeR = Mathf.Max(0f, safeR);
        _warn = Mathf.Max(0.35, warn);
        _tint = tint; _hot = hot;
        SetMotif(motif);
        BuildDangerCorners();
        ZIndex = 5; ZAsRelative = false; // 弾(0)より上・自機(10)より下で画面を満たす
    }

    // 任意向きビーム（線分）。dir 方向へ length 伸び、半太さ halfThick で判定する。
    // 予兆中は予測線（細い危険色ライン）を出すだけで当たらず、着弾フレームだけ線分上の自機を被弾させる。
    // 位置は他形状と同じく AddChild 後に GlobalPosition=発射源 を設定して使う。
    public void ConfigureBeam(Vector2 dir, float length, float halfThick,
        double warn, Color tint, Color hot, Motif motif = Motif.None)
    {
        _shape = Shape.BeamSeg;
        _segDir = dir.LengthSquared() > 0.0001f ? dir.Normalized() : new Vector2(-1, 0);
        _segLen = Mathf.Max(8f, length);
        _hh = Mathf.Max(1f, halfThick);
        _hw = _segLen; // 描画/便宜用（未使用経路の保険）
        _warn = Mathf.Max(0.35, warn);
        _tint = tint; _hot = hot;
        SetMotif(motif);
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

        // ★集中モード（#10）：予兆の時計も敵側。ここを素の delta で進めると、遅くなった弾を尻目に
        //   予兆だけが定刻で着弾する＝「遅くしたのに当たる」という最悪の読み違いを生む（必須配線）。
        float ets = GameManager.EnemyTimeScale;
        // QA走行だけ、予兆が弾と同じ倍率で遅くなっていることを1回/発動ごとにログへ出す（本番は無音）。
        if (QaPilot.Verbose && ets < 0.99f && !_slowLogged)
        {
            _slowLogged = true;
            GD.Print($"[focus] AreaStrike slowed: scale={ets:0.00} (Bullet uses the same GameManager.EnemyTimeScale) "
                   + $"warn={_warn:0.00}s -> {_warn / ets:0.00}s in real time");
        }
        _t += delta * ets;
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
        EmitImpactSparks();
    }

    // 着弾スパーク（加算・短命）。形状ごとに「どこで・どちらへ」弾けるかを変える＝形状の個性。
    //   FxLayer 未初期化（QA/ヘッドレスの一部経路）でも落ちないよう null 安全。
    private void EmitImpactSparks()
    {
        if (_sparked) return;
        _sparked = true;
        var fx = FxLayer.Instance;
        if (fx == null || !IsInstanceValid(fx)) return;

        switch (_shape)
        {
            case Shape.Fullscreen:
                // 全画面：安置の縁に沿って弾けさせる＝「安全だったのはここ」を余韻で刻む。撒きすぎない。
                if (_safeR > 0f) fx.AoeImpact(_safeCenter, _tint, _hot, Vector2.Zero, _safeR, 10);
                else fx.AoeImpact(new Vector2(Field.CenterX, Field.CenterY), _tint, _hot, Vector2.Zero, 60f, 12);
                break;
            case Shape.Circle:
                fx.AoeImpact(GlobalPosition, _tint, _hot, Vector2.Zero, Radius, 12);
                break;
            case Shape.BeamSeg:
            {
                // 線分：源・中間・先端の3点で軸方向へ散らす（走った軌跡が残る）。
                Vector2 tip = GlobalPosition + _segDir * _segLen;
                fx.AoeImpact(GlobalPosition, _tint, _hot, _segDir, _hh * 4f, 5);
                fx.AoeImpact(GlobalPosition + _segDir * (_segLen * 0.5f), _tint, _hot, _segDir, _hh * 4f, 5);
                fx.AoeImpact(tip, _tint, _hot, _segDir, _hh * 5f, 6);
                break;
            }
            case Shape.BeamH:
                fx.AoeImpact(GlobalPosition, _tint, _hot, Vector2.Right, _hh * 3f, 6);
                fx.AoeImpact(GlobalPosition + new Vector2(-_hw * 0.55f, 0f), _tint, _hot, Vector2.Right, _hh * 3f, 5);
                fx.AoeImpact(GlobalPosition + new Vector2(_hw * 0.55f, 0f), _tint, _hot, Vector2.Right, _hh * 3f, 5);
                break;
            case Shape.BeamV:
                fx.AoeImpact(GlobalPosition, _tint, _hot, Vector2.Down, _hw * 3f, 6);
                fx.AoeImpact(GlobalPosition + new Vector2(0f, -_hh * 0.55f), _tint, _hot, Vector2.Down, _hw * 3f, 5);
                fx.AoeImpact(GlobalPosition + new Vector2(0f, _hh * 0.55f), _tint, _hot, Vector2.Down, _hw * 3f, 5);
                break;
            default: // Rect
                fx.AoeImpact(GlobalPosition, _tint, _hot, Vector2.Zero, Mathf.Max(_hw, _hh), 10);
                break;
        }
    }

    public override void _Draw()
    {
        float progress = Mathf.Clamp((float)(_t / _warn), 0f, 1f);
        float fade = _struck ? 1f - Mathf.Clamp((float)((_t - _warn) / StrikeFlash), 0f, 1f) : 1f;
        float snap = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((progress - 0.82f) / 0.18f, 0f, 1f));
        Color edge = new(_tint.Lerp(_hot, snap), fade);
        Color fill = new(_struck ? _hot : _tint, (_struck ? 0.42f : 0.12f + 0.13f * progress) * fade);

        if (_shape == Shape.Fullscreen)
        {
            DrawFullscreen(progress, fade);
            return;
        }
        if (_shape == Shape.BeamSeg)
        {
            DrawSetTransform(Vector2.Zero, _segDir.Angle(), Vector2.One);
            DrawLane(new Rect2(0, -_hh, _segLen, _hh * 2), progress, fade, edge, fill);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }
        if (_shape is Shape.BeamH or Shape.BeamV)
        {
            bool vertical = _shape == Shape.BeamV;
            float length = vertical ? _hh : _hw;
            float half = vertical ? _hw : _hh;
            DrawSetTransform(Vector2.Zero, vertical ? Mathf.Pi / 2 : 0, Vector2.One);
            DrawLane(new Rect2(-length, -half, length * 2, half * 2), progress, fade, edge, fill);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }

        Color shadow = new(0.025f, 0.02f, 0.05f, 0.85f * fade);
        float width = _struck ? 2.2f : 1.1f + 0.7f * snap;
        if (_shape == Shape.Circle)
        {
            DrawCircle(Vector2.Zero, Radius, fill, true, -1, true);
            DrawMotif(new Vector2(Radius * 1.15f, Radius * 1.15f), progress, fade);
            DrawArc(Vector2.Zero, Radius, 0, Mathf.Tau, 64, shadow, width + 1.8f, true);
            DrawArc(Vector2.Zero, Radius, 0, Mathf.Tau, 64, edge, width, true);
            // 発動までの弧は危険範囲の内側に置き、実際の境界を動かさない。
            DrawArc(Vector2.Zero, Radius - 3, -Mathf.Pi / 2,
                -Mathf.Pi / 2 + Mathf.Tau * progress, 64, new Color(_hot, 0.85f * fade), 1.2f, true);
        }
        else
        {
            var rect = new Rect2(-_hw, -_hh, _hw * 2, _hh * 2);
            DrawRect(rect, fill);
            DrawMotif(new Vector2(_hw * 1.35f, _hh * 1.3f), progress, fade);
            DrawRect(rect, shadow, false, width + 1.8f);
            DrawRect(rect, edge, false, width);
            DrawFrameProgress(rect.Grow(-3), progress, new Color(_hot, 0.9f * fade));
        }
        if (_motif == Motif.None) DrawWarn(Vector2.Zero, fade);
    }

    private void DrawLane(Rect2 rect, float progress, float fade, Color edge, Color fill)
    {
        DrawRect(rect, fill);
        float thickness = _struck ? 2f : 1.2f;
        Color shadow = new(0.025f, 0.02f, 0.05f, 0.85f * fade);
        var top = rect.Position;
        var bottom = new Vector2(rect.Position.X, rect.End.Y);
        var along = new Vector2(rect.Size.X, 0);
        DrawLine(top, top + along, shadow, thickness + 1.8f);
        DrawLine(bottom, bottom + along, shadow, thickness + 1.8f);
        DrawLine(top, top + along, edge, thickness);
        DrawLine(bottom, bottom + along, edge, thickness);
        var hot = new Color(_hot, 0.85f * fade);
        DrawLine(top + Vector2.Down * 2.5f, top + Vector2.Down * 2.5f + along * progress, hot, 0.8f);
        DrawLine(bottom + Vector2.Up * 2.5f, bottom + Vector2.Up * 2.5f + along * progress, hot, 0.8f);
        if (_art == Art.ClipLine)
        {
            // The bright cut occupies the damage band, not just a thin line through a wider hitbox.
            if (_struck) DrawRect(rect.Grow(PlayerHit), new Color(_hot, 0.95f * fade));
            else
                for (float x = rect.Position.X + 4; x < rect.End.X - 4; x += 10)
                    DrawLine(new Vector2(x, 0), new Vector2(Mathf.Min(x + 4, rect.End.X - 4), 0), hot, 1f);
            return;
        }
        float span = rect.Size.X;
        int count = Mathf.Clamp((int)(span / 36f), 2, 12);
        for (int i = 0; i < count; i++)
        {
            float u = Mathf.PosMod((i + 0.5f) / count + (float)_t * 0.13f, 1f);
            float x = rect.Position.X + 10f + (span - 20f) * u;
            var at = new Vector2(x, 0);
            if (_art == Art.ReadReceipt)
                DrawReadReceipt(at, new Vector2(17, rect.Size.Y - 3), (0.45f + 0.35f * progress) * fade);
            else if (_motif != Motif.None)
                DrawStamp(i, at, new Vector2(18, rect.Size.Y - 3), (0.45f + 0.35f * progress) * fade);
            else
            {
                DrawLine(at + new Vector2(-2, -2), at, hot, 0.8f);
                DrawLine(at, at + new Vector2(-2, 2), hot, 0.8f);
            }
        }
        if (_struck)
            DrawLine(new Vector2(rect.Position.X, 0), new Vector2(rect.End.X, 0), new Color(_hot, fade), 1.8f);
    }

    private void DrawMotif(Vector2 size, float progress, float fade)
    {
        if (_motif == Motif.None) return;
        float alpha = (0.5f + progress * 0.35f) * fade;
        switch (_motif)
        {
            case Motif.Rain:
                DrawStamp(0, Vector2.Zero, size * 0.9f, alpha);
                for (int i = 0; i < 3; i++)
                {
                    float y = Mathf.Lerp(-size.Y * 0.42f, size.Y * 0.26f,
                        Mathf.PosMod((float)_t * 0.8f + i / 3f, 1f));
                    float x = size.X * (i - 1) * 0.3f;
                    DrawLine(new Vector2(x, y), new Vector2(x, y + size.Y * 0.16f), new Color(_hot, alpha * 0.65f), 0.8f);
                }
                break;
            case Motif.Screen:
                DrawStamp(_shape == Shape.Rect ? 1 : 0, Vector2.Zero, size * 0.95f, alpha);
                if (_shape == Shape.Circle)
                    DrawLine(new Vector2(-size.X * 0.16f, size.Y * 0.18f),
                        new Vector2(size.X * 0.16f, size.Y * 0.18f), new Color(_tint, alpha), 1f);
                break;
            case Motif.Stream:
                DrawStamp(0, Vector2.Zero, size, alpha);
                DrawStamp(1, new Vector2(size.X * 0.29f, -size.Y * 0.25f), size * 0.34f, alpha);
                for (int i = 0; i < 2; i++)
                {
                    float y = size.Y * (-0.12f + 0.22f * i);
                    DrawLine(new Vector2(-size.X * 0.25f, y), new Vector2(size.X * (0.18f - i * 0.08f), y),
                        new Color(_hot, alpha * 0.7f), 0.8f);
                }
                break;
            case Motif.Data:
                DrawStamp(0, Vector2.Zero, size, alpha);
                DrawStamp(3, Vector2.Zero, size * 0.9f, alpha);
                float offset = size.X * 0.34f;
                DrawLine(new Vector2(-offset, size.Y * 0.25f), new Vector2(offset, size.Y * 0.25f),
                    new Color(_tint, alpha), 0.8f);
                break;
        }
    }

    private void DrawStamp(int index, Vector2 at, Vector2 box, float alpha)
    {
        var texture = _motifArt[index % _motifArt.Length];
        Vector2 size = texture.GetSize();
        size *= Mathf.Min(box.X / size.X, box.Y / size.Y);
        Color tint = _motif == Motif.Data ? new Color(1f, 0.7f, 0.87f, alpha) : new Color(1, 1, 1, alpha);
        DrawTextureRect(texture, new Rect2(at - size / 2, size), false, tint);
    }

    private void DrawReadReceipt(Vector2 at, Vector2 box, float alpha)
    {
        Vector2 size = MessageTex.GetSize();
        size *= Mathf.Min(box.X / size.X, box.Y / size.Y);
        DrawTextureRect(MessageTex, new Rect2(at - size / 2, size), false, new Color(1, 1, 1, alpha));
        var dots = new Vector2(size.X * 0.42f, size.Y * 0.25f);
        DrawTextureRect(ReadDotsTex, new Rect2(at - dots / 2, dots), false, new Color(1, 1, 1, alpha));
    }

    private void DrawFrameProgress(Rect2 rect, float progress, Color color)
    {
        float remaining = 2f * (rect.Size.X + rect.Size.Y) * progress;
        Vector2 start = rect.Position;
        for (int side = 0; side < 4 && remaining > 0; side++)
        {
            Vector2 direction = side switch { 0 => Vector2.Right, 1 => Vector2.Down, 2 => Vector2.Left, _ => Vector2.Up };
            float length = side % 2 == 0 ? rect.Size.X : rect.Size.Y;
            DrawLine(start, start + direction * Mathf.Min(length, remaining), color, 1.1f);
            remaining -= length;
            start += direction * length;
        }
    }

    private void DrawFullscreen(float progress, float fade)
    {
        Color danger = new(_struck ? _hot : _tint, (_struck ? 0.4f : 0.13f + 0.16f * progress) * fade);
        DrawDanger(danger);
        DrawFullscreenMotif(progress, fade);
        if (_safeR <= 0) return;

        var mint = new Color(0.4f, 0.95f, 0.72f);
        DrawArc(_safeCenter, _safeR, 0, Mathf.Tau, 72, new Color(0.015f, 0.035f, 0.055f, fade), 3.2f, true);
        DrawArc(_safeCenter, _safeR, 0, Mathf.Tau, 72, new Color(mint, fade), 1.2f, true);
        DrawArc(_safeCenter, _safeR + 3.5f, -Mathf.Pi / 2,
            -Mathf.Pi / 2 + Mathf.Tau * progress, 72, new Color(0.92f, 1f, 0.96f, 0.9f * fade), 0.85f, true);
        for (int i = 0; i < 4; i++)
        {
            Vector2 direction = Vector2.Right.Rotated(i * Mathf.Pi / 2);
            Vector2 tangent = direction.Orthogonal();
            Vector2 point = _safeCenter + direction * (_safeR - 4f);
            DrawLine(point + direction * 3 + tangent * 2, point, new Color(mint, fade), 0.9f);
            DrawLine(point, point + direction * 3 - tangent * 2, new Color(mint, fade), 0.9f);
        }
    }

    private void DrawFullscreenMotif(float progress, float fade)
    {
        if (_motif == Motif.None) return;
        float alpha = (0.28f + 0.3f * progress) * fade;
        for (int row = 0; row < 4; row++)
        for (int col = 0; col < 5; col++)
        {
            float u = Mathf.PosMod((col + 0.5f + row * 0.35f) / 5f + (float)_t * (_motif == Motif.Stream ? -0.045f : 0.012f), 1f);
            var at = new Vector2(L + 20 + (W - 40) * u, T + 22 + row * (H - 44) / 3f);
            if (at.DistanceTo(_safeCenter) < _safeR + 25) continue;
            int index = _motif == Motif.Stream ? ((row + col) % 4 == 0 ? 1 : 0) : (row + col) % 3;
            if (_motif == Motif.Stream)
            {
                DrawFieldSegment(at + new Vector2(-18, 0), at + new Vector2(-11, 0), new Color(_tint, alpha));
            }
            else
            {
                Vector2 toward = (_safeCenter - at).Normalized();
                DrawFieldSegment(at, at + toward * 14, new Color(_tint, alpha));
                DrawFieldSegment(at + toward * 14, at + toward * 17 + toward.Orthogonal() * 4, new Color(_hot, alpha));
            }
            DrawStamp(index, at, new Vector2(27, 18), alpha);
            if (_motif == Motif.Stream && index == 0)
            {
                DrawLine(at + new Vector2(-8, -3), at + new Vector2(7, -3), new Color(_hot, alpha), 0.7f);
                DrawLine(at + new Vector2(-8, 1), at + new Vector2(2, 1), new Color(_hot, alpha * 0.7f), 0.7f);
            }
            if (_motif == Motif.Data && index == 0) DrawStamp(3, at, new Vector2(24, 18), alpha);
        }
    }

    private void DrawFieldSegment(Vector2 from, Vector2 to, Color color)
    {
        Vector2 closest = Geometry2D.GetClosestPointToSegment(_safeCenter, from, to);
        if (closest.DistanceTo(_safeCenter) <= _safeR + 3) return;
        DrawLine(from, to, color, 0.8f, true);
    }

    private void BuildDangerCorners()
    {
        if (_safeR <= 0) return;
        _dangerCorners = new Vector2[4][];
        const int segments = 16;
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            float angle = quadrant * Mathf.Pi / 2;
            var points = new Vector2[segments + 2];
            points[0] = _safeCenter + new Vector2(Mathf.Cos(angle + Mathf.Pi / 4), Mathf.Sin(angle + Mathf.Pi / 4))
                * (_safeR * Mathf.Sqrt(2));
            for (int i = 0; i <= segments; i++)
                points[i + 1] = _safeCenter + Vector2.Right.Rotated(angle + i * Mathf.Pi / (2 * segments)) * _safeR;
            _dangerCorners[quadrant] = points;
        }
    }

    private void DrawDanger(Color color)
    {
        if (_safeR <= 0) { DrawRect(Field.Rect, color); return; }
        // 単純な四隅と矩形に分け、穴付き多角形の三角形分割で安置が塗られるのを防ぐ。
        float left = _safeCenter.X - _safeR, right = _safeCenter.X + _safeR;
        float top = _safeCenter.Y - _safeR, bottom = _safeCenter.Y + _safeR;
        DrawRect(new Rect2(L, T, W, top - T), color);
        DrawRect(new Rect2(L, bottom, W, Field.Bottom - bottom), color);
        DrawRect(new Rect2(L, top, left - L, _safeR * 2), color);
        DrawRect(new Rect2(right, top, Field.Right - right, _safeR * 2), color);
        foreach (var corner in _dangerCorners) DrawColoredPolygon(corner, color);
    }

    private void DrawWarn(Vector2 at, float alpha)
    {
        DrawCircle(at, 3.4f, new Color(_tint, alpha * 0.7f), true, -1, true);
        DrawLine(at + new Vector2(0, -1.6f), at + new Vector2(0, 0.4f), new Color(1, 1, 1, alpha), 1f);
        DrawCircle(at + new Vector2(0, 1.6f), 0.6f, new Color(1, 1, 1, alpha), true, -1, true);
    }
}
