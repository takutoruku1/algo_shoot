using Godot;
using System;
using System.Linq;

public partial class Hub
{
    private const int MinaHomeId = 23210;
    private static Rect2 MinaHomeRect => new(W / 2 - 98, 220, 196, 44);
    private static Rect2 MinaHistoryButton => new(PhoneX + PhoneW - 96, 28, 74, 38);
    private static Rect2 MinaCaption => new(PhoneX + 24, 485, PhoneW - 48, 163);
    private Rect2 MinaHangupButton => _minaCall && _minaCasual
        ? new(W / 2 - 35, 653, 70, 46) : new(MinaTalkButton.GetCenter().X - 35, 653, 70, 46);
    private const double MinaRingDuration = 1.6;
    private const string MinaNoAnswer = "ミナからの応答がない。";
    private const string MinaLastCall = "ミナ？　聞こえる？　……返事して、ミナ。";
    private int _minaDisconnectIndex = -1;
    private bool MinaLineUnavailable => _game.AllStoryCleared && _game.IsMinaLockedForFinal;
    private bool MinaLineOpen => _minaCall || _mode == Mode.MinaUnanswered;
    private string MinaConnectionStatus => _minaCallTime < MinaRingDuration ? "呼び出し中…" : MinaNoAnswer;
    private static readonly Color CallLight = new("c9cffe"), CallSurface = new("18243b");
    private readonly Texture2D _minaSmile = GD.Load<Texture2D>(CompanionDialogue.MinaSmilePortrait);
    private readonly Texture2D _minaWorried = GD.Load<Texture2D>(CompanionDialogue.MinaWorriedPortrait);
    private bool _minaCall, _minaCasual, _minaHomeFocus, _homeRevealFromMina, _minaHistoryOpen, _minaHistoryHeld;
    private double _minaCallTime;
    private MinaCallPortrait? _minaPortrait;
    private MinaCallPortrait.BodyPose _minaBody;
    private float _minaNeckAngle;
    private float _minaNeckVelocity, _minaLeanVelocity, _minaBowVelocity;
    private Texture2D _minaCallFace = null!;
    private Vector2 _minaGaze, _minaGazeVelocity;
    private double _minaBlinkStart, _minaNextBlink;
    private int _minaBlinkIndex;
    private static readonly double[] MinaBlinkIntervals = { 4.35, 3.25, 5.1, 3.9 };
    private static readonly Vector2 MinaAside = new(9, 3.5f), MinaEyeContact = new(-1.5f, -1);
    private LimeBubble[] _minaArchive = Array.Empty<LimeBubble>();
    private float _minaArchiveScroll, _minaArchiveHeight;

    private void DrawMinaHomeButton()
    {
        var rect = MinaHomeRect;
        bool focus = _minaHomeFocus && _mode == Mode.Home;
        if (focus) UiKit.Box(_drawCanvas, rect.Grow(3), Colors.Transparent, 17, CallLight, 2);
        _phoneVisuals.DrawMinaButton(_drawCanvas, rect);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, rect.Position + new Vector2(48, 8), "ミナを呼ぶ",
            20, Colors.White, HorizontalAlignment.Center, 112);
    }

    private void OpenMinaCall()
    {
        if (MinaLineUnavailable)
        {
            _mode = Mode.MinaUnanswered;
            _minaHomeFocus = true;
            ResetMinaLineView();
            Audio.Instance?.PlayUiConfirm();
            return;
        }
        var history = _game.LimeThread("mina");
        var beats = AvailableMinaTopics().SelectMany(ConversationBeats).ToDictionary(b => b.Prompt);
        _minaBeat = null;
        bool awaitingReply = false;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Speaker != "あなた" || !beats.TryGetValue(history[i].Text, out var beat)) continue;
            _minaBeat = beat;
            awaitingReply = !history.Skip(i + 1).Any(line => line == ("ミナ", beat.Reply));
            break;
        }
        StartDialogue([_minaBeat is null ? ("あなた", "") : ("ミナ", _minaBeat.Reply)], null, noPost: true, returnMode: Mode.Home);
        BeginMinaCall(casual: true);
        if (!awaitingReply)
        {
            bool continuing = _minaBeat?.Next.Length > 0;
            if (!continuing) _minaBeat = null;
            OfferMinaChoices(continuing);
            _minaCallFace = continuing ? MinaSpeakingFace() : _minaSmile;
        }
        Audio.Instance?.PlayUiConfirm();
    }

    private void ResetMinaLineView()
    {
        _minaHistoryOpen = false;
        _minaCallTime = 0;
        _minaHistoryHeld = Input.IsKeyPressed(Key.H) || Pad.Pressed(JoyButton.X);
        _zHeld = Pad.AdvanceHeld();
        _xHeld = true;
    }

    private void BeginMinaCall(bool casual, int disconnectIndex = -1)
    {
        if (!casual) _minaBeat = null;
        _minaChoices = [];
        _minaCall = true;
        _minaDisconnectIndex = disconnectIndex;
        _minaCasual = casual;
        _minaHomeFocus = casual;
        ResetMinaLineView();
        _minaPortrait ??= new MinaCallPortrait();
        _minaPortrait.Prepare(_minaFace!);
        if (casual)
        {
            _minaPortrait.Prepare(_minaSmile);
            _minaPortrait.Prepare(_minaWorried);
        }
        for (int i = 0; i < _dlg.Length; i++)
        {
            if (_dlg[i].sp == "ミナ") _minaPortrait.Prepare(MinaFaceFor(i));
            else if (WarmMinaPrompt(_dlg[i].sp, _dlg[i].tx)) _minaPortrait.Prepare(_minaSmile);
        }
        _minaCallFace = _minaFace!;
        _minaGaze = MinaAside;
        _minaGazeVelocity = Vector2.Zero;
        _minaBlinkStart = -2;
        _minaNextBlink = 1.75;
        _minaBlinkIndex = 0;
        _minaNeckAngle = 0;
        _minaNeckVelocity = _minaLeanVelocity = _minaBowVelocity = 0;
        _minaBody = default;
    }

    private double MinaReplyPause => _dlg[_dlgIdx].sp == "ミナ" && _dlgPage == 0
        ? _minaBeat?.Mood switch { MinaMood.Shy => 1.18, MinaMood.Concerned => 1.05, MinaMood.Amused => .58, _ => .85 } : 0;
    private bool MinaCaptionReady => _dlgLineT >= MinaReplyPause && _minaCallTime >= .35;

    private MinaCallPortrait.Speech MinaSpeech => _minaChoices.Length == 0 && _dlg[_dlgIdx].sp == "ミナ"
        ? MinaCallPortrait.ReadSpeech(DlgCurPage, DlgPacing, _dlgLineT - MinaReplyPause) : default;

    private Texture2D MinaSpeakingFace() => MinaFaceFor(_dlgIdx);

    private static bool WarmMinaPrompt(string speaker, string text) => speaker == "あなた"
        && (text.StartsWith("おかえり") || text.Contains("ミナの声が聞きたくて"));

    private float MinaBlink => MinaCallPortrait.BlinkAmount(_minaCallTime - _minaBlinkStart);

    private void UpdateMinaAttention(float delta)
    {
        var (speaker, text) = _dlg[_dlgIdx];
        bool answering = speaker == "ミナ" && _minaChoices.Length == 0;
        bool welcomed = WarmMinaPrompt(speaker, text) && _dlgPage == 0;
        const double recognition = .82;
        bool recognitionBlink = welcomed && _dlgLineT >= recognition && _dlgLineT - delta < recognition
            && _minaCallTime - _minaBlinkStart > 1.35;
        if (recognitionBlink || _minaCallTime >= _minaNextBlink)
        {
            _minaBlinkStart = recognitionBlink ? _minaCallTime - (_dlgLineT - recognition) : _minaCallTime;
            _minaNextBlink = _minaCallTime + MinaBlinkIntervals[_minaBlinkIndex];
            _minaBlinkIndex = (_minaBlinkIndex + 1) % MinaBlinkIntervals.Length;
        }
        if (welcomed && _dlgLineT >= recognition + .09) _minaCallFace = _minaSmile;
        if (answering && _dlgLineT >= MinaReplyPause - .25) _minaCallFace = MinaSpeakingFace();

        bool consideringReply = answering && _dlgPage == 0 && _dlgLineT < .49
            && (text.StartsWith("ただいま") || text.StartsWith("……") || MinaSpeakingFace() == _minaWorried);
        bool shy = answering && _minaBeat?.Mood == MinaMood.Shy && _dlgPage == 0 && _dlgLineT < MinaReplyPause + .55;
        bool looksAside = _minaCallTime < .38 || consideringReply || shy || (answering && MinaSpeech.Hesitating);
        Vector2 focus = looksAside ? MinaAside : MinaEyeContact;
        if (answering && _minaBeat?.Mood == MinaMood.Relieved && _dlgPage == 0)
            focus = new Vector2(1, 4).Lerp(MinaEyeContact, Mathf.SmoothStep(0, 1, (float)(_dlgLineT / (MinaReplyPause + .45))));
        _minaGaze.X = SettlePose(_minaGaze.X, focus.X, ref _minaGazeVelocity.X, 13, delta);
        _minaGaze.Y = SettlePose(_minaGaze.Y, focus.Y, ref _minaGazeVelocity.Y, 13, delta);
    }

    private Texture2D MinaFaceFor(int index)
    {
        if (_dlg[index].sp != "ミナ") return _minaFace!;
        if (LineFace(index) is { } face) return face;
        if (_minaBeat != null) return _minaBeat.Mood switch
        {
            MinaMood.Concerned => _minaWorried,
            MinaMood.Quiet => _minaFace!,
            _ => _minaSmile,
        };
        string text = _dlg[index].tx;
        if (text.StartsWith("ご主人様、もう一度") || text.StartsWith("はい、ここに") || text.StartsWith("……返信を書いて"))
            return _minaWorried;
        if (text.StartsWith("ただいま") || text.StartsWith("ふふ") || text.StartsWith("……はい。ですから"))
            return _minaSmile;
        return _minaFace!;
    }

    private void ProcessMinaCall(double delta)
    {
        bool click = Pad.MouseClick(), back = Input.IsKeyPressed(Key.Escape) || Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.B);
        bool leave = back && !_xHeld;
        _xHeld = back;
        bool history = Input.IsKeyPressed(Key.H) || Pad.Pressed(JoyButton.X);
        bool toggleHistory = history && !_minaHistoryHeld || click && MinaHistoryButton.HasPoint(PhonePointer);
        _minaHistoryHeld = history;
        bool accept = Input.IsActionPressed("ui_accept") || Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter) || Pad.Pressed(JoyButton.A);
        bool confirm = accept && !_zHeld;
        _zHeld = accept;
        if (toggleHistory || _minaHistoryOpen && leave)
        {
            _minaHistoryOpen = !_minaHistoryOpen;
            if (_minaHistoryOpen)
            {
                _minaArchive = _game.LimeThread("mina").Select(line => new LimeBubble(line.Speaker, line.Text)).ToArray();
                _minaArchiveHeight = _minaArchive.Sum(line => line.Height);
                _minaArchiveScroll = 0;
            }
            return;
        }
        if (_minaHistoryOpen)
        {
            float direction = (Input.IsActionPressed("ui_up") ? 1 : 0) - (Input.IsActionPressed("ui_down") ? 1 : 0);
            _minaArchiveScroll = Mathf.Clamp(_minaArchiveScroll + direction * 380 * (float)delta + Pad.WheelDelta() * WheelStep,
                0, Mathf.Max(0, _minaArchiveHeight - 568));
            return;
        }
        _minaCallTime += delta;
        if (_mode == Mode.MinaUnanswered)
        {
            if (_minaCallTime - delta < MinaRingDuration && _minaCallTime >= MinaRingDuration)
                Audio.Instance?.PlayUiDeny();
            if (leave || confirm || click && MinaHangupButton.HasPoint(PhonePointer))
            {
                _mode = Mode.Home;
                _navHeld = _zHeld = _xHeld = true;
            }
            return;
        }
        DlgEnsurePages();
        _dlgLineT += delta;
        UpdateMinaPosture((float)delta);
        UpdateMinaAttention((float)delta);
        if (_minaCasual && (leave || click && MinaHangupButton.HasPoint(PhonePointer)))
        {
            if (_minaChoices.Length == 0 && _dlg[_dlgIdx].sp == "ミナ" && MinaCaptionReady && DlgLastPage) AdvanceDialogue();
            EndDialogue();
            return;
        }
        if (_minaChoices.Length > 0)
        {
            ProcessMinaChoices(confirm, click);
            return;
        }
        if (!MinaCaptionReady) return;
        _dlgReveal = DlgCurPage.Length;
        bool automatic = _autoplay || (_minaCasual || _game.AutoAdvanceDialog) && _dlg[_dlgIdx].sp != "あなた";
        if (confirm || click && (MinaCaption.HasPoint(PhonePointer) || MinaCallAction != null && MinaTalkButton.HasPoint(PhonePointer)
                || MinaStoryEnd && MinaHangupButton.HasPoint(PhonePointer)) || leave && MinaStoryEnd
            || automatic && _dlgLineT >= MinaReplyPause + Math.Max(Math.Max(1.4, DlgCurPage.Length / 15.0), MinaSpeech.End + .25))
        {
            if (DlgLastPage) AdvanceDialogue(); else DlgNextPage();
        }
    }

    private static Rect2 MinaTalkButton => new(PhoneX + PhoneW - 188, 654, 160, 44);
    private bool MinaStoryEnd => !_minaCasual && DlgLastPage && _dlgIdx == _dlg.Length - 1;
    private string? MinaCallAction => !DlgLastPage ? "続きを見る" : !MinaStoryEnd && _dlg[_dlgIdx].sp == "あなた" ? "話しかける" : null;
    private static Rect2 MinaPortraitRect => new(W / 2 - 148, 91, 296, 444);

    // Closed-form critical damping carries both pose and velocity across a change of speaker.
    private static float SettlePose(float current, float target, ref float velocity, float rate, float delta)
    {
        float offset = current - target;
        float drift = (velocity + rate * offset) * delta;
        float decay = Mathf.Exp(-rate * delta);
        velocity = (velocity - rate * drift) * decay;
        return target + (offset + drift) * decay;
    }

    private void UpdateMinaPosture(float delta)
    {
        static float ease(float start, float end, float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp((t - start) / (end - start), 0, 1));
        float time = (float)_dlgLineT;
        var (speaker, text) = _dlg[_dlgIdx];
        bool answering = speaker == "ミナ" && _minaChoices.Length == 0;
        bool greeting = answering && _dlgPage == 0 && text.StartsWith("ただいま");
        bool question = answering && text.Contains('？');
        float bow = greeting ? ease(.30f, 1.18f, time) * (1 - ease(1.65f, 3.12f, time)) : 0;
        float lean = !answering ? 2.35f : _minaBeat?.Mood == MinaMood.Concerned ? 2.0f : question ? 1.2f : -.35f;
        float breath = (1 - Mathf.Cos((float)(_minaCallTime % 5.4 / 5.4) * Mathf.Tau)) * .5f;
        float settledLean = SettlePose(_minaBody.Lean, Mathf.DegToRad(lean), ref _minaLeanVelocity, 2.3f, delta);
        float settledBow = SettlePose(_minaBody.Bow, bow, ref _minaBowVelocity, 6.4f, delta);
        // The head finishes the bow after the chest, including when a line is skipped.
        _minaBody = new(settledLean, settledBow, breath,
            Mathf.Lerp(_minaBody.Nod, settledBow, 1 - Mathf.Exp(-4.5f * delta)));
        float tilt = !answering ? 4.6f : question ? 2.3f : -.8f;
        if (answering && _minaBeat?.Mood == MinaMood.Shy && _dlgPage == 0)
            tilt += 2.8f * (1 - ease((float)MinaReplyPause, (float)MinaReplyPause + .9f, time));
        if (answering && _minaBeat?.Mood == MinaMood.Amused && _dlgPage == 0)
            tilt -= 2.2f * ease(.3f, .85f, time) * (1 - ease(1.4f, 2.5f, time));
        _minaNeckAngle = SettlePose(_minaNeckAngle, Mathf.DegToRad(tilt), ref _minaNeckVelocity, 1.9f, delta);
    }

    private void DrawMinaCall()
    {
        var canvas = _drawCanvas;
        canvas.DrawRect(new Rect2(PhoneX, 0, PhoneW, H), new Color("111e33"));
        if (_minaHistoryOpen) DrawMinaArchive();
        else if (_mode == Mode.MinaUnanswered)
        {
            DrawMinaStatus(MinaConnectionStatus);
            DrawMinaHangup();
        }
        else
        {
            DlgEnsurePages();
            if (_nightBg?.Texture is { } night)
            {
                var source = PhotoSource(night, PhotoFull, new Vector2(PhoneW, H));
                canvas.DrawTextureRectRegion(night, new Rect2(PhoneX, 0, PhoneW, H), source, new Color(.55f, .65f, .82f));
            }
            if (_dlgIdx == _minaDisconnectIndex) DrawMinaStatus(MinaNoAnswer);
            else
            {
                float appear = Mathf.SmoothStep(0, 1, (float)_minaCallTime / .45f);
                _minaPortrait!.Draw(canvas, MinaPortraitRect, _minaCallFace, _minaBody, _minaNeckAngle, _minaGaze, MinaBlink,
                    MinaCaptionReady ? MinaSpeech : default, appear);
            }

            if (_minaChoices.Length > 0)
            {
                if (_minaBeat != null)
                {
                    var recap = new Rect2(PhoneX + 24, 406, PhoneW - 48, 99);
                    UiKit.Box(canvas, recap, new Color(.045f, .075f, .14f, .96f), 14);
                    UiKit.Text(canvas, UiKit.ZenBold, recap.Position + new Vector2(14, 6), "ミナ", 15, CallLight);
                    UiKit.Multi(canvas, UiKit.Zen, recap.Position + new Vector2(14, 30), _minaBeat.Reply, 16, Colors.White, recap.Size.X - 28, 3);
                }
                DrawMinaChoices();
            }
            else
            {
                var caption = MinaCaption;
                UiKit.Box(canvas, caption, new Color(.045f, .075f, .14f, .96f), 17, new Color(CallLight, .5f), 1);
                bool listening = _minaBeat != null && !MinaCaptionReady;
                string speaker = listening ? "あなた" : _dlg[_dlgIdx].sp;
                UiKit.Text(canvas, UiKit.ZenBold, caption.Position + new Vector2(20, 12), speaker, 18,
                    speaker == "あなた" ? new Color("e9cba8") : CallLight);
                if (listening)
                    UiKit.Multi(canvas, UiKit.Zen, caption.Position + new Vector2(20, 50), _minaBeat!.Prompt, 22, Colors.White, caption.Size.X - 40, 3);
                if (MinaCaptionReady)
                {
                    Vector2 at = caption.Position + new Vector2(20, 50);
                    foreach (var line in DlgCurPage.Split('\n')) { UiKit.Text(canvas, UiKit.Zen, at, line, 22, Colors.White); at.Y += 34; }
                    if (MinaCallAction is { } action) DrawCallButton(MinaTalkButton, action);
                }
            }
            if (_minaCasual || MinaStoryEnd && MinaCaptionReady) DrawMinaHangup();
        }
        canvas.DrawRect(new Rect2(PhoneX, 0, PhoneW, 80), CallSurface);
        bool connected = _minaCall && _dlgIdx != _minaDisconnectIndex;
        canvas.DrawCircle(new Vector2(PhoneX + 33, 46), 4, connected ? new Color("8bdac6") : UiKit.Text3);
        UiKit.Text(canvas, UiKit.ZenBold, new Vector2(PhoneX + 50, 25), _minaHistoryOpen ? "通話履歴" : "ミナとの回線", 25, Colors.White);
        DrawCallButton(MinaHistoryButton, _minaHistoryOpen ? "戻る" : "履歴");
    }

    private void DrawMinaStatus(string status) => UiKit.Text(_drawCanvas, UiKit.Zen,
        new Vector2(PhoneX, H / 2 - 20), status, 24, CallLight, HorizontalAlignment.Center, PhoneW);

    private void DrawMinaHangup()
    {
        var rect = MinaHangupButton;
        bool hover = Pad.UsingMouse && rect.HasPoint(PhonePointer);
        UiKit.Box(_drawCanvas, rect, hover ? new Color("e66e79") : new Color("bf4959"), 23);
        var center = rect.GetCenter();
        _drawCanvas.DrawArc(center + new Vector2(0, 16), 21, -2.3f, -.84f, 18, Colors.White, 4, true);
        UiKit.Box(_drawCanvas, new Rect2(center + new Vector2(-17, -1), new Vector2(9, 7)), Colors.White, 3);
        UiKit.Box(_drawCanvas, new Rect2(center + new Vector2(8, -1), new Vector2(9, 7)), Colors.White, 3);
    }

    private void DrawCallButton(Rect2 rect, string label)
    {
        UiKit.Box(_drawCanvas, rect, CallSurface, 16, new Color(CallLight, .64f), 1);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, rect.Position + new Vector2(0, (rect.Size.Y - 26) / 2),
            label, 18, Colors.White, HorizontalAlignment.Center, rect.Size.X);
    }

    private void DrawMinaArchive()
    {
        if (_minaArchive.Length == 0)
            UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(PhoneX, 320), "通話履歴はまだありません", 19, CallLight, HorizontalAlignment.Center, PhoneW);
        float bottom = 105 + Mathf.Min(568, _minaArchiveHeight) + _minaArchiveScroll;
        for (int i = _minaArchive.Length - 1; i >= 0 && bottom > 80; i--)
        {
            var entry = _minaArchive[i];
            bottom -= entry.Height;
            if (bottom >= H) continue;
            Vector2 at = new(PhoneX + 37, bottom);
            UiKit.Text(_drawCanvas, UiKit.ZenBold, at, entry.Speaker, 16, CallLight);
            at.Y += 29;
            foreach (string line in entry.Lines) { UiKit.Text(_drawCanvas, UiKit.Zen, at, line, 22, Colors.White); at.Y += 34; }
        }
    }
}
