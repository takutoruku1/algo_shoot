using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class BossBattleDialogueQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private BossPracticeRoot _practice = null!;
    private static FieldInfo FindField(object obj, string name)
    {
        for (Type? type = obj.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Private) is { } field) return field;
        throw new MissingFieldException(name);
    }
    private static T Read<T>(object obj, string field) => (T)FindField(obj, field).GetValue(obj)!;
    private static void Write(object obj, string field, object value) => FindField(obj, field).SetValue(obj, value);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print("[BossBattleDialogueQA] PASS " + message);
    }

    public override async void _Ready()
    {
        int exit = 1;
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated saves");
            typeof(Settings).GetProperty(nameof(Settings.PauseWhenUnfocused))!.SetValue(null, false);
            bool screenshots = OS.GetCmdlineUserArgs().Contains("--speech-shots");
            if (screenshots)
            {
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(1280, 720));
                DisplayServer.WindowSetPosition(new Vector2I(-16000, -16000));
            }
            var game = GetNode<GameManager>("/root/Game");
            game.ResetPersistent();
            game.SelectedJob = Job.Melee;
            _practice = GD.Load<PackedScene>(BossPracticeRoot.ScenePath).Instantiate<BossPracticeRoot>();
            AddChild(_practice);
            _practice.SetProcess(false);
            int cases = 0;
            for (int tier = screenshots ? 2 : 0; tier < (screenshots ? 3 : 4); tier++)
            for (int bossIndex = 0; bossIndex < 4; bossIndex++)
            for (int phase = 0; phase < (bossIndex == 3 ? 5 : 2); phase++)
            {
                Configure(bossIndex, phase, tier);
                Check(!IsInstanceValid(Read<Hud>(_practice, "_hud")), "catalog preview has no combat HUD");
                _practice.StartPractice();
                var boss = _practice.PracticeBoss;
                var hud = Read<Hud>(_practice, "_hud");
                boss.SetPhysicsProcess(false);
                boss.SetProcess(false);
                _practice.PracticePlayer.SetPhysicsProcess(false);
                var slot = Read<BossAttackCycle.Slot[]>(_practice, "_slots")[0];
                var attack = BossCharacterTechnique.Begin(boss, slot.Kind, slot.Variant);
                for (int frame = 0; frame < 180 && attack.Elapsed <= .1 && !attack.Finished; frame++)
                {
                    boss.TickBossFrame(1d / 60);
                    await Frames(1);
                }
                Check(Read<string>(hud, "_bossLine") == "",
                    $"no attack speech tier={tier} boss={bossIndex} phase={phase}");
                Check(!Hud.BubblePaused && !hud.HoldBubble && !GetTree().Paused && attack.Elapsed > .1,
                    $"combat continues: elapsed={attack.Elapsed:0.00}, dialogue={Hud.BubblePaused}, hold={hud.HoldBubble}, paused={GetTree().Paused}");
                hud.ShowBossLine("敵", "戦闘中の演出セリフ", Colors.White, 3);
                Check(Read<string>(hud, "_bossLine") == "", "legacy combat subtitles are suppressed");
                if (screenshots && tier == 2 && (phase == 0 || phase == (bossIndex == 3 ? 4 : 1)))
                    await Shot($"boss_{bossIndex}_phase_{phase}");
                attack.Cancel();
                cases++;
            }
            GD.Print($"[BossBattleDialogueQA] battle cases={cases}");

            Configure(0, 0, 0);
            _practice.StartPractice();
            var owner = _practice.PracticeBoss;
            owner.SetProcess(false);
            owner.SetPhysicsProcess(false);
            _practice.PracticePlayer.SetPhysicsProcess(false);
            var activeHud = Read<Hud>(_practice, "_hud");
            var longAttack = BossCharacterTechnique.Begin(owner, BossTechniqueKind.Normal, 2);
            bool silent = true;
            for (int frame = 0; frame < 720; frame++)
            {
                await Frames(1);
                silent &= Read<string>(activeHud, "_bossLine") == "";
            }
            Check(silent, "long attack has no opening or followup speech");
            Check(longAttack.ShotsCreated > 0 && !Hud.BubblePaused, "shots still fire without dialogue");
            if (screenshots) await Shot("live_attack_no_dialogue");
            longAttack.Cancel();

            activeHud.ShowBossLine("", "引用: 3", Colors.White, 3);
            Check(Read<string>(activeHud, "_bossLine") == "引用: 3", "mechanic counters remain available");
            owner.Hide();
            activeHud.ShowBossLine("あかり", "戦闘後のセリフ", Colors.White, 3);
            Check(Read<string>(activeHud, "_bossLine") == "戦闘後のセリフ", "non-combat subtitles remain available");
            owner.Show();
            await Frames(2);
            Check(Read<string>(activeHud, "_bossLine") == "", "combat clears an existing flavour subtitle");
            activeHud.ShowDialog("通常会話の検証", "res://char/v3/akari_face.png");
            await Frames(2);
            Check(Hud.BubblePaused && Read<string>(activeHud, "_dlgText") == "通常会話の検証",
                "story dialogue retains its normal pause and text");
            activeHud.HideBubble();
            await Frames(2);

            var rush = new Node();
            AddChild(rush);
            rush.AddToGroup("boss_rush");
            activeHud.ShowBossLine("あかり", "戦闘中の演出セリフ", Colors.White, 3);
            Check(GameManager.BossRushActive && Read<string>(activeHud, "_bossLine") == "",
                "boss rush does not show combat subtitles");
            rush.Free();

            Configure(2, 0, 2);
            _practice.StartPractice();
            owner = _practice.PracticeBoss;
            owner.SetProcess(false);
            owner.SetPhysicsProcess(false);
            _practice.PracticePlayer.SetPhysicsProcess(false);
            var colorCall = (ReiBossTechnique)BossCharacterTechnique.Begin(owner, BossTechniqueKind.Area, 3);
            for (int frame = 0; frame < 480 && colorCall.ColorCallCue == null; frame++) await Frames(1);
            Check(colorCall.ColorCallCue is { Index: 0 } && !colorCall.IsStriking, "Rei shows the stamp paths before they reach the player");
            Check(Read<string>(Read<Hud>(_practice, "_hud"), "_bossLine") == "", "mechanic cue has no flavour dialogue");
            if (screenshots) await Shot("rei_color_call_cue");
            colorCall.Cancel();
            GD.Print("[BossBattleDialogueQA] ALL PASS");
            exit = 0;
        }
        catch (Exception ex)
        {
            GD.PushError("[BossBattleDialogueQA] FAIL " + ex);
        }
        if (IsInstanceValid(_practice)) _practice.QueueFree();
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        foreach (var player in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
        {
            player.Stop();
            player.Stream = null;
        }
        await Frames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(5);
        GetTree().Quit(exit);
    }

    private void Configure(int boss, int phase, int difficulty)
    {
        Write(_practice, "_bossIndex", boss);
        Write(_practice, "_phase", phase);
        Write(_practice, "_difficulty", difficulty);
        Write(_practice, "_technique", 0);
        _practice.ShowSelection();
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string output = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--output="))?[9..]
            ?? "res://build/qa_story/combat_no_dialogue_20261103/screenshots";
        string path = System.IO.Path.Combine(ProjectSettings.GlobalizePath(output), name + ".png");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(path) == Error.Ok, "screenshot " + name);
    }
}
