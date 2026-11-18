using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Hub
{
    private enum MinaMood { Quiet, Amused, Shy, Concerned, Relieved }
    private sealed record MinaBeat(string Prompt, string Reply, MinaMood Mood, params MinaBeat[] Next);
    private static readonly (string Stage, MinaBeat Opening)[] MinaTopics =
    [
        ("", new MinaBeat("ミナ、今ちょっと話せる？", "はい、ご主人様。ご用件は……あ、ただのおしゃべりでも、もちろん。", MinaMood.Shy,
            new MinaBeat("用事はないよ。ミナの声が聞きたくて。", "では、こちらからも。ご主人様、今日はどんな空でしたか？", MinaMood.Shy,
                new MinaBeat("帰り道、細い月が見えてたよ。", "こちらの壁紙にも、細い月を置いてみました。同じ方を見ている気がします。", MinaMood.Quiet,
                    new MinaBeat("もう少しこのまま、月を見ていようか。", "はい。次のお返事は、ゆっくりで大丈夫です。わたくしも、眺めておりますので。", MinaMood.Relieved)),
                new MinaBeat("曇ってて、月は見えなかったよ。", "では、雲の係をご主人様に。わたくしは月の係です。……ふたりで一枚の空ですね。", MinaMood.Amused)),
            new MinaBeat("逆に、ミナから僕に用事はある？", "あります。……先に呼ばれてしまいました。わたくしも、お声が聞きたかったんです。", MinaMood.Shy))),
        ("", new MinaBeat("ミナの鼻歌、聞かせて。", "ら……。あっ。いまのは音を合わせただけです。まだ拍手は早いです、ご主人様。", MinaMood.Shy,
            new MinaBeat("でも、今の音、好きだよ。", "……ら、ら。今度は二音です。好きだと言われたぶん、一音おまけしました。", MinaMood.Shy,
                new MinaBeat("三音目は僕が歌っていい？", "どうぞ。……ふふ、ちょっと高い。もう一回、今度はわたくしがそちらに合わせます。", MinaMood.Amused)),
            new MinaBeat("じゃあ、僕は小さく手拍子するね。", "とん、とん。……あら、先にわたくしがつられました。歌の係だったのに。", MinaMood.Amused,
                new MinaBeat("ふたりで手拍子だけの曲にしよう。", "賛成です。曲名は『歌いそびれたふたり』。最初の一拍、お願いしますね。", MinaMood.Amused)))),
        ("", new MinaBeat("今日はちょっと、くたびれた。", "……そうでしたか。今日は何があったか、聞いても？　話すのも疲れる日なら、ここにいます。", MinaMood.Concerned,
            new MinaBeat("ひとつ失敗して、まだ気になってる。", "あとから何度も思い出すの、嫌ですね。……今、言い直したかったこと、わたくしに言ってみますか？", MinaMood.Concerned,
                new MinaBeat("「手伝って」って言えばよかった。", "はい、聞こえました。……その一言を言えたご主人様を、今日はちゃんと覚えておきます。", MinaMood.Relieved)),
            new MinaBeat("少しだけ、黙って一緒にいて。", "はい。……あ、今うなずきました。黙っていると伝わらないかと思って。もう黙りますね。", MinaMood.Quiet,
                new MinaBeat("見えてたよ。ありがとう。", "……よかった。黙っていても一緒にいられるの、ちょっとうれしいです。", MinaMood.Relieved)))),
        ("", new MinaBeat("お茶をいれたよ。一緒に休もう。", "何のお茶でしょう。湯気は見えても、匂いだけがこちらに来ません。……ちょっと悔しい。", MinaMood.Quiet,
            new MinaBeat("ほうじ茶。少し焦げたみたいな匂い。", "焦げた、ですか。……あ、いま飲む前に笑いましたね。いい匂いの『焦げた』なんですね。", MinaMood.Amused),
            new MinaBeat("ミナなら、どんなお茶を選ぶ？", "湯気が長く残るものを。ご主人様が冷ましている間、こうしてお話しできるでしょう？", MinaMood.Shy))),
        ("akari", new MinaBeat("あかりさんの雨、覚えてる？", "はい。窓をいちばんゆっくり落ちた雨粒を、つい目で追っていました。変でしょうか。", MinaMood.Quiet,
            new MinaBeat("僕もよく雨粒どうしを競争させるよ。", "では次は競争を。わたくし、あの遅い子にします。……負けても最後まで見たいので。", MinaMood.Amused),
            new MinaBeat("その雨粒、最後はどうなったの？", "途中で小さいのとくっついて、すとん、と。……誰かに話せるほど覚えていたんですね、わたくし。", MinaMood.Relieved))),
        ("koharu", new MinaBeat("こはるの配信、次は一緒に見よう。", "はい。今度こそ、わたくしも『またね』って書きたいです。いつも、送りそびれてしまって。", MinaMood.Shy,
            new MinaBeat("送るタイミング、僕が合図するよ。", "お願いします。……あ、合図を待っているうちにお二人へ手を振ってしまいそうです。", MinaMood.Amused),
            new MinaBeat("ここで練習してみる？", "またね、こはるさん。……言えました。練習なのに、本当に次が楽しみになりました。", MinaMood.Relieved))),
        ("final", new MinaBeat("もう一度、おかえりって言っていい？", "……はい。今度は聞き逃したくないので、もう少しだけ画面の近くで。", MinaMood.Shy,
            new MinaBeat("おかえり、ミナ。", "ただいま、ご主人様。……間に合いました。今度は、ご主人様が笑ったところまで見えました。", MinaMood.Relieved),
            new MinaBeat("ミナにも、おかえりって言ってほしい。", "おかえりなさい、ご主人様。……ふふ。帰る場所は、こちらにもあったんですね。", MinaMood.Relieved))),
    ];

    private MinaBeat? _minaBeat;
    private MinaBeat[] _minaChoices = [];
    private int _minaChoice, _minaChoiceTop;
    private bool _minaFollowup;
    private const int MinaVisibleRows = 3;
    private int MinaOptionCount => _minaChoices.Length + (_minaFollowup ? 1 : 0);
    private static Rect2 MinaChoiceRect(int row) => new(PhoneX + 24, 513 + row * 46, PhoneW - 48, 42);

    private IEnumerable<MinaBeat> AvailableMinaTopics() => MinaTopics
        .Where(topic => topic.Stage == "" || (topic.Stage == "final" ? _game.IsFinalCleared : _game.IsStageCleared(topic.Stage)))
        .Select(topic => topic.Opening);

    private static IEnumerable<MinaBeat> ConversationBeats(MinaBeat beat)
    {
        yield return beat;
        foreach (var child in beat.Next)
            foreach (var reply in ConversationBeats(child)) yield return reply;
    }

    private void OfferMinaChoices(bool followup)
    {
        _minaFollowup = followup;
        _minaChoices = followup ? _minaBeat!.Next : AvailableMinaTopics().ToArray();
        _minaChoice = _minaChoiceTop = 0;
    }

    private void ChooseMinaLine()
    {
        if (_minaChoice == _minaChoices.Length)
        {
            OfferMinaChoices(false);
            return;
        }
        _minaBeat = _minaChoices[_minaChoice];
        _minaChoices = [];
        _dlg = [("あなた", _minaBeat.Prompt), ("ミナ", _minaBeat.Reply)];
        _dlgIdx = 0;
        AdvanceDialogue();
        Audio.Instance?.PlayUiConfirm();
    }

    private void ProcessMinaChoices(bool confirm, bool click)
    {
        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        float wheel = Pad.WheelDelta();
        if ((up || down) && !_navHeld)
        {
            _minaChoice = Mathf.PosMod(_minaChoice + (up ? -1 : 1), MinaOptionCount);
            Pad.ReleaseMouseFollow();
        }
        _navHeld = up || down;
        int maxTop = Math.Max(0, MinaOptionCount - MinaVisibleRows);
        if (wheel != 0)
        {
            _minaChoiceTop = Mathf.Clamp(_minaChoiceTop - Math.Sign(wheel), 0, maxTop);
            _minaChoice = Mathf.Clamp(_minaChoice, _minaChoiceTop, Math.Min(MinaOptionCount, _minaChoiceTop + MinaVisibleRows) - 1);
        }
        else _minaChoiceTop = Mathf.Clamp(_minaChoiceTop, Math.Max(0, _minaChoice - MinaVisibleRows + 1), Math.Min(_minaChoice, maxTop));
        for (int row = 0; row < Math.Min(MinaVisibleRows, MinaOptionCount); row++)
        {
            if (!click || !MinaChoiceRect(row).HasPoint(PhonePointer)) continue;
            _minaChoice = _minaChoiceTop + row;
            ChooseMinaLine();
            return;
        }
        if (confirm) ChooseMinaLine();
    }

    private void DrawMinaChoices()
    {
        int rows = Math.Min(MinaVisibleRows, MinaOptionCount);
        for (int row = 0; row < rows; row++)
        {
            int i = _minaChoiceTop + row;
            var rect = MinaChoiceRect(row);
            bool selected = Pad.UsingMouse ? rect.HasPoint(PhonePointer) : i == _minaChoice;
            string label = i == _minaChoices.Length ? "別の話をしよう" : _minaChoices[i].Prompt;
            UiKit.Box(_drawCanvas, rect, selected ? new Color("354668") : CallSurface, 12,
                new Color(CallLight, selected ? .95f : .35f), selected ? 2 : 1);
            UiKit.Text(_drawCanvas, UiKit.Zen, rect.Position + new Vector2(15, 7), label, 17, Colors.White);
        }
        if (MinaOptionCount <= rows) return;
        float thumb = rows * 46f * rows / MinaOptionCount;
        _drawCanvas.DrawRect(new Rect2(PhoneX + PhoneW - 18, 513 + _minaChoiceTop * 46f * rows / MinaOptionCount, 3, thumb), CallLight);
    }
}
