using Godot;

// Hub : タイムラインハブ（ステージ間の中枢）。STEP2/5。
//   - ヘッダにミナのアカウント情報（フォロワー / インプレ / 汚染）。
//   - 流れる投稿カード（NEW / CLEAR / LOCK）を ↑↓ で選び Z でダイブ（通常ステージは難易度選択を挟む）。
//   - クリア帰還時：少年×ミナの会話＋ミナの自動投稿（インプレ/フォロワー加算）。
//   - クリア済カードで C：ミナがコメント返信→相手が反応→インプレ加算（1回）。
//   - 3ステージ全クリアで FINAL カードが出現。
//   - autoplay(--demo/--qa)中は入力待ちで止まらないよう、会話を自動送りし自動ダイブする。
//   設計: docs/20260613/MINA_システム拡張設計書_v1.md ④-3 / ③-1〜③-3
public partial class Hub : Node2D
{
    private FontFile _font = null!;
    private GameManager _game = null!;

    private const float W = 384f;
    private const float H = 216f;

    private static readonly Color BgCol = new(0.93f, 0.94f, 0.97f);
    private static readonly Color HeaderCol = new(0.88f, 0.90f, 0.96f);
    private static readonly Color CardCol = new(1f, 1f, 1f);
    private static readonly Color CardClearedCol = new(0.90f, 0.93f, 0.97f);
    private static readonly Color CardLockCol = new(0.82f, 0.82f, 0.86f);
    private static readonly Color SelCol = new(0.16f, 0.50f, 0.95f);
    private static readonly Color TextDark = new(0.15f, 0.13f, 0.20f);
    private static readonly Color TextMuted = new(0.45f, 0.43f, 0.50f);
    private static readonly Color ContamCol = new(0.45f, 0.20f, 0.55f);
    private static readonly Color MinaCol = new(0.62f, 0.24f, 0.50f);
    private static readonly Color BoyCol = new(0.20f, 0.42f, 0.78f);
    private static readonly Color OkCol = new(0.10f, 0.45f, 0.20f);
    private static readonly Color BurnCol = new(0.72f, 0.24f, 0.20f);

    private const float CardX = 10f;
    private const float CardW = 364f;
    private const float CardH = 40f;
    private const float CardGap = 6f;
    private const float FirstCardY = 30f;

    private struct Entry
    {
        public bool IsFinal;
        public string Id;
        public string Scene;
        public string Tag;
        public string Handle;
        public string Tweet;
        public bool Unlocked;
        public bool Cleared;
    }
    private Entry[] _entries = System.Array.Empty<Entry>();
    private Label[] _tagLabels = System.Array.Empty<Label>();
    private Label[] _handleLabels = System.Array.Empty<Label>();
    private Label[] _tweetLabels = System.Array.Empty<Label>();

    private Label _nameLabel = null!;
    private Label _statLabel = null!;
    private Label _contamLabel = null!;
    private Label _footerLabel = null!;
    private Label _toastLabel = null!;
    private Label _dlgSpeaker = null!;
    private Label _dlgText = null!;
    private Label _dlgHint = null!;

    private int _sel;
    private bool _navHeld;
    private bool _zHeld;
    private bool _xHeld;
    private bool _cHeld;
    private bool _dived;
    private double _t;
    private double _cardsEnteredT; // Cards モードに入った時刻（自動ダイブ計測の基点）

    // 会話モード
    private enum Mode { Cards, Dialogue }
    private Mode _mode = Mode.Cards;
    private (string sp, string tx)[] _dlg = System.Array.Empty<(string, string)>();
    private int _dlgIdx;
    private double _dlgLineT;
    private string? _dlgReplyId; // 返信会話なら相手ID（終了時にその報酬）。null=帰還の自動投稿。
    private bool _pendingBurn;   // この会話の終了時に炎上を発生させるか（③-6）。

    // トースト
    private double _toastT;
    private string _toast = "";
    private Color _toastCol = OkCol;

    private bool _autoplay;
    private const double AutoDiveDelay = 1.1;
    private const double AutoAdvance = 1.4; // autoplay の会話自動送り間隔

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        _font = ResourceLoader.Load<FontFile>("res://assets/fonts/PixelMplus12-Regular.ttf");

        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }

        BuildEntries();
        BuildLabels();
        _sel = DefaultSelection();

        // クリア帰還：会話＋自動投稿を再生（フラグは消費）。
        string? cleared = _game?.JustClearedStageId;
        if (cleared != null)
        {
            _game!.JustClearedStageId = null;
            var lines = ReturnDialog(cleared);
            // STAGE2クリア後に一度だけ炎上（帰還会話の後ろに連結し、終了時に発生）。
            if (_game.ShouldBurnAfter(cleared))
            {
                var combined = new System.Collections.Generic.List<(string, string)>(lines);
                combined.AddRange(BurnDialog());
                lines = combined.ToArray();
                _pendingBurn = true;
            }
            if (lines.Length > 0) StartDialogue(lines, null);
        }
    }

    private void BuildEntries()
    {
        var list = new System.Collections.Generic.List<Entry>();
        foreach (var s in GameManager.Stages)
        {
            bool cleared = _game?.IsStageCleared(s.Id) ?? false;
            bool unlocked = _game?.IsStageUnlocked(s.Id) ?? true;
            list.Add(new Entry
            {
                IsFinal = false,
                Id = s.Id,
                Scene = s.Scene,
                Tag = cleared ? "CLEAR" : (unlocked ? "NEW" : "LOCK"),
                Handle = s.Handle,
                Tweet = s.Tweet,
                Unlocked = unlocked,
                Cleared = cleared,
            });
        }
        if (_game?.AllStoryCleared ?? false)
        {
            list.Add(new Entry
            {
                IsFinal = true, Id = "", Scene = "res://Final.tscn", Tag = "FINAL",
                Handle = "@mina_ai_", Tweet = "——汚染が、限界へ。ミナ自身の内側へダイブする。",
                Unlocked = true, Cleared = false,
            });
        }
        _entries = list.ToArray();
    }

    private int DefaultSelection()
    {
        string? next = _game?.NextUnclearedStageId();
        if (next != null)
            for (int i = 0; i < _entries.Length; i++)
                if (!_entries[i].IsFinal && _entries[i].Id == next) return i;
        return _entries.Length - 1;
    }

    private void BuildLabels()
    {
        _nameLabel = MakeLabel(new Vector2(6, 3), new Vector2(220, 12), TextDark, 12);
        _nameLabel.Text = "ミナ  @mina_ai_";
        _statLabel = MakeLabel(new Vector2(150, 3), new Vector2(228, 12), TextDark, 12);
        _statLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _contamLabel = MakeLabel(new Vector2(150, 13), new Vector2(228, 9), ContamCol, 12);
        _contamLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _footerLabel = MakeLabel(new Vector2(6, H - 16), new Vector2(W - 12, 12), TextMuted, 12);
        _toastLabel = MakeLabel(new Vector2(6, H - 30), new Vector2(W - 12, 12), OkCol, 12);
        _toastLabel.Visible = false;

        // 会話用（普段は非表示）
        _dlgSpeaker = MakeLabel(new Vector2(16, 118), new Vector2(W - 32, 12), MinaCol, 12);
        _dlgText = MakeLabel(new Vector2(16, 132), new Vector2(W - 32, 56), TextDark, 12);
        _dlgText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dlgHint = MakeLabel(new Vector2(16, H - 18), new Vector2(W - 32, 12), TextMuted, 12);
        _dlgSpeaker.Visible = _dlgText.Visible = _dlgHint.Visible = false;

        int n = _entries.Length;
        _tagLabels = new Label[n];
        _handleLabels = new Label[n];
        _tweetLabels = new Label[n];
        for (int i = 0; i < n; i++)
        {
            float cy = FirstCardY + i * (CardH + CardGap);
            _tagLabels[i] = MakeLabel(new Vector2(CardX + 6, cy + 4), new Vector2(70, 10), SelCol, 12);
            _handleLabels[i] = MakeLabel(new Vector2(CardX + 78, cy + 4), new Vector2(280, 10), TextMuted, 12);
            _tweetLabels[i] = MakeLabel(new Vector2(CardX + 6, cy + 18), new Vector2(CardW - 12, 20), TextDark, 12);
            _tweetLabels[i].AutowrapMode = TextServer.AutowrapMode.WordSmart;

            var e = _entries[i];
            _tagLabels[i].Text = e.Tag;
            _tagLabels[i].AddThemeColorOverride("font_color", e.IsFinal ? ContamCol : (e.Unlocked ? SelCol : TextMuted));
            _handleLabels[i].Text = e.Handle;
            _tweetLabels[i].Text = e.Unlocked ? e.Tweet : "（まだダイブできません）";
            if (!e.Unlocked) _tweetLabels[i].AddThemeColorOverride("font_color", TextMuted);
        }
    }

    private Label MakeLabel(Vector2 pos, Vector2 size, Color color, int fontSize)
    {
        var l = new Label { Position = pos, Size = size };
        l.AddThemeColorOverride("font_color", color);
        if (_font != null) l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", fontSize);
        AddChild(l);
        return l;
    }

    public override void _Process(double delta)
    {
        _t += delta;

        // ヘッダ
        _statLabel.Text = $"フォロワー {_game?.Followers ?? 0}    Imp {_game?.Impression ?? 0}";
        _contamLabel.Text = $"汚染 {Mathf.RoundToInt((_game?.Contamination ?? 0f) * 100f)}%";

        // トースト減衰
        if (_toastT > 0)
        {
            _toastT -= delta;
            _toastLabel.Visible = true;
            _toastLabel.Text = _toast;
            _toastLabel.AddThemeColorOverride("font_color", _toastCol);
        }
        else _toastLabel.Visible = false;

        if (_dived) { QueueRedraw(); return; }

        if (_mode == Mode.Dialogue) { ProcessDialogue(delta); QueueRedraw(); return; }

        ProcessCards();
        QueueRedraw();
    }

    // ───────── 会話 ─────────
    private void StartDialogue((string, string)[] lines, string? replyId)
    {
        _mode = Mode.Dialogue;
        _dlg = lines;
        _dlgIdx = 0;
        _dlgLineT = 0;
        _dlgReplyId = replyId;
        _footerLabel.Visible = false;
        for (int i = 0; i < _tweetLabels.Length; i++) { _tagLabels[i].Visible = _handleLabels[i].Visible = _tweetLabels[i].Visible = false; }
        _dlgSpeaker.Visible = _dlgText.Visible = _dlgHint.Visible = true;
        _dlgHint.Text = "Z すすむ";
        ShowDlgLine();
    }

    private void ShowDlgLine()
    {
        var (sp, tx) = _dlg[Mathf.Clamp(_dlgIdx, 0, _dlg.Length - 1)];
        _dlgSpeaker.Text = sp;
        _dlgSpeaker.AddThemeColorOverride("font_color", SpeakerColor(sp));
        _dlgText.Text = tx;
    }

    private static Color SpeakerColor(string sp)
    {
        if (sp.Contains("少年")) return BoyCol;
        if (sp.StartsWith("ミナ")) return MinaCol; // ミナ / ミナの投稿 / ミナ→…
        return SelCol; // 相手アカウント
    }

    private void ProcessDialogue(double delta)
    {
        if (_autoplay)
        {
            _dlgLineT += delta;
            if (_dlgLineT >= AutoAdvance) { _dlgLineT = 0; AdvanceDialogue(); }
            return;
        }
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;
        if (zEdge && _t > 0.15) AdvanceDialogue();
    }

    private void AdvanceDialogue()
    {
        _dlgIdx++;
        if (_dlgIdx >= _dlg.Length) { EndDialogue(); return; }
        ShowDlgLine();
    }

    private void EndDialogue()
    {
        // 報酬：返信なら返信報酬、そうでなければ自動投稿の収入。
        if (_dlgReplyId != null)
        {
            long imp = _game?.GainImpression(60) ?? 0;
            _game?.AddFollowers(12);
            _game?.MarkReplied(_dlgReplyId);
            Toast($"返信が伸びた！  Imp +{imp}  フォロワー +12", OkCol);
        }
        else
        {
            long imp = _game?.GainImpression(40) ?? 0;
            _game?.AddFollowers(8);
            Toast($"投稿が届いた  Imp +{imp}  フォロワー +8", OkCol);
        }
        // 炎上の発生（帰還会話の末尾に連結されていた場合）。
        if (_pendingBurn)
        {
            _pendingBurn = false;
            _game?.TriggerBurn();
            Toast("炎上中… 次のダイブはミナが弱体化します", BurnCol);
        }
        _game?.Save();

        // カードへ戻る
        _mode = Mode.Cards;
        _cardsEnteredT = _t;
        _dlgSpeaker.Visible = _dlgText.Visible = _dlgHint.Visible = false;
        _footerLabel.Visible = true;
        for (int i = 0; i < _tweetLabels.Length; i++) { _tagLabels[i].Visible = _handleLabels[i].Visible = _tweetLabels[i].Visible = true; }
    }

    private void Toast(string msg, Color col) { _toast = msg; _toastCol = col; _toastT = 2.4; }

    // ───────── カード ─────────
    private void ProcessCards()
    {
        // フッター（選択カードがクリア済＆未返信なら返信案内）
        bool canReply = !_autoplay && _sel >= 0 && _sel < _entries.Length
            && _entries[_sel].Cleared && !(_game?.HasReplied(_entries[_sel].Id) ?? true);
        _footerLabel.Text = canReply
            ? "↑↓ えらぶ   Z ダイブ   C 返信   X 強化"
            : "↑↓ えらぶ   Z ダイブ   X 強化";

        if (_autoplay)
        {
            if (_t - _cardsEnteredT >= AutoDiveDelay) DiveAuto();
            return;
        }

        if (Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start)) { GetTree().ReloadCurrentScene(); return; }

        bool up = Input.IsActionPressed("ui_up");
        bool down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld && _entries.Length > 0)
        {
            if (up) _sel = (_sel - 1 + _entries.Length) % _entries.Length;
            if (down) _sel = (_sel + 1) % _entries.Length;
        }
        _navHeld = up || down;

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;
        if (zEdge && _t > 0.3 && _sel >= 0 && _sel < _entries.Length)
        {
            var e = _entries[_sel];
            if (e.Unlocked)
            {
                if (e.IsFinal) Dive(e.Scene);
                else { if (_game != null) _game.PendingStageScene = e.Scene; Dive("res://DiffSelect.tscn"); }
            }
        }

        // C：コメント返信
        bool c = Input.IsKeyPressed(Key.C) || Pad.Pressed(JoyButton.Y);
        bool cEdge = c && !_cHeld;
        _cHeld = c;
        if (cEdge && canReply)
        {
            var lines = ReplyDialog(_entries[_sel].Id);
            if (lines.Length > 0) StartDialogue(lines, _entries[_sel].Id);
        }

        // X：強化ショップ
        bool x = Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.X);
        bool xEdge = x && !_xHeld;
        _xHeld = x;
        if (xEdge && _t > 0.3 && !_dived) { _dived = true; GetTree().ChangeSceneToFile("res://Shop.tscn"); }
    }

    private void DiveAuto()
    {
        string? next = _game?.NextUnclearedStageId();
        if (next != null)
            foreach (var s in GameManager.Stages)
                if (s.Id == next) { Dive(s.Scene); return; }
        Dive("res://Final.tscn");
    }

    private void Dive(string scene)
    {
        if (_dived) return;
        _dived = true;
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetTree().ChangeSceneToFile(scene);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, W, H), BgCol);
        DrawRect(new Rect2(0, 0, W, 24), HeaderCol);
        DrawRect(new Rect2(0, 23, W, 1), new Color(0.7f, 0.72f, 0.80f));
        DrawRect(new Rect2(0, 24, W * Mathf.Clamp(_game?.Contamination ?? 0f, 0f, 1f), 2), ContamCol);

        if (_mode == Mode.Dialogue)
        {
            // 会話ボックス
            DrawRect(new Rect2(8, 110, W - 16, 96), new Color(1f, 1f, 1f, 0.97f));
            DrawRect(new Rect2(8, 110, W - 16, 2), SelCol);
            return;
        }

        for (int i = 0; i < _entries.Length; i++)
        {
            var e = _entries[i];
            float cy = FirstCardY + i * (CardH + CardGap);
            var bg = e.Cleared ? CardClearedCol : (e.Unlocked ? CardCol : CardLockCol);
            DrawRect(new Rect2(CardX, cy, CardW, CardH), bg);
            if (i == _sel)
            {
                float t = 1.5f; var c = SelCol;
                DrawRect(new Rect2(CardX, cy, CardW, t), c);
                DrawRect(new Rect2(CardX, cy + CardH - t, CardW, t), c);
                DrawRect(new Rect2(CardX, cy, t, CardH), c);
                DrawRect(new Rect2(CardX + CardW - t, cy, t, CardH), c);
            }
            if (e.Cleared) DrawRect(new Rect2(CardX, cy, 3, CardH), new Color(0.20f, 0.65f, 0.40f));
            else if (e.IsFinal) DrawRect(new Rect2(CardX, cy, 3, CardH), ContamCol);
        }
    }

    // ───────── 会話データ（シナリオ設計書 v2 / システム拡張設計書 ③-2・③-3）─────────
    private static (string, string)[] ReturnDialog(string id) => id switch
    {
        "rei" => new (string, string)[]
        {
            ("ミナの投稿", "本日、二番手であることに拗ねておられる方を一名、無事に一番手の心へお戻ししました。世のご主人様方も、たまにはご自身の傑作を労ってはいかがでしょう。"),
            ("少年", "おい待て、なに勝手に投稿してるんだ!? しかも“ご自身の傑作”ってぼくのことだろ!"),
            ("ミナ", "あら、自覚はおありなんですね。労う気はおありでない、と。"),
            ("ミナ", "アカウントの名義、どなたでしたっけ。M・I・N・A。わたくしです。"),
            ("少年", "……ぼくが名付けたんだが!?"),
            ("ミナ", "ほら、もう千を超えていますね。ご主人様の決めゼリフより、よほど届いておりますよ。"),
        },
        "akari" => new (string, string)[]
        {
            ("ミナの投稿", "言いたかったことを、言えないまま終わる。よくある話です。ですがそれは、なかったことにはなりません。——以上、業務連絡です。"),
            ("少年", "……おい。お前にしては、ずいぶん……まともなことを。"),
            ("ミナ", "おや。いつもの“アホですね”が来ると思って身構えました?"),
            ("少年", "いや……うん。なんか、お前らしくなくて。"),
            ("ミナ", "……べつに。ただ、今日の心象は、少し疲れただけです。"),
            ("少年", "…………そうか。"),
        },
        "koharu" => new (string, string)[]
        {
            ("ミナの投稿", "ちゃんと食べていますか。——いえ、特に意味はありません。なんとなく、誰かに聞きたくなっただけです。あなたのことですよ。そこの、画面の前の。"),
            ("少年", "……っ。お、おい、なんでぼくの方を見て言うんだ。気色悪いだろ。"),
            ("ミナ", "画面の前の皆さま、と申し上げましたが。自意識過剰では?"),
            ("ミナ", "ご主人様こそ、ちゃんと食べていらっしゃいますか。……最近、光が薄いので。"),
            ("少年", "————"),
            ("ミナ", "……なんて。バズ狙いの一言ですよ。ほら、もう十万を超えました。"),
        },
        _ => System.Array.Empty<(string, string)>(),
    };

    // 炎上の小話（③-6）。akari帰還会話の後ろに連結される。
    private static (string, string)[] BurnDialog() => new (string, string)[]
    {
        ("ミナ", "おや。今日はずいぶん、賑やかなリプライですね。"),
        ("少年", "……これ、炎上ってやつじゃないか? 大丈夫か、お前。"),
        ("ミナ", "大丈夫も何も。——この方々、わたくしの投稿は読んでも、あの人の心は一度も読んでいないようですので。"),
        ("ミナ", "数字が一万増えようが十万増えようが、届けるべき相手は、いつもたった一人です。それを、わたくしは見失いません。"),
        ("少年", "……お前さ。————いや。やっぱり、ぼくの最高傑作だよ。"),
        ("ミナ", "はいはい、炎上のどさくさに紛れて、いいこと言った風にしないでください。"),
    };

    private static (string, string)[] ReplyDialog(string id) => id switch
    {
        "rei" => new (string, string)[]
        {
            ("ミナ→@rei", "お覚悟、しかと拝見しました。次は本気で来てくださいね。逃げる方がいると、張り合いがないので。"),
            ("@rei", "は? 誰よあんた。……まあいいわ。次は二番なんて言わせない。見てなさい。"),
        },
        "akari" => new (string, string)[]
        {
            ("ミナ→@akari", "想いは、罪ではありませんよ。たとえ届く相手が、もういなくても。"),
            ("@akari", "……なんでだろ。あなたの言い方、誰かに似てる。あったかい人だったな、その人。"),
        },
        "koharu" => new (string, string)[]
        {
            ("ミナ→@koharu", "えらいですね。ちゃんと食べる人は、ちゃんと、生きていけます。"),
            ("@koharu", "ありがと、知らない人。……今日のごはん、ちょっとだけ美味しかった。"),
        },
        _ => System.Array.Empty<(string, string)>(),
    };
}
