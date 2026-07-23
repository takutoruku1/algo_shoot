using Godot;

// ShopTutorial : 「初めて中ボスを倒した」直後に一度きり出る、強化ショップの説明パート。
//   ・全画面の会話オーバーレイで ShopTutorialLines を順に流す（Hub の会話描画作法を踏襲）。
//   ・流し切ったら実際の Shop 画面へ遷移（ミナの説明→本物のショップを見せる導線）。
//     Shop は X でハブへ戻るので、結果として「中ボス撃破→説明→ショップ→ハブ」の流れになる。
//   ・離脱判定（一度きり/初回のみ）と ShopTutorialSeen の確定は CheckpointFlow 側で済ませてある。
//
//   タプルは (int who, string text, string face)。who は Hud.LineKind 準拠（0=少年 / 1=ミナ / 3=ナレ）。
//   face は立ち絵パス（空ならナレ＝顔なし）。
public partial class ShopTutorial : Node2D
{
    // ───────── 強化ショップ案内（初回の中ボス撃破後・一度きり）─────────
    // who: 0=少年 / 1=ミナ / 3=ナレ。少年の振り→ミナの種明かし→少年のツッコミの掛け合いで案内する。
    private static readonly (int who, string text, string face)[] ShopTutorialLines =
    {
        (1, "……ご主人様。聞こえますか。最初の“声”が、静かになりました。", "res://char/mina_face.png"),
        (1, "ひと息つきましょう、ご主人様。せっかく稼いだのです、使い道をご案内しますわ。", "res://char/mina_smile.png"),
        (0, "稼いだ……？　ぼく、お金なんて拾った覚えないけど。", "res://char/shonen_face.png"),
        (1, "敵を浄化するたび、Alt で弾を“抜ける”たびに、貯まっておりますのよ。——“浄化した心”、♥の数が、それです。", "res://char/mina_face.png"),
        (0, "よけてるだけでお金が……ぼく、知らないうちに小金持ち？", "res://char/shonen_face.png"),
        (1, "その心を、強化ショップで わたくしに注いでくださいまし。拡散やホーミングの解放、光の出力・連射速度・拡散力——買えば心は減りますが、わたくしは確かに強くなります。", "res://char/mina_face.png"),
        (1, "それと——系統を奥まで伸ばしてくださると、その型だけの“奥義”が開きますのよ。楽しみになさって。", "res://char/mina_smile.png"),
        (1, "枝の先には“どちらか一方”の選択もございます。選んだ道がわたくしの形——でも、心を払えば選び直せますから、ご安心を。", "res://char/mina_face.png"),
        (1, "歯が立たない相手なら、無理は禁物。一度ここで強くなって、出直せばよろしいのです。", "res://char/mina_worried.png"),
        (1, "わたくしを、よく研いでおいてくださいね。——では、まいりましょう。", "res://char/mina_smile.png"),
    };

    // 立ち絵テクスチャは初出時にロードしてキャッシュ（毎フレームLoad禁止）。
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _faces = new();
    private Texture2D? FaceTex(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (!_faces.TryGetValue(path, out var t)) { t = ResourceLoader.Load<Texture2D>(path); _faces[path] = t; }
        return t;
    }

    private const float W = UiKit.DesignW, H = UiKit.DesignH;
    private GameManager _game = null!;

    private int _idx;
    private double _t, _lineT, _reveal;
    private bool _zHeld;
    private bool _autoplay;
    private bool _done;

    // テキストボックスは2行固定。2行を超える行はページに割り、送り（Z）で続きを読ませる（本文は削らない）。
    private readonly System.Collections.Generic.List<string> _pages = new();
    private int _page;
    private int _pagedIdx = -1;               // _pages を構築済みの行 index（行が変わったら作り直す）
    private const float BodyWrapW = W - 80f - 72f;  // DrawDialog の本文折り返し幅（box幅 W-80 の内側パディング 36×2）
    private string CurPage => _pages.Count > 0 ? _pages[Mathf.Min(_page, _pages.Count - 1)] : "";
    private bool LastPage => _pages.Count == 0 || _page >= _pages.Count - 1;

    // 現在行(_idx)のページを（未構築なら）作る。以降 _reveal は「現在ページ内の文字数」を指す。
    private void EnsurePages()
    {
        if (_pagedIdx == _idx || _idx >= ShopTutorialLines.Length) return;
        _pagedIdx = _idx; _page = 0;
        _pages.Clear();
        _pages.AddRange(UiKit.Paginate(UiKit.Zen, ShopTutorialLines[_idx].text, UiKit.FontHeading, BodyWrapW, Hud.DlgMaxLines));
    }

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
    }

    public override void _Process(double delta)
    {
        _t += delta; _lineT += delta;
        if (_done) { QueueRedraw(); return; }

        EnsurePages();
        int len = CurPage.Length;
        if (_autoplay)
        {
            _reveal = len;
            // オート：現在ページを見せたら次ページ、最終ページなら次行へ（詰まらせない）。
            if (_lineT >= 1.2) { _lineT = 0; if (!LastPage) NextPage(); else Advance(); }
            QueueRedraw();
            return;
        }

        // ポーズメニューを閉じた Z の同じ押下が漏れて会話が1行進まないよう食う（Pad.UiBlocked）。
        if (Pad.UiBlocked(this)) { _zHeld = true; QueueRedraw(); return; }

        // タイプライター（本編HUDと同じ速度。未設定なら48）。現在ページ内の文字を進める。
        if (_reveal < len)
            _reveal = Mathf.Min(len, _reveal + delta * (_game?.MsgCharsPerSec ?? 48f));

        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.15)
        {
            if (_reveal < len) _reveal = len;   // 1回目で全文（早送り）
            else if (!LastPage) NextPage();     // 後続ページがあれば続きへ
            else Advance();                     // 最終ページを読了＝次の行へ
        }
        QueueRedraw();
    }

    private void NextPage() { _page++; _reveal = 0; _lineT = 0; }

    private void Advance()
    {
        _idx++;
        _reveal = 0; _lineT = 0; _page = 0; _pagedIdx = -1;
        if (_idx >= ShopTutorialLines.Length)
        {
            _done = true;
            // 説明を読み切ったら、実際の Shop 画面を見せる（Shop は X でハブへ戻る）。
            Audio.Instance?.PlayUiConfirm();
            GetTree().ChangeSceneToFile("res://Shop.tscn");
        }
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color("0d0b1c"), new Color("0a0916"), new Color("070611") }, new[] { 0f, 0.55f, 1f });
        UiKit.RadialGlow(this, new Vector2(W * 0.5f, H * 0.32f), 480f, UiKit.Mina, 0.10f);
        for (float y = 0; y < H; y += 6f) DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.05f));

        // 見出し
        UiKit.Text(this, UiKit.Mono, new Vector2(40, 28), "SHOP TUTORIAL", UiKit.FontSmall, UiKit.Text3);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(40, 42), "強化ショップ", UiKit.FontTitle, UiKit.White);

        DrawDialog();
        UiKit.EndDesign(this);
    }

    private void DrawDialog()
    {
        if (_idx >= ShopTutorialLines.Length) return;
        var (who, text, face) = ShopTutorialLines[_idx];
        string sp = who switch { 0 => "少年", 1 => "ミナ", 3 => "", _ => "ミナ" };
        Color spc = who switch { 0 => UiKit.Info, 1 => UiKit.Mina, 3 => UiKit.Text3, _ => UiKit.Mina };

        var box = new Rect2(40, H - 220, W - 80, 180);
        UiKit.Box(this, box, new Color(0.05f, 0.04f, 0.09f, 0.96f), 16f, new Color(spc, 0.5f), 1.4f);
        if (sp.Length > 0)
        {
            var center = new Vector2(box.Position.X + 44, box.Position.Y + 44);
            var ftex = FaceTex(face);
            if (ftex != null) UiKit.FaceAvatar(this, center, 26f, ftex, spc, false, 0.06f, 1f, _t);
            else UiKit.Avatar(this, center, 26f, spc, sp.Substring(0, 1));
            UiKit.Text(this, UiKit.ZenBold, new Vector2(box.Position.X + 84, box.Position.Y + 24), sp, UiKit.FontSpeaker, spc);
        }

        // 現在ページ（2行固定）を表示済み文字数ぶんだけ描く。
        string page = CurPage;
        int shown = Mathf.Clamp((int)_reveal, 0, page.Length);
        float bodyX = box.Position.X + 36;
        float bodyY = box.Position.Y + (sp.Length > 0 ? 76 : 40);
        UiKit.Multi(this, UiKit.Zen, new Vector2(bodyX, bodyY), page.Substring(0, shown), UiKit.FontHeading,
            new Color(0.95f, 0.95f, 0.98f), box.Size.X - 72, Hud.DlgMaxLines);

        if (!_autoplay && _reveal >= page.Length)
        {
            float blink = 0.5f + 0.5f * Mathf.Sin((float)_t * 4f);
            // 後続ページがあれば「▼ つづき」、最終ページなら「Z すすむ ▸」。
            string hint = LastPage ? "Z すすむ ▸" : "▼ つづき";
            UiKit.Text(this, UiKit.Zen, new Vector2(box.Position.X + box.Size.X - 150, box.Position.Y + box.Size.Y - 36),
                hint, UiKit.FontLabel, new Color(UiKit.Info, blink));
        }
    }
}
