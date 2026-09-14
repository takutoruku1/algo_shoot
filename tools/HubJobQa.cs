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
            Check(Mode(hub) == "Cards", "new game starts on stage selection without a forced job prompt");
            await Shot("cards");
            var phone = new Rect2(400, 0, 480, 720);
            Check(phone.Encloses((Rect2)Call(hub, "HeaderJobRect")!), "character switch stays inside the phone column");
            var initialEntries = Read<IList>(hub, "_entries");
            var feed = new Rect2(400, 128, 480, 518);
            for (int i = 0; i < initialEntries.Count; i++)
            {
                var hit = (Rect2)Call(hub, "CardHitRect", i)!;
                Check(!hit.HasArea() || feed.Encloses(hit), "post hit areas are clipped above navigation and below the header");
            }
            Click(hub, (Rect2)Call(hub, "HeaderJobRect")!, "ProcessCards");
            Check(Mode(hub) == "Job", "header button opens all four job types");
            foreach (var job in Jobs.All)
            {
                var face = Read<Dictionary<string, Texture2D>>(hub, "_playerFaces")[job.CharacterId];
                Check(face.ResourcePath == $"res://char/player/{job.CharacterId}/{job.CharacterId}_spin_v2_00.png",
                    $"{job.CharacterId} player portrait is available before stage clears");
                var box = ((float x, float y, float w, float h))Call(hub, "JobBox")!;
                Check(UiKit.WrapLines(UiKit.Zen, job.Strength, 14, box.w - 48f).Count <= 2
                    && UiKit.WrapLines(UiKit.Zen, job.Weakness, 14, box.w - 48f).Count <= 2,
                    $"{job.CharacterId} descriptions fit the compact character sheet");
            }
            await Frames(20);
            await Shot("types");
            // ジョブ解禁制（2026-09-14）：新規データは結び手（ミナ）だけ。残る3人は行を残したまま
            //   灰色＋解禁条件が出る。押しても選ばれず、理由（どの面をクリアすれば開くか）だけ返る。
            foreach (var job in Jobs.All)
            {
                bool free = job.UnlockStageId.Length == 0;
                Check(game.IsJobUnlocked(job.Id) == free, $"{job.CharacterId} starts {(free ? "unlocked" : "locked")} on new data");
                Check((game.JobUnlockHint(job.Id) == null) == free,
                    $"{job.CharacterId} {(free ? "needs no unlock line" : "explains the stage that opens it")}");
            }
            Click(hub, (Rect2)Call(hub, "JobHitRect", 1)!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Job" && game.SelectedJob == Job.Tank, "locked character keeps the selection sheet open");
            Check(Read<string>(hub, "_toastSub") == game.JobUnlockHint(Job.Melee), "the refusal names the stage that opens it");
            await Shot("types_locked");
            var clearedStages = Read<HashSet<string>>(game, "_cleared");
            // 1面（あかり）クリア＝灯し手だけが開く。残る2人は伏せたまま＝1クリアにつき1人。
            clearedStages.Add("akari");
            Check(game.IsJobUnlocked(Job.Melee) && !game.IsJobUnlocked(Job.Heal) && !game.IsJobUnlocked(Job.Magic),
                "clearing akari opens only her job");
            foreach (var item in GameManager.Stages) clearedStages.Add(item.Id);
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
            Check(phone.Encloses((Rect2)Call(hub, "DetailConfirmRect", true)!)
                && !((Rect2)Call(hub, "DetailConfirmRect", true)!).Intersects((Rect2)Call(hub, "TierHitRect", 3)!),
                "dive button stays below all four difficulties");
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

            var cleared = Read<HashSet<string>>(game, "_cleared");
            foreach (var item in GameManager.Stages) cleared.Add(item.Id);
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
            await Keypress(Key.Z);
            Check(Mode(hub) == "Detail", "final stage detail opens normally");
            await Shot("final_detail_small");
            Click(hub, (Rect2)Call(hub, "DetailJobRect", false)!, "ProcessDetail", 0.01);
            await Frames(20);
            Click(hub, (Rect2)Call(hub, "JobCloseRect")!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Detail" && Read<int>(hub, "_sel") == final, "final stage job button returns to its shorter detail layout");
            Click(hub, (Rect2)Call(hub, "DetailCloseRect", false)!, "ProcessDetail", 0.01);
            Click(hub, (Rect2)Call(hub, "FooterItemRect", 0)!, "ProcessCards");
            Check(Mode(hub) == "Detail", "bottom dive navigation opens the selected post");
            await Frames(20);
            Click(hub, (Rect2)Call(hub, "DetailJobRect", false)!, "ProcessDetail", 0.01);
            await Frames(20);
            Click(hub, (Rect2)Call(hub, "JobConfirmRect")!, "ProcessJob", 0.01);
            Check(Mode(hub) == "Detail" && game.SelectedJob == Job.Magic, "character confirmation button applies the highlighted character");
            Click(hub, (Rect2)Call(hub, "DetailConfirmRect", false)!, "ProcessDetail", 0.01);
            await Frames(5);
            Check(GetTree().CurrentScene is MinaRoot, "primary dive button enters the selected stage");
            GetTree().CurrentScene.QueueFree();
            await Frames(5);
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
        catch (Exception ex)
        {
            GD.PushError($"[HubJobQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private static void Click(Hub hub, Rect2 rect, string handler, params object[] args)
    {
        // Inject design-space mouse state without moving the desktop cursor.
        void PadField(string name, object value) => typeof(Pad).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
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
