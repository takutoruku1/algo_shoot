using Godot;
using System.Collections.Generic;

// Background : 横スクロールする多層パララックス背景。
// char/bg/w0/ 配下の各レイヤー画像を「存在すれば」読み込み、画面高(216)に合わせて
// 横方向にタイル＋左へシームレスにループさせる。レイヤーごとにスクロール速度が異なり、
// 奥ほど遅く・手前ほど速い（パララックス）。
//   L0 sky  (不透明・最奥)  / L1 far / L2 mid / L3 fore(手前・ゲーム要素より前)
// 画像が1枚も無ければ HasSky=false（Main 側で単色背景にフォールバック）。
public partial class Background : Node2D
{
    private const float ScreenWidth = 384f;
    private const float ScreenHeight = 216f;

    // レイヤー定義（奥→手前）。speed=px/s, z=絶対ZIndex。
    // BaseFill(緑)=-100, ワールド=0, Player=10。foreは前景としてゲーム要素より前(50)。
    private static readonly (string file, float speed, int z)[] Defs = new[]
    {
        ("res://char/bg/w0/bg_w0_sky.png",  14f, -90),
        ("res://char/bg/w0/bg_w0_far.png",  28f, -80),
        ("res://char/bg/w0/bg_w0_mid.png",  52f, -70),
        ("res://char/bg/w0/bg_w0_fore.png", 85f,  50),
    };

    public bool HasSky { get; private set; }

    private class Layer
    {
        public Sprite2D[] Tiles = System.Array.Empty<Sprite2D>();
        public float Width;
        public float Speed;
    }

    private readonly List<Layer> _layers = new List<Layer>();

    public override void _Ready()
    {
        foreach (var d in Defs)
        {
            // 未生成のレイヤーはスキップ（読込エラーの出力を避ける）。
            if (!ResourceLoader.Exists(d.file))
                continue;

            var tex = ResourceLoader.Load<Texture2D>(d.file);
            if (tex == null)
                continue;

            if (d.file.EndsWith("bg_w0_sky.png"))
                HasSky = true;

            float scale = ScreenHeight / tex.GetHeight();
            float w = tex.GetWidth() * scale;
            int count = Mathf.CeilToInt(ScreenWidth / w) + 1;

            var tiles = new Sprite2D[count];
            for (int i = 0; i < count; i++)
            {
                var spr = new Sprite2D
                {
                    Texture = tex,
                    Centered = false,
                    Scale = new Vector2(scale, scale),
                    Position = new Vector2(i * w, 0f),
                    ZIndex = d.z,
                    ZAsRelative = false // 親に影響されない絶対z
                };
                AddChild(spr);
                tiles[i] = spr;
            }

            _layers.Add(new Layer { Tiles = tiles, Width = w, Speed = d.speed });
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach (var layer in _layers)
        {
            float span = layer.Tiles.Length * layer.Width;
            foreach (var spr in layer.Tiles)
            {
                var p = spr.Position;
                p.X -= layer.Speed * dt;
                if (p.X <= -layer.Width)
                    p.X += span; // シームレスループ
                spr.Position = p;
            }
        }
    }
}
