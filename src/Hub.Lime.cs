using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Hub
{
    private const float LimeHeaderY = 107;
    private const int LimeTextSize = 22;
    private const int LimeAppIndex = 9;
    private static Color LimeAccent => new("ade48e");
    private static readonly Color LimePaper = new("e9f0e9"), LimeInk = new("203f35");
    private readonly List<LimeBubble> _limeBubbles = new();
    private LimeBubble? _limeCurrent;
    private bool _limeConversation;
    private float _limeHeight, _limeScroll;
    private int _limeSelection;
    private LimeFriend? _limeFriend;
    private LimeBubble? _limeDirectDraft;
    private double _limeDirectReveal;
    private readonly Dictionary<LimeFriend, double> _limeReplyWait = new();
    private const float LimeTypingHeight = 71;
    private bool LimeOpen => _limeConversation || _mode is Mode.LimeFriends or Mode.LimeHistory or Mode.LimeDirect;
    private sealed record LimeFriend(string Id, string Name, (string Send, string Reply)[] Exchanges);
    private static readonly LimeFriend[] LimeFriends =
    {
        new("koharu", "こはる", new (string, string)[] {
            ("今日の抹茶オレ、どうだった？", "新しいの飲んだ！　甘いと思ったら最後にちょっと苦くてさ。それが当たり。"),
            ("当たりのやつ、僕にも教えて。", "じゃあ明日の昼休みにね。パック捨てる前に写真撮っとく。"),
            ("ペンライトって、曲に合わせて振るの難しい？", "最初は好きなとこで振ればいいよ。うれしくて手が上がったなら、そこが合図！"),
            ("次は僕も一緒に振ってみたいな。", "いいじゃん。あたしの隣ね。ズレても二人なら笑えるし。") }),
        new("rei", "レイ", new (string, string)[] {
            ("レイ、今は配信の準備中？", "今日はお休み。マイク切って、さっきから本の同じページ読んでる。"),
            ("どの辺が好きで読み返してるの？", "手紙の返事を待つ場面。結末を知ってるのに、ここで毎回そわそわするのよね。"),
            ("配信の前って、最初の一言は決めてる？", "一応ね。けど最初のコメントを見ると、大抵そっちに返事しちゃう。"),
            ("じゃあ、今日は僕が最初に「こんばんは」。", "こんばんは。……ほんとだ、用意した挨拶いらなかったわ。") }),
        new("akari", "あかり", new (string, string)[] {
            ("あかり、お茶にしようか。", "する。ちょうど送信ボタン押すとこだった。お湯が沸くまで、そのメールは下書き。"),
            ("お湯、もう沸いたよ。", "早い。じゃあ今日は薄めで。急いで飲むと、また舌やけどするから。"),
            ("机の端にある付箋、何を書いたの？", "「帰りに牛乳」。締切みたいな字で書いたせいで、見るたび背筋が伸びるんだよね。"),
            ("牛乳、忘れないように僕も覚えとく。", "ありがと。二人がかりで牛乳一本。なんかいいね、そういうの。") }),
    };

    private sealed class LimeBubble
    {
        internal readonly string Speaker;
        internal readonly string[] Lines;
        internal readonly string DisplayText;
        internal readonly bool Mine, Shared;
        internal readonly float Width, Height;

        internal LimeBubble(string speaker, string text)
        {
            Speaker = speaker;
            Mine = speaker == "あなた";
            Shared = speaker.StartsWith("Ｙ") || speaker.Contains("投稿");
            Lines = UiKit.WrapLines(UiKit.Zen, text, LimeTextSize, 322).ToArray();
            DisplayText = string.Join("\n", Lines);
            Width = Mathf.Max(50, Lines.Max(line => UiKit.TextW(UiKit.Zen, line, LimeTextSize)) + 28);
            Height = Lines.Length * 34 + 57;
        }
    }

    private void StartHubConversation((string, string)[] lines, bool noPost = false, Mode returnMode = Mode.Cards,
        string? seenKey = null, string[]? faces = null, bool returning = false)
    {
        StartDialogue(lines, null, noPost, returnMode, seenKey, faces);
        if (lines.Any(line => line.Item1 == "ミナ") && lines.All(line => line.Item1 is "ミナ" or "あなた" or "ミナの投稿" || line.Item1.StartsWith("Ｙ")))
        {
            if (returning)
                _dlg = new (string, string)[] { ("あなた", "おかえり、ミナ。"), ("ミナ", "ただいま、ご主人様。") }.Concat(_dlg).ToArray();
            int disconnect = returning && MinaLineUnavailable ? System.Array.FindIndex(_dlg, line => line.tx == MinaLastCall) : -1;
            BeginMinaCall(casual: false, disconnectIndex: disconnect);
            return;
        }
        _limeConversation = true;
        ResetLimeThread();
        _limeCurrent = null;
    }

    private void ResetLimeThread()
    {
        _limeFriend = null;
        _limeDirectDraft = null;
        _limeDirectReveal = 0;
        _limeHeight = _limeScroll = 0;
        _limeBubbles.Clear();
    }

    private void OpenLimeFriends()
    {
        _mode = Mode.LimeFriends;
        ResetLimeThread();
        _xHeld = _zHeld = true;
    }

    private void OpenLimeThread(int selection)
    {
        ResetLimeThread();
        _limeSelection = selection;
        _limeFriend = selection == 0 ? null : LimeFriends[selection - 1];
        _mode = _limeFriend == null ? Mode.LimeHistory : Mode.LimeDirect;
        _zHeld = _xHeld = true;
    }

    private List<(string Speaker, string Text)> CurrentLimeHistory => _limeFriend == null
        ? _game.LimeHistory : _game.LimeThread(_limeFriend.Id);

    private void RefreshLimeHistory()
    {
        var history = CurrentLimeHistory;
        while (_limeBubbles.Count < history.Count)
        {
            var (speaker, text) = history[_limeBubbles.Count];
            var bubble = new LimeBubble(speaker, text);
            _limeBubbles.Add(bubble);
            _limeHeight += bubble.Height;
        }
    }

    private void ProcessLime(double delta)
    {
        bool confirm = Input.IsKeyPressed(Key.Enter) || Input.IsKeyPressed(Key.KpEnter) || Input.IsKeyPressed(Key.Z)
            || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool pressed = confirm && !_zHeld;
        _zHeld = confirm;
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_xHeld;
        _xHeld = back;
        bool click = Pad.MouseClick();
        if (!_limeConversation && (backEdge || click && LimeBackRect.HasPoint(PhonePointer)))
        {
            if (_mode == Mode.LimeFriends) { GoHome(); _homeSel = LimeAppIndex; }
            else OpenLimeFriends();
            return;
        }
        if (_mode == Mode.LimeFriends)
        {
            if (Input.IsActionJustPressed("ui_up")) _limeSelection = (_limeSelection + LimeFriends.Length) % (LimeFriends.Length + 1);
            if (Input.IsActionJustPressed("ui_down")) _limeSelection = (_limeSelection + 1) % (LimeFriends.Length + 1);
            for (int i = 0; i <= LimeFriends.Length; i++)
                if (Pad.UsingMouse && LimeRowRect(i).HasPoint(PhonePointer))
                {
                    _limeSelection = i;
                    if (click) { OpenLimeThread(i); return; }
                }
            if (pressed) OpenLimeThread(_limeSelection);
            return;
        }

        RefreshLimeHistory();
        if (_limeConversation) DlgEnsurePages();
        float direction = (Input.IsActionPressed("ui_up") ? 1 : 0) - (Input.IsActionPressed("ui_down") ? 1 : 0);
        float height = _limeHeight + LimeIncomingHeight;
        _limeScroll = Mathf.Clamp(_limeScroll + direction * 380 * (float)delta + Pad.WheelDelta() * WheelStep,
            0, Mathf.Max(0, height - LimeViewHeight));
        bool send = pressed || click && LimeSendRect.HasPoint(PhonePointer);
        if (_limeConversation) ProcessLimeStory(delta, send);
        else if (_limeFriend != null) ProcessLimeDirect(delta, send, _limeFriend);
    }

    private void ProcessLimeStory(double delta, bool send)
    {
        _dlgLineT += delta;
        if (!_limeCurrent!.Mine)
        {
            if (_dlgLineT >= LimeStoryTypingTime + Mathf.Clamp(DlgCurPage.Length / 15f, 1.4f, 6f)) AdvanceDialogue();
            return;
        }
        bool ready = DlgPageReady;
        _dlgReveal = DialogueBox.AdvanceReveal(DlgCurPage, _dlgReveal, delta, _game.MsgCharsPerSec, DlgPacing);
        if (ready && (send || _autoplay && _dlgLineT >= AutoAdvance))
        {
            Audio.Instance?.PlayUiConfirm();
            AdvanceDialogue();
        }
    }

    private void ProcessLimeDirect(double delta, bool send, LimeFriend friend)
    {
        if (_limeReplyWait.ContainsKey(friend)) return;
        var history = CurrentLimeHistory;
        var exchange = friend.Exchanges[history.Count / 2 % friend.Exchanges.Length];
        _limeDirectDraft ??= new LimeBubble("あなた", exchange.Send);
        bool ready = _limeDirectReveal >= _limeDirectDraft.DisplayText.Length;
        _limeDirectReveal = DialogueBox.AdvanceReveal(_limeDirectDraft.DisplayText, _limeDirectReveal, delta, _game.MsgCharsPerSec);
        if (!ready || !send) return;
        history.Add(("あなた", exchange.Send));
        _limeReplyWait[friend] = LimeTypingTime(exchange.Reply);
        _game.AutoSave();
        _limeDirectDraft = null;
        _limeDirectReveal = 0;
        _limeScroll = 0;
        Audio.Instance?.PlayUiConfirm();
    }

    private static double LimeTypingTime(string message) => Mathf.Clamp(1.2f + message.Length * .045f, 1.6f, 3.5f);

    private void RestoreLimeReplies()
    {
        foreach (var friend in LimeFriends)
        {
            var history = _game.LimeThread(friend.Id);
            if (history.Count > 0 && history[^1].Speaker == "あなた")
                _limeReplyWait[friend] = LimeTypingTime(friend.Exchanges[history.Count / 2 % friend.Exchanges.Length].Reply);
        }
    }

    private void TickLimeReplies(double delta)
    {
        if (_limeReplyWait.Count == 0) return;
        foreach (var friend in LimeFriends)
        {
            if (!_limeReplyWait.TryGetValue(friend, out double remaining)) continue;
            if (remaining > delta) { _limeReplyWait[friend] = remaining - delta; continue; }
            var history = _game.LimeThread(friend.Id);
            history.Add((friend.Name, friend.Exchanges[history.Count / 2 % friend.Exchanges.Length].Reply));
            _limeReplyWait.Remove(friend);
            _game.AutoSave();
        }
    }

    private double LimeStoryTypingTime => _limeConversation && _limeCurrent is { Mine: false, Shared: false }
        ? LimeTypingTime(_dlg[_dlgIdx].tx) : 0;
    private string? LimeTypingSpeaker => _limeFriend != null && _limeReplyWait.ContainsKey(_limeFriend)
        ? _limeFriend.Name : _limeConversation && _dlgLineT < LimeStoryTypingTime ? _limeCurrent!.Speaker : null;
    private LimeBubble? LimeIncoming => _limeConversation && _limeCurrent is { Mine: false } && _dlgLineT >= LimeStoryTypingTime
        ? _limeCurrent : null;
    private float LimeIncomingHeight => LimeTypingSpeaker != null ? LimeTypingHeight : LimeIncoming?.Height ?? 0;
    private LimeBubble? LimeDraft => _limeConversation && _limeCurrent is { Mine: true } ? _limeCurrent : _limeDirectDraft;
    private int LimeTyped => (int)(_limeConversation ? _dlgReveal : _limeDirectReveal);
    private int LimeCaretLine
    {
        get
        {
            if (LimeDraft is not { } draft) return 0;
            int left = LimeTyped, line = 0;
            while (line < draft.Lines.Length - 1 && left > draft.Lines[line].Length)
                left -= draft.Lines[line++].Length + 1;
            return line;
        }
    }
    private float LimeFooterY => H - 70 - Mathf.Min(3, LimeCaretLine) * 30;
    private float LimeViewHeight => LimeFooterY - LimeHeaderY - 28;
    private static Rect2 LimeBackRect => new(PhoneX + 12, 18, 54, 70);
    private static Rect2 LimeSendRect => new(PhoneX + PhoneW - 66, H - 62, 50, 48);
    private static Rect2 LimeRowRect(int index) => new(PhoneX + 14, index == 0 ? 141 : 257 + (index - 1) * 86, PhoneW - 28, 80);

    private void DrawLime()
    {
        _drawCanvas.DrawRect(new Rect2(PhoneX, 0, PhoneW, H), LimePaper);
        if (_mode == Mode.LimeFriends)
        {
            DrawLimeHeader("フレンド");
            UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(PhoneX + 27, 114), "グループ", 16, LimeInk);
            UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(PhoneX + 27, 229), $"フレンド　{LimeFriends.Length}", 16, LimeInk);
            for (int i = 0; i <= LimeFriends.Length; i++)
            {
                var friend = i == 0 ? null : LimeFriends[i - 1];
                Rect2 row = LimeRowRect(i);
                UiKit.Box(_drawCanvas, row, i == _limeSelection ? new Color("d1e8c9") : Colors.White, 14);
                Vector2 avatar = row.Position + new Vector2(36, 40);
                if (friend != null)
                {
                    var (face, color, crop) = SpeakerFace(friend.Name);
                    UiKit.FaceAvatar(_drawCanvas, avatar, 23, face, color, false, crop, 1, _t);
                }
                else
                {
                    _drawCanvas.DrawCircle(avatar, 23, new Color("39735b"));
                    UiKit.Text(_drawCanvas, UiKit.ZenBold, avatar - new Vector2(23, 15), "L", 22, Colors.White, HorizontalAlignment.Center, 46);
                }
                var history = friend == null ? _game.LimeHistory : _game.LimeThread(friend.Id);
                float y = row.Position.Y + (history.Count == 0 ? 24 : 10);
                UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(row.Position.X + 76, y), friend?.Name ?? "帰還トーク", 22, LimeInk);
                if (friend != null && _limeReplyWait.ContainsKey(friend))
                    UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(row.Position.X + 76, y + 32), "入力中…", 16, LimeInk);
                else if (history.Count > 0)
                {
                    var last = history[^1];
                    string preview = FitText(UiKit.Zen, last.Text.Replace("\n", " "), 16, row.Size.X - 116);
                    UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(row.Position.X + 76, y + 32), preview, 16, new Color("4c685b"));
                }
                UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(row.End.X - 28, row.Position.Y + 24), "›", 22, LimeInk);
            }
            return;
        }
        RefreshLimeHistory();
        if (_limeConversation) DlgEnsurePages();
        var incoming = LimeIncoming;
        float height = _limeHeight + LimeIncomingHeight;
        float bottom = LimeHeaderY + 14 + Mathf.Min(LimeViewHeight, height) + _limeScroll;
        if (LimeTypingSpeaker is { } speaker)
        {
            DrawLimeTyping(speaker, bottom, _limeConversation ? LineFace(_dlgIdx) : null);
            bottom -= LimeTypingHeight;
        }
        else if (incoming != null)
        {
            DrawLimeBubble(incoming, bottom, LineFace(_dlgIdx));
            bottom -= incoming.Height;
        }
        for (int i = _limeBubbles.Count - 1; i >= 0 && bottom > LimeHeaderY; i--)
        {
            var bubble = _limeBubbles[i];
            if (bottom - bubble.Height < LimeFooterY) DrawLimeBubble(bubble, bottom, null);
            bottom -= bubble.Height;
        }
        if (_mode == Mode.LimeHistory && _limeBubbles.Count == 0)
            UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(PhoneX, 320), "帰還すると、ここに会話が届きます。", 19, LimeInk,
                HorizontalAlignment.Center, PhoneW);

        DrawLimeHeader(_limeFriend?.Name ?? "帰還トーク");
        DrawLimeComposer();
    }

    private void DrawLimeHeader(string title)
    {
        _drawCanvas.DrawRect(new Rect2(PhoneX, 0, PhoneW, LimeHeaderY), new Color("173b32"));
        float left = PhoneX + (_limeConversation ? 30 : 74);
        if (!_limeConversation)
        {
            Vector2 c = LimeBackRect.GetCenter();
            _drawCanvas.DrawPolyline(new[] { c + new Vector2(6, -10), c + Vector2.Left * 5, c + new Vector2(6, 10) }, LimeAccent, 3, true);
        }
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(left, 13), "LIME", 19, LimeAccent);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(left, 43), title, 30, Colors.White);
    }

    private void DrawLimeComposer()
    {
        float y = LimeFooterY;
        _drawCanvas.DrawRect(new Rect2(PhoneX, y, PhoneW, H - y), Colors.White);
        _drawCanvas.DrawLine(new Vector2(PhoneX, y), new Vector2(PhoneX + PhoneW, y), new Color("c6d5cd"), 1);
        var field = new Rect2(PhoneX + 18, y + 10, PhoneW - 96, H - y - 20);
        UiKit.Box(_drawCanvas, field, new Color("f1f4f2"), 22, new Color("d2dbd5"), 1);
        Vector2 at = field.Position + new Vector2(17, 9);
        var draft = LimeDraft;
        if (draft != null)
        {
            int left = LimeTyped, caretLine = LimeCaretLine, first = Mathf.Max(0, caretLine - 3);
            for (int i = 0; i <= caretLine; i++)
            {
                string line = draft.Lines[i];
                int count = Mathf.Clamp(left, 0, line.Length);
                if (i >= first)
                {
                    string typed = line[..count];
                    UiKit.Text(_drawCanvas, UiKit.Zen, at, typed, LimeTextSize, LimeInk);
                    if (i == caretLine && (_t % 1) < .65)
                    {
                        Vector2 caret = at + new Vector2(UiKit.TextW(UiKit.Zen, typed, LimeTextSize) + 2, 4);
                        _drawCanvas.DrawLine(caret, caret + Vector2.Down * 23, new Color("31845a"), 2);
                    }
                    at.Y += 30;
                }
                left -= line.Length + 1;
            }
        }
        else
        {
            UiKit.Text(_drawCanvas, UiKit.Zen, at, "メッセージ", 20, new Color("65796f"));
        }
        bool ready = draft != null && LimeTyped >= draft.DisplayText.Length;
        Vector2 c = LimeSendRect.GetCenter();
        Color ink = ready ? new("3979bd") : new("b3bec5");
        Vector2 tip = c + new Vector2(16, -14);
        _drawCanvas.DrawColoredPolygon(new[] { c + new Vector2(-17, -2), tip, c + new Vector2(-6, 4) }, ink);
        _drawCanvas.DrawColoredPolygon(new[] { c + new Vector2(-5, 6), tip, c + new Vector2(6, 16) }, ink);
        _drawCanvas.DrawColoredPolygon(new[] { c + new Vector2(-5, 6), c + new Vector2(-1, 9),
            c + new Vector2(-5, 13) }, ink.Darkened(.2f));
    }

    private void DrawLimeTyping(string speaker, float bottom, Texture2D? portrait)
    {
        var (face, color, crop) = SpeakerFace(speaker);
        float y = bottom - LimeTypingHeight + 12;
        UiKit.FaceAvatar(_drawCanvas, new Vector2(PhoneX + 38, y + 21), 19, portrait ?? face, color, false, Mathf.Max(0, crop), 1, _t);
        const string label = "入力中";
        float width = UiKit.TextW(UiKit.Zen, label, 18);
        Vector2 origin = new(PhoneX + 70, y);
        UiKit.Box(_drawCanvas, new Rect2(origin, new Vector2(width + 64, 44)), Colors.White, 17);
        UiKit.Text(_drawCanvas, UiKit.Zen, origin + new Vector2(14, 8), label, 18, LimeInk);
        for (int i = 0; i < 3; i++)
        {
            float pulse = .5f + .5f * Mathf.Cos((float)_t * 6 - i * 1.4f);
            _drawCanvas.DrawCircle(origin + new Vector2(width + 24 + i * 10, 24 - 3 * pulse), 3, new Color(LimeInk, .35f + .65f * pulse));
        }
    }

    private void DrawLimeBubble(LimeBubble bubble, float bottom, Texture2D? portrait)
    {
        float top = bottom - bubble.Height;
        float x = bubble.Mine ? PhoneX + PhoneW - 22 - bubble.Width
            : bubble.Shared ? PhoneX + (PhoneW - bubble.Width) * .5f : PhoneX + 70;
        var rect = new Rect2(x, top + 25, bubble.Width, bubble.Height - 38);
        if (!bubble.Mine && !bubble.Shared)
        {
            var (face, color, crop) = SpeakerFace(bubble.Speaker);
            UiKit.FaceAvatar(_drawCanvas, new Vector2(PhoneX + 38, rect.Position.Y + 17),
                19, portrait ?? face, color, false, Mathf.Max(0, crop), 1, _t);
        }
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(x, top), bubble.Speaker, 15, new Color("3b6154"),
            bubble.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, bubble.Width);
        Color fill = bubble.Shared ? new("f6f5e8") : bubble.Mine ? LimeAccent : Colors.White;
        UiKit.Box(_drawCanvas, rect, fill, 15, bubble.Shared ? new Color("d7d7ba") : null, 1);
        if (!bubble.Shared)
        {
            float side = bubble.Mine ? rect.End.X : x;
            float sign = bubble.Mine ? 1 : -1;
            _drawCanvas.DrawColoredPolygon(new[] { new Vector2(side - sign * 4, rect.Position.Y + 8),
                new Vector2(side + sign * 7, rect.Position.Y + 6), new Vector2(side - sign * 2, rect.Position.Y + 21) }, fill);
        }
        Vector2 at = rect.Position + new Vector2(14, 8);
        foreach (string line in bubble.Lines)
        {
            UiKit.Text(_drawCanvas, UiKit.Zen, at, line, LimeTextSize, LimeInk);
            at.Y += 34;
        }
    }
}
