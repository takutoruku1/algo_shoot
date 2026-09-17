using Godot;
using System.Collections.Generic;

// Backlog : 会話ログ（バックログ）閲覧オーバーレイ（オートロード /root/Backlog）。
//   ストーリー重視のゲーム向けに、これまで表示されたセリフ/ナレ/投稿（Hud.SetDialog を通った行）を
//   遡って読み返せる ADV/ノベルゲームのバックログ相当。履歴の蓄積は Hud 側（static Hud.Backlog）。
//
//   開き方：プレイ中は専用キー/ボタン（L / Tab / 左スティック押し込み L3）で直接、またはポーズメニューから。
//           HowToPlay と同じくツリーをポーズして最前面で描くオーバーレイ（シーン遷移しない）。
//   操作：↑↓ または 左スティックでスクロール、X / B / Esc で閉じる。表記は Pad に集約（KB/PS/Xbox 追従）。
//   色分け：話者種別(LineKind: 0少年/1ミナ/2相手/3ナレ/4投稿/5中継)を Hud.KindColor に合わせる。
public partial class Backlog : CanvasLayer
{
    private BacklogCanvas _canvas = null!;
    private bool _open;
    private float _scroll;            // スクロール量（px・設計座標。0=最下＝最新を表示）
    private float _maxScroll;         // 描画時に算出した最大スクロール
    private bool _navHeld, _backHeld;
    private bool _autoplay;
    private bool _pausedBySelf;        // このオーバーレイが自前でツリーをポーズしたか（直接開いた場合のみ true）

    private System.Action? _onClose;  // 閉じたとき1度だけ呼ぶ（ポーズから開いた場合の復帰など）

    private const float WheelStep = 90f; // ホイール1ノッチあたりのスクロール量（Shop と同じ設計座標）

    // フッタ「とじる」のクリック矩形（Records.BackHintRect と同じ作法）。BacklogCanvas のフッタは
    //   中央寄せ1行なので、同じ式で「とじる」側の位置を再現して切り出す（左の「スクロール」は説明＝対象外）。
    public static Rect2 CloseHintRect()
    {
        float pad = 64f, x = pad, y = 48f, w = UiKit.DesignW - pad * 2f, h = UiKit.DesignH - 96f;
        float scrollW = UiKit.TextW(UiKit.Mono, BacklogCanvas.FootScrollToken(), UiKit.FontSmall);
        float closeW = UiKit.TextW(UiKit.Mono, BacklogCanvas.FootCloseToken(), UiKit.FontSmall);
        float lineX = x + (w - (scrollW + closeW)) / 2f;
        float ty = y + h - 32f;
        return new Rect2(lineX + scrollW - 6f, ty - 6f, closeW + 12f, UiKit.Mono.GetHeight(UiKit.FontSmall) + 12f);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // ポーズ中も動く
        Layer = 108;                          // ポーズメニュー(100) より前・HowTo(110) より後ろ
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
        _canvas = new BacklogCanvas { Menu = this };
        AddChild(_canvas);
    }

    // ゲームプレイ画面でのみ専用キーで開く。タイトル/設定/カットシーンは除外（PauseMenu と同基準）。
    private bool CanOpenHere()
    {
        string path = GetTree().CurrentScene?.SceneFilePath ?? "";
        if (string.IsNullOrEmpty(path)) return false;
        return !(path.Contains("TitleMenu") || path.Contains("Settings")
              || path.Contains("Prologue") || path.Contains("Final") || path.Contains("Epilogue"));
    }

    public void Open(System.Action? onClose = null)
    {
        if (_autoplay) { onClose?.Invoke(); return; }
        _open = true;
        // 開くのに使ったキー（L/Tab/Back）は閉じキーとも共通なので「押されたまま」として扱う。
        // false で初期化すると、押しっぱなしの同じキーを翌フレームに閉じエッジとして拾い、
        // 開いた瞬間に閉じる→また開く…のちらつきになる（週次PT「会話ログが安定しない」の主因）。
        _navHeld = true; _backHeld = true;
        _onClose = onClose;
        _scroll = 0f; // 0=最新（最下）を表示
        // 直接開いた（=まだ誰もポーズしていない）ときだけ自前でポーズし、閉じる時に自分で解除する。
        // ポーズメニュー経由（すでに Paused）なら触らない＝閉じてもポーズメニューに戻る（HowTo と同作法）。
        _pausedBySelf = !GetTree().Paused;
        if (_pausedBySelf) GetTree().Paused = true;
        Audio.Instance?.PlayUiConfirm();
        _canvas.QueueRedraw();
    }

    private void Close()
    {
        _open = false;
        _navHeld = true;   // 閉じたキー（L/Tab/Back）が押されたままの間は再オープンさせない
        Audio.Instance?.PlayUiCancel();
        // Esc で閉じたとき、同じ押下を PauseMenu が開閉エッジとして拾わないよう通知（1回ぶん吸収）。
        GetNodeOrNull<PauseMenu>("/root/PauseMenu")?.NoteOverlayClosed();
        // 自前ポーズの解除は即時にはやらず _Process 側で「閉じキーが離れてから」行う（_pausedBySelf を保持）。
        // ここで解除すると、閉じるのに使った X がそのままボム（Player は X の押下で発動）に、
        // Esc がポーズ開閉に化けるなど、押しっぱなしのキーがゲーム側へ漏れる。
        var cb = _onClose; _onClose = null;
        _canvas.QueueRedraw();
        cb?.Invoke();
    }

    // 開閉に使うキーのどれかが押されているか（自前ポーズの解除待ち判定用）。
    private static bool AnyToggleKeyHeld()
        => Input.IsKeyPressed(Key.L) || Input.IsKeyPressed(Key.Tab) || Input.IsKeyPressed(Key.X)
        || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.Back) || Pad.Pressed(JoyButton.B);

    public override void _Process(double delta)
    {
        if (_autoplay) return;

        if (!_open)
        {
            // 自前ポーズの解除待ち：閉じキーが全て離れてから解除する（Close のコメント参照）。
            if (_pausedBySelf)
            {
                // 待機中にポーズメニューが開いたら、ポーズの所有権をそちらへ譲る（二重解除防止）。
                if (GetNodeOrNull<PauseMenu>("/root/PauseMenu") is { IsOpen: true }) { _pausedBySelf = false; return; }
                if (!AnyToggleKeyHeld()) { GetTree().Paused = false; _pausedBySelf = false; }
                return;
            }
            // プレイ中の専用キー/ボタンで直接開く（ポーズも HowTo も開いていないとき限定）。
            bool pauseOpen = GetNodeOrNull<PauseMenu>("/root/PauseMenu") is { IsOpen: true };
            bool howOpen = GetNodeOrNull<HowToPlay>("/root/HowTo") is { IsOpen: true };
            // 開キー：L／Tab（KB）／Back・Select（パッド）。
            // パッドは L3(LeftStick)=回避と衝突するため Back を使う。
            bool openKey = Input.IsKeyPressed(Key.L) || Input.IsKeyPressed(Key.Tab) || Pad.Pressed(JoyButton.Back);
            bool openEdge = openKey && !_navHeld; _navHeld = openKey;
            if (openEdge && !pauseOpen && !howOpen && CanOpenHere() && Hud.Backlog.Count > 0) Open();
            return;
        }

        // 開いている間＝下の画面（ポーズ/ステージ/ハブ）への入力を食う
        //（閉じた Esc/X の同じ押下が下で二重処理されないための門・Pad.UiBlocked）。
        Pad.ConsumeUi(this);

        // スクロール（↑↓ / 左スティック上下）。上＝過去へ（scroll を増やす）、下＝最新へ。
        float ax = 0f;
        foreach (var dev in Input.GetConnectedJoypads())
            ax += Input.GetJoyAxis(dev, JoyAxis.LeftY);
        bool up = Input.IsActionPressed("ui_up") || ax < -0.4f;
        bool down = Input.IsActionPressed("ui_down") || ax > 0.4f;
        // ホールドで連続スクロール（行送りより px 送りの方がログ閲覧は素直）。
        float step = (float)delta * 520f;
        if (up) _scroll = Mathf.Min(_maxScroll, _scroll + step);
        if (down) _scroll = Mathf.Max(0f, _scroll - step);

        // マウスホイールでも縦スクロール（↑↓と同じ向き：上(+)＝過去へ／下(−)＝最新へ）。
        _scroll = Mathf.Clamp(_scroll + Pad.WheelDelta() * WheelStep, 0f, _maxScroll);

        // マウス：フッタの「とじる」を左クリックでも押せる（開いている間はこのオーバーレイが唯一の登録者）。
        UiKit.BeginHotspots(Pad.MousePos());
        UiKit.Hotspot(CloseHintRect(), 0);
        bool clickClose = UiKit.ClickedId(Pad.MouseClick()) == 0;

        // X / B / Esc、または開キー(L/Tab/Back)でも閉じる（トグル感覚）。右クリックでも閉じる。
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B)
                    || Input.IsKeyPressed(Key.L) || Input.IsKeyPressed(Key.Tab) || Pad.Pressed(JoyButton.Back)
                    || Pad.MouseRightClick();
        if ((back && !_backHeld) || clickClose) { _backHeld = back; Close(); return; }
        _backHeld = back;

        _canvas.QueueRedraw();
    }

    public bool IsOpen => _open;
    public float Scroll => _scroll;
    public void SetMaxScroll(float v) => _maxScroll = Mathf.Max(0f, v);
}

// 会話ログの描画（CanvasLayer の子。設計座標 1280×720）。
public partial class BacklogCanvas : Node2D
{
    public Backlog Menu = null!;

    public override void _Ready() { ProcessMode = ProcessModeEnum.Always; }

    public override void _Draw()
    {
        if (Menu == null || !Menu.IsOpen) return;
        UiKit.BeginDesign(this);
        DrawScreen();
        UiKit.EndDesign(this);
    }

    private void DrawScreen()
    {
        float W = UiKit.DesignW, H = UiKit.DesignH;
        DrawRect(new Rect2(0, 0, W, H), new Color(4 / 255f, 6 / 255f, 14 / 255f, 0.92f)); // 暗幕

        float pad = 64f;
        float x = pad, y = 48f, w = W - pad * 2f, h = H - 96f;
        Color panelBg = new Color(0.05f, 0.05f, 0.10f, 0.98f);
        UiKit.Box(this, new Rect2(x, y, w, h), panelBg, 18f, new Color(UiKit.Purify, 0.6f), 1.4f);

        // 本体（縦スクロール領域）。Node2D には任意矩形クリップAPIが無いため、
        // 「範囲に掛かるブロックだけ描く」＋「上下を不透明マスク帯で覆う」で擬似クリップする。
        float listX = x + 32f, listW = w - 64f;
        float listTop = y + 92f;
        float listBottom = y + h - 44f;       // フッタの上
        float listH = listBottom - listTop;

        var log = Hud.Backlog;
        if (log.Count == 0)
        {
            Menu.SetMaxScroll(0f);
            UiKit.Text(this, UiKit.Zen, new Vector2(listX, listTop + 8), "まだ会話の記録はありません。", UiKit.FontBody, UiKit.Text3);
        }
        else
        {
            const int speakerSize = UiKit.FontLabel, bodySize = UiKit.FontBody;
            float lineGap = 14f;            // 行ブロック間の余白
            float contentH = 0f;
            float[] blockH = new float[log.Count];
            for (int i = 0; i < log.Count; i++)
            {
                blockH[i] = BlockHeight(log[i], listW, speakerSize, bodySize);
                contentH += blockH[i] + lineGap;
            }

            float maxScroll = Mathf.Max(0f, contentH - listH);
            Menu.SetMaxScroll(maxScroll);

            // scroll=0 で最新（最下）が見える。下端基準で上へ積む。
            float drawY = listBottom - contentH + Menu.Scroll;
            for (int i = 0; i < log.Count; i++)
            {
                float by = drawY, bh = blockH[i];
                if (by + bh >= listTop && by <= listBottom)
                    DrawBlock(log[i], listX, by, listW, speakerSize, bodySize);
                drawY += bh + lineGap;
            }

            // 擬似クリップ：リスト域の上下にはみ出した行を、パネル背景色の不透明帯で覆い隠す。
            ci_DrawRect(x + 1.4f, y + 1.4f, w - 2.8f, listTop - (y + 1.4f), panelBg);            // 上マスク（ヘッダ下〜listTop）
            ci_DrawRect(x + 1.4f, listBottom, w - 2.8f, (y + h - 1.4f) - listBottom, panelBg);   // 下マスク（listBottom〜フッタ）

            // スクロールバー（右）。可視割合と位置を示す。
            if (maxScroll > 0.5f)
            {
                float trackX = x + w - 18f, trackY = listTop, trackH = listH;
                DrawRect(new Rect2(trackX, trackY, 4f, trackH), new Color(1, 1, 1, 0.08f));
                float vis = Mathf.Clamp(listH / contentH, 0.08f, 1f);
                float thumbH = trackH * vis;
                float frac = 1f - Menu.Scroll / maxScroll; // 0(上/過去)..1(下/最新)
                float thumbY = trackY + (trackH - thumbH) * frac;
                DrawRect(new Rect2(trackX, thumbY, 4f, thumbH), new Color(UiKit.Purify, 0.6f));
            }
        }

        // ── ヘッダ（マスクの上に描いてはみ出し行を隠す）──
        UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x + 32, y + 22), "BACKLOG", UiKit.Info);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x + 32, y + 38), "会話ログ", UiKit.FontTitle, UiKit.White);
        DrawRect(new Rect2(x + 32, y + 74, w - 64, 1f), new Color(1, 1, 1, 0.1f));

        // ── フッタ ──
        //   「とじる」だけクリック可＝ホバーで明るくする（左の「スクロール」は操作説明なので素の文字のまま）。
        string scrollTok = FootScrollToken(), closeTok = FootCloseToken();
        float scrollW = UiKit.TextW(UiKit.Mono, scrollTok, UiKit.FontSmall);
        float closeW = UiKit.TextW(UiKit.Mono, closeTok, UiKit.FontSmall);
        float lineX = x + (w - (scrollW + closeW)) / 2f;
        float fy = y + h - 32;
        bool closeHov = UiKit.HoveredId() == 0;
        if (closeHov)
            UiKit.Box(this, Backlog.CloseHintRect(), new Color(UiKit.Purify, 0.14f), 7f, new Color(UiKit.Info, 0.5f), 1f);
        UiKit.Text(this, UiKit.Mono, new Vector2(lineX, fy), scrollTok, UiKit.FontSmall, UiKit.Text3);
        UiKit.Text(this, UiKit.Mono, new Vector2(lineX + scrollW, fy), closeTok, UiKit.FontSmall,
            closeHov ? UiKit.PurifyHi : UiKit.Text3);
    }

    // フッタ1行の分割トークン（Backlog.CloseHintRect が同じ式で「とじる」側の矩形を再現する）。
    //   マウス使用中はスクロールがホイールなので、表記もそれに合わせる（Hud.TokFocus と同じ出し分け作法）。
    public static string FootScrollToken() => (Pad.UsingMouse ? "ホイール" : Pad.ShowKeyboard ? "↑↓" : "L") + " スクロール    ";
    public static string FootCloseToken() => Pad.CancelToken + " とじる";

    // 高さ/幅が正のときだけ塗る小ヘルパ（マスク帯用）。
    private void ci_DrawRect(float x, float y, float w, float h, Color c)
    {
        if (w > 0f && h > 0f) DrawRect(new Rect2(x, y, w, h), c);
    }

    // 1行ブロック（話者ラベル＋本文）の高さを実測。本文は折り返し前提で行数から算出。
    private float BlockHeight(Hud.LogLine line, float w, int speakerSize, int bodySize)
    {
        float speakerH = UiKit.ZenBold.GetHeight(speakerSize);
        float bodyW = w - 8f;
        // 折り返し行数：描画（UiKit.Multi＝禁則つき WrapLines）と同一ロジックで実測＝高さズレを作らない。
        int n = UiKit.WrapLines(UiKit.Zen, line.Text, bodySize, bodyW).Count;
        return speakerH + 4f + n * UiKit.Zen.GetHeight(bodySize) + 2f;
    }

    private void DrawBlock(Hud.LogLine line, float x, float y, float w, int speakerSize, int bodySize)
    {
        Color col = line.Color;
        // 左の話者色アクセントバー（色分けの一目）。
        DrawRect(new Rect2(x - 12f, y + 2f, 3f, UiKit.ZenBold.GetHeight(speakerSize) - 4f), new Color(col, 0.85f));
        string sp = string.IsNullOrEmpty(line.Speaker) ? "" : line.Speaker;
        float speakerH = UiKit.ZenBold.GetHeight(speakerSize);
        if (sp.Length > 0)
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y), sp, speakerSize, col);
        // ナレは話者ラベルが薄い分、本文も少し落ち着いた色で。
        Color bodyCol = line.Kind == Hud.LineKind.Narration ? new Color(0.82f, 0.82f, 0.88f) : new Color(0.95f, 0.95f, 0.98f);
        UiKit.Multi(this, UiKit.Zen, new Vector2(x, y + speakerH + 4f), line.Text, bodySize, bodyCol, w - 8f);
    }
}
