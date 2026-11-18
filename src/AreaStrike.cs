using Godot;

// AreaStrike : 範囲攻撃テレグラフ（RefrainArenaHTML 移植）。ボスの範囲技と道中ドローンのビームで共用。
//   固定輪郭の予兆 → 内側の進行線 → キャラクター別の着弾（このフレームのみ判定）の3段。
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

    private static readonly System.Collections.Generic.Dictionary<Motif, Texture2D[]> MotifTextures = new();
    private Texture2D[] _motifArt = System.Array.Empty<Texture2D>();
    private Vector2[][] _dangerCorners = System.Array.Empty<Vector2[]>();
    private AttackLineLayer _attackLines = null!;
    private AttackPresentation? _presentation;
    private bool _presentationRetired;

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
    internal static readonly Color DangerEdge = AttackTelegraph.Area;
    private static readonly Color DangerHot = new("fff0d9");
    // 全画面AOEが覆う矩形＝盤面（Field）。Fullscreen は GlobalPosition=0 に置かれ、ここを画面座標のまま描く。
    private static float W => Field.Width;
    private const float H = Field.Height;
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
    public bool CoversPoint(Vector2 p) => !IsQueuedForDeletion() && Inside(p);

    public void Cancel()
    {
        _presentation?.Finish(false);
        _presentationRetired = true;
        _struck = false;
        RemoveFromGroup("aoe");
        SetProcess(false);
        Hide();
        QueueFree();
    }

    // 全形状共通で "aoe" グループに入れる（QaPilot が走査する）。ゲーム本編では誰も参照しない。
    public override void _Ready()
    {
        AddToGroup("aoe");
        Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };
        _attackLines = new AttackLineLayer { ShowBehindParent = true };
        AddChild(_attackLines);
        if (_shape is Shape.BeamH or Shape.BeamV or Shape.BeamSeg)
        {
            _presentation = new AttackPresentation { Character = (_owner as Enemy)?.BodyShotJob ?? (_motif switch
                { Motif.Screen => Job.Heal, Motif.Stream => Job.Magic, Motif.Data => Job.Tank, _ => Job.Melee }) };
            GetParent().AddChild(_presentation);
        }
    }

    public void Configure(Shape shape, float halfW, float halfH, double warn, Color tint, Color hot, Motif motif = Motif.None)
    {
        _shape = shape; _hw = halfW; _hh = halfH;
        _warn = Mathf.Max(0.35, warn);
        _tint = tint; _hot = hot;
        SetMotif(motif);
        ZIndex = -2; ZAsRelative = false;
    }

    // 全画面AOE。画面全体が被弾域で、安置(セーフゾーン)円(safeCenter/safeR)だけが安全。
    // safeR<=0 で安置なしの全面型。位置は画面基準で固定するので GlobalPosition=Zero で AddChild する。
    public void ConfigureFullscreen(Vector2 safeCenter, float safeR, double warn, Color tint, Color hot, Motif motif = Motif.None)
    {
        _shape = Shape.Fullscreen;
        _safeCenter = safeCenter;
        _safeR = Mathf.Max(0f, safeR);
        _warn = Mathf.Max(0.35, warn);
        _tint = tint; _hot = hot;
        SetMotif(motif);
        BuildDangerCorners();
        ZIndex = -2; ZAsRelative = false;
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
        ZIndex = -2; ZAsRelative = false;
    }

    public override void _Process(double delta)
    {
        // 発生源が着弾前に消えた／浄化されたら、予測線ごとキャンセル（倒せば攻撃も消える）。
        if (_cancelOnOwnerLoss
            && (_owner == null || !IsInstanceValid(_owner)
                || (_owner is Enemy e && (e.IsPurified || e.GaugeVulnerable || e.GaugeReforming))))
        {
            QueueFree();
            return;
        }

        // 会話中（吹き出し表示中）は弾と同じく時間を止める＝動けない自機に着弾させない。
        if (Hud.BubblePaused) return;

        if (!_struck && _owner is MidEnemy enemy) GlobalPosition = enemy.ShotCenter;

        float ets = GameManager.EnemyTimeScale;
        _t += delta * ets;
        _presentation?.Advance(delta * ets, GlobalPosition, !_struck ? (float)(_t / _warn) : -1);
        if (!_struck && _t >= _warn)
        {
            _struck = true; ZIndex = 2; Strike();
            _presentation?.Launch(GlobalPosition, _shape == Shape.BeamV ? Vector2.Down : _segDir);
        }
        if (_t >= _warn + StrikeFlash)
        {
            _presentation?.Dissolve(GlobalPosition + (_shape == Shape.BeamSeg ? _segDir * _segLen * .5f : Vector2.Zero));
            _presentation?.Finish(true);
            _presentationRetired = true;
            QueueFree(); return;
        }
        UpdateAttackLine();
        QueueRedraw();
    }

    public override void _ExitTree()
    {
        if (!_presentationRetired && IsInstanceValid(_presentation)) _presentation!.Finish(false);
    }

    private void UpdateAttackLine()
    {
        _attackLines.Begin();
        if (_struck)
        {
            float fade = 1 - Mathf.Clamp((float)((_t - _warn) / StrikeFlash), 0, 1);
            var ink = new Color(_tint.Lerp(_hot, .35f), fade);
            if (_shape == Shape.BeamSeg)
                _attackLines.Add(new[] { Vector2.Zero, _segDir * _segLen }, _hh, ink);
            else if (_shape is Shape.BeamH or Shape.BeamV)
            {
                bool vertical = _shape == Shape.BeamV;
                Vector2 axis = vertical ? Vector2.Down * _hh : Vector2.Right * _hw;
                _attackLines.Add(new[] { -axis, axis }, vertical ? _hw : _hh, ink, roundCaps: false);
            }
            else if (_shape == Shape.Fullscreen)
                _attackLines.Surface(Field.Rect, new Color(ink, fade * .6f), safeCenter: _safeCenter, safeRadius: _safeR);
            else
            {
                Vector2 half = _shape == Shape.Circle ? Vector2.One * Radius : new Vector2(_hw, _hh);
                _attackLines.Surface(new Rect2(-half, half * 2), new Color(ink, fade * .75f), disc: _shape == Shape.Circle);
            }
        }
        _attackLines.End();
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
        // 大きな揺れで固定した危険境界を見失わせない。
        if (_shape == Shape.Fullscreen) GameCamera.Instance?.Shake(2.4f, 0.12f);
        else GameCamera.Instance?.Shake(1.2f, 0.09f);
    }


    public override void _Draw()
    {
        if (!_struck && !GameManager.ShowAttackTelegraphs) return;
        float progress = Mathf.Clamp((float)(_t / _warn), 0f, 1f);
        float fade = _struck ? 1f - Mathf.Clamp((float)((_t - _warn) / StrikeFlash), 0f, 1f) : 1f;
        float snap = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((progress - 0.82f) / 0.18f, 0f, 1f));
        Color warningInk = _shape == Shape.BeamSeg ? _tint : DangerEdge;
        Color hotInk = _shape == Shape.BeamSeg ? _hot : DangerHot;
        Color edge = new(_struck ? hotInk : warningInk.Lerp(hotInk, snap * 0.3f), fade);
        Color fill = new(_struck ? hotInk : warningInk, (_struck ? 0.38f : 0.07f + 0.03f * progress) * fade);

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

        Color shadow = new(AttackTelegraph.Outline, .6f * fade);
        float width = _struck ? 2.2f : .65f;
        if (_shape == Shape.Circle)
        {
            DrawCircle(Vector2.Zero, Radius, fill, true, -1, true);
            DrawMotif(new Vector2(Radius * 1.15f, Radius * 1.15f), progress, fade);
            DrawArc(Vector2.Zero, Radius, 0, Mathf.Tau, 64, shadow, width + .85f, true);
            DrawArc(Vector2.Zero, Radius, 0, Mathf.Tau, 64, edge, width, true);
            // 発動までの弧は危険範囲の内側に置き、実際の境界を動かさない。
            DrawArc(Vector2.Zero, Radius - 3, -Mathf.Pi / 2,
                -Mathf.Pi / 2 + Mathf.Tau * progress, 64, new Color(DangerHot, .7f * fade), .65f, true);
        }
        else
        {
            var rect = new Rect2(-_hw, -_hh, _hw * 2, _hh * 2);
            DrawRect(rect, fill);
            DrawMotif(new Vector2(_hw * 1.35f, _hh * 1.3f), progress, fade);
            DrawRect(rect, shadow, false, width + .85f);
            DrawRect(rect, edge, false, width);
            DrawFrameProgress(rect.Grow(-3), progress, new Color(DangerHot, 0.9f * fade));
        }
        if (_motif == Motif.None) DrawWarn(Vector2.Zero, fade);
    }

    private void DrawLane(Rect2 rect, float progress, float fade, Color edge, Color fill)
    {
        if (_struck)
        {
            if (_motif != Motif.None)
                for (float x = rect.Position.X + 18; x < rect.End.X; x += 36)
                    DrawStamp((int)((x - rect.Position.X) / 36), new Vector2(x, 0),
                        new Vector2(18, rect.Size.Y - 4), .54f * fade);
            return;
        }
        DrawRect(rect, fill);
        float thickness = .65f;
        Color shadow = new(AttackTelegraph.Outline, .6f * fade);
        var top = rect.Position;
        var bottom = new Vector2(rect.Position.X, rect.End.Y);
        var along = new Vector2(rect.Size.X, 0);
        DrawLine(top, top + along, shadow, thickness + .85f);
        DrawLine(bottom, bottom + along, shadow, thickness + .85f);
        DrawLine(top, top + along, edge, thickness);
        DrawLine(bottom, bottom + along, edge, thickness);
        var hot = new Color(_shape == Shape.BeamSeg ? _hot : DangerHot, .65f * fade);
        DrawLine(top + Vector2.Down * 2.5f, top + Vector2.Down * 2.5f + along * progress, hot, 0.8f);
        DrawLine(bottom + Vector2.Up * 2.5f, bottom + Vector2.Up * 2.5f + along * progress, hot, 0.8f);
        float span = rect.Size.X;
        int count = Mathf.Clamp((int)(span / 72f), 1, 6);
        for (int i = 0; i < count; i++)
        {
            float u = (i + 0.5f) / count;
            float x = rect.Position.X + 10f + (span - 20f) * u;
            var at = new Vector2(x, 0);
            if (_motif != Motif.None)
                DrawStamp(i, at, new Vector2(14, rect.Size.Y - 4), (0.18f + 0.12f * progress) * fade);
            else
            {
                DrawLine(at + new Vector2(-2, -2), at, hot, 0.8f);
                DrawLine(at, at + new Vector2(-2, 2), hot, 0.8f);
            }
        }
    }

    private void DrawMotif(Vector2 size, float progress, float fade)
    {
        if (_motif == Motif.None) return;
        float alpha = (0.36f + progress * 0.2f) * fade;
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
        if (_struck)
        {
            string name = _motif switch
            {
                Motif.Screen => index % 2 == 0 ? "koharu_penlight" : "koharu_gaze_eye",
                Motif.Rain => index % 2 == 0 ? "akari_unsent_card" : "akari_envelope",
                Motif.Stream => index % 2 == 0 ? "rei_comment" : "rei_subscriber",
                _ => (index % 4) switch { 0 => "mina_crystal", 1 => "mina_memory", 2 => "mina_unanswered", _ => "mina_snapshot_pair" },
            };
            if (box.X > 0 && box.Y > 0)
                BossProjectileArt.Get(name).Draw(this, new Rect2(at - box / 2, box), _t, new Color(1, 1, 1, alpha));
            return;
        }
        var texture = _motifArt[index % _motifArt.Length];
        Vector2 size = texture.GetSize();
        size *= Mathf.Min(box.X / size.X, box.Y / size.Y);
        Color tint = _motif == Motif.Data ? new Color(1f, 0.7f, 0.87f, alpha) : new Color(1, 1, 1, alpha);
        DrawTextureRect(texture, new Rect2(at - size / 2, size), false, tint);
    }

    private void DrawFrameProgress(Rect2 rect, float progress, Color color)
    {
        float remaining = 2f * (rect.Size.X + rect.Size.Y) * progress;
        Vector2 start = rect.Position;
        for (int side = 0; side < 4 && remaining > 0; side++)
        {
            Vector2 direction = side switch { 0 => Vector2.Right, 1 => Vector2.Down, 2 => Vector2.Left, _ => Vector2.Up };
            float length = side % 2 == 0 ? rect.Size.X : rect.Size.Y;
            DrawLine(start, start + direction * Mathf.Min(length, remaining), color, .65f);
            remaining -= length;
            start += direction * length;
        }
    }

    private void DrawFullscreen(float progress, float fade)
    {
        Color danger = new(_struck ? DangerHot : DangerEdge, (_struck ? 0.3f : 0.07f + 0.03f * progress) * fade);
        DrawDanger(danger);
        DrawFullscreenMotif(progress, fade);
        if (_safeR <= 0) return;

        var mint = new Color("b7ddd6");
        DrawArc(_safeCenter, _safeR, 0, Mathf.Tau, 72, new Color(AttackTelegraph.Outline, .6f * fade), 1.5f, true);
        DrawArc(_safeCenter, _safeR, 0, Mathf.Tau, 72, new Color(mint, fade), .65f, true);
        DrawArc(_safeCenter, _safeR - 2.5f, -Mathf.Pi / 2,
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
        float alpha = (0.16f + 0.12f * progress) * fade;
        for (int row = 0; row < 4; row++)
        for (int col = 0; col < 5; col++)
        {
            float u = Mathf.PosMod((col + 0.5f + row * 0.35f) / 5f, 1f);
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
