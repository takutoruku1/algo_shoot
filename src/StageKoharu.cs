using Godot;

// StageKoharu : STAGE3「こはる（永遠に夕食を作り続ける台所）」進行（v2 [P-03]）。
//   1: ダイブ前〜着地の会話
//   2: ボス出現
//   3: ボス前の説明
//   4: ボス戦
//   5: 帰還の会話（投稿変化＋伏線④「妹を見ててくれ」）
//   6: FINAL（汚染暴走）へ遷移
public partial class StageKoharu : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
    private double _stepTime;
    private double _lineHold;
    private int _introLine;
    private BossKoharu _boss = null!;
    private bool _bossActive;
    private double _rainT;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private bool _zHeld;
    private bool _zEdge;
    private bool _startBannerShown;

    private const float SpawnX = 300f;
    private const string SCocky = "res://char/shonen_face.png";
    private const string SGentle = "res://char/shonen_gentle.png";

    // ダイブ前〜着地（v2 [P-03]）。who: 0=少年 / 1=ミナ・地 / 2=こはる。
    private static readonly (int who, string text, string face)[] Intro =
    {
        (1, "「今日も、誰のためでもないごはんを作った。」", ""),               // 投稿
        (1, "——そこは、台所でした。夕食の支度が、永遠に続いている。", ""),     // 地
        (0, "作っても料理は冷め、席は空のまま。ここは、この子の心だ。", SGentle),
        (1, "ご主人様。今日は、いつもより口数が少ないですね。", ""),
        (0, "……なんでもない。続けるぞ。", SCocky),
    };

    // ボス登場時の説明
    private static readonly (int who, string text, string face)[] BossIntro =
    {
        (0, "あれが、この子を覆ってる穢れだ。怒りの形をしている。", SCocky),
        (0, "怒りの下にあるのは、悲しみだ。剥がして、奥へ光を届けろ。", SGentle),
        (1, "……はい。あの子を傷つけずに、穢れだけ。やってみましょう。", ""),
    };

    // 帰還（v2 [P-03] 末尾）。投稿の変化＋伏線④。
    private static readonly (int who, string text, string face)[] Clear =
    {
        (1, "「ちゃんと食べてね。……あたしも、食べるから。」", ""),            // 投稿が変化
        (0, "もしぼくが寝坊して来られない日があったらさ。妹の様子でも、見ててくれよ。", SGentle),
        (1, "妹が、いらしたんですか。", ""),
    };

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        GetNodeOrNull<GameManager>("/root/Game")?.SetStageTarget(1);
    }

    public override void _Process(double delta)
    {
        _stepTime += delta;
        _lineHold += delta;
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        _zEdge = z && !_zHeld;
        _zHeld = z;
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("STAGE 3 START"); }
        switch (_step)
        {
            case 1: Step_Lines(delta, Intro); break;
            case 2: Step_BossSpawn(); break;
            case 3: Step_Lines(delta, BossIntro); break;
            case 4: Step_BossWait(); break;
            case 5: Step_Clear(delta); break;
            case 6: Step_Transition(); break;
        }
        if (_bossActive) Rain(delta);
    }

    private void Advance()
    {
        _step++;
        _stepStarted = false;
        _stepTime = 0;
    }

    private void Step_Lines(double delta, (int who, string text, string face)[] lines)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _introLine = 0;
            _lineHold = 0;
            Hud.HoldBubble = true;
            ShowLine(lines);
        }
        if (_zEdge && _lineHold >= 0.25)
        {
            _lineHold = 0;
            _introLine++;
            if (_introLine >= lines.Length)
            {
                Hud.HoldBubble = false;
                Hud.HideBubble();
                Advance();
                return;
            }
            ShowLine(lines);
        }
    }

    private void ShowLine((int who, string text, string face)[] lines)
    {
        var (who, text, face) = lines[_introLine];
        if (who == 0) Hud.ShowDialog(text, face);
        else if (who == 2) Hud.ShowDialog(text, "res://char/koharu_face.png"); // 立ち絵未用意＝立ち絵なし
        else Hud.ShowDialog(text, "res://char/mina_face.png");
    }

    private void Step_BossSpawn()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _boss = new BossKoharu { Name = "BossKoharu" };
            World.AddChild(_boss);
            _boss.GlobalPosition = new Vector2(SpawnX, 70f);
            _bossActive = true;
            Advance();
        }
    }

    private void Step_BossWait()
    {
        if (!IsInstanceValid(_boss) || _boss.Finished)
        {
            _bossActive = false;
            Advance();
        }
    }

    private bool _clearBannerShown;
    private void Step_Clear(double delta)
    {
        if (!_clearBannerShown) { _clearBannerShown = true; Hud.ShowBanner("STAGE 3 CLEAR"); }
        Step_Lines(delta, Clear);
    }

    private bool _clearing;
    private void Step_Transition()
    {
        if (_clearing) return;
        _clearing = true;
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetTree().ChangeSceneToFile("res://Final.tscn");
    }

    // 道中の言葉弾（「むだだよ」等が降る）。会話中は止む。
    private void Rain(double delta)
    {
        if (Hud.BubblePaused) return;
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        _rainT += delta;
        float mul = GetNodeOrNull<GameManager>("/root/Game")?.DanmakuIntervalMul ?? 1f;
        if (_rainT < 0.17 * mul) return;
        _rainT = 0;
        float x = _rng.RandfRange(8f, 376f);
        float vx = _rng.RandfRange(-11f, 11f);
        pool.Spawn(new Vector2(x, -6f), new Vector2(vx, 72f), isEnemy: true, 3f, 1);
    }
}
