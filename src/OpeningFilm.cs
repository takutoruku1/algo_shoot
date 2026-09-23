using Godot;
using System;

public partial class OpeningFilm : Node2D
{
    private const double PhoneDuration = 10.5;
    public const double Duration = PhoneDuration + 37.5;
    private const float ReleaseTime = 1.5f;
    private const float ImpactTime = 2.25f;
    public Action? Completed;
    public double Elapsed { get; private set; }
    public bool Finished { get; private set; }

    private static readonly double[] Cuts =
    {
        0, PhoneDuration, PhoneDuration + 3.5, PhoneDuration + 7, PhoneDuration + 10.5,
        PhoneDuration + 15.5, PhoneDuration + 19, PhoneDuration + 22.5,
        PhoneDuration + 26, PhoneDuration + 29.5, PhoneDuration + 33.5, Duration,
    };
    private static readonly (double Time, string Text)[] DraftBeats =
    {
        // 打つ(0.55s/字)より消す(旧0.20s/字)のほうが2.75倍速く、いちばん見せたい「消す」が一瞬で終わっていた。
        //   迷いの静止を 0.90→1.60 秒、消去を 0.20→0.60 秒/字に伸ばし、消す手つきを見せる。
        //   伸ばしたぶんは前後の空白（打ち始めまでの 1.8→1.2 秒、消したあとの間 1.8→1.2 秒）から借りる。
        //   PhoneDuration(10.5) は全カットの基準なので触らない。末尾 8.50 ＝完成した「たすけて」が
        //   次のカットまで 2.0 秒残る（OpeningFilmQa の可読性チェックと同じ下限）。
        (0, ""),
        (1.2, "た"),
        (1.75, "たす"),
        (2.3, "たすけ"),
        (3.9, "たす"),      // ← 迷いの静止 1.60 秒
        (4.5, "た"),
        (5.1, ""),
        (6.3, "た"),        // ← 消したあとの間 1.20 秒
        (6.95, "たす"),
        (7.75, "たすけ"),
        (8.5, "たすけて"),
    };
    private static readonly string[] Characters = { "akari", "koharu", "rei", "mina" };
    private static readonly string[] DailyLines =
    {
        "……好きって、\n言えばよかった。",
        "ちゃんと、応援しなきゃ。\n……好きで、始めたはずなのに。",
        "今日はちょっと、疲れてて……。\n……なんて。始めるわよ。",
    };
    private static readonly string[] CutinLines =
    {
        "今度は、あたしの言葉で。",
        "あたしの「好き」を、\n嫌いになりたくない。",
        "わたしの声で、\n話したいことがあるの。",
        "……聞こえています。\nあなたが、消した言葉も。",
    };
    private static readonly Color[] Accents = { new("f0c969"), new("a6dac8"), new("de91b9"), new("87d7ed") };
    private static readonly Rect2 Screen = new(0, 0, 1280, 720);
    private static readonly Rect2 SkipRect = new(1110, 657, 140, 45);
    private static readonly Vector2 PhoneTextPosition = new(-108, -108);
    private const int PhoneTextSize = 28;
    private Texture2D[] _daily = null!, _fighters = null!, _cores = null!, _cutins = null!;
    private readonly Texture2D[,] _routes = new Texture2D[4, 3];
    private readonly BulletArt.PlayerVisual[] _shots = new BulletArt.PlayerVisual[4];
    private readonly SubViewport[] _postViews = new SubViewport[4];
    private readonly (Vector2[] Points, int[] Triangles)[] _postMeshes = new (Vector2[], int[])[4];
    private static readonly Rect2 PostRect = new(-155, -95, 310, 190);
    private JobTuning[] _cast = null!;
    private Texture2D _city = null!, _light = null!, _space = null!, _message = null!;
    private Texture2D _post = null!;
    private Texture2D _phoneArt = null!;
    // Ignore near-transparent generation residue outside the actual hardware.
    private readonly Rect2 _phoneArtRegion = new(70, 145, 711, 1547);
    private string _phoneTime = "";
    private Sprite2D _mina = null!;
    private ShaderMaterial _wind = null!;
    private FilmOverlay _overlay = null!;
    private bool _inputArmed;
    private double _skipHold, _leaveTime;
    private bool _leaving;
    private int Shot => FindShot(Elapsed);

    public override void _Ready()
    {
        Name = "OpeningFilm";
        Scale = Vector2.One * UiKit.Scale;
        TextureFilter = TextureFilterEnum.Linear;
        _daily = new Texture2D[3];
        _fighters = new Texture2D[4];
        _cores = new Texture2D[4];
        _cast = new JobTuning[4];
        _cutins = new[]
        {
            GD.Load<Texture2D>("res://char/v3/cutin_akari.png"),
            GD.Load<Texture2D>("res://char/v3/cutin_koharu.png"),
            GD.Load<Texture2D>("res://char/v3/cutin_rei_gawa_a.png"),
            GD.Load<Texture2D>("res://char/bg2/opening/op_mina_v1.png"),
        };
        for (int i = 0; i < 4; i++)
        {
            string id = Characters[i];
            _cast[i] = Array.Find(Jobs.All, job => job.CharacterId == id)!;
            if (i < 3) _daily[i] = GD.Load<Texture2D>($"res://char/bg2/opening/op_{id}_v1.png");
            _fighters[i] = GD.Load<Texture2D>($"res://char/player/{id}/{id}_aim_v2_r.png");
            _cores[i] = GD.Load<Texture2D>($"res://char/player/{id}/{id}_core_v1.png");
            _shots[i] = BulletArt.PlayerShot(_cast[i].Id);
            string[] layers = { "far", "mid", "near" };
            for (int layer = 0; layer < layers.Length; layer++)
                _routes[i, layer] = GD.Load<Texture2D>($"res://char/bg2/route/{id}_{layers[layer]}.png");
        }
        _city = GD.Load<Texture2D>("res://char/bg2/title/L1_far.png");
        _light = GD.Load<Texture2D>("res://char/bg2/title/L4_light_warm.png");
        _space = GD.Load<Texture2D>("res://char/bg2/prologue/bg_p4_unsent.png");
        _phoneArt = GD.Load<Texture2D>("res://char/bg2/opening/op_phone_v1.png");
        _phoneTime = DateTime.Now.ToString("HH:mm");
        _message = GD.Load<Texture2D>("res://char/v3/fx/rei/bubble_empty_1.png");
        _post = GD.Load<Texture2D>("res://char/v3/fx/akari/realm/post_glass_v1.png");
        for (int i = 0; i < 4; i++)
        {
            _postMeshes[i] = GlassFractureArt.Mesh(PostRect, i, (ulong)(9321 + i * 71));
            _postViews[i] = new SubViewport { Size = new Vector2I(620, 380), TransparentBg = true,
                Disable3D = true, World2D = new World2D(), RenderTargetUpdateMode = SubViewport.UpdateMode.Once };
            AddChild(_postViews[i]);
            _postViews[i].AddChild(new FilmPost { Plate = _post, Cast = _cast[i], Accent = Accents[i],
                Avatar = GD.Load<Texture2D>(CompanionDialogue.AccountIcon(Characters[i])) });
        }
        _wind = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/opening_mina.gdshader") };
        _wind.SetShaderParameter("blink_texture", GD.Load<Texture2D>("res://char/bg2/opening/op_mina_blink_v1.png"));
        _mina = new Sprite2D
        {
            Texture = GD.Load<Texture2D>("res://char/bg2/opening/op_mina_v1.png"),
            Material = _wind, ZIndex = 1, Visible = false,
        };
        AddChild(_mina);
        _overlay = new FilmOverlay { Film = this, ZIndex = 2 };
        AddChild(_overlay);
        _inputArmed = !SkipHeld();
        Audio.Instance?.Music(Audio.Instance.BgmPrologue, 0.4f);
        UpdateMina();
    }

    public override void _Process(double delta)
    {
        if (Finished) return;
        if (Pad.UiBlocked(this)) { _inputArmed = false; _skipHold = 0; return; }
        Pad.ConsumeUi(this);
        if (_leaving)
        {
            _leaveTime += delta;
            if (_leaveTime >= 0.45) { Complete(); return; }
        }
        else
        {
            double previousTime = Elapsed;
            Elapsed = Math.Min(Duration, Elapsed + delta);
            bool held = SkipHeld();
            if (!held) _inputArmed = true;
            _skipHold = _inputArmed && held && Elapsed >= 0.6 ? _skipHold + delta : 0;
            if (_skipHold >= 0.65 || (Elapsed > 1 && Elapsed < Duration - 1.3
                && SkipRect.HasPoint(Pad.MousePos()) && Pad.MouseClick()))
                RequestSkip();
            if (!_leaving) PlaySoundtrack(previousTime);
            if (Elapsed >= Duration) { Complete(); return; }
        }
        UpdateMina();
        QueueRedraw();
        _overlay.QueueRedraw();
    }

    private void PlaySoundtrack(double previousTime)
    {
        bool Crossed(double time) => previousTime < time && Elapsed >= time;
        if (Crossed(0.55)) Audio.Instance?.PlayUiConfirm();
        for (int i = 1; i < DraftBeats.Length; i++)
            if (Crossed(DraftBeats[i].Time) && DraftBeats[i].Text.Length > DraftBeats[i - 1].Text.Length)
                Audio.Instance?.PlayType(Hud.LineKind.Boy);
        if (Crossed(5.1)) Audio.Instance?.PlayUiCancel();
        if (Crossed(8.7)) Audio.Instance?.PlayCalm();
        if (Crossed(Cuts[4] + 3.2)) Audio.Instance?.Music(Audio.Instance.BgmBossRei, 1.8f);
        for (int i = 0; i < 4; i++)
        {
            double start = Cuts[5 + i];
            if (Crossed(start + 0.6)) Audio.Instance?.PlayChargeReady(_cast[i].Id);
            if (Crossed(start + ReleaseTime)) Audio.Instance?.PlayChargeRelease(_cast[i].Id);
            if (Crossed(start + ImpactTime)) Audio.Instance?.PlayChargeImpact(_cast[i].Id);
        }
        if (Crossed(Cuts[9])) Audio.Instance?.PlayPurify();
        if (Crossed(Cuts[9] + 2.8)) Audio.Instance?.PlayChargeRelease(Job.Tank);
        if (Crossed(Cuts[10] + 0.16)) Audio.Instance?.PlayPurify();
        if (Crossed(Duration - 1.3)) Audio.Instance?.StopMusic(1.3f);
    }

    private static bool SkipHeld() => Input.IsKeyPressed(Key.Escape) || Input.IsKeyPressed(Key.X)
        || Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept")
        || Pad.Pressed(JoyButton.B) || Pad.Pressed(JoyButton.Start);

    public void RequestSkip()
    {
        if (_leaving || Finished || Elapsed < 0.6) return;
        _leaving = true;
        Audio.Instance?.StopMusic(0.45f);
    }

    private void Complete()
    {
        if (Finished) return;
        Finished = true;
        Visible = false;
        // 「見た」を記録しておく（2026-09-22）。このフィルムは**初回からスキップできる**ので
        //   記録がスキップの条件になることは無いが、回想（StoryFilm）と同じ台帳に載せておく
        //   ＝どのムービーを通ったかが1か所に揃う。スキップで抜けた場合も「通過した」として記録する
        //   （ここは記録がゲートではないので、見たかどうかの厳密さより台帳の単純さを採る）。
        FilmSkip.MarkSeen(GetNodeOrNull<GameManager>("/root/Game"), "opening");
        Completed?.Invoke();
        QueueFree();
    }

    private static int FindShot(double time)
    {
        int index = 0;
        while (index < Cuts.Length - 2 && time >= Cuts[index + 1]) index++;
        return index;
    }

    private static float Ease(float v) { v = Mathf.Clamp(v, 0, 1); return v * v * (3 - 2 * v); }
    private static Color Fade(Color c, float a) => new(c.R, c.G, c.B, a);
    // キャラの X ハンドル（Hub.AccountHandle と同じ引き方。ミナだけステージ表に居ないので Handles.Mina）。
    private static string Handle(JobTuning job) => job.Id == Job.Tank
        ? Handles.Mina
        : Array.Find(GameManager.Stages, stage => stage.Id == job.CharacterId)!.Handle;

    private void UpdateMina()
    {
        int shot = Shot;
        _mina.Visible = shot == 4;
        if (!_mina.Visible) return;
        float t = (float)(Elapsed - Cuts[4]);
        float p = Ease(t / 5f);
        // The camera pulls back; only the shader animates the character's loose parts.
        _mina.Position = new Vector2(Mathf.Lerp(542, 646, p), Mathf.Lerp(510, 429, p));
        _mina.Scale = Vector2.One * Mathf.Lerp(0.98f, 0.72f, p);
        _wind.SetShaderParameter("motion_time", (float)Elapsed);
        float blink = 1f - Ease(t / 0.55f);
        float secondBlink = Mathf.Max(0, 1f - Mathf.Abs(t - 3.2f) / 0.13f);
        _wind.SetShaderParameter("blink", Mathf.Max(blink, Ease(secondBlink)));
        _wind.SetShaderParameter("opacity", Ease(t / 0.45f) * (1f - Ease((t - 4.7f) / 0.3f)));
    }

    public override void _Draw()
    {
        DrawRect(Screen, Colors.Black);
        int shot = Shot;
        float local = (float)(Elapsed - Cuts[shot]);
        float transition = shot is >= 5 and <= 9 ? 0.18f : 0.42f;
        if (shot > 0 && local < transition)
            DrawShot(shot - 1, (float)(Cuts[shot] - Cuts[shot - 1]), 1);
        DrawShot(shot, local, shot == 0 ? 1 : Ease(local / transition));
    }

    private void DrawShot(int shot, float t, float alpha)
    {
        if (shot == 0)
        {
            float pull = Ease((t - 6.1f) / 2.4f);
            Background(_space, 1.18f - pull * 0.1f, new Vector2(-38 + pull * 30, 10), alpha * (0.42f + pull * 0.3f));
            DrawRect(Screen, new Color(0.015f, 0.022f, 0.025f, alpha * (0.78f - pull * 0.2f)));
            DrawUnsentSignal(t, alpha);
            DrawPhone(t, alpha);
        }
        else if (shot <= 3)
        {
            int i = shot - 1;
            float p = Ease(t / 3.5f);
            float direction = i == 1 ? -1 : 1;
            Background(_daily[i], 1.045f + p * 0.045f, new Vector2(direction * (16 - 30 * p), 6 - p * 8), alpha);
        }
        else if (shot == 4)
        {
            Background(_city, 1.12f, new Vector2(-20 + t * 7, 25 - t * 4), alpha);
            Background(_light, 1.17f, new Vector2(-40 + t * 15, 0), alpha * 0.55f);
            DrawVoices(t, alpha);
        }
        else if (shot <= 8)
        {
            int i = shot - 5;
            DrawRoute(i, t, alpha);
            DrawSpeed(t * 1.6f, Accents[i], alpha * 0.6f);
            DrawAction(i, t, alpha);
            DrawCutin(i, t, alpha);
        }
        else
        {
            Background(_city, 1.1f, new Vector2((float)Elapsed * 2 - 55, 18), alpha);
            Background(_light, 1.16f, new Vector2((float)Elapsed * 6 - 175, -6), alpha * 0.8f);
            DrawSpeed(t * 0.45f, Accents[3], alpha * (shot == 9 ? 0.55f : 0.18f));
            if (shot == 9)
                DrawTogether(t, alpha);
            else
                for (int i = 0; i < 4; i++)
                    DrawRibbon(new Vector2(-200, 570 + i * 25), new Vector2(1460, 112 + i * 32),
                        t + i * 0.4f, Accents[i], alpha * (0.3f + 0.3f * (1 - Ease(t))), 18);
        }
    }

    private void DrawRoute(int index, float t, float alpha)
    {
        for (int layer = 0; layer < 3; layer++)
        {
            var texture = _routes[index, layer];
            Vector2 size = texture.GetSize() * (720f / texture.GetHeight());
            float speed = layer == 0 ? 35 : layer == 1 ? 135 : 330;
            float x = -Mathf.PosMod(120 + t * speed, size.X);
            Color tint = Fade(Colors.White, alpha * (layer == 2 ? 0.65f : 1));
            DrawTextureRect(texture, new Rect2(new Vector2(x, 0), size), false, tint);
            DrawTextureRect(texture, new Rect2(new Vector2(x + size.X, 0), size), false, tint);
        }
        DrawRect(Screen, new Color(0.015f, 0.02f, 0.025f, alpha * 0.2f));
    }

    private void DrawAction(int index, float t, float alpha)
    {
        float arrive = Ease(t / 0.5f);
        float release = Ease((t - ReleaseTime) / 0.25f);
        float recoil = Mathf.Sin(release * Mathf.Pi) * 22;
        Vector2 position = new(290 - (1 - arrive) * 160 - recoil + t * 7, 300 - release * 26);
        Vector2 muzzle = position + new Vector2(110, -2);
        DrawRibbon(position - new Vector2(460, -75), position + new Vector2(-65, 75), t,
            Accents[index], alpha * 0.4f, 15);
        DrawFlight(index, position, 345, alpha * arrive);
        DrawPostBreak(index, t - ImpactTime, alpha * Ease((t - 1.1f) / 0.4f));
        float gather = Ease((t - 0.4f) / 1.1f) * (1 - Ease((t - ReleaseTime) / 0.18f));
        if (gather > 0)
        {
            for (int j = 0; j < 6; j++)
            {
                float p = Mathf.PosMod(t * 1.3f + j / 6f, 1);
                Vector2 point = muzzle + Vector2.FromAngle(j * Mathf.Tau / 6 + t) * (1 - p) * 96;
                DrawLine(point, point.Lerp(muzzle, 0.28f), Fade(Accents[index], alpha * gather * p), 2, true);
            }
            DrawShotArt(index, muzzle, 58 + gather * 30, 0, alpha * gather);
        }
        float progress = Mathf.Clamp((t - ReleaseTime) / (ImpactTime - ReleaseTime), 0, 1);
        float shotAlpha = alpha * Ease((t - ReleaseTime) / 0.08f) * (1 - Ease((t - ImpactTime) / 0.3f));
        int count = index == 1 ? 3 : index == 2 ? 5 : 1;
        Vector2 target = new(1030, 290);
        for (int j = 0; j < count; j++)
        {
            float lane = j - (count - 1) / 2f;
            float travel = index == 0 ? progress * progress : progress;
            Vector2 end = target + new Vector2(0, index == 2 ? lane * 53 : 0);
            Vector2 point = muzzle.Lerp(end, travel);
            if (index == 1) point.Y += Mathf.Sin(progress * Mathf.Pi) * lane * 165;
            Vector2 tail = muzzle.Lerp(point, Mathf.Max(0, travel - 0.55f));
            if (index == 1) tail.Y += lane * 25;
            DrawRibbon(tail, point, t + j, Accents[index], shotAlpha, index == 0 ? 32 : 18);
            float angle = (end - muzzle).Angle();
            DrawShotArt(index, point, index == 0 ? 165 : index == 3 ? 185 : 102, angle, shotAlpha);
        }
    }

    private void DrawShotArt(int index, Vector2 position, float size, float angle, float alpha)
    {
        var art = _shots[index];
        float scale = size / Mathf.Max(art.Region.Size.X, art.Region.Size.Y);
        DrawSetTransform(position, angle);
        DrawTextureRectRegion(art.Texture, new Rect2((art.Region.Position - art.Pivot) * scale, art.Region.Size * scale),
            art.Region, Fade(Colors.White, alpha));
        DrawSetTransform(Vector2.Zero);
    }

    private void DrawRibbon(Vector2 from, Vector2 to, float t, Color color, float alpha, float width)
    {
        Vector2 normal = (to - from).Normalized().Orthogonal();
        var points = new Vector2[20];
        for (int strand = 0; strand < 2; strand++)
        {
            for (int j = 0; j < points.Length; j++)
            {
                float p = j / (points.Length - 1f);
                float wave = Mathf.Sin(p * 9 - t * 7 + strand * Mathf.Pi);
                points[j] = from.Lerp(to, p) + normal * wave * width * Mathf.Sin(p * Mathf.Pi);
            }
            DrawPolyline(points, Fade(color, alpha * 0.12f), width * 0.55f, true);
            DrawPolyline(points, Fade(color.Lerp(Colors.White, 0.4f), alpha * 0.8f), 1.8f, true);
        }
    }

    private void DrawPostBreak(int index, float time, float alpha)
    {
        Vector2 center = new(1030, 290);
        Texture2D plate = _postViews[index].GetTexture();
        if (time < 0)
        {
            DrawSetTransform(center, -0.1f);
            DrawTextureRect(plate, PostRect, false, Fade(Colors.White, alpha));
            DrawSetTransform(Vector2.Zero);
            return;
        }
        float fade = 1 - Ease(time / 1.05f);
        DrawSetTransform(center, -0.1f);
        var mesh = _postMeshes[index];
        GlassFractureArt.DrawShards(this, plate, PostRect, mesh.Points, mesh.Triangles,
            time * 1.65f, 1.4f, Fade(Colors.White, alpha * fade));
        DrawSetTransform(Vector2.Zero);
        for (int j = 0; j < 8; j++)
        {
            Vector2 ray = Vector2.FromAngle(j * Mathf.Tau / 8 + 0.2f);
            float reach = 30 + Mathf.Sqrt(time) * 180;
            DrawLine(center + ray * reach * 0.5f, center + ray * reach,
                Fade(Accents[index], alpha * fade * 0.7f), 2, true);
        }
        DrawShotArt(index, center, 64 + time * 38, 0, alpha * fade);
    }

    private void DrawTogether(float t, float alpha)
    {
        float launch = Mathf.Pow(Mathf.Max(0, t - 2.8f) / 1.2f, 2);
        for (int i = 0; i < 4; i++)
        {
            float arrive = Ease((t - i * 0.14f) / 0.65f);
            Vector2 p = new(180 + i * 295 - (1 - arrive) * 250 + launch * (1650 - i * 190),
                330 + (i % 2) * 35 - t * 7 - launch * 190);
            DrawRibbon(p - new Vector2(550 + launch * 250, -90), p + new Vector2(-60, 80),
                t + i, Accents[i], alpha * arrive * 0.6f, 24);
            DrawFlight(i, p, i == 3 ? 390 : 350, alpha * arrive);
            DrawShotArt(i, p + new Vector2(115, 35), 50, -0.1f, alpha * arrive);
        }
        DrawSpeed(t * (1 + launch * 4), Accents[3], alpha * launch * 0.6f);
    }

    private void Background(Texture2D texture, float zoom, Vector2 offset, float alpha)
    {
        Vector2 size = texture.GetSize();
        size *= Mathf.Max(1280 / size.X, 720 / size.Y) * zoom;
        DrawTextureRect(texture, new Rect2((Screen.Size - size) * 0.5f + offset, size), false, Fade(Colors.White, alpha));
    }

    private void DrawCutin(int index, float t, float alpha)
    {
        float enter = Ease(t / 0.45f);
        float leave = Ease((t - 1.15f) / 0.55f);
        if (leave >= 1) return;
        float shift = (1 - enter) * 260 + leave * 720;
        alpha *= 1 - leave;
        Vector2[] band = { new(778 + shift, 22), new(1280 + shift, 22), new(1280 + shift, 698), new(585 + shift, 698) };
        DrawColoredPolygon(band, new Color(0.07f, 0.075f, 0.085f, alpha * 0.94f));
        DrawLine(band[0], band[3], Fade(Accents[index], alpha * enter), 3, true);
        var portrait = _cutins[index];
        Vector2 size = portrait.GetSize() * (640f / portrait.GetHeight());
        Vector2 center = new((index == 3 ? 810 : 987) + shift - t * 5, 361);
        Rect2 rect = new(center - size * 0.5f, size);
        float edgeTop = Mathf.Lerp(band[0].X, band[3].X, (rect.Position.Y - 22) / 676);
        float edgeBottom = Mathf.Lerp(band[0].X, band[3].X, (rect.End.Y - 22) / 676);
        Vector2[] points =
        {
            new(Mathf.Max(rect.Position.X, edgeTop), rect.Position.Y),
            new(Mathf.Min(rect.End.X, 1280 + shift), rect.Position.Y),
            new(Mathf.Min(rect.End.X, 1280 + shift), rect.End.Y),
            new(Mathf.Max(rect.Position.X, edgeBottom), rect.End.Y),
        };
        Vector2[] uv = Array.ConvertAll(points, point => (point - rect.Position) / rect.Size);
        Color tint = Fade(Colors.White, alpha * enter);
        DrawPolygon(points, new[] { tint, tint, tint, tint }, uv, portrait);
    }

    private static (string Text, bool Caret) PhoneDraft(double time)
    {
        int beat = 0;
        while (beat < DraftBeats.Length - 1 && time >= DraftBeats[beat + 1].Time) beat++;
        double idle = time - DraftBeats[beat].Time;
        return (DraftBeats[beat].Text, idle % 1.1 < 0.6);
    }

    private static (Vector2 Position, float Angle, float Scale) PhoneCamera(float t)
    {
        float drift = Ease(t / 6.1f);
        float pull = Ease((t - 6.1f) / 2.4f);
        Vector2 close = new Vector2(858, 485).Lerp(new Vector2(830, 475), drift);
        return (close.Lerp(new Vector2(640, 360), pull),
            Mathf.Lerp(Mathf.Lerp(-0.14f, -0.1f, drift), -0.028f, pull),
            Mathf.Lerp(Mathf.Lerp(1.92f, 1.82f, drift), 1.04f, pull));
    }

    private void DrawUnsentSignal(float t, float alpha)
    {
        float progress = Ease((t - 8.7f) / 1.8f);
        if (progress <= 0) return;
        for (int i = 0; i < 4; i++)
        {
            Vector2 from = new(640, 326);
            Vector2 to = new(i % 2 == 0 ? -100 : 1380, 95 + i * 153);
            var points = new Vector2[24];
            for (int j = 0; j < points.Length; j++)
            {
                float p = progress * j / (points.Length - 1);
                points[j] = from.Lerp(to, p) + new Vector2(0, Mathf.Sin(p * Mathf.Pi) * (i % 2 == 0 ? -45 : 45));
            }
            DrawPolyline(points, Fade(Accents[i], alpha * progress * 0.08f), 8, true);
            DrawPolyline(points, Fade(Accents[i], alpha * progress * 0.65f), 1.5f, true);
            DrawLine(points[^2], points[^1], Fade(Colors.White, alpha * progress * 0.8f), 2, true);
        }
    }

    private static (int Row, int Column, float Press) PhoneKeyAt(float t)
    {
        int beat = 0;
        while (beat < DraftBeats.Length - 1 && t >= DraftBeats[beat + 1].Time) beat++;
        if (beat == 0) return (-1, -1, 0);
        float press = 1 - Ease((t - (float)DraftBeats[beat].Time) / 0.18f);
        string text = DraftBeats[beat].Text;
        if (text.Length < DraftBeats[beat - 1].Text.Length) return (0, 4, press);
        return text[^1] switch
        {
            'す' => (0, 3, press),
            'け' => (0, 2, press),
            _ => (1, 1, press),
        };
    }

    private void DrawPhone(float t, float alpha)
    {
        var camera = PhoneCamera(t);
        float wake = Ease((t - 0.15f) / 0.65f);
        DrawSetTransform(camera.Position, camera.Angle, Vector2.One * camera.Scale);
        float artScale = 610 / _phoneArtRegion.Size.Y;
        DrawTextureRectRegion(_phoneArt, new Rect2(-_phoneArtRegion.Size * artScale / 2, _phoneArtRegion.Size * artScale),
            _phoneArtRegion, Fade(Colors.White, alpha));
        Color ink = Fade(new Color("e8edf0"), alpha * wake);
        Color muted = Fade(new Color("909da6"), alpha * wake);
        Color rule = Fade(new Color("52616b"), alpha * wake * 0.35f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(-105, -284), _phoneTime, 10, ink);
        for (int i = 0; i < 4; i++)
            UiKit.Box(this, new Rect2(62 + i * 3, -277 - i * 2, 2, 3 + i * 2), ink, 0.5f);
        UiKit.Box(this, new Rect2(83, -283, 19, 9), null, 2, muted, 0.8f);
        UiKit.Box(this, new Rect2(85, -281, 13, 5), ink, 0.8f);
        DrawLine(new Vector2(104, -280), new Vector2(104, -277), muted, 1.2f, true);

        DrawLine(new Vector2(-107, -240), new Vector2(-99, -232), muted, 1.3f, true);
        DrawLine(new Vector2(-99, -240), new Vector2(-107, -232), muted, 1.3f, true);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(-24, -248), "下書き", 16, ink);
        UiKit.Text(this, UiKit.Zen, new Vector2(77, -244), "保存", 12, muted);
        DrawLine(new Vector2(-115, -210), new Vector2(115, -210), rule, 0.8f, true);
        var draft = PhoneDraft(t);
        UiKit.Text(this, UiKit.Zen, PhoneTextPosition, draft.Text, PhoneTextSize, ink);
        if (draft.Caret)
        {
            float x = PhoneTextPosition.X + UiKit.TextW(UiKit.Zen, draft.Text, PhoneTextSize) + 2;
            DrawLine(new Vector2(x, -102), new Vector2(x, -74), Fade(UiKit.Info, alpha * wake), 1.2f, true);
        }
        UiKit.Text(this, UiKit.Zen, new Vector2(-108, 64), "未送信", 10, muted);
        DrawLine(new Vector2(-121, 90), new Vector2(121, 90), rule, 0.8f, true);
        DrawRect(new Rect2(-122, 91, 244, 180), Fade(new Color("171e24"), alpha * wake));
        UiKit.Text(this, UiKit.Zen, new Vector2(-14, 94), "かな", 9, muted);
        DrawLine(new Vector2(-112, 113), new Vector2(112, 113), rule, 0.6f, true);

        var pressed = PhoneKeyAt(t);
        string[,] kana = { { "あ", "か", "さ" }, { "た", "な", "は" }, { "ま", "や", "ら" }, { "小゛゜", "わ", "、。?!" } };
        string[,] letters = { { "", "ABC", "DEF" }, { "GHI", "JKL", "MNO" }, { "PQRS", "TUV", "WXYZ" }, { "", "", "" } };
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                Rect2 key = new(-80 + col * 54, 119 + row * 36, 49, 32);
                float press = pressed.Row == row && pressed.Column == col + 1 ? pressed.Press : 0;
                DrawPhoneKey(key, press, alpha * wake);
                int font = row == 3 && col != 1 ? 10 : 17;
                UiKit.Text(this, UiKit.Zen, key.Position + new Vector2(0, 0), kana[row, col], font,
                    ink, HorizontalAlignment.Center, key.Size.X);
                if (letters[row, col] != "")
                    UiKit.Text(this, UiKit.Zen, key.Position + new Vector2(0, 21), letters[row, col], 6,
                        muted, HorizontalAlignment.Center, key.Size.X);
            }
            Rect2 left = new(-117, 119 + row * 36, 32, 32);
            DrawPhoneKey(left, 0, alpha * wake, true);
            if (row is 1 or 2)
                UiKit.Text(this, UiKit.Zen, left.Position + new Vector2(0, 8), row == 1 ? "ABC" : "123", 10,
                    muted, HorizontalAlignment.Center, left.Size.X);
            else if (row == 0)
            {
                Vector2 p = left.GetCenter();
                DrawPolyline(new[] { p + new Vector2(7, 6), p + new Vector2(7, -4), p + new Vector2(-7, -4) }, muted, 1, true);
                DrawPolyline(new[] { p + new Vector2(-3, -8), p + new Vector2(-7, -4), p + new Vector2(-3, 0) }, muted, 1, true);
            }
            else
            {
                Vector2 p = left.GetCenter();
                DrawArc(p, 7, 0, Mathf.Tau, 24, muted, 0.8f, true);
                DrawLine(p + new Vector2(-7, 0), p + new Vector2(7, 0), muted, 0.8f, true);
                DrawLine(p + new Vector2(0, -7), p + new Vector2(0, 7), muted, 0.8f, true);
            }
            Rect2 right = new(85, 119 + row * 36, 32, 32);
            DrawPhoneKey(right, row == 0 && pressed.Column == 4 ? pressed.Press : 0, alpha * wake, true);
            if (row == 0)
            {
                Vector2 p = right.GetCenter();
                DrawPolyline(new[] { p + new Vector2(-9, 0), p + new Vector2(-4, -6), p + new Vector2(9, -6),
                    p + new Vector2(9, 6), p + new Vector2(-4, 6), p + new Vector2(-9, 0) }, muted, 1, true);
                DrawLine(p + new Vector2(-1, -3), p + new Vector2(5, 3), muted, 0.8f, true);
                DrawLine(p + new Vector2(5, -3), p + new Vector2(-1, 3), muted, 0.8f, true);
            }
            else
                UiKit.Text(this, UiKit.Zen, right.Position + new Vector2(0, 8), row == 1 ? "空白" : row == 2 ? "改行" : "完了",
                    10, muted, HorizontalAlignment.Center, right.Size.X);
        }
        UiKit.Box(this, new Rect2(-37, 282, 74, 2), Fade(new Color("b3bec7"), alpha * wake * 0.75f), 1);
        DrawSetTransform(Vector2.Zero);
    }

    private void DrawPhoneKey(Rect2 key, float press, float alpha, bool utility = false)
    {
        UiKit.Box(this, new Rect2(key.Position + new Vector2(0, 1), key.Size), Fade(new Color("0c1015"), alpha), 4);
        Color color = new Color(utility ? "252f38" : "36424d").Lerp(new Color("738e9f"), press * 0.8f);
        UiKit.Box(this, key, Fade(color, alpha), 4);
    }

    private void DrawVoices(float t, float alpha)
    {
        float morph = Ease((t - 1.1f) / 2.6f);
        for (int i = 0; i < 18; i++)
        {
            float z = Mathf.PosMod(i * 0.073f + t * 0.12f, 1f);
            float spread = 0.18f + z * z * 1.35f;
            Vector2 ray = Vector2.FromAngle(i * 2.39996f);
            Vector2 p = new Vector2(610, 315) + new Vector2(ray.X * 650, ray.Y * 360) * spread;
            float opacity = alpha * Ease(z / 0.16f) * (1 - Ease((z - 0.82f) / 0.18f));
            float size = 22 + z * 55;
            Icon(_message, p, size, opacity * (1 - morph));
            Icon(_cores[i % 4], p, size * 0.65f, opacity * morph);
        }
    }

    private void DrawSpeed(float t, Color color, float alpha)
    {
        for (int i = 0; i < 15; i++)
        {
            float x = Mathf.PosMod(i * 191 + t * (310 + i * 13), 1700) - 220;
            float y = 78 + Mathf.PosMod(i * 113, 548);
            DrawLine(new Vector2(x - 120 - i * 7, y + 18), new Vector2(x, y), Fade(color, alpha * (0.14f + i % 3 * 0.07f)), 1 + i % 2, true);
        }
    }

    private void DrawFlight(int index, Vector2 position, float height, float alpha)
    {
        var texture = _fighters[index];
        Vector2 size = texture.GetSize() * (height / texture.GetHeight());
        DrawSetTransform(position, -0.08f, Vector2.One);
        DrawTextureRect(texture, new Rect2(-size * 0.5f, size), false, Fade(Colors.White, alpha));
        DrawSetTransform(Vector2.Zero);
    }

    private void Icon(Texture2D texture, Vector2 pos, float size, float alpha)
    {
        Vector2 dimensions = texture.GetSize() * (size / Mathf.Max(texture.GetWidth(), texture.GetHeight()));
        DrawTextureRect(texture, new Rect2(pos - dimensions * 0.5f, dimensions), false, Fade(Colors.White, alpha));
    }

    private void DrawOverlay(Node2D canvas)
    {
        int shot = Shot;
        float t = (float)(Elapsed - Cuts[shot]);
        if (shot is >= 1 and <= 3)
        {
            int i = shot - 1;
            float a = Ease((t - 0.2f) / 0.35f) * (1 - Ease((t - 3.25f) / 0.25f));
            canvas.DrawRect(new Rect2(0, 520, 1280, 162), new Color(0.025f, 0.025f, 0.03f, a * 0.78f));
            canvas.DrawLine(new Vector2(68, 542), new Vector2(101, 542), Fade(Accents[i], a), 2, true);
            UiKit.Text(canvas, UiKit.ZenBold, new Vector2(116, 528), _cast[i].CharacterName, 20, Fade(Accents[i], a));
            DrawQuote(canvas, DailyLines[i], new Vector2(68, 566), 29, a);
        }
        if (shot == 4)
        {
            float a = Ease((t - 1) / 0.7f) * (1 - Ease((t - 4.2f) / 0.5f));
            UiKit.Text(canvas, UiKit.ZenBold, new Vector2(72, 125), "……聞こえました。", 27, Fade(UiKit.PurifyHi, a));
            DrawQuote(canvas, "行きましょう。\nあの声の向こうへ。", new Vector2(72, 174), 27, a);
            float dive = Ease((t - 3.9f) / 1.1f);
            for (int i = 0; i < 18; i++)
            {
                Vector2 ray = Vector2.FromAngle(i * Mathf.Tau / 18);
                Vector2 origin = new(930, 300);
                float reach = 260 + dive * (280 + i % 3 * 110);
                canvas.DrawLine(origin + ray * reach, origin + ray * (reach + dive * 220),
                    Fade(Accents[i % 4], dive * 0.6f), 1 + i % 2, true);
            }
        }
        if (shot == 10)
        {
            float a = Ease(t / 0.35f);
            string title = "Refrain";
            float settle = 1 + (1 - Ease(t / 0.65f)) * 0.18f;
            canvas.DrawRect(Screen, new Color(0.015f, 0.02f, 0.025f, a * 0.38f));
            canvas.DrawSetTransform(new Vector2(640, 302), 0, Vector2.One * settle);
            float w = UiKit.TextW(UiKit.ZenBlack, title, 112);
            UiKit.Text(canvas, UiKit.ZenBlack, new Vector2(-w / 2, -62), title, 112, Fade(UiKit.PurifyHi, a));
            canvas.DrawSetTransform(Vector2.Zero);
            string line = "消された言葉は、消えていない。";
            UiKit.Text(canvas, UiKit.Zen, new Vector2((1280 - UiKit.TextW(UiKit.Zen, line, 23)) / 2, 408), line, 23,
                Fade(UiKit.White, Ease((t - 0.6f) / 0.5f)));
            float reach = Ease(t / 0.9f) * 320;
            for (int i = 0; i < 4; i++)
                canvas.DrawLine(new Vector2(640 - reach + reach * i / 2, 382),
                    new Vector2(640 - reach + reach * (i + 1) / 2 - 3, 382), Fade(Accents[i], a * 0.9f), 2, true);
        }
        if (shot is >= 5 and <= 8)
        {
            int i = shot - 5;
            float a = Ease((t - 0.28f) / 0.25f) * (1 - Ease((t - 3.3f) / 0.2f));
            canvas.DrawRect(new Rect2(0, 480, 590, 205), new Color(0.025f, 0.025f, 0.03f, a * 0.85f));
            canvas.DrawLine(new Vector2(68, 480), new Vector2(525, 480), Fade(Accents[i], a * 0.55f), 1, true);
            float x = 68 - (1 - Ease(t / 0.55f)) * 35;
            // ★2026-09-17：ここはジョブ名（結び手／灯し手…）を名前の上に置いていたが、ユーザー指示で
            //   ジョブ名・型名の表記は全廃。ハブ／HUD と同じ「@ハンドル」に差し替える＝
            //   キャラ名の添え書きは最後まで X のアカウント表記で統一される。
            UiKit.Text(canvas, UiKit.ZenBold, new Vector2(x, 491), Handle(_cast[i]), 18, Fade(Accents[i], a));
            UiKit.Text(canvas, UiKit.ZenBlack, new Vector2(x, 516), _cast[i].CharacterName, 43, Fade(UiKit.White, a));
            DrawQuote(canvas, CutinLines[i], new Vector2(68, 581), 27, a);
        }
        if (shot == 9)
        {
            float a = Ease((t - 0.5f) / 0.6f) * (1 - Ease((t - 3.5f) / 0.4f));
            canvas.DrawRect(new Rect2(0, 568, 1280, 112), new Color(0.015f, 0.02f, 0.025f, a * 0.65f));
            string line = "まだ届いていない声が、待っている。";
            UiKit.Text(canvas, UiKit.ZenBold, new Vector2((1280 - UiKit.TextW(UiKit.ZenBold, line, 30)) / 2, 598), line, 30, Fade(UiKit.PurifyHi, a));
        }
        float bars = shot <= 3 ? 38 : shot == 4 ? Mathf.Lerp(38, 22, Ease(t / 1.2f)) : 22;
        canvas.DrawRect(new Rect2(0, 0, 1280, bars), Colors.Black);
        canvas.DrawRect(new Rect2(0, 720 - bars, 1280, bars), Colors.Black);
        float fade = Mathf.Max(1 - Ease((float)Elapsed / 0.6f), Ease((float)(Elapsed - Duration + 1)));
        if (_leaving) fade = Mathf.Max(fade, Ease((float)_leaveTime / 0.45f));
        canvas.DrawRect(Screen, new Color(0, 0, 0, fade));
        if (Elapsed > 1 && !_leaving && Elapsed < Duration - 1.3)
        {
            bool hover = SkipRect.HasPoint(Pad.MousePos());
            UiKit.Box(canvas, SkipRect, new Color(0.025f, 0.03f, 0.035f, hover ? 0.92f : 0.7f), 5);
            UiKit.Text(canvas, UiKit.Zen, SkipRect.Position + new Vector2(18, 9), "スキップ", 18, Fade(UiKit.White, hover ? 1 : 0.72f));
            Vector2 p = SkipRect.Position + new Vector2(110, 17);
            canvas.DrawPolyline(new[] { p, p + new Vector2(7, 6), p + new Vector2(0, 12) }, UiKit.Text2, 1.5f, true);
            if (_skipHold > 0)
                canvas.DrawLine(SkipRect.Position + new Vector2(0, 44), SkipRect.Position + new Vector2(140 * (float)(_skipHold / 0.65), 44), UiKit.Info, 2);
        }
    }

    private static void DrawQuote(Node2D canvas, string text, Vector2 position, int size, float alpha)
    {
        foreach (string line in text.Split('\n'))
        {
            UiKit.Text(canvas, UiKit.ZenBold, position, line, size, Fade(UiKit.White, alpha));
            position.Y += size + 9;
        }
    }

    private partial class FilmOverlay : Node2D
    {
        public OpeningFilm Film = null!;
        public override void _Draw() => Film.DrawOverlay(this);
    }

    private partial class FilmPost : Node2D
    {
        public Texture2D Plate = null!, Avatar = null!;
        public JobTuning Cast = null!;
        public Color Accent;

        public override void _Draw()
        {
            DrawTextureRect(Plate, new Rect2(0, 0, 620, 380), false);
            UiKit.FaceAvatar(this, new Vector2(65, 56), 27, Avatar, Accent, false, 0);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(109, 28), Cast.CharacterName, 27, UiKit.White);
            UiKit.Text(this, UiKit.Zen, new Vector2(109, 61), Handle(Cast), 22, Accent);
            // Mina's later story is deliberately not previewed in the opening.
            string text = Cast.Id == Job.Tank ? "……" : BossPostStory.Get(Cast.CharacterId).Posts[0];
            UiKit.Multi(this, UiKit.Zen, new Vector2(42, 135), text, 30, UiKit.White, 536);
            for (int i = 0; i < 3; i++)
                DrawLine(new Vector2(42 + i * 156, 330), new Vector2(114 + i * 156, 330), Fade(Accent, 0.45f), 2, true);
        }
    }
}
