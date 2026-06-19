using Godot;
using System.Collections.Generic;

// Enemy : SNSの悪意で「悪魔化した人間」本体。不滅で“倒さない/殺さない”。
// 周囲を旋回する黒い吹き出しパネル(Panel)を持ち、全部剥がされると【浄化(改心)】される。
// 浄化後は敵グループを抜け、笑顔の味方コメントとして左へ流れ、画面外でfree。
// 浄化の瞬間に「やさしさの波紋(Ripple)」を出し、近くの人を連鎖浄化する。
// 衝突: 本体 layer=4(接触で自機被弾)。パネルは Panel 側(layer16)。
public partial class Enemy : Area2D
{
    // 浄化(改心)時の基礎得点（派生で上書き）。
    protected int Points = 100;
    protected float BodyRadius = 9f;

    // パネル構成（派生で設定）。
    protected int PanelCount = 3;
    protected int PanelInk = 2;
    protected float OrbitRadius = 18f;
    protected float SpinSpeed = 1.4f; // rad/s
    protected bool PanelsFire = true;
    protected float PanelFireInterval = 1.9f;
    protected float EnemyBulletSpeed = 90f;

    // スプライト素材（null/未設定なら _Draw のプレースホルダ図形を使う）
    protected string PreTexPath = "";
    protected string PostTexPath = "";
    protected string PanelTexPath = "";
    // 任意：浄化の瞬間に一時表示する「大泣き」スプライト。設定すると pre→cry→(CryHoldDur秒)→post の3段階に。
    protected string CryTexPath = "";
    protected double CryHoldDur = 0;
    protected float BodyDisplayH = 40f;
    protected bool FaceLeft = true; // 進行方向(左=プレイヤー側)を向く。素材は右向きなので反転。
    private Sprite2D _bodySprite = null!;
    private bool _hasBodyTex;

    // ボス用：HP制（>0でHPバー方式に。剥がした枚数ぶんHPが減り、パネルは補充される）。
    protected int MaxHp = 0;
    protected float PanelRespawnDelay = 0f; // >0でパネル補充（ボス用）
    private int _hp;
    public bool HasHpBar => MaxHp > 0;
    public float HpRatio => MaxHp > 0 ? (float)_hp / MaxHp : 0f;

    private readonly List<Panel> _panels = new List<Panel>();
    private bool _purified;
    private bool _crying;     // 大泣き中（3段階浄化の中間）
    private double _cryT;
    private bool _flashing;
    private double _flashT;
    private const double FlashDur = 0.5;

    private CollisionShape2D _bodyShape = null!;

    public bool IsPurified => _purified;
    public int PanelsRemaining => _panels.Count;

    public override void _Ready()
    {
        AddToGroup("enemies");
        CollisionLayer = 4;  // 敵本体（接触で自機被弾）
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = true;
        _bodyShape = new CollisionShape2D { Shape = new CircleShape2D { Radius = BodyRadius } };
        AddChild(_bodyShape);

        OnEnemyReady();
        // 難易度は体力ではなく弾数で調整する方針のため、HP（剥がし回数）は固定。
        _hp = MaxHp;
        SetupBodySprite();
        SpawnPanels();
    }

    protected virtual void OnEnemyReady() { }

    // ─── スペルカードの弾形・色（RefrainHTML Danmaku v3）───
    // 派生ボスがパターン切替時に SetSpellVisual で更新し、FireBullet が反映する。
    protected BulletShape CurShape = BulletShape.Orb;
    protected Color CurTint;
    protected bool CurTintSet;
    protected void SetSpellVisual(BulletShape shape, Color tint)
    {
        CurShape = shape; CurTint = tint; CurTintSet = true;
    }
    // 現在のスペルの弾形・色で敵弾を1発撃つ（各ボスの pool.Spawn 置き換え用）。
    protected Bullet FireBullet(BulletPool pool, Vector2 pos, Vector2 vel, float radius = 3.4f, int dmg = 1)
        => pool.Spawn(pos, vel, isEnemy: true, radius, dmg, CurShape, CurTintSet ? CurTint : (Color?)null);

    // 難易度に応じた弾数。派生ボスが弾幕パターンの本数を安全にスケールするために使う。
    protected int Dn(int baseCount) =>
        GetNodeOrNull<GameManager>("/root/Game")?.ScaleBullets(baseCount) ?? baseCount;

    // 難易度に応じた発射間隔。やさしいほど長く（連射が遅く）なる。
    // 派生ボスは `_fireT >= Di(基準秒)` の形でしきい値に掛けて使う。
    protected double Di(double baseInterval) =>
        baseInterval * (GetNodeOrNull<GameManager>("/root/Game")?.DanmakuIntervalMul ?? 1f);

    private void SetupBodySprite()
    {
        if (string.IsNullOrEmpty(PreTexPath)) return;
        var t = ResourceLoader.Load<Texture2D>(PreTexPath);
        if (t == null) return;
        _hasBodyTex = true;
        _bodySprite = new Sprite2D
        {
            Name = "Body",
            Texture = t,
            Centered = true,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear, // 高解像度素材を滑らかに縮小
            ZIndex = -1, // パネルより奥
            FlipH = FaceLeft, // 素材は右向き→左(進行方向)へ反転
        };
        float s = BodyDisplayH / t.GetHeight();
        _bodySprite.Scale = new Vector2(s, s);
        AddChild(_bodySprite);
    }

    private void SpawnPanels()
    {
        for (int i = 0; i < PanelCount; i++)
            SpawnOnePanel(Mathf.Tau * i / Mathf.Max(1, PanelCount));
    }

    private void SpawnOnePanel(float baseAngle)
    {
        var p = new Panel();
        p.Setup(this, baseAngle, OrbitRadius, SpinSpeed, PanelsFire, PanelFireInterval, PanelInk, EnemyBulletSpeed, PanelTexPath);
        AddChild(p);
        _panels.Add(p);
    }

    // パネルが砕けた通知。
    // 通常敵：全部剥がれたら浄化。 ボス(HP制)：剥がした枚数ぶんHPを削り、パネルを補充。HP0で浄化。
    public void OnPanelStripped(Panel p)
    {
        _panels.Remove(p);

        if (MaxHp > 0)
        {
            if (_purified) return;
            _hp = Mathf.Max(0, _hp - 1);
            OnHpChanged();
            if (_hp <= 0) { Redeem(); return; }
            SchedulePanelRespawn();
            QueueRedraw();
            return;
        }

        if (_panels.Count == 0 && !_purified)
            Redeem();
        else
            QueueRedraw();
    }

    private void SchedulePanelRespawn()
    {
        if (PanelRespawnDelay <= 0f) return;
        var t = GetTree().CreateTimer(PanelRespawnDelay);
        t.Timeout += () =>
        {
            if (_purified || _crying || !IsInstanceValid(this)) return;
            if (_panels.Count < PanelCount) SpawnOnePanel(GD.Randf() * Mathf.Tau);
        };
    }

    // 外部（ボム等）から強制浄化。
    // ボス(HP制)はボムで即浄化しない：今あるパネルを剥がす（HPが少し減る）だけ。
    public void Purify()
    {
        if (_purified) return;
        if (MaxHp > 0)
        {
            foreach (var p in new List<Panel>(_panels))
                p.Shatter();
            return;
        }
        foreach (var p in new List<Panel>(_panels))
            p.Shatter();
        if (!_purified) Redeem();
    }

    // 進行方向に体を向ける（素材は右向き。flipH=true で左向き）。
    protected void SetSpriteFlip(bool flipH)
    {
        if (_hasBodyTex && _bodySprite != null)
            _bodySprite.FlipH = flipH;
    }

    // HPが変化した（HUDバー更新用フック）。
    protected virtual void OnHpChanged() { }

    // 改心処理（消さない。味方化して残る）。
    private void Redeem()
    {
        if (_purified) return;
        _purified = true;
        RemoveFromGroup("enemies");

        // 戦闘終了の瞬間：画面に残った自機の弾を消す（改心の会話に弾が飛び続けないように）。
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnPlayerBullets();

        // 接触で自機を傷つけないようにする。浄化は被弾シグナル中に走ることがあるため遅延設定。
        SetDeferred(Area2D.PropertyName.Monitorable, false);
        if (_bodyShape != null)
            _bodyShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);

        _flashing = true;
        _flashT = 0;

        // スコア＋コンボ（連鎖＝やさしさの広がり）。
        GetNodeOrNull<GameManager>("/root/Game")?.AddPurify(Points);

        // 浄化バースト演出＋やさしい言葉（バリエーション）＋浄化音（届いた余韻）
        FxLayer.Instance?.PurifyBurst(GlobalPosition);
        Audio.Instance?.PlayPurify();
        FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(0, -10), PickKindWord(), FxLayer.Sig2);

        // やさしさの波紋（連鎖浄化のトリガー）。
        // Redeem は被弾/パネル砕けのシグナル（物理クエリのフラッシュ中）から呼ばれることがある。
        // その最中に Ripple を即 AddChild すると Ripple._Ready が監視状態やコリジョン形状を
        // 物理フラッシュ中に書き換え（"Can't change this state while flushing queries"）、
        // 連鎖浄化が多発する場面で物理サーバを壊して落ちる。生成はフラッシュ後へ遅延する。
        var parent = GetParent();
        if (parent != null)
        {
            var ripple = new Ripple { Position = Position }; // 親が同じ＝同じ座標系のローカル位置をそのまま使う
            parent.CallDeferred(Node.MethodName.AddChild, ripple);
        }

        // 3段階対応：Cry の尺が設定されていれば先に大泣きを見せてから笑顔へ。
        // 専用立ち絵が無いボス（こはる等）でも会話に入れるよう、CryHoldDur のみで判定する
        //（SwapBody は内部で _hasBodyTex を確認するため、立ち絵が無ければ素通りする）。
        if (CryHoldDur > 0)
        {
            SwapBody(CryTexPath);
            _crying = true;
            _cryT = 0;
            OnCryStart();
        }
        else
        {
            SwapBody(PostTexPath);
            GrantFollower();
        }

        QueueRedraw();
    }

    // 本体スプライトを差し替えて表示高さに合わせて再スケール。
    private void SwapBody(string path)
    {
        if (!_hasBodyTex || string.IsNullOrEmpty(path)) return;
        var t = ResourceLoader.Load<Texture2D>(path);
        if (t == null) return;
        _bodySprite.Texture = t;
        float s = BodyDisplayH / t.GetHeight();
        _bodySprite.Scale = new Vector2(s, s);
    }

    // 救った人を algo のフォロワー（味方オプション）にする。派生で上書き可（ボス＝ヒカゲ強化）。
    protected virtual void GrantFollower()
    {
        var players = GetTree().GetNodesInGroup("player");
        if (players.Count > 0 && players[0] is Player pl)
            pl.AddFollower(GlobalPosition);
    }

    // 大泣き演出の開始／終了フック（派生でセリフ等に使う）。
    protected virtual void OnCryStart() { }
    protected virtual void OnCryEnd() { }

    // 手動送りで会話を終えたとき、Cry（その場停止）を即終了して笑顔へ着地。
    protected void EndCryNow()
    {
        if (!_crying) return;
        _crying = false;
        SwapBody(PostTexPath);
        OnCryEnd();
        GrantFollower();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_flashing)
        {
            _flashT += delta;
            if (_flashT >= FlashDur) _flashing = false;
            QueueRedraw();
        }

        // 大泣き中はその場に留まり、CryHoldDur 経過で笑顔へ着地。
        if (_crying)
        {
            _cryT += delta;
            if (_cryT >= CryHoldDur)
            {
                _crying = false;
                SwapBody(PostTexPath);
                OnCryEnd();
                GrantFollower();
            }
            return;
        }

        if (_purified)
        {
            // 笑顔の味方コメントとしてゆっくり左へ流れて退場。
            GlobalPosition += new Vector2(-30f * (float)delta, 0f);
            if (GlobalPosition.X < -24f) QueueFree();
            return;
        }

        if (Hud.BubblePaused) return; // 吹き出し表示中は動かない（襲ってこない）

        UpdateMovement(delta);
        if (GlobalPosition.X < -24f) QueueFree();
    }

    protected virtual void UpdateMovement(double delta) { }

    public override void _Draw()
    {
        // 改心フラッシュ（やさしい色：淡ピンク→淡紫に着地）
        if (_flashing)
        {
            float t = (float)(_flashT / FlashDur);
            var c = new Color(1f, 0.85f, 0.92f).Lerp(new Color(0.79f, 0.72f, 0.94f), t); // 淡ピンク→淡紫
            DrawCircle(Vector2.Zero, BodyRadius + 10f * (1f - t), new Color(c.R, c.G, c.B, 0.6f * (1f - t)));
        }

        // スプライトが無い時だけプレースホルダ図形を描く
        if (!_hasBodyTex)
            DrawPerson(_purified ? new Color(1f, 0.86f, 0.62f) : new Color(0.55f, 0.6f, 0.78f), happy: _purified);

        // 波紋射程プレビュー（残り1枚＝剥がし切ると波紋がここまで届く）
        if (!_purified && _panels.Count == 1)
            DrawArc(Vector2.Zero, Ripple.MaxRadius, 0, Mathf.Tau, 40, new Color(0.7f, 0.92f, 1f, 0.28f), 1f);
    }

    private static readonly string[] KindWords = { "ありがとう", "だいじょうぶ", "きみは悪くないよ", "ごめんね", "また話そう" };
    private static readonly RandomNumberGenerator _kw = new RandomNumberGenerator();
    private static string PickKindWord() => KindWords[_kw.RandiRange(0, KindWords.Length - 1)];

    private void DrawPerson(Color body, bool happy)
    {
        DrawCircle(new Vector2(0, 2), BodyRadius, body);                       // 体
        DrawCircle(new Vector2(0, -6), 5f, new Color(1f, 0.92f, 0.85f));       // 頭
        var eye = happy ? new Color(0.2f, 0.1f, 0.1f) : new Color(0.1f, 0.1f, 0.2f);
        float ey = happy ? -7f : -5f;
        DrawCircle(new Vector2(-2, ey), 0.9f, eye);
        DrawCircle(new Vector2(2, ey), 0.9f, eye);
        if (happy)
            DrawArc(new Vector2(0, -5), 2.2f, 0.2f, Mathf.Pi - 0.2f, 8, new Color(0.6f, 0.25f, 0.25f), 1f); // 笑み
    }
}
