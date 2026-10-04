using Godot;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class ScenarioRevisionQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private GameManager _game = null!;
    private static T Read<T>(object o, string name, Type? type = null)
        => (T)(type ?? o.GetType()).GetField(name, Private)!.GetValue(o)!;
    private static void Write(object o, string name, object value, Type? type = null)
        => (type ?? o.GetType()).GetField(name, Private)!.SetValue(o, value);
    private static object? Call(object o, string name, params object[] args)
        => o.GetType().GetMethod(name, Private)!.Invoke(o, args);
    private static T Field<T>(Type type, string name) => (T)type.GetField(name, Static)!.GetValue(null)!;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        GD.Print("[ScenarioQA] PASS " + message);
    }
    private static string[] Texts(IList lines) => lines.Cast<object>()
        .Select(line => (string)line.GetType().GetField("Text")!.GetValue(line)!).ToArray();

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            _game = GetNode<GameManager>("/root/Game");
            _game.AutoSaveEnabled = false;
            _game.ResetPersistent();
            _game.MsgCharsPerSec = 300;
            _game.AutoAdvanceDialog = true;
            await Frames(2);
            CheckChoices();
            CheckCompanions();
            await CheckPrologue();
            foreach (var id in new[] { "Akari", "Koharu", "Rei" }) await CheckMemory(id);
            foreach (var job in new[] { Job.Tank, Job.Melee, Job.Heal, Job.Magic }) await CheckFinalEntry(job);
            await CheckMinaArt();
            await CheckEndings();
            await CheckEndingArt();
            GetNode<BulletPool>("/root/Pool").DespawnAll();
            Audio.Instance?.StopMusic(0);
            foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { audio.Stop(); audio.Stream = null; }
            await Frames(5);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GD.Print("[ScenarioQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError("[ScenarioQA] FAIL " + ex);
            GetTree().Paused = false;
            GetTree().Quit(1);
        }
    }

    private void CheckChoices()
    {
        int count = 0;
        foreach (var type in new[] { typeof(Prologue), typeof(StageAkari), typeof(StageKoharu), typeof(StageRei), typeof(Final), typeof(Epilogue) })
        foreach (var field in type.GetFields(Static).Where(f => f.Name.EndsWith("Choices") && f.FieldType == typeof(string[])))
        {
            var choices = (string[])field.GetValue(null)!;
            Check(choices.Length == 2 && choices.Distinct().Count() == 2, type.Name + "." + field.Name + " has two distinct choices");
            count++;
            string reply = field.Name[..^7] + "Reply";
            var method = type.GetMethod(reply, Static);
            if (method == null) continue;
            for (int i = 0; i < 2; i++)
            {
                var lines = ((int who, string text, string face)[])method.Invoke(null, new object[] { i })!;
                Check(lines[0].who == 0 && lines[0].text == choices[i]
                    && !lines.Any(l => l.text == choices[1 - i]), type.Name + "." + reply + " keeps branch " + i + " separate");
            }
        }
        Check(count == 14, "all fourteen story choices covered");
        foreach (string name in new[] { "Route", "SkillDodge", "SkillCharge", "SkillCharge2" })
        {
            var lines = Field<(int who, string text, string face)[]>(typeof(StageTutorial), name);
            var cues = Field<ControlCard.Topic[]>(typeof(StageTutorial), name + "Cues");
            Check(lines.Length == cues.Length && lines[0].who == 0, name + " action cards follow the guide's dialogue");
        }
        var intro = typeof(CameoIntroScene).GetMethod("Dialogue", Static)!;
        var bossIntro = typeof(CameoIntroScene).GetMethod("BossDialogue", Static)!;
        foreach (var type in new[] { typeof(StageAkari), typeof(StageKoharu), typeof(StageRei) })
        foreach (var pair in new[] { ("CameoTalk1", intro), ("BossIntro", bossIntro) })
        {
            var opening = Field<(int who, string text, string face)[]>(type, pair.Item1);
            var lines = ((int who, string text, string face)[])pair.Item2.Invoke(null, new object[] { Job.Tank, type.Name[5..].ToLowerInvariant(), opening })!;
            Check(lines.SequenceEqual(opening), type.Name + "." + pair.Item1 + " reaches the renderer without dropped dialogue");
        }
        for (int i = 0; i < 2; i++)
        {
            var choices = Field<string[]>(typeof(StageKoharu), "S21Choices");
            _game.RecordChoice("s2_1", choices[i], Array.Empty<string>(), 1);
            var lines = ((int who, string text, string face)[])typeof(StageKoharu).GetMethod("CameoIntroFor", Static)!.Invoke(null, new object[] { _game })!;
            Check(lines.SequenceEqual(Field<(int who, string text, string face)[]>(typeof(StageKoharu), i == 0 ? "CameoTalk1_S21Rest" : "CameoTalk1_S21Keep")), "Koharu encounter follows the newly worded choice");
            Check(Fury.InitialFor(_game, "koharu") == (i == 0 ? -12f : 6f), "choice retains its downstream emotional effect");
        }
    }

    private void CheckCompanions()
    {
        foreach (var job in new[] { Job.Melee, Job.Heal, Job.Magic })
        {
            foreach (var menu in Enum.GetValues<CompanionDialogue.Menu>())
            {
                var lines = CompanionDialogue.MenuDialogue(job, menu);
                Check(lines.Any(l => l.who == 0) && lines.Any(l => l.who == 6)
                    && lines.All(l => l.who is 0 or 6), job + "/" + menu + " pairs the guide with the selected ally");
            }
            foreach (int chapter in new[] { 1, 2, 3, 4 })
            foreach (var beat in Enum.GetValues<CharacterStory.Beat>())
            {
                var lines = CharacterStory.Lines(job, chapter, beat);
                Check(lines.Length > 0 && lines.All(l => l.who is 0 or 3 or 4 or 6), job + "/" + chapter + "/" + beat + " has no extra companion");
            }
            foreach (string id in new[] { "akari", "koharu", "rei" })
            {
                var lines = CharacterStory.Memory(job, id);
                Check(lines.Any(l => l.who == 2) && lines.Any(l => l.who == 6)
                    && lines.All(l => l.who is 0 or 2 or 6), job + "/" + id + " retains distinct ally and boss speakers");
            }
        }
    }

    private async Task CheckPrologue()
    {
        var pro = GD.Load<PackedScene>("res://Prologue.tscn").Instantiate<Prologue>();
        GetTree().Root.AddChild(pro);
        pro.SetProcess(false);
        string[] choices = Field<string[]>(typeof(Prologue), "P2Choices");
        var first = Texts((IList)Call(pro, "P2Reply", choices[0])!);
        var second = Texts((IList)Call(pro, "P2Reply", choices[1])!);
        Check(first.Any(t => t.Contains("37回目")) && !first.Contains("……成功ですよ。")
            && second.Contains("……成功ですよ。"), "boot success and failure joke are separate branches");
        var named = Texts((IList)Call(pro, "P3Reply", 0)!);
        var parody = Texts((IList)Call(pro, "P3Reply", 1)!);
        Check(!named.Any(t => t.Contains("コンタクトインターフェース")) && parody.Any(t => t.Contains("それがわたし。")), "naming parody only plays on its selected branch");
        Check(named.Contains("[ M I N A ]") && parody.Contains("[ M I N A ]"), "both naming branches retain the ignition cue");
        var intro = Texts((IList)Call(pro, "P4Intro")!);
        Check(intro.Count(t => t.StartsWith("fx:")) == 4 && intro.Any(t => t.Contains("僕ひとりじゃ届かなかった")), "notification effects lead into the observation and isolation explanation");
        if (DisplayServer.GetName() != "headless")
        {
            var talk = Read<IList>(pro, "_talk");
            talk.Clear();
            foreach (object line in (IList)Call(pro, "P3Reply", 1)!) talk.Add(line);
            Write(pro, "_phase", 3);
            Write(pro, "_line", 0);
            Write(pro, "_p2ChoiceLine", -1);
            Call(pro, "EnsurePages");
            Write(pro, "_reveal", 999f);
            GameManager.MinaNamed = true;
            await Shot("prologue_parody");
        }
        pro.QueueFree();
        await Frames(3);
    }

    private async Task CheckMemory(string id)
    {
        _game.SelectedJob = Job.Tank;
        _game.SelectedEntry = GameManager.StageEntry.Start;
        _game.Difficulty = GameManager.Diff.Normal;
        var root = GD.Load<PackedScene>($"res://{id}.tscn").Instantiate<Node2D>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        root.GetNode("Stage" + id).SetProcess(false);
        var world = root.GetNode<Node2D>("World");
        var hud = root.GetNode<Hud>("Hud");
        world.ProcessMode = ProcessModeEnum.Inherit;
        world.GetNode<Player>("Player").SetPhysicsProcess(false);
        Enemy boss = id switch { "Akari" => new BossAkari(), "Koharu" => new BossKoharu(), _ => new BossRei() };
        world.AddChild(boss);
        boss.SetProcess(false);
        boss.SetPhysicsProcess(false);
        hud.HoldBubble = false;
        hud.HideBubble();
        Write(boss, "_memoryPending", true);
        boss._Process(0.01);
        var talk = Read<CharacterStoryTalk>(boss, "_memoryTalk");
        Check(talk.Active && Hud.BubblePaused && GetTree().GetFirstNodeInGroup("storyfilm") == null,
            id + " pauses combat for conversation before showing the memory");
        var lines = Read<(int who, string text, string face)[]>(talk, "_lines");
        Check(lines.Length >= 6 && lines.Any(l => l.who == 1) && lines.Any(l => l.who == 2)
            && lines.All(l => l.who != 0), id + " boss answers Mina, never the operator");
        hud.RevealDialogNow();
        await Shot(id + "_before_memory");
        for (int i = 0; i < 150 && talk.Active; i++)
        {
            hud.RevealDialogNow();
            talk.Update(2);
            await Frames(1);
        }
        var film = GetTree().GetFirstNodeInGroup("storyfilm") as StoryFilm;
        Check(!talk.Active && film != null, id + " starts the film only after the exchange finishes");
        film!.SetProcess(false);
        for (int i = 0; i < 180 && IsInstanceValid(film); i++)
        {
            hud.RevealDialogNow();
            film._Process(2);
            await Frames(1);
        }
        await ToSignal(GetTree().CreateTimer(4.5), SceneTreeTimer.SignalName.Timeout);
        Check(!IsInstanceValid(film) && !hud.CinematicMode && !Hud.BubblePaused,
            id + $" returns control after the memory (film={IsInstanceValid(film)}, cinematic={hud.CinematicMode}, paused={Hud.BubblePaused}, held={hud.HoldBubble})");
        root.QueueFree();
        await Frames(4);
    }

    private async Task CheckFinalEntry(Job requested)
    {
        _game.SelectedJob = requested;
        _game.SelectedEntry = GameManager.StageEntry.Start;
        var root = GD.Load<PackedScene>("res://MinaBattle.tscn").Instantiate<MinaRoot>();
        GetTree().Root.AddChild(root);
        GetTree().CurrentScene = root;
        root.SetProcess(false);
        root.Stage.SetProcess(false);
        Job expected = requested == Job.Tank ? Job.Magic : requested;
        Check(_game.SelectedJob == expected && root.Player.CharacterId == Jobs.Get(expected).CharacterId,
            requested + " enters FINAL as an ally, with Rei as the default");
        var intro = Read<(int who, string text, string face)[]>(root.Stage, "_intro");
        Check(intro[0].text.StartsWith(Jobs.Get(expected).CharacterName + "さん")
            && intro.Any(l => l.who == 6) && intro.Any(l => l.who == 0), "FINAL introduction matches the actual rescuer");
        for (int choice = 0; choice < 2; choice++)
        {
            string word = Field<string[]>(typeof(StageRei), "S37Choices")[choice];
            _game.RecordChoice("s3_7", word, Array.Empty<string>(), 1);
            var reply = ((int who, string text, string face)[])typeof(StageMina).GetMethod("S37Quote", Static)!.Invoke(null, new object[] { _game })!;
            Check(reply[0].who == 0 && reply[1].who == 6 && reply.All(l => l.who != 1), "past choice is discussed privately with the rescuer");
        }
        root.QueueFree();
        await Frames(4);
    }

    private async Task CheckEndings()
    {
        for (int choice = 0; choice < 2; choice++)
        {
            var final = GD.Load<PackedScene>("res://Final.tscn").Instantiate<Final>();
            GetTree().Root.AddChild(final);
            final.SetProcess(false);
            var talk = Read<IList>(final, "_talk");
            Write(final, "_line", talk.Count);
            if (choice == 0 && DisplayServer.GetName() != "headless")
            {
                Call(final, "ShowFinalChoice");
                await Shot("final_choices");
                Read<ChoiceOverlay>(final, "_choice").QueueFree();
            }
            Call(final, "ApplyFinalChoice", choice);
            string sent = _game.ChosenAt("f4");
            Check(sent.Length > 0 && Texts(talk).Contains(sent) && !Texts(talk).Contains(Field<string[]>(typeof(Final), "FinalChoices")[1 - choice]), "F13 records and plays only the selected emotional reply");
            final.QueueFree();
            await Frames(3);
            for (int name = 0; name < 2; name++)
            {
                _game.NameRoute = name;
                var end = GD.Load<PackedScene>("res://Epilogue.tscn").Instantiate<Epilogue>();
                GetTree().Root.AddChild(end);
                end.SetProcess(false);
                Call(end, "ApplyE6Choice", choice);
                string[] texts = Texts(Read<IList>(end, "_end"));
                Check(texts.Any(t => t.Contains("長い肩書き")) == (name == 1)
                    && texts.Any(t => t.Contains("37番目だから")) == (name == 0), "epilogue recalls only the chosen naming route");
                Check(Read<string>(end, "_rollLast") == sent, "credits retain F13 even after the epilogue choice");
                if (choice == 0 && name == 0)
                {
                    var gaze = Read<IList>(end, "_gaze");
                    var art = Read<System.Collections.Generic.Dictionary<string, Texture2D>>(end, "_callArt");
                    string partner = "ミナ";
                    for (int line = 0; line < gaze.Count; line++)
                    {
                        var entry = gaze[line]!;
                        string text = (string)entry.GetType().GetField("Text")!.GetValue(entry)!;
                        string who = (string)entry.GetType().GetField("Who")!.GetValue(entry)!;
                        if (who == "地") partner = text.Replace("との回線", "");
                        Write(end, "_line", line);
                        string actual = (string)typeof(Epilogue).GetProperty("GazePartner", Private)!.GetValue(end)!;
                        Check(actual == partner, "call image keeps " + partner + " while " + who + " speaks");
                        Check(!art[actual].ResourcePath.Contains("together") && !art[actual].ResourcePath.EndsWith("cg_ep_rest.png"),
                            "individual calls never show the old group CG");
                        if (who == partner)
                        {
                            Write(end, "_pagedKey", -1);
                            Write(end, "_page", 0);
                            Call(end, "EnsurePages");
                            Write(end, "_reveal", 10000.0);
                            Write(end, "_lineT", 1.0);
                            end.QueueRedraw();
                            await Shot("call_" + partner);
                        }
                    }
                    Write(end, "_phase", 3);
                    Write(end, "_line", 0);
                    Write(end, "_pagedKey", -1);
                    Write(end, "_page", 0);
                    Call(end, "EnsurePages");
                    Write(end, "_reveal", 10000.0);
                    end.QueueRedraw();
                    await Shot("call_mina_end");
                }
                end.QueueFree();
                await Frames(3);
            }
        }
    }

    private async Task CheckMinaArt()
    {
        foreach (var job in new[] { Job.Melee, Job.Heal, Job.Magic })
        {
            _game.SelectedJob = job;
            var root = GD.Load<PackedScene>("res://MinaBattle.tscn").Instantiate<MinaRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            root.Stage.SetProcess(false);
            _game.AutoAdvanceDialog = false;
            MinaStoryFilm.Play(root.Hud, root.World, true, () => { });
            var film = (StoryFilm)GetTree().GetFirstNodeInGroup("storyfilm");
            var lines = Read<IList>(film, "_lines", typeof(StoryFilm));
            var shots = Read<System.Collections.Generic.Dictionary<int, string>>(film, "_shotImages", typeof(StoryFilm));
            string rescuer = Jobs.Get(job).CharacterId;
            Check(shots[5].EndsWith("cg_mina_rescue_" + rescuer + "_v1.png"), "rescue CG matches " + rescuer);
            Check(!shots.Values.Any(p => p.Contains("reunion") || p.Contains("take_hand")), "rescued Mina never returns to the old black-haired group CG");
            await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
            film.SetProcess(false);
            foreach (int lineIndex in new[] { 0, 10, 11, 12, 13 })
            {
                var entry = lines[lineIndex]!;
                int shot = (int)entry.GetType().GetProperty("Shot")!.GetValue(entry)!;
                Write(film, "_line", lineIndex, typeof(StoryFilm));
                typeof(StoryFilm).GetMethod("ShowLine", Private)!.Invoke(film, null);
                typeof(StoryFilm).GetMethod("ShowLineText", Private)!.Invoke(film, null);
                var grade = Read<ShaderMaterial>(film, "_grade", typeof(StoryFilm));
                grade.SetShaderParameter("blend_amount", 1f);
                var texture = grade.GetShaderParameter("scene_texture").AsGodotObject() as Texture2D;
                Check(texture?.ResourcePath == shots[shot], "rendered shot " + shot + " uses its assigned illustration");
                if (lineIndex is >= 10 and <= 12)
                {
                    string id = new[] { "akari", "koharu", "rei" }[lineIndex - 10];
                    Check(texture!.ResourcePath.EndsWith("cg_ep_" + id + "_v1.png"), "news clip shows " + id + " in daily life");
                }
                root.Hud.RevealDialogNow();
                film.QueueRedraw();
                if (lineIndex == 0 || job == Job.Magic) await Shot("rescue_" + rescuer + "_" + shot);
            }
            film.QueueFree();
            await Frames(2);
            root.QueueFree();
            await Frames(4);
        }
    }

    private async Task CheckEndingArt()
    {
        var photos = Field<IList>(typeof(Hub), "PhotoEntries");
        foreach (var photo in photos)
        {
            string id = (string)photo.GetType().GetProperty("Id")!.GetValue(photo)!;
            if (id is not ("mina_after" or "ending")) continue;
            string path = (string)photo.GetType().GetProperty("Path")!.GetValue(photo)!;
            Check(!path.Contains("together") && !path.Contains("take_hand") && ResourceLoader.Exists(path),
                id + " gallery entry matches the revised story");
        }
        var film = new EndingFilm();
        GetTree().Root.AddChild(film);
        film.SetProcess(false);
        var cuts = Field<double[]>(typeof(EndingFilm), "Cuts");
        var art = Read<Texture2D[]>(film, "_art");
        Check(art.Length == cuts.Length - 1 && Field<string[]>(typeof(EndingFilm), "Lines").Length == art.Length
            && Field<string[]>(typeof(EndingFilm), "Speakers").Length == art.Length, "every ending cut has artwork, a speaker and a caption entry");
        Check(art.All(t => !t.ResourcePath.Contains("together") && !t.ResourcePath.EndsWith("cg_ep_rest.png")),
            "ending montage uses individual scenes");
        foreach (double time in new[] { 0.2, 31.5, 37.5, 48.9, 49.0, 52.9 })
        {
            typeof(EndingFilm).GetProperty("Elapsed")!.SetValue(film, time);
            film._Process(0);
            film.QueueRedraw();
            await Frames(3);
        }
        await Shot("ending_last_cut");
        film.QueueFree();
        await Frames(4);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string folder = ProjectSettings.GlobalizePath("res://build/scenario_sync/shots");
        DirAccess.MakeDirRecursiveAbsolute(folder);
        GetViewport().GetTexture().GetImage().SavePng(folder + "/" + name + ".png");
    }
}
