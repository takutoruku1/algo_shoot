using Godot;

// AreaSpellCaster : ボスに付ける「予測攻撃（テレグラフ）」のキャスター（RefrainArenaHTML 移植）。
//   一定間隔で技名を宣告（Xツイート風＝Hud.AnnounceSpell）→ 少し遅れて予測線/予測エリアを N 個出す。
//   キャラ別に形状・色・予兆時間・技セットが変わり、出る数は難易度でスケール（弱2/中4/強7/ルナ10）。
//   会話中(BubblePaused)は出さない。ボスの子に AddChild すれば、ボス撃破で一緒に消える。
//
// 使い方（各ボスの _Ready）：
//   var caster = new AreaSpellCaster();
//   caster.Configure("rei", GetParent());   // GetParent()=World（テレグラフの追加先）
//   AddChild(caster);
public partial class AreaSpellCaster : Node2D
{
    private const float W = 384f, H = 216f;

    private string _key = "";  // Configure のプロファイルキー（rei/akari/koharu/mina。リレー宣告の文言分岐に使う）
    private string _disp = "", _handle = "";
    private Color _tint = new(1f, 0.34f, 0.30f), _hot = new(1f, 0.92f, 0.7f);
    private double _warnMin = 1.0, _warnMax = 1.4, _interval = 6.0;
    private AreaStrike.Shape[] _shapes = { AreaStrike.Shape.Circle };
    private (string name, AreaStrike.Shape? shape)[] _spells = { ("range", null) };
    // 各ボレーの1枚目を自機の現在地にアンカーするか（rei/koharu で有効）。
    // ＝「AOEが自機と無関係な場所で光っているだけ＝脅威として死んでいる」を断ち、
    //   左端に張り付いていても定期的に一歩動かされる。アンカー枚の予兆は固定 1.2s×WarnMul
    //  （ランダム下限より長め）＝頭上に出ても必ず逃げ切れる（死は要求しない設計）。
    private bool _anchorPlayer;

    private readonly RandomNumberGenerator _rng = new();
    private Node _world = null!;
    // このキャスターを抱えるボス（＝攻撃の発生源）。浄化されたら予約中の宣告・出現済みの予兆を全てキャンセルする。
    // ＝「ボスが改心したのに、撃ち終わった範囲技だけが後から着弾する」残留攻撃を断つ。AddChild 先の親＝ボス。
    private Enemy? _owner;

    private double _castT, _fireT;
    private bool _pending;
    private AreaStrike.Shape? _pendShape;
    private double _fireDelay = 0.7; // 技名宣告 → 予兆出現までの溜め

    // ── 全画面AOE（ラスボスMina専用・通常ランダム枠とは独立した専用経路）──
    //   CastFullscreen で予約 → _aoeFireT 後に Fullscreen の AreaStrike を1枚出す。
    //   AoeActive は宣告〜予兆着弾まで true＝ボス側が通常弾(FirePattern)をこの間だけ止める(gate)のに使う。
    private bool _aoePending;            // 宣告済み・予兆出現待ち
    private double _aoeFireT;            // 予兆出現までの残り
    // 安置は常に在る（安置なしの全面型は廃止＝§7「必ず予告＋回避手段」）。
    private bool _aoeTight;              // 「絶域」＝安置が狭い版（そのぶん予兆を TightWarnBoost 倍に伸ばす）
    private const float TightWarnBoost = 1.45f; // 狭い安置ぶんの猶予（1.6s→2.32s @Normal）
    private const float AoeWarn = 1.6f;  // 全画面AOEの予兆尺（WarnMul で難易度補正）
    private const float AoeFireDelay = 0.7f; // 宣告→予兆出現の溜め
    private const float AoeSafeR = 30f;  // 安置(セーフゾーン)半径（28-32px帯）
    // フィナーレ級の「絶域」用の狭い安置半径。全面（安置なし＝回避不能）は廃し、
    // 「狭いが必ず在る安置」に置き換える（§7 理不尽回避：全ての攻撃に回避手段を実在させる）。
    // 難易度で広さを変える＝Easy は明確に広い安置、ルナは針の穴。
    private float AoeTightSafeR() => (GetNodeOrNull<GameManager>("/root/Game")?.Difficulty ?? GameManager.Diff.Normal) switch
    {
        GameManager.Diff.Easy => 30f,
        GameManager.Diff.Hard => 20f,
        GameManager.Diff.Lunatic => 17f,
        _ => 24f,
    };
    private AreaStrike? _aoeStrike;      // 出現中の全画面予兆（生存＝AOE進行中の判定に使う）
    // 宣告〜着弾までの間 true（ボスが通常弾を止めるゲート）。安置リレー中はホップ間の
    // 「生存確認の間」も含めて最終ホップの着弾終了まで true を保つ（被弾でスキップさせない）。
    public bool AoeActive => _aoePending || _chainRemain > 0 || (_aoeStrike != null && IsInstanceValid(_aoeStrike));

    // ボス側の専用ギミック（こはる「お残し禁止」等）の間、通常ランダム枠の宣告/発火を一時停止する
    // 外部ゲート。予約済み(_pending)の発火・タイマーも保留し、解除後にそのまま再開する（破棄はしない）。
    public bool Suppressed;

    // ── 安置リレー（レイ「最終選考」／ミナ強化枠で共用）──
    //   全画面AOEを hops 回連結する。各ホップ：予兆(1.6s×WarnMul)→着弾0.2s→生存確認の間(0.35s)→次ホップ。
    //   初回の安置＝既存の「70px以上の最近傍」。2ホップ目以降＝前の安置中心を起点に距離帯 [hopMin,hopMax]
    //   のランダム方向（ジグザグ保証つき）。安置半径は r=30 固定＝公平性は予兆尺（WarnMul）側で担保する。
    private int _chainRemain;            // 未出現のホップ数（>0 の間はリレー進行中＝AoeActive を保つ）
    private int _chainTotal;             // リレーの全ホップ数（宣告ラベルの何次/最終の判定に使う）
    private Vector2 _chainPrevSafe;      // 直前ホップの安置中心（次ホップの起点）
    private Vector2 _chainPrevDir;       // 直前ホップの進行方向（内積<0.5 になるまで採り直し＝ジグザグ保証）
    private float _chainHopMin, _chainHopMax; // ホップ距離帯（難易度別にボス側が渡す）
    private double _restT;               // 着弾後〜次ホップ予兆までの「生存確認の間」の残り
    private const double ChainRestDur = 0.35; // 生存確認の間（着弾フラッシュが消えてから計時）
    // 直近ホップの安置中心（リレー完走報酬の出現位置。BossRei が参照）。
    public Vector2 LastChainSafe { get; private set; }

    // ボス側（BossMina.OnHpChanged）から全画面AOEを1回予約する。
    // wide=true  … 学習用の広い安置（r=30・予兆 1.6s×WarnMul）
    // wide=false … フィナーレの「絶域」。安置は狭い（AoeTightSafeR）が必ず在り、予兆も長め（TightWarnMul倍）。
    //   旧実装は安置なし（_safeR=0）＝画面全域が被弾域で、ボム以外に回避手段が存在しなかった
    //   （ボム0個なら確定被弾）。§7「必ず予告＋回避手段」に反するため安置ありへ改める。
    public void CastFullscreen(bool wide)
    {
        if (AoeActive) return; // 多重予約しない（HP閾値の同フレーム多重発火対策）
        _aoeTight = !wide;
        _aoePending = true;
        _aoeFireT = AoeFireDelay;
        string name = wide ? "全画面浄化・安置" : "全画面浄化・絶域";
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.AnnounceSpell(_disp, _handle, name, _tint);
    }

    // ボス側から安置リレーを1回予約する（レイ HP26%／ミナ HP42%）。hops=連結数、[hopMin,hopMax]=ホップ距離帯。
    // 距離帯の上限は「予兆尺×自機速度＋安置半径」の到達限界（ルナ約158px＋帯調整）を超えないようボス側が選ぶ。
    public void CastFullscreenChain(int hops, float hopMin, float hopMax)
    {
        if (AoeActive) return; // 多重予約しない
        _chainTotal = _chainRemain = Mathf.Max(1, hops);
        _chainHopMin = hopMin; _chainHopMax = hopMax;
        _chainPrevDir = Vector2.Zero;
        _aoeTight = false; // リレーは r=30 固定（直前の「絶域」設定を引きずらない）
        _aoePending = true;
        _aoeFireT = AoeFireDelay;
        // 技名：レイは「最終選考・N連」。他ボス（ミナ）はレイの技を濁して写した体＝汎用名にする。
        string name = _key == "rei"
            ? "最終選考・" + (hops switch { 2 => "二連", 4 => "四連", _ => "三連" })
            : "全画面浄化・連鎖";
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.AnnounceSpell(_disp, _handle, name, _tint);
    }

    private void TickFullscreen(double delta)
    {
        // リレー中：直前ホップの着弾（_aoeStrike）が消えてから「生存確認の間」を置いて次ホップの予兆を出す。
        // 被弾してもリレーは止めない＝被弾をスキップ手段にしない（残機比較の完走判定はボス側）。
        if (_chainRemain > 0 && !_aoePending && (_aoeStrike == null || !IsInstanceValid(_aoeStrike)))
        {
            _restT -= delta;
            if (_restT <= 0) SpawnChainHop();
            return;
        }

        if (!_aoePending) return;
        _aoeFireT -= delta;
        if (_aoeFireT > 0) return;
        _aoePending = false;

        if (_chainRemain > 0) { SpawnChainHop(); return; } // リレーの初回ホップ

        // 単発：安置位置は自機の現在地を避けて配置（即死事故防止）。画面端に寄せすぎない。
        // 「絶域」は安置を狭くする代わりに予兆を伸ばす＝狭い的でも必ず到達できる。
        float r = _aoeTight ? AoeTightSafeR() : AoeSafeR;
        Vector2 safe = PickSafeNearPlayer(r + 14f);
        SpawnFullscreenStrike(safe, r, _aoeTight ? TightWarnBoost : 1f);
    }

    // 安置候補を12点引き、「自機から70px以上・ボス本体から60px以上」のうち最も自機に近い点を採用する。
    // 70pxの下限＝自機の真上に安置が湧いて棒立ちで済む事故の防止。上限を設けない代わりに
    // 最近傍を選ぶことで、難易度によらず「予兆内に到達できる」ことを担保する：
    //   予兆尺 = AoeWarn(1.6s) × WarnMul（易1.3 / 普1.0 / 難0.85 / ルナ0.72）＝最短1.15s(ルナ)。
    //   反応0.3sを引いた移動猶予 0.85s × 自機速度150px/s ≈ 128px ＋ 安置半径30px
    //   ＝ 安置中心まで約158pxなら間に合う。盤面(384×216)で一様12候補の「70px以上の最近傍」は
    //   ほぼ確実に70〜130px帯に収まるため、ルナティックでも到達可能。
    //   （旧実装は「70px以上を見つけ次第採用」で上限がなく、最悪381px先＝到達不能な安置が出得た）
    //   ボス60pxの除外＝安置がボス徘徊圏（例: レイ X∈[110,290]・Y∈[42,98]）内に湧き、
    //   「安置円の中なのにボス本体接触で被弾」する理不尽の防止（QA 2026-07 週次）。
    private const float BossClear = 60f; // 安置候補とボス現在位置の最低距離（ボスは徘徊42px/s＝予兆中の踏み込みは限定的）
    private Vector2 PickSafeNearPlayer(float margin)
    {
        Vector2 player = PlayerPos();
        Vector2? boss = BossPos();
        Vector2 safe = new Vector2(W / 2f, H / 2f); // 全候補が条件を満たさない時のフォールバック＝中央（12候補全棄却はほぼ起きない）
        float bestD = float.MaxValue;
        for (int i = 0; i < 12; i++)
        {
            var cand = new Vector2(_rng.RandfRange(margin, W - margin), _rng.RandfRange(margin, H - margin));
            if (boss.HasValue && cand.DistanceTo(boss.Value) < BossClear) continue; // ボスの居場所＝安置にしない
            float d = cand.DistanceTo(player);
            if (d >= 70f && d < bestD) { bestD = d; safe = cand; }
        }
        return safe;
    }

    // 自機の現在地（いなければ画面中央＝旧フォールバックを踏襲）。
    private Vector2 PlayerPos() =>
        (GetTree().GetFirstNodeInGroup("player") as Node2D)?.GlobalPosition ?? new Vector2(W / 2f, H / 2f);
    // このキャスターを抱えるボスの現在地（未キャッシュ/消滅時は null）。
    private Vector2? BossPos() =>
        _owner != null && IsInstanceValid(_owner) ? _owner.GlobalPosition : null;

    // リレーのホップを1つ出す。初回＝既存の最近傍安置。2ホップ目以降＝前の安置中心を起点に
    // 距離帯からランダム方向へ。前ホップ方向と内積<0.5になるまで採り直し（ジグザグ保証）、
    // 画面端は margin=r+14 でクランプする。クランプで距離が潰れた候補（緊張が抜ける）も採り直す。
    private void SpawnChainHop()
    {
        int hopIdx = _chainTotal - _chainRemain; // 0始まり
        _chainRemain--;
        float margin = AoeSafeR + 14f;
        Vector2 safe;
        if (hopIdx == 0)
        {
            safe = PickSafeNearPlayer(margin);
            _chainPrevDir = Vector2.Zero;
        }
        else
        {
            safe = _chainPrevSafe;
            Vector2? boss = BossPos();
            for (int i = 0; i < 24; i++)
            {
                float a = _rng.RandfRange(0f, Mathf.Tau);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                if (_chainPrevDir != Vector2.Zero && dir.Dot(_chainPrevDir) >= 0.5f) continue; // 同方向すぎ＝採り直し
                var cand = _chainPrevSafe + dir * _rng.RandfRange(_chainHopMin, _chainHopMax);
                cand.X = Mathf.Clamp(cand.X, margin, W - margin);
                cand.Y = Mathf.Clamp(cand.Y, margin, H - margin);
                if (cand.DistanceTo(_chainPrevSafe) < _chainHopMin * 0.9f) continue; // 端クランプで潰れた＝採り直し（0.9: 縦216pxで垂直系候補が潰れやすく、棄却→再抽選で横方向が創発的に優先される。sakurai査定 2026-07-05）
                if (boss.HasValue && cand.DistanceTo(boss.Value) < BossClear) continue; // ボスの上に安置＝採り直し（安置内接触の理不尽防止）
                safe = cand;
                break;
            }
            // 全採り直し失敗（角に追い詰められた等）の保険：中央方向へ hopMin ぶん逃がす
            //（この保険だけはボス距離より画面内保証を優先＝24回の抽選が全滅する極端な状況の脱出弁）。
            if (safe == _chainPrevSafe)
            {
                var dir = (new Vector2(W / 2f, H / 2f) - _chainPrevSafe).Normalized();
                safe = _chainPrevSafe + dir * _chainHopMin;
                safe.X = Mathf.Clamp(safe.X, margin, W - margin);
                safe.Y = Mathf.Clamp(safe.Y, margin, H - margin);
            }
            _chainPrevDir = (safe - _chainPrevSafe).Normalized();
        }
        _chainPrevSafe = safe;
        LastChainSafe = safe;

        // ホップ宣告（レイの選考演出）：一次選考→二次選考→（三次選考→）最終選考。
        // 他ボス（ミナ）は選考モチーフが合わないので初回宣告のみ＝安置の緑リング自体が次の合図。
        if (_key == "rei")
        {
            string label = _chainRemain == 0 ? "最終選考"
                         : hopIdx switch { 0 => "一次選考", 1 => "二次選考", _ => "三次選考" };
            (GetTree().GetFirstNodeInGroup("hud") as Hud)?.AnnounceSpell(_disp, _handle, label, _tint);
        }

        SpawnFullscreenStrike(safe, AoeSafeR); // 安置 r=30 固定（難易度で縮めない）
        _restT = ChainRestDur; // 次ホップまでの生存確認の間（着弾フラッシュが消えてから計時される）
    }

    // ボス別のテレグラフ・モチーフ（心象を予兆で語る）。BeamSeg（こはる『包丁の軌跡』）だけは None＝鋭い深紅のまま。
    private AreaStrike.Motif MotifFor(AreaStrike.Shape shape)
    {
        if (shape == AreaStrike.Shape.BeamSeg) return AreaStrike.Motif.None;
        return _key switch
        {
            "rei"    => AreaStrike.Motif.Rank,
            "akari"  => AreaStrike.Motif.Rain,
            "koharu" => AreaStrike.Motif.Kitchen,
            "mina"   => AreaStrike.Motif.Data,
            _        => AreaStrike.Motif.Data, // 既定（mina 相当）
        };
    }

    // 全画面予兆を1枚出す（単発／リレー共用）。warnBoost>1 で予兆尺を伸ばす（狭い安置の「絶域」用）。
    private void SpawnFullscreenStrike(Vector2 safe, float r, float warnBoost = 1f)
    {
        var z = new AreaStrike();
        // Fullscreen は自前の予兆演出（濁桃tint＋緑リング＋白フレーム収束）を持つ＝モチーフ層は対象外（§1-b）。
        z.ConfigureFullscreen(safe, r, AoeWarn * WarnMul() * warnBoost, _tint, _hot);
        if (_owner != null) z.SetOwner(_owner); // 着弾前にボス浄化されたら予兆ごと消える
        _world.AddChild(z);
        z.GlobalPosition = Vector2.Zero; // 画面座標基準（Fullscreen は内部で安置座標を画面系で扱う）
        _aoeStrike = z;
    }

    public void Configure(string key, Node world)
    {
        _key = key;
        _world = world;
        _rng.Randomize();
        var H_ = AreaStrike.Shape.BeamH; var V = AreaStrike.Shape.BeamV;
        var C = AreaStrike.Shape.Circle; var R = AreaStrike.Shape.Rect;
        var B = AreaStrike.Shape.BeamSeg;
        switch (key)
        {
            case "rei": // 順位掲示板・整然と裁く（予兆長め・金/菫）
                _disp = "レイ"; _handle = "@rei_compe";
                _tint = new Color("e8c45a"); _hot = new Color("ffe39a");
                _warnMin = 1.1; _warnMax = 1.6; _interval = 8.0; // 11.0→8.0：範囲技の存在感を上げる（sakurai 2026-07 週次）
                _shapes = new[] { H_, R };
                _spells = new (string, AreaStrike.Shape?)[] { ("ランキングレーザー", H_), ("表彰台圏", R), ("序列の楔", H_) };
                _anchorPlayer = true; // 1枚目は自機の現在地＝左端張り付きでも定期的に一歩動かされる
                break;
            case "akari": // 雨の教室・降る前に予報（蒼）
                _disp = "あかり"; _handle = "@akari_rain";
                _tint = new Color("6c9cd8"); _hot = new Color("a9dcff");
                _warnMin = 1.0; _warnMax = 1.4; _interval = 9.0;
                _shapes = new[] { V, C };
                _spells = new (string, AreaStrike.Shape?)[] { ("豪雨予報", V), ("沈黙の波紋", C) };
                break;
            case "koharu": // 台所・熱してから一気に（予兆やや短め・琥珀/深紅）
                _disp = "こはる"; _handle = "@koharu_kitchen";
                _tint = new Color("e8945a"); _hot = new Color("ffc06a");
                // warn 下限 0.7→1.0s：全ボス最短の予兆が5秒の宣言カードと乖離し「宣言だけ出て
                // 何も起きない」感の主因だった（QA 2026-07 週次）。短予兆の性格は上限1.3sで残す。
                _warnMin = 1.0; _warnMax = 1.3; _interval = 7.0;
                _shapes = new[] { C, R }; // 全スペルが shape 固定＝実質フォールバック（到達不能だった H_ は削除）
                // 第3スペル『包丁の軌跡』（catalog: Refrain Telegraphs.dc.html 274-277）＝±26°の深紅の斜め一閃×2。
                _spells = new (string, AreaStrike.Shape?)[] { ("熱したフライパン", C), ("沸騰鍋", R), ("包丁の軌跡", B) };
                _anchorPlayer = true; // 1枚目は自機の現在地（円/矩形は頭上・包丁は自機を通る線）
                break;
            default: // mina（暴走）：全テレグラフ同時・濁った全色
                _disp = "ミナ"; _handle = "@mina_ai_";
                _tint = new Color("e072ac"); _hot = new Color("ff8cc4");
                _warnMin = 0.8; _warnMax = 1.2; _interval = 6.0;
                _shapes = new[] { H_, V, C, R };
                _spells = new (string, AreaStrike.Shape?)[] { ("全テレグラフ同時", null), ("濁渦と雨", null) };
                break;
        }
        // INI上書き（config/boss_stats.ini の [key] セクション）。キーが無ければ上のハードコード既定のまま。
        _interval = BossTuning.F(key, "aoe_interval", (float)_interval);
        _warnMin = BossTuning.F(key, "aoe_warn_min", (float)_warnMin);
        _warnMax = BossTuning.F(key, "aoe_warn_max", (float)_warnMax);
        _anchorPlayer = BossTuning.B(key, "aoe_anchor_player", _anchorPlayer);
    }

    public override void _Process(double delta)
    {
        // 発生源（ボス）を一度キャッシュ：AddChild 先の親がボス（Configure とは独立にここで拾う）。
        _owner ??= GetParent() as Enemy;
        // ボスが浄化（改心）されたら、以降は宣告も予兆出現もしない＝攻撃が終わった後に技が残らない。
        // 予約済み（宣告→出現待ち）の発火も破棄する。出現済みの予兆は AreaStrike 側が owner 浄化で自滅する。
        if (_owner != null && _owner.IsPurified)
        {
            _pending = false;
            _aoePending = false; // 予約中の全画面AOEも破棄（出現済みは AreaStrike が owner 浄化で自滅）
            _chainRemain = 0;    // 進行中の安置リレーも打ち切る（改心後にホップが続かないように）
            return;
        }

        if (Hud.BubblePaused) return; // 会話中は出さない
        if (Suppressed) return;       // ボス側ギミック中（お残し禁止 等）は宣告も発火も保留

        // 全画面AOEの予約を進める（専用経路）。AOE進行中は通常ランダム枠は止める（弾幕の過密回避）。
        TickFullscreen(delta);
        if (AoeActive) return;

        if (_pending)
        {
            _fireT -= delta;
            if (_fireT <= 0) { SpawnTelegraphs(); _pending = false; }
            return;
        }
        _castT += delta;
        if (_castT >= _interval) { _castT = 0; Cast(); }
    }

    // 技名を宣告（Xツイート風スペルカード）→ 溜めて予兆を出す。
    private void Cast()
    {
        var sp = _spells[_rng.RandiRange(0, _spells.Length - 1)];
        // 宣告カードの色：『包丁の軌跡』だけ深紅＝ビーム本体と同色（宣告と予兆が色で結びつく）。
        Color tint = sp.shape == AreaStrike.Shape.BeamSeg ? KnifeTint : _tint;
        (GetTree().GetFirstNodeInGroup("hud") as Hud)?.AnnounceSpell(_disp, _handle, sp.name, tint);
        _pendShape = sp.shape;
        _pending = true; _fireT = _fireDelay;
    }

    // 難易度で同時に出る数が増える（弾幕に上乗せするため控えめ：弱1/中2/強3/ルナ5）。
    private int DiffCount() => (GetNodeOrNull<GameManager>("/root/Game")?.Difficulty ?? GameManager.Diff.Normal) switch
    {
        GameManager.Diff.Easy => 1,
        GameManager.Diff.Hard => 3,
        GameManager.Diff.Lunatic => 5,
        _ => 2,
    };
    // 予兆時間の難易度補正（易しいほど長く＝避ける猶予が増える）。
    // public: ボス側の自前テレグラフギミック（こはる「五徳の十字火」等）も同じ補正で予兆尺を組む。
    public float WarnMul() => (GetNodeOrNull<GameManager>("/root/Game")?.Difficulty ?? GameManager.Diff.Normal) switch
    {
        GameManager.Diff.Easy => 1.3f,
        GameManager.Diff.Hard => 0.85f,
        GameManager.Diff.Lunatic => 0.72f,
        _ => 1f,
    };

    // この一斉射ぶんの配置（重なり回避用）。
    private readonly System.Collections.Generic.List<(AreaStrike.Shape shape, Vector2 c, float hw, float hh)> _placed = new();

    private void SpawnTelegraphs()
    {
        _placed.Clear();
        int n = DiffCount();
        float wm = WarnMul();
        for (int i = 0; i < n; i++)
        {
            // 技の形状が決まっていなければ（ミナ）プロファイルの全形状から拾う＝“全テレグラフ同時”。
            var shape = _pendShape ?? _shapes[_rng.RandiRange(0, _shapes.Length - 1)];
            // 1枚目は自機の現在地にアンカー（rei/koharu）。予兆は固定 1.2s×WarnMul＝必ず逃げ切れる尺。
            bool anchor = _anchorPlayer && i == 0;
            double warn = anchor ? 1.2 * wm
                                 : _rng.RandfRange((float)_warnMin, (float)_warnMax) * wm;

            // 『包丁の軌跡』（こはる第3スペル）：±26°の深紅の一閃（catalog準拠）。1本目は自機の現在地を
            // “通る線”として引き、2本目以降は中央帯のランダム点を逆角度で交差させる。予兆を +0.35s ずつ
            // ずらして順に斬る（catalog の delay 相当）。交差が意図なので重なり回避は掛けない。
            if (shape == AreaStrike.Shape.BeamSeg)
            {
                Vector2 through = anchor ? PlayerPos()
                    : new Vector2(_rng.RandfRange(W * 0.30f, W * 0.85f), _rng.RandfRange(H * 0.25f, H * 0.75f));
                SpawnKnifeBeam(through, (i % 2 == 0 ? -1f : 1f) * KnifeDeg, warn + 0.35 * i);
                continue;
            }

            // アンカー枚は重なり回避なしで必ず置く（自機の頭上が最優先。以降のランダム枚が避ける側）。
            if (anchor)
            {
                var g = AnchorGeo(shape, PlayerPos());
                AddStrike(shape, g.c, g.hw, g.hh, warn);
                continue;
            }

            // 重ならない配置を最大16回トライ。置けなければスキップ＝過密を自然に防ぐ。
            for (int tryI = 0; tryI < 16; tryI++)
            {
                var g = RandGeo(shape);
                if (OverlapsAny(shape, g.c, g.hw, g.hh)) continue;
                AddStrike(shape, g.c, g.hw, g.hh, warn);
                break;
            }
        }
    }

    // 軸形状の AreaStrike を1枚生成して World へ置く（重なり判定用の _placed にも記録）。
    private void AddStrike(AreaStrike.Shape shape, Vector2 c, float hw, float hh, double warn)
    {
        var z = new AreaStrike();
        z.Configure(shape, hw, hh, warn, _tint, _hot, MotifFor(shape));
        // 発生源を結びつけ、着弾前にボスが浄化されたら予兆ごと消えるようにする（残留着弾を断つ）。
        if (_owner != null) z.SetOwner(_owner);
        _world.AddChild(z);
        z.GlobalPosition = c;
        _placed.Add((shape, c, hw, hh));
    }

    // 自機アンカー配置：自機の現在地を必ず覆う形で置く（＝一歩動けば外れる。死は要求しない）。
    //   円＝r20固定（小さめ＝要求は「一歩」だけ）／矩形＝中心を自機に／ビームは自機の行・列。
    private (Vector2 c, float hw, float hh) AnchorGeo(AreaStrike.Shape shape, Vector2 p)
    {
        switch (shape)
        {
            case AreaStrike.Shape.BeamH: return (new Vector2(W / 2f, p.Y), W / 2f, _rng.RandfRange(5f, 8f));
            case AreaStrike.Shape.BeamV: return (new Vector2(p.X, H / 2f), _rng.RandfRange(5f, 8f), H / 2f);
            case AreaStrike.Shape.Circle: return (p, 20f, 20f);
            default: // Rect
            {
                float w = _rng.RandfRange(32f, 56f), h = _rng.RandfRange(28f, 50f);
                return (p, w / 2f, h / 2f);
            }
        }
    }

    // 『包丁の軌跡』の1本：through を通る角度 deg の一閃。長さ460px＝対角441pxを覆う
    //（通過点が画面のどこでも全画面を横断する）。色は深紅固定（catalog #d6443f）。
    private const float KnifeDeg = 26f;
    private const float KnifeLen = 460f;
    private const float KnifeHalfThick = 6f;
    private static readonly Color KnifeTint = new("d6443f");
    private static readonly Color KnifeHot = new("ff8a7a");
    private void SpawnKnifeBeam(Vector2 through, float deg, double warn)
    {
        float a = Mathf.DegToRad(deg);
        var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        var z = new AreaStrike();
        z.ConfigureBeam(dir, KnifeLen, KnifeHalfThick, warn, KnifeTint, KnifeHot);
        if (_owner != null) z.SetOwner(_owner);
        _world.AddChild(z);
        z.GlobalPosition = through - dir * (KnifeLen * 0.5f);
    }

    // 形状ごとの候補配置（自機の起点＝左側はやや空け、中央〜右に寄せる＝フェアな逃げ場）。
    private (Vector2 c, float hw, float hh) RandGeo(AreaStrike.Shape shape)
    {
        switch (shape)
        {
            // サイズは 384×216 の実画面に合わせて小さめ（自機が避けられる大きさ）。
            case AreaStrike.Shape.BeamH: // 横ビーム（全幅・細め）
                return (new Vector2(W / 2f, _rng.RandfRange(22f, H - 22f)), W / 2f, _rng.RandfRange(5f, 8f));
            case AreaStrike.Shape.BeamV: // 縦カラム（全高・細め）
                return (new Vector2(_rng.RandfRange(W * 0.30f, W * 0.92f), H / 2f), _rng.RandfRange(5f, 8f), H / 2f);
            case AreaStrike.Shape.Circle:
            {
                float rad = _rng.RandfRange(15f, 24f);
                return (new Vector2(_rng.RandfRange(W * 0.28f, W * 0.9f), _rng.RandfRange(rad, H - rad)), rad, rad);
            }
            default: // Rect
            {
                float w = _rng.RandfRange(32f, 56f), h = _rng.RandfRange(28f, 50f);
                return (new Vector2(_rng.RandfRange(W * 0.30f, W * 0.88f), _rng.RandfRange(h / 2f, H - h / 2f)), w / 2f, h / 2f);
            }
        }
    }

    // 重なり判定：同方向の予測線（横×横／縦×縦）と、面どうし（円/矩形）は重ねない。
    // 横ビーム×縦カラムの交差や、線×面は“別物として読める”ので許可する（過剰に弾かない）。
    private bool OverlapsAny(AreaStrike.Shape s, Vector2 c, float hw, float hh)
    {
        const float gap = 8f;
        bool sH = s == AreaStrike.Shape.BeamH, sV = s == AreaStrike.Shape.BeamV;
        bool sArea = s == AreaStrike.Shape.Circle || s == AreaStrike.Shape.Rect;
        foreach (var p in _placed)
        {
            bool pH = p.shape == AreaStrike.Shape.BeamH, pV = p.shape == AreaStrike.Shape.BeamV;
            bool pArea = p.shape == AreaStrike.Shape.Circle || p.shape == AreaStrike.Shape.Rect;
            if (sH && pH) { if (Mathf.Abs(c.Y - p.c.Y) < hh + p.hh + gap) return true; }
            else if (sV && pV) { if (Mathf.Abs(c.X - p.c.X) < hw + p.hw + gap) return true; }
            else if (sArea && pArea)
            {
                if (Mathf.Abs(c.X - p.c.X) < hw + p.hw + gap && Mathf.Abs(c.Y - p.c.Y) < hh + p.hh + gap) return true;
            }
        }
        return false;
    }
}
