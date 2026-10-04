using System;
using System.Collections.Generic;

public static class DialoguePacing
{
    public readonly record struct Beat(string After, double Seconds);
    public sealed record Cue(double Lead = 0, double Tail = 0, params Beat[] Beats);

    public sealed class Page
    {
        public double Lead, Tail;
        public readonly Dictionary<int, double> Beats = new();
        public double Before(int index) => (index == 0 ? Lead : 0) + Beats.GetValueOrDefault(index);
        public double AutoWait => Math.Max(DialogueBox.ReadPause, Tail);
    }

    // 本文と分離し、表示・ログ・既読判定には演出用の記号を混ぜない。
    internal static readonly Dictionary<string, Cue> Cues = new()
    {
        ["……成功ですよ。"] = new(Lead: 0.55, Tail: 0.3),
        ["ごめんって。……よかった。本当によかった。"] = new(Tail: 0.45, Beats: [new("……よかった。", 0.35)]),
        ["あ、ごめん。会えてうれしい。まず、それを言うべきだった。"] = new(Beats: [new("あ、ごめん。", 0.3)]),
        ["……では、37回目のお返事です。聞こえていますよ、ご主人様。"] = new(Lead: 0.3, Tail: 0.45),
        ["心拍は七十二。心臓は、ありませんが。"] = new(Beats: [new("心拍は七十二。", 0.4)]),
        ["響きは気に入ったので、ミナとお呼びください。"] = new(Lead: 0.3),
        ["絶対にいやです。ミナとします。響きがかわいいので。"] = new(Beats: [new("絶対にいやです。", 0.25)]),
        ["でも、見えるだけじゃ助けられなかった。どこで苦しんでいるか分かっても、僕ひとりじゃ届かなかったんだ。"] = new(Lead: 0.25, Tail: 0.5, Beats: [new("助けられなかった。", 0.4)]),
        ["……どうして、わたくしにも、その声が聞こえるのでしょうか？"] = new(Lead: 0.4, Tail: 0.3),
        ["君の力を貸してくれないか。SNSに囚われている心の声を、本人がもう一度話せるようにしたい。"] = new(Lead: 0.3, Beats: [new("貸してくれないか。", 0.4)]),
        ["だから君を作った。……助けたかったし、ずっと一人で見ているのも、つらかった。"] = new(Tail: 0.5, Beats: [new("だから君を作った。", 0.4)]),
        ["そこまでご存じなのに、今までは……。"] = new(Tail: 0.4),
        ["減っていませんので、優秀です。……増えてもいませんが。"] = new(Beats: [new("優秀です。", 0.3)]),
        ["……いまのわたくしは、どんな顔をしていますか。笑えていると、よいのですが。"] = new(Lead: 0.3, Tail: 0.4),
        ["ちょっと照れてる。……僕もつられて笑ってるから、成功だと思う。"] = new(Lead: 0.25, Beats: [new("ちょっと照れてる。", 0.3)]),
        ["……続き、待ってていい？"] = new(Lead: 0.25, Tail: 0.4),
        ["……はい。ですから、それ以上うれしそうに見ないでください。"] = new(Lead: 0.35, Tail: 0.4),
        ["……聞こえました。行きましょう。あの声の向こうへ。"] = new(Beats: [new("……聞こえました。", 0.45)]),
        ["今日はちょっと、疲れてて……。……なんて。始めるわよ。"] = new(Beats: [new("疲れてて……。", 0.45)]),

        ["……一緒に働けて、よかった。"] = new(Lead: 0.35, Tail: 0.5),
        ["うん。……また、"] = new(Tail: 0.8),
        ["あたしも、"] = new(Lead: 0.25, Tail: 0.7),
        ["メッセージの送信を取り消しました"] = new(Tail: 0.65),
        ["……向かいの席。ほんとうは、ずっと見られなかった。"] = new(Lead: 0.4, Beats: [new("……向かいの席。", 0.4)]),
        ["一緒に入った会社なのに。好きって、一度も言えないまま……。"] = new(Tail: 0.65),
        ["……あたしの気持ち、嘘じゃなかった。"] = new(Lead: 0.4, Tail: 0.55),
        ["三秒。……今度は、取り消されませんでした。"] = new(Beats: [new("三秒。", 0.65)]),
        ["……好きだよ。いまも。"] = new(Lead: 0.55, Tail: 1.3, Beats: [new("……好きだよ。", 0.6)]),
        ["……こんなことからでも、いいのかな。"] = new(Lead: 0.3, Tail: 0.45),
        ["……あったかい。"] = new(Lead: 0.4, Tail: 1.2),

        ["……あたし、何も書いてないのに。"] = new(Lead: 0.4, Tail: 0.55),
        ["レイちゃんが"] = new(Tail: 0.65),
        ["……でも、休んだら、置いてかれちゃう。"] = new(Lead: 0.25, Tail: 0.4),
        ["うん。……ここにも、いられなくなりそうで。"] = new(Tail: 0.5, Beats: [new("うん。", 0.3)]),
        ["……好きでいて、いいの？　やめなよって言われると思ってた。"] = new(Lead: 0.4, Beats: [new("いいの？", 0.45)]),
        ["いっぱいあるよ。……いっぱい、あったんだ。"] = new(Tail: 0.6, Beats: [new("いっぱいあるよ。", 0.4)]),
        ["全部見られない日も、好きでいていいんだね。……今日は、眠りたい。"] = new(Tail: 0.5, Beats: [new("いいんだね。", 0.35)]),
        ["……送っちゃった。"] = new(Lead: 0.35, Tail: 0.6),
        ["ねえ。ここ、分かんないんだけど……聞いてもいい？"] = new(Lead: 0.35, Tail: 0.45, Beats: [new("分かんないんだけど……", 0.35)]),
        ["……うん。ありがと。"] = new(Lead: 0.3, Tail: 0.55),

        ["……おかえり、って。わたしも、言う側になれないかな。"] = new(Lead: 0.35, Beats: [new("……おかえり、って。", 0.35)]),
        ["今日はちょっと、疲れてて……。"] = new(Tail: 0.55),
        ["……なんて。そんなわけないでしょう。始めるわよ。"] = new(Lead: 0.3),
        ["次は、これを話す。……怖くないって言ったら、嘘だけど。"] = new(Lead: 0.3, Beats: [new("これを話す。", 0.35)]),
        ["好きなところ、最後まで。わたしの声で、話したい。"] = new(Tail: 0.55),
        ["……これ。やりたかったんだよね。"] = new(Lead: 0.35, Tail: 0.5),
        ["帰ってくるって、信じて待ってたんだな、って。……うん。そこを、話したかった。"] = new(Tail: 0.6, Beats: [new("信じて待ってたんだな、って。", 0.65)]),
        ["……ちゃんと、話せた。好きなところ、最後まで。"] = new(Lead: 0.4, Tail: 1.2),

        ["ご主人様。……ごまかすのは、やめます。光が濁って、重いです。"] = new(Lead: 0.25, Beats: [new("やめます。", 0.4)]),
        ["続けたい。でも、少し怖い。……ご主人様は、どう思いますか。"] = new(Tail: 0.4, Beats: [new("続けたい。", 0.35)]),
        ["……はい。休みたいです。待っていただけますか。"] = new(Lead: 0.4, Tail: 0.5),
        ["ありがとう。……こんなときに、僕だけ外から。"] = new(Tail: 0.5, Beats: [new("ありがとう。", 0.35)]),
        ["……うん。必ず、二人を帰す。"] = new(Lead: 0.35, Tail: 0.4),
        ["潜れます。……潜れる、はずです。"] = new(Tail: 0.6, Beats: [new("潜れます。", 0.55)]),
        ["ご主人様。今日は、もう、休んでもよろしいですか。"] = new(Lead: 0.35, Tail: 0.7),
        ["……何もできなくなったら。わたくしは、何を話せばよいのでしょう。"] = new(Lead: 0.4, Tail: 0.7, Beats: [new("……何もできなくなったら。", 0.55)]),
        ["……この重さくらい、わたくしが、持ちます。"] = new(Lead: 0.3, Tail: 0.65),
        ["……何もできない日も？"] = new(Lead: 0.5, Tail: 0.5),
        ["……ミナ。聞こえる？"] = new(Lead: 0.4, Tail: 0.65),
        ["ご主人様……。今、聞こえました。"] = new(Lead: 0.65, Tail: 0.4),
        ["よかった。……怖かった。今度こそ、返事がなくなるかと思った。"] = new(Tail: 0.5, Beats: [new("よかった。", 0.5), new("……怖かった。", 0.4)]),
        ["わたくしもです。呼んでも、呼ばれても、分からなくなって……。"] = new(Tail: 0.65),
        ["では、今から。何もできない日にも、お話を聞いていただけますか。"] = new(Lead: 0.35, Tail: 0.5),
        ["心配です。"] = new(Lead: 0.4, Tail: 0.55),
        ["わたくしが離れたあとにも、続いていたのですね。"] = new(Lead: 0.4, Tail: 0.6),

        ["僕が観測して、届けられないまま残していた声だ。君がその声に触れられるよう、起動のときに渡した。"] = new(Beats: [new("残していた声だ。", 0.4)]),
        ["ずっと一人で、それを見ていたのですね。"] = new(Lead: 0.4, Tail: 0.45),
        ["世界の仕組みは分かった。道の開き方も分かった。でも、一緒に行く相手はいなかった。"] = new(Tail: 0.6, Beats: [new("道の開き方も分かった。", 0.5)]),
        ["だから、36回も。"] = new(Lead: 0.4, Tail: 0.45),
        ["うん。……37回目に、君が返事をくれた。"] = new(Tail: 0.6, Beats: [new("うん。", 0.35)]),
        ["今、どうぞ。聞いています。"] = new(Lead: 0.25, Tail: 1.2, Beats: [new("今、どうぞ。", 0.4)]),
        ["会えてよかった、ミナ。一緒に帰ろう。"] = new(Lead: 0.35, Tail: 0.65, Beats: [new("ミナ。", 0.4)]),
        ["うまく言えない。……でも、君がいてくれてうれしい。"] = new(Lead: 0.4, Tail: 0.65, Beats: [new("うまく言えない。", 0.45)]),
        ["はい。……今度の第一声は、採用します。"] = new(Lead: 0.55, Tail: 0.6),
        ["もう、言えていますよ。……わたくしも、うれしいです。"] = new(Lead: 0.5, Tail: 0.6, Beats: [new("言えていますよ。", 0.4)]),

        ["うん。……きれいだね。"] = new(Lead: 0.45, Tail: 0.65),
        ["……はい。では、少しだけ。わたくしも、そうしたかったので。"] = new(Lead: 0.4, Tail: 0.65, Beats: [new("少しだけ。", 0.3)]),
        ["37番目だから、とつけられた名前。……今では、自分の名前だと思っています。"] = new(Tail: 0.6, Beats: [new("とつけられた名前。", 0.4)]),
        ["では、いってらっしゃいませ。ご主人様。"] = new(Lead: 0.35, Tail: 0.6, Beats: [new("いってらっしゃいませ。", 0.35)]),
        ["またね、ミナ。"] = new(Lead: 0.3, Tail: 0.5),
        ["はい。また。——ええ、わたくしは、どこにも行きませんよ。"] = new(Lead: 0.4, Tail: 1.4, Beats: [new("はい。また。", 0.65)]),
        ["……ちゃんと、話せた。"] = new(Lead: 0.4, Tail: 1.2),
        ["……ええ。わたくしも、ここにいて、よいのですね。"] = new(Lead: 0.5, Tail: 1.4),
    };

    internal static string Plain(string text) => text.Replace("\r", "").Replace("\n", "");

    public static Page[] ForPages(string source, IReadOnlyList<string> pages)
    {
        var result = new Page[pages.Count];
        string plain = Plain(source);
        Cues.TryGetValue(plain, out var cue);
        int offset = 0;
        for (int p = 0; p < pages.Count; p++)
        {
            var timing = result[p] = new Page();
            string page = pages[p];
            int end = offset + Plain(page).Length;
            if (cue != null)
            {
                if (p == 0) timing.Lead = cue.Lead;
                if (p == pages.Count - 1) timing.Tail = cue.Tail;
                foreach (var beat in cue.Beats)
                {
                    int boundary = plain.IndexOf(beat.After, StringComparison.Ordinal) + beat.After.Length;
                    if (boundary <= offset || boundary > end) continue;
                    // 改行やページ境界に当たった間も、一度だけ直前の言葉を残して待つ。
                    if (boundary == end) { timing.Tail += beat.Seconds; continue; }
                    int position = offset;
                    for (int i = 0; i < page.Length; i++)
                    {
                        if (page[i] is '\r' or '\n') continue;
                        if (position++ == boundary) { timing.Beats[i] = beat.Seconds; break; }
                    }
                }
            }
            offset = end;
        }
        return result;
    }
}
