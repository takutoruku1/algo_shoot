using Godot;
using System.Collections.Generic;

// ChoiceEffects : 下書き選択の共通処理と、選択が下流の場面へ効く「効果」の窓口。
//   正典: wiki/08_仮台本/17_道中の選択肢_案C.md（ユーザー承認済み・2026-09-06）。
//
// 既存の選択（P2・P3・P4・S1-4・S3-7・F4・E6）はそれぞれの場面が直に RecordChoice を呼んでいるが、
// 道中では表示候補のうち選ばれなかったぶんが散る。
// という同じ作法をそのまま繰り返すので、その一手をここへ寄せる（各ステージの Apply〜Choice が呼ぶ）。
//
// 効果の読み出し（下流の場面が参照する）:
//   Hub（返信の一語差し込み）                 … SentWordAt
//   Final（F4 の悲鳴に必ず混ざる語）           … PriorityScattered
//   Epilogue（E4 の一行）                       … ChosenAt("s3_5c") を直に読む
// いずれも GameManager の台帳（_chosenById / _scatterById）から引くだけで、新しい状態は持たない
//   ＝セーブは RecordChoice の既存キーで足りる（新しい id を足すだけで後方互換）。
public static class ChoiceEffects
{
    public static readonly string[] SkyChoices = { "あとで空を見よう。ミナと一緒に", "今は、ミナが無事でほっとしてる" };

    public static (int who, string text, string face)[] SkyReply(int sel)
    {
        const string face = "res://char/mina_face.png";
        string reply = sel switch
        {
            0 => "……一緒に。はい。ご主人様が見上げた空のこと、聞かせてください。わたくしも、こちらの空をお話しします。",
            _ => "……わたくしを、心配してくださっていたのですね。ただいま、ご主人様。少し、ここでお話ししていきましょう。",
        };
        return new[] { (0, SkyChoices[sel], ""), (1, reply, face) };
    }

    public static bool Record(GameManager? game, string id, string[] choices, int sel, float hesitationSec)
    {
        bool sent = choices[sel] != "（送らない）";
        var others = new List<string>();
        for (int i = 0; i < choices.Length; i++)
            if (i != sel && choices[i] != "（送らない）") others.Add(choices[i]);
        game?.RecordChoice(id, sent ? choices[sel] : "", others, hesitationSec);
        return sent;
    }

    // その id で送った言葉（（送らない）／未通過なら空文字）。
    public static string SentWordAt(GameManager? game, string id) => game?.ChosenAt(id) ?? "";

    // ── S2-4「送れない」の場面で散った言葉は、FINAL の頂点（F4）の悲鳴に**必ず**混ざる（枠の先頭）──
    //   悲鳴に混ざる散った言葉には枠の上限があるので、Final はこの並びを先に積んでから
    //   残りを ScatteredWords の出た順で埋める。
    public static IEnumerable<string> PriorityScattered(GameManager? game)
        => game?.ScatteredAt("s2_4") ?? System.Array.Empty<string>();


    // ── 迷い秒ゲート（docs/20260926/主人公の存在_診断と本文 §4）──
    //   その id で、最初の選択（p2）より長く迷ったか。E6 の対句（Epilogue）と同じ基準（docs/20260924 §2.4）。
    //   上げ下げには使わない＝クリア後にボスの1行が挿さるだけ。台帳を読むだけの純関数で、新しい状態は持たない。
    //   ボスから入場して選択を通っていない（HasChoiceAt=false）なら false。
    //   id: あかり=s1_4／こはる=s2_4／レイ=s3_5c。
    public static bool Hesitated(GameManager? game, string id)
        => game != null && game.HasChoiceAt(id) && game.HesitationAt(id) > game.HesitationAt("p2");
}

// GameManager の追記ぶん（本体ファイルは別担当が編集中のため partial で分ける）。
public partial class GameManager
{

    // その選択 id で散った言葉（出た順）。F4 の枠の先頭に入れる語をここから引く。
    public IReadOnlyList<string> ScatteredAt(string id)
        => _scatterById.TryGetValue(id, out var v) ? v : (IReadOnlyList<string>)System.Array.Empty<string>();
}
