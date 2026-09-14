using Godot;
using System;
using System.Linq;

public static class CompanionDialogue
{
    public enum Beat { Intro, Mid, Boss }
    public enum Menu { Select, Hub, Return, ShopEnter, ShopBuy, ShopExit, TrainEnter, TrainShoot, TrainIdle }

    public static string Portrait(Job job) => $"res://char/player/{Jobs.Get(job).CharacterId}/{Jobs.Get(job).CharacterId}_spin_v2_00.png";
    public static Color Accent(Job job) => job switch
    {
        Job.Melee => UiKit.Gold,
        Job.Heal => UiKit.Ok,
        Job.Magic => UiKit.Info,
        _ => UiKit.Mina,
    };

    private static (int who, string text, string face) M(string text) => (1, text, "res://char/mina_face.png");
    private static (int who, string text, string face) P(string text) => (6, text, "");

    public static (int who, string text, string face)[] Add(Job job, string stage, Beat beat,
        (int who, string text, string face)[] original)
    {
        var extra = Stage(job, stage, beat);
        return extra.Length == 0 ? original : original.Concat(extra).ToArray();
    }

    public static (int who, string text, string face)[] Stage(Job job, string stage, Beat beat) => (job, stage, beat) switch
    {
        (Job.Melee, "tutorial", Beat.Intro) => new[] {
            P("あたしの声、そっちに届いてる？　……こんなふうに、並べるんだ。"),
            M("はい。回線に映した姿です。歩く先は、ご主人様に。言葉は、あなたのままで。"),
            P("じゃあ、よろしく。黙ってついてくだけには、しないから。"),
        },
        (Job.Heal, "tutorial", Beat.Intro) => new[] {
            P("あたしも、そっちへ行っていいの？　見てるだけじゃなくて。"),
            M("はい。あなたの声を、回線に映しています。ご主人様と、一緒に。"),
            P("……うん。分かんないとこ、ちゃんと聞くね。"),
        },
        (Job.Magic, "tutorial", Beat.Intro) => new[] {
            P("音声チェック。星逢レイ、聞こえてる？"),
            M("聞こえています。回線に映るお姿も、そのままで。"),
            P("今日は台本なし。……詰まっても、配信事故ってことにはしないでね。"),
        },
        (Job.Melee, "akari", Beat.Intro) => new[] {
            P("……このフロア。帰ったあとまで、頭の中に残ってた。"),
            M("ここに残るのは、過去の声です。いま回線で話しているあなたとは、別に。"),
            P("分かった。あのころのあたしが、まだここにいるなら。……今度は、聞く。"),
        },
        (Job.Heal, "akari", Beat.Intro) => new[] {
            P("誰もいないのに、机の上、仕事でいっぱい。……帰っていいのかなって、迷っちゃうね。"),
            M("椅子は、空いています。ですが、声だけが残っています。"),
            P("あたしたちは、終わったら帰ろうね。ミナも、一緒に。"),
        },
        (Job.Magic, "akari", Beat.Intro) => new[] {
            P("通知だけは来るのに、待ってる返事じゃない。……つい、毎回開いちゃうのよね。"),
            M("ここには、そのたびに取り消した言葉も残っています。"),
            P("じゃあ、数字のほうは置いとく。何を言いたかったのか、聞きに行きましょ。"),
        },
        (Job.Melee, "akari", Beat.Mid) => new[] {
            P("向かいの席、つい見ちゃうんだよね。もう、誰もいないって分かってるのに。"),
            M("……立ち止まりますか。"),
            P("ううん。ひと呼吸だけ。……ありがと。行ける。"),
        },
        (Job.Heal, "akari", Beat.Mid) => new[] {
            P("送る前に十二回も消したんだ。……一回ごとに、すごく時間かかったんだろうな。"),
            M("取り消しの件数では、その時間までは測れません。"),
            P("うん。だから、すぐ言えばよかったのにって、言いたくない。"),
        },
        (Job.Magic, "akari", Beat.Mid) => new[] {
            P("わたしも、送れそうな形に直してるうちに、最初に言いたかったことが消えるの。"),
            M("ここには、取り消した跡だけが。"),
            P("跡でもいい。なかったことには、したくないわ。"),
        },
        (Job.Melee, "akari", Beat.Boss) => new[] {
            P("……あたし、あんなふうに聞こえてたんだ。"),
            M("過去の声が、形になっています。いまのあなたの返事まで、決めるものではありません。"),
            P("うん。西野の代わりに返事はできない。でも、ここから目はそらさない。"),
        },
        (Job.Heal, "akari", Beat.Boss) => new[] {
            P("「ずっと」って、言ってほしいんだね。……でも、あたしが約束しちゃだめだ。"),
            M("この方が待っている相手の、代わりにはなれません。"),
            P("うん。その代わりじゃなくて、あたしとして、聞く。"),
        },
        (Job.Magic, "akari", Beat.Boss) => new[] {
            P("「すき」って返したら、その場だけは静かになるかもしれない。でも……。"),
            M("待っていた返事とは、違いますね。"),
            P("ええ。聞こえのいい言葉で、済ませない。"),
        },
        (Job.Melee, "koharu", Beat.Intro) => new[] {
            P("鞄、置いたままだ。帰ってきて、動けなくなる日ってあるよね。"),
            M("灯りも、ついていません。画面の跡だけが残っています。"),
            P("片づけろ、は後にしよう。まず、どこにいるか探そ。"),
        },
        (Job.Heal, "koharu", Beat.Intro) => new[] {
            P("……あたしの部屋。電気つけたくなくて、画面だけ見てた夜の。"),
            M("その夜の声が、残っています。いま、こちらに届いているあなたの声とは、別です。"),
            P("うん。恥ずかしいけど。……消して終わりには、したくない。"),
        },
        (Job.Magic, "koharu", Beat.Intro) => new[] {
            P("このペンライト……わたしの配信を、ここで見てくれてたのね。"),
            M("画面のこちら側には、椅子がひとつ。"),
            P("わたしからは数字に見えてた場所に、こんな部屋があったんだ。"),
        },
        (Job.Melee, "koharu", Beat.Mid) => new[] {
            P("学校でも、ちゃんとして。家でも、ちゃんとして。……休む席がないね。"),
            M("椅子なら、これだけ並んでいるのですが。"),
            P("だよね。……一個くらい、座ってるだけでいい席にしたい。"),
        },
        (Job.Heal, "koharu", Beat.Mid) => new[] {
            P("分かんないって言うの、怖かった。前は、教えるほうだったから。"),
            M("……いま、言えましたね。"),
            P("ほんとだ。ここ、テストじゃないのに。……ずっと構えてた。"),
        },
        (Job.Magic, "koharu", Beat.Mid) => new[] {
            P("いつも来てって言うとき、来られない日のこと、考えてなかった。"),
            M("この机には、配信以外の予定もあります。"),
            P("ええ。見てくれる人の一日、わたしの枠だけじゃないものね。"),
        },
        (Job.Melee, "koharu", Beat.Boss) => new[] {
            P("頑張ってるかどうか、ここでまで聞かれるの、しんどいな。"),
            M("こちらからは、点数を返しません。"),
            P("うん。合格したら助ける、とかじゃないから。"),
        },
        (Job.Heal, "koharu", Beat.Boss) => new[] {
            P("……あそこにも、あたしがいる。まだ、ペンライト握ってる。"),
            M("あの夜、残された声です。いまのあなたを、責めるためのものではありません。"),
            P("うん。好きだったことまで、罰にしなくていいって。……あたしにも、言いたい。"),
        },
        (Job.Magic, "koharu", Beat.Boss) => new[] {
            P("こはるちゃん。今日は、見に来てって言わない。わたしのほうから、来たの。"),
            M("……返事の欄は、まだ閉じています。"),
            P("大丈夫。お礼まで、急いでもらわなくていいわ。"),
        },
        (Job.Melee, "rei", Beat.Intro) => new[] {
            P("帰ってきた部屋なのに、まだ人に見せる顔をしてる。……疲れちゃうよね。"),
            M("画面の笑顔は、消灯しても残っています。"),
            P("だったら、画面の外も見よう。そこにいる人に、会いたいから。"),
        },
        (Job.Heal, "rei", Beat.Intro) => new[] {
            P("いつも見てた枠だ。でも、今日は、こっち側からなんだ。"),
            M("画面の奥にも、ひとりぶんの部屋があります。"),
            P("……レイちゃん。今日は、うまく話してくれなくても聞くよ。"),
        },
        (Job.Magic, "rei", Beat.Intro) => new[] {
            P("待機画面、直した跡まで残ってる。……見られると、さすがに照れるわね。"),
            M("残された配信の声です。いま回線で話しているあなたは、その画面の外にいます。"),
            P("なら、この姿のまま行く。嫌いになったわけじゃないの。星逢レイのこと。"),
        },
        (Job.Melee, "rei", Beat.Mid) => new[] {
            P("消された一行、仕事の連絡じゃないのに、何度も書き直した跡がある。"),
            M("言い直したほうだけが、画面に残っています。"),
            P("あたしも「よかったじゃない」で隠したこと、ある。……続き、聞こう。"),
        },
        (Job.Heal, "rei", Beat.Mid) => new[] {
            P("笑顔に切り替わる前、見えた。……いつも、待たせないようにしてたのかな。"),
            M("切り替わるまで、一秒九でした。"),
            P("あたしは、待てるよ。一秒九より、ずっと。"),
        },
        (Job.Magic, "rei", Beat.Mid) => new[] {
            P("足音まで、拾われてるのね。配信前、いつもあの辺を往復してた。"),
            M("……始める前に、立ち止まれる場所がありません。"),
            P("今日はここで、一回止まる。始める前から、息切れしてたくないもの。"),
        },
        (Job.Melee, "rei", Beat.Boss) => new[] {
            P("笑ってるから平気、って決めつけたくない。……あたしも、平気な顔は得意だったし。"),
            M("貼りついた声から、祓います。"),
            P("うん。あの人の顔まで、消さないように。"),
        },
        (Job.Heal, "rei", Beat.Boss) => new[] {
            P("好きな姿だよ。でも、その笑顔のままじゃなくても、聞きたい。"),
            M("……いまの言葉は、こちらにも聞こえました。"),
            P("うん。レイちゃんにも、届くところまで行こう。"),
        },
        (Job.Magic, "rei", Beat.Boss) => new[] {
            P("同じ姿が、向こうにも。……あの笑顔、わたしが練習したやつだ。"),
            M("画面に残った声と、いま隣にいるあなた。同じ返事をする必要は、ありません。"),
            P("ええ。笑顔を壊しに来たんじゃない。その顔で言えなかったことを、聞きに来たの。"),
        },
        _ => Array.Empty<(int, string, string)>(),
    };

    public static (int who, string text, string face)[] Final(Job job)
    {
        string name = Jobs.Get(job).CharacterName;
        var reply = job switch
        {
            Job.Melee => new[] { P("ミナ。今度は、あたしがそっちへ行く。返事を急がせるためじゃないよ。"),
                M("……あかり、さん。あなたの声まで、ここに……。"),
                P("うん。ひとりに預けたままに、したくないから。帰ったら、あたしの話も聞いて。") },
            Job.Heal => new[] { P("ミナ、聞こえる？　あたし。今日は、画面を閉じる前に、迎えに来た。"),
                M("……こはる、さん。ご無理は、なさらず……。"),
                P("無理だったら、言うよ。だからミナも、言って。あたし、ちゃんと聞くから。") },
            Job.Magic => new[] { P("音声チェック。……ミナ、返事は後でいい。わたしから、話すわ。"),
                M("……レイ、さん。その声は、いまも……。"),
                P("ここにいる。今日は、最後の挨拶を一緒にしたいの。先に、いなくならないで。") },
            _ => Array.Empty<(int, string, string)>(),
        };
        return new[] { M("……わたくしの身体は、もう、動かせません。でも。あなたとの回線だけは、まだ……。"),
            (3, $"通信先：ミナの内側\n送信元：あなた / 同行する声：{name}", ""),
            (3, $"回線に、{name}の姿が映る。\nミナを動かすのではなく、彼女のもとへ向かう。", "") }.Concat(reply)
            .Select(line => line.Item1 == 1 ? (1, line.Item2, "res://char/mina_worried.png") : line).ToArray();
    }

    public static (string speaker, string text)[] MenuLines(Job job, Menu scene)
    {
        var lines = MenuDialogue(job, scene);
        return lines.Select(line => (line.who == 1 ? "ミナ" : Jobs.Get(job).CharacterName, line.text)).ToArray();
    }

    public static string MenuText(Job job, Menu scene) => string.Join("\n", MenuLines(job, scene).Select(line => $"{line.speaker}：{line.text}"));

    private static (int who, string text, string face)[] MenuDialogue(Job job, Menu scene) => (job, scene) switch
    {
        (Job.Melee, Menu.Select) => new[] { P("あたしも行く。いつまでも、待ってる側じゃなくて。"),
            M("はい。声を回線に映します。足取りはご主人様に、返事は、あなたに。"),
            P("任せた。……怖くなったら、ちゃんと言うから。") },
        (Job.Heal, Menu.Select) => new[] { P("あたしで、役に立てるかな。……って、また聞いちゃった。"),
            M("点数を付ける欄は、ありません。一緒に行くかどうかだけです。"),
            P("じゃあ、行きたい。あたしも、そっちに。") },
        (Job.Magic, Menu.Select) => new[] { P("星逢レイ、準備できてるわ。この姿で、いい？"),
            M("はい。話している方は、変わりませんので。"),
            P("……ありがと。今日は、声まで作らなくてよさそうね。") },
        (Job.Melee, Menu.Hub) => new[] { P("通知、来てた。……いまじゃなくても、いいか。"),
            M("お返事を、待たなくてよいのですか。"),
            P("待ってはいるよ。でも、この話を途中でやめるほどじゃない。"),
            M("では、続けます。……途中でしたので。") },
        (Job.Heal, Menu.Hub) => new[] { P("ペンライトの電池、換えようかな。光、ちょっと弱い。"),
            M("使わない日も、持っているのですね。"),
            P("うん。好きなの。つけてないときも。"),
            M("……点灯している時間だけでは、測れませんね。") },
        (Job.Magic, Menu.Hub) => new[] { P("今度、本の話をしようと思って。……誰も知らない作品かもしれないけど。"),
            M("どこがお好きなのですか。"),
            P("最後に、台所の灯りがつくの。帰ってくるって、信じてたみたいに。"),
            M("……そこを、もう少し。") },
        (Job.Melee, Menu.Return) => new[] { P("……戻った。スマホ、ずっと握ってたんだ。手、痛い。"),
            M("ここでは、置いていても声は届きます。"),
            P("そっか。じゃあ、少し置く。……ミナも、座る？") },
        (Job.Heal, Menu.Return) => new[] { P("ちゃんとできたか、まだ分かんない。でも、途中で目をそらさなかった。"),
            M("はい。こちらからも、見えていました。"),
            P("……それ、覚えとく。点数じゃないほう。") },
        (Job.Magic, Menu.Return) => new[] { P("配信なら、ここで元気に締めるんだけど。……今日は、ちょっと静かにしたい。"),
            M("終わりの挨拶は、お急ぎでなくて結構です。"),
            P("じゃあ、お水飲んでから。ミナのぶんも、隣に置いとくわ。") },
        (Job.Melee, Menu.ShopEnter) => new[] { M("支度を、整えましょう。"), P("領収書、会社には出せないね。") },
        (Job.Heal, Menu.ShopEnter) => new[] { M("何をご覧になりますか。"), P("今日は、自分のぶんを選ぶ。") },
        (Job.Magic, Menu.ShopEnter) => new[] { M("お急ぎですか。"), P("ううん。告知、出してないから。") },
        (Job.Melee, Menu.ShopBuy) => new[] { M("ひとつ、整いました。"), P("自分のために買うの、久しぶり。") },
        (Job.Heal, Menu.ShopBuy) => new[] { M("こちらを、お渡しします。"), P("ちゃんと使う。……失敗しても。") },
        (Job.Magic, Menu.ShopBuy) => new[] { M("重さは、いかがですか。"), P("見栄より軽い。これでいくわ。") },
        (Job.Melee, Menu.ShopExit) => new[] { M("お忘れ物は。"), P("ないよ。ミナも、一緒でしょ。") },
        (Job.Heal, Menu.ShopExit) => new[] { M("まいりましょうか。"), P("うん。次も、一緒に選んでね。") },
        (Job.Magic, Menu.ShopExit) => new[] { M("準備は、よろしいですか。"), P("ええ。続きは、帰ってからね。") },
        (Job.Melee, Menu.TrainEnter) => new[] { M("ここでは、失敗しても。"), P("始末書、いらない？　助かる。") },
        (Job.Heal, Menu.TrainEnter) => new[] { M("採点は、いたしません。"), P("よかった。何回でも、やってみる。") },
        (Job.Magic, Menu.TrainEnter) => new[] { M("本番ではありません。"), P("じゃあ、失敗も編集しないわ。") },
        (Job.Melee, Menu.TrainShoot) => new[] { M("いま、届きました。"), P("うん。宛先、間違えなかった。") },
        (Job.Heal, Menu.TrainShoot) => new[] { M("先ほどより、落ち着いて。"), P("うん。一個ずつ、見えてきた。") },
        (Job.Magic, Menu.TrainShoot) => new[] { M("いまの一発は、外へ。"), P("見てた？　……もう一回ね。") },
        (Job.Melee, Menu.TrainIdle) => new[] { M("休憩になさいますか。"), P("うん。今日は、断らない。") },
        (Job.Heal, Menu.TrainIdle) => new[] { M("的は、逃げません。"), P("じゃあ、お茶。戻ったら続きね。") },
        (Job.Magic, Menu.TrainIdle) => new[] { M("声を、休めても。"), P("……ありがと。少し、そうする。") },
        _ => Array.Empty<(int, string, string)>(),
    };
}
