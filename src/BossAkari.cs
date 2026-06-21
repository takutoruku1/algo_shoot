using Godot;

// BossAkari : STAGE1「あかり（雨の教室）」のボス＝穢れの核「ゆるせないわたし」。
// 自責の言葉（黒い吹き出し＝パネル）を旋回させ、下向きの自責弾を撒く。
// パネルを剥がしてHPを削り切る＝奥の“本当のあかり”の光に届く＝浄化（改心）。
// 浄化後は改心の姿を見せながら、少年（正体を隠したまま）が普遍化した言葉を贈る。フォロワーにはしない。
public partial class BossAkari : Enemy
{
    public bool Finished { get; private set; }

    private readonly BossMover _mover = new BossMover();
    private const float RoamSpeed = 40f;

    private double _fireT;
    private double _fireT2;   // フィナーレ用の第2タイマー（2スペル同時撃ち）
    private bool _finale;     // HP2割以下＝2スペル同時展開
    private float _ringOff;
    private int _pattern;       // 現在の攻撃パターン（セリフを挟むたびに変化）
    private int _beatsFired;    // 流した独白の数
    private const int PatternCount = 4;

    // HPがこの割合を割るたびに攻撃パターンを変える（独白は浄化のかけあいに集約）。
    private static readonly float[] PatternThresholds = { 0.78f, 0.52f, 0.26f };

    // スペルカード（RefrainHTML Danmaku v3 STAGE2 あかり＝雨の教室・青と白の寒色）。
    private static readonly (string name, BulletShape shape, Color tint)[] Spells =
    {
        ("ねえ、こっち見て", BulletShape.Needle,  new Color("6c9cd8")), // 雨青・降雨の針
        ("すきって言って",   BulletShape.Diamond, new Color("4a6aa0")), // 藍・大きく遅い菱形
        ("ずっと一緒",       BulletShape.Orb,     new Color("a8c8e8")), // 淡青・狙い撃ち（包囲）
        ("離さない",         BulletShape.Needle,  new Color("e8f0ff")), // 白・追尾で逃がさない
    };
    private void ApplySpell()
    {
        var s = Spells[_pattern % Spells.Length];
        SetSpellVisual(s.shape, s.tint);
        GetHud()?.AnnounceSpell("あかり", "@akari_ame", s.name, s.tint);
    }

    // 浄化時のかけあい（who: 0=少年 / 1=ミナ / 2=あかり / 3=地・記憶＝ミナ語り＋記憶フラッシュ）。Zで手動送り。
    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;
    // 浄化のかけあい（設計書 v2 [P-02b] のボス節を順序通りに）。少年は自分の声では言えず、ミナが“中継”して届ける。
    // ※「あなたのせいじゃない」は最も効かない言葉＝禁止。庇った側の意志を立てる言葉で解く。
    private const string SGentle = "res://char/shonen_gentle.png";
    // 躁的暴走＝愛の洪水（love-bombing）。傷＝本命に言えなかった「好き」が宛先を失い溢れる。
    // 決定打＝本作の主モチーフ「名前を呼ぶ」（lines§2）。本人が乞うた一語で抜く。
    // 伏線③「ぼくの声じゃだめ」は維持＝名前はミナの声で届ける（少年は正体を隠す代償）。
    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "ねえ、こっち見て。ねえってば。あたしのこと、見て。", ""),
        (2, "好き? 好きって言って。あたしも好き。だいすき。ずっと一緒にいようね。離さないから。", ""),
        (2, "……ねえ。どうして、名前で、呼んでくれないの。", ""),
        (2, "いつから、あたしは……“キミ”に、なったの……?", ""),
        (1, "“キミ”……? ご主人様、これは——", ""),
        (0, "————行こう。奥だ。", SGentle),
        (2, "来ないで……っ。あたしの“好き”は、迷惑なだけ。だから、世界中に、ばら撒くしか——", ""),
        (3, "雨の交差点。言いかけた唇。「あのね、あたし——」。クラクション。", ""),
        (0, "ミナ。……一度だけ。ぼくの代わりに、あの子の名前を、呼んでやってくれ。", SGentle),
        (1, "ご主人様の、お声では。いけないんですか。", ""),
        (0, "————ぼくの声じゃ、だめなんだ。気づかれて、しまうから。", SGentle),
        (5, "——あかり。", ""),                                                   // 決定打＝名前（一行手前で無音→ここで主題）
        (2, "……いま、名前。あたしの、名前……。あったかい……なんで、こんなに……", ""),
        (5, "きみの“好き”は、迷惑なんかじゃない。——届けたかった一人には、とっくに、届いてるよ。", ""),
        (2, "……ぁ……", ""),                                                     // 言わせない
    };

    protected override void OnEnemyReady()
    {
        Points = 1500;
        BodyRadius = 9f;
        PanelCount = 5;          // 自責の言葉（黒い吹き出し）
        PanelInk = 2;
        OrbitRadius = 26f;
        SpinSpeed = 0.9f;
        PanelsFire = false;      // 攻撃は本体の自責弾
        EnemyBulletSpeed = 80f;

        // HPバー本数は難易度別（通常ボス：Easy3/Normal4/Hard5/Lunatic6）。総HP=BarHp×本数。
        BarCount = DiffBars(finalBoss: false);

        PreTexPath = "res://char/enemy_akari_pre.png";
        // 改心の三段：穢れ(pre)→泣き(cry＝触手がほどけ涙があふれる中間)→笑顔(post)。
        // cry は会話の間ずっと保持し、手動送りし切った EndCryNow で post（笑顔）へ着地する。
        CryTexPath = "res://char/enemy_akari_cry.png";
        PostTexPath = "res://char/enemy_akari_post.png";
        // パネルは専用素材なし → Panel のプレースホルダ（黒い「・・・」吹き出し）を使う
        BodyDisplayH = 52f;
        CryHoldDur = 9999.0;     // 自動終了させない（会話を手動送りし切ったら EndCryNow で閉じる）
    }

    public override void _Ready()
    {
        base._Ready();
        // ボス登場＝道中BGMからあかり固有テーマへクロスフェード（フレーズが途中で切れる＝未完）。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmBossAkari);
        // 徘徊：画面上部のボスゾーンに収め、イージング＋ホバーで漂わせる（旧 RoamSpeed を踏襲）。
        _mover.Configure(new Vector2(200f, 70f), 90f, 28f, RoamSpeed);
        GetHud()?.ShowBossBar("あふれるわたし", "@akari.");
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        ApplySpell();

        var caster = new AreaSpellCaster();
        caster.Configure("akari", GetParent());
        AddChild(caster);
    }

    protected override void UpdateMovement(double delta)
    {
        GlobalPosition = _mover.Step(GlobalPosition, delta);
        ApplyBossMotion(_mover.VisualOffset, _mover.Lean, _mover.FacingLeft);
        FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Akari, GlobalPosition, (float)delta, 32f);
        FirePattern(delta);
    }

    // 攻撃パターン（セリフを挟むたびに _pattern が変わる）。
    private void FirePattern(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        if (_finale) { FireFinale(pool, delta); return; }
        _fireT += delta;
        switch (_pattern)
        {
            case 0: if (_fireT >= Di(1.0)) { _fireT = 0; FanDown(pool); } break;       // 下向きの雨の扇
            case 1: if (_fireT >= Di(1.2)) { _fireT = 0; Ring(pool); } break;          // 回転する放射リング
            case 2: if (_fireT >= Di(0.7)) { _fireT = 0; AimedSpread(pool); } break;   // 自機狙いの3way連射
            default: if (_fireT >= Di(0.085)) { _fireT = 0; Spiral(pool); } break;     // 二重スパイラル
        }
    }

    // フィナーレ（HP2割以下）：「雨」(雨青の針)＋「机が落ちる」(藍の菱形螺旋)を同時展開。
    private void FireFinale(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        if (_fireT >= Di(0.9)) { _fireT = 0; SetSpellVisual(Spells[0].shape, Spells[0].tint); FanDown(pool); }
        if (_fireT2 >= Di(0.085)) { _fireT2 = 0; SetSpellVisual(Spells[1].shape, Spells[1].tint); Spiral(pool); }
    }

    private void FanDown(BulletPool pool)
    {
        int k = Dn(9);
        for (int i = 0; i < k; i++)
        {
            float t = (float)i / (k - 1) - 0.5f;
            float a = Mathf.Pi / 2f + t * Mathf.DegToRad(80f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * EnemyBulletSpeed, 3.4f);
        }
    }

    private void Ring(BulletPool pool)
    {
        int k = Dn(16);
        _ringOff += Mathf.DegToRad(11f);
        for (int i = 0; i < k; i++)
        {
            float a = _ringOff + Mathf.Tau * i / k;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 72f, 3.4f);
        }
    }

    private void AimedSpread(BulletPool pool)
    {
        float baseA = Mathf.Atan2(AimAtPlayer().Y, AimAtPlayer().X);
        for (int i = -1; i <= 1; i++)
        {
            float a = baseA + i * Mathf.DegToRad(14f);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 96f, 3.4f);
        }
    }

    private void Spiral(BulletPool pool)
    {
        _ringOff += Mathf.DegToRad(13f);
        for (int s = 0; s < 2; s++)
        {
            float a = _ringOff + Mathf.Pi * s;
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 90f, 3.2f);
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
        // HPが閾値を割るたびに攻撃パターンを変える。
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
            GetHud()?.AnnounceSpell("あかり", "@akari_ame", Spells[0].name + "＋" + Spells[1].name, Spells[0].tint);
        }
    }

    // RECLOSE のキャラ別弱気セリフ（序盤=虚勢→終盤=弱気）。
    private static readonly string[] RecloseLines =
    {
        "やだ、まだ見て。離さないってば。",
        "来ないで……っ。あたしの“好き”は、迷惑なだけ。",
        "ひとりにしないで……お願い、まだ……",
    };
    private int _recloseIdx;
    protected override void OnRecloseLine()
    {
        ShowRecloseLine("あかり", RecloseLines[Mathf.Min(_recloseIdx, RecloseLines.Length - 1)]);
        _recloseIdx++;
    }

    protected override void GrantFollower() { } // 新canonにフォロワーは無い

    protected override void OnCryStart()
    {
        GetHud()?.HideBossBar();
        // 改心が始まる確実な瞬間に「解決音（完）」へ移す＝途切れていたフレーズが最後まで歌われる。
        Audio.Instance?.PlayRedeem(1);
        var hud = GetHud();
        if (hud != null) hud.HoldBubble = true;
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd()
    {
        Finished = true;
    }

    // 戦闘中の独白・浄化のかけあいを Z で手動送り。
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
                    EndCryNow(); // 改心の笑顔へ着地し退場
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
        if (kind == Hud.LineKind.Narration) // 地・記憶：記憶フラッシュを焚く
            (GetTree().GetFirstNodeInGroup("imagery") as StageImagery)?.TriggerMemoryFlash();
        string portrait = kind switch
        {
            Hud.LineKind.Boy => face,                       // 少年（行ごとの表情）
            Hud.LineKind.Other => "res://char/akari_face.png", // あかり
            _ => "res://char/mina_face.png",                // ミナ・中継
        };
        hud.ShowDialog(kind, text, portrait, otherName: "あかり");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
