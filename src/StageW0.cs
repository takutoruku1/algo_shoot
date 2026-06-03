using Godot;

// StageW0 : チュートリアル進行（W0 やすらぎの庭）
// 経過時間ベースの簡易ステップ機械でタイムラインを進める。
//   Step1: 移動説明
//   Step2: ショット説明 + PageShard を1体出す
//   Step3: 撃って浄化しよう + GlyphMote を数体ウェーブ生成
//   Step4: 敵殲滅後 STAGE CLEAR バナー
// 参照（Player/Hud/World）は Main から受け取る。敵は World 配下に add する。
public partial class StageW0 : Node
{
    // Main から渡される参照
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    // 簡易ステップ機械
    private int _step;            // 現在のステップ番号（1..）
    private bool _stepStarted;    // 現在ステップの開始処理が済んだか
    private double _stepTime;     // 現在ステップ開始からの経過時間

    // 敵スポーンの基準座標（右外から登場）
    private const float SpawnX = 390f;

    public override void _Ready()
    {
        _step = 1;
        _stepStarted = false;
        _stepTime = 0;
    }

    public override void _Process(double delta)
    {
        _stepTime += delta;

        switch (_step)
        {
            case 1: Step1_Move(); break;
            case 2: Step2_Shoot(); break;
            case 3: Step3_Wave(); break;
            case 4: Step4_Clear(); break;
            // case 5: 完了（何もしない）
        }
    }

    // ステップ遷移ヘルパ
    private void Advance()
    {
        _step++;
        _stepStarted = false;
        _stepTime = 0;
    }

    // Step1: 移動説明（数秒）
    private void Step1_Move()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            Hud.ShowMessage("Move: Arrow Keys / 矢印キーで移動");
        }

        if (_stepTime >= 4.0)
        {
            Advance();
        }
    }

    // Step2: ショット説明 + PageShard 1体
    private void Step2_Shoot()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            Hud.ShowMessage("Shoot: Z / Zキーでショット");
            SpawnPageShard(new Vector2(SpawnX, 108));
        }

        // 出した PageShard を撃破したら次へ。安全のため一定時間で先に進む保険も用意。
        if (_stepTime >= 1.0 && CountEnemies() == 0)
        {
            Advance();
        }
        else if (_stepTime >= 14.0)
        {
            Advance();
        }
    }

    // Step3: GlyphMote ウェーブ
    private void Step3_Wave()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            Hud.ShowMessage("Dodge & purify! / 撃って浄化しよう");

            // 数体を縦にばらして生成
            SpawnGlyphMote(new Vector2(SpawnX, 60));
            SpawnGlyphMote(new Vector2(SpawnX + 40, 108));
            SpawnGlyphMote(new Vector2(SpawnX + 80, 156));
            SpawnGlyphMote(new Vector2(SpawnX + 120, 84));
            SpawnGlyphMote(new Vector2(SpawnX + 160, 132));
        }

        // 全滅したらクリアへ（生成直後の0判定を避けるため少し待つ）
        if (_stepTime >= 1.0 && CountEnemies() == 0)
        {
            Advance();
        }
    }

    // Step4: STAGE CLEAR バナー
    private void Step4_Clear()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            Hud.ShowBanner("STAGE CLEAR!  W0 やすらぎの庭");
            Advance(); // 以後は何もしないステップへ
        }
    }

    // "enemies" グループの現在数を数える
    private int CountEnemies()
    {
        return GetTree().GetNodesInGroup("enemies").Count;
    }

    private void SpawnPageShard(Vector2 pos)
    {
        var e = new PageShard();
        World.AddChild(e);
        e.GlobalPosition = pos;
    }

    private void SpawnGlyphMote(Vector2 pos)
    {
        var e = new GlyphMote();
        World.AddChild(e);
        e.GlobalPosition = pos;
    }
}
