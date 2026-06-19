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
    // プール枯渇時は追加生成して可。shape/tint で弾形・スペル色を指定（敵弾のみ反映）。
    public Bullet Spawn(Vector2 pos, Vector2 vel, bool isEnemy, float radius = 3f, int damage = 1,
        BulletShape shape = BulletShape.Orb, Color? tint = null, bool homing = false)
    {
        // 難易度で敵弾の速度をスケール（やさしいほど遅く＝避けやすい）。
        if (isEnemy)
        {
            float mul = GetNodeOrNull<GameManager>("/root/Game")?.BulletSpeedMul ?? 1f;
            vel *= mul;
        }

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

        b.Activate(pos, vel, isEnemy, radius, damage, shape, tint, homing);
        return b;
    }

    // 画面上の全弾を一括で非アクティブ化（リスタート時のクリア用）。
    public void DespawnAll()
    {
        foreach (Node c in GetChildren())
            if (c is Bullet b && b.Active)
                Despawn(b);
    }

    // 画面上の自機弾だけを一括で非アクティブ化（戦闘終了＝ボス浄化の瞬間に残弾を片付ける用）。
    public void DespawnPlayerBullets()
    {
        foreach (Node n in GetTree().GetNodesInGroup("player_bullets"))
            if (n is Bullet b && b.Active)
                Despawn(b);
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
