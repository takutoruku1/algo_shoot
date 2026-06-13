using Godot;

// Hub : タイムラインハブ（ステージ間の中枢）。ダークモードX風UI。
//   - ヘッダ：ミナのアカウント（アバター/名前/フォロワー/インプレ/汚染）。
//   - 角丸の投稿カード（NEW/CLEAR/LOCK）を ↑↓ で選び Z でダイブ（通常は難易度選択を挟む）。
//   - クリア帰還で少年×ミナの会話＋自動投稿。クリア済カードで C：コメント返信（1回）。
//   - 全クリアで FINAL カード。autoplay は会話を自動送り→自動ダイブ。
//   設計: docs/20260613/MINA_システム拡張設計書_v1.md ④-3 / ③
public partial class Hub : Node2D
{
    private FontFile _font = null!;
    private GameManager _game = null!;
    private const float W = 384f, H = 216f;

    private struct Entry
    {
        public bool IsFinal;
        public string Id, Scene, Name, Handle, Tweet, Initial;
        public bool Unlocked, Cleared;
        public long Likes, Reposts, Replies;
    }
    private Entry[] _entries = System.Array.Empty<Entry>();

    private int _sel;
    private bool _navHeld, _zHeld, _xHeld, _cHeld, _dived;
    private double _t, _cardsEnteredT;

    private enum Mode { Cards, Dialogue }
    private Mode _mode = Mode.Cards;
    private (string sp, string tx)[] _dlg = System.Array.Empty<(string, string)>();
    private int _dlgIdx;
    private double _dlgLineT;
    private string? _dlgReplyId;
    private bool _pendingBurn;

    private double _toastT;
    private string _toast = "";
    private Color _toastCol = Ui.Ok;

    private bool _autoplay;
    private const double AutoDiveDelay = 1.1, AutoAdvance = 1.4;

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        _font = ResourceLoader.Load<FontFile>("res://assets/fonts/PixelMplus12-Regular.ttf");
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }

        BuildEntries();
        _sel = DefaultSelection();

        string? cleared = _game?.JustClearedStageId;
        if (cleared != null)
        {
            _game!.JustClearedStageId = null;
            var lines = ReturnDialog(cleared);
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
        var counts = new System.Collections.Generic.Dictionary<string, (long, long, long)>
        {
            ["rei"] = (12, 3, 48), ["akari"] = (34, 9, 210), ["koharu"] = (58, 21, 402),
        };
        foreach (var s in GameManager.Stages)
        {
            bool cleared = _game?.IsStageCleared(s.Id) ?? false;
            bool unlocked = _game?.IsStageUnlocked(s.Id) ?? true;
            string name = s.Title.Contains("—") ? s.Title.Split('—')[^1].Trim() : s.Title;
            var (rep, rt, lk) = counts.TryGetValue(s.Id, out var c) ? c : (0L, 0L, 0L);
            list.Add(new Entry
            {
                IsFinal = false, Id = s.Id, Scene = s.Scene, Name = name, Handle = s.Handle,
                Tweet = s.Tweet, Initial = name.Length > 0 ? name.Substring(0, 1) : "?",
                Unlocked = unlocked, Cleared = cleared,
                Replies = rep, Reposts = rt, Likes = lk,
            });
        }
        if (_game?.AllStoryCleared ?? false)
        {
            list.Add(new Entry
            {
                IsFinal = true, Id = "final", Scene = "res://Final.tscn", Name = "ミナ", Handle = "@mina_ai_",
                Tweet = "——汚染が、限界へ。ミナ自身の内側へダイブする。", Initial = "!",
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

    public override void _Process(double delta)
    {
        _t += delta;
        if (_toastT > 0) _toastT -= delta;
        if (_dived) { QueueRedraw(); return; }
        if (_mode == Mode.Dialogue) { ProcessDialogue(delta); QueueRedraw(); return; }
        ProcessCards();
        QueueRedraw();
    }

    // ───────── 会話 ─────────
    private void StartDialogue((string, string)[] lines, string? replyId)
    {
        _mode = Mode.Dialogue;
        _dlg = lines; _dlgIdx = 0; _dlgLineT = 0; _dlgReplyId = replyId;
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
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.15) AdvanceDialogue();
    }

    private void AdvanceDialogue()
    {
        _dlgIdx++;
        if (_dlgIdx >= _dlg.Length) EndDialogue();
    }

    private void EndDialogue()
    {
        if (_dlgReplyId != null)
        {
            long imp = _game?.GainImpression(60) ?? 0;
            _game?.AddFollowers(12);
            _game?.MarkReplied(_dlgReplyId);
            BuildEntries();
            Toast($"返信が伸びた！  Imp +{imp}  フォロワー +12", Ui.Ok);
        }
        else
        {
            long imp = _game?.GainImpression(40) ?? 0;
            _game?.AddFollowers(8);
            Toast($"投稿が届いた  Imp +{imp}  フォロワー +8", Ui.Ok);
        }
        if (_pendingBurn)
        {
            _pendingBurn = false;
            _game?.TriggerBurn();
            Toast("炎上中… 次のダイブはミナが弱体化します", Ui.Burn);
        }
        _game?.Save();
        _mode = Mode.Cards;
        _cardsEnteredT = _t;
    }

    private void Toast(string msg, Color col) { _toast = msg; _toastCol = col; _toastT = 2.6; }

    // ───────── カード ─────────
    private bool CanReplySel() => !_autoplay && _sel >= 0 && _sel < _entries.Length
        && _entries[_sel].Cleared && !(_game?.HasReplied(_entries[_sel].Id) ?? true);

    private void ProcessCards()
    {
        if (_autoplay) { if (_t - _cardsEnteredT >= AutoDiveDelay) DiveAuto(); return; }

        if (Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start)) { GetTree().ReloadCurrentScene(); return; }

        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld && _entries.Length > 0)
        {
            if (up) _sel = (_sel - 1 + _entries.Length) % _entries.Length;
            if (down) _sel = (_sel + 1) % _entries.Length;
        }
        _navHeld = up || down;

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.3 && _sel >= 0 && _sel < _entries.Length)
        {
            var e = _entries[_sel];
            if (e.Unlocked)
            {
                if (e.IsFinal) Dive(e.Scene);
                else { if (_game != null) _game.PendingStageScene = e.Scene; Dive("res://DiffSelect.tscn"); }
            }
        }

        bool c = Input.IsKeyPressed(Key.C) || Pad.Pressed(JoyButton.Y);
        bool cEdge = c && !_cHeld; _cHeld = c;
        if (cEdge && CanReplySel())
        {
            var lines = ReplyDialog(_entries[_sel].Id);
            if (lines.Length > 0) StartDialogue(lines, _entries[_sel].Id);
        }

        bool x = Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.X);
        bool xEdge = x && !_xHeld; _xHeld = x;
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

    // ───────── 描画 ─────────
    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, W, H), Ui.Bg);
        DrawHeader();

        if (_mode == Mode.Dialogue) { DrawCardsDim(); DrawDialog(); DrawToast(); return; }
        DrawCards();
        DrawFooter();
        DrawToast();
    }

    private void DrawHeader()
    {
        DrawRect(new Rect2(0, 0, W, 28), Ui.HeaderBg);
        Ui.Avatar(this, _font, new Vector2(15, 14), 8f, Ui.AccountColor("mina"), "ミ");
        Ui.Text(this, _font, new Vector2(28, 3), "ミナ", 11, Ui.TextMain);
        Ui.Text(this, _font, new Vector2(28, 16), "@mina_ai_", 9, Ui.TextSub);

        // 右側：フォロワー＆インプレのチップ
        long fol = _game?.Followers ?? 0, imp = _game?.Impression ?? 0;
        string folS = Ui.Abbrev(fol), impS = Ui.Abbrev(imp);
        float impW = 12f + Ui.TextW(_font, impS, 9);
        float folW = 12f + Ui.TextW(_font, folS, 9);
        float impX = W - 8 - impW, folX = impX - 8 - folW;
        Ui.IconLike(this, new Vector2(folX + 3, 12), Ui.Like);
        Ui.Text(this, _font, new Vector2(folX + 9, 7), folS, 9, Ui.TextMain);
        DrawCircle(new Vector2(impX + 3, 12), 2.6f, new Color(0.98f, 0.78f, 0.30f)); // インプレ＝金コイン
        Ui.Text(this, _font, new Vector2(impX + 9, 7), impS, 9, Ui.TextMain);

        DrawRect(new Rect2(0, 27, W, 1), Ui.Divider);
        // 汚染バー
        float contam = Mathf.Clamp(_game?.Contamination ?? 0f, 0f, 1f);
        DrawRect(new Rect2(0, 28, W, 1.5f), new Color(Ui.Contam, 0.25f));
        DrawRect(new Rect2(0, 28, W * contam, 1.5f), Ui.Contam);
    }

    private (float top, float h, float gap) CardMetrics()
    {
        int n = Mathf.Max(1, _entries.Length);
        float top = 34f, bottom = 198f, gap = 5f;
        float h = Mathf.Min(46f, (bottom - top - gap * (n - 1)) / n);
        return (top, h, gap);
    }

    private void DrawCards()
    {
        var (top, h, gap) = CardMetrics();
        for (int i = 0; i < _entries.Length; i++)
            DrawCard(_entries[i], top + i * (h + gap), h, i == _sel, 1f);
    }

    private void DrawCardsDim()
    {
        var (top, h, gap) = CardMetrics();
        for (int i = 0; i < _entries.Length; i++)
            DrawCard(_entries[i], top + i * (h + gap), h, false, 0.25f);
    }

    private void DrawCard(Entry e, float cy, float h, bool sel, float alpha)
    {
        float x = 8f, w = W - 16f;
        Color bg = e.Cleared ? Ui.Card : (e.Unlocked ? Ui.Card : Ui.CardLocked);
        if (sel) bg = Ui.CardSel;
        Color border = sel ? Ui.Blue : Ui.Border;
        Ui.Box(this, new Rect2(x, cy, w, h), new Color(bg, alpha), 6f, new Color(border, alpha), sel ? 1.4f : 0.8f);
        if (sel) DrawRect(new Rect2(x, cy + 4, 2.5f, h - 8), new Color(Ui.Blue, alpha)); // 左アクセント

        Color acc = e.IsFinal ? Ui.Contam : Ui.AccountColor(e.Id);
        float ax = x + 16, ay = cy + 15;
        Ui.Avatar(this, _font, new Vector2(ax, ay), 7.5f, new Color(acc, alpha), e.Initial);

        float tx = x + 30, w2 = w - 38;
        Color main = new(Ui.TextMain, e.Unlocked ? alpha : alpha * 0.5f);
        Color sub = new(Ui.TextSub, alpha);
        // 名前
        Ui.Text(this, _font, new Vector2(tx, cy + 4), e.Name, 10, main);
        float nameW = Ui.TextW(_font, e.Name, 10);
        Ui.Text(this, _font, new Vector2(tx + nameW + 5, cy + 5), e.Handle, 8, sub);
        // バッジ
        string badge = e.IsFinal ? "FINAL" : e.Cleared ? "✓ CLEAR" : e.Unlocked ? "NEW" : "LOCKED";
        Color bcol = e.IsFinal ? Ui.Contam : e.Cleared ? Ui.Repost : e.Unlocked ? Ui.Blue : Ui.TextMuted;
        float bw = Ui.TextW(_font, badge, 8);
        Ui.Text(this, _font, new Vector2(x + w - bw - 8, cy + 5), badge, 8, new Color(bcol, alpha));

        // 本文
        string body = e.Unlocked ? e.Tweet : "ロック中 — まだダイブできません";
        Ui.MultiText(this, _font, new Vector2(tx, cy + 16), body, 9, new Color(main, e.Unlocked ? alpha : alpha * 0.6f), w2, 2);

        // エンゲージメント（ロック/最終以外）
        if (e.Unlocked && !e.IsFinal)
        {
            float ey = cy + h - 11;
            float ex = tx;
            ex = Ui.Engagement(this, _font, ex, ey, 0, e.Replies, new Color(Ui.TextMuted, alpha));
            ex = Ui.Engagement(this, _font, ex, ey, 1, e.Reposts, new Color(Ui.Repost, alpha));
            Ui.Engagement(this, _font, ex, ey, 2, e.Likes, new Color(Ui.Like, alpha));
        }
    }

    private void DrawFooter()
    {
        string hint = CanReplySel()
            ? "↑↓ えらぶ   Z ダイブ   C 返信   X 強化"
            : "↑↓ えらぶ   Z ダイブ   X 強化";
        Ui.Text(this, _font, new Vector2(8, 202), hint, 9, Ui.TextMuted);
    }

    private void DrawToast()
    {
        if (_toastT <= 0) return;
        float w = Ui.TextW(_font, _toast, 9) + 16;
        float x = (W - w) / 2;
        Ui.Box(this, new Rect2(x, 186, w, 14), new Color(0.10f, 0.12f, 0.15f, 0.96f), 7f, new Color(_toastCol, 0.8f), 1f);
        Ui.Text(this, _font, new Vector2(x + 8, 188), _toast, 9, _toastCol);
    }

    private void DrawDialog()
    {
        var (sp, tx) = _dlg[Mathf.Clamp(_dlgIdx, 0, _dlg.Length - 1)];
        Color spc = sp.Contains("少年") ? Ui.Blue : sp.StartsWith("ミナ") ? Ui.Mina : Ui.AccountColor("rei");
        var box = new Rect2(8, 120, W - 16, 84);
        Ui.Box(this, box, new Color(0.11f, 0.13f, 0.16f, 0.98f), 8f, new Color(spc, 0.55f), 1.2f);
        // 話者アバター
        Ui.Avatar(this, _font, new Vector2(22, 134), 8f, spc, sp.Length > 0 ? sp.Substring(0, 1) : "?");
        Ui.Text(this, _font, new Vector2(36, 126), sp, 10, spc);
        Ui.MultiText(this, _font, new Vector2(16, 146), tx, 11, Ui.TextMain, W - 32, 3);
        Ui.Text(this, _font, new Vector2(W - 70, 190), _autoplay ? "" : "Z すすむ ▸", 9, Ui.TextMuted);
    }

    // ───────── 会話データ（③-2 / ③-3 / ③-6）─────────
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
