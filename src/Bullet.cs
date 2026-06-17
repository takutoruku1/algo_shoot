using Godot;

// 弾形（RefrainHTML/Refrain Danmaku v3 の弾形7種）。言葉弾は Bullet.Word で別扱い。
public enum BulletShape { Orb, Diamond, Star, Ring, Needle, Rice }

// Bullet : Area2D。Pool により生成・使い回しされる弾。
// 当たり判定（被弾処理）は敵側/自機側で行うため、Bullet 自身は Area 重なり処理を持たない。
// _PhysicsProcess で等速直線移動し、画面外(余白16px)に出たら Pool.Despawn(this)。
public partial class Bullet : Area2D
{
    // 衝突レイヤー/マスク（ビット値, 仕様準拠）
    // PlayerBullet: layer=2, mask=4
    // EnemyBullet:  layer=8, mask=1
    private const uint LayerPlayerBullet = 2;
    private const uint MaskPlayerBullet = 4;
    private const uint LayerEnemyBullet = 8;
    private const uint MaskEnemyBullet = 1;

    // 画面サイズと画面外判定の余白
    private const float ScreenWidth = 384f;
    private const float ScreenHeight = 216f;
    private const float Margin = 16f;

    public Vector2 Velocity;
    public bool IsEnemy;
    public int Damage;
    public bool Active;
    public bool Grazed;  // グレイズ済みか（重複加点防止）
    public string Word = "";  // 非空なら「言葉弾」＝文字そのものが弾（道中の敵。設計書 4）

    // 弾形とスペル色（敵弾のみ反映）。色未指定時は既定の穢れ色。
    public BulletShape Shape = BulletShape.Orb;
    public Color Tint;
    public bool TintSet;

    // ホーミング（自機ショットの誘導モード・設計書 §3-2③）。右側の穢れ標的へ最大旋回角つきで曲射。
    public bool Homing;
    private Node2D? _homeTarget;
    private const float HomingTurnRate = 200f; // deg/s（吸い寄せ感を残すため急旋回しすぎない）

    // 言葉弾の文字フォント（全弾で共有。初回だけロード）。
    private static FontFile? _wordFont;
    private static FontFile? WordFont
    {
        get
        {
            _wordFont ??= UiKit.Zen; // 言葉弾も滑らかゴシック（非ピクセル）
            return _wordFont;
        }
    }

    // 言葉弾にする（Spawn 後に呼ぶ）。再描画して文字を反映。
    public void SetWord(string w) { Word = w; QueueRedraw(); }

    public float Radius { get; private set; } = 3f;

    // RefrainScripts_tama / Refrain HUD A.dc.html の弾デザイン。
    // ドット絵ではなく「白ハイライト→中間色→暗エッジのグラデ＋外周グロー」の滑らかな弾。
    // 敵弾: radial-gradient(circle at 35% 30%, #fff, #e072ac 60%, #7a2f5a) + glow rgba(224,114,172,.75)
    private static readonly Color EnemyMid  = new Color(0.882f, 0.447f, 0.675f); // #e072ac ボス穢れ
    private static readonly Color EnemyEdge = new Color(0.478f, 0.184f, 0.353f); // #7a2f5a 暗マゼンタ縁
    private static readonly Color EnemyGlow = new Color(0.878f, 0.447f, 0.675f); // rgba(224,114,172)
    // 自機弾: radial-gradient(circle at 40% 35%, #fff, #6cbcd8 65%) + glow rgba(108,188,216,.8)
    private static readonly Color PlayerMid  = new Color(0.424f, 0.737f, 0.847f); // #6cbcd8 浄化
    private static readonly Color PlayerEdge = new Color(0.247f, 0.490f, 0.604f); // 暗めの水色縁
    private static readonly Color PlayerGlow = new Color(0.424f, 0.737f, 0.847f); // rgba(108,188,216)
    private static readonly Color KegareWord = new Color(0.96f, 0.56f, 0.78f);    // 言葉弾の文字（穢れ系）

    private CollisionShape2D _shape = null!;
    private CircleShape2D _circle = null!;

    public override void _Ready()
    {
        // 子に CollisionShape2D(CircleShape2D) を追加
        _circle = new CircleShape2D { Radius = Radius };
        _shape = new CollisionShape2D { Shape = _circle };
        AddChild(_shape);

        // 初期状態は非アクティブ
        Deactivate();
    }

    // layer/mask/見た目/位置を設定し、可視化・monitoring 有効化。
    public void Activate(Vector2 pos, Vector2 vel, bool isEnemy, float radius, int damage,
        BulletShape shape = BulletShape.Orb, Color? tint = null, bool homing = false)
    {
        Velocity = vel;
        IsEnemy = isEnemy;
        Damage = damage;
        Radius = radius;
        Active = true;
        Grazed = false;
        Word = "";  // 再利用時に前の言葉を持ち越さない
        Shape = shape;
        TintSet = tint.HasValue;
        if (tint.HasValue) Tint = tint.Value;
        Homing = homing;
        _homeTarget = null;

        GlobalPosition = pos;

        // グループ登録（ボムの一括消去・グレイズ判定用）
        AddToGroup(isEnemy ? "enemy_bullets" : "player_bullets");

        // 当たり半径を反映
        if (_circle != null)
            _circle.Radius = radius;

        // レイヤー/マスク設定（isEnemy に応じて）
        if (isEnemy)
        {
            CollisionLayer = LayerEnemyBullet; // 8
            CollisionMask = MaskEnemyBullet;   // 1
        }
        else
        {
            CollisionLayer = LayerPlayerBullet; // 2
            CollisionMask = MaskPlayerBullet;   // 4
        }

        // 可視化・検出有効化。衝突状態の変更はシグナル中でも安全なよう遅延設定する
        // （Deactivate と順序を揃え、再利用時の取り違えを防ぐ）。
        Visible = true;
        SetPhysicsProcess(true);
        SetDeferred(Area2D.PropertyName.Monitoring, true);
        SetDeferred(Area2D.PropertyName.Monitorable, true);
        if (_shape != null)
            _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, false);

        QueueRedraw();
    }

    // 非表示・monitoring 無効化・プールへ戻る準備。
    public void Deactivate()
    {
        Active = false;
        Velocity = Vector2.Zero;

        // グループから外す（プール返却時）
        RemoveFromGroup("enemy_bullets");
        RemoveFromGroup("player_bullets");

        Visible = false;
        SetPhysicsProcess(false);
        // 衝突の無効化は遅延で行う。被弾シグナル（OnAreaEntered → Despawn）の最中に
        // monitoring / shape.disabled を直接書き換えると Godot にブロックされ、
        // 「見えないのに当たり判定だけ残る弾」が発生していた。Active=false は即時なので、
        // 消費側（Player/Panel）が Active を見ている限り遅延中の1フレームも安全。
        SetDeferred(Area2D.PropertyName.Monitoring, false);
        SetDeferred(Area2D.PropertyName.Monitorable, false);
        if (_shape != null)
            _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Active)
            return;

        // 会話中（吹き出し表示中）は飛んでいる弾も止める＝攻撃を停止
        if (Hud.BubblePaused)
            return;

        // ホーミング：右側の最寄りの穢れ標的へ向きを補間（速度の大きさは一定）。
        if (Homing && !IsEnemy)
            SteerToTarget((float)delta);

        GlobalPosition += Velocity * (float)delta;

        // 画面外(余白16px)に出たら Despawn
        var p = GlobalPosition;
        if (p.X < -Margin || p.X > ScreenWidth + Margin ||
            p.Y < -Margin || p.Y > ScreenHeight + Margin)
        {
            var pool = GetNodeOrNull<BulletPool>("/root/Pool");
            if (pool != null)
                pool.Despawn(this);
            else
                Deactivate();
        }
    }

    // 標的を一度ロックし、消滅/浄化時のみ再探索（毎フレーム全探索は重いので）。
    private void SteerToTarget(float delta)
    {
        var tgt = _homeTarget;
        if (tgt == null || !IsInstanceValid(tgt) || (tgt is Enemy en && en.IsPurified))
            tgt = _homeTarget = AcquireTarget();
        if (tgt == null) return; // 標的が無ければ直進

        float spd = Velocity.Length();
        if (spd < 0.01f) return;
        float cur = Velocity.Angle();
        float want = (tgt.GlobalPosition - GlobalPosition).Angle();
        float maxStep = Mathf.DegToRad(HomingTurnRate) * delta;
        float na = cur + Mathf.Clamp(Mathf.AngleDifference(cur, want), -maxStep, maxStep);
        Velocity = new Vector2(Mathf.Cos(na), Mathf.Sin(na)) * spd;
    }

    // 右側（X が自分より大きい）の未浄化の敵本体から最寄りを選ぶ。
    private Node2D? AcquireTarget()
    {
        Node2D? best = null;
        float bestD = float.MaxValue;
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is Enemy e && !e.IsPurified && e.GlobalPosition.X > GlobalPosition.X - 4f)
            {
                float d = e.GlobalPosition.DistanceSquaredTo(GlobalPosition);
                if (d < bestD) { bestD = d; best = e; }
            }
        }
        return best;
    }

    public override void _Draw()
    {
        if (!Active)
            return;

        float r = Radius;

        // 言葉弾：文字そのものが弾（背後に薄い黒い吹き出し帯）。本人ではなく“苦しめる言葉”。
        var wf = WordFont;
        if (!string.IsNullOrEmpty(Word) && wf != null)
        {
            var sz = wf.GetStringSize(Word, HorizontalAlignment.Left, -1, 9);
            const float pad = 2f;
            DrawRect(new Rect2(-sz.X / 2f - pad, -sz.Y / 2f - pad, sz.X + pad * 2f, sz.Y + pad * 2f),
                new Color(0.06f, 0.04f, 0.09f, 0.62f));
            DrawString(wf, new Vector2(-sz.X / 2f, sz.Y / 2f - 2f), Word,
                HorizontalAlignment.Left, -1, 9, KegareWord);
            return;
        }

        if (!IsEnemy)
        {
            // 自機弾は常にガラス円弾（浄化の水色）。
            DrawGlassBullet(r, PlayerMid, PlayerEdge, PlayerGlow);
            return;
        }

        // 敵弾：スペルの色（未指定なら既定の穢れ色）と弾形で描く。
        Color c = TintSet ? Tint : EnemyMid;
        switch (Shape)
        {
            case BulletShape.Diamond: DrawDiamond(r, c); break;
            case BulletShape.Star:    DrawStar(r, c);    break;
            case BulletShape.Ring:    DrawRing(r, c);    break;
            case BulletShape.Needle:  DrawNeedle(r, c);  break;
            case BulletShape.Rice:    DrawRice(r, c);    break;
            default:                  DrawOrb(r, c);     break; // 円弾＝白リング＋暗芯（芯色のみ可変）
        }
    }

    // 外周グロー（box-shadow 相当）：薄い同心円を外→内に重ねてぼかしを近似。
    private void DrawGlow(float baseR, Color glow, float reach = 1.3f)
    {
        const int gSteps = 5;
        for (int i = gSteps; i >= 1; i--)
        {
            float t = i / (float)gSteps;
            float gr = baseR * (1f + reach * t);
            float a = 0.16f * (1f - t) + 0.04f;
            DrawCircle(Vector2.Zero, gr, new Color(glow.R, glow.G, glow.B, a), true, -1f, true);
        }
    }

    // HTML(Refrain HUD A) のガラス円弾：外周グロー＋白ハイライト→中間→暗エッジのグラデ。
    private void DrawGlassBullet(float r, Color mid, Color edge, Color glow)
    {
        DrawGlow(r, glow);
        DrawCircle(Vector2.Zero, r, edge, true, -1f, true);
        DrawCircle(Vector2.Zero, r * 0.82f, edge.Lerp(mid, 0.6f), true, -1f, true);
        DrawCircle(Vector2.Zero, r * 0.60f, mid, true, -1f, true);
        var hl = new Vector2(-0.28f * r, -0.36f * r);
        DrawCircle(hl, r * 0.34f, new Color(1f, 1f, 1f, 0.95f), true, -1f, true);
    }

    // 円弾：作品準拠の「白リング＋暗芯」。芯色のみスペル色で可変（Danmaku v3 shapeInner orb）。
    private void DrawOrb(float r, Color core)
    {
        DrawGlow(r, core);
        DrawCircle(Vector2.Zero, r, new Color(1f, 1f, 1f, 0.95f), true, -1f, true); // 白リング
        DrawCircle(Vector2.Zero, r * 0.70f, new Color(0.086f, 0.039f, 0.071f), true, -1f, true); // 暗芯リング
        DrawCircle(Vector2.Zero, r * 0.40f, core, true, -1f, true); // 芯色
    }

    // 菱形：45度回転の四角＋グロー（shapeInner diamond）。
    private void DrawDiamond(float r, Color c)
    {
        DrawGlow(r, c, 1.1f);
        float s = r * 1.15f;
        var pts = new[] { new Vector2(0, -s), new Vector2(s, 0), new Vector2(0, s), new Vector2(-s, 0) };
        DrawColoredPolygon(pts, c);
        DrawColoredPolygon(Scale(pts, 0.5f), new Color(1f, 1f, 1f, 0.85f)); // 芯の光
    }

    // 星：5芒星（shapeInner star の clip-path 相当）。
    private void DrawStar(float r, Color c)
    {
        DrawGlow(r, c, 1.1f);
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float ang = Mathf.DegToRad(-90f + i * 36f);
            float rad = (i % 2 == 0) ? r * 1.15f : r * 0.48f;
            pts[i] = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
        }
        DrawColoredPolygon(pts, c);
    }

    // リング：中空の輪（shapeInner ring）。
    private void DrawRing(float r, Color c)
    {
        DrawGlow(r, c, 1.0f);
        DrawArc(Vector2.Zero, r * 0.9f, 0, Mathf.Tau, 28, c, Mathf.Max(1.4f, r * 0.42f), true);
        DrawArc(Vector2.Zero, r * 0.9f, 0, Mathf.Tau, 28, new Color(c.R, c.G, c.B, 0.5f), 0.9f, true);
    }

    // 針：進行方向へ伸びる細い弾（shapeInner needle）。
    private void DrawNeedle(float r, Color c)
    {
        float ang = Velocity.LengthSquared() > 0.01f ? Velocity.Angle() : Mathf.Pi / 2f;
        DrawSetTransform(Vector2.Zero, ang, Vector2.One);
        float len = r * 2.8f, w = r * 0.78f;
        DrawGlow(r * 0.8f, c, 0.8f);
        DrawRect(new Rect2(-len * 0.5f, -w * 0.5f, len, w), c);
        DrawCircle(new Vector2(-len * 0.5f, 0), w * 0.5f, c, true, -1f, true); // 後端の丸
        DrawCircle(new Vector2(len * 0.5f, 0), w * 0.5f, c, true, -1f, true);  // 先端の丸
        DrawRect(new Rect2(-len * 0.5f, -w * 0.18f, len, w * 0.36f), new Color(1f, 1f, 1f, 0.55f)); // 中央のハイライト
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    // 粒弾：進行方向へ細長い楕円（shapeInner rice）。
    private void DrawRice(float r, Color c)
    {
        float ang = Velocity.LengthSquared() > 0.01f ? Velocity.Angle() : Mathf.Pi / 2f;
        DrawGlow(r * 0.8f, c, 0.8f);
        DrawSetTransform(Vector2.Zero, ang, new Vector2(1.15f, 0.5f));
        DrawCircle(Vector2.Zero, r, c, true, -1f, true);
        DrawCircle(new Vector2(-r * 0.25f, -r * 0.25f), r * 0.4f, new Color(1f, 1f, 1f, 0.7f), true, -1f, true);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private static Vector2[] Scale(Vector2[] pts, float s)
    {
        var o = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) o[i] = pts[i] * s;
        return o;
    }
}
