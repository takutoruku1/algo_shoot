using Godot;
using System;

public partial class MinaStoryFilm : StoryFilm
{
    private static Line[] Memory(GameManager game) => new Line[]
    {
        new(0, "あかりの帰還後", "", game.ClearedWithCompanion("akari")
            ? "帰ってきた仲間から、あかりさんのお話を聞きました。あの方を傷つけていた言葉が、聞き終えたあとにも残りました。"
            : "手のひらに、濁りが残りました。あかりさんの『好き』ではなく、それを傷つけていた言葉が。"),
        new(0, "あかりの帰還後", "ミナ", game.ClearedWithCompanion("akari")
            ? "このくらいなら。次の方のお話も、聞けます。" : "このくらいなら。次の方のところへ、行けます。"),
        new(1, "こはるの帰還後・返信欄", "", game.ClearedWithCompanion("koharu")
            ? "ミナの投稿に、返信が増えていました。『聞いただけのAIが、むだじゃないって決めるなよ。』"
            : "ミナの投稿に、返信が増えていました。『数えた者って、数えただけの人が何言ってんの。』"),
        new(1, "こはるの帰還後・返信欄", "", "説明すれば、こはるさんの言葉を晒してしまう。……書きかけの返信は、四件とも送らないまま、返信欄を閉じました。"),
        new(2, game.ClearedWithCompanion("rei") ? "レイの帰還を待つ間" : "レイのところへ潜る前", "", "『ご主人様』。呼んだあとの言葉を、業務報告に直しました。"),
        new(2, game.ClearedWithCompanion("rei") ? "レイの帰還を待つ間" : "レイのところへ潜る前", "ミナ", game.ClearedWithCompanion("rei")
            ? "お話を聞けます。……聞ける、はずです。" : "潜れます。……潜れる、はずです。"),
        new(3, "三人の帰還後・あなたへの下書き", "ミナの下書き", "ご主人様。今日は、もう、休んでもよろしいですか。"),
        new(3, "三人の帰還後・あなたへの下書き", "ミナ", "……何もできなくなったら。わたくしは、何を話せばよいのでしょう。"),
        new(3, "三人の帰還後・あなたへの下書き", "", "送信には、触れませんでした。入力欄を閉じると、いつもの報告だけが残りました。"),
        new(3, "三人の帰還後・あなたへの下書き", "ミナ", "……この重さくらい、わたくしが、持ちます。"),
    };

    private static readonly Line[] Aftermath =
    {
        new(5, "いま・あなたへ続く回線", "主人公", "……ミナ。聞こえる？"),
        new(5, "いま・あなたへ続く回線", "ミナ", "ご主人様……。今、聞こえました。"),
        new(5, "いま・あなたへ続く回線", "主人公", "よかった。回線は、このままつないでおく。"),
        new(5, "いま・あなたへ続く回線", "ミナ", "はい。今度は、わたくしからお話しします。"),
    };

    public static void Play(Hud hud, Node world, bool aftermath, Action completed)
    {
        var game = hud.GetNode<GameManager>("/root/Game");
        string rescuer = Jobs.Get(game.SelectedJob).CharacterId;
        hud.AddChild(new MinaStoryFilm
        {
            Name = "MinaStoryFilm", ZIndex = -10,
            _atlasPath = "res://char/v3/mina_story_atlas.png", _storyName = "Mina",
            _shotImages = new()
            {
                [5] = $"res://char/bg2/story/cg_mina_rescue_{rescuer}_v1.png",
            },
            _hud = hud, _world = world, _aftermath = aftermath,
            _lines = aftermath ? Aftermath : Memory(game), _completed = completed,
        });
    }
}
