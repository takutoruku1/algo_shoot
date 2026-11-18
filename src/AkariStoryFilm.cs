using Godot;
using System;

public partial class AkariStoryFilm : StoryFilm
{
    private static readonly Line[] Memory =
    {
        new(2, "三日前・最後の出社日", "西野", "……一緒に働けて、よかった。"),
        new(2, "三日前・最後の出社日", "あかり", "うん。……また、"),
        new(2, "三日前・最後の出社日", "", "電話が鳴りました。受話器を取っている間に——向かいのモニタが、消えます。"),
        new(3, "金曜の夜・帰りの電車", "送信したメッセージ", "あたしも、", Author: Job.Melee),
        new(3, "金曜の夜・帰りの電車", "送信履歴", "メッセージの送信を取り消しました"),
        new(3, "金曜の夜・帰りの電車", "", "自分用のメモが開きます。消そうとして、指が止まって——その一行だけが、残りました。"),
    };

    private static readonly Line[] Aftermath =
    {
        new(0, "約三年前・入社した春", "西野", "うん。これが終わったら、お昼は外で食べよう。"),
        new(0, "約三年前・入社した春", "あかり", "行く。今日、天気いいし。"),
        new(1, "二年後・二十三時過ぎ", "西野", "……帰り、何か食べていかない？"),
        new(1, "二年後・二十三時過ぎ", "あかり", "ごめん。明日の分が、まだ。……落ち着いたら、行こ。"),
        new(1, "二年後・二十三時過ぎ", "", "会話が終わって、キーを打つ音だけが続きます。……「好き」は、この日も、送られませんでした。"),
        new(4, "その後の昼休み", "相談したいこと", "ちゃんと休めるところで、働きたいです。"),
        new(4, "その後の昼休み", "予約完了", "土曜日　15:00\n転職相談の予約が完了しました。"),
        new(4, "その後の昼休み", "", "予約画面を閉じると、金曜から残る既読と、取り消し履歴が十二件。"),
        new(4, "その後の昼休み", "西野へ・下書き", AkariReplyVideo.Message, Author: Job.Melee),
        new(4, "その後の昼休み", "あかり", "……今度は、消さない。"),
    };

    private AkariReplyVideo? _reply;

    public override void _Process(double delta)
    {
        if (_reply is { Finished: false }) return;
        base._Process(delta);
    }

    protected override void OnLinesCompleted()
    {
        if (!_aftermath) { base.OnLinesCompleted(); return; }
        _hud.HideBubble();
        _reply = new AkariReplyVideo { Name = "ReplyVideo", ZIndex = 20, Completed = BeginLeave };
        AddChild(_reply);
    }

    public static void Play(Hud hud, Node world, bool aftermath, Action completed)
    {
        hud.AddChild(new AkariStoryFilm
        {
            Name = "AkariStoryFilm", ZIndex = -10,
            _atlasPath = "res://char/v3/akari_story_atlas.png", _storyName = "Akari",
            _shotImages = new()
            {
                [0] = "res://char/bg2/story/cg_akari_office_spring.png",
                [1] = "res://char/bg2/story/cg_akari_office_overtime.png",
                [4] = "res://char/bg2/story/cg_akari_office_reply.png",
            },
            _hud = hud, _world = world, _aftermath = aftermath,
            _lines = aftermath ? Aftermath : Memory, _completed = completed,
        });
    }

}
