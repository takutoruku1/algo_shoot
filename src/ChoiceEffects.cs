using Godot;
using System.Collections.Generic;

// ChoiceEffects : 道中の下書き選択（6か所）の共通処理と、選択が下流の場面へ効く「効果」の窓口。
//   正典: wiki/08_仮台本/17_道中の選択肢_案C.md（ユーザー承認済み・2026-09-06）。
//
// 既存の選択（P2・P3・P4・S1-4・S3-7・F4・E2・E6）はそれぞれの場面が直に RecordChoice を呼んでいるが、
// 道中の6か所は「3択＋（送らない）」「送らないなら【濁】+0.02」「表示候補のうち選ばれなかったぶんが散る」
// という同じ作法をそのまま繰り返すので、その一手をここへ寄せる（各ステージの Apply〜Choice が呼ぶ）。
//
// 効果の読み出し（下流の場面が参照する）:
//   Hub（再訪小話の固定・返信の一語差し込み） … PinnedIdleIndex / SentWordAt
//   Final（F4 の悲鳴に必ず混ざる語）           … PriorityScattered
//   Epilogue（E4 の一行）                       … ChosenAt("s3_5c") を直に読む
// いずれも GameManager の台帳（_chosenById / _scatterById）から引くだけで、新しい状態は持たない
//   ＝セーブは RecordChoice の既存キーで足りる（新しい id を足すだけで後方互換）。
public static class ChoiceEffects
{
    // （送らない）で【濁】微増。S1-4 の S14SkipContam / S3-7 の S37SkipContam と同値。
    public const float SkipContam = 0.02f;
    // S2-2 で「受け取った」ぶんのスコア。三つのどれでも同じ値（どの言葉を送ったかには付けない）。
    // 旧・やさしさ +0.02 の置き換え（2026-09-06 / docs/20260906/HUD整理_案.md §5）。
    public const int ReceivedScore = 500;

    // 道中の選択1か所ぶんを記録する。choices の末尾は必ず（送らない）＝表示候補は末尾を除いた3件。
    //   戻り値 true＝送った（＝効果を付ける側）。false＝（送らない）／沈黙20秒。
    //   【散】は表示候補のうち選ばれなかったぶんだけを計上する（（送らない）自体は言葉ではないので
    //   送信語にも散る語にも数えない＝表示候補3件が丸ごと散る）。S1-4・S3-7 と同じ流儀。
    public static bool Record(GameManager? game, string id, string[] choices, int sel, float hesitationSec)
    {
        bool sent = sel < choices.Length - 1;
        var others = new List<string>();
        for (int i = 0; i < choices.Length - 1; i++)
            if (i != sel) others.Add(choices[i]);
        game?.RecordChoice(id, sent ? choices[sel] : "", others, hesitationSec);
        // （送らない）＝言葉を出さずに見送った ぶんだけ、ミナの光がわずかに濁る。
        if (!sent) game?.SetContamination(game.Contamination + SkipContam);
        return sent;
    }

    // その id で送った言葉（（送らない）／未通過なら空文字）。
    public static string SentWordAt(GameManager? game, string id) => game?.ChosenAt(id) ?? "";

    // ── S2-4「送れない」の場面で散った言葉は、FINAL の頂点（F4）の悲鳴に**必ず**混ざる（枠の先頭）──
    //   悲鳴に混ざる散った言葉には枠の上限があるので、Final はこの並びを先に積んでから
    //   残りを ScatteredWords の出た順で埋める。
    public static IEnumerable<string> PriorityScattered(GameManager? game)
        => game?.ScatteredAt("s2_4") ?? System.Array.Empty<string>();

    // ── 次のハブの再訪小話を固定する（S1-2 → あかりの「雨粒」／S3-2 → レイの「同接」）──
    //   IdleDialogs(id) の並びの添字を返す。-1＝固定しない（送らなかった／その面をまだ通っていない）。
    //   「次回だけ」ではなく「送っていれば毎回この一本を優先」＝台帳を読むだけで状態を持たない。
    //   既読済みならプールの通常抽選に戻る（同じ小話を延々出さない）ので、実質「次のハブで一度」になる。
    public static int PinnedIdleIndex(GameManager? game, string stageId) => stageId switch
    {
        "akari" => string.IsNullOrEmpty(SentWordAt(game, "s1_2")) ? -1 : 0,   // （1）雨粒
        "rei" => string.IsNullOrEmpty(SentWordAt(game, "s3_2")) ? -1 : 0,     // （1）同接
        _ => -1,
    };
}

// GameManager の追記ぶん（本体ファイルは別担当が編集中のため partial で分ける）。
public partial class GameManager
{
    // 会話中の選択にスコアを足す入口。S2-2「消えたペンライトを受け取った」ぶんがここを通る。
    //   やさしさゲージ撤去（2026-09-06）でゲージ加算からスコア加算へ置き換えた。
    //   コンボ倍率は掛けない＝戦闘の加点とは別枠の一時金。
    public void AddScoreFromChoice(int amount) => Score += amount;

    // その選択 id で散った言葉（出た順）。F4 の枠の先頭に入れる語をここから引く。
    public IReadOnlyList<string> ScatteredAt(string id)
        => _scatterById.TryGetValue(id, out var v) ? v : (IReadOnlyList<string>)System.Array.Empty<string>();
}
