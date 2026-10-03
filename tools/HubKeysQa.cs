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
//   (g) 先頭のカードで ↑ → ヘッダー（使用中のアカウント）にフォーカス → Z でアカウント切り替え（フッタ経由と同じ状態）。
//       ↓ で先頭のカードへ戻る。ヘッダーにいる間はヒント帯の先頭が [↓] もどる に変わり、カードのボタンは消える
//   (h) 全クリア後の FINAL カード（ミナ自身の投稿）は [ダイブ] だけ（返信なし）。Z → 詳細 → Z で FINAL へ潜る
//   (i)〜(iv) 左下のキャラ切り替えボタン（スマホの左隣・2026-09-27）
//   (i)  ホームで Tab → アカウント切り替え、もう一度 Tab で閉じる（写真アプリでも同じ）。Tab で会話ログは開かない（L は開く）
//   (ii) SNS のカード上・投稿詳細でも Tab で開き、Tab で元の画面へ戻る
//   (iii) 会話中は Tab でもボタンでも開かない（ボタンは薄い）
//   (iv) ボタンの矩形は左下（x<400・y>600）、仲間のアバター列も x<400 に収まる。クリック（PressSwitchButton）で開閉
//   窓ありで `-- --hk-shot` を付けると build/shots_hub_keys/ に sns_card_buttons／footer_focus／home／
//   header_focus／final_card_focus とヒント帯の3倍切り抜き hint_zoom（パッド表記は hint_zoom_pad）、
//   キャラ切り替えボタンの switch_button／switch_button_hover／switch_button_dialogue／switch_open_home／
//   switch_open_photos と3倍切り抜き switch_button_zoom／switch_button_hover_zoom／switch_button_zoom_pad を保存する。
public partial class HubKeysQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static T Prop<T>(object obj, string name) => (T)obj.GetType().GetProperty(name, Private)!.GetValue(obj)!;
    private static object? CallStatic(string name, params object[] args) =>
        typeof(Hub).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
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
            // 強化ショップの初回説明は既読にする。あかりをクリア済みにした瞬間 ShopUnlocked が立ち、
            //   ホームが説明の会話を始めてしまう（CompanionDialogueQa と同じ扱い）。
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
            Check(!pause.ShowHint, "(f) Hub hides the PauseMenu corner chip (the hint bar carries M)");

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
            await Frames(30);
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
            // (iii) 会話中は Tab でキャラ切り替えを開かない。ハブでは Tab で会話ログも開かない（ログは L）。
            //   ボタンは薄く（SwitchAvailable が偽）、押しても何も起きない。
            var backlog = GetNode<Backlog>("/root/Backlog");
            Check(!Prop<bool>(hub, "SwitchAvailable"), "(iii) the switch button is disabled during the conversation");
            await Keypress(Key.Tab);
            Check(Mode(hub) == "Dialogue" && !backlog.IsOpen && !GetTree().Paused,
                "(iii) Tab during the conversation opens neither account switching nor the log");
            Check(!(bool)Call(hub, "PressSwitchButton")! && Mode(hub) == "Dialogue",
                "(iii) pressing the switch button during the conversation does nothing");
            await Shot("switch_button_dialogue");
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
            // フッタ経由で開いたアカウント切り替えの状態（戻り先・カーソル）を控えておく＝ヘッダー経由と比べる。
            string footJobReturn = Read<object>(hub, "_jobReturnMode").ToString()!;

            // (g) 先頭のカードで ↑ → ヘッダー。
            for (int guard = 0; guard < 60 && Read<int>(hub, "_sel") > 0; guard++) await Keypress(Key.Up);
            Check(Read<int>(hub, "_sel") == 0 && !Read<bool>(hub, "_headFocus"), "arrows reach the first card (not the header yet)");
            await Keypress(Key.Up);
            Check(Read<bool>(hub, "_headFocus") && Read<int>(hub, "_sel") == 0 && Read<int>(hub, "_footSel") < 0,
                "(g) ↑ on the first card focuses the header account");
            Check(!(bool)Call(hub, "CardButtonsShown", 0)!, "(g) the card selection is hidden while the header is focused");
            await CheckHint(hub, "header", "↓", "アカウント切り替え");
            var headItems = ((string token, string label)[])Call(hub, "HintItems")!;
            Check(headItems[0] == ("↓", "もどる") && headItems[1] == ("Z", "アカウント切り替え") && headItems[2] == ("Esc", "ホームへ"),
                "(g) header hint reads [↓] もどる [Z] アカウント切り替え [Esc] ホームへ [M] メニュー");
            await Shot("header_focus");
            await Keypress(Key.Up);
            Check(Read<bool>(hub, "_headFocus") && Read<int>(hub, "_sel") == 0, "(g) ↑ on the header stays on the header (no wrap)");
            await Keypress(Key.Left);
            Check(Read<bool>(hub, "_headFocus") && Mode(hub) == "Cards", "(g) ← on the header does nothing");
            await Keypress(Key.Z);
            Check(Mode(hub) == "Job" && Read<object>(hub, "_jobReturnMode").ToString() == footJobReturn,
                $"(g) Z on the header opens account switching (return mode {footJobReturn}, same as the footer)");
            await Seconds(0.3);
            await Keypress(Key.X);
            Check(Mode(hub) == "Cards" && Read<bool>(hub, "_headFocus"), "X closes account switching back onto the header");
            await Keypress(Key.Down);
            Check(!Read<bool>(hub, "_headFocus") && Read<int>(hub, "_sel") == 0 && Read<int>(hub, "_footSel") < 0,
                "(g) ↓ on the header returns to the first card");
            await CheckHint(hub, "sns after header", "↑↓", "ボタン");

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

            // ── (i)〜(iv) 左下のキャラ切り替えボタン（Tab／パッド Y／クリック）──
            // (iv) ボタンとアバター列・選択キャラ名がスマホ本体の左に収まる。
            var sw = (Rect2)CallStatic("SwitchButtonRect")!;
            Check(sw.HasArea() && sw.Position.X >= 0f && sw.End.X < 400f && sw.Position.Y > 600f && sw.End.Y <= UiKit.DesignH,
                $"(iv) the switch button sits at the bottom-left, left of the phone ({sw})");
            int allJobs = Jobs.All.Length;
            foreach (var job in Jobs.All)
                for (int count = 1; count <= allJobs; count++)
                {
                    float labelEnd = (float)CallStatic("CompanionLabelX", count)! + UiKit.TextW(UiKit.Zen, job.CharacterName, 13);
                    Check(labelEnd < 400f, $"(iv) {job.CharacterName} with {count} avatars fits left of the phone (x {labelEnd:0.0})");
                }
            Check(Prop<bool>(hub, "SwitchAvailable"), "(iv) the switch button is live on home");
            await Frames(3);
            await Shot("switch_button");
            var swArea = new Rect2(sw.Position, new Vector2(399f - sw.Position.X, sw.Size.Y));
            await ShotZoom("switch_button_zoom", swArea);
            if (_shots)
            {
                // ホバー：マウスをボタンの中央へ置く（窓ありのときだけ。ヘッドレスではカーソルが動かない）。
                //   マウス座標の取り込み（Pad.PollMouse）は PauseMenu が毎フレーム回すが、この QA は PauseMenu を止めているので自前で回す。
                GetViewport().WarpMouse(sw.GetCenter() * UiKit.Scale);
                await PollMouseFrames(4);
                typeof(Pad).GetField("_usingMouse", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
                await PollMouseFrames(4);
                Check(Prop<bool>(hub, "SwitchHovered"), "(iv) the mouse over the button hovers it");
                await Shot("switch_button_hover");
                await ShotZoom("switch_button_hover_zoom", swArea);
                GetViewport().WarpMouse(new Vector2(640f, 90f) * UiKit.Scale);
                await PollMouseFrames(4);
                SetPad(true);
                await Frames(3);
                await ShotZoom("switch_button_zoom_pad", swArea);
                SetPad(false);
                await Frames(3);
            }
            // (iv) クリック＝判定関数を直接呼ぶ（ヘッドレスではマウスの位置が取れない）。もう一度で閉じる。
            Check((bool)Call(hub, "PressSwitchButton")! && Mode(hub) == "Job" && Read<object>(hub, "_jobReturnMode").ToString() == "Home",
                "(iv) clicking the switch button opens account switching over home");
            await Seconds(0.3);
            Check((bool)Call(hub, "PressSwitchButton")! && Mode(hub) == "Home", "(iv) clicking it again closes back to home");

            // (i) ホームで Tab → 切り替え画面（ログは開かない）。もう一度 Tab で閉じる。
            await Keypress(Key.Tab);
            Check(Mode(hub) == "Job" && Read<object>(hub, "_jobReturnMode").ToString() == "Home" && !backlog.IsOpen,
                "(i) Tab on home opens account switching (not the log)");
            await Seconds(0.3);
            await Shot("switch_open_home");
            await Keypress(Key.Tab);
            Check(Mode(hub) == "Home" && !backlog.IsOpen, "(i) Tab again closes account switching back to home");

            // 写真アプリでも Tab で開いて閉じる（戻り先は写真アプリ）。
            for (int guard = 0; guard < 6 && Read<int>(hub, "_homeSel") != 3; guard++) await Keypress(Key.Right);
            await Keypress(Key.Z);
            Check(Mode(hub) == "Photos", "the photos app opens from home");
            await Seconds(0.3);
            await Keypress(Key.Tab);
            Check(Mode(hub) == "Job" && Read<object>(hub, "_jobReturnMode").ToString() == "Photos", "Tab in the photos app opens account switching");
            await Seconds(0.3);
            await Shot("switch_open_photos");
            await Keypress(Key.Tab);
            Check(Mode(hub) == "Photos", "Tab again returns to the photos app");
            await Keypress(Key.Escape);
            Check(Mode(hub) == "Home", "Esc leaves the photos app");
            for (int guard = 0; guard < 6 && Read<int>(hub, "_homeSel") != 0; guard++) await Keypress(Key.Left);

            // (ii) SNS のカード上でも Tab で開く（戻り先はタイムライン・カードの選択はそのまま）。投稿詳細からも開く。
            await Keypress(Key.Z);
            for (int i = 0; i < 600 && Mode(hub) == "SnsOpening"; i++) await Frames(1);
            await Seconds(0.4);
            Check(Mode(hub) == "Cards", "(ii) SNS timeline is open again");
            for (int guard = 0; guard < 40 && Read<int>(hub, "_sel") != akari; guard++)
                await Keypress(Read<int>(hub, "_sel") < akari ? Key.Down : Key.Up);
            await Keypress(Key.Tab);
            Check(Mode(hub) == "Job" && Read<object>(hub, "_jobReturnMode").ToString() == "Cards" && !backlog.IsOpen,
                "(ii) Tab on a timeline card opens account switching");
            await Seconds(0.3);
            await Keypress(Key.Tab);
            Check(Mode(hub) == "Cards" && Read<int>(hub, "_sel") == akari, "(ii) Tab again returns to the same card");
            await Keypress(Key.Z);
            Check(Mode(hub) == "Detail", "(ii) Z on [dive] opens the difficulty sheet");
            await Seconds(0.3);
            await Keypress(Key.Tab);
            Check(Mode(hub) == "Job" && Read<object>(hub, "_jobReturnMode").ToString() == "Detail", "(ii) Tab on the difficulty sheet opens account switching");
            await Seconds(0.3);
            await Keypress(Key.Tab);
            Check(Mode(hub) == "Detail", "(ii) Tab again returns to the difficulty sheet");
            await Keypress(Key.Escape);
            await Keypress(Key.Escape);
            Check(Mode(hub) == "Home", "back to home");
            // L は従来どおりハブでも会話ログを開く（Tab だけがボタンに回った）。
            await Keypress(Key.L);
            await Frames(3);
            Check(backlog.IsOpen, "L still opens the conversation log on the hub");
            await Keypress(Key.L);
            for (int i = 0; i < 20 && GetTree().Paused; i++) await Frames(1);
            Check(!backlog.IsOpen && !GetTree().Paused, "L closes the log again");
            await Frames(3);

            // ── (h) 全クリア後の FINAL カード ──
            //   本編3面をクリア済みにして Hub を開き直す（カードの並びは _Ready で組まれる）。
            //   FINAL 初挑戦は結び手（ミナ）では潜れない（IsMinaLockedForFinal）ので、あかりのアカウントで入る。
            hub.QueueFree();
            await Frames(2);
            foreach (var st in GameManager.Stages) Read<HashSet<string>>(game, "_cleared").Add(st.Id);
            Check(game.AllStoryCleared, "all story stages cleared");
            game.SelectedJob = Job.Melee;
            Check(game.IsJobUnlocked(Job.Melee), "akari's account is unlocked");
            hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            hub.GetType().GetField("_idleTalkPending", Private)!.SetValue(hub, false);
            SetPad(false);
            await Frames(10);
            await Seconds(0.5);
            Check(Mode(hub) == "Home", "(h) reopened hub starts on the phone home");
            await Keypress(Key.Z);
            for (int i = 0; i < 600 && Mode(hub) == "SnsOpening"; i++) await Frames(1);
            await Seconds(0.4);
            Check(Mode(hub) == "Cards", "(h) SNS timeline is open");
            int fin = IndexOf(hub, "final");
            Check(fin >= 0, "(h) the FINAL card is on the timeline");
            for (int guard = 0; guard < 60 && Read<int>(hub, "_sel") != fin; guard++)
                await Keypress(Read<int>(hub, "_sel") < fin ? Key.Down : Key.Up);
            await Seconds(0.3);   // フィードのスクロールが寄り切るのを待つ
            Check(Read<int>(hub, "_sel") == fin && (bool)Call(hub, "CardButtonsShown", fin)!, "(h) FINAL card focused with buttons shown");
            var fdive = (Rect2)Call(hub, "CardBtnRect", fin, 0)!;
            var fcard = (Rect2)Call(hub, "CardHitRect", fin)!;
            int fcount = (int)hub.GetType().GetMethod("CardBtnCount", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new[] { Read<Array>(hub, "_entries").GetValue(fin)! })!;
            Check(fdive.HasArea() && fcard.Encloses(fdive) && fcount == 1,
                $"(h) FINAL card has a [dive] button inside the card and no [reply] ({fdive}, count {fcount})");
            Check(fdive.End.X >= fcard.End.X - 30f, "(h) the lone [dive] sits at the right edge of the card");
            await Keypress(Key.Right);
            Check(Read<int>(hub, "_cardBtn") == 0, "(h) → keeps the cursor on [dive] (no reply on FINAL)");
            await Shot("final_card_focus");
            await Keypress(Key.Z);
            Check(Mode(hub) == "Detail", "(h) Z on FINAL [dive] opens the FINAL sheet");
            await Keypress(Key.Z);
            Check(Read<bool>(hub, "_dived") && game.PendingStageScene == "res://MinaBattle.tscn",
                "(h) Z on the FINAL sheet starts the FINAL dive (MinaBattle.tscn)");

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

    private async Task PollMouseFrames(int count)
    {
        for (int i = 0; i < count; i++) { Pad.PollMouse(GetViewport()); await Frames(1); }
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
