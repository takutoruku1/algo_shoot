using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
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
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            foreach (var job in Jobs.All) game.MarkIdleDialogSeen($"once_companion_select_{job.CharacterId}");
            game.Difficulty = GameManager.Diff.Normal;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            await Frames(1);
            // Keep the desktop cursor outside this deterministic input test.
            GetNode<PauseMenu>("/root/PauseMenu").SetProcess(false);
            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            await Frames(30);
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
                Check(stats == System.FormattableString.Invariant($"♥{job.MaxLifeDelta:+0;-0;+0}／移動×{job.MoveMul:0.##}／回避距離×{job.DodgeDistMul:0.##}")
                    && UiKit.TextW(UiKit.Zen, stats, 14) <= box.w - 124f,
                    $"{job.CharacterId} shows only the actual life, movement and dodge modifiers on one line");
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
                await Keypress(Key.J);
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
            await Keypress(Key.J);
            Check(Mode(hub) == "Job", "J also opens types from the detail screen");
            await KeyAction("ui_down");
            await Keypress(Key.X);
            Check(Mode(hub) == "Detail" && game.SelectedJob == Job.Magic, "keyboard cancel keeps the selected job");
            await Keypress(Key.J);
            Click(hub, (Rect2)Call(hub, "JobCloseRect")!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Detail", "close button returns to the originating detail screen");
            await Frames(10);
            await Keypress(Key.J);
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
            await Keypress(Key.J);
            await Keypress(Key.X);
            Check(Mode(hub) == "Cards", "existing card-screen shortcut still returns to cards");

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
            Click(hub, (Rect2)Call(hub, "DetailJobRect", false)!, "ProcessDetail", 0.01);
            await Frames(20);
            Click(hub, (Rect2)Call(hub, "JobCloseRect")!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Detail" && Read<int>(hub, "_sel") == final, "final stage job button returns to its shorter detail layout");
            Click(hub, (Rect2)Call(hub, "DetailCloseRect", false)!, "ProcessDetail", 0.01);
            Click(hub, (Rect2)Call(hub, "FooterItemRect", 0)!, "ProcessCards");
            Check(Mode(hub) == "Home" && !Read<bool>(hub, "_dived"), "former dive navigation returns home instead of starting a stage");
            await Frames(3);
            Write(hub, "_idleTalkPending", false);
            Click(hub, (Rect2)Call(hub, "HomeAppRect", 0)!, "ProcessHome");
            await Frames(45);
            await Keypress(Key.Z);
            Check(Mode(hub) == "Detail", $"opening a post reaches stage controls (mode={Mode(hub)})");
            await Frames(20);
            Click(hub, (Rect2)Call(hub, "DetailJobRect", false)!, "ProcessDetail", 0.01);
            await Frames(20);
            Click(hub, (Rect2)Call(hub, "JobConfirmRect")!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Detail" && game.SelectedJob == Job.Magic, "character confirmation button applies the highlighted character");
            Click(hub, (Rect2)Call(hub, "FinalDiveRect")!, "ProcessDetail", 0.01);
            await Frames(5);
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
            await Frames(80);
            Check(GetTree().CurrentScene is Shop && game.SelectedJob == Job.Magic, "shop app opens with the active SNS account");
            Call(GetTree().CurrentScene, "ExitShop");
            await Frames(180);
            Check(GetTree().CurrentScene is Hub && Mode((Hub)GetTree().CurrentScene) == "Home", "closing the shop returns home");
            hub = (Hub)GetTree().CurrentScene;
            await KeyAction("ui_right");
            await KeyAction("ui_right");
            await Keypress(Key.Z);
            await Frames(70);
            Check(GetTree().CurrentScene is Records, "keyboard navigation opens the records app");
            await Keypress(Key.X);
            Check(GetTree().CurrentScene is Hub && Mode((Hub)GetTree().CurrentScene) == "Home", "records also returns home");
            GetTree().CurrentScene.QueueFree();
            await Frames(5);
            game.ResetPersistent();
            Read<HashSet<string>>(game, "_cleared").Add(GameManager.FirstStageId);
            game.ShopTutorialSeen = true;
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            for (int visit = 0; visit < 2; visit++)
            {
                game.JustClearedStageId = GameManager.FirstStageId;
                hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
                GetTree().Root.AddChild(hub);
                GetTree().CurrentScene = hub;
                await Frames(10);
                Check(Mode(hub) == "Dialogue", "rescue conversation precedes the home screen");
                Call(hub, "EndDialogue");
                if (visit == 0)
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
                if (visit == 0) await Shot("home_first_rescue");
                hub.QueueFree();
                await Frames(5);
            }
            await Finish();
        }
        catch (Exception ex)
        {
            GD.PushError($"[HubJobQA] FAIL {ex}");
            GetTree().Quit(1);
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
        Vector2 mouse = Pad.MousePos();
        foreach (var (offset, y) in new[] { (0f, 644f), (200f, 130f) })
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
        Select("rei");
        Check((string)Call(hub, "SideStoryId")! == "akari", "selecting a locked post does not reveal its illustration");
        Write(hub, "_previewState", "first");
        Call(hub, "ApplyPreview");
        Check((string)Call(hub, "SideStoryId")! == "koharu", "first rescue advances the story preview to Koharu");
        Select("rei");
        Check((string)Call(hub, "SideStoryId")! == "koharu", "later locked stories remain hidden after a rescue");
        Write(hub, "_previewState", "all");
        Call(hub, "ApplyPreview");
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
        foreach (var area in new[] { new Rect2I(30, 165, 330, 355), new Rect2I(925, 115, 300, 245) })
        {
            float difference = 0;
            for (int y = area.Position.Y; y < area.End.Y; y += 4)
                for (int x = area.Position.X; x < area.End.X; x += 4)
                {
                    var a = still.GetPixel(x, y);
                    var b = moved.GetPixel(x, y);
                    difference += Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B);
                }
            Check(difference / (area.Size.X * area.Size.Y / 16) > 0.003f, "sidebar artwork visibly moves while the phone is idle");
        }
        Write(hub, "_t", time);
        hub.SetProcess(true);
    }

    private static void PadField(string name, object value) => typeof(Pad).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);

    private static void Click(Hub hub, Rect2 rect, string handler, params object[] args)
    {
        // Inject design-space mouse state without moving the desktop cursor.
        Vector2 previousPosition = Pad.MousePos();
        PadField("_mousePos", rect.GetCenter());
        PadField("_usingMouse", true);
        PadField("_mL", true);
        PadField("_mLPrev", false);
        Call(hub, handler, args);
        PadField("_mousePos", previousPosition);
        PadField("_mL", false);
        PadField("_mLPrev", false);
        PadField("_usingMouse", false);
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
        string path = ProjectSettings.GlobalizePath("res://build/qa_story/hub_job/shots");
        DirAccess.MakeDirRecursiveAbsolute(path);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{path}/{name}.png") == Error.Ok, $"screenshot {name}");
    }
}
