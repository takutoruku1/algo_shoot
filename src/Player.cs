using Godot;
using System.Collections.Generic;

// Player : Area2D。グループ "player" に追加。
// 移動(通常110 / 低速50 px/s)、連射(Pool経由・右方向+260・上下2way)、被弾無敵点滅、TakeHit、Lives。
// W0 では残機を減らさず「練習中」扱い（ゲームオーバーにしない）。
// 衝突: layer=1, mask=12（敵=4 と 敵弾=8 を検出）。
// 当たり判定は半径2px の極小（胸の紫十字相当）。可視ヒットボックス点を _Draw で小さく描く。
public partial class Player : Area2D
{
    // 速度
    private const float NormalSpeed = 150f;
    private const float FocusSpeed = 65f;

    // 連射
    private const float FireInterval = 0.11f;
    private float _fireCooldown = 0f;

    // 当たり半径（極小）
    private const float HitRadius = 2f;
    // グレイズ半径（かすり判定の広さ）
    private const float GrazeRadius = 11f;

    // ボム入力のエッジ検出用
    private bool _bombHeld = false;

    // ショットモード切替（V / Pad B）のエッジ検出。_modeInit で初回に HUD へ現在モードを通知。
    private bool _modeHeld = false;
    private bool _modeInit = false;

    // ヒカゲ専用スキル（フォロワーにヒカゲがいる時だけ・Cキー）
    private bool _specialHeld = false;
    private bool _kindHeld = false; // やさしさ全開の手動発動エッジ検出
    private float _specialCd = 0f;
    private const float SpecialCdMax = 7f;

    // フォロワー（浄化した人＝味方オプション）
    private readonly List<Follower> _followers = new List<Follower>();
    private const int MaxFollowers = 4;
    private const int SavedPerFollower = 3; // この人数を救うごとに1体増える（増加を緩やかに）
    private int _savedCount = 0;
    private int _shotParity = 0;            // フォロワーの発射間引き用
    private bool _overload = false;         // やさしさ全開中か（GameManager）
    private static readonly Vector2[] FollowerSlots =
    {
        new Vector2(-14, -11), new Vector2(-14, 11),
        new Vector2(-26, -5), new Vector2(-26, 5),
    };

    // ヒカゲを仲間に。フォロワーが満員(4)なら1体をヒカゲに強化、空きがあれば強化フォロワーとして追加。
    public void AddHikageFollower(Vector2 globalFromPos)
    {
        // すでにヒカゲがいるなら重複させない
        foreach (var f in _followers)
            if (f.IsHikage) return;

        if (_followers.Count >= MaxFollowers)
        {
            // 満員：通常フォロワーの1体をヒカゲに強化
            foreach (var f in _followers)
            {
                if (!f.IsHikage)
                {
                    f.PromoteToHikage();
                    FxLayer.Instance?.PurifyBurst(f.GlobalPosition);
                    return;
                }
            }
            return;
        }

        // 空きあり：強化フォロワーとして新規追加
        var nf = new Follower { SlotOffset = FollowerSlots[_followers.Count] };
        AddChild(nf);
        nf.Position = ToLocal(globalFromPos);
        nf.PromoteToHikage();
        FxLayer.Instance?.PurifyBurst(nf.GlobalPosition);
        _followers.Add(nf);
    }

    // 人を救うたびに呼ばれる（Enemy.Redeem）。一定人数ごとにフォロワーが1体増える。
    // 救った本人がフォロワー化したら true（呼び出し元の Enemy はその本体を退場させずフォロワーに引き継ぐ）。
    public bool AddFollower(Vector2 globalFromPos)
    {
        _savedCount++;
        if (_followers.Count >= MaxFollowers) return false;
        if (_savedCount % SavedPerFollower != 0) return false; // 3人救うごとに1体
        var f = new Follower { SlotOffset = FollowerSlots[_followers.Count] };
        AddChild(f);
        f.Position = ToLocal(globalFromPos); // 浄化した位置（＝救った本人の場所）から飛んでくる
        _followers.Add(f);
        return true;
    }

    // プレイ領域
    private const float MinX = 0f;
    private const float MaxX = 384f;
    private const float MinY = 0f;
    private const float MaxY = 216f;

    // 残機
    public int Lives { get; private set; } = 3;

    // 自機の見た目スキン（"mina"=銀髪メイド / "boy"=少年）。AddChild 前にセットする。
    public string Skin = "mina";

    // 無敵・点滅
    private bool _invincible = false;
    private float _invincibleTimer = 0f;
    private const float InvincibleDuration = 1.2f;
    private float _blinkPhase = 0f;

    // 表示用スプライト（algo.png）。読み込めない場合は null のまま → _Draw フォールバック。
    private Sprite2D _sprite = null!;
    private bool _hasTexture = false;

    private bool _focus = false; // 低速（Shift）中か。ヒットボックス強調表示に使う。

    // 「今かすった」を自機側のリングで一瞬光らせる残光（1→0 へ減衰）。FxLayer.Graze の閃光と併用。
    private float _grazeFlash = 0f;
    private const float GrazeFlashDecay = 6f; // 約0.17秒で消える（即・短く＝テンポを殺さない）

    // ミナの汚染ティント（0=澄んだ光 → 1=黒く濁る）。スプライトの SelfModulate にのみ掛け、
    // 被弾点滅（Modulate のα）とは独立に作用させる。
    private float _corruption = 0f;
    private static readonly Color CleanTint = new Color(1f, 1f, 1f);
    private static readonly Color MurkTint = new Color(0.42f, 0.40f, 0.52f); // 濁った藍鼠
    // やさしさ全開のオーラ（金色に発光・脈動）。SelfModulate を 1 超で持ち上げて光らせる。
    private static readonly Color OverloadTint   = new Color(1.15f, 1.05f, 0.72f);
    private static readonly Color OverloadTintHi = new Color(1.5f, 1.35f, 0.9f);
    public void SetCorruption(float level) => _corruption = Mathf.Clamp(level, 0f, 1f);

    // チュートリアル（ステージ0）の自機系ステップで、自機を“光らせて”目立たせる（既存グレイズ残光を流用）。
    // World↔設計座標変換の事故を避けるため、Hudの暗転穴ではなく自機側の発光で注意を引く方式。
    public void TutorialGlow() => _grazeFlash = Mathf.Max(_grazeFlash, 0.6f);

    // 常時ふわふわ浮遊（スプライトのみ上下に揺らす。当たり判定点は固定）
    private float _bobTime = 0f;
    private const float BobSpeed = 3.2f; // 角速度(rad/s) 約2秒周期
    private const float BobAmp = 2.0f;   // 揺れ幅(px)

    // 移動バンク（進行方向へ体を傾け＋わずかに先行。見た目=_spriteのみ／当たり判定は不動）。
    // _lean を入力方向へ指数補間して慣性を持たせる＝予備動作（タメ）と余韻（揺り戻し）が自動で出る。
    private Vector2 _lean = Vector2.Zero;
    private static readonly float BankX = Mathf.DegToRad(8f);  // 前後（横移動）の前傾
    private static readonly float BankY = Mathf.DegToRad(13f); // 上下（縦移動）のバンク
    private const float LeadPx = 2.5f;        // 進行方向への体の先行量(px)
    private const float LeanResponse = 9f;    // 慣性の追従の速さ（大きいほど機敏／小さいほどたゆたう）
    private const float FocusLeanMul = 0.45f; // 低速(Shift)時はバンクを抑える＝丁寧さを画で見せる

    // ───────── 回避（ドッジ）─────────
    // 入力＝ALT / パッド L3(LeftStick)。短い無敵で弾を「すり抜ける」攻めの回避。
    // 値は控えめ＝乱用させず、ここぞの一回が気持ちいい範囲に置く（tunable）。
    private const float DodgeIFrame   = 0.45f;  // 無敵時間（この主旨どおり回避中は被弾しない）
    private const float DodgeDuration = 0.55f;  // 回避モーション全体の長さ（変位＋余韻）。ゆっくり見せる
    private const float DodgeDistance = 64f;    // ダッシュ総変位(px)
    private const float DodgeCooldown = 0.8f;   // 再回避までのクールダウン
    private const float DodgeAntic    = 0.05f;  // アンティシペーション（一瞬の逆タメ）秒
    private const int   DodgeSpins    = 3;      // 回避中に回す回転数（縦軸ピルエット＝トリプルアクセル感の3）。tunable。
    private const float DodgeLift     = 8f;     // ジャンプスピンの軽い浮き(px・上方向)。やり過ぎない。
    private const float DodgeTrailGap = 0.03f;  // 残像を置く間隔(秒)＝最大10枚程度
    private const float DodgeTrailTtl = 0.18f;  // 残像1枚が消えるまで(秒)
    private const int   DodgeTrailMax = 8;      // 同時に存在する残像の上限（プール）

    private bool _dodgeHeld = false;            // 入力エッジ検出
    private float _dodgeTimer = 0f;             // 残り回避時間（>0で回避中）
    private float _dodgeInv = 0f;               // 残り回避無敵時間（>0で被弾しない）
    private float _dodgeCd = 0f;                // 残りクールダウン
    private Vector2 _dodgeDir = Vector2.Zero;   // 回避の進行方向（正規化）。その場回避では Zero＝変位なし。
    private bool _dodgeInPlace = false;         // 方向入力なしの回避＝その場でスピンのみ（ダッシュ変位ゼロ）。
    private Vector2 _dodgeFrom = Vector2.Zero;  // 回避開始位置
    private float _dodgeTrailAccum = 0f;        // 残像スポーン用タイマ
    private bool _dodgeFlip = false;            // 回避中のスプライト左右反転（スピンの8ステップで真横以降のフレームを FlipH 流用するために使う）

    // HUD・チュートリアル向けの公開アクセサ（挙動には一切影響しない読み取り専用情報）。
    public bool DodgeReady => _dodgeCd <= 0f;   // クールダウンが明けて今すぐ回避できるか（HUD操作ガイドの点灯に使う）
    public int  DodgeCount { get; private set; } // 回避を実行した累計回数（チュートリアルがベースライン比較で実行検出に使う）
    public int  BombCount { get; private set; }  // ボムを発動した累計回数（練習モードでは残数が減らないのでチュートリアルはこの増分で発動検出）
    private float _dodgeSpinSign = 1f;          // スピンの向き（+1=00→01→02… / -1=逆回り）。回避方向から決める。
    private float _baseScaleX = 1f;             // 素の横スケール（高さ正規化値）。フレーム差し替えのたびにこの基準で再計算する。
    private int _dodgeGrazeCount = 0;           // 今回の回避でよけた弾数（farming防止のCap判定用）。TryDodge でリセット。
    private const int DodgeGrazeCap = 12;       // 1回の回避で報酬対象にする弾数の上限（壁に突っ込んで無限に稼げないように）
    // 通常／回避フレームのテクスチャは _Ready で一度だけロードしてキャッシュ（毎フレームLoad禁止）。
    private Texture2D _idleTex = null!;
    // 回転各アングルの差分イラスト（0/45/90/135/180°）。225/270/315° は FlipH で 03/02/01 を流用。
    // boyスキン等で読めない場合は要素が null。_spinReady で全周ロード成否を持つ。
    private readonly Texture2D[] _spinTex = new Texture2D[5];
    private bool _spinReady = false;            // 5枚すべて読めたか（false なら従来の idle 単一フレームへフォールバック）
    private readonly List<Sprite2D> _trail = new List<Sprite2D>(); // 残像スプライトのプール

    // Pool 取得用キャッシュ
    private BulletPool _pool = null!;

    // GameManager キャッシュ（恒久強化の効果を毎フレーム参照する）
    private GameManager _game = null!;
    // 当たり判定の実効半径（回避域強化で縮小）。_Ready で確定。
    private float _hitR = HitRadius;

    public override void _Ready()
    {
        AddToGroup("player");

        // GameManager をキャッシュ（残機・恒久強化の効果取得）。
        _game = GetNodeOrNull<GameManager>("/root/Game")!;

        // 難易度＋恒久強化（最大♥）に応じた残機。
        Lives = _game?.StartLives ?? 3;

        // 回避域強化で当たり判定を縮小。
        _hitR = HitRadius * (_game?.HitRadiusMul ?? 1f);

        // 衝突レイヤー: layer=1, mask=12（敵=4, 敵弾=8）
        CollisionLayer = 1;
        CollisionMask = 12;
        Monitoring = true;
        Monitorable = true;

        // 当たり判定（CircleShape2D）。半径は回避域強化で縮む。
        var shape = new CollisionShape2D
        {
            Name = "HitShape",
            Shape = new CircleShape2D { Radius = _hitR }
        };
        AddChild(shape);

        // テクスチャ読み込み（失敗時は _Draw フォールバック）
        // ドット絵スプライトを優先。無ければ透過カットアウト→元イラストにフォールバック。
        var tex = (Skin == "boy" ? ResourceLoader.Load<Texture2D>("res://char/shonen_idle.png") : null)
                  ?? ResourceLoader.Load<Texture2D>("res://char/mina_idle.png")
                  ?? ResourceLoader.Load<Texture2D>("res://char/algo_idle.png")
                  ?? ResourceLoader.Load<Texture2D>("res://char/algo_cutout.png")
                  ?? ResourceLoader.Load<Texture2D>("res://char/algo.png");
        if (tex != null)
        {
            _hasTexture = true;
            _idleTex = tex; // 回避終了時に戻す通常テクスチャ
            _sprite = new Sprite2D
            {
                Name = "Sprite",
                Texture = tex,
                Centered = true,
                // 背景に合わせ、なめらか高精細で小さく表示（リニア縮小）
                TextureFilter = CanvasItem.TextureFilterEnum.Linear
            };
            // 表示高さ約36px（弾幕向けに小さめ）
            float texHeight = tex.GetHeight();
            if (texHeight > 0)
            {
                float scale = 36f / texHeight;
                _sprite.Scale = new Vector2(scale, scale);
                _baseScaleX = scale; // 縦軸スピンの cos 駆動はこの素値を基準にする（向きは FlipH 固定＝符号は常に正）。
            }
            AddChild(_sprite);
        }

        // 回避スピンのフレーム5枚を一度だけロードしてキャッシュ（mina のみ。boy 等は絵が無い）。
        // 5枚すべて読めたときだけ _spinReady=true＝本物のフレームアニメで回す。1枚でも欠けたら
        // _spinReady=false のまま idle 単一フレームへフォールバック（落とさない）。
        if (Skin != "boy")
        {
            bool allLoaded = true;
            for (int i = 0; i < _spinTex.Length; i++)
            {
                _spinTex[i] = ResourceLoader.Load<Texture2D>($"res://char/mina_spin_{i:00}.png");
                if (_spinTex[i] == null) allLoaded = false;
            }
            _spinReady = allLoaded;
        }

        // 被弾検出（敵 / 敵弾）
        AreaEntered += OnAreaEntered;

        // グレイズ判定エリア（自機より広い円。敵弾(=layer8)のかすりを検出）
        var grazeArea = new Area2D
        {
            Name = "GrazeArea",
            CollisionLayer = 0,
            CollisionMask = 8, // 敵弾
            Monitoring = true,
            Monitorable = false,
        };
        grazeArea.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = GrazeRadius } });
        AddChild(grazeArea);
        grazeArea.AreaEntered += OnGrazeAreaEntered;

        // Pool 取得
        _pool = GetNode<BulletPool>("/root/Pool");

        ZIndex = 10;

        // 開始/リスタート直後の被弾を防ぐスポーン無敵（点滅）
        _invincible = true;
        _invincibleTimer = 1.5f;
        _blinkPhase = 0f;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // 操作ガイドの KB/パッド出し分け用に、直近デバイスを毎フレーム判定。
        Pad.PollDevice();

        // 移動入力。会話中（吹き出し表示中）は動けない。
        Vector2 dir = Vector2.Zero;
        if (!Hud.BubblePaused)
        {
            dir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
            // WASD でも動けるように。矢印キーは「↑+←+Z」など3キー同時押しが
            // 安価なキーボードでゴースト（入力が消える）するため、代替手段を用意する。
            Vector2 wasd = new Vector2(
                (Input.IsKeyPressed(Key.D) ? 1f : 0f) - (Input.IsKeyPressed(Key.A) ? 1f : 0f),
                (Input.IsKeyPressed(Key.S) ? 1f : 0f) - (Input.IsKeyPressed(Key.W) ? 1f : 0f));
            if (wasd != Vector2.Zero) dir = wasd;
            dir = dir.LimitLength(1f);
        }
        // 低速＝Shift / 肩ボタン(L1・R1)
        bool focus = Input.IsKeyPressed(Key.Shift) || Pad.Pressed(JoyButton.LeftShoulder) || Pad.Pressed(JoyButton.RightShoulder);
        _focus = focus;
        // 機動力強化で移動速度UP。
        float speed = (focus ? FocusSpeed : NormalSpeed) * (_game?.MoveSpeedMul ?? 1f);

        // 回避入力＝ALT（左Alt想定）/ パッド L3。空き弾の無い瞬間に「攻めで抜ける」短い無敵ダッシュ。
        // 方向は移動入力があればその方向へ変位ダッシュ、無ければその場回避（変位ゼロ＝スピン＆無敵だけ）。
        bool dodgeKey = Input.IsKeyPressed(Key.Alt) || Pad.Pressed(JoyButton.LeftStick);
        if (dodgeKey && !_dodgeHeld && !Hud.BubblePaused)
            TryDodge(dir);
        _dodgeHeld = dodgeKey;
        if (_dodgeCd > 0f) _dodgeCd -= dt;
        if (_dodgeInv > 0f) _dodgeInv -= dt;

        Vector2 pos;
        if (_dodgeTimer > 0f)
        {
            // 回避中は入力移動を無視し、ダッシュ軌道で位置を駆動する。
            _dodgeTimer -= dt;
            float t = Mathf.Clamp(1f - _dodgeTimer / DodgeDuration, 0f, 1f); // 0→1
            // アンティシペーション（最初だけ逆方向にわずかに引く）→ イーズアウトで一気に滑る→ 余韻。
            float anticK = DodgeAntic / DodgeDuration;
            float disp;
            if (t < anticK)
                disp = -DodgeDistance * 0.12f * (t / anticK);            // 逆タメ
            else
            {
                float u = (t - anticK) / (1f - anticK);                  // 0→1
                disp = DodgeDistance * (1f - (1f - u) * (1f - u));       // ease-out quad（フォロースルー）
            }
            pos = _dodgeFrom + _dodgeDir * disp;
            // 残像トレイル（一定間隔で薄い分身を置く。テクスチャはキャッシュ済み＝Load しない）。
            _dodgeTrailAccum += dt;
            if (_dodgeTrailAccum >= DodgeTrailGap)
            {
                _dodgeTrailAccum = 0f;
                SpawnTrail();
            }
            if (_dodgeTimer <= 0f) EndDodge();
        }
        else
        {
            pos = GlobalPosition + dir * speed * dt;
        }

        // プレイ領域内にクランプ
        pos.X = Mathf.Clamp(pos.X, MinX, MaxX);
        pos.Y = Mathf.Clamp(pos.Y, MinY, MaxY);
        GlobalPosition = pos;
        // 残像は自機に追従しない独立座標なので、毎フレーム減衰させる。
        UpdateTrail(dt);

        // ショット
        if (_fireCooldown > 0f)
            _fireCooldown -= dt;

        // やさしさ全開なら連射が速くなる。発動の瞬間（立ち上がり）にカタルシス演出を出す。
        bool nowOverload = _game?.IsOverload ?? false;
        if (nowOverload && !_overload) OnOverloadStart();
        _overload = nowOverload;

        // ショット＝Z / Aボタン。会話中（吹き出し表示中）は不可
        // 初回に HUD へ現在モードを通知（HUD の _Ready 順に依存しないよう最初の物理フレームで）。
        if (!_modeInit && _game != null)
        {
            if (!_game.IsModeUnlocked(_game.SelectedShotMode)) _game.SelectedShotMode = GameManager.ShotMode.Rapid;
            (GetTree().GetFirstNodeInGroup("hud") as Hud)?.SetShotMode(_game.SelectedShotMode, false);
            _modeInit = true;
        }

        // ショットモード切替＝V / Pad B。解放済みモードを循環。会話中は不可。
        bool modeKey = Input.IsKeyPressed(Key.V) || Pad.Pressed(JoyButton.B);
        if (modeKey && !_modeHeld && !Hud.BubblePaused && _game != null)
        {
            var nm = _game.NextUnlockedMode(_game.SelectedShotMode);
            if (nm != _game.SelectedShotMode)
            {
                _game.SelectedShotMode = nm;
                (GetTree().GetFirstNodeInGroup("hud") as Hud)?.SetShotMode(nm, true);
            }
        }
        _modeHeld = modeKey;

        bool shoot = (Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A)) && !Hud.BubblePaused;
        if (shoot && _fireCooldown <= 0f)
        {
            Fire();
            // 連射速度強化で発射間隔を短縮（全開中は従来どおり最速）。ホーミングは強すぎるので間隔を広げる（×1.7）。
            float modeMul = (_game?.SelectedShotMode == GameManager.ShotMode.Homing) ? 1.7f : 1f;
            _fireCooldown = _overload ? 0.07f : FireInterval * (_game?.FireIntervalMul ?? 1f) * modeMul;
        }

        // ボム（X）: 押した瞬間だけ発動
        // ボム＝X / Xボタン（□）
        bool bombKey = Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.X);
        if (bombKey && !_bombHeld && !Hud.BubblePaused)
            TryBomb();
        _bombHeld = bombKey;

        // ヒカゲ専用スキル（C）: ヒカゲが仲間にいる時だけ・クールダウン制
        if (_specialCd > 0f) _specialCd -= dt;
        // スキル＝C / Yボタン（△）
        bool specialKey = Input.IsKeyPressed(Key.C) || Pad.Pressed(JoyButton.Y);
        if (specialKey && !_specialHeld && !Hud.BubblePaused)
            TryHikageSpecial();
        _specialHeld = specialKey;
        // HUDにスキル状態を反映
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.SetHikageSkill(HasHikage(), _specialCd <= 0f);
        // HUDの操作ガイド「回避」点灯にCD状態を反映（CD中は淡色）。スキルと同じ毎フレーム通知の流儀。
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.SetDodgeReady(DodgeReady);

        // やさしさ全開＝手動発動（満タン時に Ctrl / R3）。自動発動をやめ“使う”判断を委ねる。
        // Space は ui_accept（＝ショット）と重複し誤発動するため Ctrl（左右どちらも）に変更。
        bool kindKey = Input.IsKeyPressed(Key.Ctrl) || Pad.Pressed(JoyButton.RightStick);
        if (kindKey && !_kindHeld && !Hud.BubblePaused && (_game?.TryActivateKindness() ?? false))
        {
            FxLayer.Instance?.PurifyBurst(GlobalPosition);
            (GetTree().GetFirstNodeInGroup("hud") as Hud)?.Flash();
        }
        _kindHeld = kindKey;

        // 無敵・点滅更新
        if (_invincible)
        {
            _invincibleTimer -= dt;
            _blinkPhase += dt;
            if (_invincibleTimer <= 0f)
            {
                _invincible = false;
                SetSpriteVisible(true);
            }
            else
            {
                // 約 20Hz で点滅
                bool show = ((int)(_blinkPhase * 20f) % 2) == 0;
                SetSpriteVisible(show);
            }
        }

        // グレイズ残光の減衰
        if (_grazeFlash > 0f)
            _grazeFlash = Mathf.Max(0f, _grazeFlash - GrazeFlashDecay * dt);

        // 常時ふわふわ浮遊＋移動バンク（スプライトのみ。当たり判定点は固定）
        _bobTime += dt;
        // 慣性つきで入力方向へ寄せる（会話中は dir=0 なので自然に直立へ戻る＝余韻）。
        _lean = _lean.Lerp(dir, 1f - Mathf.Exp(-LeanResponse * dt));
        if (_hasTexture && _sprite != null)
        {
            float leanMul = _focus ? FocusLeanMul : 1f;
            float bobY = Mathf.Sin(_bobTime * BobSpeed) * BobAmp;
            // 進行方向へわずかに先行（体が動きをリードする）。bob は縦に重畳。
            _sprite.Position = new Vector2(_lean.X * LeadPx * leanMul, bobY + _lean.Y * LeadPx * leanMul);
            // 前傾＋バンク：右移動で前へ、上下移動で機首を振る。
            _sprite.Rotation = (_lean.X * BankX + _lean.Y * BankY) * leanMul;
            // 汚染ティント（光が濁っていく。被弾点滅のαとは独立に SelfModulate へ）。
            // 全開中は汚染を上書きして金色に発光＝「やさしさ全開」を体で示す（脈動）。
            if (_overload)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(_bobTime * 11f);
                _sprite.SelfModulate = OverloadTint.Lerp(OverloadTintHi, pulse);
            }
            else
                _sprite.SelfModulate = CleanTint.Lerp(MurkTint, _corruption);

            // ── 回避中は「その場ピルエット」＝縦軸まわりの本物のスピン（回転各アングルの差分イラストを送る）──
            // 旧実装の Scale.X=cos によるカードスピン擬似（紙っぽさの原因）は廃止し、5枚のフレーム＋FlipH 流用で
            // 全周8ステップのフレームアニメにする。Rotation はスピン中 0（直立）を保つ。
            if (_dodgeTimer > 0f)
            {
                float k = Mathf.Clamp(1f - _dodgeTimer / DodgeDuration, 0f, 1f); // 0→1
                // 変位と同じカーブ：アンティシペーション区間は据え置き、本体は ease-out quad（回り出し速く終端で減速）。
                float anticK = DodgeAntic / DodgeDuration;
                float spinEase = k < anticK ? 0f : 1f - (1f - (k - anticK) / (1f - anticK)) * (1f - (k - anticK) / (1f - anticK));
                // 位相 θ：0→2π*DodgeSpins。終端で spinEase=1 → 周回数ちょうど → 正面(00)に着地する。
                float theta = Mathf.Tau * DodgeSpins * spinEase;
                if (_spinReady) ApplySpinFrame(theta);
                _sprite.Rotation = 0f; // 直立を保つ＝側転(Z回転)はしない。通常バンクも回避中は無効化。
                // ジャンプスピンの軽い浮き＋進行方向への先行（やり過ぎない）。
                _sprite.Position += _dodgeDir * (3f * Mathf.Sin(k * Mathf.Pi)) - new Vector2(0f, DodgeLift * Mathf.Sin(k * Mathf.Pi));
                // 回避無敵を発光＋点滅で可視化（i-frame 終了で通常へ）。
                if (_dodgeInv > 0f)
                {
                    float gl = 0.6f + 0.4f * Mathf.Sin(_bobTime * 40f);
                    _sprite.SelfModulate = new Color(0.8f, 1.1f, 1.4f).Lerp(new Color(1.4f, 1.6f, 2.0f), gl);
                }
            }
        }

        // ヒットボックス点を毎フレーム更新描画
        QueueRedraw();
    }

    private void Fire()
    {
        if (_pool == null)
            return;

        // 銃口（中心からやや右）。光の出力強化でダメージ増。
        Vector2 muzzle = GlobalPosition + new Vector2(20f, 0f);
        int dmg = 1 + (_game?.ShotDamageBonus ?? 0);

        // 選択中のショットモードで発射パターンを分岐（設計書 §3）。
        switch (_game?.SelectedShotMode ?? GameManager.ShotMode.Rapid)
        {
            case GameManager.ShotMode.Spread: FireSpread(muzzle, dmg); break;
            case GameManager.ShotMode.Homing: FireHoming(muzzle, dmg); break;
            default:                          FireRapid(muzzle, dmg);  break;
        }

        // フォロワーは通常2回に1回、全開中は毎ショット同期発射
        _shotParity++;
        if (_overload || (_shotParity & 1) == 0)
            foreach (var f in _followers)
                f.Fire();

        // マズルフラッシュ＋発射音（画と同フレーム）
        FxLayer.Instance?.Muzzle(muzzle);
        Audio.Instance?.PlayShot(_overload);
    }

    // 連射：右へ直線の高速ストリーム。段数 = 2 + ⌊光の出力Lv/2⌋（最大4段）＝正面集中。
    private void FireRapid(Vector2 muzzle, int dmg)
    {
        Vector2 vel = new Vector2(360f, 0f);
        int lines = Mathf.Clamp(2 + (_game?.ShotDamageBonus ?? 0) / 2, 2, 4);
        float[] offs = lines <= 2 ? new[] { -4f, 4f }
                     : lines == 3 ? new[] { -6f, 0f, 6f }
                                  : new[] { -10f, -4f, 4f, 10f };
        foreach (float dy in offs)
            _pool.Spawn(muzzle + new Vector2(0f, dy), vel, isEnemy: false, 3f, dmg);
    }

    // 拡散：右方向へ扇状 n-way（±28°）。1発威力 ×0.8（下限1）＝面制圧。
    private void FireSpread(Vector2 muzzle, int dmg)
    {
        int n = Mathf.Max(5, _game?.SpreadWays ?? 5);
        int sdmg = Mathf.Max(1, Mathf.RoundToInt(dmg * 0.8f));
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0f : (float)i / (n - 1) - 0.5f;
            float ang = t * Mathf.DegToRad(56f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            _pool.Spawn(muzzle, dir * 320f, isEnemy: false, 3f, sdmg);
        }
    }

    // ホーミング：追尾弾を扇状に放ち、右側の穢れへ曲射。弾速200。追尾数 2→2→3（誘導Lv）。
    // 自動追尾が価値＝その対価としてDPSを rapid/spread 未満に抑える（弾速↓・威力×0.7の追尾税）。
    private void FireHoming(Vector2 muzzle, int dmg)
    {
        int shots = Mathf.Max(1, _game?.HomingShots ?? 2);
        int hdmg = Mathf.Max(1, Mathf.RoundToInt(dmg * 0.7f)); // 追尾税（拡散の×0.8より重い＝当て易さの対価）
        for (int i = 0; i < shots; i++)
        {
            float t = shots == 1 ? 0f : (float)i / (shots - 1) - 0.5f;
            float ang = t * Mathf.DegToRad(40f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            _pool.Spawn(muzzle, dir * 200f, isEnemy: false, 3f, hdmg, BulletShape.Orb, null, homing: true); // 弾速 260→200
        }
    }

    // ───────── 回避（ドッジ）アクション ─────────
    // 入力方向があればその方向へ短い無敵ダッシュ、無ければその場回避（変位ゼロ＝スピン＆無敵のみ）。
    // クールダウン中・会話中・ゲームオーバー中は不可。
    private void TryDodge(Vector2 dir)
    {
        if (_dodgeCd > 0f || _dodgeTimer > 0f || _gameOver) return;

        // 方向入力あり＝その方向へダッシュ。無し＝その場回避（変位ゼロ＝_dodgeDir を Zero に）。
        _dodgeInPlace = dir.LengthSquared() <= 0.01f;
        _dodgeDir = _dodgeInPlace ? Vector2.Zero : dir.Normalized();
        _dodgeFrom = GlobalPosition;
        _dodgeTimer = DodgeDuration;
        _dodgeInv = DodgeIFrame;
        _dodgeCd = DodgeCooldown;
        DodgeCount++;         // 実行回数を加算（チュートリアルの回避検出用。報酬や挙動には無関係）
        _dodgeGrazeCount = 0; // 回避ごとに報酬カウンタをリセット（Cap=DodgeGrazeCap までが高報酬対象）
        _dodgeTrailAccum = 0f;
        _dodgeFlip = false; // 開始は正面フレーム（00・FlipHなし）。ApplySpinFrame が毎フレーム更新する。
        // スピンの回り始めの向き：右/下回避=正、左/上回避=負。横成分があればそちらを優先。
        // 横成分が無い（真上/真下）回避は、上方向なら負・下方向なら正＝進行方向へ巻き込む自然な向き。
        // その場回避（方向入力なし）は既定で右回り（正）。
        if (_dodgeInPlace)
            _dodgeSpinSign = 1f;
        else if (Mathf.Abs(_dodgeDir.X) > 0.01f)
            _dodgeSpinSign = _dodgeDir.X >= 0f ? 1f : -1f;
        else
            _dodgeSpinSign = _dodgeDir.Y >= 0f ? 1f : -1f;

        // スプライトを回避スピンの開始フレーム（正面 00）へ。フレームが揃っているときだけ（mina）。
        // 揃っていないスキン（boy 等）は idle のまま回す＝フォールバック（落とさない）。
        if (_hasTexture && _sprite != null && _spinReady)
            ApplySpinFrame(0f);

        // 踏み込みの SE/演出（既存のグレイズ閃光を流用＝専用アセット不要で“抜けた”手応え）。
        FxLayer.Instance?.Graze(GlobalPosition);
        Audio.Instance?.PlayGraze();
        SpawnTrail();
    }

    // 位相 θ（0→2π*DodgeSpins）から全周8ステップのフレームを引いてスプライトに適用する。
    // 8分割インデックス k=floor(frac(θ/2π)*8)。マッピング（正回り _dodgeSpinSign>=0）:
    //   0→00,flip- / 1→01,flip- / 2→02,flip- / 3→03,flip- / 4→04,flip- / 5→03,flip+ / 6→02,flip+ / 7→01,flip+
    // 逆回り（_dodgeSpinSign<0）は k を反転（00→07→06…相当）して左右逆に見せる＝(8-k)%8 を引く。
    // 高さは全フレーム360で統一されているので高さ正規化スケール(36f/h)はどのフレームでも同一になる。
    private static readonly int[]  SpinFrameIdx  = { 0, 1, 2, 3, 4, 3, 2, 1 };
    private static readonly bool[] SpinFrameFlip = { false, false, false, false, false, true, true, true };
    private void ApplySpinFrame(float theta)
    {
        float frac = theta / Mathf.Tau;
        frac -= Mathf.Floor(frac);                 // 0..1（周回を畳む）
        int k = (int)(frac * 8f);
        if (k > 7) k = 7;                          // frac→1.0 の境界保険
        if (_dodgeSpinSign < 0f) k = (8 - k) % 8;  // 逆回り＝たどり順を反転（00→07→06…）

        var tex = _spinTex[SpinFrameIdx[k]];
        if (tex == null) return;                   // 念のため null 安全（_spinReady 前提だが）
        _sprite.Texture = tex;
        _dodgeFlip = SpinFrameFlip[k];
        _sprite.FlipH = _dodgeFlip;
        // フレーム差し替えごとに高さ正規化スケールを再計算（高さ360で統一＝実質どれも同一値）。
        float h = tex.GetHeight();
        if (h > 0)
        {
            float scale = 36f / h;
            _baseScaleX = scale;
            _sprite.Scale = new Vector2(scale, scale);
        }
    }

    // 回避終了：必ず正面フレーム(00)・FlipH=false・Rotation=0・idle テクスチャへ戻す。中途半端なフレームで固定しない。
    private void EndDodge()
    {
        _dodgeTimer = 0f;
        _dodgeFlip = false;
        if (_hasTexture && _sprite != null)
        {
            _sprite.FlipH = false;
            _sprite.Rotation = 0f; // 直立・正位置へ（次フレームから通常バンク／idle が滑らかに引き継ぐ）
            if (_idleTex != null)
            {
                _sprite.Texture = _idleTex;
                float h = _idleTex.GetHeight();
                if (h > 0) { _baseScaleX = 36f / h; }
            }
            // スケールを素値へきっちり戻す（Scale.X=Scale.Y=baseScale）。
            _sprite.Scale = new Vector2(_baseScaleX, _baseScaleX);
        }
    }

    // 残像を1枚置く。Sprite2D を上限数だけ使い回す（毎フレーム Load も new もしない）。
    private void SpawnTrail()
    {
        if (!_hasTexture || _sprite == null) return;
        // プールが満杯なら一番古い（=リストの先頭）を使い回す。
        Sprite2D s;
        if (_trail.Count >= DodgeTrailMax)
        {
            s = _trail[0];
            _trail.RemoveAt(0);
        }
        else
        {
            s = new Sprite2D
            {
                Centered = true,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                ZIndex = ZIndex - 1, // 自機の背面に薄く残す
            };
            // 自機と同じ親（World）直下に置き、自機に追従させない（その場に残る残像）。
            GetParent().AddChild(s);
        }
        s.Texture = _sprite.Texture;   // その瞬間のスピンフレームを写す＝“回ってる残像”になる
        s.Scale = _sprite.Scale;
        s.FlipH = _sprite.FlipH;       // フレームごとの左右反転も写す（真横以降の FlipH 流用フレームを正しく残す）
        s.Rotation = _sprite.Rotation; // 回避中は 0（直立）＝側転痕は残らない
        s.GlobalPosition = _sprite.GlobalPosition;
        s.SelfModulate = new Color(0.7f, 0.9f, 1.2f); // 薄い青白の分身
        s.Modulate = new Color(1f, 1f, 1f, 0.45f);
        s.SetMeta("ttl", DodgeTrailTtl);
        s.SetMeta("age", 0f);
        s.Visible = true;
        _trail.Add(s);
    }

    // 残像をフェードアウト。寿命切れは Visible=false で隠してプールに残す（再利用）。
    private void UpdateTrail(float dt)
    {
        for (int i = _trail.Count - 1; i >= 0; i--)
        {
            var s = _trail[i];
            if (!IsInstanceValid(s)) { _trail.RemoveAt(i); continue; }
            if (!s.Visible) continue;
            float age = (float)s.GetMeta("age") + dt;
            float ttl = (float)s.GetMeta("ttl");
            if (age >= ttl) { s.Visible = false; continue; }
            s.SetMeta("age", age);
            float a = (1f - age / ttl) * 0.45f;
            s.Modulate = new Color(1f, 1f, 1f, a);
        }
    }

    private void OnAreaEntered(Area2D area)
    {
        // 敵 or 敵弾との接触で TakeHit。
        // 弾側の damage 処理は敵側 / 弾側で行うため、ここでは被弾のみ扱う。
        if (area is Enemy e && !e.IsPurified)
        {
            TakeHit();
            return;
        }

        // b.Active を必須にする。プールへ返却済み（=非アクティブ）の弾が
        // 当たり判定だけ残っていても被弾しないようにする。
        if (area is Bullet b && b.IsEnemy && b.Active)
        {
            TakeHit();
            // 当たった敵弾は消す
            if (_pool != null)
                _pool.Despawn(b);
        }
    }

    // グレイズ（敵弾のかすり）検出 → 加点。
    private void OnGrazeAreaEntered(Area2D area)
    {
        if (area is Bullet b && b.IsEnemy && b.Active && !b.Grazed)
        {
            b.Grazed = true; // どちらの分岐でも立てて二重取りを防ぐ
            var game = GetNodeOrNull<GameManager>("/root/Game");

            // 回避の無敵中（_dodgeInv>0）に貫通した敵弾は「回避よけ」＝高報酬＋お金。
            // ただし1回避あたり DodgeGrazeCap 発まで。超過分は貫通するが追加報酬なし（farming防止）。
            // i-frame が切れた回避モーション後半（_dodgeInv==0 だが _dodgeTimer>0）は通常グレイズ扱い。
            if (_dodgeInv > 0f && _dodgeGrazeCount < DodgeGrazeCap && game != null)
            {
                _dodgeGrazeCount++;
                long imp = game.AddDodgeGraze();
                // 回避よけのフィードバックは通常グレイズと差別化＝自機位置に金色「+N」（N=稼いだインプレ）。
                FxLayer.Instance?.DamageNumber(GlobalPosition + new Vector2(0, -16), "+" + imp, FxLayer.Gold, 12);
            }
            else
            {
                game?.AddGraze();
            }

            FxLayer.Instance?.Graze(GlobalPosition); // グレイズ閃光（共通の手応え）
            Audio.Instance?.PlayGraze();
            _grazeFlash = 1f; // 自機側のグレイズリングを一瞬光らせる（“今かすった”を強調）
        }
    }

    // ボム「魔法陣・解放」: 画面の敵弾を消去＋画面内の敵を浄化＋短時間無敵＋画面フラッシュ。
    private void TryBomb()
    {
        var game = GetNodeOrNull<GameManager>("/root/Game");
        if (game == null || !game.UseBomb())
            return;

        BombCount++; // 発動成功＝累計を加算（練習モードの発動検出用。残数では見れないため）

        // ボム演出（魔法陣＋光の波）＋画面効果
        FxLayer.Instance?.Bomb(GlobalPosition);
        Audio.Instance?.PlayBomb(); // ③溜め→開放の二段。破壊でなく「鎮める／光が満ちる」
        GameCamera.Instance?.Shake(4.5f, 0.15f);
        GameCamera.Instance?.Hitstop(0.05);

        // 画面内の敵弾を「花びらに変換」して消去（加点）
        foreach (Node node in GetTree().GetNodesInGroup("enemy_bullets"))
        {
            if (node is Bullet b && b.Active)
            {
                game.AddBulletCleared();
                FxLayer.Instance?.BulletToPetal(b.GlobalPosition);
                _pool?.Despawn(b);
            }
        }

        // 画面内の敵を浄化
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is Enemy e)
                e.Purify();
        }

        // 短時間無敵 ＋ 画面フラッシュ
        StartInvincible();
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.Flash();
    }

    // ヒカゲが仲間にいるか。
    private bool HasHikage()
    {
        foreach (var f in _followers)
            if (f.IsHikage) return true;
        return false;
    }

    // ヒカゲ専用スキル「やさしさの大波（鎮火）」。
    // 前方に強い大粒ハート弾を扇状に放ち、前方の敵弾を花びらに変えて消す。クールダウンあり。
    private void TryHikageSpecial()
    {
        if (_specialCd > 0f || !HasHikage() || _gameOver) return;
        _specialCd = SpecialCdMax;

        // 前方に扇状の大波（強い大粒弾）
        const int n = 15;
        for (int i = 0; i < n; i++)
        {
            float t = n == 1 ? 0f : (float)i / (n - 1) - 0.5f; // -0.5..0.5
            float ang = t * Mathf.DegToRad(64f);               // 上下±32°の扇
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            _pool?.Spawn(GlobalPosition + new Vector2(14f, 0f), dir * 400f, isEnemy: false, 4.5f, 3);
        }

        // 前方の敵弾を花びらに変えて消す（防御も兼ねる）
        var game = GetNodeOrNull<GameManager>("/root/Game");
        foreach (Node node in GetTree().GetNodesInGroup("enemy_bullets"))
        {
            if (node is Bullet b && b.Active && b.GlobalPosition.X > GlobalPosition.X - 8f)
            {
                game?.AddBulletCleared();
                FxLayer.Instance?.BulletToPetal(b.GlobalPosition);
                _pool?.Despawn(b);
            }
        }

        // 演出
        FxLayer.Instance?.PurifyBurst(GlobalPosition + new Vector2(20f, 0f));
        GameCamera.Instance?.Shake(3.5f, 0.12f);
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.Flash();
    }

    private bool _gameOver = false;

    public void TakeHit()
    {
        // 無敵中・回避無敵中・ゲームオーバー中は無効（回避の主旨＝回避中は被弾しない）
        if (_invincible || _dodgeInv > 0f || _gameOver)
            return;

        // 被弾演出（自機周囲のフラッシュ＋波紋）＋ 赤フラッシュ・シェイク・ヒットストップで「被弾」を明確化。
        FxLayer.Instance?.PlayerHit(GlobalPosition);
        Audio.Instance?.PlayHit(); // 最優先（Alertバス）。実時間再生でヒットストップに引きずられない
        GameCamera.Instance?.Shake(5.5f, 0.28f);
        GameCamera.Instance?.Hitstop(0.09);
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.HitFlash();

        // チュートリアル練習モード：被弾演出は出すが、残機を減らさず・ゲームオーバーにせず・フォロワーも離さない（詰み防止）。
        // 短時間無敵だけ付けて先へ進める（同じ弾で連続被弾しない）。
        if (_game?.TutorialNoConsume ?? false)
        {
            StartInvincible();
            return;
        }

        // ♥（残機）を1つ減らして HUD 更新
        Lives = Mathf.Max(0, Lives - 1);
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.SetLives(Lives);

        // 被弾でフォロワーも全員離れてしまう（やさしさの輪がほどける）
        foreach (var f in _followers)
        {
            FxLayer.Instance?.KindnessMote(f.GlobalPosition);
            f.QueueFree();
        }
        _followers.Clear();

        // フラッシュ＋短時間無敵
        StartInvincible();

        if (Lives <= 0)
            GameOver();
    }

    // ♥（残機）を回復する。上限は難易度＋最大♥強化由来の初期値（StartLives）。
    // 既に上限なら増やさない。戻り値＝実際に増えたか（フィードバック演出の判定用）。
    // 中ボス撃破の回復報酬（GameManager.RewardCameoDefeat）から呼ぶ。
    public bool AddLife(int n = 1)
    {
        if (_gameOver || n <= 0) return false;
        int cap = _game?.StartLives ?? 3;
        if (Lives >= cap) return false;
        Lives = Mathf.Min(cap, Lives + n);
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.SetLives(Lives);
        return true;
    }

    private void GameOver()
    {
        _gameOver = true;
        _invincible = true;
        _invincibleTimer = 9999f; // 以降は無敵で待機
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.ShowBanner("くじけちゃった… Rでもう一度");
        // 自動リロードはしない。各ステージルート(*Root.cs)の _Process が R＝リトライ／Q＝抜ける を
        // 受け付けるので、プレイヤーが選ぶまでこのまま無敵で待機する。
    }

    private void StartInvincible()
    {
        _invincible = true;
        _invincibleTimer = InvincibleDuration;
        _blinkPhase = 0f;
        // 被弾フラッシュ（一瞬非表示にして点滅開始の合図）
        SetSpriteVisible(false);
    }

    private void SetSpriteVisible(bool visible)
    {
        if (_hasTexture && _sprite != null)
            _sprite.Visible = visible;
        // _Draw フォールバック側は modulate ではなく可視フラグで制御
        Modulate = new Color(1f, 1f, 1f, visible ? 1f : 0.35f);
    }

    public override void _Draw()
    {
        // テクスチャが無い場合のプレースホルダ（白い体＋紫十字）
        if (!_hasTexture)
        {
            // 体（白い円）
            DrawCircle(Vector2.Zero, 12f, new Color(1f, 1f, 1f, 0.95f));
            DrawArc(Vector2.Zero, 12f, 0f, Mathf.Tau, 24, new Color(0.6f, 0.6f, 0.7f), 1f);

            // 紫の十字（胸）
            var purple = new Color(0.6f, 0.2f, 0.8f);
            DrawLine(new Vector2(-4f, 0f), new Vector2(4f, 0f), purple, 1.5f);
            DrawLine(new Vector2(0f, -4f), new Vector2(0f, 4f), purple, 1.5f);
        }

        // ── グレイズ境界（外側リング）＝「ぎりぎり回避＝ご褒美」になる範囲 ──
        // 実際の判定値 GrazeRadius(11f) に必ず一致させて描く（旧 5.5f の不一致を廃止）。
        // 控えめなシアン：被弾点(赤)と意味が一目で違う。通常時は薄く、低速(Shift)時は濃く＝精密回避を促す。
        // “今かすった”残光(_grazeFlash)を上乗せして一瞬明るくする。
        float grazeBaseA = _focus ? 0.55f : 0.22f;
        float grazeA = Mathf.Min(0.95f, grazeBaseA + _grazeFlash * 0.6f);
        float grazeW = _focus ? 1.4f : 1f;
        DrawArc(Vector2.Zero, GrazeRadius, 0f, Mathf.Tau, 40,
            new Color(0.45f, 0.95f, 1f, grazeA), grazeW); // シアンの細いリング＝グレイズ境界

        // ── やさしさ全開オーラ（金の二重リングが脈動）＝全開中だと一目で分かる ──
        if (_overload)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(_bobTime * 11f);
            float r = GrazeRadius + 5f + pulse * 4f;
            var gold = new Color(1f, 0.86f, 0.45f, 0.35f + 0.4f * pulse);
            DrawArc(Vector2.Zero, r, 0f, Mathf.Tau, 44, gold, 1.6f);
            DrawArc(Vector2.Zero, r + 3f, 0f, Mathf.Tau, 44, new Color(1f, 0.95f, 0.7f, 0.18f * pulse), 1f);
        }

        // ── 被弾点（中心・常時・最も目立つ）＝この赤い点に当たると死ぬ ──
        DrawCircle(Vector2.Zero, _hitR + 1.1f, new Color(1f, 1f, 1f, 0.95f)); // 白フチで背景に沈まない
        DrawCircle(Vector2.Zero, _hitR, new Color(1f, 0.2f, 0.45f, 1f));      // 赤コア＝被弾点
    }

    // やさしさ全開・発動の瞬間のカタルシス演出（§4 止め＋光＋揺れ／§8 解放の谷）。
    // SE とトーストは Hud が JustOverloaded で鳴らす。ここは画の爆ぜと弾の浄化を担う。
    private void OnOverloadStart()
    {
        FxLayer.Instance?.PurifyBurst(GlobalPosition);
        GameCamera.Instance?.Shake(4f, 0.22f);
        GameCamera.Instance?.Hitstop(0.06);
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.Flash();

        // 自機周囲だけの浄化パルス（全消しはボム専任＝全開は“攻め”の見返り／§15 役割分離）。
        // 近接の弾だけ花びら化＝一拍の局所的な解放（§8）。守りの保険には使えない＝温存退避の遊びを断つ。
        const float pulseR = 40f;
        foreach (Node node in GetTree().GetNodesInGroup("enemy_bullets"))
        {
            if (node is Bullet b && b.Active && b.GlobalPosition.DistanceTo(GlobalPosition) <= pulseR)
            {
                _game?.AddBulletCleared();
                FxLayer.Instance?.BulletToPetal(b.GlobalPosition);
                _pool?.Despawn(b);
            }
        }
    }
}
