using Godot;

// StageAkari : STAGE1「あかり（雨の教室）」進行。
//   1: 導入会話（少年＝声/テロップ、ミナ＝立ち絵で毒舌）
//   2: あかりボス出現
//   3: ボス戦（自責の弾雨＋あかりの自責弾。浄化＝改心で会話完了まで）
//   4: クリア（灯がともる）
// ボス戦中は天井の自責の雨が降り続ける（会話中は止む）。
public partial class StageAkari : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private int _step;
    private bool _stepStarted;
    private double _stepTime;
    private double _lineHold;   // 行表示からの経過（誤連打防止の最小表示時間用）
    private int _introLine;
    private bool _zHeld;
    private bool _zEdge;
    private BossAkari _boss = null!;
    private bool _bossActive;
    private double _rainT;
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    private const float SpawnX = 300f;

    // 導入会話（who: 0=少年テロップ / 1=ミナ立ち絵）
    private static readonly (int who, string text)[] Intro =
    {
        (0, "Xで、ひとり溺れてる子がいる。自分を責めて、責めて……心が穢れちまった。"),
        (1, "それで、わざわざ他人の心の中まで潜るんですか。ご主人様、物好きですね。"),
        (0, "穢れを祓えば、その子は少し楽になれる。ここは、その子の心象世界だ。"),
        (1, "雨の、教室……。ずいぶん、降っていますね。"),
        (0, "……ああ。降りやまない自責の雨だ。いくよ、ミナ。"),
    };

    // ボス登場時の説明（who: 0=少年 / 1=ミナ）
    private static readonly (int who, string text)[] BossIntro =
    {
        (0, "来た。あれが、この子の“ゆるせないわたし”——自責が形になった穢れだ。"),
        (0, "本人を撃つんじゃない。あの子を縛ってる“自責”を剥がして、奥の光に届かせろ。"),
        (1, "……はいはい。あの子を傷つけずに、穢れだけ、ですね。やってみましょう。"),
    };

    public override void _Ready()
    {
        _rng.Randomize();
        _step = 1;
        GetNodeOrNull<GameManager>("/root/Game")?.SetStageTarget(1); // ボスを浄化＝100%（部屋が晴れる）
    }

    public override void _Process(double delta)
    {
        _stepTime += delta;
        _lineHold += delta;
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Input.IsActionPressed("ui_accept");
        _zEdge = z && !_zHeld;
        _zHeld = z;
        switch (_step)
        {
            case 1: Step_Lines(delta, Intro); break;
            case 2: Step_BossSpawn(); break;
            case 3: Step_Lines(delta, BossIntro); break; // ボスは出現済みだが会話中は止まる
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

    // ---- 会話ステップ（配列を順に流す。Zで手動送り。会話中は弾が止まる） ----
    private void Step_Lines(double delta, (int who, string text)[] lines)
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _introLine = 0;
            _lineHold = 0;
            Hud.HoldBubble = true; // 自動で消えない＝手動送り
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

    private void ShowLine((int who, string text)[] lines)
    {
        var (who, text) = lines[_introLine];
        if (who == 0) Hud.ShowDialog(text, "res://char/shonen_face.png"); // 少年
        else Hud.ShowDialog(text, "res://char/mina_face.png");            // ミナ
    }

    // ---- 2: ボス出現 ----
    private void Step_BossSpawn()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            _boss = new BossAkari { Name = "BossAkari" };
            World.AddChild(_boss);
            _boss.GlobalPosition = new Vector2(SpawnX, 70f);
            _bossActive = true;
            Advance(); // 出現と同時に説明会話へ（会話中はボス停止・雨も止む）
        }
    }

    // ---- 3: ボス戦（浄化＆会話完了まで） ----
    private void Step_BossWait()
    {
        if (!IsInstanceValid(_boss) || _boss.Finished)
        {
            _bossActive = false;
            Advance();
        }
    }

    // ---- 4: クリア ----
    private void Step_Clear()
    {
        if (!_stepStarted)
        {
            _stepStarted = true;
            Hud.ShowBanner("灯が、ともった。");
            Hud.ShowDialog("……行きましょう、ご主人様。次の人のところへ。", "res://char/mina_face.png");
            Advance();
        }
    }

    // 天井から降る「自責の雨」（会話中は止む）。
    private void Rain(double delta)
    {
        if (Hud.BubblePaused) return;
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        _rainT += delta;
        if (_rainT < 0.16) return;
        _rainT = 0;
        float x = _rng.RandfRange(8f, 376f);
        float vx = _rng.RandfRange(-10f, 10f);
        pool.Spawn(new Vector2(x, -6f), new Vector2(vx, 74f), isEnemy: true, 3f, 1);
    }
}
