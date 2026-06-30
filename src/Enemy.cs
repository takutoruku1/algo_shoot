using Godot;
using System.Collections.Generic;

// Enemy : SNSの悪意で「悪魔化した人間」本体。不滅で“倒さない/殺さない”。
// 周囲を旋回する黒い吹き出しパネル(Panel)を持ち、全部剥がされると【浄化(改心)】される。
// 浄化後は敵グループを抜け、笑顔の味方コメントとして左へ流れ、画面外でfree。
// 浄化の瞬間に「やさしさの波紋(Ripple)」を出し、近くの人を連鎖浄化する。
// 衝突: 本体 layer=4(接触で自機被弾)。パネルは Panel 側(layer16)。
public partial class Enemy : Area2D
{
    // 浄化(改心)時の基礎得点（派生で上書き）。
    protected int Points = 100;
    protected float BodyRadius = 9f;

    // パネル構成（派生で設定）。
    protected int PanelCount = 3;
    protected int PanelInk = 2;
    protected float OrbitRadius = 18f;
    protected float PanelDisplayScale = 1f; // パネル絵＆当たりの拡縮（ザコ縮小用。ボスは1のまま）
    protected float SpinSpeed = 1.4f; // rad/s
    protected bool PanelsFire = true;
    protected float PanelFireInterval = 1.9f;
    protected float EnemyBulletSpeed = 90f;

    // スプライト素材（null/未設定なら _Draw のプレースホルダ図形を使う）
    protected string PreTexPath = "";
    protected string PostTexPath = "";
    protected string PanelTexPath = "";
    // 任意：浄化の瞬間に一時表示する「大泣き」スプライト。設定すると pre→cry→(CryHoldDur秒)→post の3段階に。
    protected string CryTexPath = "";
    protected double CryHoldDur = 0;
    protected float BodyDisplayH = 40f;
    protected bool FaceLeft = true; // 進行方向(左=プレイヤー側)を向く。素材は右向きなので反転。
    private Sprite2D _bodySprite = null!;
    private bool _hasBodyTex;

    // ─── ボス用：言葉のシールド＋無防備窓サイクル（HPバー方式リワーク）───
    // SHIELDED（周回パネル・通常弾幕。弾はパネルのInkを削るだけ。本体HPは減らない）
    //  → 全パネル破壊で BREAK（タメ＋合図演出）
    //  → EXPOSED 無防備窓（プレイヤー弾の威力が本体HPへ直通。パネル復活なし）
    //  → 窓終了で RECLOSE（キャラ別セリフ→パネル一括再生成）→ SHIELDED
    //  → HP<=0 で Redeem()
    protected enum BossPhase { Shielded, Break, Exposed, Reclose }
    private BossPhase _phase = BossPhase.Shielded;
    private double _phaseT; // 現フェーズ経過秒
    public const int BarHp = 100;                 // 1本=100（HUDで大きく動く）
    protected int BarCount = 0;                    // >0でHPバー方式に。総HP=BarHp×BarCount（派生が設定）。
    private const double VulnDur = 4.0;            // 無防備窓（全難易度共通）
    private const double BreakCueDur = 0.45;       // BREAK タメ＋合図
    private const double RecloseLineDur = 1.2;     // RECLOSE セリフ尺
    private const double RespawnGap = 0.15;        // RECLOSE 後パネル一括再生成までの間（弱気セリフ後の空白を詰めてテンポ維持）
    private const double VulnWarnLead = 1.0;       // 窓終了この秒前から明滅を速める（終了予告）
    // ── 無防備窓の「密着ボーナス」(桜井: 引き撃ちだけが最適にならないよう、近いほど得) ──
    // 本体 GlobalPosition と自機の距離が PointBlankRange 以内なら密着クリティカル。
    // 当たり判定(_bodyShape/本体GlobalPosition/自機ヒット半径)は一切変えない＝ダメージ計算のみ。
    private const float PointBlankRange = 48f;     // この内側はクリティカル（2バンドで明快に）
    private const float PointBlankMult = 1.6f;     // 密着クリティカル倍率（約+60%）
    private const int   PointBlankCap = 6;         // クリティカル時の上限（過剰即殺を防ぎバー方式の手応えを保つ）
    // ── 1つの無防備窓で本体へ通せる被ダメ上限（窓キャップ）──
    //   1窓で削れる量を頭打ちにし、密着クリティカル＋高連射での「1窓即殺」を抑える。
    //   到達後はその窓では本体HPが減らない（弾の Despawn は継続＝撃ち心地は残す）。EnterExposed で 0 にリセット。
    //   密着クリティカルは上限を超えず「到達を早める」だけ＝近づく価値は残しつつ過剰削りを抑える。
    private const int   ExposedDamageCap = 90;
    private int _windowDamage;                      // 現在の無防備窓で本体へ通した累計ダメージ
    private bool _windowCapNotified;               // 「MAX」表示を窓ごとに一度だけ出すワンショット
    // 本体ヒットのクールダウン（同一フレームの多重弾で過剰に削れるのを軽く抑える補助）。
    private double _bodyHitCd;
    private const double BodyHitCd = 0.05;
    private int _maxHp;                             // 総HP（=BarHp×BarCount）
    private int _hp;
    public bool HasHpBar => _maxHp > 0;
    public float HpRatio => _maxHp > 0 ? (float)_hp / _maxHp : 0f;
    // HUD「1本リフィル方式」用。現在の1本ぶんを 0〜1 で、残バー数を index/total で示す。
    public int TotalBars => BarCount;
    public int CurrentBarIndex => _maxHp <= 0 ? 0 : Mathf.Clamp((_hp - 1) / BarHp, 0, BarCount - 1); // 残バーの先頭(0始まり)
    public float CurrentBarFrac => _maxHp <= 0 ? 0f : (_hp <= 0 ? 0f : (float)(((_hp - 1) % BarHp) + 1) / BarHp);

    private readonly List<Panel> _panels = new List<Panel>();
    private bool _purified;
    private bool _becameFollower; // この本人がフォロワーに化けた＝退場（左流れ）をスキップして二重表示を防ぐ
    private bool _crying;     // 大泣き中（3段階浄化の中間）
    private double _cryT;
    private bool _flashing;
    private double _flashT;
    private const double FlashDur = 0.5;

    // 無防備窓で本体を撃ち込んだ瞬間の手応え（“効いてる”実感）。
    // 当たり判定は一切触らず、_Draw の発光リング＋音＋（大ダメージ時）軽い揺れ/止めだけで返す。
    private double _hitFlashT;                 // 被弾発光の残り（_Draw が参照）
    private const double HitFlashDur = 0.16;   // 短く・即・尾を引かせない（テンポ維持）
    private float _hitFlashMag;                // 直近被弾の威力（リングの強さに反映）

    // ─── 改心の“溶けるような”差し替え演出（クロスフェード＋squash→pop）の調整定数 ───
    // 当たり判定は一切動かさない：すべて _bodySprite の Transform/Modulate のみで表現する。
    private const double SwapFadeDur = 0.12; // 旧→新テクスチャのクロスフェード尺
    private const float SquashScale = 1.15f; // 差し替え瞬間の最大ふくらみ（×BaseScale）
    private const float PopLiftPx = 6f;      // フォロースルーで一瞬持ち上げる量(px・見た目のみ)
    private const double HitstopDur = 0.08;  // 改心確定の一拍で止める長さ

    // 立ち絵の“素”のスケール（BodyDisplayH/テクスチャ高で決まる）。squash はこれに係数を掛ける。
    private float _baseScale = 1f;
    // ApplyBossMotion が与える呼吸/浮遊オフセット。pop の持ち上げはこれに加算して描く（呼吸と喧嘩しない）。
    private Vector2 _motionOffset = Vector2.Zero;

    // 差し替えクロスフェード：旧テクスチャを別 Sprite2D に退避してα落とし、本体(新)をα上げ。
    private Sprite2D? _fadeSprite;
    // squash→pop の進行（0..1）。差し替えの瞬間に起動し、SwapAnimDur で 1 に達して終わる。
    private bool _swapAnim;
    private double _swapAnimT;
    private const double SwapAnimDur = 0.22; // squash→pop の全長（クロスフェードより少し長く余韻を残す）

    private CollisionShape2D _bodyShape = null!;

    public bool IsPurified => _purified;
    public int PanelsRemaining => _panels.Count;
    public bool IsExposed => _phase == BossPhase.Exposed; // 派生／演出が「今は殴れる」を参照

    public override void _Ready()
    {
        AddToGroup("enemies");
        CollisionLayer = 4;  // 敵本体（接触で自機被弾）
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = true;
        _bodyShape = new CollisionShape2D { Shape = new CircleShape2D { Radius = BodyRadius } };
        AddChild(_bodyShape);
        // 無防備窓中だけ本体が自機弾を拾う（EnterExposed/EnterReclose で mask=2 を開閉する）。
        AreaEntered += OnBodyHitByPlayerBullet;

        OnEnemyReady();
        // ボスHPは難易度別バー本数で決まる（総HP=BarHp×BarCount）。本数は派生 OnEnemyReady で確定済み。
        _maxHp = BarCount * BarHp;
        _hp = _maxHp;
        SetupBodySprite();
        SpawnPanels();
    }

    protected virtual void OnEnemyReady() { }

    // ─── スペルカードの弾形・色（RefrainHTML Danmaku v3）───
    // 派生ボスがパターン切替時に SetSpellVisual で更新し、FireBullet が反映する。
    protected BulletShape CurShape = BulletShape.Orb;
    protected Color CurTint;
    protected bool CurTintSet;
    protected void SetSpellVisual(BulletShape shape, Color tint)
    {
        CurShape = shape; CurTint = tint; CurTintSet = true;
    }
    // 現在のスペルの弾形・色で敵弾を1発撃つ（各ボスの pool.Spawn 置き換え用）。
    protected Bullet FireBullet(BulletPool pool, Vector2 pos, Vector2 vel, float radius = 3.4f, int dmg = 1)
        => pool.Spawn(pos, vel, isEnemy: true, radius, dmg, CurShape, CurTintSet ? CurTint : (Color?)null);

    // 難易度別HPバー本数（総HP=BarHp×本数）。派生ボスが OnEnemyReady で BarCount に設定する。
    protected int DiffBars(bool finalBoss) =>
        GetNodeOrNull<GameManager>("/root/Game")?.DiffBarBonus(finalBoss) ?? (finalBoss ? 5 : 4);

    // 難易度に応じた弾数。派生ボスが弾幕パターンの本数を安全にスケールするために使う。
    protected int Dn(int baseCount) =>
        GetNodeOrNull<GameManager>("/root/Game")?.ScaleBullets(baseCount) ?? baseCount;

    // 難易度に応じた発射間隔。やさしいほど長く（連射が遅く）なる。
    // 派生ボスは `_fireT >= Di(基準秒)` の形でしきい値に掛けて使う。
    protected double Di(double baseInterval) =>
        baseInterval * (GetNodeOrNull<GameManager>("/root/Game")?.DanmakuIntervalMul ?? 1f);

    private void SetupBodySprite()
    {
        if (string.IsNullOrEmpty(PreTexPath)) return;
        var t = ResourceLoader.Load<Texture2D>(PreTexPath);
        if (t == null) return;
        _hasBodyTex = true;
        _bodySprite = new Sprite2D
        {
            Name = "Body",
            Texture = t,
            Centered = true,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear, // 高解像度素材を滑らかに縮小
            ZIndex = -1, // パネルより奥
            FlipH = FaceLeft, // 素材は右向き→左(進行方向)へ反転
        };
        float s = BodyDisplayH / t.GetHeight();
        _baseScale = s;
        _bodySprite.Scale = new Vector2(s, s);
        AddChild(_bodySprite);
    }

    private void SpawnPanels()
    {
        for (int i = 0; i < PanelCount; i++)
            SpawnOnePanel(Mathf.Tau * i / Mathf.Max(1, PanelCount));
    }

    private void SpawnOnePanel(float baseAngle)
    {
        var p = new Panel();
        p.Setup(this, baseAngle, OrbitRadius, SpinSpeed, PanelsFire, PanelFireInterval, PanelInk, EnemyBulletSpeed, PanelTexPath, PanelDisplayScale);
        AddChild(p);
        _panels.Add(p);
    }

    // パネルが砕けた通知。
    // 通常敵：全部剥がれたら浄化。
    // ボス(HPバー方式)：本体HPは減らさない＝残数カウントのみ。0枚で BREAK へ遷移し、無防備窓を開く。
    public void OnPanelStripped(Panel p)
    {
        _panels.Remove(p);

        if (_maxHp > 0)
        {
            if (_purified) return;
            // SHIELDED 中に全パネルを剥がし切ったら BREAK（合図）へ。
            // BREAK/EXPOSED 中はパネルが無いので通常ここには来ないが、保険で残数だけ見る。
            if (_phase == BossPhase.Shielded && _panels.Count == 0)
                EnterBreak();
            else
                QueueRedraw();
            return;
        }

        if (_panels.Count == 0 && !_purified)
            Redeem();
        else
            QueueRedraw();
    }

    // ─── 無防備窓サイクルのフェーズ遷移 ───
    private void EnterBreak()
    {
        _phase = BossPhase.Break; _phaseT = 0;
        // 合図演出：画面フラッシュ＋ BREAK! 表示。ミナの煽りセリフは OnBreakCue（弾を止めない字幕）で。
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.Flash();
        FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(0, -14), "BREAK!", FxLayer.Sig2);
        Audio.Instance?.PlaySpell();
        OnBreakCue(); // 派生：ミナの煽りセリフ等（共通実装あり）
        QueueRedraw();
    }

    private void EnterExposed()
    {
        _phase = BossPhase.Exposed; _phaseT = 0;
        _windowDamage = 0;            // 窓キャップを新しい窓ぶんリセット
        _windowCapNotified = false;
        _bodyHitCd = 0;
        // 無防備窓：本体が自機弾を拾うよう監視・マスクを開く（衝突中の変更は遅延設定）。
        SetDeferred(Area2D.PropertyName.Monitoring, true);
        SetCollisionMaskValue(2, true); // 自機弾 layer=2 を拾う
        if (_bodyShape != null)
            _bodyShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, false);
        QueueRedraw();
    }

    private void EnterReclose()
    {
        _phase = BossPhase.Reclose; _phaseT = 0;
        // 本体を再び無敵化（自機弾を拾わない）。
        SetDeferred(Area2D.PropertyName.Monitoring, false);
        SetCollisionMaskValue(2, false);
        OnRecloseLine(); // 派生：キャラ別の弱気セリフを最短表示
        QueueRedraw();
    }

    private void EnterShielded()
    {
        _phase = BossPhase.Shielded; _phaseT = 0;
        if (_panels.Count == 0) SpawnPanels(); // パネル一括再生成
        QueueRedraw();
    }

    // 無防備窓中：本体に当たった自機弾の威力ぶん本体HPを削る。
    private void OnBodyHitByPlayerBullet(Area2D area)
    {
        if (_phase != BossPhase.Exposed || _purified) return;
        if (area is Bullet b && !b.IsEnemy && b.Active)
        {
            GetNodeOrNull<BulletPool>("/root/Pool")?.Despawn(b);

            // 窓キャップ到達後は、この窓では本体HPを削らない（弾の消滅は上で済ませ撃ち心地は残す）。
            // 到達の瞬間だけ "MAX" を1回出して「これ以上は次の窓で」を伝える。
            if (_windowDamage >= ExposedDamageCap)
            {
                if (!_windowCapNotified)
                {
                    _windowCapNotified = true;
                    FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(0, -12), "MAX", FxLayer.Gold, 13);
                }
                return;
            }
            // 本体ヒットのクールダウン中は削らない（同一フレーム多重弾の過剰削りを軽く抑える補助）。
            if (_bodyHitCd > 0) return;

            int dmg = Mathf.Clamp(b.Damage, 1, 4); // ExposedHitDmg=1+ShotDamageBonus を Bullet.Damage 経由で（上限4）

            // 密着ボーナス：自機が本体に PointBlankRange 以内まで踏み込むとクリティカル（約+60%・上限6）。
            // 当たり判定は不変＝近づくこと自体が接触被弾＆濃い弾幕というリスクの対価。
            // 自機が取れない場合は base ダメージにフォールバック（null安全）。
            bool crit = false;
            if (GetTree().GetFirstNodeInGroup("player") is Player pl)
            {
                float d = GlobalPosition.DistanceTo(pl.GlobalPosition);
                if (d <= PointBlankRange)
                {
                    crit = true;
                    dmg = Mathf.Min(PointBlankCap, Mathf.RoundToInt(dmg * PointBlankMult));
                }
            }

            // 窓キャップ：残り許容ぶんへクランプ（密着クリティカルは上限を超えず到達を早めるだけ）。
            dmg = Mathf.Min(dmg, ExposedDamageCap - _windowDamage);
            _windowDamage += dmg;
            _bodyHitCd = BodyHitCd;
            _hp = Mathf.Max(0, _hp - dmg);
            // クリティカルは金色＋一回り大きく＋"!" で「密着が効いている」を視認させる（通常は既存色）。
            if (crit)
                FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(GD.Randf() * 8 - 4, -10), dmg + "!", FxLayer.Gold, 13);
            else
                FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(GD.Randf() * 8 - 4, -8), dmg.ToString(), FxLayer.Sig2);
            OnHpChanged();

            // 手応え：本体に当たった一発ごとに「効いてる」を即・短く返す（当たり判定は不変）。
            // ・発光リング(_Draw)＋本体ヒット専用SE(PlayBossHit)。大威力(>=3)は揺れも足して“刺さった”感を強調。
            //   PlayBossHit は中低域の「刺さる／ドスッ」＝剥離(PlayStrip)・自機被弾(PlayHit)と音域を分け混同回避。
            //   dmg>=3 では重い低音版が鳴り、下の GameCamera.Shake と同期して決定打が映える。
            //   密着クリティカルは dmg が上がるので重い音＋Shake が自然に発火し、リングも気持ち強める。
            _hitFlashT = HitFlashDur;
            _hitFlashMag = crit ? dmg + 2 : dmg;
            Audio.Instance?.PlayBossHit(dmg);
            if (dmg >= 3) GameCamera.Instance?.Shake(1.6f, 0.10f);

            if (_hp <= 0)
            {
                // 窓中の本体撃破。Redeem は被弾シグナル中に走るため監視・形状の無効化は遅延される。
                // マスク書換も衝突シグナルのディスパッチ中（フラッシュ中）なので遅延化する。
                CallDeferred(MethodName.SetCollisionMaskValue, 2, false);
                Redeem();
                return;
            }
            QueueRedraw();
        }
    }

    // 外部（ボム等）から強制浄化。
    // ボス(HPバー方式)はボムで即浄化しない：今あるパネルを全砕き→ BREAK を誘発するだけ。
    public void Purify()
    {
        if (_purified) return;
        if (_maxHp > 0)
        {
            // 無防備窓中／合図中はパネルが無いので何も起きない（直減もしない）。
            foreach (var p in new List<Panel>(_panels))
                p.Shatter();
            return;
        }
        foreach (var p in new List<Panel>(_panels))
            p.Shatter();
        if (!_purified) Redeem();
    }

    // 合図・弱気セリフの派生フック。
    // BREAK 合図は全ボス共通でミナが煽る（who=1）。RECLOSE は派生がキャラ別の弱気セリフを出す。
    // どちらも ShowBossLine 経由＝弾を止めない（テンポ維持）。
    protected virtual void OnBreakCue()
    {
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?
            .ShowBossLine("ミナ", "シールドが、剥がれました! いまです、撃ち抜いて!", UiKit.Mina, BreakCueDur + VulnDur);
    }
    protected virtual void OnRecloseLine() { }

    // RECLOSE セリフを表示するヘルパー（派生から呼ぶ）。サイクルごとに index を進め、超えたら最後を使い回す。
    protected void ShowRecloseLine(string speaker, string text)
    {
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?
            .ShowBossLine(speaker, text, UiKit.Kegare, RecloseLineDur);
    }

    // 進行方向に体を向ける（素材は右向き。flipH=true で左向き）。
    protected void SetSpriteFlip(bool flipH)
    {
        if (_hasBodyTex && _bodySprite != null)
            _bodySprite.FlipH = flipH;
    }

    // ボス徘徊の“見た目だけ”の演出を立ち絵(_bodySprite)へ適用する（BossMover 経由）。
    // visualOffset=呼吸/浮遊の微小オフセット、lean=進行方向への傾き(rad)、faceLeft=向き。
    // ★当たり判定（本体 Area2D の GlobalPosition と _bodyShape）は一切動かさない＝弾避けの公平性を保つ。
    // 立ち絵が無い（プレースホルダ図形の）ボスでは何もしない。
    protected void ApplyBossMotion(Vector2 visualOffset, float lean, bool faceLeft)
    {
        if (!_hasBodyTex || _bodySprite == null) return;
        _motionOffset = visualOffset; // 呼吸/浮遊。pop の持ち上げはこれへ加算するため保持。
        // 差し替えアニメ中は _PhysicsProcess 側が Position/Scale を握る（pop の持ち上げを潰さない）。
        if (!_swapAnim)
            _bodySprite.Position = visualOffset;
        _bodySprite.Rotation = lean;
        _bodySprite.FlipH = faceLeft;
    }

    // HPが変化した（HUDバー更新用フック）。
    protected virtual void OnHpChanged() { }

    // 改心処理（消さない。味方化して残る）。
    private void Redeem()
    {
        if (_purified) return;
        _purified = true;
        RemoveFromGroup("enemies");

        // 戦闘終了の瞬間：画面に残った自機の弾を消す（改心の会話に弾が飛び続けないように）。
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnPlayerBullets();

        // 接触で自機を傷つけないようにする。浄化は被弾シグナル中に走ることがあるため遅延設定。
        SetDeferred(Area2D.PropertyName.Monitorable, false);
        if (_bodyShape != null)
            _bodyShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);

        _flashing = true;
        _flashT = 0;

        // スコア＋コンボ（連鎖＝やさしさの広がり）。
        GetNodeOrNull<GameManager>("/root/Game")?.AddPurify(Points);

        // 浄化バースト演出＋やさしい言葉（バリエーション）＋浄化音（届いた余韻）
        // 改心が確定する一拍：止め(Hitstop)＋光(PurifyBurst)＋フラッシュ を同フレームで揃える。
        GameCamera.Instance?.Hitstop(HitstopDur);
        FxLayer.Instance?.PurifyBurst(GlobalPosition);
        Audio.Instance?.PlayPurify();
        FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(0, -10), PickKindWord(), FxLayer.Sig2);

        // やさしさの波紋（連鎖浄化のトリガー）。
        // Redeem は被弾/パネル砕けのシグナル（物理クエリのフラッシュ中）から呼ばれることがある。
        // その最中に Ripple を即 AddChild すると Ripple._Ready が監視状態やコリジョン形状を
        // 物理フラッシュ中に書き換え（"Can't change this state while flushing queries"）、
        // 連鎖浄化が多発する場面で物理サーバを壊して落ちる。生成はフラッシュ後へ遅延する。
        var parent = GetParent();
        if (parent != null)
        {
            var ripple = new Ripple { Position = Position }; // 親が同じ＝同じ座標系のローカル位置をそのまま使う
            parent.CallDeferred(Node.MethodName.AddChild, ripple);
        }

        // 3段階対応：Cry の尺が設定されていれば先に大泣きを見せてから笑顔へ。
        // 専用立ち絵が無いボス（こはる等）でも会話に入れるよう、CryHoldDur のみで判定する
        //（SwapBody は内部で _hasBodyTex を確認するため、立ち絵が無ければ素通りする）。
        if (CryHoldDur > 0)
        {
            SwapBody(CryTexPath);
            _crying = true;
            _cryT = 0;
            OnCryStart();
        }
        else
        {
            SwapBody(PostTexPath);
            GrantFollower();
        }

        QueueRedraw();
    }

    // 本体スプライトを“溶けるように”差し替える（クロスフェード＋squash→pop）。
    // 旧テクスチャを別 Sprite2D(_fadeSprite) に退避してα落とししつつ、本体(新)をα上げ。
    // 同時に squash→pop（Scale を一瞬ふくらませて弾み、見た目を少し持ち上げて戻す）を起動。
    // ★テクスチャ差し替え／再スケールの基準だけ確定し、実アニメは _PhysicsProcess(TickSwapAnim) が進める。
    // ★当たり判定は触らない：動かすのは _bodySprite と _fadeSprite の Transform/Modulate だけ。
    private void SwapBody(string path)
    {
        if (!_hasBodyTex || string.IsNullOrEmpty(path)) return;
        var t = ResourceLoader.Load<Texture2D>(path);
        if (t == null) return;

        // 旧テクスチャをそのままの見た目で退避（同じ Transform/Flip/ZIndex）し、α落とし用に使う。
        var old = _bodySprite.Texture;
        if (old != null)
        {
            _fadeSprite?.QueueFree();
            _fadeSprite = new Sprite2D
            {
                Texture = old,
                Centered = true,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                ZIndex = _bodySprite.ZIndex,
                FlipH = _bodySprite.FlipH,
                Position = _bodySprite.Position,
                Rotation = _bodySprite.Rotation,
                Scale = _bodySprite.Scale,
            };
            AddChild(_fadeSprite);
        }

        // 本体を新テクスチャへ。基準スケールを更新し、α0 から上げ始める。
        _bodySprite.Texture = t;
        _baseScale = BodyDisplayH / t.GetHeight();
        _bodySprite.Scale = new Vector2(_baseScale, _baseScale);
        _bodySprite.SelfModulate = new Color(1f, 1f, 1f, _fadeSprite != null ? 0f : 1f);

        // squash→pop を起動（_PhysicsProcess で進める）。
        _swapAnim = true;
        _swapAnimT = 0;
    }

    // squash→pop ＋ クロスフェードを 1 フレームぶん進める。
    // squash: BaseScale×SquashScale から BaseScale へ Back/Out 風に弾ませる。
    // pop:    見た目を PopLiftPx 持ち上げて戻す（呼吸オフセット _motionOffset に加算＝当たり判定は不変）。
    private void TickSwapAnim(double delta)
    {
        if (!_swapAnim) return;
        _swapAnimT += delta;
        float u = (float)Mathf.Clamp(_swapAnimT / SwapAnimDur, 0, 1);

        // Back/Out 風：行き過ぎてから戻す。t=0 で +(SquashScale-1)、t=1 で ±0 に収束。
        float over = BackOut(u);                 // 0→1（途中で >1 にオーバーシュート）
        float scaleMul = Mathf.Lerp(SquashScale, 1f, over);
        _bodySprite.Scale = new Vector2(_baseScale * scaleMul, _baseScale * scaleMul);

        // pop の持ち上げ：序盤に最大、終盤で 0（sin の山）。呼吸オフセットへ加算。
        float lift = -PopLiftPx * Mathf.Sin(u * Mathf.Pi);
        _bodySprite.Position = _motionOffset + new Vector2(0f, lift);

        // クロスフェード：旧(_fadeSprite)をα落とし、新(_bodySprite)をα上げ。
        if (_fadeSprite != null)
        {
            float fa = (float)Mathf.Clamp(_swapAnimT / SwapFadeDur, 0, 1); // フェード進行
            _bodySprite.SelfModulate = new Color(1f, 1f, 1f, fa);
            _fadeSprite.SelfModulate = new Color(1f, 1f, 1f, 1f - fa);
            _fadeSprite.Scale = _bodySprite.Scale; // 同じ squash に乗せて一体に揺らす
            _fadeSprite.Position = _bodySprite.Position;
            if (fa >= 1f) { _fadeSprite.QueueFree(); _fadeSprite = null; }
        }

        if (u >= 1f)
        {
            _swapAnim = false;
            _bodySprite.Scale = new Vector2(_baseScale, _baseScale);
            _bodySprite.Position = _motionOffset;
            _bodySprite.SelfModulate = Colors.White;
            if (_fadeSprite != null) { _fadeSprite.QueueFree(); _fadeSprite = null; }
        }
    }

    // Back/Out 風イージング（行き過ぎてから 1 へ収束）。
    private static float BackOut(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    // 救った人を algo のフォロワー（味方オプション）にする。派生で上書き可（ボス＝ヒカゲ強化）。
    protected virtual void GrantFollower()
    {
        var players = GetTree().GetNodesInGroup("player");
        if (players.Count > 0 && players[0] is Player pl)
            _becameFollower = pl.AddFollower(GlobalPosition); // フォロワー化したら本体は退場せず引き継ぐ
    }

    // 大泣き演出の開始／終了フック（派生でセリフ等に使う）。
    protected virtual void OnCryStart() { }
    protected virtual void OnCryEnd() { }

    // 手動送りで会話を終えたとき、Cry（その場停止）を即終了して笑顔へ着地。
    protected void EndCryNow()
    {
        if (!_crying) return;
        _crying = false;
        SwapBody(PostTexPath);
        OnCryEnd();
        GrantFollower();
    }

    public override void _PhysicsProcess(double delta)
    {
        // 差し替えアニメ（クロスフェード＋squash→pop）は状態に関わらず常に進める。
        if (_swapAnim) { TickSwapAnim(delta); QueueRedraw(); }

        if (_flashing)
        {
            _flashT += delta;
            if (_flashT >= FlashDur) _flashing = false;
            QueueRedraw();
        }

        // 大泣き中はその場に留まり、CryHoldDur 経過で笑顔へ着地。
        if (_crying)
        {
            _cryT += delta;
            if (_cryT >= CryHoldDur)
            {
                _crying = false;
                SwapBody(PostTexPath);
                OnCryEnd();
                GrantFollower();
            }
            return;
        }

        if (_purified)
        {
            // フォロワー化した本人は、その場に湧いた Follower が見た目を引き継ぐので本体は即退場
            //（救った娘が去りつつ別の娘がくっつく“二重表示”を防ぐ＝救った本人がそのまま付くように見える）。
            if (_becameFollower) { QueueFree(); return; }
            // それ以外：笑顔の味方コメントとしてゆっくり左へ流れて退場。
            GlobalPosition += new Vector2(-30f * (float)delta, 0f);
            if (GlobalPosition.X < -24f) QueueFree();
            return;
        }

        // 無防備窓サイクルの進行（BubblePaused でも止めない＝合図/窓が固まらないように）。
        if (_maxHp > 0) TickBossPhase(delta);

        if (Hud.BubblePaused) return; // 吹き出し表示中は動かない（襲ってこない）

        UpdateMovement(delta);
        if (GlobalPosition.X < -24f) QueueFree();
    }

    // BREAK→EXPOSED→RECLOSE→SHIELDED の尺管理。SHIELDED 中は何もしない（パネル待ち）。
    private void TickBossPhase(double delta)
    {
        if (_phase == BossPhase.Shielded) return;
        _phaseT += delta;
        switch (_phase)
        {
            case BossPhase.Break:
                if (_phaseT >= BreakCueDur) EnterExposed();
                else QueueRedraw();
                break;
            case BossPhase.Exposed:
                if (_hitFlashT > 0) _hitFlashT -= delta; // 被弾発光の減衰
                if (_bodyHitCd > 0) _bodyHitCd -= delta; // 本体ヒットCDの消化
                QueueRedraw(); // 発光/明滅（_Draw）を更新し「今は殴れる」を可視化
                if (_phaseT >= VulnDur) EnterReclose();
                break;
            case BossPhase.Reclose:
                // 弱気セリフ(RecloseLineDur)を見せ、RespawnGap 置いてパネルを一括再生成＝SHIELDED へ。
                if (_phaseT >= RecloseLineDur + RespawnGap) EnterShielded();
                break;
        }
    }

    protected virtual void UpdateMovement(double delta) { }

    public override void _Draw()
    {
        // 改心フラッシュ（やさしい色：淡ピンク→淡紫に着地）
        if (_flashing)
        {
            float t = (float)(_flashT / FlashDur);
            var c = new Color(1f, 0.85f, 0.92f).Lerp(new Color(0.79f, 0.72f, 0.94f), t); // 淡ピンク→淡紫
            DrawCircle(Vector2.Zero, BodyRadius + 10f * (1f - t), new Color(c.R, c.G, c.B, 0.6f * (1f - t)));
        }

        // 合図・無防備窓の本体演出（「今は殴れる」の可視化）。
        if (!_purified && _maxHp > 0)
        {
            if (_phase == BossPhase.Break)
            {
                // タメ：白く膨らむ合図リング。
                float t = (float)(_phaseT / BreakCueDur);
                DrawCircle(Vector2.Zero, BodyRadius + 4f + 18f * t, new Color(1f, 1f, 1f, 0.5f * (1f - t)));
            }
            else if (_phase == BossPhase.Exposed)
            {
                // 露出中：黄金の明滅オーラ。終了 VulnWarnLead 秒前から点滅を速めて終了を予告。
                double rem = VulnDur - _phaseT;
                bool warn = rem <= VulnWarnLead;
                float hz = warn ? 9f : 3.2f;
                float pulse = 0.5f + 0.5f * Mathf.Sin((float)_phaseT * hz * Mathf.Tau);
                // 終了予告中はオーラを金→白へ寄せて「閉じる」を色でも伝える（明滅速度だけだと見落としやすい）。
                var aura = warn
                    ? new Color(1f, 0.97f, 0.85f, 0.30f + 0.45f * pulse)
                    : new Color(1f, 0.86f, 0.36f, 0.30f + 0.45f * pulse);
                DrawCircle(Vector2.Zero, BodyRadius + 6f + 3f * pulse, aura);
                DrawArc(Vector2.Zero, BodyRadius + 9f, 0, Mathf.Tau, 32, new Color(1f, 0.95f, 0.6f, 0.5f * pulse), 1.5f);
                // スイートスポット：PointBlankRange の薄い金リング＝「ここまで近づくと大ダメージ」を学習させる。
                // 弾を隠さない淡さ＆破線風（点描）で控えめに。当たり判定とは無関係の見せかけ。
                DrawArc(Vector2.Zero, PointBlankRange, 0, Mathf.Tau, 48,
                        new Color(1f, 0.84f, 0.32f, 0.10f + 0.06f * pulse), 1f);
                // 終了予告：窓が「閉じてくる」収縮リング（外→内へ詰まる＝残り時間を直感的に見せる）。
                if (warn)
                {
                    float closing = (float)(rem / VulnWarnLead); // 1→0
                    float rr = BodyRadius + 9f + 16f * closing;
                    DrawArc(Vector2.Zero, rr, 0, Mathf.Tau, 32, new Color(1f, 1f, 1f, 0.55f * closing), 2f);
                }
                // 被弾の手応え：撃ち込んだ瞬間の白い衝撃リング（短く・即・尾を引かない）。
                if (_hitFlashT > 0)
                {
                    float h = (float)(_hitFlashT / HitFlashDur);     // 1→0
                    float rr = BodyRadius + 4f + (10f + 4f * _hitFlashMag) * (1f - h);
                    DrawCircle(Vector2.Zero, rr, new Color(1f, 1f, 1f, 0.5f * h));
                    DrawArc(Vector2.Zero, rr, 0, Mathf.Tau, 28, new Color(1f, 1f, 1f, 0.85f * h), 2f);
                }
            }
        }

        // スプライトが無い時だけプレースホルダ図形を描く
        if (!_hasBodyTex)
            DrawPerson(_purified ? new Color(1f, 0.86f, 0.62f) : new Color(0.55f, 0.6f, 0.78f), happy: _purified);

        // 波紋射程プレビュー（残り1枚＝剥がし切ると波紋がここまで届く）
        if (!_purified && _panels.Count == 1)
            DrawArc(Vector2.Zero, Ripple.MaxRadius, 0, Mathf.Tau, 40, new Color(0.7f, 0.92f, 1f, 0.28f), 1f);
    }

    private static readonly string[] KindWords = { "ありがとう", "だいじょうぶ", "きみは悪くないよ", "ごめんね", "また話そう" };
    private static readonly RandomNumberGenerator _kw = new RandomNumberGenerator();
    private static string PickKindWord() => KindWords[_kw.RandiRange(0, KindWords.Length - 1)];

    private void DrawPerson(Color body, bool happy)
    {
        DrawCircle(new Vector2(0, 2), BodyRadius, body);                       // 体
        DrawCircle(new Vector2(0, -6), 5f, new Color(1f, 0.92f, 0.85f));       // 頭
        var eye = happy ? new Color(0.2f, 0.1f, 0.1f) : new Color(0.1f, 0.1f, 0.2f);
        float ey = happy ? -7f : -5f;
        DrawCircle(new Vector2(-2, ey), 0.9f, eye);
        DrawCircle(new Vector2(2, ey), 0.9f, eye);
        if (happy)
            DrawArc(new Vector2(0, -5), 2.2f, 0.2f, Mathf.Pi - 0.2f, 8, new Color(0.6f, 0.25f, 0.25f), 1f); // 笑み
    }
}
