using Godot;

// CameoBoss : 道中の“チラ見せ”ミニボス（レイ/あかり/こはる共通）。
//
// 旧実装（CameoHit＋その場で揺れる Sprite2D＋保険タイマー退場）を廃し、本戦ボス(BossRei/Akari/Koharu)と
// 同じ土台＝Enemy 派生＋シールド制サイクル＋BossMover で“ちゃんとしたミニボス”に作り替えたもの。
//   ・敵グループ("enemies")に入る＝自機弾で普通に当たる（QA/オートが撃てる＝ソフトロック回避）。
//   ・シールド制フル採用：SHIELDED（パネル周回・弾幕）→ 全パネル破壊で BREAK → EXPOSED（無防備窓で本体HP減）
//     → RECLOSE（弱気セリフ→パネル再生成）→ SHIELDED の反復。ただし道中相当に縮小（下の Tunables 参照）。
//   ・BossMover で巡航しながら弾幕。VisualOffset/Lean/FacingLeft を立ち絵へ反映。
//   ・保険タイマー退場は無し。本体HPを削り切る（=サイクル完了で Redeem）まで撃破されない＝Stage は進めない。
//
// ステージ差分（テクスチャ/セリフ/弾幕テーマ/色/BGM/オーラ）は CameoTheme で外から差し替える。
// 3サブクラス量産は避け、このクラス1つを各 Stage がパラメータ設定して使う。
//
// 撃破（=Redeem）時：RewardCameoDefeat（スコア+2000）＋手応え演出（Enemy.Redeem が PurifyBurst/
// Hitstop を出す）。その後キャラ別の捨て台詞を一行オーバーレイで流し切ってから Finished=true。
// Stage は Finished を見て本ボス前の次フェーズへ Advance する。

// 弾幕テーマ（ステージごとに差し替える攻撃の作法）。
public enum CameoFireTheme { ReiAggressive, AkariGrief, KoharuFalling }

// ステージ差分の設定一式（Stage が new して CameoBoss.Theme に渡す）。
public struct CameoTheme
{
    public string DisplayName;     // ボスバー＆セリフ話者名（例 "レイ"）
    public string Handle;          // ボスバーのハンドル（例 "@rei_____"）
    public string PreTex, CryTex, PostTex; // 立ち絵（穢れ/泣き/笑顔）
    public string Face;            // 会話の顔アイコン（例 "res://char/rei_face.png"）
    public Color SpellTint;        // 弾の色（スペル基調色）
    public BulletShape SpellShape; // 弾形
    public CameoFireTheme Fire;    // 弾幕パターンの作法
    public FxLayer.BossAura Aura;  // 徘徊オーラ
    public AudioStream? Bgm;       // 登場時に流すテーマ（null可。実音源/合成どちらも受ける）

    // セリフ：登場の第一声(who=2)・戦闘中の挑発(who=2)群・撃破後の締め(who=2)群。
    public (int who, string text, string face)[] IntroLines; // 登場の掛け合い（FirstBossLine で本人の一声だけ拾う）
    public (int who, string text, string face)[] TauntLines; // RECLOSE で順送りする挑発（弱気セリフの代わり）
    public (int who, string text, string face)[] DefeatLines;// 撃破後の捨て台詞（本人 who=2 行だけ一行オーバーレイで流す）
}

public partial class CameoBoss : Enemy
{
    // ───────── スケール調整用の定数（道中ミニボス相当に縮小。本戦ボスより控えめ）─────────
    // 後で調整しやすいよう、ここ1か所に集約する。総HP = Enemy.BarHp(100) × CameoBars。
    private const int CameoBars = 2;          // HPバー本数（=サイクル数の目安。本戦は難易度別3〜6本）
    private const int CameoPanels = 3;        // 周回パネル枚数（本戦5より少なめ）
    private const int CameoPanelInk = 2;      // 1パネルの耐久（剥がすのに要する被弾数）
    private const float CameoOrbitR = 24f;    // パネル周回半径
    private const float CameoSpin = 1.0f;     // パネル周回速度(rad/s)
    private const float CameoBodyR = 9f;      // 本体当たり半径
    private const float CameoBodyH = 50f;     // 立ち絵の表示高
    private const int CameoPoints = 700;      // 浄化スコア（本戦1500より控えめ）
    private const float CameoBulletSpd = 80f; // 基準弾速

    // 徘徊ゾーン（盤面上部・右寄り～中央。自機側＝左下まで降りすぎない）。盤面の矩形は Field が定義元。
    //   中心Xは盤面中心の +18px（Left=0 の従来値 210 に一致）、半幅は盤面幅の 80/384。
    private static readonly Vector2 ZoneCenter = new Vector2(Field.CenterX + 18f, 72f);
    private const float ZoneHalfW = Field.Width * (80f / 384f);
    private const float ZoneHalfH = 26f;
    private const float RoamSpeed = 40f;

    // 捨て台詞の一行オーバーレイ1行あたりの表示秒（旧 CameoHit.PostLineDur を踏襲）。
    private const double DefeatLineDur = 2.4;

    // Stage がスポーン前に設定するテーマ（_Ready より前に代入される前提）。
    public CameoTheme Theme;

    // 撃破→捨て台詞を流し切ったら true（Stage が見て次フェーズへ Advance）。
    public bool Finished { get; private set; }

    private readonly BossMover _mover = new BossMover();
    private double _fireT;
    private double _fireT2;
    private float _ringOff;

    // ── 中ボスの立ち位置サイクル（2026-09-07）──
    // 旧実装は BossMover の旧シグネチャ（ゾーン指定）を呼んでいたため、状態機械にも立ち位置にも
    // 繋がらず「Ring の立ち位置＝ゾーン中心に着いたら、あとは一生ホバーするだけ」だった
    //（実測: 戦闘中の x が 300 から1pxも動かない＝ユーザー所感「ふよふよして動かない」の正体）。
    // 中ボスは尺が短いので本戦ボスのようなスペル切替を持たない。代わりに、撃った攻撃の種類を
    // そのまま「次の立ち位置」として宣言する＝2種の弾幕を交互に撃つあいだ、寄る／離れるを往復する。
    // 立ち位置そのものは BossMover.Attack が決める（Aimed=自機の x を追って寄る／Ring=中央に据わる／
    // Wall=自機と反対の端へ離れる／Spell=中央の高めで動かない）。
    // ステージ別の性格は Configure の section 名で分ける（下の MoverSection）。
    private BossMover.Attack _stance = BossMover.Attack.Ring;

    // 撃った攻撃の種類を BossMover へ伝える。同じ種類が続く間は立ち位置を動かさない
    //（SetNextAttack が同値を弾く）＝撃つたびに目標が飛んで震えることはない。
    private void Declare(BossMover.Attack a)
    {
        _stance = a;
        _mover.SetNextAttack(a);
        _mover.OnAttack(a);
    }

    // ステージごとの性格（BossMover.Configure の section 名。値は config/boss_stats.ini の同名節）。
    //   レイ  ＝ 配信の枠から出ない（本戦のレイと同じ "cameo_rei"：横移動が小さく傾きで演じる）
    //   あかり＝ 重い（座ったまま滑る。加速が遅く上下に揺れない）
    //   こはる＝ 小刻み（軽く速い。構えて一瞬止まってから鋭く出る）
    private string MoverSection => Theme.Fire switch
    {
        CameoFireTheme.ReiAggressive => "cameo_rei",
        CameoFireTheme.AkariGrief    => "cameo_akari",
        _                            => "cameo_koharu",
    };
    private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

    // 撃破後の捨て台詞ドライバ（Enemy.Redeem→OnCryStart で起動）。
    private bool _defeatSeq;
    private int _defeatIdx;
    private double _defeatT;

    protected override void OnEnemyReady()
    {
        // 主要バランス値は INI（config/boss_stats.ini [cameo]＝3ステージの中ボス共通）で上書き可。
        // 第3引数＝現行既定値（上の Tunables 定数）。
        Points = BossTuning.I("cameo", "points", CameoPoints);
        BodyRadius = BossTuning.F("cameo", "body_radius", CameoBodyR);
        PanelCount = BossTuning.I("cameo", "panel_count", CameoPanels);
        PanelInk = BossTuning.I("cameo", "panel_ink", CameoPanelInk);
        OrbitRadius = BossTuning.F("cameo", "orbit_radius", CameoOrbitR);
        SpinSpeed = BossTuning.F("cameo", "spin_speed", CameoSpin);
        PanelsFire = false;            // 弾は本体の弾幕に集約（パネルは撃たない＝本戦ボスと同様）
        EnemyBulletSpeed = BossTuning.F("cameo", "bullet_speed", CameoBulletSpd);
        BarCount = Mathf.Max(1, BossTuning.I("cameo", "hp_bars", CameoBars)); // HPバー方式ON（総HP=BarHp×本数）

        PreTexPath = Theme.PreTex;
        CryTexPath = Theme.CryTex;
        PostTexPath = Theme.PostTex;
        BodyDisplayH = CameoBodyH;
        // v3 のちび（360px の全身立ち）は足元に余白があり、絵の重心が判定中心より約4px上に来る
        // （前タスクの実測。表示高50pxに対し約8%＝無防備窓の円が胸〜頭に乗り脚がはみ出す）。
        // 姿勢オフセット表の "cameo" 行で絵を下げ、円の中心へ寄せる。判定（BodyRadius）は不変。
        BodyOffsetName = "cameo";
        // 撃破の会話尺いっぱい cry を保持し、流し切った EndCryNow で post（笑顔）へ着地。
        CryHoldDur = 9999.0;

        // 登場演出はカメオ簡易版：短く・揺れ控えめ（道中のテンポを削らない。本戦の見得は本戦だけ豪華に）。
        EntranceDur = 0.85;
        EntranceShake = 1.6f;
    }

    public override void _Ready()
    {
        _rng.Randomize();
        base._Ready();
        if (Audio.Instance != null && Theme.Bgm != null) Audio.Instance.Music(Theme.Bgm);
        // 移動：状態機械＋立ち位置つき（本戦ボスと同じ作法）。速度・立ち位置は ini の
        // [cameo_rei]/[cameo_akari]/[cameo_koharu] 節で上書きでき、無ければ BossMover 側の性格既定値。
        _mover.Configure(MoverSection, ZoneCenter, ZoneHalfW, ZoneHalfH);
        _mover.SetNextAttack(_stance);
        SetSpellVisual(Theme.SpellShape, Theme.SpellTint);

        // カメオ用ボスバー（本戦ボスと同じ複数ゲージ式）。本ボス前なので時系列は重ならない。
        GetHud()?.ShowBossBar(Theme.DisplayName, Theme.Handle);
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);

        // 登場の第一声（弾を止めない一行オーバーレイ）。
        string first = FirstBossLine(Theme.IntroLines);
        if (!string.IsNullOrEmpty(first))
            GetHud()?.ShowBossLine(Theme.DisplayName, first, UiKit.Kegare, 2.6);
    }

    protected override void UpdateMovement(double delta)
    {
        // 自機の x は毎フレーム渡す（自機狙いの横滑りと、向きの反転判定に要る）。
        if (GetTree().GetFirstNodeInGroup("player") is Node2D pl) _mover.SetPlayerX(pl.GlobalPosition.X);
        GlobalPosition = _mover.Step(GlobalPosition, delta);
        ApplyBossMotion(_mover.VisualOffset, _mover.Lean, _mover.FacingLeft, _mover.SquashScale);
        FxLayer.Instance?.EmitBossAura(Theme.Aura, GlobalPosition, (float)delta, 30f);
        FirePattern(delta);
    }

    // 弾幕：ステージのテーマ別。SHIELDED 中に撃ち、無防備窓中は止めない（本戦ボスと同じく常時撃つ）。
    private void FirePattern(double delta)
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        SetSpellVisual(Theme.SpellShape, Theme.SpellTint);
        switch (Theme.Fire)
        {
            case CameoFireTheme.ReiAggressive: FireRei(pool, delta); break;
            case CameoFireTheme.AkariGrief:    FireAkari(pool, delta); break;
            default:                           FireKoharu(pool, delta); break;
        }
    }

    // レイ：詰める。自機狙いの扇＝自機の側へ寄る／回転リング＝中央に据わって撒く。
    // 弾の間隔・数・速さは一切変えていない（Declare は立ち位置と一拍だけを動かす）。
    private void FireRei(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        if (_fireT >= Di(0.95)) { _fireT = 0; Declare(BossMover.Attack.Aimed); Aimed(pool, 3, 13f, 96f); }
        if (_fireT2 >= Di(1.2)) { _fireT2 = 0; Declare(BossMover.Attack.Ring); Ring(pool, Dn(12), CameoBulletSpd * 0.85f); }
    }

    // あかり：悲嘆の雨（上から降る自責）＝画面幅の帯なので端へ離れて張る／本人周りの弱いリング＝中央。
    private void FireAkari(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        if (_fireT >= Di(1.0)) { _fireT = 0; Declare(BossMover.Attack.Wall); RainDown(pool, Dn(7), CameoBulletSpd * 0.9f); }
        if (_fireT2 >= Di(1.4)) { _fireT2 = 0; Declare(BossMover.Attack.Ring); Ring(pool, Dn(8), CameoBulletSpd * 0.55f); }
    }

    // こはる：落ちる祈り（上から落ちる弾）＝帯なので端へ／足元からの下向きの扇＝自機の側へ寄って落とす。
    private void FireKoharu(BulletPool pool, double delta)
    {
        _fireT += delta; _fireT2 += delta;
        if (_fireT >= Di(1.1)) { _fireT = 0; Declare(BossMover.Attack.Wall); RainDown(pool, Dn(8), CameoBulletSpd * 0.95f); }
        if (_fireT2 >= Di(1.5)) { _fireT2 = 0; Declare(BossMover.Attack.Aimed); FanDown(pool, Dn(5), 50f, CameoBulletSpd * 0.7f); }
    }

    // ── 弾幕プリミティブ ──
    private void Ring(BulletPool pool, int k, float spd)
    {
        _ringOff += Mathf.DegToRad(9f);
        for (int i = 0; i < k; i++)
        {
            float a = _ringOff + Mathf.Tau * i / Mathf.Max(1, k);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * spd, 3.2f);
        }
    }

    private void Aimed(BulletPool pool, int fan, float stepDeg, float spd)
    {
        Vector2 d = AimAtPlayer();
        float baseA = Mathf.Atan2(d.Y, d.X);
        int half = fan / 2;
        for (int i = -half; i <= half; i++)
        {
            float a = baseA + i * Mathf.DegToRad(stepDeg);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * spd, 3.2f);
        }
    }

    private void RainDown(BulletPool pool, int k, float spd)
    {
        for (int i = 0; i < k; i++)
        {
            float x = 20f + 344f * (i + 0.5f) / Mathf.Max(1, k);
            FireBullet(pool, new Vector2(x, -6f), new Vector2(_rng.RandfRange(-8f, 8f), spd), 3.2f);
        }
    }

    private void FanDown(BulletPool pool, int fan, float spreadDeg, float spd)
    {
        for (int i = 0; i < fan; i++)
        {
            float a = Mathf.Pi / 2f + Mathf.DegToRad(((float)i / Mathf.Max(1, fan - 1) - 0.5f) * spreadDeg);
            FireBullet(pool, GlobalPosition, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * spd, 3.2f);
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

    // 本体に弾が刺さった一拍を移動側へ渡す（小さくのけぞって戻る）。当たり判定の中心は
    // 最大4px・時定数0.12sでしか動かない＝弾避けの公平性は保つ（BossMover.OnHit のコメント参照）。
    protected override void OnBodyDamaged(Vector2 fromDir) => _mover.OnHit(fromDir);

    protected override void OnHpChanged()
    {
        GetHud()?.UpdateBossBar(CurrentBarIndex, TotalBars, CurrentBarFrac);
    }

    // RECLOSE のキャラ別弱気セリフ（TauntLines の本人 who=2 行をサイクルごとに順送り）。
    private int _tauntIdx;
    protected override void OnRecloseLine()
    {
        string? line = NextBossLine(Theme.TauntLines, ref _tauntIdx);
        _tauntIdx++; // 次サイクルは次の行へ（尽きたら以降は出ない＝静かに再生成）
        if (line != null) ShowRecloseLine(Theme.DisplayName, line);
        // 言い返しながらパネルを張り直す一拍＝宣告の立ち位置（中央の高めに据わって動かない）。
        // 次に弾を撃った時点で Declare が通常の立ち位置へ戻すので、据わるのはこのサイクルの間だけ。
        _mover.SetNextAttack(BossMover.Attack.Spell);
    }

    // カメオはフォロワー化しない（味方化は本戦ボスのみ）。
    protected override void GrantFollower() { }

    // ── 撃破（Redeem=サイクル完了/HP0）後の締め ──
    // Enemy.Redeem が Reward/手応え演出（PurifyBurst/Hitstop）の作法を持つが、カメオ専用の報酬
    // （RewardCameoDefeat＝スコア+2000）はここで1回だけ付与する。
    // その後、捨て台詞を一行オーバーレイで流し切ってから EndCryNow→OnCryEnd で Finished を立てる。
    protected override void OnCryStart()
    {
        GetHud()?.HideBossBar(); // 撃破でバーを必ず Hide（本ボスバーと競合しない）
        GetNodeOrNull<GameManager>("/root/Game")?.RewardCameoDefeat();
        Audio.Instance?.PlayRedeem(0);
        _defeatSeq = true; _defeatIdx = -1; _defeatT = DefeatLineDur; // 即・最初の行へ
    }

    protected override void OnCryEnd()
    {
        Finished = true;
        // 道中へ戻るのでステージBGMへ復帰する。撃破時の PlayRedeem は「一度きり（ループしない）」の
        // 2.6秒ワンショットなので、ここで戻さないと道中後半が鳴り切ったあと無音のままになる（#BGM消失）。
        if (Audio.Instance != null) Audio.Instance.ResumeStageMusic();
    }

    // 保険タイムアウトで cry が強制終了されたとき、捨て台詞ドライバも畳む。
    protected override void AbortCrySequence() => _defeatSeq = false;

    public override void _Process(double delta)
    {
        if (!_defeatSeq) return;
        _defeatT += delta;
        if (_defeatT < DefeatLineDur) return;
        _defeatT = 0;
        _defeatIdx++;
        NotifyCryProgress(); // 自動送りだが、進んでいる間は保険タイムアウトを起こさない
        string? line = NextBossLine(Theme.DefeatLines, ref _defeatIdx);
        if (line == null)
        {
            _defeatSeq = false;
            EndCryNow(); // cry→post（笑顔）へ着地し OnCryEnd（Finished=true）
            return;
        }
        GetHud()?.ShowBossLine(Theme.DisplayName, line, UiKit.Kegare, DefeatLineDur);
    }

    private Hud? GetHud() => GetTree().GetFirstNodeInGroup("hud") as Hud;

    // ── セリフ配列ヘルパ（旧 CameoHit から移設）──
    // 会話配列(who, text, face)[] から「本人(who=2)の最初の発話」を1行返す（登場の第一声用）。
    private static string FirstBossLine((int who, string text, string face)[] lines)
    {
        foreach (var (who, text, _) in lines)
            if (who == 2) return text;
        return lines.Length > 0 ? lines[0].text : "";
    }

    // 配列から who=2 の行を idx 番目以降で次に探して返す。無ければ null。
    // idx は呼び出し側が見つかった位置を受け取り、++ してから次回に渡す前提。
    private static string? NextBossLine((int who, string text, string face)[] lines, ref int idx)
    {
        for (; idx < lines.Length; idx++)
            if (lines[idx].who == 2) return lines[idx].text;
        return null;
    }
}
