using Godot;

// Job : ラン単位で1つだけ選ぶ「型」と、その補正値の一覧。
//   設計書 docs/20260907/ジョブシステム設計_2026-09-07.md（§2）が正典。数値はここに一本化し、
//   Player / Enemy / Panel / GameManager は必ず JobTuning 経由で読む（各所に生の係数を散らさない）。
//
// 4ジョブとショットモードは 1対1 で固定する（設計書 §3）。プレイヤーがモードを直接選ぶ操作（V の循環）は
// 廃止したので、GameManager.SelectedShotMode は「ジョブが決める従属値」＝ Job から毎回導出される。
//   灯し手(Melee)=加速球 ／ 祈り手(Heal)=ホーミング ／ 結び手(Tank)=連射 ／ 語り手(Magic)=拡散
//
// ★enum の並びは保存値（save_N.json の "job"）になるので末尾追加のみ。既存値の入れ替え禁止。
//   セーブに "job" が無い旧データは Tank（結び手＝初期選択）へ落ちる（GameManager.LoadFromSlot）。
//
// ★2026-09-14 解禁制：最初は結び手（ミナ）だけ。あかり／こはる／レイのジョブは、その子の面を
//   クリアすると開く（JobTuning.UnlockStageId）＝救った子が隣に立ってくれる、という意味を持たせる。
//   判定は GameManager._cleared（IsStageCleared）だけを見る＝新しい永続項目は足さない＝既存セーブは
//   読み込んだ時点で自動的に正しい解禁状態になる（Hub.ShopUnlocked と同じ流儀）。
public enum Job
{
    Tank = 0,   // 結び手（耐久・連射）← 初期選択。一番素直で死ににくい
    Melee = 1,  // 灯し手（近接・加速球）
    Heal = 2,   // 祈り手（回復・ホーミング）
    Magic = 3,  // 語り手（魔法・拡散）
}

// 1ジョブぶんの補正値。すべて「基準値に掛ける／足す」形で持ち、既定（Tank 以外の素の値）は
// 各フィールドのコメントに書いた基準と一致させる＝ジョブを外しても挙動が変わらない構造にする。
public sealed class JobTuning
{
    public Job Id;
    // ★2026-09-17 ユーザー指示：ジョブ名（結び手/灯し手/祈り手/語り手）と型名（耐久/近接/回復/魔法）の
    //   表記は UI から全廃し、画面にはキャラクター名（ミナ/あかり/こはる/レイ＝CharacterName）だけを出す。
    //   旧 JobTuning.Name はこの決定で消した（画面表記の入口を残すと再び漏れる）。enum の識別子
    //   （Job.Tank 等）と --job= の英語トークンは内部表現なので従来どおり。ログも CharacterName で出す。
    public string CharacterId = "";
    public string CharacterName = "";
    public string PlayerTexturePath = "";
    // 解禁条件のステージID（GameManager.Stages の Id）。空＝最初から選べる（結び手のみ）。
    //   キャラ＝そのステージのボス本人なので、条件は素直に「その子の面をクリアする」＝救った子が隣に立つ。
    public string UnlockStageId = "";
    public GameManager.ShotMode Mode;        // このジョブが固定で使うショットモード

    // ── 体力・被弾 ──
    public int MaxLifeDelta;                 // 最大♥の増減（基準0）
    public float HitInvulSec = 1.2f;         // 被弾無敵（基準1.2秒＝Player.InvincibleDuration）
    public bool NoHitKnockback;              // 被弾のけぞりの「変位」を殺すか（結び手の踏みとどまり）

    // ── 機動 ──
    public float MoveMul = 1f;               // 移動速度倍率（基準1.0）
    public float DodgeDistMul = 1f;          // 回避距離倍率（基準1.0）
    public float DodgeCdMul = 1f;            // 回避クールダウン倍率（基準1.0・小さいほど速く回る）
    public float CloseDodgeCdMul = 1f;       // 密着圏(CloseRange)に居る間だけ重ねる回避CD倍率（基準1.0）

    // ── 威力（ボス本体・パネル共通で効く基礎倍率）──
    public float PowerMul = 1f;              // 弾の基礎威力倍率（基準1.0）

    public string ChargeDescription = "";
    public int ChargeWays = 1;
    public float ChargeSpreadDegrees;
    public int ChargePower = 12;
    public float ChargeSpeed = 820f;
    public float ChargeRadius = 9f;
    public int ChargePierce = 5;

    // ── 距離ボーナス（近接の利／遠隔の利。互いに鏡像）──
    public bool CritEnabled = true;          // 密着クリティカルが成立するか
    public float CritMult = 1.25f;           // 密着クリ倍率（基準＝非近接ジョブの 1.25）
    public int CritCap = 5;                  // 密着クリ時の1ヒット上限（基準5）
    public float FarMult = 1f;               // 遠隔ボーナス倍率（基準1.0＝無し）

    // ── 祈り手の回復回路 ──
    public int DrainPerLife;                 // 雑魚をこの体数浄化するごとに♥+1（0=無効）
    public bool BombOnBreak;                 // BREAK 成立ごとに BOMB+1
    public float VeilFloorRadius;            // 祈りの帳を未購入でも常時持つときの半径(px)（0=無し）
    public float VeilFloorDuration;          // 同・持続(秒)
}

public static class Jobs
{
    // 密着／遠隔の距離しきい値。設計書 §2 の 48px（近接）と 120px（遠隔）。
    // 48 は Enemy 側の既存スイートスポット(PointBlankRange)と同値＝判定の見え方を変えない。
    public const float CloseRange = 48f;
    public const float FarRange = 120f;

    // ジョブごとの補正表（設計書 §2）。値の根拠はコメントに書く。
    private static readonly JobTuning[] Table =
    {
        // ── 結び手（耐久・連射）＝初期選択。避けるのではなく耐える ──
        new()
        {
            Id = Job.Tank, Mode = GameManager.ShotMode.Rapid,
            CharacterId = "mina", CharacterName = "ミナ", PlayerTexturePath = "res://char/player/mina/mina_idle_v2.png",
            ChargeDescription = "直線を貫く高速弾",
            MaxLifeDelta = +2,
            HitInvulSec = 1.8f,        // 1.2→1.8。連鎖被弾を潰す
            NoHitKnockback = true,     // 踏みとどまり＝手触りの本体
            MoveMul = 0.88f,
            DodgeDistMul = 0.9f,
        },

        // ── 灯し手（近接・加速球）＝無防備窓の瞬間火力 ──
        new()
        {
            Id = Job.Melee, Mode = GameManager.ShotMode.Accel,
            CharacterId = "akari", CharacterName = "あかり", PlayerTexturePath = "res://char/player/akari/akari_idle_v2.png",
            UnlockStageId = "akari",
            ChargeDescription = "高威力の加速弾",
            ChargePower = 18, ChargeSpeed = 960f, ChargeRadius = 12f, ChargePierce = 1,
            MaxLifeDelta = -1,
            CritMult = 2.0f,           // 他ジョブ ×1.25 に対し ×2.0
            CritCap = 8,               // 本体1ヒット上限(8)と同値＝クリの伸びしろを潰さない
            CloseDodgeCdMul = 0.75f,   // 48px以内に居る間だけ回避CDが縮む
        },

        // ── 祈り手（回復・ホーミング）＝削られても戻せる。そのぶん一発が軽い ──
        new()
        {
            Id = Job.Heal, Mode = GameManager.ShotMode.Homing,
            CharacterId = "koharu", CharacterName = "こはる", PlayerTexturePath = "res://char/player/koharu/koharu_idle_v2.png",
            UnlockStageId = "koharu",
            ChargeDescription = "3発の追尾弾",
            ChargeWays = 3, ChargeSpreadDegrees = 32f,
            ChargePower = 4, ChargeSpeed = 300f, ChargeRadius = 6f, ChargePierce = 1,
            // 火力は4ジョブ最遅（設計書 §2）。ホーミング自体が既に ×0.85（HomingPowerMul）なので、
            // ここを強く掛けると基礎威力1の序盤で下限(max(1,…))に張り付いて差が消える。
            // ×0.8 なら 0.85×0.8=0.68＝実効で最遅を保ちつつ、強化が伸びた終盤でも
            // 語り手(0.50×1.0、遠隔で×1.3)・結び手(1.0)との序列が入れ替わらない。
            PowerMul = 0.8f,
            DrainPerLife = 24,         // 浄化ドレイン。満タン時はカウンタを進めない（GameManager 側で担保）
            BombOnBreak = true,
            VeilFloorRadius = 14f,     // veil 未購入でも「小さく」常時（Lv1=20px より小さい＝購入の意味を残す）
            VeilFloorDuration = 0.4f,  // 同上（Lv1=0.5s より短い）
        },

        // ── 語り手（魔法・拡散）＝面の制圧。近づかれたら逃げる手段が薄い ──
        new()
        {
            Id = Job.Magic, Mode = GameManager.ShotMode.Spread,
            CharacterId = "rei", CharacterName = "レイ", PlayerTexturePath = "res://char/player/rei/rei_idle_v2.png",
            UnlockStageId = "rei",
            ChargeDescription = "5方向の拡散弾",
            ChargeWays = 5, ChargeSpreadDegrees = 64f,
            ChargePower = 3, ChargeSpeed = 540f, ChargeRadius = 6f, ChargePierce = 1,
            CritEnabled = false,       // 密着クリ無効（近接の鏡像）
            FarMult = 1.3f,
            DodgeCdMul = 1.15f,
        },
    };

    // 全ジョブ（ハブの選択画面・デバッグ表示が並べる順。Tank が先頭＝初期選択）。
    public static JobTuning[] All => Table;

    public static JobTuning Get(Job j)
    {
        foreach (var t in Table)
            if (t.Id == j) return t;
        return Table[0]; // 破損セーブ等の保険＝結び手へ落とす
    }

    // コマンドライン --job=melee|heal|tank|magic の解決（Main が読む）。未知の語は null。
    //   英語トークンは enum 名そのもの＝内部表現なので据え置き。和名の別名は 2026-09-17 に
    //   ジョブ名（結び手…）からキャラクター名（ミナ…）へ差し替えた＝画面と同じ語で呼べる。
    public static Job? Parse(string s) => s.ToLowerInvariant() switch
    {
        "tank" or "mina" or "ミナ" => Job.Tank,
        "melee" or "akari" or "あかり" => Job.Melee,
        "heal" or "koharu" or "こはる" => Job.Heal,
        "magic" or "rei" or "レイ" => Job.Magic,
        _ => null,
    };
}

// ChargeTier : 溜め打ちの「段」（2026-09-25 ユーザー決定「2段階チャージ」）。
//
//   1段（誰でも最初から）  ChargeNeedSec 秒（0.60）で満ちる。弾は JobTuning の素の値そのまま。
//   2段（ショップ n_charge）さらに押し続けると満ちる。溜め時間は**倍**（合計 1.20 秒）で、
//                           威力・弾の大きさ・【激情】の変化量が伸びる。1段で離せば従来どおりの弾が出る。
//
// ★数値はここ一箇所に集める。実機で触ってから調整する前提なので、config/boss_stats.ini の
//   [charge] セクション（後勝ち）からも差し替えられる＝再ビルド無しで詰められる。
//   キー: hold_mul / power_mul / radius_mul / fury_mul（下の Default* と同名の意味）。
public static class ChargeTier
{
    // 段の呼び名。Bullet / Player / Fx が「何段目か」をこの値で受け渡す。
    public const int First = 1;
    public const int Second = 2;

    // 2段目が満ちるまでの長押し秒 ＝ 1段目 × HoldMul。作者指示の「倍」がそのまま既定。
    public const float DefaultHoldMul = 2.0f;
    // 2段目の威力倍率。×4 の大玉（1段）をさらに倍にすると、ボス本体の1ヒット上限 32（Enemy.cs）へ
    //   結び手 12×2=24 ／ 灯し手 18×2=36（上限で頭打ち）と、上限に触れるか触れないかの辺りに収まる。
    //   「倍待って倍痛い」が素直に読める値として ×2.0 を初期値に置く。
    public const float DefaultPowerMul = 2.0f;
    // 2段目の弾半径倍率（見た目と当たり判定の両方。Bullet.Radius が描画も判定も決める）。
    //   ×1.6＝結び手 9→14.4px。自機(36px)より小さく保ちつつ、並べれば一目で「太い」と分かる。
    public const float DefaultRadiusMul = 1.6f;
    // 2段目で当てたときの【激情】変化量の倍率。★受け皿だけ用意し、実際に動かすのは次段（FuryMeter.cs 参照）。
    public const float DefaultFuryMul = 2.0f;

    public static float HoldMul => BossTuning.F("charge", "hold_mul", DefaultHoldMul);
    public static float PowerMul => BossTuning.F("charge", "power_mul", DefaultPowerMul);
    public static float RadiusMul => BossTuning.F("charge", "radius_mul", DefaultRadiusMul);
    public static float FuryMul => BossTuning.F("charge", "fury_mul", DefaultFuryMul);

    // 段ごとの倍率（1段は必ず素の 1.0＝未購入の挙動を一切変えない）。
    public static float PowerMulFor(int stage) => stage >= Second ? PowerMul : 1f;
    public static float RadiusMulFor(int stage) => stage >= Second ? RadiusMul : 1f;
    // 【激情】の変化量倍率。第2段の「言葉を当てる」処理がこれを掛けて AddFury を呼ぶ想定。
    public static float FuryMulFor(int stage) => stage >= Second ? FuryMul : 1f;
}
