using Godot;

// Records : クリアタイムの記録画面。ステージ(縦) × 難易度(横) のベストタイム表。
//   未記録は "--"。UiKit のガラストーン/角丸/役割色を踏襲。Hub のフッタ T から開き、X/Esc で戻る。
//   セーブ(save_N.json)の clearTimes を GameManager 経由で参照（このシーンは状態を変更しない）。
public partial class Records : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    private double _t;
    private bool _backHeld;
    private bool _autoplay;

    // 表の行＝ストーリーステージ＋FINAL。表示名は短く。
    private static readonly (string id, string label)[] Rows =
    {
        ("akari", "STAGE 1 — あかり"),
        ("koharu", "STAGE 2 — こはる"),
        ("rei", "STAGE 3 — レイ"),
        ("final", "FINAL — ミナ"),
    };
    // 列＝難易度。
    private static readonly (GameManager.Diff diff, string label)[] Cols =
    {
        (GameManager.Diff.Easy, "EASY"),
        (GameManager.Diff.Normal, "NORMAL"),
        (GameManager.Diff.Hard, "HARD"),
        (GameManager.Diff.Lunatic, "LUNATIC"),
    };

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
    }

    // 「もどる」フッタヒントのクリック矩形（_Draw のフッタ Hint と同じ x=padX, y=H-56）。
    //   キー分の幅＋ラベル幅を含む帯を丸ごとクリック可能にする（Records は戻る操作のみ）。
    private static Rect2 BackHintRect()
    {
        float padX = 56f, fy = H - 56f;
        string key = Pad.CancelToken;
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        float lw = UiKit.TextW(UiKit.Zen, "もどる", UiKit.FontLabel);
        return new Rect2(padX, fy - 16f, kw + 8f + lw + 8f, 32f);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (!_autoplay)
        {
            // ポーズメニューを閉じた Esc の同じ押下が漏れて「もどる」が誤発火しないよう食う（Pad.UiBlocked）。
            if (Pad.UiBlocked(this)) { _backHeld = true; QueueRedraw(); return; }

            // マウス：フッタの「もどる」クリック／右クリックでも戻れる（Records は他に選択要素なし）。
            UiKit.BeginHotspots(Pad.MousePos());
            UiKit.Hotspot(BackHintRect(), 0);
            bool clickBack = UiKit.ClickedId(Pad.MouseClick()) == 0 || Pad.MouseRightClick();

            bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape)
                        || Input.IsKeyPressed(Key.T) || Pad.Pressed(JoyButton.B);
            bool backEdge = back && !_backHeld; _backHeld = back;
            if ((backEdge || clickBack) && _t > 0.2) { Audio.Instance?.PlayUiCancel(); GetTree().ChangeSceneToFile("res://Hub.tscn"); }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);

        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color("0c142a"), new Color("0a1022"), new Color("070a16") }, new[] { 0f, 0.55f, 1f });
        UiKit.RadialGlow(this, new Vector2(W * 0.5f, 0), 460f, new Color(120 / 255f, 150 / 255f, 210 / 255f), 0.14f);
        for (float y = 0; y < H; y += 6f) DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.05f));

        float padX = 56f, top = 40f;
        // ── ヘッダ ──
        UiKit.Draw(this, UiKit.SmallLabel, new Vector2(padX, top + 8), "RECORDS", UiKit.Info);
        float tagW = UiKit.TrackedW(UiKit.SmallLabel, "RECORDS");
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(padX + tagW + 16, top), "クリアタイム", UiKit.FontTitle, UiKit.White);
        UiKit.Text(this, UiKit.Zen, new Vector2(padX, top + 4), "ステージ × 難易度のベストタイム／スコア", UiKit.FontBody, UiKit.Text3,
            HorizontalAlignment.Right, W - padX * 2);
        DrawRect(new Rect2(padX, top + 44, W - padX * 2, 1f), new Color(1, 1, 1, 0.1f));

        // ── 表のジオメトリ ──
        float tableTop = top + 70f;
        float tableW = W - padX * 2;
        float labelW = 300f;                         // 行見出し（ステージ名）
        float colsW = tableW - labelW;
        float colW = colsW / Cols.Length;
        float headH = 40f;
        float rowH = 92f, rowGap = 12f; // タイム＋スコアの2段表示ぶん、タイムのみ(74)より高さを取る

        // ── 列見出し（難易度）──
        for (int c = 0; c < Cols.Length; c++)
        {
            float cx = padX + labelW + c * colW;
            Color cc = DiffColor(Cols[c].diff);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(cx, tableTop + 12), Cols[c].label, UiKit.FontBody, cc,
                HorizontalAlignment.Center, colW);
        }
        DrawRect(new Rect2(padX, tableTop + headH, tableW, 1f), new Color(1, 1, 1, 0.08f));

        // ── 各行 ──
        for (int r = 0; r < Rows.Length; r++)
        {
            float ry = tableTop + headH + 8f + r * (rowH + rowGap);
            DrawRow(Rows[r].id, Rows[r].label, padX, ry, labelW, colW, rowH);
        }

        // ── フッタ ──
        float fy = H - 56f;
        DrawRect(new Rect2(padX, fy - 14, W - padX * 2, 1f), new Color(1, 1, 1, 0.08f));
        Hint(padX, fy, Pad.CancelToken, "もどる", true);

        UiKit.EndDesign(this);
    }

    private void DrawRow(string id, string label, float x, float y, float labelW, float colW, float h)
    {
        float tableW = labelW + colW * Cols.Length;
        // 行のガラス下敷き
        UiKit.Box(this, new Rect2(x, y, tableW, h), new Color(22 / 255f, 18 / 255f, 34 / 255f, 0.42f), 12f, new Color(1, 1, 1, 0.07f), 1f);
        // 行アクセントバー（ステージ色）
        DrawRect(new Rect2(x + 4, y + 10, 3, h - 20), new Color(StageColor(id), 0.6f));

        // ステージ名（2段：STAGE n / 名前）
        string head = label.Contains("—") ? label.Split('—')[0].Trim() : label;
        string name = label.Contains("—") ? label.Split('—')[^1].Trim() : label;
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 22, y + 16), head, UiKit.FontSmall, UiKit.Text3);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 22, y + 36), name, UiKit.FontHeading, UiKit.White);

        // この行で最速の難易度（ハイライト用）。
        var best = _game?.BestAcrossDiffs(id);

        for (int c = 0; c < Cols.Length; c++)
        {
            float cx = x + labelW + c * colW;
            var t = _game?.GetBestTime(id, Cols[c].diff);
            var sc = _game?.GetBestScore(id, Cols[c].diff);
            bool isRowBest = best != null && t != null && Mathf.IsEqualApprox(t.Value, best.Value.sec) && Cols[c].diff == best.Value.diff;
            string s = t != null ? UiKit.FormatTime(t.Value) : "--";
            string scS = sc != null ? UiKit.FormatScore(sc.Value) : "--";
            Color tc = t == null ? UiKit.Text4 : (isRowBest ? UiKit.Gold : UiKit.PurifyHi);
            Color scc = sc == null ? UiKit.Text4 : UiKit.Gold;
            // セル枠（行内ベストは淡く強調・タイム＋スコアの2段ぶん広げる）
            if (isRowBest)
                UiKit.Box(this, new Rect2(cx + 8, y + h / 2f - 26, colW - 16, 52f), new Color(UiKit.Gold, 0.08f), 8f, new Color(UiKit.Gold, 0.4f), 1f);
            // タイム行（上段）
            UiKit.Text(this, UiKit.Mono, new Vector2(cx, y + h / 2f - 20), s, UiKit.FontHeading, tc,
                HorizontalAlignment.Center, colW);
            // ベストスコア行（下段・タイムより小さく）
            UiKit.Text(this, UiKit.Mono, new Vector2(cx, y + h / 2f + 8), scS, UiKit.FontSmall, scc,
                HorizontalAlignment.Center, colW);
        }
    }

    private static Color DiffColor(GameManager.Diff d) => d switch
    {
        GameManager.Diff.Easy => UiKit.Ok,
        GameManager.Diff.Hard => new Color("e89460"),
        GameManager.Diff.Lunatic => UiKit.Kegare,
        _ => UiKit.Purify,
    };

    private static Color StageColor(string id) => id switch
    {
        "rei" => new Color(0.90f, 0.52f, 0.38f),
        "akari" => new Color(0.40f, 0.62f, 0.88f),
        "koharu" => new Color(0.46f, 0.74f, 0.52f),
        "final" => UiKit.Kegare,
        _ => UiKit.Text3,
    };

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
