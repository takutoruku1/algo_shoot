using Godot;

// TrainingDummy : トレーニングモード（試し打ち場）専用の「数値が見える実験台」。
//   本編の Enemy を継承する＝ホーミング／バックファイア／連鎖の光／集中の光／ボムの
//   ターゲット探索・ダメージ経路がすべて本編と同一に効く（それらは "enemies" グループの
//   `n is Enemy e && !e.IsPurified` を見る＝Enemy 型でないと拾われない。旧・独自 Area2D 実装では
//   ホーミングが誘導されず／ボムが通らなかった＝#4/#5 の原因）。
//
//   ただし本編ボスの無防備窓サイクル（Shielded→Break→Exposed→Reclose・窓キャップ・改心退場）は
//   DPS 計測を歪めるので使わない。非ボス(BarCount=0・パネル0)として基底を不活性に保ち、
//   本体の被弾は自前の HP へ通す：
//     ・自機弾ヒット … 自前の AreaEntered で受け、当たった弾の Damage ぶん自前HPを削る（貫通/連鎖/集中も
//       本編と同じ作法で処理）。基底の OnBodyHitByPlayerBullet は _phase!=Exposed で早期 return＝不活性。
//     ・ボム直撃    … Purify() を上書きして BombStrikeBase×BombPowerMul を自前HPへ通す（本編と同じ量）。
//   与ダメは毎回 DPS 計へ通知し、数字を出す。HP0で一拍だけ倒れ表示→満タン復活（撃ち込みを止めない）。
public partial class TrainingDummy : Enemy
{
    public const int DummyMaxHp = 4000;        // 大きめ＝DPSが安定して読める。倒したら即リセット。
    private const float Radius = 12f;           // 撃ち込みやすい当たり判定（実験台なので大きめ）。
    private const double DownHold = 0.45;       // 撃破→復活までの余韻。

    // 与ダメージの通知先（TrainingRoot が DPS 集計に使う）。null 安全。
    public System.Action<int>? OnDamaged;

    private int _hpDummy = DummyMaxHp;
    private bool _down;
    private double _downT;
    private double _flashT;
    private float _flashMag;
    private double _bobT;
    private CollisionShape2D _hitShape = null!;
    private Sprite2D _tex = null!;
    private bool _hasTex;

    protected override void OnEnemyReady()
    {
        // 非ボス（BarCount=0）・パネル0＝基底のフェーズ機構/パネル剥がし/改心退場をすべて不活性化する。
        BarCount = 0;
        PanelCount = 0;
        PanelsFire = false;
        BodyRadius = Radius;
        AutoBank = false; // 姿勢は自前（ゆらゆら）。基底の自動バンクは切る。
    }

    public override void _Ready()
    {
        base._Ready(); // AddToGroup("enemies") / CollisionLayer=4 / Monitorable / AreaEntered(基底) 等はそのまま使う。

        // 本体が常に自機弾(layer=2)を拾えるよう監視ON＋mask を開く（基底は Exposed のときだけ開く）。
        Monitoring = true;
        SetCollisionMaskValue(2, true);
        AreaEntered += OnHitByPlayerBullet; // 自前の被弾処理（基底ハンドラは _phase!=Exposed で不活性）。

        // 撃ち込み台の見た目（アンチくん pre。無ければプレースホルダ）。
        var t = ResourceLoader.Load<Texture2D>("res://char/enemy_anti_pre.png");
        if (t != null)
        {
            _hasTex = true;
            _tex = new Sprite2D
            {
                Name = "DummyBody",
                Texture = t,
                Centered = true,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                ZIndex = -1,
                FlipH = true, // 素材は右向き→自機（左）を向く
            };
            float s = 40f / t.GetHeight();
            _tex.Scale = new Vector2(s, s);
            AddChild(_tex);
        }
        _hitShape = new CollisionShape2D { Shape = new CircleShape2D { Radius = Radius } };
        AddChild(_hitShape);
        ZIndex = 5;
    }

    public int Hp => _hpDummy;
    public float HpRatio => (float)_hpDummy / DummyMaxHp;

    // 基底の移動は使わず、ゆらゆら（見た目のみ・当たり判定は不動）。
    protected override void UpdateMovement(double delta)
    {
        _bobT += delta;
        if (_hasTex && _tex != null)
            _tex.Position = new Vector2(0f, Mathf.Sin((float)_bobT * 2.4f) * 2.2f);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_flashT > 0) _flashT -= delta;

        if (_down)
        {
            _downT += delta;
            if (_downT >= DownHold) Revive();
            QueueRedraw();
            return;
        }
        base._PhysicsProcess(delta); // UpdateMovement（ゆらゆら）を回す。IsPurified=false なので改心退場には入らない。
        if (_flashT > 0) QueueRedraw();
    }

    // 自機弾ヒット（貫通/連鎖/集中は本編 Enemy と同じ作法）。
    private void OnHitByPlayerBullet(Area2D area)
    {
        if (_down) return;
        if (area is Bullet b && !b.IsEnemy && b.Active)
        {
            b.TryChain(this);                                  // 連鎖の光：Despawn 前に跳弾
            if (b.Pierce > 0) b.Pierce--;                      // 貫く光：残貫通は素通し
            else GetNodeOrNull<BulletPool>("/root/Pool")?.Despawn(b);
            (GetTree().GetFirstNodeInGroup("player") as Player)?.NotifyShotHit(this); // 集中の光
            ApplyDamage(Mathf.Max(1, b.Damage), FxLayer.Sig2);
        }
    }

    // ボム直撃：本編と同じ量（BombStrikeBase×BombPowerMul）を自前HPへ通す＝「ボムがどのぐらい効くか」を数値化。
    //   Player.TryBomb が画面内の全 Enemy に Purify() を呼ぶ経路にそのまま乗る（#4）。基底は呼ばない。
    public override void Purify()
    {
        if (_down) return;
        var game = GetNodeOrNull<GameManager>("/root/Game");
        int dmg = Mathf.RoundToInt(BombStrikeBase * (game?.BombPowerMul ?? 1f));
        ApplyDamage(Mathf.Max(1, dmg), FxLayer.Gold, big: true);
    }

    // ダメージ適用＋数字＋DPS通知＋撃破判定を1本に集約。
    private void ApplyDamage(int dmg, Color numCol, bool big = false)
    {
        _hpDummy = Mathf.Max(0, _hpDummy - dmg);
        OnDamaged?.Invoke(dmg); // DPS 計へ
        FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(GD.Randf() * 8 - 4, -14), dmg.ToString(), numCol, big ? 15 : 9);
        _flashT = 0.16;
        _flashMag = dmg;
        Audio.Instance?.PlayBossHit(Mathf.Clamp(dmg, 1, 4));
        if (_hpDummy <= 0) Down();
        else QueueRedraw();
    }

    private void Down()
    {
        _down = true;
        _downT = 0;
        // 復活待ち中は弾を拾わない。IsPurified には触れない（ホーミング標的は復活で維持したいので
        // グループから外して隠す＝AcquireTarget は !IsPurified に加え存在するノードしか拾わない）。
        SetDeferred(Area2D.PropertyName.Monitoring, false);
        if (_hitShape != null) _hitShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
        RemoveFromGroup("enemies");
        FxLayer.Instance?.PurifyBurst(GlobalPosition);
        FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(0, -20), "RESET", FxLayer.Gold, 12);
        Audio.Instance?.PlayPurify();
    }

    private void Revive()
    {
        _down = false;
        _downT = 0;
        _hpDummy = DummyMaxHp;
        AddToGroup("enemies");
        SetDeferred(Area2D.PropertyName.Monitoring, true);
        if (_hitShape != null) _hitShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, false);
        QueueRedraw();
    }

    public override void _Draw()
    {
        // 被弾の白い衝撃リング（短く・即・尾を引かない）。
        if (_flashT > 0)
        {
            float h = (float)(_flashT / 0.16);
            float rr = Radius + 4f + (10f + 3f * _flashMag) * (1f - h);
            DrawCircle(Vector2.Zero, rr, new Color(1f, 1f, 1f, 0.4f * h));
            DrawArc(Vector2.Zero, rr, 0, Mathf.Tau, 28, new Color(1f, 1f, 1f, 0.8f * h), 2f);
        }

        if (_down)
        {
            float k = (float)(_downT / DownHold);
            DrawCircle(Vector2.Zero, Radius + 8f * k, new Color(0.79f, 0.72f, 0.94f, 0.4f * (1f - k)));
            return;
        }

        if (!_hasTex)
        {
            DrawCircle(new Vector2(0, 2), Radius, new Color(0.55f, 0.6f, 0.78f));
            DrawCircle(new Vector2(0, -6), 6f, new Color(1f, 0.92f, 0.85f));
        }

        // HPバー（本体の上）。数値が見える実験台＝残量を常時可視化。
        const float bw = 40f, bh = 4f;
        float by = -Radius - 12f;
        DrawRect(new Rect2(-bw / 2f, by, bw, bh), new Color(0f, 0f, 0f, 0.55f));
        DrawRect(new Rect2(-bw / 2f, by, bw * HpRatio, bh), new Color(0.42f, 0.74f, 0.85f));
        DrawRect(new Rect2(-bw / 2f, by, bw, bh), new Color(1f, 1f, 1f, 0.25f), false, 0.6f);
    }
}
