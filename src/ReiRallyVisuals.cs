using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ReiRallyVisuals : Node
{
    private SubViewport _view = null!;
    private Ball[] _balls = null!;
    private Tile[] _tiles = null!;
    private double _time;
    private bool _broken;
    private Vector2 _impact;
    private int _streak;
    private double _impactAt = -10;
    private static PackedScene? _model;

    private sealed class Ball
    {
        public Node3D Root = null!, Good = null!, Danger = null!, Hidden = null!;
        public double ImpactAt = -10;
        public float Spin;
    }

    private sealed class Tile
    {
        public Node3D Root = null!;
        public Node3D[] Shards = null!;
        public Vector3[] Homes = null!;
        public bool Damaged;
        public float Open;
        public double HitAt = -10;
    }

    internal static void Prepare()
    {
        _model ??= GD.Load<PackedScene>("res://assets/fx/rei_rally.glb");
        ReiRallySound.Prepare();
    }

    internal void Initialize(int count)
    {
        Prepare();
        Name = "ReiRallyVisuals";
        _view = new SubViewport
        {
            Name = "ReiRallyView", Size = new Vector2I(1536, 864), TransparentBg = true,
            OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa2X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        AddChild(_view);
        _view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Transparent,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("c7bfdc"), AmbientLightEnergy = .65f,
                AmbientLightSkyContribution = 0, TonemapMode = Godot.Environment.ToneMapper.Linear,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color("332e60"), SkyHorizonColor = new Color("f7e3b4"),
                    GroundBottomColor = new Color("142b32"), GroundHorizonColor = new Color("91cac6"),
                    SkyEnergyMultiplier = .7f, GroundEnergyMultiplier = .45f,
                } },
            },
        });
        _view.AddChild(new Camera3D
        {
            Position = new Vector3(192, -108, 500), Far = 1000,
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 216,
            KeepAspect = Camera3D.KeepAspectEnum.Height, Current = true,
        });
        _view.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-28, -35, 0), LightColor = new Color("fff1cf"), LightEnergy = 1.5f,
        });
        _view.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(25, 55, 0), LightColor = new Color("9cafff"), LightEnergy = .8f,
        });
        var source = _model!.Instantiate<Node3D>();
        Node3D Copy(string name, Node parent)
        {
            var copy = (Node3D)source.GetNode<Node3D>(name).Duplicate();
            parent.AddChild(copy);
            return copy;
        }
        _balls = Enumerable.Range(0, count).Select(index =>
        {
            var root = new Node3D { Name = $"Ball{index}" };
            _view.AddChild(root);
            return new Ball { Root = root, Good = Copy("ReturnBall", root),
                Danger = Copy("DangerBall", root), Hidden = Copy("HiddenBall", root) };
        }).ToArray();
        _tiles = Enumerable.Range(0, 5).Select(index =>
        {
            var root = Copy("WallTile", _view);
            root.Name = $"Tile{index}";
            var shards = root.GetChildren().OfType<Node3D>().ToArray();
            return new Tile { Root = root, Shards = shards, Homes = shards.Select(shard => shard.Position).ToArray() };
        }).ToArray();
        source.Free();
    }

    internal void ImpactBall(int index, double time)
    {
        _balls[index].ImpactAt = time;
    }

    internal void HitWall(int streak, Vector2 at, double time)
    {
        _impact = at;
        _impactAt = time;
        _streak = streak;
        var nearest = _tiles.Select((tile, index) => (tile, index)).Where(pair => !pair.tile.Damaged)
            .MinBy(pair => Math.Abs(TileY(pair.index) - at.Y)).tile;
        if (nearest != null) { nearest.Damaged = true; nearest.HitAt = time; }
    }

    internal void ClearDamage()
    {
        foreach (var tile in _tiles) tile.Damaged = false;
    }

    private static float TileY(int index) => Field.Top + 6 + (index + .5f) * (Field.Height - 12) / 5;

    internal void Sync(IReadOnlyList<ReiBossTechnique.RallyBall> balls, float wallX, double time,
        double delta, bool broken, double wallHitAt)
    {
        _time = time;
        _broken = broken;
        float breakAge = broken ? Mathf.Clamp((float)(time - wallHitAt), 0, 1) : 0;
        for (int index = 0; index < balls.Count; index++)
        {
            var state = balls[index];
            var ball = _balls[index];
            ball.Root.Visible = state.Node.Visible;
            ball.Good.Visible = state.Revealed && !state.IsDanger;
            ball.Danger.Visible = state.Revealed && state.IsDanger;
            ball.Hidden.Visible = !state.Revealed;
            ball.Root.Position = new Vector3(state.Position.X, -state.Position.Y, 12);
            ball.Spin += (float)delta * (state.Revealed && state.IsDanger ? -.8f : 1.4f);
            float impact = Mathf.Exp(-Math.Max(0, (float)(time - ball.ImpactAt)) * 18);
            float closing = state.Stage == ReiBossTechnique.RallyStage.Closing
                ? 1 - Mathf.Clamp((float)((time - state.StageAt) / .9), 0, 1) : 1;
            float radius = ReiBossTechnique.RallyRadius * closing;
            // Keep the contact silhouette within 6% of its fixed collision radius during impact.
            ball.Root.Scale = new Vector3(radius * (1 - impact * .06f), radius * (1 + impact * .06f), radius);
            ball.Root.Rotation = new Vector3(.12f * Mathf.Sin(ball.Spin), .22f * Mathf.Cos(ball.Spin * .7f),
                state.Revealed && state.IsDanger ? .12f * Mathf.Sin(ball.Spin) : ball.Spin);
        }
        for (int index = 0; index < _tiles.Length; index++)
        {
            var tile = _tiles[index];
            tile.Open = Mathf.MoveToward(tile.Open, tile.Damaged ? 1 : 0, (float)delta * 6);
            float hit = Mathf.Exp(-Math.Max(0, (float)(time - tile.HitAt)) * 13);
            tile.Root.Position = new Vector3(wallX + hit * 2, -TileY(index), -10);
            tile.Root.Scale = new Vector3(12, (Field.Height - 12) / 5 - 3, 12);
            tile.Root.Rotation = new Vector3(0, -.24f, 0);
            tile.Root.Visible = !broken || breakAge < .95f;
            for (int shard = 0; shard < tile.Shards.Length; shard++)
            {
                float side = shard % 2 == 0 ? -1 : 1;
                Vector3 offset = new(side * tile.Open * .035f, (shard / 2 == 0 ? 1 : -1) * tile.Open * .024f, 0);
                if (broken)
                    offset += new Vector3((1.4f + shard * .5f) * breakAge,
                        (2 - index) * breakAge * .3f - breakAge * breakAge, (shard - 1.5f) * breakAge);
                tile.Shards[shard].Position = tile.Homes[shard] + offset;
                tile.Shards[shard].Rotation = broken ? new Vector3(side, index - 2, side * 2) * breakAge
                    : new Vector3(0, side * tile.Open * .07f, 0);
                tile.Shards[shard].Scale = Vector3.One * (1 - breakAge * breakAge);
            }
        }
    }

    internal void Draw(CanvasItem canvas)
    {
        // Crop the composite at the combat boundary so shards never cover Rei's portrait frame.
        canvas.DrawTextureRectRegion(_view.GetTexture(), Field.Rect,
            new Rect2(Field.Rect.Position * 4, Field.Rect.Size * 4));
        if (!_broken)
        {
            for (int index = 0; index < _tiles.Length; index++)
            {
                float open = _tiles[index].Open;
                if (open <= .01f) continue;
                foreach (var seam in WallSeams)
                    canvas.DrawPolyline(seam.Select(point =>
                    {
                        Vector3 world = _tiles[index].Root.Transform * new Vector3(point.X, point.Y, .155f);
                        return new Vector2(world.X, -world.Y);
                    }).ToArray(),
                        new Color("9dffd4", open * .85f), .8f, true);
            }
        }
        float age = (float)(_time - _impactAt);
        if (age >= 0 && age < .32f)
        {
            float fade = 1 - age / .32f;
            canvas.DrawArc(_impact, 4 + age * (40 + _streak * 6), -Mathf.Pi * .65f, Mathf.Pi * .65f, 24,
                new Color("d6ffe8", fade * .7f), 1.1f, true);
        }
    }

    internal void Stop() => _view.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

    private static readonly Vector2[][] WallSeams =
    {
        new[] { new Vector2(-.08f, .5f), new Vector2(-.02f, .33f), new Vector2(-.1f, .17f), new Vector2(.04f, .05f) },
        new[] { new Vector2(-.5f, .06f), new Vector2(-.3f, .13f), new Vector2(-.15f, -.03f), new Vector2(.04f, .05f) },
        new[] { new Vector2(.04f, .05f), new Vector2(.13f, -.12f), new Vector2(-.03f, -.29f), new Vector2(.02f, -.5f) },
        new[] { new Vector2(.04f, .05f), new Vector2(.22f, -.04f), new Vector2(.35f, .04f), new Vector2(.5f, -.02f) },
    };
}
