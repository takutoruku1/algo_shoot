using Godot;

// BossKoharu : STAGE3「こはる（永遠に夕食を作り続ける台所）」のボス＝穢れの核「むだなわたし」。
// もう帰らない兄を、夕食で呼び戻そうとする死の否認。家事＝祈りが砕けた無力感。
// 怒り（他責）の下にある悲しみへ光を届ける（正典 v3: 兄=少年は物語開始前に事故死。余命設定は非正典）。
// 禁止語「あなたのせいじゃない」は使わない。祈りが届いていたことを伝えて解く。
public partial class BossKoharu : Enemy
{
    public bool Finished { get; private set; }

    private readonly BossMover _mover = new BossMover();
    private const float RoamSpeed = 38f;

    private double _fireT;
    private double _fireT2;   // フィナーレ用の第2タイマー（2スペル同時撃ち）
    private bool _finale;     // HP2割以下＝2スペル同時展開
    private float _ringOff;
    private int _pattern;
    private int _beatsFired;
    private const int PatternCount = 4;

    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;

    private const string SCocky = "res://char/shonen_face.png";
    private const string SGentle = "res://char/shonen_gentle.png";
    // 絶望（兄が消える恐怖に呑まれる）行だけ、こはるの蒼白＝血色を失った立ち絵に差し替える。
    private const string KPale = "res://char/koharu_face_pale.png";

    // HPがこの割合を割るたびに攻撃パターンを変える（独白は浄化のかけあいに集約）。
    private static readonly float[] PatternThresholds = { 0.78f, 0.50f, 0.26f };

    // スペルカード（RefrainHTML Danmaku v3 STAGE3 こはる＝台所・琥珀と深紅の暖色）。
    private static readonly (string name, BulletShape shape, Color tint)[] Spells =
    {
        ("ぜんぶ食べて",         BulletShape.Orb,     new Color("e8a24a")), // 琥珀・台所の灯
        ("のこしちゃだめ",       BulletShape.Diamond, new Color("d6443f")), // 深紅・高速ダイヤ雨
        ("じっとしてて",         BulletShape.Needle,  new Color("e87a3c")), // 橙・十字バースト（動くな）
        ("あたしがちゃんとするから", BulletShape.Rice, new Color("ffa14a")), // 燃え残り・扇の粒弾
    };
    private void ApplySpell()
    {
        var s = Spells[_pattern % Spells.Length];
        SetSpellVisual(s.shape, s.tint);
        GetHud()?.SetBossBarTint(s.tint); // HPバーもスペル色へ（#26 フェーズ移行の可視化）
        GetHud()?.AnnounceSpell("こはる", "@koharu_kitchen", s.name, s.tint);
    }

    // 浄化のかけあい（who: 0=少年 / 1=ミナ / 2=こはる）。少年はミナの声で“中継”して届ける。
    // 設計書 v2 [P-03] のボス節を順序通りに（ミナの気遣い・少年の取り繕いも含む）。
    // 躁的暴走＝支配的な世話焼き。傷＝兄の死を認めない唯一の力＝完璧にすれば失わない、という呪術（死の否認）。
    // 決定打＝劇的アイロニー。兄＝すでに逝った少年の遺した声が、妹に。言い切らせず観客に繋がせ、ミナが半分気づく。
    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "ごはん、できたよ! ぜんぶ食べてね。残したら……許さないんだから。", ""),
        (2, "あ、そこ汚れてる。掃除するから、じっとしてて。動かないで。ね?", ""),
        (2, "だいじょうぶ。あたしがちゃんとするから。お兄ちゃんは、なにもしなくていいの。", ""),
        (1, "……ご主人様? お顔の色が。", ""),
        (0, "……なんでもない。続けるぞ。", SCocky),
        (2, "ちゃんと作って、ちゃんと食べさせて、ちゃんと、ちゃんとしてれば——", ""),
        (2, "……ちゃんとしてれば、お兄ちゃん、いなくならないでしょ? ねえ、いなくならないよね……?", KPale), // 絶望＝蒼白
        (5, "——怒りの下の悲しみを、ちゃんと、悲しんでいい。", ""),
        (2, "あたしのごはんじゃ、お兄ちゃんは帰ってこない……! じゃあ、なんの、意味が……!", KPale),     // 絶望＝蒼白（否認が一瞬だけ割れる）
        (5, "お兄さんが最後の日まで、あったかいままでいられたのは——きみの食卓が、あったからだ。祈りは、届いてたよ。", ""),
        (2, "……ほんとに? ……ちゃんと、食べて、くれるかな。今日は。", ""),       // 日常語＝小さな願い
        (0, "……ああ。残さず、食べるよ。", SGentle),                              // 兄が、妹に（観客だけが意味を知る）
        (0, "明日のぶんは——……いや。たくさん、作ってやってくれ。きみは、えらいよ。", SGentle), // “明日のぶんはいい”を呑み込む
        (1, "ご主人様。いまの……どなたに、おっしゃったんですか。", ""),         // ミナが半分気づく（Final/伏線へ）
        // ② こはるの否認「ちゃんとすれば、いなくならない」と、ミナ自身の否認が同型。
        //    説明せず、ミナが半秒だけ自分の否認に触れる“間”の一行（Final受容への助走）。
        (1, "……ちゃんとしてれば、いなくならない。……ええ。わたくしも、そう思っていたいです。", "res://char/mina_worried.png"),
        (0, "…………帰ろう、ミナ。", SCocky),                                      // 再仮面
    };

    protected override void OnEnemyReady()
    {
        Points = 1800;
        BodyRadius = 9f;
        PanelCount = 5;          // 「むだだよ」等の言葉（黒い吹き出し）
        PanelInk = 2;
        OrbitRadius = 26f;
        SpinSpeed = 0.85f;
        PanelsFire = false;
        EnemyBulletSpeed = 80f;

        // HPバー本数は難易度別（通常ボス：Easy2/Normal4/Hard5/Lunatic6）。総HP=BarHp×本数。
        BarCount = DiffBars(finalBoss: false);

        PreTexPath = "res://char/enemy_koharu_pre.png";   // 穢れ・病んだ核
        // 改心の三段：穢れ(pre)→泣き(cry＝黒い炎が熾火へ鎮まり大粒の涙)→笑顔(post)。
        // cry は会話の間ずっと保持し、手動送りし切った EndCryNow で post（笑顔）へ着地する。
        CryTexPath = "res://char/enemy_koharu_cry.png";
        PostTexPath = "res://char/enemy_koharu_post.png";
        BodyDisplayH = 52f;
        CryHoldDur = 9999.0;     // 自動終了させない（会話を手動送りし切ったら EndCryNow で閉じる）
    }

    public override void _Ready()
    {
        base._Ready();
        // ボス登場＝道中BGMからこはる固有テーマへクロスフェード（温かい旋律が冷えて減衰＝未完）。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBossKoharu);
        // 徘徊：画面上部のボスゾーンに収め、イージング＋ホバーで漂わせる（旧 RoamSpeed を踏襲）。
        _mover.Configure(new Vector2(200f, 70f), 90f, 28f, RoamSpeed);
        GetHud()?.ShowBossBar("とまれないわたし", "@koharu");
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        ApplySpell();

        var caster = new AreaSpellCaster();
        caster.Configure("koharu", GetParent());
        AddChild(caster);
    }

    protected override void UpdateMovement(double delta)
    {
        GlobalPosition = _mover.Step(GlobalPosition, delta);
        ApplyBossMotion(_mover.VisualOffset, _mover.Lean, _mover.FacingLeft);
        FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Koharu, GlobalPosition, (float)delta, 32f);
        FirePattern(delta);
    }

    private void FirePattern(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        if (_finale) { FireFinale(pool, delta); return; }
        _fireT += delta;
        switch (_pattern)
        {
            case 0: if (_fireT >= Di(1.0)) { _fireT = 0; Ring(pool, Dn(16), 70f); } break;
            case 1: if (_fireT >= Di(1.1)) { _fireT = 0; FanDown(pool); } break;
            case 2: if (_fireT >= Di(0.7)) { _fireT = 0; Aimed(pool); } break;
            default: if (_fireT >= Di(0.085)) { _fireT = 0; Spiral(pool); } break;
        }
    }

    // フィナーレ（HP2割以下）：「むだだよ」(琥珀の円弾リング)＋「怒り（他責）」(深紅の菱形雨)を同時展開。
    private void FireFinale(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        if (_fireT >= Di(0.95)) { _fireT = 0; SetSpellVisual(Spells[0].shape, Spells[0].tint); Ring(pool, Dn(16), 70f); }
        if (_fireT2 >= Di(0.85)) { _fireT2 = 0; SetSpellVisual(Spells[1].shape, Spells[1].tint); FanDown(pool); }
    }

    private void Ring(BulletPool pool, int k, float spd)
    {
        _ringOff += Mathf.DegToRad(8f);
        for (int i = 0; i < k; i++)
        {
            float a = _ringOff + Mathf.Tau * i / k;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * spd, 3.4f);
        }
    }

    // 「祈り弾」ギミック（#12 機構側／#20）：下方向の扇＝食卓に落ちる祈りは、自機弾で“受け止め”られる。
    // 消すと双方消滅＋やさしさ微加算（GameManager.AddPrayerCleared）。自機・フォロワーの弾列が受け皿になる。
    // FanDown はスペル「のこしちゃだめ」(pattern1)とフィナーレでしか撃たない＝スペル限定が自然に成立。
    private void FanDown(BulletPool pool)
    {
        int k = Dn(9);
        for (int i = 0; i < k; i++)
        {
            float t = (float)i / (k - 1) - 0.5f;
            float a = Mathf.Pi / 2f + t * Mathf.DegToRad(78f);
            var b = FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * EnemyBulletSpeed, 3.4f);
            b.MakeErasable();
        }
    }

    private void Aimed(BulletPool pool)
    {
        Vector2 d = AimAtPlayer();
        float baseA = Mathf.Atan2(d.Y, d.X);
        for (int i = -1; i <= 1; i++)
        {
            float a = baseA + i * Mathf.DegToRad(13f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 96f, 3.4f);
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(12f);
        for (int s = 0; s < 2; s++)
        {
            float a = _ringOff + Mathf.Pi * s;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 88f, 3.2f);
        }
    }

    private Vector2 AimAtPlayer()
    {
        var players = GetTree().GetNodesInGroup("player");
        if (players.Count > 0 && players[0] is Node2D pl)
        {
            var d = pl.GlobalPosition - GlobalPosition;
            if (d.LengthSquared() > 0.01f) return d.Normalized();
        }
        return new Vector2(-1, 0);
    }

    protected override void OnHpChanged()
    {
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        if (_beatsFired < PatternThresholds.Length && HpRatio <= PatternThresholds[_beatsFired])
        {
            _pattern = (_pattern + 1) % PatternCount;
            _beatsFired++;
            ApplySpell();
        }
        // フィナーレ発火＝最後のバーの残り50%（finaleRatio = 0.5 / バー本数）。
        if (!_finale && HpRatio <= 0.5f / Mathf.Max(1, TotalBars))
        {
            _finale = true;
            GetHud()?.SetBossBarTint(Spells[0].tint); // フィナーレ色（#26）
            GetHud()?.AnnounceSpell("こはる", "@koharu_kitchen", Spells[0].name + "＋" + Spells[1].name, Spells[0].tint);
        }
    }

    // RECLOSE のキャラ別弱気セリフ（序盤=支配→終盤=絶望）。
    private static readonly string[] RecloseLines =
    {
        "まだだよ。ちゃんとしなきゃ、だめなの。",
        "じっとしてて。手を止めたら、終わっちゃう。",
        "やめないで……止まったら、お兄ちゃんが……",
    };
    private int _recloseIdx;
    protected override void OnRecloseLine()
    {
        ShowRecloseLine("こはる", RecloseLines[Mathf.Min(_recloseIdx, RecloseLines.Length - 1)]);
        _recloseIdx++;
    }

    protected override void GrantFollower() { }

    protected override void OnCryStart()
    {
        var hud = GetHud();
        hud?.HideBossBar();
        hud?.HideSpellCard(); // 宣告カードの残留を断つ（改心会話中はタイマー停止＝自然には消えない）
        GetNodeOrNull<GameManager>("/root/Game")?.NotifyRedemptionStart(); // 残機0の抜けプロンプトを演出に重ねない
        // 改心が始まる確実な瞬間に「解決音（完）」へ移す＝冷えていた旋律に温かい残響が戻る。
        Audio.Instance?.PlayRedeem(2);
        if (hud != null) hud.HoldBubble = true;
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd()
    {
        // S3 画の反転：改心成立（cry→post）で、空席の箸に湯気が戻り始める
        //（帰還の会話の背景でゆっくり回復。指ししない＝気づく余白）。
        (GetTree().GetFirstNodeInGroup("imagery") as StageImagery)?.TriggerReversal();
        Finished = true;
    }

    public override void _Process(double delta)
    {
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;
        _lineT += delta;

        if (_seq)
        {
            if (zEdge && _lineT >= 0.25)
            {
                _lineT = 0; _line++;
                if (_line >= Lines.Length)
                {
                    _seq = false;
                    var hud = GetHud();
                    if (hud != null) { hud.HoldBubble = false; hud.HideBubble(); }
                    EndCryNow();
                }
                else ShowLine();
            }
            return;
        }
    }

    private void ShowLine()
    {
        var (who, text, face) = Lines[_line];
        var hud = GetHud();
        if (hud == null) return;
        var kind = (Hud.LineKind)who;
        string portrait = kind switch
        {
            Hud.LineKind.Boy => face,
            // こはるは通常 koharu_face。絶望行だけ face に蒼白(pale)を指定して差し替える。
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? "res://char/koharu_face.png" : face,
            Hud.LineKind.Mina => string.IsNullOrEmpty(face) ? "res://char/mina_face.png" : face, // ミナも行ごと表情（worried 等）
            _ => "res://char/mina_face.png", // 中継ほか
        };
        hud.ShowDialog(kind, text, portrait, otherName: "こはる");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
