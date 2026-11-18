using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class BossProjectileQa : RoadProjectileQa
{
    protected override IEnumerable<string> ModelNames => OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--character=")) is { } character
        ? BossProjectileArt.Names.Where(name => name.StartsWith(character[12..] + "_")) : BossProjectileArt.Names;
    private protected override ProjectileTurntable Model(string name) => BossProjectileArt.Get(name);
    protected override Texture2D Original(string name) => BossProjectileArt.Source(name);
    protected override bool IncludeFlatComparison => false;
    protected override string GalleryTitle => "ボス技 / Blender 立体回転";
    protected override void ApplyModel(Bullet bullet, string name)
        => bullet.UseBossIllustration(BulletArt.EnemyVisual(Original(name), Colors.White), fitRadius: bullet.IllustrationRadius);

    public override void _Ready()
    {
        if (!OS.GetCmdlineUserArgs().Contains("--inventory")) { base._Ready(); return; }
        int exit = 0;
        try
        {
            if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/")) throw new Exception("isolated saves required");
            string output = ProjectSettings.GlobalizePath("res://build/qa_story/boss3d_20261029");
            System.IO.Directory.CreateDirectory(output);
            int rows = (BossProjectileArt.Names.Count() + 6) / 7;
            using var sheet = Image.CreateEmpty(7 * 144, rows * 144, false, Image.Format.Rgba8);
            sheet.Fill(new Color("202b3c"));
            int index = 0;
            foreach (string name in BossProjectileArt.Names)
            {
                using var source = BossProjectileArt.Source(name).GetImage();
                if (source.IsCompressed()) source.Decompress();
                using var icon = source.GetRegion(source.GetUsedRect());
                float ratio = 118f / Math.Max(icon.GetWidth(), icon.GetHeight());
                icon.Resize((int)(icon.GetWidth() * ratio), (int)(icon.GetHeight() * ratio));
                sheet.BlitRect(icon, new Rect2I(Vector2I.Zero, icon.GetSize()),
                    new Vector2I(index % 7 * 144 + (144 - icon.GetWidth()) / 2, index / 7 * 144 + (144 - icon.GetHeight()) / 2));
                index++;
            }
            sheet.SavePng(output + "/references.png");
            System.IO.File.WriteAllText(output + "/references.json", JsonSerializer.Serialize(BossProjectileArt.Names));
            GD.Print("[Boss3D] reference inventory " + index);
        }
        catch (Exception error) { GD.PushError(error.ToString()); exit = 1; }
        GetTree().Quit(exit);
    }

    protected override async Task CheckRuntime()
    {
        _pool.SetPhysicsProcess(false);
        foreach (var child in _pool.GetChildren()) child.SetPhysicsProcess(false);
        var target = new Area2D { Position = Field.PlayerStart, CollisionLayer = 1, CollisionMask = 8 };
        target.AddToGroup("player");
        target.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = Player.HitRadius } });
        AddChild(target);
        foreach (string name in ModelNames)
        {
            var bullet = _pool.Spawn(Field.PlayerStart, Vector2.Right * 40, true, 3.4f);
            bullet.SetSprite(Original(name));
            float envelope = bullet.CollisionRadius;
            ApplyModel(bullet, name);
            Check(bullet.ModelAnimationName == name, name + ": source artwork resolves to its own Blender model");
            bullet.UseBossIllustration(BulletArt.EnemyVisual(Original(name), Colors.White), fitRadius: bullet.IllustrationRadius);
            Check(Mathf.Abs(bullet.IllustrationRadius - envelope) < .001f, name + ": changing artwork preserves its full rotation envelope");
            for (int frame = 0; frame < ProjectileTurntable.FrameCount; frame++)
            {
                Frame(bullet, frame);
                if (bullet.ModelAnimationFrame != frame || bullet.CollisionRadius > envelope + .001f)
                    throw new Exception(name + ": frame or forecast envelope mismatch");
            }
            bullet.SyncEnemyCollision(target.Position);
            await PhysicsFrames(2);
            Check(bullet.GetShapeOwners().Any(owner => !bullet.IsShapeOwnerDisabled((uint)owner)), name + ": native collision synchronized");
            bullet.Position = new Vector2(Field.Left + 8, Field.Top + 8);
            bullet.SyncEnemyCollision(target.Position);
            await PhysicsFrames(2);
            bullet.StepEnemy(.1, 1);
            Check(bullet.Active, name + ": distant shot remains inside the arena");
            bullet.Position = target.Position;
            bullet.SyncEnemyCollision(target.Position);
            await PhysicsFrames(2);
            Check(bullet.GetShapeOwners().Any(owner => !bullet.IsShapeOwnerDisabled((uint)owner)), name + ": a distant rotating shot restores collision on return");
            int frozen = bullet.ModelAnimationFrame;
            Hud.BubblePaused = true;
            bullet.StepEnemy(.5, 1);
            Check(bullet.ModelAnimationFrame == frozen, name + ": dialogue freezes the model");
            Hud.BubblePaused = false;
            _pool.Despawn(bullet);
            var reused = _pool.Spawn(Field.PlayerStart, Vector2.Right * 40, false, 2.6f);
            var playerModel = PlayerProjectileArt.Get(GameManager.Instance!.SelectedJob);
            var sprite = reused.GetChildren().OfType<Sprite2D>().Single();
            Check(ReferenceEquals(bullet, reused) && reused.ModelAnimationName == playerModel.Name && sprite.Visible
                && sprite.Texture == playerModel.Texture && sprite.ZIndex == 0,
                name + ": reuse restores the player shot without a stale boss model");
            _pool.Despawn(reused);
        }
        target.QueueFree();
        await Frames(2);
        _results.Add(new { boss_model_sources = ModelNames.Count(), forecast_envelope_preserved = true, pause_and_reuse = true });
    }

    protected override Task CheckRoster() => Task.CompletedTask;
}
