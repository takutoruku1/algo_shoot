using Godot;

// StageZero : ステージ0「完全チュートリアル」の進行。
//   Prologue 直後・Hub 入場前に独立シーンとして 9 ステップで各操作を教える。
//   各ステップ＝「①暗転＋対象ゲージだけスポット→少年が説明（会話＝ツリー停止）→
//                ②指示帯を残して実践（停止解除）→③その技を“やり遂げる”まで進まない」の3拍。
//   実践は押した瞬間/短時間では進まず、撃破数・回避回数・ボム巻き込み数・全開での撃破など
//   「教えた操作を実際に使った結果」を達成条件にする。標的が尽きたら自動で湧き直し、
//   進行不能を避けるため保険タイムアウト(SafetyTimeout=60s)だけ長めに置く。進捗は指示帯に n/N で出す。
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
    private double _t1Up, _t1Down, _t1Left, _t1Right;
    private int _t2KillBase;                              // ショット：ダミー撃破の起点（PurifiedCount 増分で判定）
    private double _t3FocusHeld; private bool _t3Moved;   // 低速：Shift 保持秒／低速中の移動
    private int _t4DodgeBase;                             // 回避：DodgeCount の起点
    private int _t6PurifyBase;                            // 浄化：PurifiedCount の起点
    private int _t7OverloadKillBase; private bool _t7Activated; // 全開：発動済みフラグと、発動後の撃破起点
    private double _t7ActivatedT;                               // 全開：発動した時刻（_phaseTime 基準）。フォールバック判定用
    private double _refill;

    // 各ステップの達成目標。
    private const int ShotKillNeed   = 3;   // ショットでダミーを倒す数
    private const int DodgeNeed      = 3;   // 回避を成功させる回数
    private const int BombKillNeed   = 3;   // ボムでまとめて巻き込んで倒す数
    private const int PurifyNeed     = 2;   // 浄化する数
    private const double SlowHoldNeed = 1.0; // 低速で動き続ける最低秒

    // 進行不能回避のための保険タイムアウト（十分長く＝通常プレイで勝手に進まない）。
    private const double SafetyTimeout = 60.0;

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
        (1, "……ここは。どこまでも、暗い。", "res://char/mina_worried.png"),
        (1, "ご主人様……まっくらで、なにも見えませんよ。", "res://char/mina_worried.png"),
        (0, "心配ない。ここはぼくが付きっきりで教える。", "res://char/shonen_gentle.png"),
        (0, "暗い中で“光った所”だけ見てればいい。それ以外は、見なくていいんだ。", "res://char/shonen_gentle.png"),
        (1, "……光った所だけ。それなら、わたくしにもできそうです。", "res://char/mina_face.png"),
        (0, "ああ。ひとつずつ、いっしょにやろう。", "res://char/shonen_proud.png"),
    };
    private static readonly (int who, string text, string face)[] Tut1Move =
    {
        (0, "まずは、動くこと。下のボタンで、上下左右。", "res://char/shonen_face.png"),
        (0, "斜めも入れて、ぜんぶで8方向。光の中を、好きに泳いでごらん。", "res://char/shonen_gentle.png"),
        (1, "泳ぐ、ですか。……ふわふわして、なんだか心地いいですね。", "res://char/mina_smile.png"),
        (0, "その調子だ。身体が、きみのものになってきた証拠だよ。", "res://char/shonen_proud.png"),
    };
    private static readonly (int who, string text, string face)[] Tut2Shot =
    {
        (0, "次は、撃つ。下のボタンを押しっぱなしで、光が出る。", "res://char/shonen_face.png"),
        (0, "飛んでくる“言葉”や“板”——あれを、その光で祓うんだ。", "res://char/shonen_gentle.png"),
        (1, "撃つ、というより……払いのける感じですね。", "res://char/mina_face.png"),
        (0, "そう。倒すんじゃない。やさしく、どかすだけ。さあ、撃ってみて。", "res://char/shonen_proud.png"),
    };
    private static readonly (int who, string text, string face)[] Tut3Slow =
    {
        (0, "狭い隙間を抜けたい時は、低速。下のボタンを押すと、ゆっくり動ける。", "res://char/shonen_face.png"),
        (0, "それと——きみの真ん中に、小さな赤い点が見えるだろ。", "res://char/shonen_gentle.png"),
        (1, "あ、ほんとうだ。これが……?", "res://char/mina_face.png"),
        (0, "それが、当たる所。そこさえ弾に触れなきゃ平気だ。低速で、丁寧に避けてごらん。", "res://char/shonen_proud.png"),
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
        (0, "それでも、囲まれて逃げ場がない時がある。そんな時は——ボムだ。", "res://char/shonen_face.png"),
        (0, "画面じゅうの弾を、まとめて吹き飛ばす。その間、きみは無敵だ。", "res://char/shonen_gentle.png"),
        (1, "そんな大技、いくらでも使えるんですか?", "res://char/mina_face.png"),
        (0, "いや。右の“ボム残り”を、ひとつ食う。ここぞ、って時のための切り札さ。", "res://char/shonen_gentle.png"),
        (0, "ほら、ダミーの弾だ。怖がらず、下のボタンでボムを。", "res://char/shonen_proud.png"),
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
        (1, "……あ。ご主人様、見てください。暗闇に、ひとつだけ。行く先の光が、灯りました。", "res://char/mina_smile.png"),
        (0, "行こう、ミナ。——Stay。きみは、ひとりじゃない。ぼくも、ここにいるから。", "res://char/shonen_proud.png"), // 合言葉を注釈なしで＝意味は本編Prologueで定義済み。「ぼくも」＝Acrostic“And I won't either.”の先触れ
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
        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P1）。
        bool z = Pad.AdvanceHeld();
        _zEdge = z && !_zHeld;
        _zHeld = z;
        if (!_bannerShown) { _bannerShown = true; Hud.ShowBanner("れんしゅう"); }

        // 全開（やさしさ）は監視して、起きたらトースト等の既存演出に任せる（HUD が自動で出す）。
        Drive(delta);
    }

    // 各ステップ（説明会話フェーズ＆実践フェーズ）で、その操作に割り当たった“全ボタン”を
    // 指示帯の上にバッジで出すための操作名。Player.cs の入力判定と一致させる。
    //   move=移動 / shot=撃つ（浄化も板を撃って祓う）/ focus=低速 / dodge=回避 / bomb=ボム / kind=やさしさ全開。
    //   導入(0)・締め(15) は操作なし＝空。会話／実践のどちらのフェーズでも同じ操作名を出す。
    private static string OpForPhase(int phase) => phase switch
    {
        1 or 2   => "move",
        3 or 4   => "shot",
        5 or 6   => "focus",
        7 or 8   => "dodge",
        9 or 10  => "bomb",
        11 or 12 => "shot",   // 浄化＝ショットで板を祓う
        13 or 14 => "kind",
        _        => "",       // 0=導入 / 15=締め は操作ボタンを出さない
    };

    // 各フェーズ。説明会話＝Hud(止まる)＋スポットON／実践＝指示帯(止めない)＋スポット弱め。
    private void Drive(double delta)
    {
        // 今のステップに対応する“全ボタン”バッジを毎フレーム指定（Hud が All* に展開して描く）。
        Hud.SetTutorialOp(OpForPhase(_phase));

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
            case 2: // 実践（上下左右をそれぞれ約1秒ずつ押す＝全方向そろうまで進まない）
                if (!_phaseStarted) { _phaseStarted = true; _t1Up = _t1Down = _t1Left = _t1Right = 0; Hud.ClearSpot(); }
                Player?.TutorialGlow();
                {
                    const double need = 1.0; // 各方向これだけ押し続けたらカウント
                    Vector2 v = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
                    if (v.X < -0.2f || Input.IsKeyPressed(Key.A)) _t1Left  += delta;
                    if (v.X >  0.2f || Input.IsKeyPressed(Key.D)) _t1Right += delta;
                    if (v.Y < -0.2f || Input.IsKeyPressed(Key.W)) _t1Up    += delta;
                    if (v.Y >  0.2f || Input.IsKeyPressed(Key.S)) _t1Down  += delta;
                    int done = (_t1Up >= need ? 1 : 0) + (_t1Down >= need ? 1 : 0)
                             + (_t1Left >= need ? 1 : 0) + (_t1Right >= need ? 1 : 0);
                    Hud.SetTutorialHint($"上下左右を それぞれ1秒くらい 押してみよう（{done}/4）");
                    if (done >= 4 || _phaseTime > 30.0) { Hud.ClearTutorialHint(); NextPhase(); }
                }
                break;

            // ── 2 ショット（Z） ──
            case 3:
                if (TutTalk(Tut2Shot)) NextPhase();
                break;
            case 4: // ダミーを Z で3体撃破（倒すたび湧き直し）→達成で次へ
                if (!_phaseStarted)
                {
                    _phaseStarted = true;
                    _t2KillBase = GetNodeOrNull<GameManager>("/root/Game")?.PurifiedCount ?? 0;
                    Hud.ClearSpot();
                    SpawnDummy(true); // 弾を撃たない無害ダミー（撃って祓う標的）
                }
                Player?.TutorialGlow();
                {
                    int killed = (GetNodeOrNull<GameManager>("/root/Game")?.PurifiedCount ?? 0) - _t2KillBase;
                    // 倒し切る前に標的が尽きたら湧き直し（詰み防止）。湧き直しは1体ずつで「撃って→消える」を反復。
                    if (killed < ShotKillNeed && CountLiveEnemies() == 0) SpawnDummy(true);
                    Hud.SetTutorialHint($"Z で ダミーを たおそう（{Mathf.Min(killed, ShotKillNeed)}/{ShotKillNeed}）");
                    if (killed >= ShotKillNeed || _phaseTime > SafetyTimeout)
                    {
                        Hud.ClearTutorialHint();
                        ClearDummies();
                        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                        NextPhase();
                    }
                }
                break;

            // ── 3 低速（Shift） ──
            case 5:
                if (TutTalk(Tut3Slow)) NextPhase();
                break;
            case 6: // ゆっくり弾を流す中、低速(Shift)を保ったまま動き続ける体験（1秒以上＋移動）→達成で次へ
                if (!_phaseStarted)
                {
                    _phaseStarted = true; _t3FocusHeld = 0; _t3Moved = false; _refill = 0;
                    Hud.ClearSpot();
                    SpawnSlowBullets(); // 精密回避の的になるゆっくり弾
                }
                Player?.TutorialGlow();
                _refill += delta;
                if (_refill > 1.6 && CountEnemyBullets() < 4) { _refill = 0; SpawnSlowBullets(); } // 隙間を絶やさない
                {
                    bool focus = Input.IsKeyPressed(Key.Shift) || Pad.Pressed(JoyButton.LeftShoulder); // RB は向き反転へ移した（Player.cs と一致）
                    // 低速を保ったまま動いている間だけ加算（離す/止まると進捗は溜まらない＝低速の意味を体験）。
                    if (focus && MovePressed()) { _t3FocusHeld += delta; _t3Moved = true; }
                    int pct = Mathf.Clamp((int)(_t3FocusHeld / SlowHoldNeed * 100), 0, 100);
                    Hud.SetTutorialHint($"Shift で低速のまま 弾の隙間を ぬけよう（{pct}%）");
                    if ((_t3FocusHeld >= SlowHoldNeed && _t3Moved) || _phaseTime > SafetyTimeout)
                    {
                        Hud.ClearTutorialHint();
                        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                        NextPhase();
                    }
                }
                break;

            // ── 4 回避（各方向） ──
            case 7:
                if (TutTalk(Tut4Dash)) NextPhase();
                break;
            case 8: // 回避を3回成功（DodgeCount+3）→達成で次へ。弾を絶やさず「弾の近くで抜ける」感を出す。
                if (!_phaseStarted)
                {
                    _phaseStarted = true;
                    _t4DodgeBase = Player?.DodgeCount ?? 0;
                    _refill = 0;
                    Hud.ClearSpot();
                    SpawnSlowBullets();
                }
                Player?.TutorialGlow();
                _refill += delta;
                if (_refill > 1.6 && CountEnemyBullets() < 4) { _refill = 0; SpawnSlowBullets(); }
                {
                    int dodged = (Player?.DodgeCount ?? 0) - _t4DodgeBase;
                    Hud.SetTutorialHint($"いろんな方向に 回避してみよう（{Mathf.Min(dodged, DodgeNeed)}/{DodgeNeed}）");
                    if (dodged >= DodgeNeed || _phaseTime > SafetyTimeout)
                    {
                        Hud.ClearTutorialHint();
                        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                        NextPhase();
                    }
                }
                break;

            // ── 5 ボム（X）：BOMB残数チップをスポット ──
            case 9:
                if (!_phaseStarted) { _phaseStarted = true; Hud.SetSpot(SpotBomb, 0.5f); }
                if (TutTalk(Tut5Bomb)) NextPhase();
                break;
            case 10: // ダミー敵をボム範囲に固めて出す→ X でまとめて3体巻き込んで倒す→達成で次へ
                if (!_phaseStarted)
                {
                    _phaseStarted = true;
                    _t5BombBase = Player?.BombCount ?? 0;
                    _t5PurifyBase = GetNodeOrNull<GameManager>("/root/Game")?.PurifiedCount ?? 0;
                    Hud.SetSpot(SpotBomb, 0.25f);
                    SpawnBombCluster(); // 自機の少し上に3体まとめて
                }
                {
                    var game = GetNodeOrNull<GameManager>("/root/Game");
                    int caught = (game?.PurifiedCount ?? 0) - _t5PurifyBase;
                    bool bombed = (Player?.BombCount ?? 0) - _t5BombBase >= 1;
                    // ボムを撃ったのに3体まとめられなかった＝固め直して再挑戦（押すだけでは進めない）。
                    if (bombed && caught < BombKillNeed)
                    {
                        _t5BombBase = Player?.BombCount ?? 0;
                        _t5PurifyBase = game?.PurifiedCount ?? 0;
                        ClearDummies();
                        SpawnBombCluster();
                    }
                    // 散らばって標的が尽きたら固め直し（詰み防止）。
                    else if (!bombed && CountLiveEnemies() == 0)
                        SpawnBombCluster();
                    Hud.SetTutorialHint($"ダミーを 3体まとめて X のボムで（{Mathf.Min(caught, BombKillNeed)}/{BombKillNeed}）");
                    if (caught >= BombKillNeed || _phaseTime > SafetyTimeout)
                    {
                        Hud.ClearTutorialHint();
                        Hud.ClearSpot();
                        ClearDummies();
                        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                        NextPhase();
                    }
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
            case 12: // GlyphMote をパネル剥がしで浄化（2体）→達成で次へ。未浄化で全滅したら湧き直し。
                if (!_phaseStarted)
                {
                    _phaseStarted = true;
                    _t6PurifyBase = GetNodeOrNull<GameManager>("/root/Game")?.PurifiedCount ?? 0;
                    Hud.SetSpot(SpotHeart, 0.25f); // 浄化で増える♥心チップをそっと示す
                    SpawnDummy(false);
                }
                {
                    var game = GetNodeOrNull<GameManager>("/root/Game");
                    int purified = (game?.PurifiedCount ?? 0) - _t6PurifyBase;
                    if (purified < PurifyNeed && CountLiveEnemies() == 0)
                        SpawnDummy(false); // 逃げて全滅したら湧き直し（詰み防止）
                    Hud.SetTutorialHint($"ダミーの敵を 浄化してみよう（{Mathf.Min(purified, PurifyNeed)}/{PurifyNeed}）");
                    if (purified >= PurifyNeed || _phaseTime > SafetyTimeout)
                    {
                        Hud.ClearTutorialHint();
                        Hud.ClearSpot();
                        ClearDummies();
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
            case 14: // 満タン→Ctrlで全開発動→（できれば全開中にダミーを撃ち込む）→達成で次へ
                if (!_phaseStarted)
                {
                    _phaseStarted = true;
                    _t7Activated = false;
                    _t7ActivatedT = 0;
                    var g0 = GetNodeOrNull<GameManager>("/root/Game");
                    g0?.FillKindnessForTutorial();           // 確実に満タンから始める
                    Hud.SetSpot(SpotKindness, 0.30f);
                    SpawnDummy(true);                        // 無害な撃ち込み台
                }
                Player?.TutorialGlow();
                {
                    var game = GetNodeOrNull<GameManager>("/root/Game");
                    bool overloadNow = game?.IsOverload ?? false;

                    // 発動の検出は1フレームの JustOverloaded を取りこぼしても拾えるよう、
                    // 「JustOverloaded か、現在 IsOverload か」のどちらかで一度でも立てたら保持する。
                    if (!_t7Activated && ((game?.JustOverloaded ?? false) || overloadNow))
                    {
                        _t7Activated = true;
                        _t7ActivatedT = _phaseTime;          // 発動からの経過を測る
                        _t7OverloadKillBase = game?.PurifiedCount ?? 0;
                    }

                    if (!_t7Activated)
                    {
                        // 発動前：毎フレーム満タンを維持。発動で消費されても次フレームで満タンに戻すので
                        // 「消費で即未満→発動扱いにならない」事故が起きない（発動自体は上で検出済み）。
                        if (!overloadNow && !(game?.KindnessReady ?? false)) game?.FillKindnessForTutorial();
                        Hud.SetTutorialHint("ゲージ満タン。Ctrl で やさしさ全開!");
                    }
                    else
                    {
                        // 発動後：全開中にダミーを撃ち込めたら理想だが、必須にして詰む経路は作らない。
                        // ① 全開中にダミー撃破できたら即完了。標的が尽きたら全開のうちに湧き直す。
                        // ② 全開が切れても（撃破前でも）「発動から少し体験したら」フォールバックで完了。
                        int killed = (game?.PurifiedCount ?? 0) - _t7OverloadKillBase;
                        if (overloadNow && killed < 1 && CountLiveEnemies() == 0) SpawnDummy(true);

                        Hud.SetTutorialHint(overloadNow
                            ? "全開のまま ダミーに撃ち込んで 倒そう!"
                            : "やさしさ全開、できました！");

                        // 完了条件（いずれか）：撃破した／全開が切れた（一度発動したら必ず体験完了に）／発動から十分経った。
                        bool done = killed >= 1
                                    || (!overloadNow)                            // 全開が自然終了＝発動を体験し切った
                                    || (_phaseTime - _t7ActivatedT > 6.0);        // 念のための時間フォールバック
                        if (done)
                        {
                            Hud.ClearTutorialHint();
                            Hud.ClearSpot();
                            ClearDummies();
                            GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
                            NextPhase();
                        }
                    }

                    // 最終保険：発動すらされないまま保険時間を超えても必ず進む（softlock 根絶）。
                    if (!_t7Activated && _phaseTime > SafetyTimeout)
                    {
                        Hud.ClearTutorialHint();
                        Hud.ClearSpot();
                        ClearDummies();
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
        Hud.ClearTutorialOp();
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
                 && (_zEdge || Hud.FastForwarding || (Hud.AutoAdvance && _lineHold >= 1.4)))  // FastForwarding=既読スキップ（Ctrl/RB長押し・既読行のみ・#22）
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
    private int CountEnemyBullets() => GetTree().GetNodesInGroup("enemy_bullets").Count;
    private int _t5BombBase, _t5PurifyBase;

    // まだ改心していない（＝倒せる）ダミー敵の数。湧き直し判定・進捗計測の補助に使う。
    private int CountLiveEnemies()
    {
        int n = 0;
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
            if (node is Enemy e && !e.IsPurified) n++;
        return n;
    }

    // ステップ間の取りこぼし防止：場に残ったダミー敵（改心済みフォロワー含む）を片付ける。
    private void ClearDummies()
    {
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
            if (node is Enemy e) e.QueueFree();
    }

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

    // ボム練習：ダミー敵を3体まとめて出す（ボムは全画面浄化なので3体居れば1発で巻き込める）。
    private void SpawnBombCluster()
    {
        for (int i = 0; i < BombKillNeed; i++)
        {
            var e = new GlyphMote { Harmless = true }; // 弾を撃たない＝練習中に痛手なし
            World.AddChild(e);
            e.GlobalPosition = new Vector2(330f + i * 18f, 80f + i * 34f);
        }
    }

    // ダミー敵（GlyphMote）。harmless=true で弾を撃たない撃ち込み台。
    private void SpawnDummy(bool harmless)
    {
        var e = new GlyphMote { Harmless = harmless };
        World.AddChild(e);
        e.GlobalPosition = new Vector2(360f, _rng.RandfRange(70f, 150f));
    }
}
