using Godot;
using System;

public sealed record BossPostStory(string Id, string Name, Color Accent, Color Dawn,
    string Face, string Quote, string[] Posts, string[] Replies, (int who, string text, string face)[] Lines)
{
    public string FakeBackground => $"res://char/bg2/boss/{Id}_v1.png";
    public string RealBackground => $"res://char/bg2/boss/{Id}_real_v1.png";
    public string Handle => Id == "mina" ? Handles.Mina : Array.Find(GameManager.Stages, stage => stage.Id == Id)!.Handle;

    public static BossPostStory Get(string id) => id switch
    {
        "akari" => new(id, "あかり", new("a3d7eb"), new("ffe2d6"),
            "res://char/v3/akari_face_cry.png", "……好きだよ。いまも。",
            new[] { "同期が、新しい職場へ。\nあたしも、負けずに頑張らなきゃ。\nおめでとう！", "おめでとう。\n……あたしは、まだここにいるよ。\nたまには、思い出してね。", "置いていかれた、なんて。\n思いたくないのに。\nよかったねって、笑えない。", "同期じゃなくなったら、\nもう、一緒に帰れないのかな。\nほんとは、行かないでって言いたい。", "おめでとう　ほんとだよ　元気でね\n\n……好きだよ。いまも。" },
            new[] { "……それは、みんなに見せても平気な、あたし。", "重いって思われるのが、怖くて。書き直した。", "祝いたいのに。寂しいばっかり、増えていって。", "……違う。それより、もっと前。最初に書いたのは……。", "好きも、おめでとうも。……どっちも、本当だった。" },
            new (int who, string text, string face)[] {
        (2, "……向かいの席。ほんとうは、ずっと見られなかった。", "res://char/v3/akari_face.png"),
        (2, "一緒に入った会社なのに。好きって、一度も言えないまま……。", "res://char/v3/akari_face.png"),
        (2, "寂しいのも、おめでとうも。最初から、両方あったんだね。", "res://char/v3/akari_face.png"),
        (2, "……あたしの気持ち、嘘じゃなかった。", "res://char/v3/akari_face.png"),
    }),
        "koharu" => new(id, "こはる", new("efb0cf"), new("d3f2dd"),
            "res://char/v3/koharu_face_pale.png", "休んでも、好きでいたい。",
            new[] { "今日の配信も最高だった。\nこれで、明日も学校、行ける。", "今日も最後まで見たよ。\n明日も、ちゃんと応援するね。\n……ちゃんと、しなきゃ。", "配信が終わると、怖くなる。\n学校も、模試も、そのままで。\nまだ画面を閉じたくない。", "全部見ないと、忘れられそう。\n役に立たないあたしには、\nここにも居場所がないのかな。", "レイちゃんがいたから、\n今日も学校に行けた。ありがとう。\nでも今日は、もう休みたい。" },
            new[] { "楽しかったのは、ほんと。……それだけ書けば、心配されないから。", "応援まで、できる子でいなきゃって。", "画面を閉じたら、できないあたしに戻っちゃう。", "……最初は、ちゃんとするためじゃ、なかったのに。", "ありがとうも、休みたいも。……いっしょに言って、よかったんだ。" },
            new (int who, string text, string face)[] {
        (2, "……あたしの部屋。机も、明日の制服も、ちゃんとある。", "res://char/v3/koharu_face.png"),
        (2, "レイちゃんの声に助けられたのは、ほんとだよ。そこまで、消したくない。", "res://char/v3/koharu_face.png"),
        (2, "全部見られない日も、好きでいていいんだね。……今日は、眠りたい。", "res://char/v3/koharu_face.png"),
        (2, "明日、分からないところを、隣の子に聞いてみる。答えを写す前に。", "res://char/v3/koharu_face.png"),
    }),
        "rei" => new(id, "レイ", new("dbccfa"), new("f7e4b2"),
            "res://char/v3/rei_face_cry.png", "好きな話を、聞いてほしい。",
            new[] { "見てくれて、ありがとう。\n次も、いつも通りにやるから。\n楽しみにしてて。", "次は好きな作品の話でも……。\nううん、いつもの企画にする。\n退屈させたくないから。", "数字を見て、また書き直した。\n好きな話をして減ったら、\nわたしまで嫌われた気がする。", "強いわたしじゃなくても、\n最後まで聞いてくれる？\n……今日は、ちょっと怖い。", "好きな作品の話がしたい。\nうまく笑わせられなくても、\nわたしの声を、聞いてほしい。" },
            new[] { "いつも通り。それなら、何も変わらないと思った。", "告知から消しただけ。……やりたくなくなったんじゃない。", "一人減るたびに、わたしが間違ってたみたいで。", "笑顔を作る前は、ただ、好きな話がしたかったんだ。", "……聞いてほしかった。数字じゃなくて、この話を。" },
            new (int who, string text, string face)[] {
        (2, "……小さい部屋でしょう。最初は、ここから声が届くだけで、うれしかった。", "res://char/v3/rei_face.png"),
        (2, "この本の話、ずっとしたかった。何度告知を消しても、机からは片づけられなかった。", "res://char/v3/rei_face.png"),
        (2, "次は、これを話す。……怖くないって言ったら、嘘だけど。", "res://char/v3/rei_face.png"),
        (2, "好きなところ、最後まで。わたしの声で、話したい。", "res://char/v3/rei_face.png"),
    }),
        "mina" => new(id, "ミナ", new("aedfd5"), new("fff0d9"),
            "res://char/mina_tears.png", "助けて。いっしょに帰りたい。",
            new[] { "本日の対応、三件完了。\n皆さまの帰還を確認しました。\nわたくしは、大丈夫です。", "三件、完了しました。\n少しだけ、返事が遅くなります。\n……ご心配には、及びません。", "今日は、もう休んでも\nよろしいですか。\n次の声を開くのが、怖いのです。", "何もできなくなったら、\nわたくしは、ここにいても……？\n誰か、返事をください。", "わたくしも、ひとりは怖いです。助けて。いっしょに帰りたい。" },
            new[] { "完了したのは、皆さまの帰還です。……わたくしのことは、書きませんでした。", "心配をかけない言葉なら、送れると思いました。", "休みたい。それだけのお願いにも、許可が要る気がして。", "役に立つ言葉に直す前は……ただ、お返事がほしかった。", "……消しません。これは、わたくしのお願いです。" },
            new (int who, string text, string face)[] {
        (1, "皆さまを送り届けたら、わたくしの用は終わるのだと、思っていました。", "res://char/mina_face.png"),
        (6, "終わったら、帰って話すの。今日みたいに。誰が先に帰っても、続きはできる。", ""),
        (1, "……何もできない日も？", "res://char/mina_face.png"),
        (6, "ええ。聞き役の日くらい、わたしにもよこしなさい。", ""),
    }),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };
}
