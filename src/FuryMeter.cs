using Godot;

// FuryMeter : 【激情】——ボス戦のあいだだけ動く一本のメーター（第1段・本体と表示のみ／2026-09-25）。
//
//   +100 ┃ 激情 ── 荒れている
//        ┃
//      0 ┃ ← プレイヤーはここを保つ（理想）
//        ┃
//   -100 ┃ 無感情 ── 心が消えている
//
// 正典: docs/20260925/激情メーター_台詞と選択肢_2026-09-25.md（目盛りと上昇速度）。
//       docs/20260924/エンディング3分岐_設計と本文_2026-09-24.md §2.2（道中14か所 → ボス3人の対応表）。
//
// 第1段で実装するのはこの3つだけ:
//   (1) 値の保持と、ボス戦中の自然上昇（何もしなければ荒れる）
//   (2) 道中の選択から初期値を決める純関数（既存の選択台帳を読むだけ／新しいセーブ項目は持たない）
//   (3) 画面右端の縦メーター表示（Hud.DrawFury）
// 選択肢の表示・言葉の装填・チャージショットへの紐付け・命中増減・エンディング分岐・
// 弾幕の変化・ボスの危険信号台詞は**すべて次段**。ここには書かない。
public static class Fury
{
    // 目盛り（作者決定・変更不可）。
    public const float Min = -100f;
    public const float Max = 100f;
    public const float Center = 0f;
    // 端＝失敗の境目。第1段では分岐しないが、表示（端の警告色）がこの値を見る。
    public const float RageEdge = 90f;    // これ以上＝激情エンド
    public const float NumbEdge = -90f;   // これ以下＝無感情エンド

    // 自然上昇（/秒）。暫定 +2〜3（シナリオ稿 §まとめ「効果量の数値」）。
    //   調整はここ一箇所か、config/boss_stats.ini の [fury] rise_per_sec（後勝ち）。
    //   ボス別に変えたくなったら [fury] rise_per_sec_akari 等を足す（RisePerSec が先に見る）。
    public const float DefaultRisePerSec = 2.5f;

    // そのボスの自然上昇速度（/秒）。INI が無ければ DefaultRisePerSec。
    public static float RisePerSec(string bossId)
        => BossTuning.F("fury", $"rise_per_sec_{bossId}", BossTuning.F("fury", "rise_per_sec", DefaultRisePerSec));

    // 道中の選択で傾く初期値の上限（片側）。道中だけで端（±90）に届かせない＝控えめに。
    public const float InitialClamp = 30f;

    // ── 初期値：道中の選択 → そのボスのメーター（純関数。台帳を読むだけで状態を持たない）──
    //
    // 換算の考え方（旧設計 §2.2 の【開き】Δ は「本音を開いている＝良い状態」の軸で、
    //   激情＋／無感情− とは軸が違う＝機械的には換算できない）。各選択の**意味**を、
    //   そのボスの「上げるもの／下げるもの」（2026-09-25 稿の各 STAGE 冒頭）に当てて符号を決めた:
    //
    //   あかり  上げる＝宛先・返事・確認を思い出させる言葉（待つ相手が居ると刺激する）
    //           下げる＝もう返さなくていいと許す言葉（待つ理由そのものを抜く）
    //   こはる  上げる＝「ちゃんと」「見られている」の圧・止まることを禁じる言葉
    //           下げる＝休んでいい・やめていい・見なくていい
    //   レイ    上げる＝数字・見られている・気づかれる（「見ている」が最強の燃料）
    //           下げる＝見なくていい・数えなくていい・いつも通りでいい
    //
    // 振れ幅は旧 Δ（±0.5 で満スケール）を「±30 で満スケール」に読み替えた ≒ ×60 を目安に、
    //   端に寄りすぎないよう丸めてある。合計は必ず ±InitialClamp で切る。
    public static float InitialFor(GameManager? game, string bossId)
    {
        if (game == null) return Center;
        float v = bossId switch
        {
            "akari" => InitialAkari(game),
            "koharu" => InitialKoharu(game),
            "rei" => InitialRei(game),
            _ => Center,
        };
        return Mathf.Clamp(v, -InitialClamp, InitialClamp);
    }

    private static float InitialAkari(GameManager g)
    {
        float v = 0f;
        // p4 : 冒頭。声の持ち主を知ろうとしたか、見なかったことにしたか。
        v += g.ChosenAt("p4") switch
        {
            "だれの声" => +6f,                 // 宛先を探した＝「返事を待つ相手が居る」を起こす
            "見なかったことにする" => -6f,     // 見ないと決めた＝待つ理由を抜く
            _ => 0f,
        };
        // s1_4 : 取り消された十二通を、どれだけ浴びるか。あかりの本体はその十二通。
        v += g.ChosenAt("s1_4") switch
        {
            "十二件、ぜんぶ" => +12f,          // 全部の宛先を受け取った＝最大の「届いている」
            "いちばん上の、一件だけ" => +3f,   // 一件だけ届いた
            _ => g.HasChoiceAt("s1_4") ? -6f : 0f,   // （送らない）＝二件散る
        };
        // s1_5 : 雨の中で何を返すか。
        v += g.ChosenAt("s1_5") switch
        {
            "傘、忘れてる" => +9f,             // 具体名詞＝中身を見ている（数えている）
            "宛先、ちがう" => +3f,             // 宛先を突く＝問い直し
            "ぜったい" => +4f,                 // 並走。あかりでは「一緒に居る」が執着を焚く
            _ => g.HasChoiceAt("s1_5") ? -6f : 0f,
        };
        // s1_2 : 雨の言いかけへの返し。
        v += g.ChosenAt("s1_2") switch
        {
            "置き傘、三本目" => +9f,           // 数えている＝ミナの流儀が伝染する
            "きらいじゃない" => +6f,           // 否定で肯定する、あかりと同じ話法
            "べつに" => 0f,                    // 突き放しでも歩み寄りでもない
            _ => g.HasChoiceAt("s1_2") ? -6f : 0f,
        };
        return v;
    }

    private static float InitialKoharu(GameManager g)
    {
        float v = 0f;
        // s2_1 : 壁の予定表。休む日を足すか、続ける手当てをするか、やれている証拠を返すか。
        v += g.ChosenAt("s2_1") switch
        {
            "一日、空けて" => -12f,            // 休ませる＝下げるものの直撃（好きだったことごと止まる罠）
            "三本、飲んでから" => +6f,         // 続ける前提の手当て＝「続けていい」の承認
            "全部に丸がついてる" => +12f,      // 事実の読み上げが「ちゃんとやれている」の証拠になる
            _ => g.HasChoiceAt("s2_1") ? -6f : 0f,   // （送らない）＝三件散る
        };
        // s2_2 : 消えたペンライト。嘘を壊すか、付き合うか、先を示すか。
        //   ※「電池、切れてる」（事実を突く＝止める）と「次は、点くほう」（嘘を壊さず先へ）は
        //     激情↑／無感情↓ のどちらとも読めた（§報告参照）ので、どちらも 0 に置いて反映しない。
        v += g.ChosenAt("s2_2") switch
        {
            "言わない" => -5f,                 // 嘘に付き合いきる＝先へ進めない＝そっと抜く
            "電池、切れてる" => 0f,            // ※符号を決めきれず未反映（報告済み）
            "次は、点くほう" => 0f,            // ※同上
            _ => g.HasChoiceAt("s2_2") ? -6f : 0f,   // （送らない）＝三件散る
        };
        // s2_4 : カーソルが「あなた」の側に点く。送らなかった相手を、誰と言うか。
        v += g.ChosenAt("s2_4") switch
        {
            "返してない返信" => +8f,           // まだ送れる＝「返さなきゃ」の圧が残る
            "既読のまま、三日" => +4f,         // 数字を出した＝数えさせる
            "もう送れない相手" => -12f,        // こちらが欠損を差し出す＝「ひとりでなんとかしなくても」
            _ => g.HasChoiceAt("s2_4") ? -6f : 0f,
        };
        return v;
    }

    private static float InitialRei(GameManager g)
    {
        float v = 0f;
        // s3_2 : 同接一桁の配信に、何を返すか。レイは数字に見られている人。
        v += g.ChosenAt("s3_2") switch
        {
            "同接、9" => +12f,                 // 数字で返した＝最強の燃料
            "ちゃんと見てる" => +7f,           // 「見ている」はレイでは最も強く燃える
            "見えてる" => +3f,
            _ => g.HasChoiceAt("s3_2") ? -6f : 0f,
        };
        // s3_5c : 三件の下書き。三件目はミナ自身のもの（敬体だけが手がかり）。
        v += g.ChosenAt("s3_5c") switch
        {
            "見ています" => +12f,              // 気づいた＝いちばん深く見られている
            "見てる" => +4f,
            "ここにいる" => +4f,
            _ => g.HasChoiceAt("s3_5c") ? -6f : 0f,
        };
        return v;
    }

    // 表示用の帯。第1段では色分けにしか使わない（弾幕・台詞の切り替えは次段）。
    public enum Band { Numb, Calm, Rage }
    public static Band BandOf(float v) => v >= RageEdge ? Band.Rage : v <= NumbEdge ? Band.Numb : Band.Calm;
}

// GameManager の追記ぶん（本体ファイルは別担当が編集中のため partial で分ける。ChoiceEffects.cs と同じ作法）。
public partial class GameManager
{
    // ── 【激情】のラン中状態。セーブしない（ボス戦を抜ければ用済み＝Contamination とは別物）──
    public bool FuryActive { get; private set; }
    public string FuryBossId { get; private set; } = "";
    public float FuryValue { get; private set; } = Fury.Center;
    private float _furyRise = Fury.DefaultRisePerSec;

    // [一時/デバッグ] --fury N : ボス戦開始時の初期値を N で上書きする（道中の選択を無視）。
    //   端（±90）の挙動を道中を踏まずに確かめる口。--choice / --boss と同じ流儀で、
    //   通常プレイ・配布ビルドでは付けない前提。--fury-rate R で上昇速度も差し替えられる。
    public float? DebugFuryStart { get; private set; }
    public float? DebugFuryRate { get; private set; }

    // コマンドライン引数の読み取り（ParseFuryDebugArgs は GameManager._Ready の引数解釈から呼ぶ）。
    private void ParseFuryDebugArgs()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            // "--fury 80" と "--fury=80" の両方を受ける（既存の --shot-at 系＝分離、--job= 系＝結合の両流儀があるため）。
            string a = args[i];
            if (a == "--fury" && i + 1 < args.Length && float.TryParse(args[i + 1], out float v1))
                DebugFuryStart = Mathf.Clamp(v1, Fury.Min, Fury.Max);
            else if (a.StartsWith("--fury=") && float.TryParse(a.Substring(7), out float v2))
                DebugFuryStart = Mathf.Clamp(v2, Fury.Min, Fury.Max);
            else if (a == "--fury-rate" && i + 1 < args.Length && float.TryParse(args[i + 1], out float r1))
                DebugFuryRate = r1;
            else if (a.StartsWith("--fury-rate=") && float.TryParse(a.Substring(12), out float r2))
                DebugFuryRate = r2;
        }
        if (DebugFuryStart.HasValue || DebugFuryRate.HasValue)
            GD.Print($"[FURY] debug start={DebugFuryStart?.ToString() ?? "-"} rate={DebugFuryRate?.ToString() ?? "-"}");
    }

    // ボス戦開始（各ボスの OnEnemyReady が ShowBossBar と並べて呼ぶ）。初期値は道中の選択から決まる。
    public void BeginFury(string bossId)
    {
        FuryBossId = bossId;
        FuryValue = DebugFuryStart ?? Fury.InitialFor(this, bossId);
        _furyRise = DebugFuryRate ?? Fury.RisePerSec(bossId);
        FuryActive = true;
        GD.Print($"[FURY] begin {bossId} start={FuryValue:0.0} rise={_furyRise:0.00}/s");
    }

    // ボス戦終了（改心＝各ボスの OnCryStart／面を抜ける時）。以後メーターは動かず、表示も消える。
    public void EndFury()
    {
        if (!FuryActive) return;
        GD.Print($"[FURY] end {FuryBossId} final={FuryValue:0.0} band={Fury.BandOf(FuryValue)}");
        FuryActive = false;
    }

    // 値を動かす共通入口（第2段の「言葉を当てた」ぶんもここを通す想定）。必ず端で止まる。
    public void AddFury(float delta)
    {
        if (!FuryActive) return;
        FuryValue = Mathf.Clamp(FuryValue + delta, Fury.Min, Fury.Max);
    }

    // 自然上昇。GameManager._Process から毎フレーム呼ぶ。
    //   戦闘が止まっているとき（会話バブル＝Hud.BubblePaused／改心などの割り込み＝Hud.SuppressCallouts）は
    //   上昇も止める＝「放置すると荒れる」が会話の裏で進まない（コンボ猶予の凍結と同じ理屈）。
    private void TickFury(double delta)
    {
        if (!FuryActive) return;
        if (Hud.BubblePaused) return;
        if ((GetTree()?.GetFirstNodeInGroup("hud") as Hud)?.SuppressCallouts ?? false) return;
        AddFury(_furyRise * (float)delta);
    }
}
