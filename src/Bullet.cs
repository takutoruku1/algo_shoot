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
            if (_wordFont == null)
            {
                _wordFont = ResourceLoader.Load<FontFile>("res://assets/fonts/PixelMplus12-Regular.ttf");
                if (_wordFont != null)
                {
                    _wordFont.Antialiasing = TextServer.FontAntialiasing.None;
                    _wordFont.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
                    _wordFont.Hinting = TextServer.Hinting.None;
                }
            }
            return _wordFont;
        }
    }

    // 言葉弾にする（Spawn 後に呼ぶ）。再描画して文字を反映。
    public void SetWord(string w) { Word = w; QueueRedraw(); }

    public float Radius { get; private set; } = 3f;

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
                HorizontalAlignment.Left, -1, 9, new Color(1f, 0.62f, 0.68f));
            return;
        }

        if (IsEnemy)
        {
            // ドット弾：明ハロー → 明フチ → 黒インク → 中心の熱
            PixelDisc(r + 2f, new Color(1f, 0.97f, 0.9f, 0.5f));
            PixelDisc(r + 1f, new Color(1f, 0.95f, 0.88f, 0.95f));
            PixelDisc(r, new Color(0.06f, 0.04f, 0.09f));
            PixelDisc(r * 0.5f, new Color(1.0f, 0.32f, 0.46f));
        }
        else
        {
            // ドット弾：暗縁 → 水色 → 白コア
            PixelDisc(r + 1f, new Color(0.10f, 0.18f, 0.30f, 0.5f));
            PixelDisc(r, new Color(0.50f, 0.86f, 1.0f, 0.95f));
            PixelDisc(r * 0.55f, new Color(1.0f, 1.0f, 1.0f));
        }
    }

    // 1x1の正方ドットで塗る“ピクセルの円”。滑らかな円でなくドット絵に見せる。
    private void PixelDisc(float r, Color col)
    {
        int ri = Mathf.CeilToInt(r);
        float r2 = r * r;
        for (int y = -ri; y < ri; y++)
            for (int x = -ri; x < ri; x++)
            {
                float cx = x + 0.5f, cy = y + 0.5f;
                if (cx * cx + cy * cy <= r2)
                    DrawRect(new Rect2(x, y, 1, 1), col);
            }
    }
}
