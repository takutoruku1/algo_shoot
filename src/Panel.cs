using Godot;

// Panel : 悪魔化した人(Enemy)に貼りついた「黒い吹き出し（暴言）」パネル。
// 本体の周囲を旋回し、algoの弾を遮る盾。
// algoの光弾が当たるとインクが減り、0で砕けて剥がれる（＝浄化が一歩進む）。
// 衝突: layer=16(パネル), mask=2(自機弾)。自機本体(mask)はパネルを含めないので接触では痛くない。
public partial class Panel : Area2D
{
    public int Ink = 2;
    // 一時不可侵（Enemy.SetPanelsInvulnerable 経由）。演出でボスが画面外へ退場している間、
    // 流れ弾・ボム・波紋でパネルが剥がれて BREAK が空撃ちされるのを防ぐ（あかり戦「雨の帰り道」）。
    public bool Invulnerable;
    private Enemy _owner = null!;
    private float _baseAngle, _orbitRadius, _spinSpeed, _spin;
    private bool _dead;
    private int _maxInk;
    private float _motionTime;
    private UnfolderPose _pose;
    private CanvasModulate? _worldTint;
    private CollisionShape2D _shape = null!;
    private string _texPath = "";
    private Sprite2D _sprite = null!;
    private bool _hasTex;
    private float _displayScale = 1f; // ザコ縮小用。絵＆当たりに掛ける（ボスは1）
    private const float DisplayH = 14f;
    private double _hitFlashT;                // 剥がし途中の一撃発光の残り（Enemy._hitFlashT と同方式）
    private const double HitFlashDur = 0.09;  // 一瞬。連射で点滅し続けないよう Enemy(0.16)よりさらに短く
    // 穢れの吹き出し（設計色 #e072ac）。ドット絵ではなく、マゼンタのグロー＋明リング＋
    // 暗マルーン核（radial-gradient rgba(70,24,52)→#180812 相当）＋滑らかな「・・・」。
    private static readonly Color KegareRim = new Color(0.882f, 0.447f, 0.675f);   // #e072ac
    private static readonly Color MaroonMid = new Color(0.274f, 0.094f, 0.204f);   // rgba(70,24,52)
    private static readonly Color MaroonCore = new Color(0.094f, 0.031f, 0.071f);  // #180812
    private static readonly Color BubbleDot = new Color(0.92f, 0.86f, 0.92f);

    public bool CanLock => !_dead && !Invulnerable && !IsQueuedForDeletion();
    private bool BossStyle => _owner.UnfolderStyle != UnfolderKind.None;
    private float DisplayHeight => BossStyle ? 22f : DisplayH;

    public void Setup(Enemy owner, float baseAngle, float orbitRadius, float spinSpeed,
                      int ink, string texPath = "", float displayScale = 1f)
    {
        _displayScale = displayScale;
        _owner = owner;
        _baseAngle = baseAngle;
        _orbitRadius = orbitRadius;
        _spinSpeed = spinSpeed;
        Ink = ink;
        _maxInk = ink;
        _texPath = texPath;
    }

    public static string ResolveTexPath(string scenePath)
    {
        string id = GameManager.StageIdForScene(scenePath) ?? "final";
        return id switch
        {
            "akari"  => "res://char/v3/panel_akari.png",
            "koharu" => "res://char/v3/panel_koharu.png",
            "rei"    => "res://char/v3/panel_rei.png",
            _        => "res://char/v3/panel_final.png",
        };
    }

    public override void _Ready()
    {
        if (BossStyle)
        {
            _worldTint = TintLift.Find(this);
            Modulate = TintLift.Of(_worldTint, TintLift.EnemyBody);
            TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        }
        CollisionLayer = 16; // パネル
        CollisionMask = 2;   // 自機弾
        Monitoring = true;
        Monitorable = true;
        _shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = 3f * _displayScale } };
        AddChild(_shape);
        AreaEntered += OnAreaEntered;

        // 吹き出しスプライト。未指定なら面ごとの盾（ResolveTexPath）を引く。読めなければ _Draw のプレースホルダ。
        if (BossStyle) _texPath = UnfolderMotion.TexturePath(_owner.UnfolderStyle);
        else if (string.IsNullOrEmpty(_texPath)) _texPath = ResolveTexPath(GetTree().CurrentScene?.SceneFilePath ?? "");
        if (!string.IsNullOrEmpty(_texPath))
        {
            var t = ResourceLoader.Load<Texture2D>(_texPath);
            if (t != null)
            {
                _hasTex = true;
                _sprite = new Sprite2D
                {
                    Texture = t,
                    Centered = true,
                    TextureFilter = BossStyle ? TextureFilterEnum.LinearWithMipmaps : TextureFilterEnum.Linear,
                };
                float s = DisplayHeight * _displayScale / t.GetHeight();
                _sprite.Scale = new Vector2(s, s);
                AddChild(_sprite);
            }
        }

        UpdateOrbit(0);
    }

    private void UpdateOrbit(double delta)
    {
        if (BossStyle)
        {
            if (_owner.UnfoldersActive && !Invulnerable) _motionTime += (float)delta;
            _pose = UnfolderMotion.Sample(_owner.UnfolderStyle, _motionTime * _spinSpeed, _baseAngle, _orbitRadius, _owner.UnfolderStage);
            if (_owner.UnfoldersActive)
                _pose = _pose with
                {
                    Position = Bound(_pose.Position), EchoA = Bound(_pose.EchoA), EchoB = Bound(_pose.EchoB),
                    WarpFrom = Bound(_pose.WarpFrom), WarpTo = Bound(_pose.WarpTo),
                };
            Position = _pose.Position;
            if (_sprite != null) _sprite.Rotation = _pose.Tilt;
            QueueRedraw();
            return;
        }
        _spin += _spinSpeed * (float)delta;
        float a = _baseAngle + _spin;
        Position = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _orbitRadius;
    }

    private Vector2 Bound(Vector2 local)
    {
        var extent = UnfolderMotion.OrbitExtent(_owner.UnfolderStyle, _orbitRadius) + new Vector2(16, 16);
        var center = _owner.GlobalPosition;
        // 軌道全体を内側へ寄せ、画面端で個々の盾が同じ位置へ潰れるのを防ぐ。
        center.X = Mathf.Clamp(center.X, Field.Left + extent.X, Field.Right - extent.X);
        center.Y = Mathf.Clamp(center.Y, Field.Top + extent.Y, Field.Bottom - extent.Y);
        return local + center - _owner.GlobalPosition;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_dead) return;
        if (BossStyle) Modulate = TintLift.Of(_worldTint, TintLift.EnemyBody);
        // 発光の減衰は旋回停止中も進める（吹き出しが出た瞬間に白いまま固まるのを防ぐ）。
        if (_hitFlashT > 0)
        {
            _hitFlashT -= delta;
            if (_sprite != null)
            {
                float h = _hitFlashT > 0 ? (float)(_hitFlashT / HitFlashDur) : 0f; // 1→0（0で必ず素の色へ戻す）
                float m = 1f + 1.6f * h;
                _sprite.Modulate = new Color(m, m, m);
            }
        }
        if (Hud.BubblePaused) return; // 吹き出し表示中は旋回を止める
        UpdateOrbit(delta);
    }

    private int InkCost(Bullet bullet)
    {
        if (_owner is MidEnemy) return Mathf.Max(1, bullet.Damage);
        int cost = 1 + Mathf.Max(0, bullet.Damage - 1) / 2;
        if (!_owner.HasHpBar) return cost;
        // Keep charge tiers stronger without letting a high-power projectile erase a boss panel.
        int normalCap = BossStyle && !bullet.Accel ? 1 : 2;
        int cap = !bullet.Charged ? normalCap : bullet.ChargeStage >= ChargeTier.Second ? 5 : 3;
        return Mathf.Min(cost, cap);
    }

    private void OnAreaEntered(Area2D area)
    {
        if (_dead || Invulnerable || Hud.BubblePaused) return;
        if (area is Bullet b && !b.IsEnemy && b.Active)
        {
            if (!b.RegisterChargeHit(this)) return;
            b.ChargeImpact(GlobalPosition);
            // 連鎖の光（chain_light）：消費位置から最寄りの別の敵へ跳弾（Despawn 前。跳ね先から持ち主は除外）。
            b.TryChain(_owner);
            // 貫く光（shot_pierce）：残貫通数のある弾はパネルを削りつつ突き抜ける（並んだ盾をまとめて撃てる）。
            if (b.Pierce > 0) b.Pierce--;
            else GetNodeOrNull<BulletPool>("/root/Pool")?.Despawn(b);
            // 集中の光（focus_fire）：パネル越しの撃ち込みも「同じ敵に当て続けている」に数える。
            var player = GetTree().GetFirstNodeInGroup("player") as Player;
            player?.NotifyShotHit(_owner);
            int cost = InkCost(b);
            Ink -= player?.PoweredShotDamage(b, cost) ?? cost;
            if (Ink <= 0) Shatter();
            else
            {
                // 剥がしの途中経過にも手応えを返す（本体ヒットと同じ「当てた→返る」の非対称を解消）。
                // 発光(_hitFlashT)はテクスチャ付きのみ。QueueRedraw は残Ink表示の更新用
                // （テクスチャ無しは _Draw の同心円縮小、テクスチャ付きは DrawInkNotches の残量表示）。
                QueueRedraw();
                _hitFlashT = HitFlashDur;
                Audio.Instance?.PlayStrip(light: true);
            }
        }
    }

    public void WeakenByRipple()
    {
        if (_dead || Invulnerable) return;
        if (_owner is MidEnemy)
        {
            Ink = Mathf.Max(0, Ink - 6);
            if (Ink > 0) { _hitFlashT = HitFlashDur; QueueRedraw(); return; }
        }
        Shatter();
    }

    public void Shatter()
    {
        if (_dead || Invulnerable) return; // 不可侵中はボム/波紋の一括砕きも効かない（退場中のBREAK空撃ち防止）
        _dead = true;
        // 砕けは被弾シグナル(OnAreaEntered)中に走るため、衝突無効化は遅延設定する。
        SetDeferred(Area2D.PropertyName.Monitoring, false);
        SetDeferred(Area2D.PropertyName.Monitorable, false);
        if (_shape != null) _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
        GetNodeOrNull<GameManager>("/root/Game")?.AddBulletCleared(); // 剥がし小加点
        FxLayer.Instance?.Shatter(GlobalPosition); // 砕け＋やさしさの粒
        Audio.Instance?.PlayStrip(); // ④軽い剥離「コツッ」（浄化成立より一段軽い）
        _owner?.OnPanelStripped(this);
        QueueFree();
    }

    public override void _Draw()
    {
        if (_dead) return;
        if (BossStyle && _hasTex && _owner.UnfoldersActive && !Invulnerable) DrawMotion();
        if (_hasTex) { DrawInkNotches(); return; } // 絵付きパネルは残量表示だけ重ね描き
        float r = 3.2f + Ink * 0.8f;  // インクが多いほど大きい

        // 外周グロー（box-shadow 相当）。
        for (int i = 3; i >= 1; i--)
        {
            float t = i / 3f;
            DrawCircle(Vector2.Zero, r * (1f + 0.9f * t),
                new Color(KegareRim.R, KegareRim.G, KegareRim.B, 0.12f * (1f - t) + 0.05f), true, -1f, true);
        }
        DrawCircle(Vector2.Zero, r + 1.2f, new Color(KegareRim, 0.9f), true, -1f, true); // 明マゼンタリング
        DrawCircle(Vector2.Zero, r, MaroonMid, true, -1f, true);                         // マルーン
        DrawCircle(Vector2.Zero, r * 0.66f, MaroonCore, true, -1f, true);                // 暗芯

        // 「・・・」（滑らかな白ドット）。
        DrawCircle(new Vector2(-2f, 0f), 0.7f, BubbleDot, true, -1f, true);
        DrawCircle(new Vector2(0f, 0f), 0.7f, BubbleDot, true, -1f, true);
        DrawCircle(new Vector2(2f, 0f), 0.7f, BubbleDot, true, -1f, true);
    }

    private void DrawInkNotches()
    {
        if (Ink <= 0) return;
        float halfH = DisplayHeight * _displayScale * 0.5f;
        // High durability must not stretch the indicator across neighboring panels.
        if (_owner is MidEnemy || _maxInk > 4)
        {
            float width = 12f * _displayScale;
            var track = new Rect2(-width * 0.5f, -halfH - 3f, width, 1.4f);
            DrawRect(track.Grow(0.5f), MaroonCore);
            DrawRect(track, new Color(KegareRim, 0.3f));
            track.Size = new Vector2(width * Ink / _maxInk, track.Size.Y);
            DrawRect(track, BubbleDot);
            return;
        }
        const float dotR = 0.9f;
        const float spacing = 3.0f;
        float y = -(halfH + dotR + 1.5f);              // 絵柄を隠さないよう少し上に浮かせる
        float startX = -(Ink - 1) * spacing * 0.5f;
        for (int i = 0; i < Ink; i++)
        {
            var p = new Vector2(startX + i * spacing, y);
            DrawCircle(p, dotR + 0.4f, new Color(KegareRim, 0.9f), true, -1f, true); // 縁（設計色）
            DrawCircle(p, dotR, BubbleDot, true, -1f, true);                         // 本体
        }
    }

    private void DrawMotion()
    {
        var tint = UnfolderMotion.ColorFor(_owner.UnfolderStyle);
        var size = _sprite.Texture.GetSize() * _sprite.Scale;
        void Echo(Vector2 at, float alpha)
        {
            if (alpha <= 0.01f) return;
            DrawSetTransform(at - Position, _pose.Tilt);
            DrawTextureRect(_sprite.Texture, new Rect2(-size * 0.5f, size), false,
                new Color(tint, alpha));
            DrawSetTransform(Vector2.Zero);
        }
        Echo(_pose.EchoA, _pose.EchoAlpha);
        Echo(_pose.EchoB, _pose.EchoAlpha);
        if (_pose.WarpCue > 0)
        {
            var to = _pose.WarpTo - Position;
            Echo(_pose.WarpTo, 0.1f + _pose.WarpCue * 0.18f);
            float extent = 13f - _pose.WarpCue * 3f;
            for (int i = 0; i < 4; i++)
            {
                var axis = Vector2.FromAngle(i * Mathf.Pi / 2 + Mathf.Pi / 4);
                var corner = to + axis * extent;
                DrawLine(corner, corner - axis * 3, new Color(tint, 0.3f + _pose.WarpCue * 0.4f), 0.8f, true);
            }
        }
        if (_pose.WarpFlash > 0)
        {
            Echo(_pose.WarpFrom, _pose.WarpFlash * 0.25f);
            DrawLine(_pose.WarpFrom - Position, Vector2.Zero, new Color(tint, _pose.WarpFlash * 0.3f), 0.8f, true);
        }
    }
}
