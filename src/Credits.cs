using Godot;
using System.Collections.Generic;

// Credits : クレジット画面。タイトルメニュー「クレジット」から開き、X/Esc で戻る。
//   BGM 等のフリー素材のクレジット表記義務を満たすための画面。UiKit のガラストーン/役割色を踏襲。
//   表記内容はデータ駆動（config/credits.ini）＝曲の追加・変更でコードは触らない（ini に1行足すだけ）。
//   書式は ConfigFile ではなく行単位の独自パース（引用符不要）:
//     [見出し — HEADING] ＝セクション / 素の行＝本文 / "> " 始まり＝補足（小さく薄く）/ ";" "#" ＝コメント
//   読み込みチェーン（後勝ち・ファイル単位で丸ごと差し替え。BossTuning と同じ運用思想）:
//     1) res://config/credits.ini …… 既定（export_presets の include_filter "config/*.ini" で同梱）
//     2) エディタ実行時: プロジェクトルート直下の credits.ini（試し書き用・任意）
//     3) exe 隣接の credits.ini …… 配布後の表記修正に「置くだけ」で対応
//   ファイル欠落・空でも最小の開発表記で動く（起動不能・実行時例外は絶対に出さない）。
//   ↑↓ 押しっぱなしでスクロール（曲数が増えても収まる）。パッドはスティック/十字キーで同じ。
public partial class Credits : Node2D
{
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    // 表示要素の種類。Empty＝空セクションの埋め草（見出しだけが連続してバグに見えないように）。
    private enum Kind { Section, Line, Note, Empty }
    private readonly List<(Kind kind, string text)> _items = new();

    private double _t;
    private float _scroll;    // スクロール量（設計px・0=先頭）
    private float _contentH;  // 直近 _Draw で測った内容全高（クランプ上限に使う）
    private bool _backHeld;
    private bool _autoplay;

    private const float ScrollSpeed = 360f; // 押しっぱなしスクロール速度(設計px/s)
    private const float ClipTop = 130f;     // 内容ビューポート上端（ヘッダ罫線の下）
    private const float ClipBottom = 636f;  // 内容ビューポート下端（フッタ罫線の上）
    private const float FadeH = 30f;        // 上下端のフェード幅（ハードクリップの代わり）
    private const float PadX = 56f;         // 左右余白（Records と同じ）

    public override void _Ready()
    {
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
        LoadData();
    }

    // ───────── データ読み込み（起動時一度だけ）─────────

    private void LoadData()
    {
        _items.Clear();
        // 後勝ち＝リスト後方で読めたファイルが丸ごと採用される（キー単位マージはしない＝自由文のため）。
        string? text = null;
        foreach (string path in CandidatePaths())
        {
            string? s = ReadText(path);
            if (s != null) text = s;
        }
        if (text != null) Parse(text);
        // 欠落・空・コメントだけでも最低限の開発表記を出す（画面が空白＝バグに見えるのを防ぐ）。
        if (_items.Count == 0)
        {
            _items.Add((Kind.Section, "開発 — DEVELOPMENT"));
            _items.Add((Kind.Line, "企画・開発 — algo project"));
        }
    }

    private static IEnumerable<string> CandidatePaths()
    {
        yield return "res://config/credits.ini";
        if (OS.HasFeature("editor"))
            yield return ProjectSettings.GlobalizePath("res://credits.ini");
        string exeDir = OS.GetExecutablePath().GetBaseDir();
        if (!string.IsNullOrEmpty(exeDir))
            yield return exeDir.PathJoin("credits.ini");
    }

    private static string? ReadText(string path)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return f?.GetAsText();
    }

    // 行単位パース。セクション見出し / 本文 / 補足 / コメントの4種だけの素朴な書式。
    private void Parse(string text)
    {
        bool sectionHasBody = true; // 直前のセクションに本文があったか（先頭は true＝埋め草不要）
        foreach (string raw in text.Split('\n'))
        {
            string s = raw.Trim();
            if (s.Length == 0 || s.StartsWith(";") || s.StartsWith("#")) continue;
            if (s.StartsWith("[") && s.EndsWith("]"))
            {
                if (!sectionHasBody) _items.Add((Kind.Empty, ""));
                _items.Add((Kind.Section, s[1..^1].Trim()));
                sectionHasBody = false;
            }
            else if (s.StartsWith(">"))
            {
                _items.Add((Kind.Note, s[1..].Trim()));
                sectionHasBody = true;
            }
            else
            {
                _items.Add((Kind.Line, s));
                sectionHasBody = true;
            }
        }
        if (!sectionHasBody) _items.Add((Kind.Empty, ""));
    }

    // ───────── 入力 ─────────

    // マウスホイール1ノッチあたりのスクロール量（設計px）。行高（Line=27）の約4行ぶん。
    private const float WheelStep = 110f;

    // 「もどる」フッタヒントのクリック矩形。フッタは可変（スクロールヒントの有無で x が動く）ため、
    // 常に存在する「もどる」ヒント帯を右クリック相当でも押せるよう、フッタ全域を戻る領域にせず帯だけ登録する。
    private Rect2 BackHintRect()
    {
        float fy = H - 56f, fx = PadX;
        // スクロール可能時はフッタ先頭に「↑↓ スクロール」ヒントが入る＝その分だけ「もどる」を右へずらす。
        if (_contentH > ClipBottom - ClipTop)
        {
            float k1 = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, "↑↓", 12) + 12f);
            fx += k1 + 8f + UiKit.TextW(UiKit.Zen, "スクロール", UiKit.FontLabel) + 24f;
        }
        string key = Pad.CancelToken;
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        float lw = UiKit.TextW(UiKit.Zen, "もどる", UiKit.FontLabel);
        return new Rect2(fx, fy - 16f, kw + 8f + lw + 8f, 32f);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_autoplay) { GetTree().ChangeSceneToFile("res://TitleMenu.tscn"); return; }
        // 上のオーバーレイを閉じた Esc 等の同じ押下が「もどる」として漏れないよう食う（Records と同じ防御）。
        if (Pad.UiBlocked(this)) { _backHeld = true; QueueRedraw(); return; }

        // マウス：フッタの「もどる」クリック／右クリックでも戻る。ホイールでスクロール（上+/下-）。
        UiKit.BeginHotspots(Pad.MousePos());
        UiKit.Hotspot(BackHintRect(), 0);
        bool clickBack = UiKit.ClickedId(Pad.MouseClick()) == 0 || Pad.MouseRightClick();

        // スクロール（↑↓押しっぱなしで連続＋マウスホイール。端でクランプ。_contentH は _Draw が更新する）
        float max = Mathf.Max(0f, _contentH - (ClipBottom - ClipTop));
        if (Input.IsActionPressed("ui_down")) _scroll += ScrollSpeed * (float)delta;
        if (Input.IsActionPressed("ui_up")) _scroll -= ScrollSpeed * (float)delta;
        _scroll -= Pad.WheelDelta() * WheelStep; // 上ホイール(+)で上へ戻る＝_scroll 減少
        _scroll = Mathf.Clamp(_scroll, 0f, max);

        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld; _backHeld = back;
        if ((backEdge || clickBack) && _t > 0.2) { Audio.Instance?.PlayUiCancel(); GetTree().ChangeSceneToFile("res://TitleMenu.tscn"); }

        QueueRedraw();
    }

    // ───────── 描画（設計座標 1280×720）─────────

    public override void _Draw()
    {
        UiKit.BeginDesign(this);

        // 背景：Records と同系の夜グラデ＋放射グロウ＋スキャンライン
        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color("0c142a"), new Color("0a1022"), new Color("070a16") }, new[] { 0f, 0.55f, 1f });
        UiKit.RadialGlow(this, new Vector2(W * 0.5f, 0), 460f, new Color(120 / 255f, 150 / 255f, 210 / 255f), 0.14f);
        for (float y = 0; y < H; y += 6f) DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.05f));

        // ── ヘッダ ──
        float top = 40f;
        UiKit.Text(this, UiKit.Mono, new Vector2(PadX, top + 8), "CREDITS", UiKit.FontLabel, UiKit.Info);
        float tagW = UiKit.TextW(UiKit.Mono, "CREDITS", UiKit.FontLabel);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(PadX + tagW + 16, top), "クレジット", UiKit.FontTitle, UiKit.White);
        UiKit.Text(this, UiKit.Zen, new Vector2(PadX, top + 4), "音楽・素材・開発スタッフ", UiKit.FontBody, UiKit.Text3,
            HorizontalAlignment.Right, W - PadX * 2);
        DrawRect(new Rect2(PadX, top + 44, W - PadX * 2, 1f), new Color(1, 1, 1, 0.1f));

        // ── 内容（スクロール・上下端はアルファフェードで溶かす）──
        DrawContent();

        // ── フッタ ──
        float fy = H - 56f;
        DrawRect(new Rect2(PadX, fy - 14, W - PadX * 2, 1f), new Color(1, 1, 1, 0.08f));
        float fx = PadX;
        if (_contentH > ClipBottom - ClipTop) fx = Hint(fx, fy, "↑↓", "スクロール", false);
        Hint(fx, fy, Pad.CancelToken, "もどる", true);

        UiKit.EndDesign(this);
    }

    private void DrawContent()
    {
        float contentW = W - PadX * 2 - 24f; // 右にスクロールバーの逃げ
        float y = ClipTop + 6f - _scroll;
        bool first = true;
        foreach (var (kind, text) in _items)
        {
            if (kind == Kind.Section && !first) y += 28f; // セクション前の間
            float h = ItemH(kind);
            if (y + h > ClipTop - 10f && y < ClipBottom + 10f)
                DrawItem(kind, text, PadX, y, contentW);
            y += h;
            first = false;
        }
        _contentH = y + _scroll - ClipTop + 10f; // 実測全高（次フレームのクランプ上限）

        DrawScrollbar();
    }

    private static float ItemH(Kind k) => k switch
    {
        Kind.Section => 48f,
        Kind.Line => 27f,
        Kind.Note => 20f,
        _ => 24f, // Empty
    };

    // 上下端のフェード係数（ビューポート外へ出るほど 0 へ）。ハードクリップ不要で境界が上品になる。
    private static float EdgeFade(float yCenter)
    {
        float a = Mathf.Clamp((yCenter - ClipTop) / FadeH, 0f, 1f);
        float b = Mathf.Clamp((ClipBottom - yCenter) / FadeH, 0f, 1f);
        return Mathf.Min(a, b);
    }

    private void DrawItem(Kind kind, string text, float x, float y, float w)
    {
        float fade = EdgeFade(y + ItemH(kind) / 2f);
        if (fade <= 0.01f) return;
        switch (kind)
        {
            case Kind.Section:
            {
                // 見出し：アクセントバー＋和文＋（あれば）英字サブ表記。下に罫線（Records の行頭作法）。
                string jp = text.Contains('—') ? text.Split('—')[0].Trim() : text;
                string en = text.Contains('—') ? text.Split('—')[^1].Trim() : "";
                DrawRect(new Rect2(x, y + 6, 4f, 22f), new Color(UiKit.Purify, 0.7f * fade));
                UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 16, y + 4), jp, UiKit.FontSpeaker, new Color(UiKit.White, fade));
                if (en.Length > 0)
                {
                    float jpW = UiKit.TextW(UiKit.ZenBold, jp, UiKit.FontSpeaker);
                    UiKit.Text(this, UiKit.Mono, new Vector2(x + 16 + jpW + 12, y + 13), en.ToUpper(), UiKit.FontSmall, new Color(UiKit.Info, 0.7f * fade));
                }
                DrawRect(new Rect2(x, y + 38, w, 1f), new Color(1, 1, 1, 0.08f * fade));
                break;
            }
            case Kind.Line:
                UiKit.Text(this, UiKit.Zen, new Vector2(x + 16, y + 3), text, UiKit.FontBody, new Color(UiKit.Text2, fade));
                break;
            case Kind.Note:
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 34, y + 2), text, UiKit.FontSmall, new Color(UiKit.Text3, 0.9f * fade));
                break;
            case Kind.Empty:
                UiKit.Text(this, UiKit.Zen, new Vector2(x + 16, y + 3), "——（いまは表記対象の素材はありません）", UiKit.FontLabel, new Color(UiKit.Text4, fade));
                break;
        }
    }

    private void DrawScrollbar()
    {
        float viewH = ClipBottom - ClipTop;
        float max = _contentH - viewH;
        if (max <= 0f) return;
        float x = W - PadX + 14f;
        UiKit.Box(this, new Rect2(x, ClipTop, 4f, viewH), new Color(1, 1, 1, 0.07f), 2f);
        float thumbH = Mathf.Max(36f, viewH * viewH / _contentH);
        float ty = ClipTop + (viewH - thumbH) * (_scroll / max);
        UiKit.Box(this, new Rect2(x, ty, 4f, thumbH), new Color(UiKit.Purify, 0.55f), 2f);
    }

    private float Hint(float x, float y, string key, string label, bool accent)
    {
        Color kbg = accent ? new Color(UiKit.Purify, 0.12f) : new Color(1, 1, 1, 0.07f);
        Color kbd = accent ? new Color(UiKit.Info, 0.5f) : new Color(1, 1, 1, 0.16f);
        UiKit.Key(this, new Vector2(x, y - 12), key, kbg, kbd, accent ? UiKit.PurifyHi : UiKit.Text2);
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + kw + 8, y - 8), label, UiKit.FontLabel, accent ? UiKit.Info : UiKit.Text3);
        return x + kw + 8 + UiKit.TextW(UiKit.Zen, label, UiKit.FontLabel) + 24f;
    }
}
