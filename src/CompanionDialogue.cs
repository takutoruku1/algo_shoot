using Godot;
using System;
using System.Linq;

public static class CompanionDialogue
{
    public enum Beat { Intro, Mid, Boss }
    public enum Menu { Select, Hub, Return, ShopEnter, ShopBuy, ShopExit, TrainEnter, TrainShoot, TrainIdle }

    public const string ReiAvatarPortrait = "res://char/v3/rei_gawa_face_v1.png";

    public static string Portrait(Job job) => job switch
    {
        Job.Melee => "res://char/v3/akari_face.png",
        Job.Heal => "res://char/v3/koharu_face.png",
        Job.Magic => ReiAvatarPortrait,
        _ => "res://char/mina_face.png",
    };

    public static string AccountPortrait(Job job) => AccountIcon(Jobs.Get(job).CharacterId);
    public static string AccountIcon(string id) => $"res://char/ui/sns_{(id == "final" ? "mina" : id)}_v1.png";
    public static Color Accent(Job job) => job switch
    {
        Job.Melee => UiKit.Gold,
        Job.Heal => UiKit.Ok,
        Job.Magic => UiKit.Info,
        _ => UiKit.Mina,
    };




    // ※2026-09-15 他ジョブ潜行リワーク：STAGE1〜3 は Add を呼ばなくなった（他ジョブ時は
    //   CharacterStory の専用ストーリーへ全面置換されるため、同行3行の追記が成立しない）。
    //   現在の呼び元はチュートリアル（StageZero）だけ。akari/koharu/rei の Stage アームは
    //   ステージからは参照されない（scenario の素材として当面残す）。
    public static (int who, string text, string face)[] Add(Job job, string stage, Beat beat,
        (int who, string text, string face)[] original)
    {
        var extra = Stage(job, stage, beat);
        return extra.Length == 0 ? original : original.Concat(extra).ToArray();
    }

    public static (int who, string text, string face)[] Stage(Job job, string stage, Beat beat) => (job, stage, beat) switch
    {
        (Job.Melee, "tutorial", Beat.Intro) => new (int, string, string)[] {
        (6, "あたしの声、そっちに届いてる？　……こんなふうに、並べるんだ。", ""),
        (0, "聞こえてる。僕のダイブで、ここまで来られた。動きは、一緒に確かめよう。", ""),
        (6, "じゃあ、よろしく。黙ってついてくだけには、しないから。", ""),
    },
        (Job.Heal, "tutorial", Beat.Intro) => new (int, string, string)[] {
        (6, "あたしも、そっちへ行っていいの？　見てるだけじゃなくて。", ""),
        (0, "もちろん。こはるが行きたいなら、僕が道を開く。分からないことは聞いて。", ""),
        (6, "……うん。分かんないとこ、ちゃんと聞くね。", ""),
    },
        (Job.Magic, "tutorial", Beat.Intro) => new (int, string, string)[] {
        (6, "音声チェック。星逢レイ、聞こえてる？", ""),
        (0, "聞こえるよ。回線も、ダイブも問題なし。今日はここで練習しよう。", ""),
        (6, "今日は台本なし。……詰まっても、配信事故ってことにはしないでね。", ""),
    },
        _ => System.Array.Empty<(int, string, string)>(),
    };

    // ※Final（FINAL 導入を同行キャラの掛け合いへ置換）は 2026-09-15 に廃止した。
    //   FINAL はジョブに関わらず常にミナ本編（StageMina）。他ジョブの専用ストーリーは
    //   STAGE1〜3 のみで、テーブルは CharacterStory.cs に集約されている。

    public static (string speaker, string text)[] MenuLines(Job job, Menu scene)
    {
        var lines = MenuDialogue(job, scene);
        return lines.Select(line => (line.who == 0 ? "あなた" : line.who == 1 ? "ミナ" : Jobs.Get(job).CharacterName, line.text)).ToArray();
    }

    public static string MenuText(Job job, Menu scene) => string.Join("\n", MenuLines(job, scene).Select(line => $"{line.speaker}：{line.text}"));

    public static (int who, string text, string face)[] MenuDialogue(Job job, Menu scene) => (job, scene) switch
    {
        (Job.Melee, Menu.Select) => new (int, string, string)[] {
        (6, "あたしも行く。いつまでも、待ってる側じゃなくて。", ""),
        (0, "うん。僕が道を開く。準備ができたら、あかりさんをダイブさせるよ。", ""),
        (6, "画面の向こうのあなたも、よろしく。怖くなったら、消さずに言うから。", ""),
    },
        (Job.Heal, Menu.Select) => new (int, string, string)[] {
        (6, "あたしで、役に立てるかな。……って、また聞いちゃった。", ""),
        (0, "行きたいかどうかを聞かせて。分からないことは、僕が説明するから。", ""),
        (6, "じゃあ、行きたい。画面の向こうのあなたにも、できないとこから見てもらうね。", ""),
    },
        (Job.Magic, Menu.Select) => new (int, string, string)[] {
        (6, "星逢レイ、準備できてるわ。この姿で、いい？", ""),
        (0, "もちろん。レイさんが話しやすい姿で。回線をつないで、僕が送り出す。", ""),
        (6, "画面の向こうのあんたにも、この声で話すわ。配信用の元気、足さないでね。", ""),
    },
        (Job.Melee, Menu.Hub) => new (int, string, string)[] {
        (6, "通知、来てた。……いまじゃなくても、いいか。", ""),
        (0, "気になるなら、先に見てもいいよ。話の続きは待ってる。", ""),
        (6, "待ってはいるよ。でも今は、あなたに続きを話したい。……画面の向こうの、あなたに。", ""),
        (0, "うん。さっきの続きを聞かせて。", ""),
    },
        (Job.Heal, Menu.Hub) => new (int, string, string)[] {
        (6, "ペンライトの電池、換えようかな。光、ちょっと弱い。", ""),
        (0, "使わない日にも、手元に置いてるんだね。", ""),
        (6, "うん。あなたにも見せたいな、これ。光ってなくても、好きなものなんだよ。", ""),
        (0, "うん。どうして好きなのかも、聞いてみたい。", ""),
    },
        (Job.Magic, Menu.Hub) => new (int, string, string)[] {
        (6, "今度、本の話をしようと思って。……誰も知らない作品かもしれないけど。", ""),
        (0, "僕も聞きたい。どんなところが好き？", ""),
        (6, "最後に、台所の灯りがつくの。……画面の向こうのあんたにも、そこを聞いてほしい。", ""),
        (0, "その灯り、誰を待ってつけたんだろう。もう少し教えて。", ""),
    },
        (Job.Melee, Menu.Return) => new (int, string, string)[] {
        (6, "……戻った。スマホ、ずっと握ってたんだ。手、痛い。", ""),
        (0, "おかえり。もう置いて大丈夫。回線は、そのままつながってるよ。", ""),
        (6, "じゃ、少し置く。……ただいま。あなたも、お茶にする？", ""),
    },
        (Job.Heal, Menu.Return) => new (int, string, string)[] {
        (6, "ちゃんとできたか、まだ分かんない。でも、途中で目をそらさなかった。", ""),
        (0, "おかえり。僕も見てた。怖いところで、呼んでくれたね。", ""),
        (6, "あなたにも、帰ってきたよって言いたかった。点数の報告じゃなくて。", ""),
    },
        (Job.Magic, Menu.Return) => new (int, string, string)[] {
        (6, "配信なら、ここで元気に締めるんだけど。……今日は、ちょっと静かにしたい。", ""),
        (0, "おかえり。ここは、無理に明るく締めなくていいよ。", ""),
        (6, "じゃあ、お水飲んでから。あんたの前では、無理に締めなくてもよさそうね。", ""),
    },
        (Job.Melee, Menu.ShopEnter) => new (int, string, string)[] {
        (0, "次のダイブの前に、支度を整えよう。", ""),
        (6, "領収書、会社には出せないね。", ""),
    },
        (Job.Heal, Menu.ShopEnter) => new (int, string, string)[] {
        (0, "今日は、何を強くしたい？　今買える段を開くね。", ""),
        (6, "今日は、自分のぶんを選ぶ。", ""),
    },
        (Job.Magic, Menu.ShopEnter) => new (int, string, string)[] {
        (0, "ゆっくり選べそう？　急ぐ用事があれば、また後でも。", ""),
        (6, "ううん。告知、出してないから。", ""),
    },
        (Job.Melee, Menu.ShopBuy) => new (int, string, string)[] {
        (0, "これで一段、力が増えた。使い心地は、練習で確かめよう。", ""),
        (6, "自分のために買うの、久しぶり。", ""),
    },
        (Job.Heal, Menu.ShopBuy) => new (int, string, string)[] {
        (0, "手元に届いたかな。使い方は、一緒に確かめよう。", ""),
        (6, "ちゃんと使う。……失敗しても。", ""),
    },
        (Job.Magic, Menu.ShopBuy) => new (int, string, string)[] {
        (0, "さっきより重くない？　動かしにくかったら教えて。", ""),
        (6, "見栄より軽い。これでいくわ。", ""),
    },
        (Job.Melee, Menu.ShopExit) => new (int, string, string)[] {
        (0, "忘れ物はなさそうかな。帰り道まで、僕が案内するよ。", ""),
        (6, "ないよ。あなたの声も、一緒に来るんでしょ。", ""),
    },
        (Job.Heal, Menu.ShopExit) => new (int, string, string)[] {
        (0, "準備できたら行こう。こはるの返事で、道を開く。", ""),
        (6, "うん。次も、一緒に選んでね。", ""),
    },
        (Job.Magic, Menu.ShopExit) => new (int, string, string)[] {
        (0, "準備はできた？　大丈夫なら、回線を切り替える。", ""),
        (6, "ええ。続きは、帰ってからね。", ""),
    },
        (Job.Melee, Menu.TrainEnter) => new (int, string, string)[] {
        (0, "ここは練習用の場所だ。失敗しても、繰り返せるよ。", ""),
        (6, "始末書、いらない？　助かる。", ""),
    },
        (Job.Heal, Menu.TrainEnter) => new (int, string, string)[] {
        (0, "ここでは採点しないよ。慣れるまで、何度でも。", ""),
        (6, "よかった。何回でも、やってみる。", ""),
    },
        (Job.Magic, Menu.TrainEnter) => new (int, string, string)[] {
        (0, "本番前に、動きを確かめよう。止まりたいときは言って。", ""),
        (6, "じゃあ、失敗も編集しないわ。", ""),
    },
        (Job.Melee, Menu.TrainShoot) => new (int, string, string)[] {
        (0, "届いた。狙ったところに、ちゃんと当たったね。", ""),
        (6, "うん。宛先、間違えなかった。", ""),
    },
        (Job.Heal, Menu.TrainShoot) => new (int, string, string)[] {
        (0, "さっきより、ゆっくり狙えてた。今の感じ、どうだった？", ""),
        (6, "うん。一個ずつ、見えてきた。", ""),
    },
        (Job.Magic, Menu.TrainShoot) => new (int, string, string)[] {
        (0, "今の一発、的に届いた。手応え、あった？", ""),
        (6, "見てた？　……もう一回ね。", ""),
    },
        (Job.Melee, Menu.TrainIdle) => new (int, string, string)[] {
        (0, "少し休む？　僕もちょうど、お茶を飲みたい。", ""),
        (6, "うん。今日は、断らない。", ""),
    },
        (Job.Heal, Menu.TrainIdle) => new (int, string, string)[] {
        (0, "的は逃げないから、休憩にしよう。僕も待ってる。", ""),
        (6, "じゃあ、お茶。戻ったら続きね。", ""),
    },
        (Job.Magic, Menu.TrainIdle) => new (int, string, string)[] {
        (0, "レイさん、声を休めようか。今日は、もう十分試せたよ。", ""),
        (6, "……ありがと。少し、そうする。", ""),
    },
        _ => System.Array.Empty<(int, string, string)>(),
    };
}
