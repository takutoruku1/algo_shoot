using Godot;
using System.Collections.Generic;

// BulletPool : Autoload シングルトン (/root/Pool)。
// _Ready で 512 発を事前生成しプール（非アクティブ）。
// Spawn でプールから取り出して Activate、Despawn で Deactivate して返却。
// 生成した Bullet は Pool 自身の子として add する。
public partial class BulletPool : Node2D
{
    private const int PreallocCount = 512;

    // 非アクティブな弾のスタック（再利用用）
    private readonly Stack<Bullet> _free = new Stack<Bullet>();

    public override void _Ready()
    {
        // 512 発を事前生成しプール（非アクティブ）
        for (int i = 0; i < PreallocCount; i++)
        {
            var b = CreateBullet();
            _free.Push(b);
        }
    }

    private Bullet CreateBullet()
    {
        var b = new Bullet();
        AddChild(b); // Pool 自身の子として add（_Ready 内で Deactivate される）
        return b;
    }

    // pos に vel で移動する弾を取り出して有効化。
    // プール枯渇時は追加生成して可。
    public Bullet Spawn(Vector2 pos, Vector2 vel, bool isEnemy, float radius = 3f, int damage = 1)
    {
        Bullet b;
        if (_free.Count > 0)
        {
            b = _free.Pop();
        }
        else
        {
            // 枯渇時は追加生成
            b = CreateBullet();
        }

        b.Activate(pos, vel, isEnemy, radius, damage);
        return b;
    }

    // 弾を非アクティブ化してプールへ返却。
    public void Despawn(Bullet b)
    {
        if (b == null)
            return;

        // 既に返却済みのものを二重に積まないようガード
        if (!b.Active)
        {
            // 念のため非アクティブ化を保証しつつ重複返却を避ける
            return;
        }

        b.Deactivate();
        _free.Push(b);
    }
}
