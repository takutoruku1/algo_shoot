using Godot;
using System.Collections.Generic;

public partial class PvCurrent : Node2D
{
    private const float W = 1280f;
    private const float H = 720f;
    private const float Duration = 49f;

    private readonly Dictionary<string, Texture2D> _tex = new();
    private AudioStreamPlayer? _music;
    private double _t;

    public override void _Ready()
    {
        DisplayServer.WindowSetSize(new Vector2I(1152, 648));
        _music = new AudioStreamPlayer
        {
            Stream = ResourceLoader.Load<AudioStream>("res://audio/bgm_boss_mina.ogg"),
            Bus = "Music",
            VolumeDb = -4f,
        };
        AddChild(_music);
        _music.Play();
    }

    public override void _Process(double delta)
    {
        _t += delta;
        QueueRedraw();
        if (_t >= Duration) GetTree().Quit();
    }

    public override void _Draw()
    {
        float t = (float)_t;
        UiKit.BeginDesign(this);
        DrawBase();
        if (In(t, 0f, 6f)) Title(t);
        if (In(t, 5f, 6.5f)) ShootLine(t);
        if (In(t, 10.5f, 7f)) Characters(t);
        if (In(t, 16.8f, 7.5f)) Charge(t);
        if (In(t, 23.6f, 8f)) BossPatterns(t);
        if (In(t, 30.8f, 8.5f)) Posts(t);
        if (In(t, 38.5f, 5.8f)) Ending(t);
        if (In(t, 43.6f, 5.8f)) FinalCard(t);
        Grain(t);
        UiKit.EndDesign(this);
    }

    private void DrawBase()
    {
        DrawRect(new Rect2(0, 0, W, H), new Color(0.015f, 0.015f, 0.030f, 1f));
        UiKit.VGradient(this, new Rect2(0, 0, W, H),
            new[] { new Color(0.02f, 0.03f, 0.08f, 1f), new Color(0.08f, 0.03f, 0.07f, 1f) },
            new[] { 0f, 1f });
        for (int i = 0; i < 28; i++)
        {
            float x = Frac(i * 0.371f + (float)_t * 0.010f) * W;
            float y = Frac(i * 0.619f + (float)_t * 0.006f) * H;
            float r = 1.2f + (i % 5) * 0.6f;
            DrawCircle(new Vector2(x, y), r, new Color(0.6f, 0.85f, 1f, 0.08f));
        }
    }

    private void Title(float t)
    {
        float p = Seg(t, 0f, 6f);
        DrawCover(T("res://char/bg2/title/title_mina_v2.png"), Fade(p), 1.04f + p * 0.06f, 0f, -14f);
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.22f));
        DrawCover(T("res://char/bg2/title/mina_kv.png"), 0.90f * Fade(p), 0.72f, 360f - p * 40f, 0f);
        Caption(95, 132, "Refrain", 82, UiKit.ZenBlack, new Color(1f, 0.97f, 0.90f, Fade(p)));
        Caption(102, 236, "心象シューティング PV", 27, UiKit.ZenBold, new Color(0.82f, 0.93f, 1f, Fade(p)));
        Caption(104, 288, "2026.09.22 current build footage", 20, UiKit.Mono, new Color(0.72f, 0.72f, 0.86f, 0.78f * Fade(p)));
        DrawRule(102, 330, 450, new Color(0.82f, 0.93f, 1f, 0.36f * Fade(p)));
    }

    private void ShootLine(float t)
    {
        float p = Seg(t, 5f, 6.5f);
        var shots = new[]
        {
            "build/qa_story/player_shots/mina_shoot_1280x720.png",
            "build/qa_story/player_shots/akari_shoot_1280x720.png",
            "build/qa_story/player_shots/koharu_shoot_1280x720.png",
            "build/qa_story/player_shots/rei_shoot_1280x720.png",
        };
        int a = Mathf.Clamp((int)(p * shots.Length), 0, shots.Length - 1);
        DrawCover(T(shots[a]), Fade(p), 1.02f, (p - 0.5f) * 34f, 0f);
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.40f));
        Caption(92, 470, "言葉は、弾になる。", 52, UiKit.ZenBlack, new Color(1f, 0.98f, 0.92f, Fade(p)));
        Caption(96, 542, "読め。避けろ。撃ち返せ。", 25, UiKit.ZenBold, new Color(0.76f, 0.92f, 1f, 0.86f * Fade(p)));
    }

    private void Characters(float t)
    {
        float p = Seg(t, 10.5f, 7f);
        DrawCover(T("char/bg2/route/mina_mid.png"), 0.36f * Fade(p), 1.10f, 0, 0);
        string[] ids = { "rei", "akari", "koharu", "mina" };
        string[] names = { "レイ", "あかり", "こはる", "ミナ" };
        Color[] cols = { new("b5a1ff"), new("ff9fc7"), new("ffd386"), new("93e7ff") };
        for (int i = 0; i < ids.Length; i++)
        {
            float local = Mathf.Clamp((p * 1.15f - i * 0.10f) / 0.62f, 0f, 1f);
            float a = Smooth(local) * Fade(p);
            float x = 70 + i * 292;
            DrawPanel(new Rect2(x, 110 + (1f - a) * 36f, 250, 430), cols[i] with { A = a });
            DrawContain(T($"res://char/v3/panel_{(ids[i] == "mina" ? "final" : ids[i])}.png"),
                new Rect2(x + 22, 142 + (1f - a) * 22f, 206, 300), a);
            Caption(x + 26, 480, names[i], 30, UiKit.ZenBlack, cols[i] with { A = a });
            Caption(x + 26, 520, "PLAYABLE", 15, UiKit.Mono, new Color(1, 1, 1, 0.45f * a));
        }
        Caption(70, 604, "4人の結び手。違う弾道、違う傷、同じ救い。", 31, UiKit.ZenBold, new Color(1, 1, 1, 0.92f * Fade(p)));
    }

    private void Charge(float t)
    {
        float p = Seg(t, 16.8f, 7.5f);
        string[] frames =
        {
            "build/qa_story/player_shots/mina_charge_ready.png",
            "build/qa_story/player_shots/mina_charge_release.png",
            "build/qa_story/player_shots/rei_charge_impact_1.png",
        };
        int idx = Mathf.Clamp((int)(p * 3f), 0, 2);
        DrawCover(T(frames[idx]), Fade(p), 1.04f, 0, 0);
        DrawRect(new Rect2(0, 0, W, H), new Color(0.01f, 0.01f, 0.02f, 0.28f));
        float pulse = Mathf.Sin((float)_t * 8f) * 0.5f + 0.5f;
        DrawCircle(new Vector2(274, 360), 78 + pulse * 12, new Color(0.45f, 0.92f, 1f, 0.10f * Fade(p)));
        DrawLine(new Vector2(280, 360), new Vector2(1030, 330 + Mathf.Sin((float)_t * 5f) * 18f),
            new Color(0.8f, 0.96f, 1f, 0.70f * Fade(p)), 8f, true);
        DrawLine(new Vector2(280, 360), new Vector2(1030, 330 + Mathf.Sin((float)_t * 5f) * 18f),
            new Color(1f, 1f, 1f, 0.95f * Fade(p)), 2.2f, true);
        Caption(82, 96, "新要素", 22, UiKit.ZenBold, new Color(0.85f, 0.95f, 1f, Fade(p)));
        Caption(82, 132, "チャージショット", 54, UiKit.ZenBlack, new Color(1f, 0.98f, 0.88f, Fade(p)));
        Caption(86, 206, "溜めて、貫く。通常弾とは違う決定打。", 24, UiKit.ZenBold, new Color(0.78f, 0.92f, 1f, 0.86f * Fade(p)));
    }

    private void BossPatterns(float t)
    {
        float p = Seg(t, 23.6f, 8f);
        string[] shots =
        {
            "build/qa_story/boss_spells/shots/aoe_Akari_corridor_active.png",
            "build/qa_story/boss_spells/shots/readability_Koharu_1280x720.png",
            "build/qa_story/boss_spells/shots/aoe_Rei_Fullscreen_impact.png",
            "build/qa_story/boss_spells/shots/readability_MinaBattle_1280x720.png",
        };
        int idx = Mathf.Clamp((int)(p * 4f), 0, 3);
        DrawCover(T(shots[idx]), Fade(p), 1.03f, (idx % 2 == 0 ? -16 : 16) * p, 0);
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.24f));
        Caption(760, 82, "BOSS SPELLS", 20, UiKit.Mono, new Color(0.75f, 0.90f, 1f, Fade(p)));
        Caption(760, 120, "読める弾幕。\n逃げ場のある絶望。", 46, UiKit.ZenBlack, new Color(1f, 0.96f, 0.88f, Fade(p)));
        DrawRule(764, 255, 350, new Color(1f, 0.78f, 0.58f, 0.45f * Fade(p)));
    }

    private void Posts(float t)
    {
        float p = Seg(t, 30.8f, 8.5f);
        string[] shots =
        {
            "build/qa_story/boss_posts/shots/rei_shatter_4.png",
            "build/qa_story/boss_posts/shots/koharu_shatter_4.png",
            "build/qa_story/boss_posts/shots/mina_real_realm.png",
            "build/qa_story/akari_realm_movie_v2/rupture_sequence.png",
        };
        int idx = Mathf.Clamp((int)(p * shots.Length), 0, shots.Length - 1);
        DrawCover(T(shots[idx]), Fade(p), idx == 3 ? 1.18f : 1.03f, idx == 3 ? (p - 0.5f) * -240f : 0f, 0f);
        DrawRect(new Rect2(0, 0, W, H), new Color(0.01f, 0, 0.015f, 0.34f));
        Caption(78, 88, "LATEST CUT", 20, UiKit.Mono, new Color(0.86f, 0.90f, 1f, Fade(p)));
        Caption(78, 126, "投稿を撃ち抜く。\nその奥の本音へ。", 48, UiKit.ZenBlack, new Color(1f, 0.98f, 0.94f, Fade(p)));
        Caption(82, 260, "Boss-post break / real realm sequence", 19, UiKit.Mono, new Color(0.70f, 0.78f, 0.92f, 0.78f * Fade(p)));
    }

    private void Ending(float t)
    {
        float p = Seg(t, 38.5f, 5.8f);
        DrawCover(T("res://char/bg2/ending/cg_ep_together_v1.png"), Fade(p), 1.08f, 0, -12f);
        DrawCover(T("res://char/bg2/ending/cg_final_received_v1.png"), 0.42f * Smooth(Mathf.Clamp((p - 0.46f) / 0.40f, 0f, 1f)) * Fade(p), 1.02f, 190f, 0f);
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.36f));
        Caption(86, 480, "救いは、スコアでは終わらない。", 44, UiKit.ZenBlack, new Color(1f, 0.98f, 0.92f, Fade(p)));
        Caption(90, 548, "回想、選択、帰り道までをひとつの弾幕に。", 24, UiKit.ZenBold, new Color(0.78f, 0.90f, 1f, 0.82f * Fade(p)));
    }

    private void FinalCard(float t)
    {
        float p = Seg(t, 43.6f, 5.8f);
        DrawCover(T("res://char/bg2/title/title_mina_v2.png"), Fade(p), 1.02f, 0, 0);
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.52f));
        Caption(0, 218, "Refrain", 88, UiKit.ZenBlack, new Color(1f, 0.98f, 0.90f, Fade(p)), HorizontalAlignment.Center, W);
        Caption(0, 330, "固定画面シューティング × 心象ノベル", 29, UiKit.ZenBold, new Color(0.82f, 0.94f, 1f, 0.90f * Fade(p)), HorizontalAlignment.Center, W);
        Caption(0, 410, "最新開発版PV", 22, UiKit.Mono, new Color(0.75f, 0.76f, 0.88f, 0.78f * Fade(p)), HorizontalAlignment.Center, W);
        DrawRule(420, 388, 440, new Color(0.86f, 0.94f, 1f, 0.34f * Fade(p)));
    }

    private Texture2D T(string path)
    {
        path = path.Replace("\\", "/");
        if (_tex.TryGetValue(path, out var cached)) return cached;
        Texture2D? tex = null;
        if (path.StartsWith("res://"))
        {
            tex = ResourceLoader.Load<Texture2D>(path);
        }
        else
        {
            string abs = ProjectSettings.GlobalizePath("res://" + path);
            var img = Image.LoadFromFile(abs);
            if (img != null && !img.IsEmpty()) tex = ImageTexture.CreateFromImage(img);
        }
        tex ??= Blank();
        _tex[path] = tex;
        return tex;
    }

    private Texture2D Blank()
    {
        var img = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        img.Fill(new Color(0.04f, 0.04f, 0.08f, 1f));
        return ImageTexture.CreateFromImage(img);
    }

    private void DrawCover(Texture2D tex, float alpha, float zoom = 1f, float ox = 0f, float oy = 0f)
    {
        if (alpha <= 0.002f) return;
        float tw = Mathf.Max(1, tex.GetWidth());
        float th = Mathf.Max(1, tex.GetHeight());
        float s = Mathf.Max(W / tw, H / th) * zoom;
        float dw = tw * s;
        float dh = th * s;
        DrawTextureRect(tex, new Rect2((W - dw) * 0.5f + ox, (H - dh) * 0.5f + oy, dw, dh), false,
            new Color(1, 1, 1, alpha));
    }

    private void DrawContain(Texture2D tex, Rect2 box, float alpha)
    {
        if (alpha <= 0.002f) return;
        float tw = Mathf.Max(1, tex.GetWidth());
        float th = Mathf.Max(1, tex.GetHeight());
        float s = Mathf.Min(box.Size.X / tw, box.Size.Y / th);
        Vector2 size = new(tw * s, th * s);
        DrawTextureRect(tex, new Rect2(box.Position + (box.Size - size) * 0.5f, size), false, new Color(1, 1, 1, alpha));
    }

    private void DrawPanel(Rect2 r, Color col)
    {
        UiKit.Box(this, r, new Color(0.05f, 0.045f, 0.075f, 0.68f * col.A), 8f, col with { A = 0.62f * col.A }, 2f);
        UiKit.RadialGlow(this, r.Position + r.Size * new Vector2(0.5f, 0.38f), 180f, col, 0.16f * col.A);
    }

    private void Caption(float x, float y, string s, int size, Font font, Color col,
        HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f)
    {
        if (col.A <= 0.002f) return;
        float asc = font.GetAscent(size);
        DrawString(font, new Vector2(x + 2, y + asc + 3), s, align, width, size, new Color(0, 0, 0, 0.62f * col.A));
        DrawString(font, new Vector2(x, y + asc), s, align, width, size, col);
    }

    private void DrawRule(float x, float y, float w, Color col)
    {
        DrawRect(new Rect2(x, y, w, 2), col);
        UiKit.RadialGlow(this, new Vector2(x + w, y + 1), 44, col, col.A * 0.5f);
    }

    private void Grain(float t)
    {
        for (int i = 0; i < 70; i++)
        {
            float x = Frac(i * 0.113f + t * 0.37f) * W;
            float y = Frac(i * 0.271f + t * 0.19f) * H;
            DrawRect(new Rect2(x, y, 1.2f, 1.2f), new Color(1, 1, 1, 0.035f));
        }
    }

    private static bool In(float t, float start, float len) => t >= start && t < start + len;
    private static float Seg(float t, float start, float len) => Mathf.Clamp((t - start) / len, 0f, 1f);
    private static float Smooth(float x) => x * x * (3f - 2f * x);
    private static float Frac(float x) => x - Mathf.Floor(x);

    private static float Fade(float p)
    {
        float i = Smooth(Mathf.Clamp(p / 0.12f, 0f, 1f));
        float o = Smooth(Mathf.Clamp((1f - p) / 0.12f, 0f, 1f));
        return Mathf.Min(i, o);
    }
}
