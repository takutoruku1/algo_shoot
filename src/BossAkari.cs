using Godot;

// BossAkari : STAGE1「あかり（雨の降りやまない退勤後のフロア）」のボス＝「あふれるわたし」@akari_ame。
// 自責の言葉（黒い吹き出し＝パネル）を旋回させ、下向きの自責弾を撒く。
// パネルを剥がしてHPを削り切る＝奥の“本当のあかり”の光に届く＝浄化（改心）。
// 浄化後は改心の姿を見せながら、ミナ自身が決定打を届ける（案C。S1-10）。フォロワーにはしない。
public partial class BossAkari : Enemy
{
    public override UnfolderKind UnfolderStyle => UnfolderKind.Akari;
    public bool EdgeAttackActive => _caster != null && _caster.EdgeAttackActive;
    public bool Finished { get; private set; }

    private readonly BossMover _mover = new BossMover();
    private const float RoamSpeed = 40f;

    private bool _lowHpRallied;
    private bool _memoryPending;
    private bool _memoryPlayed;
    private int _shieldBreaks;
    protected override int WindowCap => GameManager.Instance?.Difficulty is GameManager.Diff.Easy or GameManager.Diff.Normal
        ? Mathf.Min(base.WindowCap, TotalBars * BarHp / 5) : base.WindowCap;
    protected override void OnShieldBroken() => _shieldBreaks++;
    private BossPostSequence _posts = null!;
    public bool PostSequenceActive => _posts != null && _posts.Active;
    private int _pattern;
    private int _beatsFired;    // 流した独白の数
    private const int PatternCount = 4;

    private static readonly float[] PatternThresholds = { 0.78f, 0.52f, 0.26f };

    private AreaSpellCaster _caster = null!;
    // ── INI 外出しのバランス値（config/boss_stats.ini [akari]。読めなければ現行既定値）──
    private float _roamSpeed = RoamSpeed;

    private static readonly (BulletShape shape, Color tint, string art, float rot)[] MoodVisuals =
    {
        (BulletShape.Needle,  new Color("6c9cd8"), "akari_sticky",   70f), // 赤い付箋＝こっち見て
        (BulletShape.Diamond, new Color("4a6aa0"), "akari_envelope", 38f), // 封筒＝未送信の一通
        (BulletShape.Orb,     new Color("a8c8e8"), "akari_clip",     52f), // クリップ＝鎖のように
        (BulletShape.Needle,  new Color("e8f0ff"), "akari_stamp",    26f), // 承認印＝離さない
    };
    private static BossMover.Attack StanceOf(int pattern) => (pattern % PatternCount) switch
    {
        1 => BossMover.Attack.Ring,
        2 => BossMover.Attack.Aimed,
        _ => BossMover.Attack.Wall,
    };

    private void ApplyMood()
    {
        var s = MoodVisuals[_pattern % MoodVisuals.Length];
        _mover.SetNextAttack(StanceOf(_pattern));
        SetSpellVisual(s.shape, s.tint, BulletArt.Get(s.art), s.rot);
        GetHud()?.SetBossBarTint(s.tint);
    }

    // 浄化時のかけあい（who: 1=ミナ / 2=あかり）。Zで手動送り。
    private bool _seq;
    private int _line;
    private double _lineT;
    private bool _zHeld;
    private static readonly (int who, string text, string face)[] MemoryLeadIn =
    {
        (2, "……待って。まだ、言えてないのに。", "res://char/v3/akari_face.png"),
        (1, "はい。ここにおります。誰に、何を伝えたかったのですか。", "res://char/mina_face.png"),
        (2, "西野くん。同じ年に会社に入って、向かいの席で……。", "res://char/v3/akari_face.png"),
        (1, "その方が、いなくなったのですね。", "res://char/mina_face.png"),
        (2, "三日前に辞めたの。最後の日も、あたし、普通に仕事してた。", "res://char/v3/akari_face.png"),
        (1, "普通にしているほうが、楽でしたか。", "res://char/mina_face.png"),
        (2, "……そう思ってた。ちゃんと「またね」って言えれば、終わらない気がして。", "res://char/v3/akari_face.png"),
        (1, "その日は、どこまで言えたのでしょう。", "res://char/mina_face.png"),
        (2, "帰る前に、あたしの席へ来たの。そしたら、電話が鳴って……。", "res://char/v3/akari_face.png"),
    };
    private static readonly (int who, string text, string face)[] Lines =
    {
        (2, "……返事がほしかった。おめでとう、より先に。", "res://char/v3/akari_face_cry.png"),
        (2, "だから、何度も送って……怖くなって、取り消した。", "res://char/v3/akari_face_cry.png"),
        (1, "……いま、見えた下書き。あの一通だけは、消さずに残していたのですね。", "res://char/mina_face.png"),
        (1, "——「おめでとう ほんとだよ 元気でね」。……宛名は、ありません。", "res://char/mina_face.png"),
        (2, "……ほんとは、ちゃんと笑って見送りたかった。でも、寂しいほうが先に出ちゃった。", "res://char/v3/akari_face_cry.png"),
        (1, "取り消された十二通は、ぜんぶ、わたくしに当たりました。", "res://char/mina_face.png"),
        (1, "十二通、読みました。寂しかったのですね。……「好き」まで悪いものにしなくて、よいと思います。", "res://char/mina_face.png"),
        (2, "……ぁ……", "res://char/v3/akari_face_cry.png"),
    };
    private const int SilenceAtLine = 5;

    private static (int who, string text, string face)[] LinesFor(GameManager? game)
    {
        if (game?.ChosenAt("s1_4") != "一つずつ聞こう。ミナも無理しないで") return Lines;
        var lines = Lines[..];
        lines[SilenceAtLine] = (1, "机では、一通だけ開きました。続きは、あかりさんから聞きたくて。", "res://char/mina_face.png");
        lines[SilenceAtLine + 1] = (1, "いま、聞かせてくださった言葉を、覚えておきます。寂しかったのですね。……「好き」まで悪いものにしなくて、よいと思います。", "res://char/mina_face.png");
        return lines;
    }

    // 改心の台詞の実体。他ジョブ潜行中はミナ前提の Lines の代わりに改心相当シーン
    //   （CharacterStory.Redemption＝キャラ×面の9通り）を流す（2026-09-15。_Ready で差し替える）。
    //   _storySilenceAt＝改心相当シーンの「ここでBGM停止」行（ト書き。-1=無し）。
    private bool _charStory;
    private (int who, string text, string face)[] _lines = Lines;
    private int _storySilenceAt = -1;
    private CharacterStoryTalk? _memoryTalk;

    protected override void OnEnemyReady()
    {
        // 主要バランス値は INI（config/boss_stats.ini [akari]）で上書き可。第3引数＝現行既定値。
        Points = BossTuning.I("akari", "points", 1500);
        BodyRadius = BossTuning.F("akari", "body_radius", 19f);
        BodyHalfH = BossTuning.F("akari", "body_half_h", 23f);   // 縦長カプセル（絵の形に沿わせる）
        PanelCount = BossTuning.I("akari", "panel_count", 5); // 自責の言葉（黒い吹き出し）
        PanelInk = BossTuning.I("akari", "panel_ink", 36);
        OrbitRadius = BossTuning.F("akari", "orbit_radius", 26f);
        SpinSpeed = BossTuning.F("akari", "spin_speed", 0.9f);
        PanelsFire = false;      // 攻撃は本体の自責弾
        EnemyBulletSpeed = BossTuning.F("akari", "bullet_speed", 80f);

        int bars = BossTuning.I("akari", "hp_bars", 0);
        BarCount = bars > 0 ? bars : Mathf.Max(1, DiffBars(finalBoss: false) - 1);

        // 弾幕・ギミックの外出し値（INIに無ければフィールド初期値＝現行値のまま）。
        _roamSpeed = BossTuning.F("akari", "roam_speed", RoamSpeed);

        PreTexPath = "res://char/v3/boss_akari_body_idle_v3.png";
        DownTexPath = BossDownArt.Path("akari");
        AttackTexPath = "res://char/v3/boss_akari_body_attack_v3.png";
        RecoveryTexPath = "res://char/v3/boss_motion/akari_recover_v1.png";
        // 改心の三段：穢れ(pre＝待機)→泣き(cry＝専用の泣き顔)→改心後(post)。
        // cry は会話の間ずっと保持し、手動送りし切った EndCryNow で post へ着地する。
        // 旧 *_body_hit.png は被弾リアクション用で笑顔のままだった＝撃破しても穢れのままに見えたので、
        // 描き下ろしの *_body_cry.png（720px・エフェクトなし）に差し替えた。倍率・アンカーは待機と同じ。
        Form2TexPath = BossAnimalArt.Path("akari", "idle");
        Form2AttackTexPath = BossAnimalArt.Path("akari", "attack");
        Form2DownTexPath = BossAnimalArt.Path("akari", "down");
        CryTexPath = "res://char/v3/boss_akari_body_cry_v2.png";
        PostTexPath = "res://char/v3/enemy_akari_post.png";
        // パネルは専用素材なし → Panel のプレースホルダ（黒い「・・・」吹き出し）を使う
        // 表示高は ini（body_display_h）。v3 の本体はエフェクト込みで焼いていないぶん、旧52だと小さく見える。
        BodyDisplayH = BossTuning.F("akari", "body_display_h", 72f);
        BodyOffsetName = "akari";
        CryHoldDur = 9999.0;     // 自動終了させない（会話を手動送りし切ったら EndCryNow で閉じる）
    }

    protected override (float Scale, Vector2 Offset) GetBodyFrame(Texture2D texture)
    {
        // Match the person, not the trailing coat, and keep the head's horizontal axis steady.
        var axis = texture.ResourcePath.GetFile() switch
        {
            "boss_akari_body_idle_v3.png" => new Vector2(315, 603),
            "boss_akari_body_attack_v3.png" => new Vector2(340, 594),
            "boss_akari_body_form2_v3.png" => new Vector2(303, 572),
            _ => Vector2.Zero,
        };
        if (axis == Vector2.Zero) return base.GetBodyFrame(texture);
        float scale = 718f / axis.Y;
        return (scale, new Vector2(69.5f, 358f) / scale - (axis - texture.GetSize() / 2f));
    }

    public override void _Ready()
    {
        base._Ready();
        var game = GetNodeOrNull<GameManager>("/root/Game");
        _charStory = CharacterStory.DiveActive(game);
        if (_charStory)
        {
            _lines = CharacterStory.Redemption(game!.SelectedJob, "akari");
            _storySilenceAt = CharacterStory.RedemptionSilenceAt(game.SelectedJob, "akari");
        }
        else _lines = LinesFor(game);
        // 移動：スペルごとの立ち位置＋状態機械（待機→構え→攻撃→余韻）。数値は INI（[akari] の
        // cruise_speed / accel_time / stance_*）。あかりは「座ったまま滑る」＝重く（accel_time 大）、
        // 上下に揺れない（hover_amp 0）。
        _mover.Configure("akari", new Vector2(Field.BossCenterX, Field.BossZoneCenterY), Field.BossZoneHalfW, Field.BossZoneHalfH);
        GetHud()?.ShowBossBar("あふれるわたし", BossHandles.AkariBar, this);
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        // 【激情】メーター開始。初期値は道中（p4/s1_4/s1_5）の選択から決まる（FuryMeter.cs）。
        GetNodeOrNull<GameManager>("/root/Game")?.BeginFury("akari");
        ApplyMood();

        _caster = new AreaSpellCaster();
        _caster.Configure("akari", GetParent());
        AddChild(_caster);
        _posts = BossPostSequence.Attach(this, "akari", _caster.CancelPendingAttacks, OnHpChanged);

        // 部品の演出層（char/v3/fx/akari/*.png）を本体の子として1個ぶら下げる。当たり判定は持たない。
        // GetBodyFrame normalizes the new outfit to the existing 720px effect reference frame.
        AttachParts("akari", idleTexW: 393f, attackTexW: 645f);
    }

    protected override void UpdateMovement(double delta)
    {
        // 自機の位置を渡す＝自機狙いの追従（x と y の両方に寄る）と、反転の判定（40px 以上・0.6秒）に使う。
        if (GetTree().GetFirstNodeInGroup("player") is Node2D pl) _mover.SetPlayerPos(pl.GlobalPosition);
        if (!BarrageHolding) GlobalPosition = _mover.Step(GlobalPosition, delta, IsForm2 ? 1.65f : 1f);
        ApplyBossMotion(_mover.VisualOffset, _mover.Lean, IsForm2 ? !_mover.FacingLeft : _mover.FacingLeft, _mover.SquashScale);
        FxLayer.Instance?.EmitBossAura(FxLayer.BossAura.Akari, GlobalPosition, (float)delta, 32f);
        TickForm2Technique(delta, _mover, _caster);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (PostSequenceActive || _memoryTalk is { Active: true }) return;
        base._PhysicsProcess(delta);
    }

    protected override int LimitBodyDamage(int damage)
    {
        if (_posts is { Active: true }) return 0;
        float floor = _posts?.Floor ?? 0;
        if ((GameManager.Instance?.Difficulty is GameManager.Diff.Easy or GameManager.Diff.Normal) && !HasEvolved && _shieldBreaks < 3)
            floor = Mathf.Max(floor, 0.54f);
        return DamageToHpFloor(damage, floor);
    }

    public override void Purify()
    {
        if (PostSequenceActive) return;
        base.Purify();
    }

    // 本体に弾が刺さった一拍を移動側へ渡す（小さくのけぞって戻る）。当たり判定の中心は
    // 最大4px・時定数0.12sでしか動かない＝弾避けの公平性は保つ（BossMover.OnHit のコメント参照）。
    protected override void OnBodyDamaged(Vector2 fromDir) => _mover.OnHit(fromDir);

    protected override void OnHpChanged()
    {
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
        if (_memoryPending) return;
        if (_beatsFired < PatternThresholds.Length && HpRatio <= PatternThresholds[_beatsFired])
        {
            _pattern = (_pattern + 1) % PatternCount;
            _beatsFired++;
            ApplyMood();
            // 第二形態は既存の閾値の中盤（PatternThresholds[1]=0.52）に乗せる＝新しい閾値を足さない。
            if (_beatsFired == 2 && !_memoryPlayed)
            {
                _memoryPending = true;
                return;
            }
        }
        if (!_lowHpRallied && HpRatio <= 0.20f && (GameManager.LunaticActive || _posts.Count >= 4))
        {
            _lowHpRallied = true;
            RallyShield();
            GetHud()?.SetBossBarTint(MoodVisuals[0].tint); // フィナーレ色（#26）
        }
    }

    // S1-9 の RECLOSE（仮台本 06）。虚勢→弱気→最終形の宣言（動詞「返して」）の三段。
    //   2026-09-26（docs/20260926/主人公の存在_診断と本文 §3.1(b)）：弱気と最終形の間に、画面の前で読んでいる
    //   もう一人（ミナの後ろ＝あなた）へ返事を求める行。最終形「返して」は最後のまま（Min(idx, len-1) で以後ずっと出る行）。
    private static readonly string[] RecloseLines =
    { "既読も三秒でつけるし。返事も、すぐ書くし。……だから、ね?", "バカ。……バカ、バカ。", "……ねえ、ミナ。黙らないで。読んだなら、あなたの言葉で返してよ。", "……返して。読んだなら、返してよ。" };
    private int _recloseIdx;
    protected override void OnRecloseLine()
    {
        ShowRecloseLine("あかり", RecloseLines[Mathf.Min(_recloseIdx, RecloseLines.Length - 1)]);
        _recloseIdx++;
    }

    protected override void GrantFollower() { } // 新canonにフォロワーは無い

    protected override void OnCryStart()
    {
        SetBodyPose(BossParts.Pose.Cry);
        var hud = GetHud();
        hud?.HideBossBar();
        hud?.HideSpellCard(); // 宣告カードの残留を断つ（改心会話中はタイマー停止＝自然には消えない）
        GetNodeOrNull<GameManager>("/root/Game")?.EndFury();           // 【激情】メーターを止めて畳む
        GetNodeOrNull<GameManager>("/root/Game")?.NotifyRedemptionStart(); // 残機0の抜けプロンプトを演出に重ねない
        // 改心が始まる確実な瞬間に「解決音（完）」へ移す＝途切れていたフレーズが最後まで歌われる。
        Audio.Instance?.PlayRedeem(1);
        // 会話を出せない状況（Hud が取れない／台詞が無い）なら会話に入らず即着地させる
        //   ＝送るものが無いのに EndCryNow を待ち続けて Finished が立たない詰まりを断つ。
        //   ルナティックも同じ経路＝改心のかけあい（弾を止める会話）を出さず、その場で着地して Finished へ。
        if (hud == null || _lines.Length == 0 || GameManager.LunaticActive) { EndCryNow(); return; }
        hud.HoldBubble = true;
        _seq = true; _line = 0; _lineT = 0;
        ShowLine();
    }

    protected override void OnCryEnd()
    {
        // S3 画の反転：改心成立（cry→post）で、自責の言葉が「ありがとう」へ溶け始める
        //（帰還の会話の背景でゆっくりクロスフェード。指ししない＝気づく余白）。
        (GetTree().GetFirstNodeInGroup("imagery") as StageImagery)?.TriggerReversal();
        Finished = true;
    }

    // 保険タイムアウトで cry が強制終了されたとき、会話ドライバも畳む（_seq が残ると台詞が出続ける）。
    protected override void AbortCrySequence() => _seq = false;

    public override void _Process(double delta)
    {
        if (PostSequenceActive) return;
        if (_memoryTalk is { Active: true }) { _memoryTalk.Update(delta); return; }
        // Start outside collision dispatch so suspending the world cannot interrupt a hit callback.
        if (_memoryPending && !_seq && !IsPurified && !Hud.BubblePaused)
        {
            _memoryPending = false;
            _memoryPlayed = true;
            // ルナティック：回想を挟まない。フィルム明けの復帰（第二形態→宣告→保留していた閾値の拾い直し）だけをその場で通す
            //   ＝弾幕も曲も途切れない。
            if (GameManager.LunaticActive)
            {
                AdvanceForm2();
                ApplyMood();
                OnHpChanged();
                return;
            }
            _caster.CancelPendingAttacks();
            void ResumeBattle()
            {
                _zHeld = Pad.AdvanceHeld();
                _posts.ResumeMusic();
                AdvanceForm2();
                ApplyMood();
                OnHpChanged();
            }
            // ミナ潜行＝この面のボスのフィルム（操作キャラを問わない。StoryFilm.cs 冒頭の「選択の規則」参照）。
            // 他ジョブ潜行＝一枚絵を起こさず CharacterStory.Memory（潜行キャラ×この面のボスの9通り）を会話枠で
            //   （2026-09-23 ユーザー指示「吹き出しのやり取りだけにして」）。どちらも終わりは同じ ResumeBattle。
            if (_charStory)
            {
                var game = GetNodeOrNull<GameManager>("/root/Game");
                FilmSkip.MarkSeen(game, CharacterStory.SeenKey(game!.SelectedJob, aftermath: false));   // 写真アプリの解禁
                _memoryTalk = CharacterStoryTalk.Start(CharacterStory.Memory(game.SelectedJob, "akari"),
                    GetHud, ShowStoryLine, ResumeBattle);
            }
            else
            {
                GetHud()!.HideSpellCard();
                Audio.Instance?.StopMusic(0.7f);
                _memoryTalk = CharacterStoryTalk.Start(MemoryLeadIn, GetHud, ShowStoryLine,
                    () => AkariStoryFilm.Play(GetHud()!, GetParent(), aftermath: false, completed: ResumeBattle));
            }
            return;
        }
        if (!_seq && !IsPurified) _posts.UpdateDamage();
        // 改心の会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld && !Pad.UiBlocked(this);
        _zHeld = z;
        _lineT += delta;

        if (_seq)
        {
            var dialogHud = GetHud()!;
            if (zEdge && !dialogHud.DialogRevealed)
            {
                dialogHud.RevealDialogNow();
                _lineT = 0;
                NotifyCryProgress();
            }
            else if (dialogHud.DialogRevealed
                     && (zEdge || (dialogHud.FastForwarding && _lineT >= 0.25) || (dialogHud.AutoAdvanceReady && _lineT >= 1.4)))
            {
                _lineT = 0; _line++;
                NotifyCryProgress(); // 送れている間は保険タイムアウトを起こさない
                if (_line >= _lines.Length)
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
        var (who, text, face) = _lines[_line];
        var hud = GetHud();
        if (hud == null) return;
        var kind = (Hud.LineKind)who;
        if (_line == (_charStory ? _storySilenceAt : SilenceAtLine)) Audio.Instance?.StopMusic(1.2f);
        string portrait = kind switch
        {
            Hud.LineKind.Companion => face,                 // 潜行キャラ本人＝表情差分（空欄は Hud がジョブ立ち絵へ）
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? "res://char/v3/akari_face.png" : face, // あかりは行ごと差し替え可（こはる方式）
            _ => "res://char/mina_face.png",                // ミナ
        };
        hud.ShowDialog(kind, text, portrait, otherName: "あかり");
    }

    private void ShowStoryLine(Hud hud, int who, string text, string face)
    {
        var kind = (Hud.LineKind)who;
        string portrait = kind switch
        {
            Hud.LineKind.Companion => face,
            Hud.LineKind.Other => string.IsNullOrEmpty(face) ? "res://char/v3/akari_face.png" : face,
            _ => face,
        };
        hud.ShowDialog(kind, text, portrait, otherName: "あかり");
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;
}
