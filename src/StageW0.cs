using Godot;

// StageW0 : チュートリアル進行（W0 やすらぎの庭）＋ 物語の導入。
// 経過時間ベースの簡易ステップ機械でタイムラインを進める。
//   1-3: オープニング導入（世界・algo・目的）
//   4  : 移動チュートリアル
//   5  : ショット説明 + うつむきさん(練習台)
//   6  : 浄化＆連鎖の本番ウェーブ
//   7  : エリア浄化完了（締め）
// 参照（Player/Hud/World）は Main から受け取る。
public partial class StageW0 : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
    private double _stepTime;
    private Spawner _spawner = null!;
    private BossHikage _boss = null!;
    private int _mainBase; // 本編開始時の累計浄化数（ウェーブ進捗の基準）

    private const float SpawnX = 390f;
    private const int WaveCount = 12;   // 本編で浄化する雑魚の数（ボス前）
    private const int StageTarget = 18; // 浄化ゲージ満タン＝チュートリアル＋本編＋ボス想定

    public override void _Ready()
    {
        _step = 1;
        _stepStarted = false;
        _stepTime = 0;
        GetNodeOrNull<GameManager>("/root/Game")?.SetStageTarget(StageTarget);
    }

    public override void _Process(double delta)
    {
        _stepTime += delta;
        switch (_step)
        {
            // 冒頭：algo が話すシーン（立ち絵＋吹き出し）
            case 1: Talk("…ん。めが、さめた。", 3.0); break;
            case 2: Talk("ここは、声が流れる世界 ― タイムライン。", 3.4); break;
            case 3: Talk("黒い言葉が、みんなの心を歪めてる…。", 3.4); break;
            case 4: Talk("だいじょうぶ。ボクが“やさしさ”を取り戻すよ。", 3.6); break;
            case 5: Talk("ハル……あなたを、もう一度さがしに行くね。", 3.6); break;
            // チュートリアル＆本番
            case 6: Step_Move(); break;
            case 7: Step_Shoot(); break;
            case 8: Step_Wave(); break;
            case 9: Step_TutorialClear(); break;
            // ステージ本編：連続スポナー＋浄化ゲージ目標
            case 10: Step_MainStart(); break;
            case 11: Step_MainLoop(); break;
            // climax：炎上ボス「ヒカゲ」
            case 12: Step_BossIntro(); break;
            case 13: Step_BossWait(); break;
            case 14: Step_StageClear(); break;
        }
    }

    private void Advance()
    {
        _step++;
        _stepStarted = false;
        _stepTime = 0;
    }

    // ---- algo が話す（立ち絵＋吹き出し）。表示中は敵が止まる ----
    private void Talk(string line, double dur)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            Hud.ShowDialog(line);
        }
        if (_stepTime >= dur) Advance();
    }

    // ---- 4: 移動 ----
    private void Step_Move()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            Hud.ShowMessage("矢印キーで動こう（Shiftでゆっくり）");
        }
        if (_stepTime >= 4.0) Advance();
    }

    // ---- 5: ショット説明 + うつむきさん ----
    private void Step_Shoot()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            Hud.ShowMessage("Zの光で、黒い吹き出しを剥がそう");
            SpawnPageShard(new Vector2(SpawnX, 108));
        }
        if (_stepTime >= 1.0 && CountEnemies() == 0) Advance();
        else if (_stepTime >= 16.0) Advance();
    }

    // ---- 6: 浄化＆連鎖の本番 ----
    private void Step_Wave()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            Hud.ShowMessage("全部剥がせば“浄化”！やさしさは連鎖する");
            SpawnGlyphMote(new Vector2(SpawnX, 92));
            SpawnGlyphMote(new Vector2(SpawnX + 34, 120));
            SpawnGlyphMote(new Vector2(SpawnX + 70, 100));
            SpawnGlyphMote(new Vector2(SpawnX + 104, 128));
        }
        if (_stepTime >= 1.0 && CountEnemies() == 0) Advance();
    }

    // ---- 9: チュートリアル浄化完了（本編への導入） ----
    private void Step_TutorialClear()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            Hud.ShowBanner("やさしさが、ひろがった。");
            Hud.ShowDialog("ね、やさしさって…ちゃんと、ひろがるんだ。この先は、声の濁流。みんなを浄化して、空を晴らそう！");
        }
        if (_stepTime >= 4.5) Advance(); // 会話を見せてから本編へ
    }

    // ---- 10: 本編開始（スポナー起動＋目標提示） ----
    private void Step_MainStart()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _mainBase = GetNodeOrNull<GameManager>("/root/Game")?.PurifiedCount ?? 0;
            _spawner = new Spawner { Name = "Spawner", World = World };
            AddChild(_spawner);
            _spawner.Begin();
            Hud.ShowMessage("浄化ゲージを満タンに！タイムラインを晴らそう");
        }
        if (_stepTime >= 1.0) Advance();
    }

    // ---- 11: 本編ループ（ウェーブ規定数を浄化したらボスへ） ----
    private void Step_MainLoop()
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (game != null && game.PurifiedCount - _mainBase >= WaveCount)
        {
            _spawner?.Stop();
            Advance();
        }
    }

    // ---- 12: ボス前の予兆＋登場 ----
    private void Step_BossIntro()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            Hud.ShowDialog("……来る。すごく大きな、炎上だ。");
        }
        // 残った雑魚がはけて会話を見せたら、ヒカゲ登場
        if (_stepTime >= 3.5 && CountEnemies() == 0)
        {
            _boss = new BossHikage { Name = "BossHikage" };
            World.AddChild(_boss);
            _boss.GlobalPosition = new Vector2(SpawnX + 20f, 108f);
            Advance();
        }
        else if (_stepTime >= 10.0) // 念のためのタイムアウト
        {
            _boss = new BossHikage { Name = "BossHikage" };
            World.AddChild(_boss);
            _boss.GlobalPosition = new Vector2(SpawnX + 20f, 108f);
            Advance();
        }
    }

    // ---- 13: ボス戦（浄化→大泣き→最高の笑顔の完了まで） ----
    private void Step_BossWait()
    {
        if (!IsInstanceValid(_boss) || _boss.Finished)
            Advance();
    }

    // ---- 12: ステージクリア（空が晴れる） ----
    private void Step_StageClear()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            Hud.ShowBanner("空が、晴れた。");
            Hud.ShowDialog("ヒカゲ、もう ひとりじゃないよ。やさしさが、世界を温め直してる…。ハル、もうすこし、まっててね。");
            Advance();
        }
    }

    private int CountEnemies() => GetTree().GetNodesInGroup("enemies").Count;

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
