using Godot;

// Panel : 悪魔化した人(Enemy)に貼りついた「黒い吹き出し（暴言）」パネル。
// 本体の周囲を旋回し、①暴言弾の発生源 ②algoの弾を遮る盾 を兼ねる。
// algoの光弾が当たるとインクが減り、0で砕けて剥がれる（＝浄化が一歩進む）。
// 衝突: layer=16(パネル), mask=2(自機弾)。自機本体(mask)はパネルを含めないので接触では痛くない。
public partial class Panel : Area2D
{
    public int Ink = 2;
    // 一時不可侵（Enemy.SetPanelsInvulnerable 経由）。演出でボスが画面外へ退場している間、
    // 流れ弾・ボム・波紋でパネルが剥がれて BREAK が空撃ちされるのを防ぐ（あかり戦「雨の帰り道」）。
    public bool Invulnerable;

    private Enemy _owner = null!;
    private float _baseAngle, _orbitRadius, _spinSpeed, _spin, _fireInterval;
    private bool _fires;
    private bool _dead;
    private int _maxInk;
    private float _motionTime;
    private UnfolderPose _pose;
    private CanvasModulate? _worldTint;
    public bool CanLock => !_dead && !Invulnerable && !IsQueuedForDeletion();
    private bool BossStyle => _owner.UnfolderStyle != UnfolderKind.None;
    private float DisplayHeight => BossStyle ? 22f : DisplayH;
    private CollisionShape2D _shape = null!;
    private string _texPath = "";
    private Sprite2D _sprite = null!;
    private bool _hasTex;
    private float _displayScale = 1f; // ザコ縮小用。絵＆当たりに掛ける（ボスは1）
    private const float DisplayH = 14f;
    private double _hitFlashT;                // 剥がし途中の一撃発光の残り（Enemy._hitFlashT と同方式）
    private const double HitFlashDur = 0.09;  // 一瞬。連射で点滅し続けないよう Enemy(0.16)よりさらに短く

    public void Setup(Enemy owner, float baseAngle, float orbitRadius, float spinSpeed,
                      bool fires, float fireInterval, int ink, string texPath = "", float displayScale = 1f)
    {
        _displayScale = displayScale;
        _owner = owner;
        _baseAngle = baseAngle;
        _orbitRadius = orbitRadius;
        _spinSpeed = spinSpeed;
        // 発射は本体(Enemy/MidEnemy)へ一括移管したため、パネル自前の発射は常に無効。
        // パネルは「盾＝弾を遮る／剥がして浄化」の役割のみに専念する（引数 fires/fireInterval は後方互換のため残置。値は保持のみで未使用）。
        _fires = false;
        _fireInterval = fireInterval;
        Ink = ink;
        _maxInk = ink;
        _texPath = texPath;
    }

    // 面ごとの盾の絵（案C・2026-09-06）。心のないコメントが投稿の穢れを守っている、という意味を面の闇で読ませる。
    //   あかり: 送信取消（取り消し線の入った紙飛行機）／こはる: 視線（目）／レイ: 低評価（下向きの親指）／FINAL・その他: 非表示の返信（…）。
    // 器は共通の暗い吹き出し（藍黒＋淡い藤の縁＋白い記号の 3 色）。剥がれ具合は DrawInkNotches の残量表示で示す。
    // ボス・中ボス・道中の敵・カメオはすべて同じ面の絵を使う（Enemy 側が PanelTexPath を空で渡すとここで決まる）。
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
        var world = _owner.GlobalPosition + local;
        return new Vector2(Mathf.Clamp(world.X, Field.Left + 13, Field.Right - 13),
            Mathf.Clamp(world.Y, Field.Top + 13, Field.Bottom - 13)) - _owner.GlobalPosition;
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
        // 発射は本体側へ移管済み（_fires は常に false）。ここでは旋回＝盾の挙動のみ。
    }

    private int InkCost(Bullet bullet)
    {
        int cost = 1 + Mathf.Max(0, bullet.Damage - 1) / 2;
        if (!_owner.HasHpBar) return cost;
        // Keep charge tiers stronger without letting a high-power projectile erase a boss panel.
        int normalCap = BossStyle && !bullet.Accel ? 1 : 2;
        int cap = !bullet.Charged ? normalCap : bullet.ChargeStage >= ChargeTier.Second ? 5 : 3;
        if (GameManager.Instance!.ShotPowerMul > 1)
            cap += bullet.Charged ? bullet.ChargeStage >= ChargeTier.Second ? 3 : 2 : 1;
        return Mathf.Min(cost, cap);
    }

    private void OnAreaEntered(Area2D area)
    {
        if (_dead || Invulnerable) return; // 不可侵中は弾も受けない（消しもしない＝素通し）
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
            (GetTree().GetFirstNodeInGroup("player") as Player)?.NotifyShotHit(_owner);
            Ink -= InkCost(b);
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

    // やさしさの波紋で剥がす（MVPでは即砕く）。
    public void WeakenByRipple()
    {
        if (_dead) return;
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

    // 穢れの吹き出し（設計色 #e072ac）。ドット絵ではなく、マゼンタのグロー＋明リング＋
    // 暗マルーン核（radial-gradient rgba(70,24,52)→#180812 相当）＋滑らかな「・・・」。
    private static readonly Color KegareRim = new Color(0.882f, 0.447f, 0.675f);   // #e072ac
    private static readonly Color MaroonMid = new Color(0.274f, 0.094f, 0.204f);   // rgba(70,24,52)
    private static readonly Color MaroonCore = new Color(0.094f, 0.031f, 0.071f);  // #180812
    private static readonly Color BubbleDot = new Color(0.92f, 0.86f, 0.92f);

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
        if (_maxInk > 4)
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
            DrawTextureRect(_sprite.Texture, new Rect2(at - Position - size * 0.5f, size), false,
                new Color(tint, alpha));
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
