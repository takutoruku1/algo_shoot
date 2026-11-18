using Godot;
using System.Collections.Generic;

public partial class SnsPostMedia : Node
{
    internal sealed record Attachment(int Photo = -1, string Clip = "", string ImagePath = "")
    {
        internal bool IsVideo => Clip.Length > 0;
    }

    private static readonly Dictionary<string, Attachment> Attachments = new()
    {
        ["last_train"] = new(0), ["draft_reply"] = new(1), ["archive_night"] = new(2),
        ["penlight_drawer"] = new(3), ["unfinished_color"] = new(4), ["two_cups"] = new(5),
        ["old_blanket"] = new(6), ["muted_notifications"] = new(7), ["waiting_reply"] = new(8),
        ["rain_window"] = new(Clip: "rain"), ["stream_room"] = new(Clip: "studio"),
        ["akari_warm_meal"] = new(ImagePath: "res://assets/sns/akari_warm_meal_v2.png"),
        ["koharu_worked_notes"] = new(ImagePath: "res://assets/sns/koharu_worked_notes_v2.png"),
        ["rei_book_night"] = new(ImagePath: "res://assets/sns/rei_book_night_v2.png"),
        ["mina_shared_sky"] = new(ImagePath: "res://assets/sns/mina_shared_sky_v2.png"),
    };

    private Texture2D _photos = null!;
    private readonly Dictionary<string, Texture2D> _posters = new();
    private readonly Dictionary<string, Texture2D> _images = new();
    private readonly HashSet<string> _pausedPosts = new();
    private VideoStreamPlayer _player = null!;
    private string _loadedPostId = "";
    internal string ActivePostId { get; private set; } = "";
    internal bool Playing => ActivePostId.Length > 0 && _player.IsPlaying() && !_player.Paused;
    internal static Attachment? Find(string id) => Attachments.GetValueOrDefault(id);

    public override void _Ready()
    {
        ProcessPriority = 110;
        _photos = GD.Load<Texture2D>("res://assets/sns/story_posts_v4.png");
        foreach (string clip in new[] { "rain", "studio" })
            _posters[clip] = GD.Load<Texture2D>($"res://assets/sns/{clip}_poster.png");
        foreach (var (id, attachment) in Attachments)
            if (attachment.ImagePath.Length > 0) _images[id] = GD.Load<Texture2D>(attachment.ImagePath);
        _player = new VideoStreamPlayer { Visible = false, VolumeDb = -80, Loop = true };
        AddChild(_player);
    }

    public override void _Process(double delta)
    {
        if (GetParent() is Hub hub && hub.IsProcessing()) hub.UpdateSnsMedia();
    }

    internal void SetVisiblePost(string id)
    {
        ActivePostId = id;
        if (id.Length == 0) { _player.Paused = true; return; }
        if (id != _loadedPostId)
        {
            _player.Stop();
            _player.Stream = GD.Load<VideoStream>($"res://assets/sns/{Find(id)!.Clip}_loop.ogv");
            _loadedPostId = id;
            _player.Play();
        }
        _player.Paused = _pausedPosts.Contains(id);
    }

    internal void Toggle(string id)
    {
        if (!_pausedPosts.Remove(id) && ActivePostId == id && Playing) _pausedPosts.Add(id);
        if (ActivePostId == id) _player.Paused = _pausedPosts.Contains(id);
    }

    public override void _ExitTree()
    {
        _player.Stop();
        _player.Stream = null;
    }

    internal void Draw(CanvasItem canvas, string id, Rect2 rect, float alpha)
    {
        var attachment = Find(id)!;
        Texture2D texture = _photos;
        Rect2 source;
        if (attachment.IsVideo)
        {
            texture = _loadedPostId == id && _player.StreamPosition > 0
                ? _player.GetVideoTexture() : _posters[attachment.Clip];
            source = new Rect2(Vector2.Zero, texture.GetSize());
        }
        else if (attachment.ImagePath.Length > 0)
        {
            texture = _images[id];
            source = new Rect2(Vector2.Zero, texture.GetSize());
        }
        else
        {
            Vector2 cell = texture.GetSize() / 3;
            source = new Rect2(new Vector2(attachment.Photo % 3, attachment.Photo / 3) * cell + Vector2.One, cell - Vector2.One * 2);
        }
        DrawRoundedPhoto(canvas, texture, source, rect, alpha);
        if (!attachment.IsVideo) return;
        bool playing = ActivePostId == id && Playing;
        var badge = new Rect2(rect.Position + new Vector2(10, 10), new Vector2(74, 24));
        UiKit.Box(canvas, badge, new Color(.025f, .035f, .045f, .8f * alpha), 12);
        UiKit.Text(canvas, UiKit.Mono, badge.Position + new Vector2(12, 3), "0:04", 13, new Color(1, 1, 1, alpha));
        Vector2 speaker = badge.Position + new Vector2(59, 12);
        canvas.DrawPolyline(new[] { speaker + new Vector2(-5, -2), speaker + new Vector2(-2, -2),
            speaker + new Vector2(1, -5), speaker + new Vector2(1, 5), speaker + new Vector2(-2, 2),
            speaker + new Vector2(-5, 2), speaker + new Vector2(-5, -2) }, new Color(1, 1, 1, alpha), 1.2f, true);
        canvas.DrawLine(speaker + new Vector2(-6, -6), speaker + new Vector2(6, 6), new Color(1, 1, 1, alpha), 1.2f, true);
        var button = new Rect2(rect.End - new Vector2(110, 38), new Vector2(100, 28));
        UiKit.Box(canvas, button, new Color(.025f, .035f, .045f, .8f * alpha), 14);
        Vector2 center = button.Position + new Vector2(16, 14);
        if (playing)
        {
            canvas.DrawLine(center + new Vector2(-3, -5), center + new Vector2(-3, 5), new Color(1, 1, 1, alpha), 2);
            canvas.DrawLine(center + new Vector2(3, -5), center + new Vector2(3, 5), new Color(1, 1, 1, alpha), 2);
        }
        else canvas.DrawColoredPolygon(new[] { center + new Vector2(-3, -6), center + new Vector2(6, 0), center + new Vector2(-3, 6) }, new Color(1, 1, 1, alpha));
        UiKit.Text(canvas, UiKit.Zen, button.Position + new Vector2(30, 4), playing ? "一時停止" : "再生", 13, new Color(1, 1, 1, alpha));
        float progress = _loadedPostId == id ? (float)(_player.StreamPosition % 4) / 4 : 0;
        canvas.DrawLine(rect.Position + new Vector2(12, rect.Size.Y - 5), rect.Position + new Vector2(rect.Size.X - 12, rect.Size.Y - 5), new Color(1, 1, 1, .22f * alpha), 2);
        canvas.DrawLine(rect.Position + new Vector2(12, rect.Size.Y - 5), rect.Position + new Vector2(12 + (rect.Size.X - 24) * progress, rect.Size.Y - 5), new Color(1, 1, 1, .75f * alpha), 2);
    }

    private static void DrawRoundedPhoto(CanvasItem canvas, Texture2D texture, Rect2 source, Rect2 target, float alpha)
    {
        float scale = Mathf.Max(target.Size.X / source.Size.X, target.Size.Y / source.Size.Y);
        Vector2 size = target.Size / scale;
        source = new Rect2(source.GetCenter() - size * .5f, size);
        const float radius = 12;
        var vertices = new Vector2[36];
        var uv = new Vector2[36];
        var centers = new[] { target.Position + new Vector2(target.Size.X - radius, radius),
            target.End - Vector2.One * radius, target.Position + new Vector2(radius, target.Size.Y - radius),
            target.Position + Vector2.One * radius };
        for (int corner = 0; corner < 4; corner++)
            for (int step = 0; step <= 8; step++)
            {
                float angle = (-90 + corner * 90 + step * 90f / 8) * Mathf.Pi / 180;
                int index = corner * 9 + step;
                vertices[index] = centers[corner] + Vector2.FromAngle(angle) * radius;
                uv[index] = (source.Position + (vertices[index] - target.Position) / target.Size * source.Size) / texture.GetSize();
            }
        canvas.DrawPolygon(vertices, new[] { new Color(1, 1, 1, alpha) }, uv, texture);
    }
}
