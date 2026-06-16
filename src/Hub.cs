using Godot;

// Hub : タイムラインハブ（ステージ間の中枢）。RefrainHTML のデザイン言語で非ピクセル化。
//   - ヘッダ：ミナのアカウント（アバター/名前/フォロワー/インプレ/汚染）。
//   - 角丸ガラスの投稿カード（NEW/CLEAR/LOCK）を ↑↓ で選び Z でダイブ（通常は難易度選択を挟む）。
//   - クリア帰還で少年×ミナの会話＋自動投稿。クリア済カードで C：コメント返信（1回）。
//   - 全クリアで FINAL カード。autoplay は会話を自動送り→自動ダイブ。
public partial class Hub : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

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
    private Color _toastCol = UiKit.Info;

    private bool _autoplay;
    private const double AutoDiveDelay = 1.1, AutoAdvance = 1.4;

    private static Color AccountColor(string id) => id switch
    {
        "mina" => UiKit.Mina,
        "rei" => new Color(0.90f, 0.52f, 0.38f),
        "akari" => new Color(0.40f, 0.62f, 0.88f),
        "koharu" => new Color(0.46f, 0.74f, 0.52f),
        _ => UiKit.Kegare,
    };

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
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
                Tweet = "——汚染が、限界へ。ミナ自身の内側へダイブする。", Initial = "ミ",
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
            Toast($"返信が伸びた！  Imp +{imp}  フォロワー +12", UiKit.Ok);
        }
        else
        {
            long imp = _game?.GainImpression(40) ?? 0;
            _game?.AddFollowers(8);
            Toast($"投稿が届いた  Imp +{imp}  フォロワー +8", UiKit.Ok);
        }
        if (_pendingBurn)
        {
            _pendingBurn = false;
            _game?.TriggerBurn();
            Toast("炎上中… 次のダイブはミナが弱体化します", UiKit.Burn);
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
        UiKit.BeginDesign(this);
        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color("0e1430"), new Color("0a0e22"), new Color("070a16") }, new[] { 0f, 0.55f, 1f });
        UiKit.RadialGlow(this, new Vector2(W * 0.5f, 0), 500f, new Color(120 / 255f, 150 / 255f, 210 / 255f), 0.12f);
        for (float y = 0; y < H; y += 6f) DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.05f));

        DrawHeader();

        if (_mode == Mode.Dialogue) { DrawCards(0.22f); DrawDialog(); DrawToast(); UiKit.EndDesign(this); return; }
        DrawCards(1f);
        DrawFooter();
        DrawToast();
        UiKit.EndDesign(this);
    }

    private void DrawHeader()
    {
        float padX = 40f, hy = 24f;
        UiKit.Avatar(this, new Vector2(padX + 28, hy + 28), 28f, UiKit.Mina, "ミ");
        UiKit.Text(this, UiKit.ZenBold, new Vector2(padX + 70, hy + 6), "ミナ", 22, UiKit.White);
        UiKit.Text(this, UiKit.Mono, new Vector2(padX + 70, hy + 36), "@mina_ai_", 14, UiKit.Text3);

        long fol = _game?.Followers ?? 0, imp = _game?.Impression ?? 0;
        string folS = UiKit.Abbrev(fol), impS = UiKit.Abbrev(imp);
        // インプレ（金）
        float impW = 40f + UiKit.TextW(UiKit.Mono, impS, 18);
        float impX = W - padX - impW, chipY = hy + 12f;
        UiKit.Box(this, new Rect2(impX, chipY, impW, 34f), new Color(UiKit.Gold, 0.1f), 17f, new Color(UiKit.Gold, 0.4f), 1f);
        DrawCircle(new Vector2(impX + 17, chipY + 17), 7f, UiKit.Gold);
        UiKit.Text(this, UiKit.Mono, new Vector2(impX + 30, chipY + 8), impS, 18, new Color("f0d98a"));
        // フォロワー（桃ハート）
        float folW = 40f + UiKit.TextW(UiKit.Mono, folS, 18);
        float folX = impX - 12 - folW;
        UiKit.Box(this, new Rect2(folX, chipY, folW, 34f), new Color(UiKit.Hp, 0.1f), 17f, new Color(UiKit.Hp, 0.4f), 1f);
        DrawHeart(new Vector2(folX + 18, chipY + 17), 6f, UiKit.Hp);
        UiKit.Text(this, UiKit.Mono, new Vector2(folX + 30, chipY + 8), folS, 18, new Color("f3aec6"));

        DrawRect(new Rect2(0, hy + 64, W, 1f), new Color(1, 1, 1, 0.08f));
        // 汚染バー
        float contam = Mathf.Clamp(_game?.Contamination ?? 0f, 0f, 1f);
        DrawRect(new Rect2(0, hy + 65, W, 3f), new Color(UiKit.Kegare, 0.18f));
        if (contam > 0) DrawRect(new Rect2(0, hy + 65, W * contam, 3f), UiKit.Kegare);
    }

    private (float top, float h, float gap) CardMetrics()
    {
        int n = Mathf.Max(1, _entries.Length);
        float top = 112f, bottom = 656f, gap = 14f;
        float h = Mathf.Min(150f, (bottom - top - gap * (n - 1)) / n);
        return (top, h, gap);
    }

    private void DrawCards(float alpha)
    {
        var (top, h, gap) = CardMetrics();
        for (int i = 0; i < _entries.Length; i++)
            DrawCard(_entries[i], top + i * (h + gap), h, _mode == Mode.Cards && i == _sel, alpha);
    }

    private void DrawCard(Entry e, float cy, float h, bool sel, float alpha)
    {
        float x = 40f, w = W - 80f;
        Color bg = e.Unlocked ? new Color(22 / 255f, 18 / 255f, 34 / 255f, 0.55f * alpha) : new Color(16 / 255f, 14 / 255f, 24 / 255f, 0.45f * alpha);
        Color border = sel ? new Color(UiKit.Purify, 0.85f * alpha) : new Color(1, 1, 1, 0.09f * alpha);
        UiKit.Box(this, new Rect2(x, cy, w, h), bg, 16f, border, sel ? 1.6f : 1f);

        Color acc = (e.IsFinal ? UiKit.Kegare : AccountColor(e.Id)) with { A = alpha };
        float ax = x + 36, ay = cy + 36;
        UiKit.Avatar(this, new Vector2(ax, ay), 24f, acc, e.Initial);

        float tx = x + 74, w2 = w - 110;
        Color main = new(UiKit.White, e.Unlocked ? alpha : alpha * 0.5f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(tx, cy + 16), e.Name, 19, main);
        float nameW = UiKit.TextW(UiKit.ZenBold, e.Name, 19);
        UiKit.Text(this, UiKit.Mono, new Vector2(tx + nameW + 12, cy + 22), e.Handle, 13, new Color(UiKit.Text3, alpha));

        // バッジ（右上）
        string badge = e.IsFinal ? "FINAL" : e.Cleared ? "✓ CLEAR" : e.Unlocked ? "NEW" : "LOCKED";
        Color bcol = e.IsFinal ? UiKit.Kegare : e.Cleared ? UiKit.Ok : e.Unlocked ? UiKit.Purify : UiKit.Text4;
        float bw = UiKit.TextW(UiKit.Mono, badge, 12);
        UiKit.Text(this, UiKit.Mono, new Vector2(x + w - bw - 24, cy + 18), badge, 12, new Color(bcol, alpha));

        // 本文
        string body = e.Unlocked ? e.Tweet : "ロック中 — まだダイブできません";
        UiKit.Multi(this, UiKit.Zen, new Vector2(tx, cy + 44), body, 16, new Color(232 / 255f, 224 / 255f, 240 / 255f, e.Unlocked ? alpha : alpha * 0.6f), w2, 2);

        // エンゲージメント（ロック/最終以外）
        if (e.Unlocked && !e.IsFinal && h > 110f)
        {
            float ey = cy + h - 26f, ex = tx;
            ex = Metric(ex, ey, 0, e.Replies, new Color(UiKit.Text3, alpha));
            ex = Metric(ex, ey, 1, e.Reposts, new Color(0f, 0.73f, 0.49f, alpha));
            Metric(ex, ey, 2, e.Likes, new Color(UiKit.Hp, alpha));
        }
    }

    // 小さなエンゲージメント指標（0=返信 1=リポスト 2=いいね）。次の x を返す。
    private float Metric(float x, float y, int kind, long count, Color col)
    {
        var c = new Vector2(x + 8, y + 8);
        switch (kind)
        {
            case 0: UiKit.Box(this, new Rect2(c.X - 7, c.Y - 6, 14, 10), null, 3f, col, 1.4f); break;
            case 1:
                DrawLine(new Vector2(c.X - 6, c.Y - 3), new Vector2(c.X + 5, c.Y - 3), col, 1.4f);
                DrawLine(new Vector2(c.X + 6, c.Y + 3), new Vector2(c.X - 5, c.Y + 3), col, 1.4f);
                break;
            default: DrawHeart(c, 6f, col); break;
        }
        string s = UiKit.Abbrev(count);
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 20, y), s, 13, col);
        return x + 20 + UiKit.TextW(UiKit.Mono, s, 13) + 26;
    }

    private void DrawHeart(Vector2 c, float r, Color col)
    {
        DrawCircle(new Vector2(c.X - r * 0.45f, c.Y - r * 0.25f), r * 0.55f, col);
        DrawCircle(new Vector2(c.X + r * 0.45f, c.Y - r * 0.25f), r * 0.55f, col);
        DrawColoredPolygon(new[] { new Vector2(c.X - r * 0.9f, c.Y), new Vector2(c.X + r * 0.9f, c.Y), new Vector2(c.X, c.Y + r) }, col);
    }

    private void DrawFooter()
    {
        float y = H - 40f, x = 40f;
        x = Hint(x, y, "↑↓", "えらぶ", false);
        x = Hint(x, y, "Z", "ダイブ", true);
        if (CanReplySel()) x = Hint(x, y, "C", "返信", false);
        Hint(x, y, "X", "強化", false);
    }

    private float Hint(float x, float y, string key, string label, bool accent)
    {
        Color kbg = accent ? new Color(UiKit.Purify, 0.12f) : new Color(1, 1, 1, 0.07f);
        Color kbd = accent ? new Color(UiKit.Info, 0.5f) : new Color(1, 1, 1, 0.16f);
        UiKit.Key(this, new Vector2(x, y - 12), key, kbg, kbd, accent ? UiKit.PurifyHi : UiKit.Text2);
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + kw + 8, y - 8), label, 14, accent ? UiKit.Info : UiKit.Text3);
        return x + kw + 8 + UiKit.TextW(UiKit.Zen, label, 14) + 24f;
    }

    private void DrawToast()
    {
        if (_toastT <= 0) return;
        float w = UiKit.TextW(UiKit.ZenBold, _toast, 15) + 48;
        float x = (W - w) / 2f;
        UiKit.Box(this, new Rect2(x, H - 120, w, 40f), new Color(0.06f, 0.05f, 0.10f, 0.96f), 12f, new Color(_toastCol, 0.7f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, H - 110), _toast, 15, _toastCol, HorizontalAlignment.Center, w);
    }

    private void DrawDialog()
    {
        var (sp, tx) = _dlg[Mathf.Clamp(_dlgIdx, 0, _dlg.Length - 1)];
        Color spc = sp.Contains("少年") ? UiKit.Info : sp.StartsWith("ミナ") ? UiKit.Mina : AccountColor("rei");
        var box = new Rect2(40, 470, W - 80, 200);
        UiKit.Box(this, box, new Color(0.05f, 0.04f, 0.09f, 0.96f), 16f, new Color(spc, 0.5f), 1.4f);
        UiKit.Avatar(this, new Vector2(box.Position.X + 44, box.Position.Y + 44), 26f, spc, sp.Length > 0 ? sp.Substring(0, 1) : "?");
        UiKit.Text(this, UiKit.ZenBold, new Vector2(box.Position.X + 84, box.Position.Y + 24), sp, 18, spc);
        UiKit.Multi(this, UiKit.Zen, new Vector2(box.Position.X + 36, box.Position.Y + 76), tx, 21, new Color(0.95f, 0.95f, 0.98f), box.Size.X - 72, 3);
        if (!_autoplay)
        {
            float blink = 0.5f + 0.5f * Mathf.Sin((float)_t * 4f);
            UiKit.Text(this, UiKit.Zen, new Vector2(box.Position.X + box.Size.X - 150, box.Position.Y + box.Size.Y - 36),
                "Z すすむ ▸", 14, new Color(UiKit.Info, blink));
        }
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
