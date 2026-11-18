using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class HubJobQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    private static void Write(object obj, string name, object value) => obj.GetType().GetField(name, Private)!.SetValue(obj, value);
    private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private)!.Invoke(obj, args);
    private static string Mode(Hub hub) => Read<object>(hub, "_mode").ToString()!;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print($"[HubJobQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            foreach (var job in Jobs.All) game.MarkIdleDialogSeen($"once_companion_select_{job.CharacterId}");
            game.Difficulty = GameManager.Diff.Normal;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            // Keep the desktop cursor outside this deterministic input test.
            GetNode<PauseMenu>("/root/PauseMenu").SetProcess(false);
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--unlocks") >= 0)
            {
                await CheckRescueReturns(game);
                await Finish();
                return;
            }
            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            await Frames(30);
            bool allCards = OS.GetCmdlineUserArgs().Contains("--companion-cards");
            if (allCards || OS.GetCmdlineUserArgs().Contains("--akari-card"))
            {
                await Keypress(Key.Tab);
                Check(Mode(hub) == "Job" && Read<VideoStreamPlayer?>(hub, "_unlockMovie") == null, "new game has no arrival card");
                foreach (var job in Jobs.All.Where(job => job.UnlockStageId.Length > 0 && (allCards || job.Id == Job.Melee)))
                    hub = await CheckUnlockCard(hub, game, job);
                if (allCards) CheckPendingCards(game);
                await Finish();
                return;
            }
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--feed") >= 0)
            {
                await CheckProgressiveFeed(hub, game);
                await Finish();
                return;
            }
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--sns") >= 0)
            {
                await CheckSocialFeed(hub, game);
                await Finish();
                return;
            }
            bool introMovie = Array.IndexOf(OS.GetCmdlineUserArgs(), "--intro-movie") >= 0;
            Check(Mode(hub) == "Home", "new game starts on the phone home before opening SNS");
            Check(!game.IsIdleDialogSeen("once_phone_home"), "starting on home does not consume the first rescue reward reveal");
            var phone = new Rect2(400, 0, 480, 720);
            for (int i = 0; i < 4; i++)
            {
                Check(phone.Encloses((Rect2)Call(hub, "HomeAppRect", i)!), "app icons stay inside the phone screen");
                Check((bool)Call(hub, "HomeAppUnlocked", i)! == (i == 0 || i == 3), "new data can open SNS and photos but keeps shop and records locked");
            }
            await Shot("home_first_visit");
            if (!introMovie) await CheckSidePresentation(hub, phone);
            if (!introMovie)
            {
                DisplayServer.WindowSetSize(new Vector2I(960, 540));
                await Frames(20);
                await Shot("home_first_visit_small");
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                await Frames(20);
            }
            else await Frames(120);
            long initialImpression = game.Impression, initialFollowers = game.Followers;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = true });
            Click(hub, (Rect2)Call(hub, "HomeAppRect", 0)!, "ProcessHome");
            Check(Mode(hub) == "SnsOpening", "SNS opens with an app launch transition");
            await Frames(8);
            await Shot("sns_opening_start");
            await Frames(12);
            await Shot("sns_opening_expand");
            await Frames(58);
            Check(Mode(hub) == "Dialogue" && Read<int>(hub, "_dlgIdx") == 0 && Read<int>(hub, "_dlgPage") == 0,
                "holding confirm through app launch does not skip the purpose explanation");
            Check(Read<bool>(hub, "_dlgNoPost") && !Read<bool>(hub, "_dived"), "the introduction neither posts nor enters combat");
            var introPost = (Rect2)Call(hub, "CardHitRect", Read<int>(hub, "_sel"))!;
            Check(introPost.Position.Y >= 128 && introPost.End.Y < 428, "the first voice remains visible above the purpose dialogue");
            Check(!game.IsIdleDialogSeen("once_sns_intro"), "the explanation stays unread until it is completed");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Z, Pressed = false });
            await Frames(3);
            foreach (var (_, line) in Read<(string, string)[]>(hub, "_dlg"))
                foreach (string page in UiKit.Paginate(UiKit.Zen, line, UiKit.FontHeading, 432f, Hud.DlgMaxLines))
                    Check(UiKit.WrapLines(UiKit.Zen, page, UiKit.FontHeading, 432f).Count <= Hud.DlgMaxLines,
                        "the SNS purpose dialogue fits its two-line pages");
            int introPages = 0;
            while (Mode(hub) == "Dialogue" && introPages < 20)
            {
                await Frames(introMovie ? 180 : 60);
                await Shot($"sns_intro_{introPages}");
                await Keypress(Key.Z);
                introPages++;
            }
            Check(Mode(hub) == "Cards" && game.IsIdleDialogSeen("once_sns_intro"), "reading the guide returns to SNS and remembers completion");
            Check(game.Impression == initialImpression && game.Followers == initialFollowers && Read<double>(hub, "_toastT") == 0,
                "reading the guide awards no post rewards or notification");
            game.ResetIdleDialogSeen();
            Check(game.IsIdleDialogSeen("once_sns_intro"), "small-talk resets preserve the SNS introduction");
            Check(game.LoadFromSlot(0) && game.IsIdleDialogSeen("once_sns_intro"), "SNS introduction completion is saved");
            await Frames(30);
            Check(Read<float>(hub, "_feedScrollTarget") <= (float)Call(hub, "FeedMaxScroll")!,
                "closing the guide restores the full feed scroll range");
            await Shot("sns_first_selection");
            if (introMovie)
            {
                await Frames(90);
                await Finish();
                return;
            }
            int initialStage = Read<int>(hub, "_sel");
            var initialFooter = (IList)Call(hub, "FooterItems")!;
            Check(initialFooter.Count == 2, "home replaces dive without duplicating navigation");
            await Keypress(Key.X);
            Check(Mode(hub) == "Home" && Read<int>(hub, "_sel") == initialStage, "back returns home without changing the selected post");
            await Keypress(Key.Z);
            Check(Mode(hub) == "SnsOpening", "keyboard confirmation opens the SNS app");
            await Frames(45);
            Check(Mode(hub) == "Cards", "opening SNS again does not repeat the explanation");
            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            await Frames(20);
            await Shot("first_timeline_small");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(20);
            await Shot("cards");
            await CheckScroll(hub);
            Check(phone.Encloses((Rect2)Call(hub, "HeaderJobRect")!), "character switch stays inside the phone column");
            var initialEntries = Read<IList>(hub, "_entries");
            var feed = new Rect2(400, 128, 480, 518);
            for (int i = 0; i < initialEntries.Count; i++)
            {
                var hit = (Rect2)Call(hub, "CardHitRect", i)!;
                Check(!hit.HasArea() || feed.Encloses(hit), "post hit areas are clipped above navigation and below the header");
            }
            Click(hub, (Rect2)Call(hub, "HeaderJobRect")!, "ProcessCards");
            Check(Mode(hub) == "Job", "header button opens the available accounts");
            Check(Read<JobTuning[]>(hub, "_jobChoices").Length == 1
                && Read<JobTuning[]>(hub, "_jobChoices")[0].Id == Job.Tank, "new data shows only Mina without locked rows");
            foreach (var job in Jobs.All)
            {
                var face = Read<Dictionary<string, Texture2D>>(hub, "_playerFaces")[job.CharacterId];
                Check(face.ResourcePath == CompanionDialogue.AccountPortrait(job.Id),
                    $"{job.CharacterId} dedicated SNS icon is available before stage clears");
                var box = ((float x, float y, float w, float h))Call(hub, "JobBox")!;
                string stats = (string)typeof(Hub).GetMethod("JobStats", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { job })!;
                // 2026-09-27: 段の1行は「撃ち方／火力」に変わり、♥・移動・回避は下の詳細パネルが持つ。
                Check(stats == $"{Jobs.ModeName(job.Mode)}／火力 {Jobs.PowerLabel(job)}"
                    && UiKit.TextW(UiKit.Zen, stats, 12) <= box.w - 124f,
                    $"{job.CharacterId} shows its shot mode and firepower on the row in one line");
                string handle = (string)typeof(Hub).GetMethod("AccountHandle", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { job })!;
                string expectedHandle = job.Id == Job.Tank ? Handles.Mina : Array.Find(GameManager.Stages, s => s.Id == job.CharacterId)!.Handle;
                Check(handle == expectedHandle && UiKit.TextW(UiKit.Mono, handle, 11) <= 96f,
                    $"{job.CharacterId} uses its timeline handle in the compact account button");
                Check(phone.Encloses(new Rect2(box.x, box.y, box.w, box.h)), "account sheet stays inside the phone column");
            }
            await Frames(20);
            await Shot("types");
            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            await Frames(20);
            await Shot("accounts_mina_small");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await KeyAction("ui_down");
            await KeyAction("ui_up");
            Check(Read<int>(hub, "_jobSel") == 0, "keyboard navigation cannot reach an unrescued account");
            foreach (var job in Jobs.All)
            {
                bool free = job.UnlockStageId.Length == 0;
                Check(game.IsJobUnlocked(job.Id) == free, $"{job.CharacterId} starts {(free ? "unlocked" : "locked")} on new data");
                Check((game.JobUnlockHint(job.Id) == null) == free,
                    $"{job.CharacterId} {(free ? "needs no unlock line" : "explains the stage that opens it")}");
            }
            var clearedStages = Read<HashSet<string>>(game, "_cleared");
            for (int i = 0; i < GameManager.Stages.Length; i++)
            {
                await Keypress(Key.X);
                clearedStages.Add(GameManager.Stages[i].Id);
                await OpenAccountsByKeys(hub);
                var choices = Read<JobTuning[]>(hub, "_jobChoices");
                Check(choices.Length == i + 2 && choices[^1].CharacterId == GameManager.Stages[i].Id,
                    "each rescue adds exactly its character to the account list");
                var box = ((float x, float y, float w, float h))Call(hub, "JobBox")!;
                Check(phone.Encloses(new Rect2(box.x, box.y, box.w, box.h)), "account sheet grows within the phone screen");
                for (int row = 0; row < choices.Length; row++)
                    Check(!((Rect2)Call(hub, "JobHitRect", row)!).Intersects((Rect2)Call(hub, "JobConfirmRect")!),
                        "account rows do not overlap the confirmation button");
                await Shot($"accounts_after_{GameManager.Stages[i].Id}");
            }
            foreach (var job in Jobs.All) Check(game.IsJobUnlocked(job.Id), $"{job.CharacterId} opens once every stage is cleared");
            // 既存セーブの移行：解禁は専用キーではなくクリア記録から導くので、読み直すだけで開いている。
            game.SelectedJob = Job.Magic;
            game.SaveToSlot(0);
            game.SelectedJob = Job.Tank;
            Check(game.LoadFromSlot(0) && game.SelectedJob == Job.Magic && game.IsJobUnlocked(Job.Magic),
                "an existing cleared save loads with its jobs already open");
            // --job= のデバッグ起動は解禁を無視する（クリア記録が空でも全ジョブに届く）。
            clearedStages.Clear();
            Check(!game.IsJobUnlocked(Job.Magic), "clearing the record locks the job again");
            game.JobForcedByCmdline = true;
            foreach (var job in Jobs.All) Check(game.IsJobUnlocked(job.Id), $"--job= reaches {job.CharacterId} without any clear");
            game.JobForcedByCmdline = false;
            // クリア記録と食い違うジョブが保存されていたら、ロードで結び手へ落とす（解禁前の旧データの保険）。
            game.SaveToSlot(0);
            Check(game.LoadFromSlot(0) && game.SelectedJob == Job.Tank, "a save whose job is not yet earned falls back to the starting job");
            foreach (var item in GameManager.Stages) clearedStages.Add(item.Id);
            Call(hub, "OpenJob");
            Write(hub, "_toastT", 0d);
            await Frames(20);
            await Shot("types_unlocked");
            Click(hub, (Rect2)Call(hub, "JobHitRect", 1)!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Cards" && game.SelectedJob == Job.Melee, "header selection applies the job and returns to cards");
            await Frames(10);

            await Keypress(Key.Z);
            Check(Mode(hub) == "Detail", "stage selection opens its difficulty screen");
            await KeyAction("ui_down");
            int stage = Read<int>(hub, "_sel"), tier = Read<int>(hub, "_tierSel");
            Check(tier == (int)GameManager.Diff.Hard, "difficulty can be chosen before changing type");
            Check(!((Rect2)Call(hub, "DetailJobRect", true)!).Intersects((Rect2)Call(hub, "TierHitRect", 3)!),
                "type button does not overlap difficulty rows");
            Check(phone.Encloses((Rect2)Call(hub, "TierHitRect", 3)!)
                && !((Rect2)Call(hub, "DetailCloseRect", true)!).Intersects((Rect2)Call(hub, "TierHitRect", 3)!),
                "direct-entry difficulty rows stay inside the phone and clear of navigation");
            await Frames(240);
            await Shot("detail");
            for (int i = 0; i < Jobs.All.Length; i++)
            {
                Click(hub, (Rect2)Call(hub, "DetailJobRect", true)!, "ProcessDetail", 0.01);
                Check(Mode(hub) == "Job", "detail button opens job selection");
                await Frames(20);
                Click(hub, (Rect2)Call(hub, "JobHitRect", i)!, "ProcessJob", 0.01);
                Check(Mode(hub) == "Detail" && game.SelectedJob == Jobs.All[i].Id
                    && game.SelectedShotMode == Jobs.All[i].Mode, $"{Jobs.All[i].Id} applies without starting combat");
                Check(Read<int>(hub, "_sel") == stage && Read<int>(hub, "_tierSel") == tier
                    && !Read<bool>(hub, "_dived"), "stage and difficulty remain selected");
                await Frames(20);
            }
            // 2026-09-27：J の直行は廃止（詳細からはボタンのクリックだけ）。J は何も起こさない。
            await Keypress(Key.J);
            Check(Mode(hub) == "Detail", "J no longer opens types from the detail screen");
            Click(hub, (Rect2)Call(hub, "DetailJobRect", true)!, "ProcessDetail", 0.01);
            Check(Mode(hub) == "Job", "the detail button opens types again");
            await Frames(20);
            await KeyAction("ui_down");
            await Keypress(Key.X);
            Check(Mode(hub) == "Detail" && game.SelectedJob == Job.Magic, "keyboard cancel keeps the selected job");
            Click(hub, (Rect2)Call(hub, "DetailJobRect", true)!, "ProcessDetail", 0.01);
            await Frames(20);
            Click(hub, (Rect2)Call(hub, "JobCloseRect")!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Detail", "close button returns to the originating detail screen");
            await Frames(10);
            Click(hub, (Rect2)Call(hub, "DetailJobRect", true)!, "ProcessDetail", 0.01);
            await Frames(20);
            game.JobForcedByCmdline = true;
            await KeyAction("ui_down");
            await Keypress(Key.Z);
            Check(Mode(hub) == "Detail" && game.SelectedJob == Job.Magic, "debug job lock preserves the detail screen and job");
            game.JobForcedByCmdline = false;
            await Frames(240);

            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            await Frames(20);
            await Shot("detail_small");
            Click(hub, (Rect2)Call(hub, "DetailJobRect", true)!, "ProcessDetail", 0.01);
            await Frames(20);
            await Shot("types_small");
            await Keypress(Key.X);
            await Keypress(Key.X);
            Check(Mode(hub) == "Cards", "detail still closes to stage selection");
            await Shot("cards_small");
            await OpenAccountsByKeys(hub);
            Check(Mode(hub) == "Job", "the footer account item opens types from the card screen");
            await Keypress(Key.X);
            Check(Mode(hub) == "Cards", "closing the footer-opened types returns to cards");

            foreach (var job in Jobs.All)
            {
                int index = Array.FindIndex(Jobs.All, entry => entry.Id == job.Id);
                Click(hub, (Rect2)Call(hub, "HeaderJobRect")!, "ProcessCards");
                await Frames(20);
                Click(hub, (Rect2)Call(hub, "JobHitRect", index)!, "ProcessJob", 0.01);
                Check(Mode(hub) == "Cards" && game.JobDef.CharacterId == job.CharacterId,
                    $"{job.CharacterId} becomes the active timeline account");
                Check(Read<string>(hub, "_toast") == "アカウントを切り替えました"
                    && Read<string>(hub, "_toastSub").Contains(job.CharacterName), "account switch notification names the new user");
                Write(hub, "_toastT", 0d);
                await Frames(20);
                await Shot($"account_{job.CharacterId}");
                Click(hub, (Rect2)Call(hub, "HeaderJobRect")!, "ProcessCards");
                await Frames(20);
                Click(hub, (Rect2)Call(hub, "JobHitRect", index)!, "ProcessJob", 0.01);
                Check(Mode(hub) == "Cards" && Read<double>(hub, "_toastT") == 0d,
                    "choosing the active account returns without another switch notification");
                await Frames(3);
            }

            var cleared = Read<HashSet<string>>(game, "_cleared");
            foreach (var item in GameManager.Stages) cleared.Add(item.Id);
            game.ShopTutorialSeen = true;
            Call(hub, "BuildEntries");
            Call(hub, "LoadFaces");
            var entries = Read<IList>(hub, "_entries");
            var footer = (IList)Call(hub, "FooterItems")!;
            for (int i = 0; i < footer.Count; i++)
                Check(phone.Encloses((Rect2)Call(hub, "FooterItemRect", i)!), "unlocked navigation fits the phone column");
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]!;
                string id = (string)entry.GetType().GetField("Id")!.GetValue(entry)!;
                if (id != "akari" && id != "koharu" && id != "rei" && id != "pinned") continue;
                Write(hub, "_sel", i);
                Call(hub, "UpdateFeedScrollTarget");
                await Frames(40);
                await Shot($"cleared_{id}_small");
            }
            DisplayServer.WindowSetSize(new Vector2I(1920, 1080));
            await Frames(20);
            await Shot("cards_wide");
            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            await Frames(20);
            int final = -1;
            for (int i = 0; i < entries.Count; i++)
                if ((bool)entries[i]!.GetType().GetField("IsFinal")!.GetValue(entries[i])!) final = i;
            Check(final >= 0, "final stage is available for layout verification");
            Write(hub, "_sel", final);
            Call(hub, "UpdateFeedScrollTarget");
            await Keypress(Key.Z);
            Check(Mode(hub) == "Detail", "final stage detail opens normally");
            await Shot("final_detail_small");
            bool finalTiers = !(game.IsMinaLockedForFinal && game.SelectedJob == Job.Tank);
            Click(hub, (Rect2)Call(hub, "DetailJobRect", finalTiers)!, "ProcessDetail", 0.01);
            await Frames(20);
            Click(hub, (Rect2)Call(hub, "JobCloseRect")!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Detail" && Read<int>(hub, "_sel") == final, "final stage job button returns to its detail layout");
            Click(hub, (Rect2)Call(hub, "DetailCloseRect", finalTiers)!, "ProcessDetail", 0.01);
            Click(hub, (Rect2)Call(hub, "FooterItemRect", 0)!, "ProcessCards");
            Check(Mode(hub) == "Home" && !Read<bool>(hub, "_dived"), "former dive navigation returns home instead of starting a stage");
            await Frames(3);
            Write(hub, "_idleTalkPending", false);
            Click(hub, (Rect2)Call(hub, "HomeAppRect", 0)!, "ProcessHome");
            await Frames(45);
            await Keypress(Key.Z);
            Check(Mode(hub) == "Detail", $"opening a post reaches stage controls (mode={Mode(hub)})");
            await Frames(20);
            Click(hub, (Rect2)Call(hub, "DetailJobRect", finalTiers)!, "ProcessDetail", 0.01);
            await Frames(20);
            Click(hub, (Rect2)Call(hub, "JobConfirmRect")!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Detail" && game.SelectedJob == Job.Magic, "character confirmation button applies the highlighted character");
            Click(hub, (Rect2)Call(hub, "TierHitRect", Read<int>(hub, "_tierSel"))!, "ProcessDetail", 0.01);
            await QaSceneTransition.Wait(this);
            Check(GetTree().CurrentScene is MinaRoot, "primary dive button enters the selected stage");
            GetTree().CurrentScene.QueueFree();
            await Frames(5);
            hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            await Frames(30);
            Check(Mode(hub) == "Home", "returning to the hub lands on home without a random conversation");
            Write(hub, "_idleTalkPending", false);
            Click(hub, (Rect2)Call(hub, "HomeAppRect", 0)!, "ProcessHome");
            await Frames(45);
            Check(Mode(hub) == "Cards", "returning players open SNS without the introduction");
            Click(hub, (Rect2)Call(hub, "FooterItemRect", 0)!, "ProcessCards");
            Check(Mode(hub) == "Home", "home navigation is available after rescue");
            for (int i = 0; i < 4; i++) Check((bool)Call(hub, "HomeAppUnlocked", i)!, "cleared stages unlock all existing apps");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(20);
            await Shot("home_unlocked");
            DisplayServer.WindowSetSize(new Vector2I(1920, 1080));
            await Frames(20);
            await Shot("home_wide");
            Click(hub, (Rect2)Call(hub, "HomeAppRect", 1)!, "ProcessHome");
            await QaSceneTransition.Wait(this);
            Check(GetTree().CurrentScene is Shop && game.SelectedJob == Job.Magic, "shop app opens with the active SNS account");
            Call(GetTree().CurrentScene, "ExitShop");
            await Frames(180);
            await QaSceneTransition.Wait(this);
            Check(GetTree().CurrentScene is Hub && Mode((Hub)GetTree().CurrentScene) == "Home", "closing the shop returns home");
            hub = (Hub)GetTree().CurrentScene;
            await KeyAction("ui_right");
            await KeyAction("ui_right");
            await Keypress(Key.Z);
            await QaSceneTransition.Wait(this);
            Check(GetTree().CurrentScene is Records, "keyboard navigation opens the records app");
            await Keypress(Key.X);
            await QaSceneTransition.Wait(this);
            Check(GetTree().CurrentScene is Hub && Mode((Hub)GetTree().CurrentScene) == "Home", "records also returns home");
            GetTree().CurrentScene.QueueFree();
            await Frames(5);
            await CheckRescueReturns(game);
            await Finish();
        }
        catch (Exception ex)
        {
            GD.PushError($"[HubJobQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private async Task<Hub> CheckUnlockCard(Hub hub, GameManager game, JobTuning job)
    {
        var previous = game.SelectedJob;
        Check(!game.IsJobUnlocked(job.Id), $"{job.CharacterId} is locked before the rescue");
        hub.QueueFree();
        await Frames(2);
        game.ShopTutorialSeen = true;
        game.CompleteStage(job.UnlockStageId);
        Check(game.NextUnlockCard?.Id == job.Id && game.SaveToSlot(1), "first clear queues the reveal in this save");
        hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        await Frames(5);
        Check(Mode(hub) == "Dialogue" && Read<VideoStreamPlayer?>(hub, "_unlockMovie") == null,
            "return dialogue precedes the card reveal");
        Call(hub, "EndDialogue");
        await ToSignal(GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
        Check(Mode(hub) == "Home" && game.LoadFromSlot(1) && game.NextUnlockCard?.Id == job.Id
            && (Call(hub, "get_PendingHomeCard") as JobTuning)?.Id == job.Id,
            "unopened reveal survives saving and loading");
        await Shot($"{job.CharacterId}_card_before");
        GD.Print($"[CardQA] {job.CharacterId} open frame {Engine.GetProcessFrames()}");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Tab, Pressed = true });
        await Frames(3);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Tab, Pressed = false });
        var movie = Read<VideoStreamPlayer>(hub, "_unlockMovie");
        Check(Mode(hub) == "Job" && movie.IsPlaying() && movie.Stream.ResourcePath == $"res://assets/video/{job.CharacterId}_unlock.ogv",
            "the character-switch button plays the Resolve card");
        Check(Read<JobTuning[]>(hub, "_jobChoices")[Read<int>(hub, "_jobSel")].Id == job.Id
            && game.SelectedJob == previous, $"{job.CharacterId} has focus without changing the active character");
        await ToSignal(GetTree().CreateTimer(.40), SceneTreeTimer.SignalName.Timeout);
        Check(movie.StreamPosition > .2 && movie.GetVideoTexture().GetSize() == new Vector2(864, 700),
            "card clock advances with the intended artwork dimensions");
        await Shot($"{job.CharacterId}_card_reveal");
        await ToSignal(GetTree().CreateTimer(2.8), SceneTreeTimer.SignalName.Timeout);
        Check(Read<VideoStreamPlayer?>(hub, "_unlockMovie") == null && Mode(hub) == "Job"
            && Read<Texture2D?>(hub, "_unlockPortrait") != null, "the welcome stays visible after playback");
        await Shot($"{job.CharacterId}_card_ready");
        await Keypress(Key.Z);
        Check(game.SelectedJob == job.Id && Mode(hub) == "Home", $"confirm selects {job.CharacterId} on the same screen");
        await Keypress(Key.Tab);
        Check(Mode(hub) == "Job" && Read<VideoStreamPlayer?>(hub, "_unlockMovie") == null
            && Read<Texture2D?>(hub, "_unlockPortrait") == null,
            "reopening does not play the reveal");
        Check(game.LoadFromSlot(0) && game.NextUnlockCard == null, "consumed reveal survives reloading");
        game.CompleteStage(job.UnlockStageId);
        Check(game.NextUnlockCard == null, "replaying the stage does not queue a second reveal");
        await Keypress(Key.Tab);
        Check(game.LoadFromSlot(1) && game.NextUnlockCard?.Id == job.Id, "an earlier slot keeps its own pending reveal");
        Click(hub, new Rect2(990, 198, 100, 34), "ProcessSwitchButton");
        await Frames(12);
        Check(Read<VideoStreamPlayer?>(hub, "_unlockMovie") != null, "clicking the home invitation opens the reveal");
        await Keypress(Key.Z);
        Check(game.SelectedJob == job.Id && Mode(hub) == "Home" && Read<VideoStreamPlayer?>(hub, "_unlockMovie") == null,
            $"confirm during the animation selects {job.CharacterId} immediately and releases the movie");
        return hub;
    }

    private static void CheckPendingCards(GameManager game)
    {
        game.ResetPersistent();
        game.CompleteStage("akari");
        game.CompleteStage("koharu");
        game.CompleteStage("rei");
        Check(game.SaveToSlot(2) && game.LoadFromSlot(2), "unopened cards survive reloading together");
        foreach (var id in new[] { Job.Magic, Job.Heal, Job.Melee })
        {
            Check(game.NextUnlockCard?.Id == id, $"pending card {id} appears in rescue order, newest first");
            game.AcknowledgeUnlockCard(Jobs.Get(id));
        }
        Check(game.NextUnlockCard == null, "viewing all cards clears all home invitations");
        string oldSlot = Startup.DataPath("save_2.json");
        var data = Json.ParseString(FileAccess.GetFileAsString(oldSlot)).AsGodotDictionary();
        data.Remove("pendingUnlockCards");
        foreach (bool showAkari in new[] { true, false })
        {
            data["akariUnlockCardPending"] = showAkari;
            using (var file = FileAccess.Open(oldSlot, FileAccess.ModeFlags.Write)) file.StoreString(Json.Stringify(data));
            Check(game.LoadFromSlot(2) && game.NextUnlockCard?.Id == (showAkari ? Job.Melee : (Job?)null),
                "legacy saves preserve the Akari notice and skip previously rescued characters");
        }
    }

    private async Task CheckRescueReturns(GameManager game)
    {
        game.ResetPersistent();
        game.ShopTutorialSeen = true;
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        foreach (var job in Jobs.All.Where(j => j.UnlockStageId.Length > 0))
        {
            for (int visit = 0; visit < 2; visit++)
            {
                game.CompleteStage(job.UnlockStageId);
                var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
                GetTree().Root.AddChild(hub);
                GetTree().CurrentScene = hub;
                await Frames(10);
                bool quietReturn = visit > 0 && game.AllStoryCleared && game.IsMinaLockedForFinal
                    && CompanionDialogue.MenuLines(game.SelectedJob, CompanionDialogue.Menu.Return).Length == 0;
                Check(Mode(hub) == (quietReturn ? "Home" : "Dialogue"),
                    quietReturn ? "a replay while Mina is unreachable returns directly home" : "rescue conversation precedes the home screen");
                if (visit == 0)
                {
                    Check(!GetTree().Paused && Read<double>(hub, "_toastT") > 0 && Read<string>(hub, "_toast") == $"{job.CharacterName}が使えるようになりました"
                        && Read<string>(hub, "_toastSub").Contains("キャラ切り替え"), job.CharacterId + " notice names the playable character and how to select them");
                    await Shot("unlock_" + job.CharacterId);
                }
                else if (quietReturn)
                    Check(Read<string>(hub, "_toast") == $"{Handles.Mina} の投稿が 8 人に届いた",
                        "quiet replay shows the return reward instead of an unlock notice");
                else Check(Read<double>(hub, "_toastT") == 0, job.CharacterId + " replays do not announce an unlock");
                if (!quietReturn) Call(hub, "EndDialogue");
                if (visit == 0 && job.UnlockStageId == GameManager.FirstStageId)
                {
                    Check(Mode(hub) == "HomeReveal" && Read<Texture2D>(hub, "_homeSnapshot").GetWidth() > 0, "first return captures SNS for the home reveal");
                    await Frames(26);
                    await Shot("home_first_reveal");
                    await Frames(70);
                    await Shot("home_shop_activation");
                    await Frames(60);
                }
                Check(Mode(hub) == "Home", "home reveal completes and does not repeat on replay");
                Check(game.IsIdleDialogSeen("once_phone_home"), "home reveal completion is remembered");
                game.ResetIdleDialogSeen();
                Check(game.IsIdleDialogSeen("once_phone_home"), "small-talk resets do not repeat the reveal");
                Check(game.LoadFromSlot(0) && game.IsIdleDialogSeen("once_phone_home"), "home reveal completion survives save loading");
                if (visit == 0 && job.UnlockStageId == GameManager.FirstStageId) await Shot("home_first_rescue");
                hub.QueueFree();
                await Frames(5);
            }
        }
    }

    private async Task Finish()
    {
        Audio.Instance?.StopMusic(0);
        foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
            if (child is AudioStreamPlayer player)
            {
                player.Stop();
                player.Stream = null;
            }
        // Audio playback cleanup runs on a real-time thread even with --fixed-fps.
        await Task.Delay(250);
        await Frames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GD.Print("[HubJobQA] ALL PASS");
        GetTree().Quit();
    }

    private async Task CheckScroll(Hub hub)
    {
        int selected = Read<int>(hub, "_sel");
        float target = Read<float>(hub, "_feedScrollTarget");
        float maxScroll = (float)Call(hub, "FeedMaxScroll")!;
        Vector2 mouse = Pad.MousePos();
        foreach (var (offset, y) in new[] { (0f, 644f), (Mathf.Min(200f, maxScroll * 0.5f), 130f) })
        {
            Write(hub, "_feedScroll", offset);
            Write(hub, "_feedScrollTarget", offset);
            PadField("_mousePos", new Vector2(620, y));
            PadField("_usingMouse", true);
            await Frames(120);
            Check(Read<float>(hub, "_feedScrollTarget") == offset && Read<float>(hub, "_feedScroll") == offset,
                "hovering a clipped post at the timeline edge never scrolls");
        }
        float beforeWheel = Read<float>(hub, "_feedScrollTarget");
        PadField("_wheelFrame", -1f);
        Call(hub, "ProcessCards");
        PadField("_wheelFrame", 0f);
        await Frames(120);
        Check(Read<float>(hub, "_feedScrollTarget") > beforeWheel
            && Mathf.Abs(Read<float>(hub, "_feedScroll") - Read<float>(hub, "_feedScrollTarget")) < 0.01f,
            "mouse wheel scrolls and never snaps back after idling");
        PadField("_usingMouse", false);
        PadField("_mousePos", mouse);
        Write(hub, "_sel", Read<IList>(hub, "_entries").Count - 2);
        Write(hub, "_feedScrollTarget", 0f);
        await KeyAction("ui_down");
        Check(Read<float>(hub, "_feedScrollTarget") > 0f, "keyboard navigation still brings its selected post into view");
        Write(hub, "_sel", selected);
        Write(hub, "_feedScrollTarget", target);
        await Frames(60);
    }

    private readonly record struct FeedPost(string Id, string Kind, string Name, string Handle, string Text,
        string Time, int Icon);

    private static T EntryField<T>(object entry, string field) => (T)entry.GetType().GetField(field)!.GetValue(entry)!;

    private static FeedPost[] FeedSnapshot(Hub hub) => Read<IList>(hub, "_entries").Cast<object>()
        .Where(entry => EntryField<object>(entry, "Sort").ToString() != "Pinned")
        .Select(entry => new FeedPost(EntryField<string>(entry, "Id"), EntryField<object>(entry, "Sort").ToString()!,
            EntryField<string>(entry, "Name"), EntryField<string>(entry, "Handle"), EntryField<string>(entry, "Tweet"),
            EntryField<string>(entry, "RelT"), EntryField<int>(entry, "Icon")))
        .ToArray();

    private async Task CheckProgressiveFeed(Hub hub, GameManager game)
    {
        FeedPost[] previous = Array.Empty<FeedPost>();
        string[] voices = GameManager.Stages.Select(stage => stage.Id).Append("final").ToArray();
        for (int progress = 0; progress < voices.Length; progress++)
        {
            if (progress > 0)
            {
                hub.QueueFree();
                await Frames(3);
                game.CompleteStage(voices[progress - 1]);
                hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
                GetTree().Root.AddChild(hub);
                GetTree().CurrentScene = hub;
                await Frames(3);
            }
            var feed = FeedSnapshot(hub);
            var entries = Read<IList>(hub, "_entries").Cast<object>().ToArray();
            Check(entries.All(entry => EntryField<bool>(entry, "Unlocked")), "the feed contains no locked post placeholders");
            Check(feed.Where(post => post.Kind == "Voice").Select(post => post.Id)
                .SequenceEqual(voices.Take(progress + 1).Reverse()), $"progress {progress} only contains encountered voices, newest first");
            Check(feed.Length == 9 + progress * 5 && feed.Select(post => post.Id).Distinct().Count() == feed.Length,
                "daily conversations and encountered voices have distinct stable IDs");
            Check(feed.Take(5).Count(post => post.Kind == "Filler") == 4 && feed[2].Id == voices[progress],
                "the next character appears between everyday posts in the new batch");
            Check((progress == 0 || feed.Skip(5).SequenceEqual(previous)), "previous batches retain their text, authors and order after clearing");
            Check(EntryField<string>(entries[Read<int>(hub, "_sel")], "Id") == voices[progress],
                "stage-clear return selects the newly available voice, including Mina");
            Check(entries.Count(entry => EntryField<object>(entry, "Sort").ToString() == "Pinned") == (progress > 0 ? 1 : 0),
                "Mina's return post remains separate from the new timeline batch");
            Call(hub, "BuildEntries");
            Check(FeedSnapshot(hub).SequenceEqual(feed), "refreshing the feed does not reroll or duplicate posts");
            game.SaveToSlot(0);
            Read<HashSet<string>>(game, "_cleared").Clear();
            Check(game.LoadFromSlot(0), "clear progress can be loaded without adding a new save field");
            Call(hub, "BuildEntries");
            Check(FeedSnapshot(hub).SequenceEqual(feed), "reloading restores the same unlocked posts and batches");

            Write(hub, "_mode", Enum.Parse(Read<object>(hub, "_mode").GetType(), "Cards"));
            Write(hub, "_idleTalkPending", false);
            Write(hub, "_toastT", 0d);
            Write(hub, "_t", 3d);
            Call(hub, "LoadFaces");
            Call(hub, "UpdateFeedScrollTarget");
            await Frames(90);
            await Shot($"feed_progress_{progress}_{voices[progress]}");
            if (progress == 1)
            {
                DisplayServer.WindowSetSize(new Vector2I(960, 540));
                await Frames(20);
                await Shot("feed_after_akari_small");
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                await Frames(20);
            }
            previous = feed;
        }
        game.CompleteStage(GameManager.FirstStageId);
        Call(hub, "BuildEntries");
        Check(FeedSnapshot(hub).SequenceEqual(previous), "replaying a cleared stage never duplicates its unlocked batch");
        foreach (var (state, count, selected) in new[] { ("lock", 9, "akari"), ("first", 14, "koharu"),
            ("all", 24, "final"), ("final", 24, "final") })
        {
            Write(hub, "_previewState", state);
            Call(hub, "ApplyPreview");
            Check(FeedSnapshot(hub).Length == count
                && EntryField<string>(Read<IList>(hub, "_entries")[Read<int>(hub, "_sel")]!, "Id") == selected,
                $"{state} preview uses the same discovery rules without leaking later posts");
        }
        Write(hub, "_previewState", null!);
        game.ResetPersistent();
        Call(hub, "BuildEntries");
        Check(FeedSnapshot(hub).Length == 9 && FeedSnapshot(hub).All(post => post.Kind != "Voice" || post.Id == "akari"),
            "starting over removes future posts instead of leaving locked rows");
    }

    private async Task CheckSocialFeed(Hub hub, GameManager game)
    {
        game.MarkIdleDialogSeen("once_sns_intro");
        Write(hub, "_idleTalkPending", false);
        Write(hub, "_t", 3d);
        PadField("_usingMouse", false);
        PadField("_autoUsingPad", false);
        Call(hub, "OpenHomeApp", 0);
        Check(Read<int>(hub, "_sel") == 0 && Read<float>(hub, "_feedScrollTarget") == 0,
            "returning SNS readers start at the top of the everyday feed");
        Write(hub, "_mode", Enum.Parse(Read<object>(hub, "_mode").GetType(), "Cards"));
        Write(hub, "_cardsEnteredT", 0d);
        await Frames(5);
        var start = FeedSnapshot(hub);
        Check(start.Count(p => p.Kind == "Filler") == 8, "eight daily conversations are available before any battle");
        Check(!start.Any(p => p.Id == "plant_1"), "later episodes do not appear before their chapter");
        await Shot("sns_daily_feed");
        await KeyAction("ui_right");
        Check(Read<int>(hub, "_footSel") == 2, "a horizontal arrow reaches the concerned-voice shortcut without traversing the whole feed");
        await Keypress(Key.Z);
        Check(EntryField<string>(Read<IList>(hub, "_entries")[Read<int>(hub, "_sel")]!, "Id") == "akari",
            "keyboard shortcut selects the next story post");
        Write(hub, "_sel", 0);
        Write(hub, "_feedScroll", 0f);
        Write(hub, "_feedScrollTarget", 0f);
        long followers = game.Followers, impression = game.Impression;
        float contamination = game.Contamination;
        await Keypress(Key.Z);
        Check(Mode(hub) == "SocialThread", "Z opens an everyday post instead of denying input or diving");
        Check(Read<SnsVoices.Post>(hub, "_socialPost").Id == "plant_0", "reader opens the selected resident's post");
        Check(Call(hub, "RelatedSocialPost", false) == null && Call(hub, "RelatedSocialPost", true) == null,
            "unavailable episodes cannot be opened from the reader");
        await Shot("sns_thread_first");
        await Keypress(Key.Escape);
        Check(Mode(hub) == "Cards" && Read<int>(hub, "_sel") == 0, "back returns to the original timeline position");
        Click(hub, (Rect2)Call(hub, "CardHitRect", 0)!, "ProcessCards");
        Check(Mode(hub) == "SocialThread", "mouse opens the same post and reply thread");
        Click(hub, new Rect2(412, 16, 40, 40), "ProcessSocialThread", 0d);
        Check(Mode(hub) == "Cards", "the reader's back button returns to the feed");
        foreach (var stage in GameManager.Stages) Read<HashSet<string>>(game, "_cleared").Add(stage.Id);
        Call(hub, "BuildEntries");
        Call(hub, "LoadFaces");
        await Frames(40);
        Check(SnsVoices.Posts.Select(p => p.Id).Distinct().Count() == SnsVoices.Posts.Length, "all resident posts have unique IDs");
        Check(SnsVoices.Posts.Sum(p => p.Replies.Length) == 60, "all sixty authored replies are present");
        foreach (var post in SnsVoices.Posts)
        {
            Check(post.Replies.All(r => r.Author >= 0 && r.Author < SnsVoices.Count && r.Text.Length > 0),
                $"{post.Id}: reply authors and texts are valid");
            if (post.PreviousId.Length > 0)
            {
                var previous = SnsVoices.Posts.Single(p => p.Id == post.PreviousId);
                Check(previous.Author == post.Author && previous.Chapter < post.Chapter,
                    $"{post.Id}: previous episode preserves its author and points backward in time");
            }
            Check(UiKit.WrapLines(UiKit.Zen, post.Text, 17, 432).Count <= 3,
                $"{post.Id}: timeline text fits without losing its ending");
            Call(hub, "OpenSocialThread", post);
            Check(Read<string[][]>(hub, "_socialLines").Length == post.Replies.Length + 1,
                $"{post.Id}: full post and every reply are laid out");
        }
        Write(hub, "_sel", Array.FindIndex(FeedSnapshot(hub), p => p.Id == "plant_3") + 1);
        Write(hub, "_feedScroll", 0f);
        Write(hub, "_feedScrollTarget", 0f);
        Call(hub, "OpenSocialThread", SnsVoices.Posts.Single(p => p.Id == "plant_3"));
        await KeyAction("ui_left");
        Check(Read<SnsVoices.Post>(hub, "_socialPost").Id == "plant_2", "left opens the earlier post in this resident's story");
        await KeyAction("ui_right");
        Check(Read<SnsVoices.Post>(hub, "_socialPost").Id == "plant_3", "right returns to the follow-up post");
        Click(hub, new Rect2(416, 660, 218, 42), "ProcessSocialThread", 0d);
        Check(Read<SnsVoices.Post>(hub, "_socialPost").Id == "plant_2", "mouse can browse earlier episodes too");
        await Shot("sns_thread_followup");
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetSize(new Vector2I(960, 540));
            await Frames(20);
            await Shot("sns_thread_small");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(20);
        }
        Call(hub, "OpenSocialThread", SnsVoices.Posts.Single(p => p.Id == "draft_0"));
        PadField("_wheelFrame", -100f);
        Call(hub, "ProcessSocialThread", 1d);
        PadField("_wheelFrame", 0f);
        float max = Mathf.Max(0, Read<float>(hub, "_socialContentHeight") - 568f);
        Check(max > 0 && Read<float>(hub, "_socialScrollTarget") == max, "scrolling reaches the final reply and clamps at the bottom");
        await Shot("sns_thread_bottom");
        await Keypress(Key.Escape);
        Check(Read<float>(hub, "_feedScrollTarget") == 0 && Read<int>(hub, "_sel") == 1,
            "browsing related posts preserves the original timeline selection and scroll");
        var footType = typeof(Hub).GetNestedType("FootAct", BindingFlags.NonPublic)!;
        Call(hub, "FooterClick", Enum.Parse(footType, "Voice"));
        Check(EntryField<string>(Read<IList>(hub, "_entries")[Read<int>(hub, "_sel")]!, "Id") == "final",
            "the concerned-voice shortcut still reaches the next story post");
        Check(game.Followers == followers && game.Impression == impression && game.Contamination == contamination,
            "reading residents' conversations does not change combat stats or currency");
        Check(!Read<bool>(hub, "_dived"), "reading and scrolling never start a battle");
    }

    private async Task CheckSidePresentation(Hub hub, Rect2 phone)
    {
        foreach (string field in new[] { "CompanionArea", "StoryArea" })
        {
            var area = (Rect2)typeof(Hub).GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            Check(!area.Intersects(phone) && new Rect2(0, 0, 1280, 720).Encloses(area),
                $"{field} stays outside the interactive phone");
        }
        Check(Read<Dictionary<string, Texture2D>>(hub, "_sidePortraits").Count == 4,
            "all playable companions have a dedicated sidebar illustration");
        Check((string)Call(hub, "SideStoryId")! == "akari", "new home previews the first available story");
        var entries = Read<IList>(hub, "_entries");
        object mode = Read<object>(hub, "_mode");
        int selected = Read<int>(hub, "_sel");
        void Select(string id)
        {
            var list = Read<IList>(hub, "_entries");
            for (int i = 0; i < list.Count; i++)
                if ((string)list[i]!.GetType().GetField("Id")!.GetValue(list[i])! == id) Write(hub, "_sel", i);
        }
        Write(hub, "_mode", Enum.Parse(mode.GetType(), "Cards"));
        Check(!FeedSnapshot(hub).Any(post => post.Id is "koharu" or "rei" or "final"),
            "unavailable voices have no timeline entry or selectable placeholder");
        Write(hub, "_previewState", "first");
        Call(hub, "ApplyPreview");
        Call(hub, "LoadFaces");
        Check((string)Call(hub, "SideStoryId")! == "koharu", "first rescue advances the story preview to Koharu");
        Check(!FeedSnapshot(hub).Any(post => post.Id is "rei" or "final"),
            "later voices remain absent after the first rescue");
        Write(hub, "_previewState", "all");
        Call(hub, "ApplyPreview");
        Call(hub, "LoadFaces");
        Select("rei");
        Check((string)Call(hub, "SideStoryId")! == "rei", "an available selected story controls the illustration");
        Write(hub, "_mode", mode);
        Check((string)Call(hub, "SideStoryId")! == "final", "all rescues reveal the final story on home");
        Write(hub, "_previewState", null!);
        Write(hub, "_entries", entries);
        Write(hub, "_sel", selected);

        double time = Read<double>(hub, "_t");
        hub.SetProcess(false);
        Write(hub, "_t", 2d);
        hub.QueueRedraw();
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var still = GetViewport().GetTexture().GetImage();
        await Shot("home_journey");
        Write(hub, "_t", 4d);
        hub.QueueRedraw();
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var moved = GetViewport().GetTexture().GetImage();
        foreach (var area in new[] { new Rect2I(30, 165, 330, 355) })
        {
            float difference = 0;
            for (int y = area.Position.Y; y < area.End.Y; y += 4)
                for (int x = area.Position.X; x < area.End.X; x += 4)
                {
                    var a = still.GetPixel(x, y);
                    var b = moved.GetPixel(x, y);
                    difference += Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B);
                }
            Check(difference / (area.Size.X * area.Size.Y / 16) > 0.003f, "companion artwork visibly moves while the phone is idle");
        }
        Write(hub, "_t", time);
        hub.SetProcess(true);
    }

    private static void PadField(string name, object value) => typeof(Pad).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);

    private static void Click(Hub hub, Rect2 rect, string handler, params object[] args)
    {
        // Inject design-space mouse state without moving the desktop cursor.
        Vector2 previousPosition = Pad.MousePos();
        Vector2 point = rect.GetCenter();
        if (handler == "ProcessHome") point = (Transform2D)Call(hub, "HomePhoneTransform")! * point;
        PadField("_mousePos", point);
        PadField("_usingMouse", true);
        PadField("_mL", true);
        PadField("_mLPrev", false);
        Call(hub, handler, args);
        PadField("_mousePos", previousPosition);
        PadField("_mL", false);
        PadField("_mLPrev", false);
        PadField("_usingMouse", false);
    }

    // キーボードだけでアカウント切り替えを開く（2026-09-27 に J の直行を廃止した代わりの経路）：
    //   最後のカードへ移って ↓ でフッタ「アカウント」、Z で押す。カードの選択は開いた後に元へ戻す
    //   （以降の手順が「選んでいた投稿」を前提にしているため）。
    private async Task OpenAccountsByKeys(Hub hub)
    {
        int sel = Read<int>(hub, "_sel");
        Write(hub, "_footSel", -1);
        Write(hub, "_sel", Read<Array>(hub, "_entries").Length - 1);
        await Frames(2);
        await KeyAction("ui_down");
        Check(Read<int>(hub, "_footSel") >= 0, "↓ on the last card reaches the footer");
        await Keypress(Key.Z);
        Write(hub, "_sel", sel);
        Write(hub, "_footSel", -1);
    }

    private async Task Keypress(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = true });
        await Frames(20);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
        await Frames(3);
    }

    private async Task KeyAction(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        await Frames(2);
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        await Frames(3);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/hub_job/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
