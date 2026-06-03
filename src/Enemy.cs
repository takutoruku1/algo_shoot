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
    protected float BodyDisplayH = 40f;
    protected bool FaceLeft = true; // 進行方向(左=プレイヤー側)を向く。素材は右向きなので反転。
    private Sprite2D _bodySprite = null!;
    private bool _hasBodyTex;

    private readonly List<Panel> _panels = new List<Panel>();
    private bool _purified;
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
        SetupBodySprite();
        SpawnPanels();
    }

    protected virtual void OnEnemyReady() { }

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
        {
            var p = new Panel();
            float baseAngle = Mathf.Tau * i / Mathf.Max(1, PanelCount);
            p.Setup(this, baseAngle, OrbitRadius, SpinSpeed, PanelsFire, PanelFireInterval, PanelInk, EnemyBulletSpeed, PanelTexPath);
            AddChild(p);
            _panels.Add(p);
        }
    }

    // パネルが砕けた通知。最後の1枚が剥がれたら浄化。
    public void OnPanelStripped(Panel p)
    {
        _panels.Remove(p);
        if (_panels.Count == 0 && !_purified)
            Redeem();
        else
            QueueRedraw();
    }

    // 外部（ボム等）から強制浄化。全パネルを剥がす。
    public void Purify()
    {
        if (_purified) return;
        foreach (var p in new List<Panel>(_panels))
            p.Shatter();
        if (!_purified) Redeem();
    }

    // 改心処理（消さない。味方化して残る）。
    private void Redeem()
    {
        if (_purified) return;
        _purified = true;
        RemoveFromGroup("enemies");

        // 接触で自機を傷つけないようにする。
        Monitorable = false;
        if (_bodyShape != null)
            _bodyShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);

        _flashing = true;
        _flashT = 0;

        // 本体スプライトを「浄化後（笑顔）」へ差し替え。
        if (_hasBodyTex && !string.IsNullOrEmpty(PostTexPath))
        {
            var t = ResourceLoader.Load<Texture2D>(PostTexPath);
            if (t != null)
            {
                _bodySprite.Texture = t;
                float s = BodyDisplayH / t.GetHeight();
                _bodySprite.Scale = new Vector2(s, s);
            }
        }

        // スコア＋コンボ（連鎖＝やさしさの広がり）。
        GetNodeOrNull<GameManager>("/root/Game")?.AddPurify(Points);

        // やさしさの波紋（連鎖浄化のトリガー）。
        var parent = GetParent();
        if (parent != null)
        {
            var ripple = new Ripple();
            parent.AddChild(ripple);
            ripple.GlobalPosition = GlobalPosition;
        }

        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_flashing)
        {
            _flashT += delta;
            if (_flashT >= FlashDur) _flashing = false;
            QueueRedraw();
        }

        if (_purified)
        {
            // 笑顔の味方コメントとしてゆっくり左へ流れて退場。
            GlobalPosition += new Vector2(-30f * (float)delta, 0f);
            if (GlobalPosition.X < -24f) QueueFree();
            return;
        }

        UpdateMovement(delta);
        if (GlobalPosition.X < -24f) QueueFree();
    }

    protected virtual void UpdateMovement(double delta) { }

    public override void _Draw()
    {
        // 改心の虹色フラッシュ（スプライト有無に関わらず重ねる）
        if (_flashing)
        {
            float t = (float)(_flashT / FlashDur);
            var c = Color.FromHsv(Mathf.PosMod(t * 2f, 1f), 0.5f, 1f);
            DrawCircle(Vector2.Zero, BodyRadius + 8f * (1f - t), new Color(c.R, c.G, c.B, 0.55f * (1f - t)));
        }

        // スプライトが無い時だけプレースホルダ図形を描く
        if (!_hasBodyTex)
            DrawPerson(_purified ? new Color(1f, 0.86f, 0.62f) : new Color(0.55f, 0.6f, 0.78f), happy: _purified);

        // 波紋射程プレビュー（残り1枚＝剥がし切ると波紋がここまで届く）
        if (!_purified && _panels.Count == 1)
            DrawArc(Vector2.Zero, Ripple.MaxRadius, 0, Mathf.Tau, 40, new Color(0.7f, 0.92f, 1f, 0.28f), 1f);
    }

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
