using Godot;

// BossKoharu : STAGE3「こはる（永遠に夕食を作り続ける台所）」のボス＝穢れの核「むだなわたし」。
// 兄の余命を知り、家事＝祈りが砕けた無力感。怒り（他責）の下にある悲しみへ光を届ける。
// 専用立ち絵は未用意のため、Enemy の _Draw プレースホルダ（人型）で本体を表示する。
// 禁止語「あなたのせいじゃない」は使わない。祈りが届いていたことを伝えて解く。
public partial class BossKoharu : Enemy
{
    public bool Finished { get; private set; }

    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();
    private Vector2 _moveTarget;
    private bool _hasTarget;
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
        GetHud()?.AnnounceSpell("こはる", "@koharu_kitchen", s.name, s.tint);
    }

    // 浄化のかけあい（who: 0=少年 / 1=ミナ / 2=こはる）。少年はミナの声で“中継”して届ける。
    // 設計書 v2 [P-03] のボス節を順序通りに（ミナの気遣い・少年の取り繕いも含む）。
    // 躁的暴走＝支配的な世話焼き。傷＝兄の死に抗える唯一の力＝完璧にすれば失わない、という呪術。
    // 決定打＝劇的アイロニー。兄＝死にゆく少年が、妹に。言い切らせず観客に繋がせ、ミナが半分気づく。
    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "ごはん、できたよ! ぜんぶ食べてね。残したら……許さないんだから。", ""),
        (2, "あ、そこ汚れてる。掃除するから、じっとしてて。動かないで。ね?", ""),
        (2, "だいじょうぶ。あたしがちゃんとするから。お兄ちゃんは、なにもしなくていいの。", ""),
        (1, "……ご主人様? お顔の色が。", ""),
        (0, "……なんでもない。続けるぞ。", SCocky),
        (2, "ちゃんと作って、ちゃんと食べさせて、ちゃんと、ちゃんとしてれば——", ""),
        (2, "……ちゃんとしてれば、お兄ちゃん、いなくならないでしょ? ねえ、いなくならないよね……?", ""),
        (5, "——怒りの下の悲しみを、ちゃんと、悲しんでいい。", ""),
        (2, "あたしのごはんじゃ、お兄ちゃんは助からない……! じゃあ、なんの、意味が……!", ""),
        (5, "お兄さんが今日まで生きてこられたのは——きみの食卓が、あったからだ。祈りは、届いてたよ。", ""),
        (2, "……ほんとに? ……ちゃんと、食べて、くれるかな。今日は。", ""),       // 日常語＝小さな願い
        (0, "……ああ。残さず、食べるよ。", SGentle),                              // 兄が、妹に（観客だけが意味を知る）
        (0, "明日のぶんは——……いや。たくさん、作ってやってくれ。きみは、えらいよ。", SGentle), // “明日のぶんはいい”を呑み込む
        (1, "ご主人様。いまの……どなたに、おっしゃったんですか。", ""),         // ミナが半分気づく（Final/伏線へ）
        (0, "…………帰ろう、ミナ。", SCocky),                                      // 再仮面
    };

    protected override void OnEnemyReady()
    {
        _rng.Randomize();
        Points = 1800;
        BodyRadius = 9f;
        PanelCount = 5;          // 「むだだよ」等の言葉（黒い吹き出し）
        PanelInk = 2;
        OrbitRadius = 26f;
        SpinSpeed = 0.85f;
        PanelsFire = false;
        EnemyBulletSpeed = 80f;

        MaxHp = 44;
        PanelRespawnDelay = 1.4f;

        PreTexPath = "res://char/enemy_koharu_pre.png";   // 穢れ・病んだ核
        CryTexPath = "res://char/enemy_koharu_post.png";  // 浄化＝改心を見せながら会話（その場で静止）
        PostTexPath = "res://char/enemy_koharu_post.png";
        BodyDisplayH = 52f;
        CryHoldDur = 9999.0;     // 自動終了させない（会話を手動送りし切ったら EndCryNow で閉じる）
    }

    public override void _Ready()
    {
        base._Ready();
        // ボス登場＝道中BGMからボスBGMへクロスフェード。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBoss);
        GetHud()?.ShowBossBar("とまれないわたし");
        GetHud()?.UpdateBossBar(HpRatio);
        ApplySpell();
    }

    protected override void UpdateMovement(double delta)
    {
        if (!_hasTarget) PickTarget();
        Vector2 to = _moveTarget - GlobalPosition;
        if (to.Length() < 8f) { PickTarget(); to = _moveTarget - GlobalPosition; }
        GlobalPosition += to.Normalized() * RoamSpeed * (float)delta;
        FirePattern(delta);
    }

    private void PickTarget()
    {
        _moveTarget = new Vector2(_rng.RandfRange(70f, 320f), _rng.RandfRange(38f, 108f));
        _hasTarget = true;
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

    private void FanDown(BulletPool pool)
    {
        int k = Dn(9);
        for (int i = 0; i < k; i++)
        {
            float t = (float)i / (k - 1) - 0.5f;
            float a = Mathf.Pi / 2f + t * Mathf.DegToRad(78f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * EnemyBulletSpeed, 3.4f);
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
        GetHud()?.UpdateBossBar(HpRatio);
        if (_beatsFired < PatternThresholds.Length && HpRatio <= PatternThresholds[_beatsFired])
        {
            _pattern = (_pattern + 1) % PatternCount;
            _beatsFired++;
            ApplySpell();
        }
        if (!_finale && HpRatio <= 0.2f)
        {
            _finale = true;
            GetHud()?.AnnounceSpell("こはる", "@koharu_kitchen", Spells[0].name + "＋" + Spells[1].name, Spells[0].tint);
        }
    }

    protected override void GrantFollower() { }

    protected override void OnCryStart()
    {
        GetHud()?.HideBossBar();
        var hud = GetHud();
        if (hud != null) hud.HoldBubble = true;
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd() => Finished = true;

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
            Hud.LineKind.Other => "res://char/koharu_face.png",
            _ => "res://char/mina_face.png", // ミナ・中継
        };
        hud.ShowDialog(kind, text, portrait, otherName: "こはる");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
