using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// HubKeysQa : スマホ画面（Hub）の操作を「矢印で選ぶ・Z で決める・Esc／X でもどる」に絞った変更（2026-09-27）の検証。
//   (a) キーボードだけで ホーム → SNS → キャラのカードの [返信] を選んで会話を開く
//   (b) [ダイブ] で投稿詳細（難易度選択）に入る
//   (c) 最後のカードで ↓ → フッタ「アカウント」 → Z でアカウント切り替え
//   (d) J／C／T／R を押しても何も起きない（ホーム・SNS・詳細）
//   (e) 画面下端のヒント帯が描かれる（Hub.HintBarRect／HintBarDrawnRect）。会話中は出ない。パッド表記は 十字／A／B／≡
//   (f) Hub では PauseMenu の右下チップ（ShowHint）を出さない
//   窓ありで `-- --hk-shot` を付けると build/shots_hub_keys/ に sns_card_buttons／footer_focus／home と
//   ヒント帯の3倍切り抜き hint_zoom（パッド表記は hint_zoom_pad）を保存する。
public partial class HubKeysQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static string Mode(Hub hub) => Read<object>(hub, "_mode").ToString()!;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[HubKeysQA] PASS {message}");
    }

    private bool _shots;
    private string _out = "";

    public override async void _Ready()
    {
        try
        {
            _shots = Array.IndexOf(OS.GetCmdlineUserArgs(), "--hk-shot") >= 0;
            _out = ProjectSettings.GlobalizePath("res://build/shots_hub_keys");
            if (_shots) DirAccess.MakeDirRecursiveAbsolute(_out);
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            foreach (var job in Jobs.All) game.MarkIdleDialogSeen($"once_companion_select_{job.CharacterId}");
            game.MarkIdleDialogSeen("once_sns_intro");
            game.MarkIdleDialogSeen("once_phone_home");
            game.MarkIdleDialogSeen("once_account_intro");
            // あかりをクリア済みにする＝あかりのカードで [返信] が使える（CanReplySel）。
            Read<HashSet<string>>(game, "_cleared").Add(GameManager.FirstStageId);
            // 強化ショップの初回説明（ShopTutorial）は既読にする。あかりをクリア済みにした瞬間 ShopUnlocked が立ち、
            //   ホームが ShopTutorial.tscn へ飛んでしまう（CompanionDialogueQa と同じ扱い）。
            game.ShopTutorialSeen = true;
            game.AutoSaveEnabled = false;
            game.Difficulty = GameManager.Diff.Normal;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            var pause = GetNode<PauseMenu>("/root/PauseMenu");
            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            // 再訪小話（入場の約半分で SNS を開いたときに1本挟む雑談）は乱数なので、ここでは止める。
            hub.GetType().GetField("_idleTalkPending", Private)!.SetValue(hub, false);
            // デスクトップのカーソル位置やパッドの有無で表記が揺れないよう、キーボード表記に固定する。
            SetPad(false);
            pause.SetProcess(false);
            await Frames(10);
            await Seconds(0.5);   // Hub の入力ゲート（_t > 0.3）を実時間で越える

            // ── (f) PauseMenu の右下チップは Hub では出さない ──
            Check(!pause.ShowHint && !pause.HintClickable, "(f) Hub hides the PauseMenu corner chip (the hint bar carries M)");

            // ── ホーム ──
            Check(Mode(hub) == "Home", "hub starts on the phone home");
            await CheckHint(hub, "home", "↑↓←→", "ひらく");
            await Shot("home");
            foreach (var k in new[] { Key.J, Key.C, Key.T, Key.R })
            {
                await Keypress(k);
                Check(Mode(hub) == "Home" && !Read<bool>(hub, "_dived") && GetTree().CurrentScene == hub,
                    $"(d) {k} does nothing on home");
            }

            // ── SNS を開く（ホームの選択は SNS＝0 番）──
            Check(Read<int>(hub, "_homeSel") == 0, "home cursor starts on the SNS app");
            await Keypress(Key.Z);
            // SNS はアプリ起動アニメ（SnsOpening）を挟んでから Cards へ移る。抜けるまで待つ。
            for (int i = 0; i < 600 && Mode(hub) == "SnsOpening"; i++) await Frames(1);
            await Seconds(0.4);
            Check(Mode(hub) == "Cards", "(a) Z on home opens the SNS timeline");
            await CheckHint(hub, "sns", "↑↓", "ボタン");

            // あかりのカードへ ↑↓ だけで移る。
            int akari = IndexOf(hub, GameManager.FirstStageId);
            Check(akari >= 0, "akari card is on the timeline");
            for (int guard = 0; guard < 40 && Read<int>(hub, "_sel") != akari; guard++)
                await Keypress(Read<int>(hub, "_sel") < akari ? Key.Down : Key.Up);
            Check(Read<int>(hub, "_sel") == akari && Read<int>(hub, "_footSel") < 0, "(a) arrows reach akari's card");
            Check((bool)Call(hub, "CardButtonsShown", akari)! && Read<int>(hub, "_cardBtn") == 0,
                "(a) the selected voice card shows [dive] [reply] with dive focused");
            Check((bool)Call(hub, "CanReplySel")!, "akari's card can be replied to");
            var dive = (Rect2)Call(hub, "CardBtnRect", akari, 0)!;
            var reply = (Rect2)Call(hub, "CardBtnRect", akari, 1)!;
            var card = (Rect2)Call(hub, "CardHitRect", akari)!;
            Check(card.Encloses(dive) && card.Encloses(reply) && !dive.Intersects(reply) && dive.End.X < reply.Position.X,
                "card buttons sit side by side inside the card");
            await Shot("sns_card_buttons");
            await ShotZoom("hint_zoom", hub.HintBarRect);

            // ←→ はボタン切り替え（フッタへは降りない）。
            await Keypress(Key.Right);
            Check(Read<int>(hub, "_cardBtn") == 1 && Read<int>(hub, "_footSel") < 0, "(a) → moves to [reply] and stays on the card");
            await Keypress(Key.Left);
            Check(Read<int>(hub, "_cardBtn") == 0 && Read<int>(hub, "_footSel") < 0, "← moves back to [dive]");
            await Keypress(Key.Right);
            Check(Read<int>(hub, "_cardBtn") == 1, "→ selects [reply] again");
            await Shot("sns_reply_focus");

            // (d) 旧ショートカットは無反応（C は返信できる状態でも開かない）。
            foreach (var k in new[] { Key.J, Key.C, Key.T, Key.R })
            {
                await Keypress(k);
                Check(Mode(hub) == "Cards" && !Read<bool>(hub, "_dived") && GetTree().CurrentScene == hub
                    && IsInstanceValid(hub) && !game.HasReplied(GameManager.FirstStageId),
                    $"(d) {k} does nothing on the timeline");
            }

            // (a) Z で返信の会話が開く。会話中はヒント帯を出さない。
            await Keypress(Key.Z);
            Check(Mode(hub) == "Dialogue" && Read<string?>(hub, "_dlgReplyId") == GameManager.FirstStageId,
                "(a) Z on [reply] opens the reply conversation");
            await Frames(3);
            Check(!hub.HintBarRect.HasArea(), "(e) no hint bar while the reply conversation is open");
            if (_shots || DisplayServer.GetName() != "headless")
                Check(!hub.HintBarDrawnRect.HasArea(), "(e) the hint bar is not drawn during the conversation");
            for (int guard = 0; guard < 80 && Mode(hub) == "Dialogue"; guard++) await Keypress(Key.Z);
            Check(Mode(hub) == "Cards" && game.HasReplied(GameManager.FirstStageId), "reply completes and returns to the timeline");
            await Frames(3);
            Check(Read<int>(hub, "_cardBtn") == 0, "a used reply drops the cursor back to [dive]");
            await Keypress(Key.Right);
            Check(Read<int>(hub, "_cardBtn") == 0 && Read<int>(hub, "_footSel") < 0, "a dimmed [reply] cannot be selected");
            await Shot("sns_reply_dimmed");

            // (b) [ダイブ] → 投稿詳細（難易度選択）。
            await Keypress(Key.Z);
            Check(Mode(hub) == "Detail" && !Read<bool>(hub, "_dived"), "(b) Z on [dive] opens the difficulty sheet");
            await CheckHint(hub, "detail", "↑↓", "もどる");
            await Keypress(Key.J);
            Check(Mode(hub) == "Detail", "(d) J does nothing on the difficulty sheet");
            await Keypress(Key.Escape);
            Check(Mode(hub) == "Cards", "Esc closes the difficulty sheet");

            // (c) 最後のカードで ↓ → フッタ「アカウント」 → Z。
            int last = Read<Array>(hub, "_entries").Length - 1;
            for (int guard = 0; guard < 60 && Read<int>(hub, "_sel") < last; guard++) await Keypress(Key.Down);
            Check(Read<int>(hub, "_sel") == last && Read<int>(hub, "_footSel") < 0, "arrows reach the last card");
            await Keypress(Key.Down);
            Check(Read<int>(hub, "_footSel") == 1, "(c) ↓ on the last card lands on the footer account item");
            await CheckHint(hub, "footer", "←→", "けってい");
            await Shot("footer_focus");
            await Keypress(Key.Left);
            Check(Read<int>(hub, "_footSel") == 0, "← on the footer moves to home");
            await Keypress(Key.Right);
            Check(Read<int>(hub, "_footSel") == 1, "→ on the footer moves back to account");
            await Keypress(Key.Z);
            Check(Mode(hub) == "Job", "(c) Z on the footer account opens account switching");
            await Seconds(0.3);   // シートの展開（入力ゲート）を待つ
            await Keypress(Key.X);
            Check(Mode(hub) == "Cards", "X closes account switching");
            await Keypress(Key.Up);
            Check(Read<int>(hub, "_footSel") < 0 && Read<int>(hub, "_sel") == last, "↑ from the footer returns to the last card");

            // (e) パッド表記：十字／A／B／≡。
            SetPad(true);
            await Frames(3);
            var items = ((string token, string label)[])Call(hub, "HintItems")!;
            var tokens = new List<string>();
            foreach (var it in items) tokens.Add(it.token);
            Check(tokens.SequenceEqual(new[] { "十字", "A", "B", "≡" }), $"(e) pad hint tokens are 十字/A/B/≡ ({string.Join(",", tokens)})");
            for (int guard = 0; guard < 40 && Read<int>(hub, "_sel") != akari; guard++)
                await Keypress(Read<int>(hub, "_sel") < akari ? Key.Down : Key.Up);
            SetPad(true);   // 矢印キーでキーボード表記へ戻らないよう、撮る直前にもう一度立てる
            await Frames(3);
            await ShotZoom("hint_zoom_pad", hub.HintBarRect);
            SetPad(false);

            // X／Esc でホームへ。
            await Keypress(Key.Escape);
            Check(Mode(hub) == "Home", "Esc on the timeline returns home");

            GD.Print("[HubKeysQA] ALL PASS");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[HubKeysQA] FAIL {ex.Message}");
            GD.PrintErr(ex.StackTrace ?? "");
            GetTree().Quit(1);
        }
    }

    // ヒント帯：いまのモードで出ていて、画面の下端・右寄せに収まり、期待した先頭トークンとラベルを持つ。
    private async Task CheckHint(Hub hub, string where, string firstToken, string label)
    {
        await Frames(3);
        var r = hub.HintBarRect;
        var screen = new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH);
        Check(r.HasArea() && screen.Encloses(r) && r.End.Y > UiKit.DesignH - 24f && r.End.X > UiKit.DesignW - 24f,
            $"(e) {where}: hint bar sits at the bottom-right edge ({r})");
        var items = ((string token, string label)[])Call(hub, "HintItems")!;
        Check(items.Length > 0 && items[0].token == firstToken && Array.Exists(items, i => i.label == label)
            && items[^1].label == "メニュー", $"(e) {where}: hint bar reads [{firstToken}] … {label} … [M] メニュー");
        // ヘッドレスでは _Draw が走らないことがあるので、描かれた矩形の確認は窓ありのときだけ。
        if (DisplayServer.GetName() != "headless")
            Check(hub.HintBarDrawnRect.HasArea(), $"(e) {where}: hint bar is drawn");
    }

    private static int IndexOf(Hub hub, string id)
    {
        var entries = Read<Array>(hub, "_entries");
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries.GetValue(i)!;
            if ((string)e.GetType().GetField("Id")!.GetValue(e)! == id) return i;
        }
        return -1;
    }

    private static void SetPad(bool pad)
    {
        typeof(Pad).GetField("_autoUsingPad", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, pad);
        typeof(Pad).GetField("_usingMouse", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
    }

    private async Task Keypress(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Frames(4);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(3);
    }

    private async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (!_shots) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        string path = $"{_out}/{name}.png";
        Check(image.SavePng(path) == Error.Ok, $"screenshot {path}");
    }

    // 設計座標の矩形のまわりを切り抜いて 3 倍に拡大して保存する（ヒント帯の読みやすさの確認用）。
    private async Task ShotZoom(string name, Rect2 design)
    {
        if (!_shots) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        float k = image.GetWidth() / UiKit.DesignW;
        var grown = design.Grow(10f).Intersection(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH));
        var px = new Rect2I((int)(grown.Position.X * k), (int)(grown.Position.Y * k), (int)(grown.Size.X * k), (int)(grown.Size.Y * k));
        var crop = image.GetRegion(px);
        crop.Resize(crop.GetWidth() * 3, crop.GetHeight() * 3, Image.Interpolation.Nearest);
        string path = $"{_out}/{name}.png";
        Check(crop.SavePng(path) == Error.Ok, $"screenshot {path}");
    }
}
