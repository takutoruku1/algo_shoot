using Godot;

// StageRei : STAGE1「レイ（順位掲示板の海）」進行＋操作チュートリアル（移動/ショット/かすり）。
//   1: 導線・着地＋チュートリアル会話
//   2: ボス出現
//   3: ボス前の説明
//   4: ボス戦
//   5: クリア → STAGE2(あかり)へ
public partial class StageRei : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
    private double _stepTime;
    private double _lineHold;
    private int _introLine;
    private BossRei _boss = null!;
    private bool _bossActive;
    private double _rainT;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private bool _zHeld;
    private bool _zEdge;
    private bool _startBannerShown;

    private const float SpawnX = 300f;
    private const string SCocky = "res://char/shonen_face.png";
    private const string SGentle = "res://char/shonen_gentle.png";
    private const string SProud = "res://char/shonen_proud.png";

    // 着地＋チュートリアル（移動/ショット/かすり）の会話（who: 0=少年 / 1=ミナ）
    private static readonly (int who, string text, string face)[] Intro =
    {
        (1, "ここが……人の、心の中。", ""),
        (0, "そうだ。よく見ろ。この風景ぜんぶが、この人の“今の気持ち”だ。", SCocky),
        (0, "飛んでくるのは、この人を苦しめてる“言葉”だ。本人じゃない。撃って祓っていい。", SCocky),
        (0, "倒すんじゃない。いちばん奥の“本当のこの人”に、光を届けるのが、ぼくらの仕事だ。", SGentle),
        (0, "怖がらず、近づけ。弾にかすって、相手の本音に触れにいくんだ。", SGentle),
        (1, "……人使いが荒いですね、ご主人様は。やってみますけど。", ""),
    };

    // ボス登場時の説明
    private static readonly (int who, string text, string face)[] BossIntro =
    {
        (0, "来た。あれが、この人の心を覆ってる“穢れ”の核だ。", SCocky),
        (1, "ご主人様、ずいぶん張り切ってますね。初仕事だからですか。", ""),
        (0, "当然だろ。天才のデビュー戦だぞ。完璧に決めるさ。", SProud),
        (1, "デビュー戦で空回りしないことを祈りますね。", ""),
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
        if (!_startBannerShown) { _startBannerShown = true; Hud.ShowBanner("STAGE 1 START"); }
        switch (_step)
        {
            case 1: Step_Lines(delta, Intro); break;
            case 2: Step_BossSpawn(); break;
            case 3: Step_Lines(delta, BossIntro); break;
            case 4: Step_BossWait(); break;
            case 5: Step_Clear(); break;
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
        else Hud.ShowDialog(text, "res://char/mina_face.png");
    }

    private void Step_BossSpawn()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _boss = new BossRei { Name = "BossRei" };
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

    private bool _clearing;
    private void Step_Clear()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _stepTime = 0;
            Hud.ShowBanner("STAGE 1 CLEAR");
            Hud.ShowDialog("初仕事、お見事でした。……次の人のところへ、行きましょうか。", "res://char/mina_face.png");
        }
        // 余韻のあと STAGE2（あかり）へ（多重遷移ガード）
        if (!_clearing && _stepTime >= 5.5)
        {
            _clearing = true;
            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            GetTree().ChangeSceneToFile("res://Akari.tscn");
        }
    }

    // 道中の言葉弾（「どうせ二番」等が降る）。会話中は止む。
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
        float vx = _rng.RandfRange(-12f, 12f);
        pool.Spawn(new Vector2(x, -6f), new Vector2(vx, 72f), isEnemy: true, 3f, 1);
    }
}
