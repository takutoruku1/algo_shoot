using Godot;

// Player : Area2D。グループ "player" に追加。
// 移動(通常110 / 低速50 px/s)、連射(Pool経由・右方向+260・上下2way)、被弾無敵点滅、TakeHit、Lives。
// W0 では残機を減らさず「練習中」扱い（ゲームオーバーにしない）。
// 衝突: layer=1, mask=12（敵=4 と 敵弾=8 を検出）。
// 当たり判定は半径2px の極小（胸の紫十字相当）。可視ヒットボックス点を _Draw で小さく描く。
public partial class Player : Area2D
{
    // 速度
    private const float NormalSpeed = 150f;
    private const float FocusSpeed = 65f;

    // 連射
    private const float FireInterval = 0.11f;
    private float _fireCooldown = 0f;

    // 当たり半径（極小）
    private const float HitRadius = 2f;
    // グレイズ半径（かすり判定の広さ）
    private const float GrazeRadius = 11f;

    // ボム入力のエッジ検出用
    private bool _bombHeld = false;

    // プレイ領域
    private const float MinX = 0f;
    private const float MaxX = 384f;
    private const float MinY = 0f;
    private const float MaxY = 216f;

    // 残機
    public int Lives { get; private set; } = 3;

    // 無敵・点滅
    private bool _invincible = false;
    private float _invincibleTimer = 0f;
    private const float InvincibleDuration = 1.2f;
    private float _blinkPhase = 0f;

    // 表示用スプライト（algo.png）。読み込めない場合は null のまま → _Draw フォールバック。
    private Sprite2D _sprite = null!;
    private bool _hasTexture = false;

    // 常時ふわふわ浮遊（スプライトのみ上下に揺らす。当たり判定点は固定）
    private float _bobTime = 0f;
    private const float BobSpeed = 3.2f; // 角速度(rad/s) 約2秒周期
    private const float BobAmp = 2.0f;   // 揺れ幅(px)

    // Pool 取得用キャッシュ
    private BulletPool _pool = null!;

    public override void _Ready()
    {
        AddToGroup("player");

        // 衝突レイヤー: layer=1, mask=12（敵=4, 敵弾=8）
        CollisionLayer = 1;
        CollisionMask = 12;
        Monitoring = true;
        Monitorable = true;

        // 当たり判定（CircleShape2D, 半径2px）
        var shape = new CollisionShape2D
        {
            Name = "HitShape",
            Shape = new CircleShape2D { Radius = HitRadius }
        };
        AddChild(shape);

        // テクスチャ読み込み（失敗時は _Draw フォールバック）
        // ドット絵スプライトを優先。無ければ透過カットアウト→元イラストにフォールバック。
        var tex = ResourceLoader.Load<Texture2D>("res://char/algo_idle.png")
                  ?? ResourceLoader.Load<Texture2D>("res://char/algo_cutout.png")
                  ?? ResourceLoader.Load<Texture2D>("res://char/algo.png");
        if (tex != null)
        {
            _hasTexture = true;
            _sprite = new Sprite2D
            {
                Name = "Sprite",
                Texture = tex,
                Centered = true,
                // 背景に合わせ、なめらか高精細で小さく表示（リニア縮小）
                TextureFilter = CanvasItem.TextureFilterEnum.Linear
            };
            // 表示高さ約36px（弾幕向けに小さめ）
            float texHeight = tex.GetHeight();
            if (texHeight > 0)
            {
                float scale = 36f / texHeight;
                _sprite.Scale = new Vector2(scale, scale);
            }
            AddChild(_sprite);
        }

        // 被弾検出（敵 / 敵弾）
        AreaEntered += OnAreaEntered;

        // グレイズ判定エリア（自機より広い円。敵弾(=layer8)のかすりを検出）
        var grazeArea = new Area2D
        {
            Name = "GrazeArea",
            CollisionLayer = 0,
            CollisionMask = 8, // 敵弾
            Monitoring = true,
            Monitorable = false,
        };
        grazeArea.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = GrazeRadius } });
        AddChild(grazeArea);
        grazeArea.AreaEntered += OnGrazeAreaEntered;

        // Pool 取得
        _pool = GetNode<BulletPool>("/root/Pool");

        ZIndex = 10;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // 移動入力
        Vector2 dir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        bool focus = Input.IsKeyPressed(Key.Shift);
        float speed = focus ? FocusSpeed : NormalSpeed;

        Vector2 pos = GlobalPosition + dir * speed * dt;

        // プレイ領域内にクランプ
        pos.X = Mathf.Clamp(pos.X, MinX, MaxX);
        pos.Y = Mathf.Clamp(pos.Y, MinY, MaxY);
        GlobalPosition = pos;

        // ショット
        if (_fireCooldown > 0f)
            _fireCooldown -= dt;

        bool shoot = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept");
        if (shoot && _fireCooldown <= 0f)
        {
            Fire();
            _fireCooldown = FireInterval;
        }

        // ボム（X）: 押した瞬間だけ発動
        bool bombKey = Input.IsKeyPressed(Key.X);
        if (bombKey && !_bombHeld)
            TryBomb();
        _bombHeld = bombKey;

        // 無敵・点滅更新
        if (_invincible)
        {
            _invincibleTimer -= dt;
            _blinkPhase += dt;
            if (_invincibleTimer <= 0f)
            {
                _invincible = false;
                SetSpriteVisible(true);
            }
            else
            {
                // 約 20Hz で点滅
                bool show = ((int)(_blinkPhase * 20f) % 2) == 0;
                SetSpriteVisible(show);
            }
        }

        // 常時ふわふわ浮遊（スプライトのみ上下に揺らす）
        _bobTime += dt;
        if (_hasTexture && _sprite != null)
        {
            _sprite.Position = new Vector2(0f, Mathf.Sin(_bobTime * BobSpeed) * BobAmp);
        }

        // ヒットボックス点を毎フレーム更新描画
        QueueRedraw();
    }

    private void Fire()
    {
        if (_pool == null)
            return;

        // 銃口（中心からやや右）
        Vector2 muzzle = GlobalPosition + new Vector2(20f, 0f);
        Vector2 vel = new Vector2(360f, 0f);

        // 上下に少しずらした 2way
        _pool.Spawn(muzzle + new Vector2(0f, -4f), vel, isEnemy: false, 3f, 1);
        _pool.Spawn(muzzle + new Vector2(0f, 4f), vel, isEnemy: false, 3f, 1);
    }

    private void OnAreaEntered(Area2D area)
    {
        // 敵 or 敵弾との接触で TakeHit。
        // 弾側の damage 処理は敵側 / 弾側で行うため、ここでは被弾のみ扱う。
        if (area is Enemy)
        {
            TakeHit();
            return;
        }

        if (area is Bullet b && b.IsEnemy)
        {
            TakeHit();
            // 当たった敵弾は消す
            if (_pool != null)
                _pool.Despawn(b);
        }
    }

    // グレイズ（敵弾のかすり）検出 → 加点。
    private void OnGrazeAreaEntered(Area2D area)
    {
        if (area is Bullet b && b.IsEnemy && b.Active && !b.Grazed)
        {
            b.Grazed = true;
            GetNodeOrNull<GameManager>("/root/Game")?.AddGraze();
        }
    }

    // ボム「魔法陣・解放」: 画面の敵弾を消去＋画面内の敵を浄化＋短時間無敵＋画面フラッシュ。
    private void TryBomb()
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (game == null || !game.UseBomb())
            return;

        // 画面内の敵弾を消去（加点）
        foreach (Node node in GetTree().GetNodesInGroup("enemy_bullets"))
        {
            if (node is Bullet b && b.Active)
            {
                game.AddBulletCleared();
                _pool?.Despawn(b);
            }
        }

        // 画面内の敵を浄化
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is Enemy e)
                e.Purify();
        }

        // 短時間無敵 ＋ 画面フラッシュ
        StartInvincible();
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.Flash();
    }

    public void TakeHit()
    {
        // 無敵中は無効
        if (_invincible)
            return;

        // フラッシュ＋短時間無敵
        StartInvincible();

        // W0 は練習扱い: 残機を減らさない（ゲームオーバーにしない）。
        // 残機を実際に減らす運用に切り替える場合はここで Lives-- する。
    }

    private void StartInvincible()
    {
        _invincible = true;
        _invincibleTimer = InvincibleDuration;
        _blinkPhase = 0f;
        // 被弾フラッシュ（一瞬非表示にして点滅開始の合図）
        SetSpriteVisible(false);
    }

    private void SetSpriteVisible(bool visible)
    {
        if (_hasTexture && _sprite != null)
            _sprite.Visible = visible;
        // _Draw フォールバック側は modulate ではなく可視フラグで制御
        Modulate = new Color(1f, 1f, 1f, visible ? 1f : 0.35f);
    }

    public override void _Draw()
    {
        // テクスチャが無い場合のプレースホルダ（白い体＋紫十字）
        if (!_hasTexture)
        {
            // 体（白い円）
            DrawCircle(Vector2.Zero, 12f, new Color(1f, 1f, 1f, 0.95f));
            DrawArc(Vector2.Zero, 12f, 0f, Mathf.Tau, 24, new Color(0.6f, 0.6f, 0.7f), 1f);

            // 紫の十字（胸）
            var purple = new Color(0.6f, 0.2f, 0.8f);
            DrawLine(new Vector2(-4f, 0f), new Vector2(4f, 0f), purple, 1.5f);
            DrawLine(new Vector2(0f, -4f), new Vector2(0f, 4f), purple, 1.5f);
        }

        // 極小ヒットボックス点（常に描画。集中時の被弾点の目安）
        DrawCircle(Vector2.Zero, HitRadius, new Color(0.8f, 0.2f, 0.9f, 0.9f));
        DrawCircle(Vector2.Zero, 1f, new Color(1f, 1f, 1f, 1f));
    }
}
