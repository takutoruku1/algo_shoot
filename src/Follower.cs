using Godot;

// Follower : 浄化(改心)した人がalgoの味方オプションになったもの。
// algoの周囲の定位置(SlotOffset)へ飛んできて追従し、algoのショットに同期してハート弾を撃つ。
// Player の子ノードとして配置されるので、algoと一緒に動き、リスタート時も一緒に消える。
public partial class Follower : Node2D
{
    public Vector2 SlotOffset; // algoローカル座標での定位置
    public bool IsHikage { get; private set; } // ヒカゲ強化フォロワーか

    private Sprite2D _sprite = null!;
    private float _bobT;
    private const float MoveInSpeed = 220f; // 飛んでくる速度(px/s)

    public override void _Ready()
    {
        ZIndex = 9; // 自機(10)のすぐ後ろ
        var tex = ResourceLoader.Load<Texture2D>("res://char/enemy_anti_post.png"); // 改心後の笑顔を流用
        _sprite = new Sprite2D
        {
            Centered = true,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        };
        if (tex != null)
        {
            _sprite.Texture = tex;
            float s = 18f / tex.GetHeight(); // 小さめ
            _sprite.Scale = new Vector2(s, s);
        }
        AddChild(_sprite);
        _bobT = GD.Randf() * 6f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _bobT += dt;
        // 定位置(+ふわふわ)へ寄っていく
        Vector2 target = SlotOffset + new Vector2(0f, Mathf.Sin(_bobT * 3f) * 1.5f);
        Position = Position.MoveToward(target, MoveInSpeed * dt);
    }

    // algoのショットに同期して前方へハート弾を撃つ。
    // 方向は親（Player）の向き反転（ShotDir）に追従する＝自機だけ左を向いて子分は右へ撃つ、が起きない。
    // 親が取れない（テスト等でぶら下がっていない）場合だけ従来どおり右へ撃つ。
    public void Fire()
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        Vector2 dir = (GetParent() as Player)?.ShotDir ?? Vector2.Right;
        if (IsHikage)
        {
            // 強化：太く速い弾を上下2way（火力アップ）
            pool.Spawn(GlobalPosition + new Vector2(0f, -3f), dir * 380f, isEnemy: false, 3.6f, 2);
            pool.Spawn(GlobalPosition + new Vector2(0f, 3f), dir * 380f, isEnemy: false, 3.6f, 2);
        }
        else
        {
            pool.Spawn(GlobalPosition, dir * 300f, isEnemy: false, 2.5f, 1);
        }
    }

    // W0 専用・非正典。正典導線からは到達しない（2026-09-06 ユーザー決定: ヒカゲは使わない）。以後この系統への追加投資はしない。
    // 通常フォロワーをヒカゲに強化（見た目と火力を変える）。
    public void PromoteToHikage()
    {
        IsHikage = true;
        var tex = ResourceLoader.Load<Texture2D>("res://char/enemy_hikage_post.png");
        if (tex != null && _sprite != null)
        {
            _sprite.Texture = tex;
            float s = 24f / tex.GetHeight(); // 通常より少し大きい
            _sprite.Scale = new Vector2(s, s);
        }
        ZIndex = 9;
    }
}
