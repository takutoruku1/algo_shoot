using Godot;

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
    public void Activate(Vector2 pos, Vector2 vel, bool isEnemy, float radius, int damage)
    {
        Velocity = vel;
        IsEnemy = isEnemy;
        Damage = damage;
        Radius = radius;
        Active = true;
        Grazed = false;
        Word = "";  // 再利用時に前の言葉を持ち越さない

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

        if (IsEnemy)
            DrawGlassBullet(r, EnemyMid, EnemyEdge, EnemyGlow);
        else
            DrawGlassBullet(r, PlayerMid, PlayerEdge, PlayerGlow);
    }

    // HTML(Refrain HUD A) の弾を再現：外周グロー(box-shadow 相当)＋
    // 白ハイライト(オフセット) → 中間色 → 暗エッジ の滑らかなグラデ円。
    // DrawCircle は antialiased:true でドットにならず滑らかに描かれる。
    private void DrawGlassBullet(float r, Color mid, Color edge, Color glow)
    {
        // 外周グロー：薄い同心円を外→内に重ねてぼかしを近似（box-shadow blur 相当）
        const int gSteps = 5;
        for (int i = gSteps; i >= 1; i--)
        {
            float t = i / (float)gSteps;                 // 1=最外周
            float gr = r * (1f + 1.3f * t);
            float a = 0.16f * (1f - t) + 0.04f;          // 外ほど薄い
            DrawCircle(Vector2.Zero, gr, new Color(glow.R, glow.G, glow.B, a), true, -1f, true);
        }

        // 本体：エッジ → 中間 → 中間寄りの薄帯 → 白ハイライト
        DrawCircle(Vector2.Zero, r, edge, true, -1f, true);
        DrawCircle(Vector2.Zero, r * 0.82f, edge.Lerp(mid, 0.6f), true, -1f, true);
        DrawCircle(Vector2.Zero, r * 0.60f, mid, true, -1f, true);

        // 白ハイライト：HTML の "circle at ~35% 30%" を再現して左上にオフセット
        var hl = new Vector2(-0.28f * r, -0.36f * r);
        DrawCircle(hl, r * 0.34f, new Color(1f, 1f, 1f, 0.95f), true, -1f, true);
    }
}
