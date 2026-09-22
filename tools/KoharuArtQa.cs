using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

public partial class KoharuArtQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object obj, string field, Type? type = null)
        => (T)(type ?? obj.GetType()).GetField(field, Private)!.GetValue(obj)!;
    private static void Write(object obj, string field, object value, Type? type = null)
        => (type ?? obj.GetType()).GetField(field, Private)!.SetValue(obj, value);
    private static void Call(object obj, Type type, string method, params object[] args)
        => type.GetMethod(method, Private)!.Invoke(obj, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        GD.Print($"[KoharuArtQA] PASS {message}");
    }

    public override async void _Ready()
    {
        try
        {
            Check(OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"), "isolated save data");
            var game = GetNode<GameManager>("/root/Game");
            game.AutoSaveEnabled = false;
            game.SelectedJob = Job.Tank;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            using var manifest = JsonDocument.Parse(FileAccess.GetFileAsString("res://char/v3/koharu_redesign_v1.prompt.json"));
            foreach (var asset in manifest.RootElement.GetProperty("assets").EnumerateArray())
            {
                string path = "res://" + asset.GetProperty("target").GetString();
                var texture = GD.Load<Texture2D>(path);
                using var pixels = texture.GetImage();
                Check(pixels.GetUsedRect().Size.X > 20 && pixels.GetUsedRect().Size.Y > 20, $"nonempty imported image {path}");
                if (asset.TryGetProperty("height", out var height))
                {
                    Check(texture.GetHeight() == height.GetInt32(), "sprite retains expected resolution");
                    Check(pixels.GetPixel(0, 0).A < 0.1f && pixels.GetPixel(pixels.GetWidth() - 1, 0).A < 0.1f,
                        "sprite background is transparent");
                }
                else if (!path.Contains("atlas"))
                    Check(Mathf.Abs(texture.GetWidth() / (float)texture.GetHeight() - 16f / 9f) < 0.01f,
                        "story composition remains 16:9");
            }
            await Frames(1);
            var root = GD.Load<PackedScene>("res://Koharu.tscn").Instantiate<KoharuRoot>();
            GetTree().Root.AddChild(root);
            GetTree().CurrentScene = root;
            root.SetProcess(false);
            root.Stage.SetProcess(false);
            root.Player.SetPhysicsProcess(false);
            root.World.ProcessMode = ProcessModeEnum.Inherit;
            root.Hud.HideBubble();
            Write(root.Hud, "_bannerTimer", 0d);
            Write(root.Stage, "_stepStarted", false);
            Call(root.Stage, typeof(StageKoharu), "Step_BossCameo", 0d);
            var cameo = root.World.GetNode<CameoBoss>("KoharuCameo");
            Freeze(cameo);
            Check(cameo.GetNode<Sprite2D>("Body").Texture.ResourcePath.EndsWith("koharu_mid.png"),
                "stage creates the redesigned midboss");
            await Shot("midboss");
            cameo.QueueFree();
            await Frames(3);
            root.Hud.HideBubble();
            root.GetNode<StageBackground>("StageBackground").EnterBoss();
            foreach (bool flip in new[] { true, false })
            {
                var boss = new BossKoharu();
                Write(boss, "FaceLeft", flip, typeof(Enemy));
                root.World.AddChild(boss);
                Freeze(boss);
                Read<AreaSpellCaster>(boss, "_caster").SetProcess(false);
                root.Hud.HideSpellCard();
                var body = boss.GetNode<Sprite2D>("Body");
                Vector2 anchor = Foot(body, new Vector2(219, 718));
                float personHeight = body.Scale.Y * 718;
                await Shot($"boss_idle_{flip}");
                Call(boss, typeof(Enemy), "TriggerAttackPose");
                Call(boss, typeof(Enemy), "TickSwapAnim", 1d);
                Check(body.Texture.ResourcePath.EndsWith("body_attack.png"), "attack pose is wired");
                CheckPose(body, anchor, personHeight, 75, new Vector2(263, 718));
                var tip = LocalPoint(body, new Vector2(469, 7));
                var muzzle = BossParts.AnchorMuzzle("koharu", Read<float>(boss, "BodyDisplayH", typeof(Enemy)), 585);
                if (flip) muzzle.X = -muzzle.X;
                Check(tip.DistanceTo(muzzle) < 0.1f, "penlight effect anchor matches the new attack pose");
                await Shot($"boss_attack_{flip}");
                Call(boss, typeof(Enemy), "TickAttackPose", 1d);
                Call(boss, typeof(Enemy), "AdvanceForm2");
                Call(boss, typeof(Enemy), "TickSwapAnim", 1d);
                CheckPose(body, anchor, personHeight, 0, new Vector2(235, 718));
                Call(boss, typeof(Enemy), "TriggerAttackPose");
                Call(boss, typeof(Enemy), "TickAttackPose", 1d);
                Call(boss, typeof(Enemy), "TickSwapAnim", 1d);
                Check(body.Texture.ResourcePath.EndsWith("body_idle2.png"), "attack returns to the second form");
                CheckPose(body, anchor, personHeight, 0, new Vector2(235, 718));
                await Shot($"boss_form2_{flip}");
                foreach (var (path, head, foot) in new[] {
                    ("boss_koharu_body_cry.png", 0f, new Vector2(220, 718)),
                    ("enemy_koharu_post.png", 0f, new Vector2(93, 359)) })
                {
                    Call(boss, typeof(Enemy), "SwapBody", "res://char/v3/" + path, 1f);
                    Call(boss, typeof(Enemy), "TickSwapAnim", 1d);
                    CheckPose(body, anchor, personHeight, head, foot);
                    await Shot($"{path.GetBaseName()}_{flip}");
                }
                Check(Read<float>(boss, "BodyRadius", typeof(Enemy)) == BossTuning.F("koharu", "body_radius", 19)
                    && Read<float>(boss, "BodyHalfH", typeof(Enemy)) == BossTuning.F("koharu", "body_half_h", 23),
                    "art changes do not alter combat hitboxes");
                boss.QueueFree();
                await Frames(3);
            }
            root.QueueFree();
            Audio.Instance?.StopMusic(0);
            foreach (var child in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
            { child.Stop(); child.Stream = null; }
            await Frames(5);
            GD.Print("[KoharuArtQA] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError($"[KoharuArtQA] FAIL {ex}");
            GetTree().Quit(1);
        }
    }

    private static void Freeze(Enemy enemy)
    {
        enemy.SetProcess(false);
        enemy.SetPhysicsProcess(false);
        enemy.Position = new Vector2(285, 100);
        Call(enemy, typeof(Enemy), "TickEntrance", 0d);
        Call(enemy, typeof(Enemy), "TickEntrance", 2d);
        foreach (var panel in enemy.GetChildren().OfType<Panel>()) panel.Hide();
    }

    private static Vector2 LocalPoint(Sprite2D body, Vector2 point)
    {
        point -= body.Texture.GetSize() / 2f;
        if (body.FlipH) point.X = -point.X;
        return (point + body.Offset) * body.Scale;
    }

    private static Vector2 Foot(Sprite2D body, Vector2 point) => body.Position + LocalPoint(body, point);

    private static void CheckPose(Sprite2D body, Vector2 anchor, float height, float head, Vector2 foot)
    {
        Check(Foot(body, foot).DistanceTo(anchor) < 0.05f, "pose keeps the same foot position");
        Check(Mathf.Abs(body.Scale.Y * (foot.Y - head) - height) < 0.05f, "person height stays stable across poses");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Shot(string name)
    {
        await Frames(12);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string folder = ProjectSettings.GlobalizePath("res://build/qa_story/koharu_redesign/shots");
        DirAccess.MakeDirRecursiveAbsolute(folder);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.GetUsedRect().Size.X > 100 && image.SavePng($"{folder}/{name}.png") == Error.Ok,
            $"rendered screenshot {name}");
    }
}
