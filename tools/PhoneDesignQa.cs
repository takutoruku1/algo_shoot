using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class PhoneDesignQa : Node
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static void Write(object target, string name, object value) => target.GetType().GetField(name, Members)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Members)!.Invoke(target, args);
    private static string Mode(Hub hub) => Read<object>(hub, "_mode").ToString()!;
    private static void SetMode(Hub hub, string value)
    {
        var field = typeof(Hub).GetField("_mode", Members)!;
        field.SetValue(hub, Enum.Parse(field.FieldType, value));
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print("[PhoneDesignQA] PASS " + message);
    }

    private string _output = "";
    private bool _headless, _movie;

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            _headless = DisplayServer.GetName() == "headless";
            _movie = Array.Exists(OS.GetCmdlineUserArgs(), argument => argument == "--movie");
            _output = ProjectSettings.GlobalizePath("res://build/qa_story/phone_ui_3d/design");
            var args = OS.GetCmdlineUserArgs();
            int outputArg = Array.IndexOf(args, "--output");
            if (outputArg >= 0 && outputArg + 1 < args.Length) _output = ProjectSettings.GlobalizePath(args[outputArg + 1]);
            DirAccess.MakeDirRecursiveAbsolute(_output);
            GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
            GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.GetSetMethod(true)!.Invoke(null, new object[] { false });
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.AutoSaveEnabled = false;
            foreach (string seen in new[] { "once_sns_intro", "once_phone_home", "once_account_intro" }) game.MarkIdleDialogSeen(seen);
            foreach (var job in Jobs.All) game.MarkIdleDialogSeen($"once_companion_select_{job.CharacterId}");
            game.ShopTutorialSeen = true;
            if (!_headless)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            }
            await Frames(2);
            if (args.Contains("--mina-topics"))
            {
                await CheckMinaTopics(game);
                await Finish();
                return;
            }
            if (args.Contains("--mina-contact"))
            {
                await CheckMinaContact(game);
                await Finish();
                return;
            }
            if (args.Contains("--mina-motion"))
            {
                await CheckMinaMotion(game);
                await Finish();
                return;
            }
            if (args.Contains("--lime"))
            {
                await CheckLime(game);
                await Finish();
                return;
            }
            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            await Frames(30);
            Check(Mode(hub) == "Home", "phone home is the initial screen");
            Check(!(bool)Call(hub, "HomeAppUnlocked", 1)!, "shop starts locked in a new save");
            Pad.ReleaseMouseFollow();
            Write(hub, "_homeSel", 0);
            await Keypress(Key.Right);
            await Keypress(Key.Z);
            Check(Mode(hub) == "Home" && GetTree().CurrentScene == hub && !LoadingScreen.IsActive,
                "confirming a locked app does not start a transition");
            Write(hub, "_toastT", 0.0);

            var visuals = Read<Node>(hub, "_phoneVisuals");
            var hardware = visuals.GetNode<SubViewport>("PhoneHardwareView");
            var icons = visuals.GetNode<SubViewport>("PhoneIconsView");
            var phone = Read<Node3D>(visuals, "_phone");
            var models = Read<Array>(visuals, "_icons").Cast<object>().ToArray();
            Check(hardware.OwnWorld3D && hardware.TransparentBg && icons.OwnWorld3D && icons.TransparentBg,
                "hardware and app icons use transparent isolated worlds");
            Check(hardware.Size == new Vector2I(1280, 720) && icons.Size == new Vector2I(1024, 768) && models.Length == Hub.HomeAppCount,
                "render sizes and icon count match the phone UI");
            var unlocked = Enumerable.Range(0, models.Length).Select(index => (bool)Call(hub, "HomeAppUnlocked", index)!).ToArray();
            var activation = Enumerable.Repeat(1f, models.Length).ToArray();
            void Update(Transform2D pose, bool home, bool interactive, int focused, double delta = .6)
                => Call(visuals, "Update", pose, home, interactive, focused, unlocked, activation, new Vector2(640, 360), delta);
            float Focus(int index) => Read<float>(models[index], "Focus");
            void CheckProjection(Transform2D pose)
            {
                var camera = hardware.GetCamera3D();
                var samples = new List<Vector2> { new(400, 0), new(880, 0), new(880, 720), new(400, 720) };
                Check(pose.X.Y == 0 && pose.Y.X == 0 && Mathf.IsEqualApprox(pose.X.X, pose.Y.Y),
                    "phone text remains horizontal and uniformly scaled");
                samples.AddRange(Enumerable.Range(0, models.Length).Select(index => ((Rect2)Call(hub, "HomeAppIconRect", index)!).GetCenter()));
                foreach (var screen in samples)
                {
                    Vector3 local = new((screen.X - 640) / 100, (360 - screen.Y) / 100, 0);
                    Vector2 actual = camera.UnprojectPosition(phone.ToGlobal(local));
                    Vector2 expected = pose * screen;
                    Check(actual.DistanceTo(expected) < .75f,
                        $"phone screen projection agrees with input at {screen}: error={actual.DistanceTo(expected):F3}px");
                }
            }

            hub.SetProcess(false);
            for (int sample = 0; sample <= 4; sample++)
            {
                SetMode(hub, "SnsOpening");
                Write(hub, "_snsOpeningT", .65 * sample / 4);
                var pose = (Transform2D)Call(hub, "HomePhoneTransform")!;
                Update(pose, true, false, -1);
                CheckProjection(pose);
                hub.QueueRedraw();
                if (sample >= 2) await CheckScreenCorners(hub, pose, $"sns_opening_{sample}");
            }
            SetMode(hub, "Home");
            var homePose = (Transform2D)Call(hub, "HomePhoneTransform")!;
            Update(homePose, true, true, 0);
            var snapshotBounds = (Rect2)Call(hub, "HomePhoneBounds")!;
            var projectedMin = Vector2.One * float.MaxValue;
            var projectedMax = Vector2.One * float.MinValue;
            int meshCount = 0;
            foreach (var mesh in phone.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            {
                Aabb bounds = mesh.GetAabb();
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = bounds.Position + new Vector3((corner & 1) != 0 ? bounds.Size.X : 0,
                        (corner & 2) != 0 ? bounds.Size.Y : 0, (corner & 4) != 0 ? bounds.Size.Z : 0);
                    Vector2 projected = hardware.GetCamera3D().UnprojectPosition(mesh.ToGlobal(local));
                    projectedMin = projectedMin.Min(projected);
                    projectedMax = projectedMax.Max(projected);
                }
                meshCount++;
            }
            var projectedBounds = new Rect2(projectedMin, projectedMax - projectedMin);
            Check(meshCount > 0 && snapshotBounds.Grow(2).Encloses(projectedBounds),
                $"phone hardware fits the app-transition snapshot: model={projectedBounds}, snapshot={snapshotBounds}");
            Check(Focus(0) > .8f && Enumerable.Range(1, 7).All(index => Focus(index) < .05f),
                "only the selected unlocked icon rises");
            Update(homePose, true, true, 1);
            Update(homePose, true, true, 1);
            Check(models.All(model => Read<float>(model, "Focus") < .05f), "a locked icon does not rise");
            Update(homePose, true, true, 3);
            Update(homePose, true, false, 3);
            Update(homePose, true, false, 3);
            Check(models.All(model => Read<float>(model, "Focus") < .05f), "overlay input blocking settles every icon");
            Check(hardware.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled
                && icons.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled,
                "a home overlay keeps its underlying phone visible");
            Update(Transform2D.Identity, false, false, -1);
            Check(hardware.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled
                && icons.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled,
                "SNS retains its phone rim while disabling unused app-icon rendering");
            Update(homePose, true, true, 0);
            hub.SetProcess(true);
            await Shot("locked_home");

            Read<HashSet<string>>(game, "_cleared").Add(GameManager.FirstStageId);
            Write(hub, "_idleTalkPending", false);
            Check((bool)Call(hub, "HomeAppUnlocked", 1)! && (bool)Call(hub, "HomeAppUnlocked", 2)!,
                "clearing the first stage unlocks shop and records");
            if (!_headless)
            {
                GetWindow().GrabFocus();
                await Frames(3);
                for (int index = 0; index < models.Length; index++)
                {
                    Write(hub, "_homeSel", index / 4 * 4);
                    await Frames(3);
                    Rect2 icon = (Rect2)Call(hub, "HomeAppIconRect", index)!;
                    Vector2 target = (Transform2D)Call(hub, "HomePhoneTransform")! * icon.GetCenter();
                    GetViewport().WarpMouse(target * UiKit.Scale);
                    for (int frame = 0; frame < 35; frame++) { Pad.PollMouse(GetViewport()); await Frames(1); }
                    Check(Read<int>(hub, "_homeSel") == index && Focus(index) > .65f,
                        $"mouse hover selects and raises app {index} at its projected screen position: selected={Read<int>(hub, "_homeSel")}, rise={Focus(index):F2}, windowFocus={GetWindow().HasFocus()}");
                    if (index is 0 or 4 or 8) await Shot($"home_page_{index / 4}");
                }
            }
            Pad.ReleaseMouseFollow();
            Write(hub, "_homeSel", 0);
            await Keypress(Key.Tab);
            Check(Mode(hub) == "Job", "account overlay opens over the home");
            await Frames(35);
            Check(models.All(model => Read<float>(model, "Focus") < .05f), "actual account overlay stops icon hover");
            await Shot("account_overlay");
            await Keypress(Key.Tab);
            Check(Mode(hub) == "Home", "account overlay returns to home");
            await Keypress(Key.Z);
            Check(Mode(hub) is "SnsOpening" or "Cards", "SNS starts from the selected icon");
            for (int frame = 0; frame < 600 && Mode(hub) == "SnsOpening"; frame++) await Frames(1);
            Check(Mode(hub) == "Cards", "SNS opening completes at the readable timeline");
            await Frames(5);
            Check(icons.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled
                && hardware.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled,
                "normal SNS flow applies the viewport visibility policy");
            CheckProjection(Transform2D.Identity);
            await Shot("sns_timeline");
            hub.SetProcess(false);
            foreach (int footer in new[] { 0, 2 })
            {
                Write(hub, "_feedScroll", footer == 0 ? 0f : 240f);
                Write(hub, "_footSel", footer);
                hub.QueueRedraw();
                await CheckScreenCorners(hub, Transform2D.Identity, $"sns_scroll_footer_{footer}");
            }
            Write(hub, "_footSel", -1);
            hub.SetProcess(true);
            if (_movie) await Frames(90);
            await Keypress(Key.Escape);
            Check(Mode(hub) == "Home", "back from SNS returns to the phone home");
            await Frames(5);
            Check(icons.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled, "returning home restores icon rendering");
            await Shot("home_return");
            if (_movie) await Frames(60);

            hub.QueueFree();
            await Frames(4);
            Check(!IsInstanceValid(visuals) && !IsInstanceValid(hardware) && !IsInstanceValid(icons),
                "leaving the hub releases its 3D rendering surfaces");
            await CheckLime(game);
            await Finish();
        }
        catch (Exception error)
        {
            GD.PushError("[PhoneDesignQA] FAIL " + error);
            GetTree().Quit(1);
        }
    }

    private async Task CheckMinaTopics(GameManager game)
    {
        game.ResetPersistent();
        game.MarkIdleDialogSeen("once_phone_home");
        game.AutoAdvanceDialog = false;
        var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        await Frames(25);
        Call(hub, "OpenHomeApp", 0);
        Call(hub, "ProcessSnsOpening", 2d);
        int selection = Read<int>(hub, "_sel");
        Check(Read<bool>(hub, "_minaCall") && !(bool)Call(hub, "get_DialogShown")!, "SNS opens Mina's full-screen app for the introduction");
        Call(hub, "AdvanceDialogue");
        Call(hub, "ProcessMinaCall", 1.3d);
        await Shot("sns_call");
        while (Read<bool>(hub, "_minaCall")) Call(hub, "AdvanceDialogue");
        Check(game.IsIdleDialogSeen("once_sns_intro") && Mode(hub) == "Cards" && Read<int>(hub, "_sel") == selection,
            "finishing the call returns to the selected SNS post and marks the guide seen");
        await Keypress(Key.Escape);
        await Keypress(Key.Up);
        await Keypress(Key.Enter);
        int choices() => Read<Array>(hub, "_minaChoices").Length;
        Check(choices() == 4 && Read<bool>(hub, "_minaCasual"), "the home call offers four spoiler-free subjects");
        await Shot("topics");
        int start = game.LimeThread("mina").Count;
        await Keypress(Key.Down);
        await Keypress(Key.Enter);
        Check(game.LimeThread("mina")[^1].Text.Contains("鼻歌") && choices() == 0, "selecting a topic sends the player's own line");
        void FinishReply()
        {
            for (int i = 0; i < 5 && choices() == 0; i++) Call(hub, "ProcessMinaCall", 20d);
            Check(choices() > 0 && Read<bool>(hub, "_minaCall"), "Mina finishes speaking and stays on the line");
        }
        FinishReply();
        Check(choices() == 2 && game.LimeThread("mina").Count == start + 2, "the song has two distinct answers");
        await Shot("song_replies");
        if (_headless) await Keypress(Key.Enter); else await ClickLime(hub, new Vector2(640, 533));
        FinishReply();
        Check(game.LimeThread("mina")[^1].Text.Contains("二音") && choices() == 1, "a compliment gets the second note and an invitation to sing");
        await Keypress(Key.Enter);
        FinishReply();
        Check(choices() == 4 && game.LimeThread("mina").Count == start + 6, "three exchanges lead directly to the next topic");
        await Keypress(Key.Down);
        await Keypress(Key.Down);
        await Keypress(Key.Enter);
        Call(hub, "ProcessMinaCall", 1.25d);
        Check(Read<Texture2D>(hub, "_minaCallFace").ResourcePath == CompanionDialogue.MinaWorriedPortrait,
            "Mina shows concern when the player is tired");
        FinishReply();
        await Keypress(Key.H);
        Check(Read<Array>(hub, "_minaArchive").Length == start + 8, "the choices and replies appear once each in call history");
        await Keypress(Key.Escape);
        if (_headless) await Keypress(Key.Escape); else await ClickLime(hub, new Vector2(640, 676));
        Check(Mode(hub) == "Home" && game.SaveToSlot(1) && game.LoadFromSlot(1), "hanging up and reloading preserves the conversation");
        Call(hub, "OpenMinaCall");
        Check(choices() == 2 && game.LimeThread("mina").Count == start + 8, "calling back resumes the unfinished topic without resending lines");
        int topicCount() => ((System.Collections.IEnumerable)Call(hub, "AvailableMinaTopics")!).Cast<object>().Count();
        game.CompleteStage("akari");
        Check(topicCount() == 5, "Akari's return opens her rain conversation");
        game.CompleteStage("koharu");
        Check(topicCount() == 6, "Koharu's stream joins the topics after her return");
        game.RecordClearTime(GameManager.FinalStageId, GameManager.Diff.Normal, 150f);
        Check(topicCount() == 7, "the reunion conversation becomes available after FINAL");
        hub.QueueFree();
        await Frames(4);
    }

    private async Task CheckMinaContact(GameManager game)
    {
        Hub Spawn()
        {
            var scene = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(scene);
            GetTree().CurrentScene = scene;
            return scene;
        }
        game.SelectedJob = Job.Tank;
        game.AutoAdvanceDialog = false;
        foreach (var stage in GameManager.Stages) game.CompleteStage(stage.Id);
        var hub = Spawn();
        await Frames(24);
        var lines = Read<(string speaker, string text)[]>(hub, "_dlg");
        int cutoff = Read<int>(hub, "_minaDisconnectIndex");
        Check(cutoff >= 3 && lines[cutoff - 3].speaker == "あなた" && lines[cutoff - 2].speaker == "ミナ"
            && lines[cutoff - 2].text.EndsWith("……") && lines[cutoff - 1].text.StartsWith(FinalAccount.Handle)
            && !(bool)Call(hub, "get_MinaCompanionOffline")!,
            "the voice breaks up before a notification from the masked account");
        Check(Read<bool>(hub, "_minaCall") && cutoff == lines.Length - 1 && cutoff > 0
            && lines[cutoff].text.Contains("返事して、ミナ"), "Rei's original return reaches the loss of contact");
        for (int i = 0; i < cutoff; i++) Call(hub, "AdvanceDialogue");
        Check((bool)Call(hub, "get_MinaCompanionOffline")!, "Mina disappears from the companion panel when the line drops");
        await Shot("lost_signal");
        Call(hub, "AdvanceDialogue");
        Check(Mode(hub) == "Home" && game.LimeThread("mina").SequenceEqual(lines), "the last exchange ends at the unanswered plea");
        game.LimeThread("mina").Add(("あなた", "ミナ、今ちょっと話せる？"));
        var history = game.LimeThread("mina").ToArray();
        Check(game.SaveToSlot(2) && game.LoadFromSlot(2), "reload the pre-FINAL save with a pending call");
        if (_headless) { await Keypress(Key.Up); await Keypress(Key.Enter); }
        else await ClickLime(hub, (Transform2D)Call(hub, "HomePhoneTransform")! * new Vector2(640, 241));
        Check(Mode(hub) == "MinaUnanswered" && !Read<bool>(hub, "_minaCall")
            && (string)Call(hub, "get_MinaConnectionStatus")! == "呼び出し中…", "the home button dials without connecting");
        await Frames(110);
        Check((string)Call(hub, "get_MinaConnectionStatus")! == "ミナからの応答がない。", "the call rings out");
        await Shot("no_response");
        await Keypress(Key.H);
        Check(Read<bool>(hub, "_minaHistoryOpen") && Read<Array>(hub, "_minaArchive").Length == history.Length,
            "the disconnected line can still show earlier messages");
        await Keypress(Key.Escape);
        if (_headless) await Keypress(Key.Enter);
        else await ClickLime(hub, new Vector2(772, 676));
        Check(Mode(hub) == "Home" && game.LimeThread("mina").SequenceEqual(history), "closing a failed call preserves the pending question");
        hub.QueueFree();
        await Frames(3);

        game.CompleteStage("akari");
        hub = Spawn();
        await Frames(24);
        Call(hub, "TryStartIdleSmallTalk");
        Check(Mode(hub) == "Home" && game.LimeThread("mina").SequenceEqual(history), "a replay and idle talk cannot reconnect Mina");
        game.RecordClearTime(GameManager.FinalStageId, GameManager.Diff.Normal, 150f);
        Check(game.SaveToSlot(2) && game.LoadFromSlot(2), "reload the FINAL clear record");
        var replay = ((string speaker, string text)[])Call(hub, "ReturnDialog", "rei")!;
        Check(replay[^1].speaker == "ミナ" && replay.All(line => line.speaker != "Ｙ システム"),
            "replaying Rei after restoration does not repeat the lost signal");
        for (int frame = 0; frame < 600 && game.LunaticUnlockPending; frame++) await Frames(1);
        await Frames(8);
        Check(!game.LunaticUnlockPending && Mode(hub) == "Home", "the first-clear unlock presentation returns control to Home");
        if (_headless) { await Keypress(Key.Up); await Keypress(Key.Enter); }
        else await ClickLime(hub, (Transform2D)Call(hub, "HomePhoneTransform")! * new Vector2(640, 241));
        Check(Read<bool>(hub, "_minaCall") && Read<(string, string)[]>(hub, "_dlg").Length == 1,
            "Mina answers the pending question after FINAL");
        Call(hub, "ProcessMinaCall", 1.2d);
        await Keypress(Key.Enter);
        Check(game.LimeThread("mina").Count == history.Length + 1 && game.LimeThread("mina")[^1].Speaker == "ミナ",
            "the restored line delivers exactly one reply");
        hub.QueueFree();
        await Frames(3);
    }

    private async Task CheckMinaMotion(GameManager game)
    {
        if (!_headless) await CheckMinaRestPose();
        game.SelectedJob = Job.Tank;
        game.AutoAdvanceDialog = false;
        game.CompleteStage("akari");
        var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(hub);
        GetTree().CurrentScene = hub;
        await Frames(3);
        Check(Read<bool>(hub, "_minaCall"), "first stage return opens the animated call");
        bool pressed = false, sent = false, tilted = false, listenerBlink = false, replyBlink = false;
        bool spoke = false, rested = false;
        bool metGaze = false, smiledWhileListening = false, gatheredThoughts = false, addressedOwner = false;
        bool chestLed = false, headFollowed = false;
        var points = Read<Vector2[]>(Read<MinaCallPortrait>(hub, "_minaPortrait"), "_points");
        var source = Read<Vector2[]>(Read<MinaCallPortrait>(hub, "_minaPortrait"), "_source");
        int shoulder = Array.IndexOf(source, new Vector2(208, 864));
        int otherShoulder = Array.IndexOf(source, new Vector2(880, 864));
        int brooch = Array.IndexOf(source, new Vector2(544, 896));
        int elbow = Array.IndexOf(source, new Vector2(144, 1152));
        int hem = Array.IndexOf(source, new Vector2(544, 1504));
        int Vertex(int x, int y) => Array.IndexOf(source, new Vector2(x, y));
        int crest = Vertex(512, 32), chin = Vertex(544, 736), left = Vertex(352, 608), right = Vertex(752, 608);
        var faceSpans = new[] { (crest, chin), (left, right), (chin, left), (chin, right) };
        int hemRight = Vertex(800, 1504);
        float faceLengthError = 0, torsoLengthError = 0;
        Vector2 shoulderRest = default, broochRest = default, elbowRest = default, hemRest = default;
        float shoulderTravel = 0, broochTravel = 0, elbowTravel = 0, hemTravel = 0;
        float shoulderIncline = 0;
        float previousRoll = 0, listeningRoll = 0, initialReturn = 0, peakTurnSpeed = 0, lastTurnSpeed = 0;
        double previousPoseTime = 0;
        Vector2 lowest = default, highest = default;
        int frame = 0;
        while (Read<double>(hub, "_minaCallTime") < 7.8)
        {
            double clock = Read<double>(hub, "_minaCallTime");
            if (pressed || !sent && clock >= 3.4)
            {
                pressed = !sent;
                sent = true;
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = pressed });
            }
            await Frames(1);
            if (!_headless) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var speech = (MinaCallPortrait.Speech)Call(hub, "get_MinaSpeech")!;
            var body = Read<MinaCallPortrait.BodyPose>(hub, "_minaBody");
            bool mina = Read<int>(hub, "_dlgIdx") == 1;
            if (!_headless)
            {
                if (frame == 0) (shoulderRest, broochRest, elbowRest, hemRest) = (points[shoulder], points[brooch], points[elbow], points[hem]);
                shoulderTravel = Math.Max(shoulderTravel, points[shoulder].DistanceTo(shoulderRest));
                broochTravel = Math.Max(broochTravel, points[brooch].DistanceTo(broochRest));
                elbowTravel = Math.Max(elbowTravel, points[elbow].DistanceTo(elbowRest));
                hemTravel = Math.Max(hemTravel, points[hem].DistanceTo(hemRest));
                var heights = new Vector2((points[shoulder].Y + points[otherShoulder].Y) * .5f, points[brooch].Y);
                shoulderIncline = Math.Max(shoulderIncline, Math.Abs(points[shoulder].Y - points[otherShoulder].Y));
                lowest = frame == 0 ? heights : lowest.Min(heights);
                highest = frame == 0 ? heights : highest.Max(heights);
                float scale = points[hemRight].DistanceTo(points[hem]) / 256f;
                foreach (var (a, b) in faceSpans)
                {
                    float expected = scale * source[a].DistanceTo(source[b]);
                    faceLengthError = Math.Max(faceLengthError, Math.Abs(points[a].DistanceTo(points[b]) - expected));
                }
                Vector2 waist = points[hem] - new Vector2(0, 72 * scale);
                float chestLength = (1432 - source[brooch].Y) * scale;
                torsoLengthError = Math.Max(torsoLengthError, Math.Abs(points[brooch].DistanceTo(waist) - chestLength));
                float roll = Mathf.RadToDeg((points[right] - points[left]).Angle());
                double poseTime = Read<double>(hub, "_minaCallTime");
                if (!mina) listeningRoll = roll;
                else if (poseTime > previousPoseTime)
                {
                    lastTurnSpeed = Math.Abs(roll - previousRoll) / (float)(poseTime - previousPoseTime);
                    peakTurnSpeed = Math.Max(peakTurnSpeed, lastTurnSpeed);
                    if (Read<double>(hub, "_dlgLineT") <= .17)
                        initialReturn = Math.Max(initialReturn, Math.Abs(roll - listeningRoll));
                }
                previousPoseTime = poseTime;
                previousRoll = roll;
            }
            bool closed = (float)Call(hub, "get_MinaBlink")! >= .9f;
            var gaze = Read<Vector2>(hub, "_minaGaze");
            if (!mina)
            {
                CheckQuiet(speech);
                metGaze |= gaze.X < 0 && Read<float>(hub, "_minaNeckAngle") < Mathf.DegToRad(3);
                smiledWhileListening |= closed && Read<Texture2D>(hub, "_minaCallFace").ResourcePath == CompanionDialogue.MinaSmilePortrait;
                tilted |= Read<float>(hub, "_minaNeckAngle") > Mathf.DegToRad(4);
                listenerBlink |= closed;
            }
            else
            {
                gatheredThoughts |= Read<double>(hub, "_dlgLineT") < .85 && gaze.X > 6;
                addressedOwner |= speech.Mouth > .4f && gaze.X < 0;
                chestLed |= body.Bow > .4f && body.Bow - body.Nod > .12f;
                headFollowed |= chestLed && body.Nod > .72f;
                spoke |= speech.Mouth > .75f;
                replyBlink |= closed && speech.Articulating;
                rested |= spoke && !speech.Articulating && speech.Mouth == 0;
            }
            if (_movie && !_headless)
            {
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng($"{_output}/{frame:D4}.png") != Error.Ok) throw new Exception("call frame export");
            }
            frame++;
        }
        Check(tilted && listenerBlink, "Mina tilts and blinks while listening with her mouth at rest");
        Check(metGaze && smiledWhileListening && gatheredThoughts && addressedOwner,
            "Mina meets the caller's eyes, smiles while listening, then looks back to deliver the welcome");
        Check(spoke && replyBlink && rested, "the welcome articulates, blinks during speech, and finishes at rest");
        Check(chestLed && headFollowed, "the welcome bow starts in the chest and carries through to the head");
        if (!_headless)
        {
            Check(initialReturn < .65f && peakTurnSpeed < 10 && lastTurnSpeed < .35f && listeningRoll - previousRoll > 7.5f,
                $"visible head return: {initialReturn:F2} deg in first 0.17s, peak {peakTurnSpeed:F2} deg/s, final {lastTurnSpeed:F2} deg/s");
            Vector2 span = highest - lowest;
            Check(faceLengthError < .02f && torsoLengthError < .02f,
                $"length drift throughout the greeting: face {faceLengthError:F4}px, brooch to waist {torsoLengthError:F4}px");
            Check(span.X < 2 && span.Y < 2,
                $"peak-to-peak vertical travel: shoulder midpoint {span.X:F2}px, brooch {span.Y:F2}px");
            Check(shoulderIncline > 5 && broochTravel > 3 && elbowTravel > 2 && hemTravel < .01f,
                $"in-game travel: shoulder {shoulderTravel:F1}px, collar inclination {shoulderIncline:F1}px across shoulders, brooch {broochTravel:F1}px, sleeve {elbowTravel:F1}px, waist {hemTravel:F2}px");
        }
        var pause = MinaCallPortrait.ReadSpeech("あ、い", null, .24);
        Check(!pause.Articulating && pause.Mouth == 0 && MinaCallPortrait.ReadSpeech("あ、い", null, .43).Mouth > .4f,
            "a comma closes the mouth and the next syllable reopens it");
        await Keypress(Key.H);
        double lineTime = Read<double>(hub, "_dlgLineT");
        double callTime = Read<double>(hub, "_minaCallTime");
        Vector2 heldGaze = Read<Vector2>(hub, "_minaGaze");
        await Frames(15);
        Check(Read<bool>(hub, "_minaHistoryOpen") && lineTime == Read<double>(hub, "_dlgLineT")
            && callTime == Read<double>(hub, "_minaCallTime") && heldGaze == Read<Vector2>(hub, "_minaGaze"),
            "the transcript pauses speech, gaze and gestures together");
        await Keypress(Key.H);
        Call(hub, "OpenMinaCall");
        Call(hub, "ProcessMinaCall", .5d);
        const double step = 1.0 / 120;
        float NeckDegrees() => Mathf.RadToDeg(Read<float>(hub, "_minaNeckAngle"));
        float before = NeckDegrees();
        Call(hub, "ProcessMinaCall", step);
        float atSwitch = NeckDegrees();
        Call(hub, "ChooseMinaLine");
        Call(hub, "ProcessMinaCall", step);
        float beforeSpeed = (atSwitch - before) / (float)step;
        float afterSpeed = (NeckDegrees() - atSwitch) / (float)step;
        Check(beforeSpeed > 1 && afterSpeed > 0 && Math.Abs(afterSpeed - beforeSpeed) < .6f,
            "advancing during a tilt carries its current velocity into the return");
        bool lookedAside = false, reconnected = false;
        for (int frameIndex = 0; frameIndex < 180; frameIndex++)
        {
            if (_movie && !_headless)
            {
                await Frames(1);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng($"{_output}/casual_{frameIndex:D4}.png") != Error.Ok) throw new Exception("casual call frame export");
            }
            else Call(hub, "ProcessMinaCall", 1d / 30);
            var delivery = (MinaCallPortrait.Speech)Call(hub, "get_MinaSpeech")!;
            var gaze = Read<Vector2>(hub, "_minaGaze");
            lookedAside |= delivery.Hesitating && delivery.Mouth == 0 && gaze.X > 5;
            reconnected |= lookedAside && delivery.Mouth > .4f && gaze.X < 0;
        }
        Check(lookedAside && reconnected, "the casual reply glances away at its ellipsis and reconnects when speech resumes");
        hub.QueueFree();
        await Frames(3);
        GD.Print($"[PhoneDesignQA] Mina call frames: {frame}");

        static void CheckQuiet(MinaCallPortrait.Speech speech)
        {
            if (speech.Articulating || speech.Mouth != 0) throw new Exception("Mina spoke during the player's turn");
        }
    }

    private async Task CheckMinaRestPose()
    {
        var rest = GD.Load<Texture2D>(CompanionDialogue.MinaPortrait);
        var viewport = new SubViewport { Size = (Vector2I)rest.GetSize(), TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var canvas = new Node2D { TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps };
        var rect = new Rect2(Vector2.Zero, rest.GetSize());
        bool mesh = false;
        Vector2 gaze = Vector2.Zero;
        MinaCallPortrait.Speech speech = default;
        var portrait = new MinaCallPortrait();
        portrait.Prepare(rest);
        canvas.Draw += () =>
        {
            if (mesh) portrait.Draw(canvas, rect, rest, default, 0, gaze, 0, speech, 1);
            else canvas.DrawTextureRect(rest, rect, false);
        };
        viewport.AddChild(canvas);
        AddChild(viewport);
        async Task Redraw()
        {
            canvas.QueueRedraw();
            await Frames(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        await Redraw();
        using var original = viewport.GetTexture().GetImage();
        mesh = true;
        await Redraw();
        using var rigged = viewport.GetTexture().GetImage();
        var before = original.GetData();
        var after = rigged.GetData();
        int peak = 0;
        for (int i = 0; i < before.Length; i++) peak = Math.Max(peak, Math.Abs(before[i] - after[i]));
        Check(peak <= 1, $"rest pose matches the source portrait at native resolution (max RGBA difference {peak})");
        gaze = new Vector2(9, 3.5f);
        await Redraw();
        var source = Read<Vector2[]>(portrait, "_source");
        var points = Read<Vector2[]>(portrait, "_points");
        Vector2 Travel(int x, int y) => points[Array.IndexOf(source, new Vector2(x, y))] - new Vector2(x, y);
        Check(Travel(448, 520).DistanceTo(gaze) < .01f && Travel(640, 544).DistanceTo(gaze) < .01f
            && Travel(448, 496).Length() < .01f && Travel(640, 520).Length() < .01f,
            "both irises track the gaze while the lash lines stay registered to the artwork");
        rest = GD.Load<Texture2D>(CompanionDialogue.MinaSmilePortrait);
        portrait.Prepare(rest);
        gaze = Vector2.Zero;
        mesh = false;
        await Redraw();
        using var smile = viewport.GetTexture().GetImage();
        mesh = true;
        speech = MinaCallPortrait.ReadSpeech("ただいま、ご主人様。", null, 1.8);
        await Redraw();
        using var punctuation = viewport.GetTexture().GetImage();
        Check(speech.End > 1.8 && !speech.Articulating && smile.GetData().AsSpan().SequenceEqual(punctuation.GetData()),
            "the final punctuation renders the complete original closed smile");
        speech = MinaCallPortrait.ReadSpeech("い", null, .03);
        await Redraw();
        Check(speech.Articulating && Travel(544, 680).Length() > 2
            && Travel(496, 696).Length() < .01f && Travel(592, 696).Length() < .01f && Travel(544, 720).Length() < .01f,
            "a narrow vowel moves the lips while both lower cheeks and the chin stay registered");
        viewport.QueueFree();
    }

    private async Task CheckLime(GameManager game)
    {
        game.ResetPersistent();
        game.AutoSaveEnabled = false;
        game.ShopTutorialSeen = true;
        var expected = new List<(string, string)>();
        var direct = new Dictionary<string, (string, string)[]>();
        foreach (var (stage, job) in new[] { ("akari", Job.Tank), ("koharu", Job.Heal), ("rei", Job.Magic) })
        {
            game.SelectedJob = job;
            game.CompleteStage(stage);
            var hub = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
            GetTree().Root.AddChild(hub);
            GetTree().CurrentScene = hub;
            await Frames(24);
            var lines = Read<(string, string)[]>(hub, "_dlg");
            if (stage == "akari")
            {
                Check(Read<bool>(hub, "_minaCall") && !Read<bool>(hub, "_limeConversation"), "two-person return opens Mina's line");
                Check(lines[0] == ("あなた", "おかえり、ミナ。") && lines[1] == ("ミナ", "ただいま、ご主人様。"), "private return starts with the welcome");
                await Keypress(Key.Enter);
                Check(Read<int>(hub, "_dlgIdx") == 1 && !(bool)Call(hub, "get_MinaCaptionReady")!, "Mina listens before answering");
                Call(hub, "ProcessMinaCall", 1.2d);
                Check((bool)Call(hub, "get_MinaCaptionReady")! && ((Texture2D)Call(hub, "MinaSpeakingFace")!).ResourcePath == "res://char/v3/mina_conversation_smile_v1.png",
                    "Mina smiles on her welcome reply");
                Check(Call(hub, "get_MinaCallAction") == null, "Mina's reply has no listen button");
                await Shot("mina_return");
                if (!_headless)
                {
                    await ClickLime(hub, new Vector2(772, 676));
                    Check(Read<int>(hub, "_dlgIdx") == 1, "the removed listen button has no hit area");
                    await ClickLime(hub, new Vector2(640, 565));
                    Check(Read<int>(hub, "_dlgIdx") == 2, "tapping the reply advances the call");
                }
                for (int presses = 0; presses < lines.Length * 4 && Read<bool>(hub, "_minaCall"); presses++)
                {
                    Call(hub, "ProcessMinaCall", 1.2d);
                    await Keypress(Key.Enter);
                }
                Check(game.LimeThread("mina").SequenceEqual(lines) && game.LimeHistory.Count == 0,
                    "return saves every private line once in Mina's transcript");
            }
            else
            {
                Check(Mode(hub) == "Dialogue" && Read<bool>(hub, "_limeConversation"), stage + ": group return opens LIME");
                expected.AddRange(lines);
            }
            bool captured = false;
            bool capturedTyping = false;
            for (int step = 0; step < lines.Length + 1 && Read<bool>(hub, "_limeConversation"); step++)
            {
                int index = Read<int>(hub, "_dlgIdx");
                int count = game.LimeHistory.Count;
                Call(hub, "DlgEnsurePages");
                string speaker = lines[index].Item1;
                string? Typing() => (string?)Call(hub, "get_LimeTypingSpeaker");
                if (speaker != "あなた" && !speaker.StartsWith("Ｙ") && !speaker.Contains("投稿"))
                {
                    Check(Typing() == speaker && Call(hub, "get_LimeIncoming") == null, "story reply starts with its sender typing");
                    if (!capturedTyping) { await Shot("story_typing_" + stage); capturedTyping = true; }
                    double remaining = (double)Call(hub, "get_LimeStoryTypingTime")! - Read<double>(hub, "_dlgLineT");
                    Call(hub, "ProcessLime", remaining + .05);
                    Check(Typing() == null && Call(hub, "get_LimeIncoming") != null && Read<int>(hub, "_dlgIdx") == index,
                        "story reply replaces typing and has its own reading time");
                }
                else Check(Typing() == null, "posted notices and outgoing drafts have no incoming typing indicator");
                Call(hub, "ProcessLime", 12d);
                if (lines[index].Item1 == "あなた")
                {
                    game.AutoAdvanceDialog = true;
                    Call(hub, "ProcessLime", 12d);
                    Check(Read<int>(hub, "_dlgIdx") == index && game.LimeHistory.Count == count,
                        "a completed draft waits for confirmation even with auto enabled");
                    if (!captured) { await Shot("draft_" + stage); captured = true; }
                    await Keypress(Key.Enter);
                    Check(game.LimeHistory.Count == count + 1 && game.LimeHistory[^1] == lines[index],
                        "Enter commits exactly one outgoing message");
                    game.AutoAdvanceDialog = false;
                }
                else Check(game.LimeHistory.Count == count + 1, "incoming line arrives automatically");
            }
            if (Mode(hub) == "HomeReveal") Call(hub, "ProcessHomeReveal", 3d);
            Check(Mode(hub) == "Home" && game.LimeHistory.SequenceEqual(expected),
                stage + ": return completes and retains the actual speakers and messages once");
            Check(game.IsIdleDialogSeen("once_account_intro"), "first return account introduction completes");
            if (stage == "akari")
            {
                Pad.ReleaseMouseFollow();
                Write(hub, "_homeSel", 0);
                var atlas = Read<PhoneVisuals>(hub, "_phoneVisuals").GetNode<SubViewport>("PhoneIconsView");
                var button = atlas.GetNode<Node3D>("MinaCallButton");
                Check(button.IsVisibleInTree() && button.FindChildren("*", "MeshInstance3D", true, false).Count > 0,
                    "the pinned Mina call entry uses the Blender model in the home icon atlas");
                await Shot("mina_home");
                if (_headless) { await Keypress(Key.Up); await Keypress(Key.Enter); }
                else await ClickLime(hub, (Transform2D)Call(hub, "HomePhoneTransform")! * new Vector2(640, 241));
                Check(Read<bool>(hub, "_minaCasual") && Read<bool>(hub, "_minaCall"), "home face opens a casual call");
                await Keypress(Key.H);
                Check(Read<bool>(hub, "_minaHistoryOpen") && Read<Array>(hub, "_minaArchive").Length == lines.Length,
                    "the call can display its existing private transcript");
                await Shot("mina_history");
                await Keypress(Key.Escape);
                Call(hub, "ProcessMinaCall", 1d);
                await Keypress(Key.Enter);
                await Keypress(Key.Escape);
                Check(Mode(hub) == "Home" && game.LimeThread("mina")[^1].Speaker == "あなた", "hanging up during the pause keeps the unanswered question");
                Check(game.SaveToSlot(3), "save the same pending question format used by the previous LIME version");
                await Keypress(Key.Enter);
                Check(Read<(string, string)[]>(hub, "_dlg").Length == 1 && Read<(string, string)[]>(hub, "_dlg")[0].Item1 == "ミナ",
                    "calling back delivers the outstanding reply");
                Call(hub, "ProcessMinaCall", 1.2d);
                Check(Read<MinaCallPortrait.BodyPose>(hub, "_minaBody").Bow == 0, "an ordinary reply keeps a standing posture");
                await Keypress(Key.Enter);
                direct["mina"] = game.LimeThread("mina").ToArray();
                await Keypress(Key.Escape);
                await Keypress(Key.Down);
            }
            Pad.ReleaseMouseFollow();
            Write(hub, "_homeSel", 9);
            await Frames(24);
            await Shot("lime_icon");
            await Keypress(Key.Z);
            Check(Mode(hub) == "LimeFriends", "LIME home icon opens the friend list");
            await Keypress(Key.Enter);
            Check(Mode(hub) == "LimeHistory", "group row opens the return conversation");
            Input.ActionPress("ui_up");
            await Frames(12);
            Input.ActionRelease("ui_up");
            if (expected.Count > 0) Check(Read<float>(hub, "_limeScroll") > 0, "history scrolls to earlier bubbles");
            await Keypress(Key.Escape);
            if (stage == "rei")
            {
                await Shot("friends");
                foreach (var (id, name) in new[] { ("koharu", "こはる"), ("rei", "レイ"), ("akari", "あかり") })
                {
                    await Keypress(Key.Down);
                    await Keypress(Key.Enter);
                    Check(Mode(hub) == "LimeDirect", name + ": opens a private chat");
                    Call(hub, "ProcessLime", 12d);
                    Call(hub, "ProcessLime", 12d);
                    var thread = game.LimeThread(id);
                    Check(thread.Count == 0, "a direct draft is absent from delivered history");
                    if (id == "koharu")
                    {
                        await Shot("direct_draft");
                        if (!_headless)
                        {
                            await ClickLime(hub, new Vector2(620, 330));
                            Check(thread.Count == 0, "clicking the transcript does not send");
                            await ClickLime(hub, new Vector2(840, 678));
                        }
                        else await Keypress(Key.Enter);
                    }
                    else await Keypress(Key.Enter);
                    Check(thread.Count == 1 && thread[0].Speaker == "あなた"
                        && (string?)Call(hub, "get_LimeTypingSpeaker") == name && Call(hub, "get_LimeDraft") == null,
                        name + ": only the outgoing message exists while the friend types");
                    if (id == "rei") Check(game.SaveToSlot(2), "save captures a pending direct reply");
                    if (id == "koharu") await Shot("direct_typing");
                    await Keypress(Key.Enter);
                    Call(hub, "TickLimeReplies", .8d);
                    Check(thread.Count == 1, "extra confirm does not rush delivery or send another message");
                    for (int frame = 0; thread.Count == 1 && frame < 220; frame++) await Frames(1);
                    Check(thread.Count == 2 && thread[0].Speaker == "あなた" && thread[1].Speaker == name,
                        "the selected contact replies to exactly one confirmed send");
                    Call(hub, "ProcessLime", 2d);
                    if (id == "koharu") await Shot("direct_reply");
                    await Keypress(Key.Enter);
                    await Keypress(Key.Escape);
                    Check(Mode(hub) == "LimeFriends" && thread.Count == 3, "leaving a chat does not expose a pending reply");
                    Call(hub, "TickLimeReplies", 4d);
                    Check(thread.Count == 4 && thread[2].Text != thread[0].Text && thread[3].Text != thread[1].Text,
                        name + ": follow-up exchange continues in the same thread");
                    direct[id] = thread.ToArray();
                    await Keypress(Key.Enter);
                    Check(Mode(hub) == "LimeDirect" && thread.SequenceEqual(direct[id]), "reopening does not resend");
                    await Keypress(Key.Escape);
                }
                await Shot("friends_with_history");
            }
            await Keypress(Key.Escape);
            Check(Mode(hub) == "Home" && game.LimeHistory.SequenceEqual(expected), "leaving history does not replay story");
            hub.QueueFree();
            await Frames(4);
        }
        Check(game.SaveToSlot(1), "conversation saved to the isolated slot");
        game.ResetPersistent();
        Check(game.LimeHistory.Count == 0 && direct.Keys.All(id => game.LimeThread(id).Count == 0)
            && game.LoadFromSlot(1) && game.LimeHistory.SequenceEqual(expected)
            && direct.All(pair => game.LimeThread(pair.Key).SequenceEqual(pair.Value)),
            "new game clears all threads and loading restores each conversation separately");
        Check(game.LoadFromSlot(2), "load save made while Rei was typing");
        var restored = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(restored);
        GetTree().CurrentScene = restored;
        var resumed = game.LimeThread("rei");
        Call(restored, "TickLimeReplies", 1d);
        Check(resumed.Count == 1, "restored reply is still pending");
        Call(restored, "TickLimeReplies", 4d);
        Call(restored, "TickLimeReplies", 4d);
        Check(resumed.SequenceEqual(direct["rei"].Take(2)), "restored pending reply is delivered exactly once");
        restored.QueueFree();
        await Frames(4);
        Check(game.LoadFromSlot(3), "load the pending private question");
        var legacy = GD.Load<PackedScene>("res://Hub.tscn").Instantiate<Hub>();
        GetTree().Root.AddChild(legacy);
        GetTree().CurrentScene = legacy;
        Call(legacy, "OpenMinaCall");
        var reply = Read<(string, string)[]>(legacy, "_dlg");
        Check(reply.Length == 1 && reply[0] == direct["mina"][^1], "an old pending Mina DM resumes on the private line");
        Call(legacy, "ProcessMinaCall", 1.2d);
        await Keypress(Key.Enter);
        Check(game.LimeThread("mina").SequenceEqual(direct["mina"]), "resumed call preserves the original transcript");
        legacy.QueueFree();
        await Frames(4);
    }

    private async Task ClickLime(Hub hub, Vector2 point)
    {
        hub.GetWindow().GrabFocus();
        hub.GetViewport().WarpMouse(point * UiKit.Scale);
        await Frames(3);
        foreach (bool pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed,
                Position = point * UiKit.Scale, GlobalPosition = point * UiKit.Scale });
            await Frames(3);
        }
        Pad.ReleaseMouseFollow();
    }

    private async Task Finish()
    {
        Audio.Instance?.StopMusic(0);
        foreach (var node in GetNode<Audio>("/root/Audio").GetChildren())
            if (node is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
        await Task.Delay(250);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GD.Print("[PhoneDesignQA] ALL PASS; state fixtures and UI input checks; not a usability study.");
        GetTree().Quit();
    }

    private async Task Keypress(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Frames(4);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(4);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (_headless) return;
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng($"{_output}/{name}.png") == Error.Ok, "screenshot " + name);
    }

    private async Task CheckScreenCorners(Hub hub, Transform2D pose, string name)
    {
        if (_headless) return;
        var mask = hub.GetNode<Node2D>("PhoneScreenMask");
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var visible = GetViewport().GetTexture().GetImage();
        Check(visible.SavePng($"{_output}/{name}.png") == Error.Ok, "screenshot " + name);
        mask.Visible = false;
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var background = GetViewport().GetTexture().GetImage();
        mask.Visible = true;
        int checkedPixels = 0;
        float largestDifference = 0;
        foreach (bool right in new[] { false, true })
        foreach (bool bottom in new[] { false, true })
        foreach (var inset in new[] { new Vector2(4, 4), new Vector2(8, 8), new Vector2(4, 12), new Vector2(12, 4) })
        {
            Vector2 local = new(right ? 880 - inset.X : 400 + inset.X, bottom ? 720 - inset.Y : inset.Y);
            Vector2 position = pose * local * visible.GetWidth() / 1280f;
            int x = Mathf.RoundToInt(position.X), y = Mathf.RoundToInt(position.Y);
            Color actual = visible.GetPixel(x, y), expected = background.GetPixel(x, y);
            float difference = Mathf.Max(Mathf.Abs(actual.R - expected.R), Mathf.Max(Mathf.Abs(actual.G - expected.G), Mathf.Abs(actual.B - expected.B)));
            largestDifference = Mathf.Max(largestDifference, difference);
            checkedPixels++;
        }
        Check(checkedPixels == 16 && largestDifference <= 2f / 255,
            $"{name}: all four rounded corners preserve the phone rim/background; max difference={largestDifference:F4}");
        await Frames(4);
    }
}
