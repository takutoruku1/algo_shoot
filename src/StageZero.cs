using Godot;

// StageZero : ステージ0「完全チュートリアル」の進行。
//   Prologue 直後・Hub 入場前に独立シーンとして 9 ステップで各操作を教える。
//   各ステップ＝「①暗転＋対象ゲージだけスポット→少年が説明（会話＝ツリー停止）→
//                ②指示帯を残して実践（停止解除）→③能動達成 or タイムアウト(FB) で次へ」の3拍。
//   練習モード（Stage0Root が GameManager.TutorialNoConsume=ON）なのでゲージ/残機/ボムは消費しない。
//   進行ロジックは StageRei の Tutorial 経路（TutTalk/TutNext/SetTutorialHint 機構）を抽出・再構成したもの。
public partial class StageZero : Node
{
    public Player Player = null!;
    public Hud Hud = null!;
    public Node2D World = null!;

    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    // ── ステップ機械 ──
    // 各「ステップ」は内部に複数フェーズ（説明会話／実践）を持つ。_phase で細かく回す。
    private int _phase;
    private bool _phaseStarted;
    private double _phaseTime;
    private double _lineHold;

    // 会話ミニプレイヤの状態（StageRei.TutTalk を移植）。
    private int _tLine;
    private bool _tTalkStarted;
    private bool _zHeld;
    private bool _zEdge;
    private bool _started;     // Hub 遷移を一度きりに
    private bool _bannerShown;

    // 達成計測用ベースライン／フラグ。
    private bool _t1Moved, _t1MovedH, _t1MovedV;
    private int _t2ShotCount;
    private double _t3FocusHeld; private bool _t3Moved;
    private int _t4DodgeBase;
    private int _t6PurifyBase;
    private double _refill;

    // スポット矩形（設計座標 1280x720）。Hud.cs の各 Draw* の実座標から確定。
    private static readonly Rect2 SpotBomb     = new Rect2(18, 16, 200, 86);   // DrawLifeBomb（LIFE/BOMB パネル）
    private static readonly Rect2 SpotPurify   = new Rect2(640 - 210, 16, 420, 38); // DrawPurify（浄化カプセル）
    private static readonly Rect2 SpotHeart    = new Rect2(1280 - 22 - 470, 60, 470, 30); // DrawScore の♥心チップ一帯
    private static readonly Rect2 SpotKindness = new Rect2(20, 130, 168 + 60, 24); // DrawKindness（やさしさゲージ＋全開ラベル）

    // 自機の練習場での定位置（穏やかな中央）。
    private const float CenterX = 192f, CenterY = 120f;

    // ════════════════════ セリフ ════════════════════
    // who: 0=少年 / 1=ミナ / 3=ナレ（Hud.LineKind）。face は char/ に実在確認済み。
    private static readonly (int who, string text, string face)[] Tut0Intro =
    {
        (3, "　　　　——どこまでも、暗い場所だった。", ""),
        (1, "ご主人様……ここ、まっくらで、なにも見えませんよ。", "res://char/mina_worried.png"),
        (0, "心配ない。ここはぼくが付きっきりで教える。", "res://char/shonen_gentle.png"),
        (0, "暗い中で“光った所”だけ見てればいい。それ以外は、見なくていいんだ。", "res://char/shonen_gentle.png"),
        (1, "……光った所だけ。それなら、わたくしにもできそうです。", "res://char/mina_face.png"),
        (0, "ああ。ひとつずつ、いっしょにやろう。", "res://char/shonen_proud.png"),
    };
    private static readonly (int who, string text, string face)[] Tut1Move =
    {
        (0, "まずは、動くこと。矢印キーで、上下左右。", "res://char/shonen_face.png"),
        (0, "斜めも入れて、ぜんぶで8方向。光の中を、好きに泳いでごらん。", "res://char/shonen_gentle.png"),
        (1, "泳ぐ、ですか。……ふわふわして、なんだか心地いいですね。", "res://char/mina_smile.png"),
        (0, "その調子だ。身体が、きみのものになってきた証拠だよ。", "res://char/shonen_proud.png"),
    };
    private static readonly (int who, string text, string face)[] Tut2Shot =
    {
        (0, "次は、撃つ。Z キーだ。押しっぱなしで、光が出る。", "res://char/shonen_face.png"),
        (0, "飛んでくる“言葉”や“板”——あれを、その光で祓うんだ。", "res://char/shonen_gentle.png"),
        (1, "撃つ、というより……払いのける感じですね。", "res://char/mina_face.png"),
        (0, "そう。倒すんじゃない。やさしく、どかすだけ。Z を、押してみて。", "res://char/shonen_proud.png"),
    };
    private static readonly (int who, string text, string face)[] Tut3Slow =
    {
        (0, "狭い隙間を抜けたい時は、Shift。押すと、ゆっくり動ける。", "res://char/shonen_face.png"),
        (0, "それと——きみの真ん中に、小さな赤い点が見えるだろ。", "res://char/shonen_gentle.png"),
        (1, "あ、ほんとうだ。これが……?", "res://char/mina_face.png"),
        (0, "それが、当たる所。そこさえ弾に触れなきゃ平気だ。Shift で、丁寧に避けてごらん。", "res://char/shonen_proud.png"),
    };
    private static readonly (int who, string text, string face)[] Tut4Dash =
    {
        (0, "もうひとつ、とっておき。回避だ。一瞬だけ無敵になって、弾を“抜ける”。", "res://char/shonen_face.png"),
        (0, "向きは自由。上にも、横にも、斜めにも——出したい方向へ、ぱっと。", "res://char/shonen_gentle.png"),
        (1, "弾の中を、すり抜けてしまう……? ずいぶん、思い切った技ですね。", "res://char/mina_worried.png"),
        (0, "怖がらなくていい。当たる前に抜けりゃ、ノーダメージさ。", "res://char/shonen_gentle.png"),
        (0, "いろんな方向に、何度でも試してみて。身体で覚えるのが、いちばん早い。", "res://char/shonen_proud.png"),
    };
    private static readonly (int who, string text, string face)[] Tut5Bomb =
    {
        (0, "それでも、囲まれて逃げ場がない時がある。そんな時は——X。", "res://char/shonen_face.png"),
        (0, "画面じゅうの弾を、まとめて吹き飛ばす。その間、きみは無敵だ。", "res://char/shonen_gentle.png"),
        (1, "そんな大技、いくらでも使えるんですか?", "res://char/mina_face.png"),
        (0, "いや。右の“ボム残り”を、ひとつ食う。ここぞ、って時のための切り札さ。", "res://char/shonen_gentle.png"),
        (0, "ほら、ダミーの弾だ。怖がらず、X。", "res://char/shonen_proud.png"),
    };
    private static readonly (int who, string text, string face)[] Tut6Purify =
    {
        (0, "さあ、本番に近いやつだ。あの“声”——周りの板を、全部祓ってみて。", "res://char/shonen_face.png"),
        (0, "板がぜんぶ無くなると、奥の本体に光が届く。それが、浄化だ。", "res://char/shonen_gentle.png"),
        (1, "倒すのではなく、届ける。……これが、わたくしの役目なんですね。", "res://char/mina_face.png"),
        (0, "そうだ。浄化するたび、“浄化した心”が少しずつ貯まる。", "res://char/shonen_gentle.png"),
        (0, "それを持ち帰れば、ハブのショップで、きみを強くできる。やってごらん。", "res://char/shonen_proud.png"),
    };
    private static readonly (int who, string text, string face)[] Tut7Warmth =
    {
        (0, "最後に、いちばん大事なこと。左の“やさしさゲージ”だ。", "res://char/shonen_face.png"),
        (0, "弾をかすめたり、浄化したりすると、これが少しずつ満ちていく。", "res://char/shonen_gentle.png"),
        (1, "満ちたら、なにか起きるんですか?", "res://char/mina_face.png"),
        (0, "満タンになったら——Ctrl。Space じゃない、Ctrl だ。“やさしさ全開”。", "res://char/shonen_gentle.png"),
        (0, "数秒だけ、きみから光が溢れる。連射は最速、撃つ弾は花びらになる。", "res://char/shonen_proud.png"),
        (1, "花びら……。わたくしの撃つものが、そんなにきれいになるんですね。", "res://char/mina_smile.png"),
        (0, "ああ。満タンになったら、Ctrl。ダミーに、思いきり撃ち込んでみて。", "res://char/shonen_proud.png"),
    };
    private static readonly (int who, string text, string face)[] Tut8End =
    {
        (0, "——これで、全部だ。動いて、撃って、避けて、抜けて、囲まれたら祓う。", "res://char/shonen_gentle.png"),
        (1, "ぜんぶ、覚えました。……ご主人様が、そばで教えてくれましたから。", "res://char/mina_smile.png"),
        (0, "ぼくは、ずっとここにいる。きみが迷っても、ちゃんと声が届く所に。", "res://char/shonen_gentle.png"),
        (3, "　　　　暗闇に、ひとつだけ、行く先の光が灯った。", ""),
        (0, "行こう、ミナ。——きみは、ひとりじゃない。ぼくが、ここにいる（Stay）。", "res://char/shonen_proud.png"),
    };

    public override void _Ready()
    {
        _rng.Randomize();
        // 浄化カプセルが進まないよう目標は大きめに（チュートリアルでクリア扱いにしない）。
        GetNodeOrNull<GameManager>("/root/Game")?.SetStageTarget(99);
    }

    public override void _Process(double delta)
    {
        _phaseTime += delta;
        _lineHold += delta;
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        _zEdge = z && !_zHeld;
        _zHeld = z;
        if (!_bannerShown) { _bannerShown = true; Hud.ShowBanner("れんしゅう"); }

        // 全開（やさしさ）は監視して、起きたらトースト等の既存演出に任せる（HUD が自動で出す）。
        Drive(delta);
    }

    // 各フェーズ。説明会話＝Hud(止まる)＋スポットON／実践＝指示帯(止めない)＋スポット弱め。
    private void Drive(double delta)
    {
        switch (_phase)
        {
            // ── 0 導入（会話のみ・全画面うっすら暗転） ──
            case 0:
                if (!_phaseStarted) { _phaseStarted = true; Hud.SetSpot(new Rect2(), 0.4f); }
                if (TutTalk(Tut0Intro)) { Hud.ClearSpot(); NextPhase(); }
                break;

            // ── 1 移動（自機系：スポットは使わず自機を発光させる） ──
            case 1: // 説明
                if (TutTalk(Tut1Move)) NextPhase();
                break;
            case 2: // 実践（上下＋左右の入力 or 6s）
                if (!_phaseStarted) { _phaseStarted = true; _t1MovedH = _t1MovedV = _t1Moved = false; Hud.ClearSpot(); }
                Hud.SetTutorialHint("↑↓←→ で、8方向に動いてみよう");
                Player?.TutorialGlow();
                {
                    Vector2 v = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
                    if (Mathf.Abs(v.X) > 0.2f || Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.D)) _t1MovedH = true;
                    if (Mathf.Abs(v.Y) > 0.2f || Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.S)) _t1MovedV = true;
                }
                if (_t1MovedH && _t1MovedV) _t1Moved = true;
                if (_t1Moved || _phaseTime > 6.0) { Hud.ClearTutorialHint(); NextPhase(); }
                break;

            // ── 2 ショット（Z） ──
            case 3:
                if (TutTalk(Tut2Shot)) NextPhase();
                break;
            case 4: // 自弾7発相当 or 6s
                if (!_phaseStarted) { _phaseStarted = true; _t2ShotCount = 0; Hud.ClearSpot(); }
                Hud.SetTutorialHint("Z で撃ってみよう");
                Player?.TutorialGlow();
                if (CountPlayerBullets() >= 1) _t2ShotCount++;
                if (_t2ShotCount >= 7 || _phaseTime > 6.0)
                {
                    Hud.ClearTutorialHint();
                    GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                    NextPhase();
                }
                break;

            // ── 3 低速（Shift） ──
            case 5:
                if (TutTalk(Tut3Slow)) NextPhase();
                break;
            case 6: // Shift保持0.5s＋移動 or 5s
                if (!_phaseStarted) { _phaseStarted = true; _t3FocusHeld = 0; _t3Moved = false; Hud.ClearSpot(); }
                Hud.SetTutorialHint("Shift を押しながら動こう");
                Player?.TutorialGlow();
                {
                    bool focus = Input.IsKeyPressed(Key.Shift) || Pad.Pressed(JoyButton.LeftShoulder) || Pad.Pressed(JoyButton.RightShoulder);
                    if (focus) _t3FocusHeld += delta;
                    if (focus && MovePressed()) _t3Moved = true;
                    if ((_t3FocusHeld >= 0.5 && _t3Moved) || _phaseTime > 5.0) { Hud.ClearTutorialHint(); NextPhase(); }
                }
                break;

            // ── 4 回避（各方向） ──
            case 7:
                if (TutTalk(Tut4Dash)) NextPhase();
                break;
            case 8: // DodgeCount が3増える or 10s（ゆっくりダミー弾を流す）
                if (!_phaseStarted)
                {
                    _phaseStarted = true;
                    _t4DodgeBase = Player?.DodgeCount ?? 0;
                    _refill = 0;
                    Hud.ClearSpot();
                    SpawnSlowBullets();
                }
                Hud.SetTutorialHint("いろんな方向に回避してみよう");
                Player?.TutorialGlow();
                _refill += delta;
                if (_refill > 1.6 && CountEnemyBullets() < 4) { _refill = 0; SpawnSlowBullets(); }
                if ((Player?.DodgeCount ?? 0) - _t4DodgeBase >= 3 || _phaseTime > 10.0)
                {
                    Hud.ClearTutorialHint();
                    GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                    NextPhase();
                }
                break;

            // ── 5 ボム（X）：BOMB残数チップをスポット ──
            case 9:
                if (!_phaseStarted) { _phaseStarted = true; Hud.SetSpot(SpotBomb, 0.5f); }
                if (TutTalk(Tut5Bomb)) NextPhase();
                break;
            case 10: // ダミー弾を濃いめに撒く→ボム発動検出 or 6s
                if (!_phaseStarted)
                {
                    _phaseStarted = true;
                    _t5BombBase = Player?.BombCount ?? 0;
                    Hud.SetSpot(SpotBomb, 0.25f);
                    SpawnDenseBullets();
                }
                Hud.SetTutorialHint("ダミーの弾が来た。X で一掃しよう");
                if ((Player?.BombCount ?? 0) - _t5BombBase >= 1 || _phaseTime > 6.0)
                {
                    Hud.ClearTutorialHint();
                    Hud.ClearSpot();
                    GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                    NextPhase();
                }
                break;

            // ── 6 浄化＋通貨：浄化カプセル＋♥心チップをスポット ──
            case 11:
                if (!_phaseStarted)
                {
                    _phaseStarted = true;
                    // 2 つの要素を見せたいので、まずは浄化カプセルにスポット（会話は1枠なので片方を主役に）。
                    Hud.SetSpot(SpotPurify, 0.5f);
                }
                if (TutTalk(Tut6Purify))
                {
                    Hud.ClearSpot();
                    NextPhase();
                }
                break;
            case 12: // GlyphMote 1体→PurifiedCount+1（FBなし＝未浄化で全滅したら湧き直し）
                if (!_phaseStarted)
                {
                    _phaseStarted = true;
                    _t6PurifyBase = GetNodeOrNull<GameManager>("/root/Game")?.PurifiedCount ?? 0;
                    Hud.SetSpot(SpotHeart, 0.25f); // 浄化で増える♥心チップをそっと示す
                    SpawnDummy(false);
                }
                Hud.SetTutorialHint("ダミーの敵を浄化してみよう");
                {
                    var game = GetNodeOrNull<GameManager>("/root/Game");
                    bool purified = (game?.PurifiedCount ?? 0) - _t6PurifyBase >= 1;
                    if (!purified && GetTree().GetNodesInGroup("enemies").Count == 0)
                        SpawnDummy(false); // 逃げて全滅したら湧き直し（詰み防止・FBなし設計）
                    if (purified)
                    {
                        Hud.ClearTutorialHint();
                        Hud.ClearSpot();
                        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                        NextPhase();
                    }
                }
                break;

            // ── 7 やさしさ全開（Ctrl）：やさしさゲージをスポット＋満タン化 ──
            case 13:
                if (!_phaseStarted)
                {
                    _phaseStarted = true;
                    GetNodeOrNull<GameManager>("/root/Game")?.FillKindnessForTutorial(); // 満タンに
                    Hud.SetSpot(SpotKindness, 0.5f);
                }
                if (TutTalk(Tut7Warmth))
                {
                    Hud.ClearSpot();
                    NextPhase();
                }
                break;
            case 14: // 無害ダミーを出す→JustOverloaded 検出 or FB(12s)
                if (!_phaseStarted)
                {
                    _phaseStarted = true;
                    var g = GetNodeOrNull<GameManager>("/root/Game");
                    if (!(g?.IsOverload ?? false) && !(g?.KindnessReady ?? false)) g?.FillKindnessForTutorial(); // 念押しで満タン保証
                    Hud.SetSpot(SpotKindness, 0.25f);
                    SpawnDummy(true); // 無害な撃ち込み台
                }
                Hud.SetTutorialHint("やさしさ全開（Ctrl）で、ダミーの敵に撃ち込もう");
                {
                    var game = GetNodeOrNull<GameManager>("/root/Game");
                    if ((game?.JustOverloaded ?? false) || _phaseTime > 12.0)
                    {
                        Hud.ClearTutorialHint();
                        Hud.ClearSpot();
                        foreach (Node n in GetTree().GetNodesInGroup("enemies")) if (n is Enemy e) e.QueueFree();
                        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                        NextPhase();
                    }
                }
                break;

            // ── 8 締め（会話のみ）→ MarkTutorialSeen → Hub ──
            case 15:
                if (!_phaseStarted) { _phaseStarted = true; Hud.SetSpot(new Rect2(), 0.35f); }
                if (TutTalk(Tut8End)) { Hud.ClearSpot(); ToHub(); }
                break;
        }
    }

    private void NextPhase()
    {
        _phase++;
        _phaseStarted = false;
        _phaseTime = 0;
        Hud.ClearTutorialHint();
    }

    private void ToHub()
    {
        if (_started) return;
        _started = true;
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (game != null) game.TutorialNoConsume = false; // 練習モード解除（Stage0Root._ExitTree でも保険的に解除）
        game?.MarkTutorialSeen();
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetTree().ChangeSceneToFile("res://Hub.tscn");
    }

    // ════════════════════ 会話ミニプレイヤ（StageRei.TutTalk を移植） ════════════════════
    private bool TutTalk((int who, string text, string face)[] lines)
    {
        if (!_tTalkStarted)
        {
            _tTalkStarted = true;
            _tLine = 0;
            _lineHold = 0;
            Hud.HoldBubble = true;
            Hud.ClearTutorialHint();
            TutShowLine(lines);
        }
        if (_zEdge && _lineHold >= 0.15 && !Hud.DialogRevealed)
        {
            Hud.RevealDialogNow();
            _lineHold = 0;
        }
        else if (_lineHold >= 0.15 && Hud.DialogRevealed
                 && (_zEdge || (Hud.AutoAdvance && _lineHold >= 1.4)))
        {
            _lineHold = 0;
            _tLine++;
            if (_tLine >= lines.Length)
            {
                Hud.HoldBubble = false;
                Hud.HideBubble();
                _tTalkStarted = false;
                return true;
            }
            TutShowLine(lines);
        }
        return false;
    }

    private void TutShowLine((int who, string text, string face)[] lines)
    {
        var (who, text, face) = lines[_tLine];
        Hud.ShowDialog((Hud.LineKind)who, text, string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face);
    }

    private bool MovePressed() =>
        Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down").Length() > 0.2f
        || Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.D);

    // ════════════════════ ダミー弾・ダミー敵 ════════════════════
    // 自弾の本数（ショット練習の達成計測用）。
    private int CountPlayerBullets()
    {
        int n = 0;
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return 0;
        foreach (Node c in pool.GetChildren())
            if (c is Bullet b && b.Active && !b.IsEnemy) n++;
        return n;
    }
    private int CountEnemyBullets() => GetTree().GetNodesInGroup("enemy_bullets").Count;
    private int _t5BombBase;

    // 回避練習：ゆっくり横切るダミー弾を少数（避けやすい／回避で抜けやすい）。
    private void SpawnSlowBullets()
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        float px = Player?.GlobalPosition.X ?? CenterX;
        for (int i = 0; i < 4; i++)
        {
            float y = 40f + i * 36f;
            pool.Spawn(new Vector2(Mathf.Min(370f, px + 90f + i * 12f), y), new Vector2(-30f, 0f), isEnemy: true, 3f, 1);
        }
    }

    // ボム練習：少し濃いめ。囲まれ感を出してから X を促す。
    private void SpawnDenseBullets()
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        for (int i = 0; i < 12; i++)
        {
            float x = _rng.RandfRange(40f, 360f);
            float y = _rng.RandfRange(-40f, -4f);
            pool.Spawn(new Vector2(x, y), new Vector2(_rng.RandfRange(-16f, 16f), 50f), isEnemy: true, 3f, 1);
        }
    }

    // ダミー敵（GlyphMote）。harmless=true で弾を撃たない撃ち込み台（やさしさ全開の練習用）。
    private void SpawnDummy(bool harmless)
    {
        var e = new GlyphMote { Harmless = harmless };
        World.AddChild(e);
        e.GlobalPosition = new Vector2(360f, 108f);
    }
}
