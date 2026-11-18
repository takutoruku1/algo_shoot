using Godot;
using System.Collections.Generic;

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
    internal bool RallyDriven { get; private set; }
    private Enemy _owner = null!;
    private float _baseAngle, _orbitRadius, _spinSpeed, _spin;
    private bool _dead;
    private int _maxInk;
    private float _motionTime;
    private ulong _layoutFrame = ulong.MaxValue;
    private UnfolderPose _pose;
    private float _frameOrbitBlend = 1;
    private bool _placed;
    private CanvasModulate? _worldTint;
    private CollisionShape2D _shape = null!;
    private string _texPath = "";
    private Sprite2D _sprite = null!;
    private bool _hasTex;
    private bool _rotatingArt;
    internal int CurrentArtFrame { get; private set; }
    private float _displayScale = 1f; // ザコ縮小用。絵＆当たりに掛ける（ボスは1）
    private const float DisplayH = 14f;
    // All boss rotation frames fit inside this radius, including filtered edge pixels.
    internal const float ArtRadius = .54f;
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
    private bool InBossFrame => BossStyle && !RallyDriven && (Field.BossFrameActive || _owner.BossFrameReturning) && !_owner.BossInArena;
    private float VisualScale => _displayScale * (BossStyle ? Mathf.Lerp(1, 16f / DisplayHeight, _frameOrbitBlend) : 1);

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

    internal void BeginRally()
    {
        RallyDriven = true;
        Invulnerable = true;
        CollisionLayer = CollisionMask = 0;
        Monitoring = Monitorable = false;
        Scale = Vector2.One * 1.4f;
        if (_sprite != null)
        {
            if (_rotatingArt)
            {
                CurrentArtFrame = 0;
                _sprite.Texture = UnfolderMotion.FrameTexture(_texPath, 0);
            }
            _sprite.Rotation = 0;
            _sprite.Scale = Vector2.One * DisplayHeight * _displayScale / _sprite.Texture.GetHeight();
        }
        QueueRedraw();
    }

    internal void SetRallyPosition(Vector2 position)
    {
        GlobalPosition = position;
        QueueRedraw();
    }

    public static string ResolveTexPath(string scenePath)
    {
        string id = GameManager.StageIdForScene(scenePath) ?? "final";
        return id switch
        {
            "akari"  => "res://char/ui/blender/panel_akari.png",
            "koharu" => "res://char/ui/blender/panel_koharu.png",
            "rei"    => "res://char/ui/blender/panel_rei.png",
            _        => "res://char/ui/blender/panel_final.png",
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
        CollisionLayer = RallyDriven ? 0u : 16u;
        CollisionMask = RallyDriven ? 0u : 2u;
        Monitoring = !RallyDriven;
        Monitorable = !RallyDriven;
        _shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = 3f * _displayScale } };
        AddChild(_shape);
        AreaEntered += OnAreaEntered;

        // 吹き出しスプライト。未指定なら面ごとの盾（ResolveTexPath）を引く。読めなければ _Draw のプレースホルダ。
        if (BossStyle) _texPath = UnfolderMotion.TexturePath(_owner.UnfolderStyle);
        else if (string.IsNullOrEmpty(_texPath)) _texPath = ResolveTexPath(GetTree().CurrentScene?.SceneFilePath ?? "");
        if (!string.IsNullOrEmpty(_texPath))
        {
            _rotatingArt = _texPath.StartsWith("res://char/ui/blender/");
            CurrentArtFrame = UnfolderMotion.FrameAt(0, _baseAngle);
            var t = _rotatingArt ? UnfolderMotion.FrameTexture(_texPath, CurrentArtFrame)
                : ResourceLoader.Load<Texture2D>(_texPath);
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
        if (RallyDriven) return;
        if (BossStyle)
        {
            LayoutBossPanels(delta);
            return;
        }
        _spin += _spinSpeed * (float)delta;
        if (!Invulnerable) _motionTime += (float)delta;
        UpdateArt();
        float a = _baseAngle + _spin;
        Position = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _orbitRadius;
    }

    private void UpdateArt()
    {
        if (!_rotatingArt) return;
        int frame = UnfolderMotion.FrameAt(_motionTime, _baseAngle);
        if (frame == CurrentArtFrame) return;
        CurrentArtFrame = frame;
        _sprite.Texture = UnfolderMotion.FrameTexture(_texPath, frame);
    }

    private void LayoutBossPanels(double delta)
    {
        ulong frame = Engine.GetPhysicsFrames();
        if (delta > 0 && _layoutFrame == frame) return;
        var panels = new List<Panel>();
        foreach (var child in _owner.GetChildren())
            if (child is Panel { _dead: false, RallyDriven: false, _hasTex: true } panel)
                panels.Add(panel);
        panels.Sort((a, b) => a._baseAngle.CompareTo(b._baseAngle));
        var face = BossIllustrationMotion.FaceClearance(_owner);
        var gauge = new Rect2(_owner.GlobalPosition + new Vector2(-_owner.GaugeWidth * .5f - 3, _owner.GaugeTop - 9),
            new Vector2(_owner.GaugeWidth + 6, 15));
        var occupied = new List<Rect2> { face, gauge };
        foreach (var panel in panels)
        {
            panel._layoutFrame = delta > 0 ? frame : ulong.MaxValue;
            panel._frameOrbitBlend = Mathf.MoveToward(panel._frameOrbitBlend, panel.InBossFrame ? 1 : 0, (float)delta / .85f);
            if (_owner.UnfoldersActive && !panel.Invulnerable) panel._motionTime += (float)delta;
            panel.UpdateArt();
            var pose = UnfolderMotion.Sample(_owner.UnfolderStyle, panel._motionTime * panel._spinSpeed,
                panel._baseAngle, panel._orbitRadius, _owner.UnfolderStage);
            bool warping = pose.WarpFlash > panel._pose.WarpFlash;
            panel._pose = pose;
            panel._sprite.Scale = Vector2.One * panel.DisplayHeight * panel.VisualScale / panel._sprite.Texture.GetHeight();
            panel._sprite.Rotation = pose.Tilt;
            var footprint = panel.SpriteBounds().Merge(panel.InkBounds());
            var extent = UnfolderMotion.OrbitExtent(_owner.UnfolderStyle, panel._orbitRadius);
            var position = _owner.GlobalPosition;
            var room = (position + footprint.Position - new Vector2(Field.Left, Field.Top))
                .Min(new Vector2(Field.WorldRight, Field.Bottom) - position - footprint.End).Max(Vector2.Zero);
            var arenaRadius = extent.Min(room);
            var frameRadius = new Vector2(Field.BossFrameWidth * .5f - 16 * ArtRadius - 1,
                Mathf.Min(panel._orbitRadius + 20, Mathf.Min(_owner.GlobalPosition.Y + footprint.Position.Y - 1,
                    Field.Bottom - _owner.GlobalPosition.Y - footprint.End.Y - 1)));
            Vector2 Bound(Vector2 local)
            {
                var framed = _owner.GlobalPosition + local / extent * frameRadius;
                if (local.Y < 0)
                    framed.Y = position.Y + local.Y / extent.Y
                        * Mathf.Min(frameRadius.Y, -(_owner.GaugeTop + 6) + footprint.Position.Y - 1);
                return (position + local / extent * arenaRadius).Lerp(framed, panel._frameOrbitBlend) - position;
            }
            pose = pose with {
                Position = Bound(pose.Position), EchoA = Bound(pose.EchoA), EchoB = Bound(pose.EchoB),
                WarpFrom = Bound(pose.WarpFrom), WarpTo = Bound(pose.WarpTo),
            };
            float left = Mathf.Lerp(Field.Left, Field.BossFrameLeft, panel._frameOrbitBlend);
            Vector2 Place(Vector2 local) => panel.InBossFrame ? local
                : FindClearPosition(_owner.GlobalPosition + local, footprint, occupied, left) - _owner.GlobalPosition;
            var goal = Place(pose.Position);
            if (panel._placed && panel._frameOrbitBlend < 1 && delta > 0 && !warping)
            {
                var step = panel.Position.MoveToward(goal, 90 * (float)delta);
                goal = Place(step);
            }
            var offset = goal - pose.Position;
            panel.Position = goal;
            panel._placed = true;
            panel._pose = pose with {
                Position = goal, EchoA = pose.EchoA + offset, EchoB = pose.EchoB + offset,
                WarpTo = pose.WarpCue > 0 ? Place(pose.WarpTo) : pose.WarpTo,
            };
            occupied.Add(new Rect2(panel.GlobalPosition + footprint.Position, footprint.Size));
            panel.QueueRedraw();
        }
    }

    internal static Vector2 FindClearPosition(Vector2 wanted, Rect2 footprint, List<Rect2> occupied, float left)
    {
        var min = new Vector2(left, Field.Top) - footprint.Position;
        var max = new Vector2(Field.WorldRight, Field.Bottom) - footprint.End;
        wanted = wanted.Clamp(min, max);
        bool Clear(Vector2 point)
        {
            var bounds = new Rect2(point + footprint.Position, footprint.Size);
            foreach (var obstacle in occupied)
                if (bounds.Intersects(obstacle)) return false;
            return true;
        }
        if (Clear(wanted)) return wanted;

        // The closest free center is aligned with the desired point or an edge of an obstacle.
        var best = wanted;
        float distance = float.PositiveInfinity;
        for (int x = -1; x < occupied.Count * 2; x++)
        {
            float px = x < 0 ? wanted.X : x % 2 == 0
                ? occupied[x / 2].Position.X - footprint.End.X - .1f
                : occupied[x / 2].End.X - footprint.Position.X + .1f;
            if (px < min.X || px > max.X) continue;
            for (int y = -1; y < occupied.Count * 2; y++)
            {
                float py = y < 0 ? wanted.Y : y % 2 == 0
                    ? occupied[y / 2].Position.Y - footprint.End.Y - .1f
                    : occupied[y / 2].End.Y - footprint.Position.Y + .1f;
                if (py < min.Y || py > max.Y) continue;
                var point = new Vector2(px, py);
                float d = wanted.DistanceSquaredTo(point);
                if (d < distance && Clear(point)) { best = point; distance = d; }
            }
        }
        return best;
    }

    private bool MotionVisible(Vector2 local, bool warpCue = false)
    {
        var sprite = SpriteBounds();
        if (warpCue) sprite = sprite.Merge(new Rect2(-Vector2.One * WarpExtent, Vector2.One * WarpExtent * 2));
        var bounds = new Rect2(_owner.GlobalPosition + local + sprite.Position, sprite.Size).Grow(1);
        var face = BossIllustrationMotion.FaceClearance(_owner);
        var gauge = new Rect2(_owner.GlobalPosition + new Vector2(-_owner.GaugeWidth * .5f - 3, _owner.GaugeTop - 9),
            new Vector2(_owner.GaugeWidth + 6, 15));
        return !bounds.Intersects(face) && !bounds.Intersects(gauge);
    }

    private Rect2 SpriteBounds()
    {
        var extent = _sprite.Texture.GetSize() * _sprite.Scale * ArtRadius;
        return new Rect2(-extent, extent * 2);
    }

    private Rect2 InkBounds()
    {
        float halfH = DisplayHeight * VisualScale * .5f;
        if (_owner is MidEnemy || _maxInk > 4)
        {
            float width = 12 * VisualScale;
            return new Rect2(-width * .5f, -halfH - 3, width, 1.4f).Grow(.5f);
        }
        return new Rect2(-(Ink - 1) * 1.5f - 1.3f, -halfH - 3.7f, (Ink - 1) * 3 + 2.6f, 2.6f);
    }

    private float WarpExtent => (13 - _pose.WarpCue * 3) * VisualScale + .5f;

    internal Rect2 CoreBounds => _hasTex ? WorldBounds(SpriteBounds().Merge(InkBounds())) : new Rect2();

    internal Rect2[] VisualBounds
    {
        get
        {
            if (_dead || RallyDriven || !BossStyle || !_hasTex || !IsVisibleInTree()) return System.Array.Empty<Rect2>();
            var sprite = SpriteBounds();
            var bounds = new List<Rect2> { WorldBounds(sprite), WorldBounds(InkBounds()) };
            void Echo(Vector2 at) { if (MotionVisible(at)) bounds.Add(WorldBounds(new Rect2(sprite.Position + at - Position, sprite.Size))); }
            if (_owner.UnfoldersActive && !Invulnerable)
            {
                if (_frameOrbitBlend < .01f && _pose.EchoAlpha > .01f) { Echo(_pose.EchoA); Echo(_pose.EchoB); }
                if (_pose.WarpCue > 0 && MotionVisible(_pose.WarpTo, warpCue: true))
                {
                    Echo(_pose.WarpTo);
                    bounds.Add(WorldBounds(new Rect2(_pose.WarpTo - Position - Vector2.One * WarpExtent,
                        Vector2.One * WarpExtent * 2)));
                }
                if (_pose.WarpFlash > .04f) Echo(_pose.WarpFrom);
            }
            return bounds.ToArray();
        }
    }

    private Rect2 WorldBounds(Rect2 rect)
    {
        var origin = ToGlobal(rect.Position);
        var bounds = new Rect2(origin, Vector2.Zero);
        bounds = bounds.Expand(ToGlobal(rect.Position + new Vector2(rect.Size.X, 0)));
        bounds = bounds.Expand(ToGlobal(rect.End));
        return bounds.Expand(ToGlobal(rect.Position + new Vector2(0, rect.Size.Y)));
    }

    internal (Vector2 From, Vector2 To)[] VisualLinks
    {
        get
        {
            if (_dead || RallyDriven || !BossStyle || !IsVisibleInTree() || !_owner.UnfoldersActive
                || Invulnerable || _pose.WarpFlash <= 0) return System.Array.Empty<(Vector2, Vector2)>();
            var from = ToGlobal(_pose.WarpFrom - Position);
            var to = GlobalPosition;
            var face = BossIllustrationMotion.FaceClearance(_owner).Grow(.5f);
            float entry = 0, exit = 1;
            var delta = to - from;
            bool Clip(float start, float direction, float min, float max)
            {
                if (Mathf.Abs(direction) < .0001f) return start >= min && start <= max;
                float a = (min - start) / direction, b = (max - start) / direction;
                entry = Mathf.Max(entry, Mathf.Min(a, b));
                exit = Mathf.Min(exit, Mathf.Max(a, b));
                return entry <= exit;
            }
            if (!Clip(from.X, delta.X, face.Position.X, face.End.X)
                || !Clip(from.Y, delta.Y, face.Position.Y, face.End.Y)) return new[] { (from, to) };
            var lines = new List<(Vector2, Vector2)>();
            if (entry > 0) lines.Add((from, from + delta * entry));
            if (exit < 1) lines.Add((from + delta * exit, to));
            return lines.ToArray();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_dead) return;
        if (BossStyle && !RallyDriven) Modulate = TintLift.Of(_worldTint, TintLift.EnemyBody);
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
        if (Hud.BubblePaused)
        {
            if (BossStyle) UpdateOrbit(0);
            return;
        }
        UpdateOrbit(delta);
    }

    private int InkCost(Bullet bullet)
    {
        if (_owner is MidEnemy) return Mathf.Max(1, bullet.Damage);
        int cost = 1 + Mathf.Max(0, bullet.Damage - 1) / 2;
        if (!_owner.HasHpBar) return cost;
        if (bullet.Charged)
        {
            if (bullet.ChargeJob == Job.Magic && !bullet.ChargeFanCenter) return 1;
            return Mathf.Max(1, Mathf.RoundToInt(Jobs.Get(bullet.ChargeJob).ChargeShieldPower
                * ChargeTier.PowerMulFor(bullet.ChargeStage)));
        }
        return Mathf.Min(cost, BossStyle && !bullet.Accel ? 1 : 2);
    }

    private void OnAreaEntered(Area2D area)
    {
        if (_dead || Invulnerable || _owner.RallyGuarded || Hud.BubblePaused) return;
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
            if (_owner.TechniquePractice) return;
            int cost = InkCost(b);
            int damage = player?.PoweredShotDamage(b, cost) ?? cost;
            if (_owner.HasHpBar) GameManager.Instance?.AddBossHit(Mathf.Min(damage, Ink));
            Ink -= damage;
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

    internal bool TakeSpecialHit(int damage)
    {
        if (damage <= 0 || !CanLock || Hud.BubblePaused || !_owner.UnfoldersActive
            || _owner.TechniquePractice || _owner.Transforming || _owner.RallyGuarded) return false;
        if (_owner.HasHpBar) GameManager.Instance?.AddBossHit(Mathf.Min(damage, Ink));
        Ink = Mathf.Max(0, Ink - damage);
        if (Ink <= 0) Shatter();
        else
        {
            _hitFlashT = HitFlashDur;
            Audio.Instance?.PlayStrip(light: true);
            QueueRedraw();
        }
        return true;
    }

    public void WeakenByBomb()
    {
        if (_owner.TechniquePractice) return;
        if (_dead || Invulnerable || _owner.RallyGuarded) return;
        int damage = Mathf.Max(1, Mathf.FloorToInt(_maxInk * .25f * (GameManager.Instance?.BombPowerMul ?? 1f)));
        if (_owner.HasHpBar) GameManager.Instance?.AddBossHit(Mathf.Min(damage, Ink));
        Ink = Mathf.Max(0, Ink - damage);
        if (Ink <= 0) Shatter();
        else { _hitFlashT = HitFlashDur; QueueRedraw(); }
    }

    public void WeakenByRipple()
    {
        if (_owner.TechniquePractice) return;
        if (_dead || Invulnerable || _owner.RallyGuarded) return;
        if (_owner is MidEnemy)
        {
            Ink = Mathf.Max(0, Ink - 6);
            if (Ink > 0) { _hitFlashT = HitFlashDur; QueueRedraw(); return; }
        }
        if (_owner.HasHpBar) GameManager.Instance?.AddBossHit(Ink);
        Shatter();
    }

    public void Shatter()
    {
        if (_owner.TechniquePractice) return;
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
        if (_dead || RallyDriven) return;
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
        float halfH = DisplayHeight * VisualScale * 0.5f;
        // High durability must not stretch the indicator across neighboring panels.
        if (_owner is MidEnemy || _maxInk > 4)
        {
            float width = 12f * VisualScale;
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
            if (alpha <= 0.01f || !MotionVisible(at)) return;
            DrawSetTransform(at - Position, _pose.Tilt);
            DrawTextureRect(_sprite.Texture, new Rect2(-size * 0.5f, size), false,
                new Color(tint, alpha));
            DrawSetTransform(Vector2.Zero);
        }
        if (_frameOrbitBlend < .01f)
        {
            Echo(_pose.EchoA, _pose.EchoAlpha);
            Echo(_pose.EchoB, _pose.EchoAlpha);
        }
        if (_pose.WarpCue > 0 && MotionVisible(_pose.WarpTo, warpCue: true))
        {
            var to = _pose.WarpTo - Position;
            Echo(_pose.WarpTo, 0.1f + _pose.WarpCue * 0.18f);
            float extent = WarpExtent - .5f;
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
            foreach (var link in VisualLinks)
                DrawLine(ToLocal(link.From), ToLocal(link.To), new Color(tint, _pose.WarpFlash * 0.3f), 0.8f, true);
        }
    }
}
