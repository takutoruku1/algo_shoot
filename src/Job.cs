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
    public string Name = "";                 // 画面表記（名詞形）
    public string CharacterId = "";
    public string CharacterName = "";
    public string PlayerTexturePath = "";
    // 解禁条件のステージID（GameManager.Stages の Id）。空＝最初から選べる（結び手のみ）。
    //   キャラ＝そのステージのボス本人なので、条件は素直に「その子の面をクリアする」＝救った子が隣に立つ。
    public string UnlockStageId = "";
    public string TypeName = "";             // タイプ（近接／回復／耐久／魔法）
    public string Strength = "";             // 得意（選択画面の1行）
    public string Weakness = "";             // 捨てる（選択画面の1行）
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
            Id = Job.Tank, Name = "結び手", TypeName = "耐久", Mode = GameManager.ShotMode.Rapid,
            CharacterId = "mina", CharacterName = "ミナ", PlayerTexturePath = "res://char/player/mina/mina_idle_v2.png",
            Strength = "被弾しても止まらない。最大♥ +2／無敵1.8秒／のけぞらない",
            Weakness = "機動力。移動 ×0.88／回避距離 ×0.9",
            MaxLifeDelta = +2,
            HitInvulSec = 1.8f,        // 1.2→1.8。連鎖被弾を潰す
            NoHitKnockback = true,     // 踏みとどまり＝手触りの本体
            MoveMul = 0.88f,
            DodgeDistMul = 0.9f,
        },

        // ── 灯し手（近接・加速球）＝無防備窓の瞬間火力 ──
        new()
        {
            Id = Job.Melee, Name = "灯し手", TypeName = "近接", Mode = GameManager.ShotMode.Accel,
            CharacterId = "akari", CharacterName = "あかり", PlayerTexturePath = "res://char/player/akari/akari_idle_v2.png",
            UnlockStageId = "akari",
            Strength = "密着すると一撃が2倍（上限8）。近いほど回避が速く戻る",
            Weakness = "安全な距離。最大♥ −1",
            MaxLifeDelta = -1,
            CritMult = 2.0f,           // 他ジョブ ×1.25 に対し ×2.0
            CritCap = 8,               // 本体1ヒット上限(8)と同値＝クリの伸びしろを潰さない
            CloseDodgeCdMul = 0.75f,   // 48px以内に居る間だけ回避CDが縮む
        },

        // ── 祈り手（回復・ホーミング）＝削られても戻せる。そのぶん一発が軽い ──
        new()
        {
            Id = Job.Heal, Name = "祈り手", TypeName = "回復", Mode = GameManager.ShotMode.Homing,
            CharacterId = "koharu", CharacterName = "こはる", PlayerTexturePath = "res://char/player/koharu/koharu_idle_v2.png",
            UnlockStageId = "koharu",
            Strength = "雑魚24体の浄化ごとに♥+1／BREAK ごとに BOMB+1／帳を常時持つ",
            Weakness = "一発の重さ。4ジョブで最も遅い（威力 ×0.8）",
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
            Id = Job.Magic, Name = "語り手", TypeName = "魔法", Mode = GameManager.ShotMode.Spread,
            CharacterId = "rei", CharacterName = "レイ", PlayerTexturePath = "res://char/player/rei/rei_idle_v2.png",
            UnlockStageId = "rei",
            Strength = "面を取る。120pxより遠くから当てた弾は威力 ×1.3",
            Weakness = "至近戦。密着クリ無効／回避クールダウン ×1.15",
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
    public static Job? Parse(string s) => s.ToLowerInvariant() switch
    {
        "tank" or "結び手" => Job.Tank,
        "melee" or "灯し手" => Job.Melee,
        "heal" or "祈り手" => Job.Heal,
        "magic" or "語り手" => Job.Magic,
        _ => null,
    };
}
