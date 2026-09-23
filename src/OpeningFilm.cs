using Godot;
using System;

public partial class OpeningFilm : Node2D
{
    private const double PhoneDuration = 10.5;
    public const double Duration = PhoneDuration + 37.5;
    private static readonly float[] ReleaseTimes = { 1.5f, 1.55f, 1.45f, 0.95f };
    private static readonly float[] ImpactTimes = { 1.91f, 2.45f, 2.18f, 2.46f };
    public Action? Completed;
    public double Elapsed { get; private set; }
    public bool Finished { get; private set; }

    private static readonly double[] Cuts =
    {
        0, PhoneDuration, PhoneDuration + 3.5, PhoneDuration + 7, PhoneDuration + 10.5,
        PhoneDuration + 15.5, PhoneDuration + 18.6, PhoneDuration + 22.6,
        PhoneDuration + 25.9, PhoneDuration + 29.5, PhoneDuration + 33.5, Duration,
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
    private static readonly Vector2[] PortraitFocus = { new(0.52f, 0.24f), new(0.64f, 0.25f), new(0.51f, 0.24f), new(0.45f, 0.2f) };
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
    private FontFile _filmFont = null!, _titleFont = null!;
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
        _cutins = new Texture2D[4];
        _filmFont = GD.Load<FontFile>("res://assets/fonts/ShipporiMincho-SemiBold.ttf");
        _titleFont = GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-Italic.ttf");
        for (int i = 0; i < 4; i++)
        {
            string id = Characters[i];
            _cast[i] = Array.Find(Jobs.All, job => job.CharacterId == id)!;
            _cutins[i] = GD.Load<Texture2D>($"res://char/bg2/opening/op_{id}_cutin_v1.png");
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
            if (Crossed(start + ReleaseTimes[i] - 0.45)) Audio.Instance?.PlayChargeReady(_cast[i].Id);
            if (Crossed(start + ReleaseTimes[i])) Audio.Instance?.PlayChargeRelease(_cast[i].Id);
            if (Crossed(start + ImpactTimes[i])) Audio.Instance?.PlayChargeImpact(_cast[i].Id);
            if (i == 1)
                for (int j = 1; j < 3; j++)
                    if (Crossed(start + ReleaseTimes[i] + j * 0.18)) Audio.Instance?.PlayShot();
            if (i == 2 && Crossed(start + ReleaseTimes[i] + 0.36)) Audio.Instance?.PlayChargeRelease(_cast[i].Id);
            if (i == 3)
            {
                for (int j = 1; j < 6; j++)
                    if (Crossed(start + ReleaseTimes[i] + j * 0.14)) Audio.Instance?.PlayShot();
                if (Crossed(start + 2.12)) Audio.Instance?.PlayChargeRelease(_cast[i].Id);
            }
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
            DrawAction(i, t, alpha);
            DrawCutin(i, t, alpha);
        }
        else
        {
            Background(_city, 1.1f, new Vector2((float)Elapsed * 2 - 55, 18), alpha);
            Background(_light, 1.16f, new Vector2((float)Elapsed * 6 - 175, -6), alpha * 0.8f);
            if (shot == 9)
                DrawTogether(t, alpha);
        }
    }

    private void DrawRoute(int index, float t, float alpha)
    {
        for (int layer = 0; layer < 3; layer++)
        {
            var texture = _routes[index, layer];
            Vector2 size = texture.GetSize() * (720f / texture.GetHeight());
            float pace = index switch
            {
                0 => 0.5f + Ease((t - 0.85f) / 0.8f) * 1.8f,
                1 => -0.65f,
                2 => 0.35f + Ease((t - 1.3f) / 0.8f) * 0.5f,
                _ => 0.15f + Ease((t - 2.1f) / 0.5f) * 0.6f,
            };
            float speed = (layer == 0 ? 35 : layer == 1 ? 135 : 330) * pace;
            float x = -Mathf.PosMod(120 + t * speed, size.X);
            Color tint = Fade(Colors.White, alpha * (layer == 2 ? 0.65f : 1));
            DrawTextureRect(texture, new Rect2(new Vector2(x, 0), size), false, tint);
            DrawTextureRect(texture, new Rect2(new Vector2(x + size.X, 0), size), false, tint);
        }
        DrawRect(Screen, new Color(0.015f, 0.02f, 0.025f, alpha * 0.2f));
    }

    private void DrawAction(int index, float t, float alpha)
    {
        switch (index)
        {
            case 0: DrawAkariAction(t, alpha); break;
            case 1: DrawKoharuAction(t, alpha); break;
            case 2: DrawReiAction(t, alpha); break;
            case 3: DrawMinaAction(t, alpha); break;
        }
    }

    private void DrawAkariAction(float t, float alpha)
    {
        float dash = Ease((t - 0.88f) / 0.6f);
        float impact = Mathf.Max(0, t - ImpactTimes[0]);
        float kick = Mathf.Sin(impact * 60) * 10 * (1 - Ease(impact / 0.22f));
        Vector2 target = new(1000 + kick, 270);
        Vector2 position = new(180 + dash * 440 - Ease((t - 1.5f) / 0.2f) * 38, 405 - dash * 82);
        DrawPostBreak(0, t - ImpactTimes[0], alpha * Ease((t - 0.8f) / 0.35f), target, 1.15f);
        if (t is > 0.9f and < 1.5f)
        {
            for (int echo = 2; echo > 0; echo--)
            {
                float lag = Ease((t - 0.88f - echo * 0.06f) / 0.6f);
                DrawFlight(0, new Vector2(180 + lag * 440, 405 - lag * 82), 330, alpha * 0.09f, -0.2f);
            }
        }
        DrawFlight(0, position, 330 + dash * 30, alpha * Ease(t / 0.4f), -0.06f - Mathf.Sin(dash * Mathf.Pi) * 0.22f);
        Vector2 muzzle = position + new Vector2(110, -20);
        float charge = Ease((t - 0.5f) / 0.55f) * (1 - Ease((t - ReleaseTimes[0]) / 0.16f));
        DrawShotArt(0, muzzle, 50 + charge * 85, -0.15f, alpha * charge);
        float p = Mathf.Clamp((t - ReleaseTimes[0]) / (ImpactTimes[0] - ReleaseTimes[0]), 0, 1);
        float shotAlpha = alpha * Ease((t - ReleaseTimes[0]) / 0.06f) * (1 - Ease((t - ImpactTimes[0]) / 0.12f));
        for (int echo = 3; echo >= 0; echo--)
        {
            float lag = Mathf.Max(0, p - echo * 0.045f);
            Vector2 point = new Vector2(730, 303).Lerp(target, lag * lag);
            DrawShotArt(0, point, 190 - echo * 20, -0.09f, shotAlpha * (echo == 0 ? 1 : 0.12f));
        }
    }

    private void DrawKoharuAction(float t, float alpha)
    {
        Vector2 position = new(1010 - Ease(t / 0.65f) * 92, 355 + Mathf.Sin(t * 1.6f) * 25);
        DrawFlight(1, position, 330, alpha * Ease(t / 0.5f), 0.06f, true);
        for (int j = 0; j < 3; j++)
        {
            Vector2 target = new(250 + j * 105, 170 + j * 150);
            float launch = ReleaseTimes[1] + j * 0.18f;
            float impact = ImpactTimes[1] + j * 0.22f;
            DrawPostBreak(1, t - impact, alpha * Ease((t - 1.1f) / 0.3f), target, 0.66f, 0.07f - j * 0.06f);
            Vector2 start = new(824, 285 + j * 58);
            float p = Mathf.Clamp((t - launch) / (impact - launch), 0, 1);
            Vector2 controlA = start + new Vector2(-145, (j - 1) * 230);
            Vector2 controlB = target + new Vector2(270, (j - 1) * 140);
            float show = alpha * Ease((t - 0.68f - j * 0.12f) / 0.25f) * (1 - Ease((t - impact) / 0.15f));
            for (int echo = 2; echo >= 0; echo--)
            {
                float lag = Mathf.Max(0, p - echo * 0.045f);
                Vector2 point = start.BezierInterpolate(controlA, controlB, target, lag);
                if (t < launch) point += new Vector2(Mathf.Sin(t * 4 + j) * 12, Mathf.Cos(t * 3 + j) * 16);
                float angle = start.BezierDerivative(controlA, controlB, target, lag).Angle();
                DrawShotArt(1, point, echo == 0 ? 95 : 72, angle, show * (echo == 0 ? 1 : p * 0.12f));
            }
        }
    }

    private void DrawReiAction(float t, float alpha)
    {
        float reveal = Ease((t - 0.5f) / 0.8f);
        Vector2 position = new(310 + reveal * 65, 430 - reveal * 85);
        float recoil = Mathf.Sin(Ease((t - ReleaseTimes[2]) / 0.22f) * Mathf.Pi) * 18;
        DrawFlight(2, position - new Vector2(recoil, 0), 365, alpha * reveal, -0.02f);
        for (int j = 0; j < 3; j++)
            DrawPostBreak(2, t - ImpactTimes[2] - j * 0.05f, alpha * Ease((t - 1) / 0.4f),
                new Vector2(1020, 180 + j * 153), 0.62f, (j - 1) * 0.08f);
        Vector2 start = new(490, 336);
        for (int volley = 0; volley < 2; volley++)
        {
            float launch = ReleaseTimes[2] + volley * 0.36f;
            float p = Mathf.Clamp((t - launch) / 0.9f, 0, 1.35f);
            float visible = alpha * Ease((t - launch) / 0.07f) * (1 - Ease((p - 0.95f) / 0.4f));
            for (int lane = -2; lane <= 2; lane++)
            {
                float angle = lane * 0.14f + (volley == 1 ? 0.035f : 0);
                Vector2 direction = Vector2.FromAngle(angle);
                Vector2 point = start + direction * p * 650;
                DrawShotArt(2, point - direction * 32, 66, angle, visible * 0.12f);
                DrawShotArt(2, point, 100, angle, visible);
            }
        }
    }

    private void DrawMinaAction(float t, float alpha)
    {
        float arrive = Ease((t - 0.35f) / 0.8f);
        float recoil = Mathf.Sin(Ease((t - 2.12f) / 0.3f) * Mathf.Pi) * 24;
        Vector2 position = new(260 - recoil, 330 + (1 - arrive) * 55);
        DrawFlight(3, position, 365, alpha * arrive, 0);
        for (int j = 2; j >= 0; j--)
            DrawPostBreak(3, t - ImpactTimes[3] - j * 0.156f, alpha * Ease((t - 1.4f) / 0.35f),
                new Vector2(725 + j * 160, 315 - j * 8), 0.72f - j * 0.1f, 0.025f * j);
        for (int j = 0; j < 6; j++)
        {
            float launch = ReleaseTimes[3] + j * 0.14f;
            float p = (t - launch) / 0.6f;
            if (p is < 0 or > 1.2f) continue;
            Vector2 point = new(365 + p * 850, 304 + (j % 3 - 1) * 22);
            DrawShotArt(3, point, 72, 0, alpha * (1 - Ease((p - 0.85f) / 0.35f)));
        }
        float charge = Ease((t - 1.7f) / 0.4f) * (1 - Ease((t - 2.12f) / 0.1f));
        DrawShotArt(3, new Vector2(375, 310), 65 + charge * 80, 0, alpha * charge);
        float finish = (t - 2.12f) / 0.83f;
        if (finish is >= 0 and < 1.2f)
        {
            float a = alpha * (1 - Ease((finish - 0.95f) / 0.25f));
            Vector2 point = new(375 + finish * 850, 310);
            DrawShotArt(3, point - new Vector2(60, 0), 150, 0, a * 0.16f);
            DrawShotArt(3, point, 230, 0, a);
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

    private void DrawPostBreak(int index, float time, float alpha, Vector2 center, float scale = 1, float angle = -0.1f)
    {
        Texture2D plate = _postViews[index].GetTexture();
        if (time < 0)
        {
            DrawSetTransform(center, angle, Vector2.One * scale);
            DrawTextureRect(plate, PostRect, false, Fade(Colors.White, alpha));
            DrawSetTransform(Vector2.Zero);
            return;
        }
        float fade = 1 - Ease(time / 1.05f);
        DrawSetTransform(center, angle, Vector2.One * scale);
        var mesh = _postMeshes[index];
        GlassFractureArt.DrawShards(this, plate, PostRect, mesh.Points, mesh.Triangles,
            time * 1.65f, 1.4f, Fade(Colors.White, alpha * fade));
        DrawSetTransform(Vector2.Zero);
        DrawShotArt(index, center, (64 + time * 38) * scale, index == 1 ? Mathf.Pi : 0, alpha * fade);
    }

    private void DrawTogether(float t, float alpha)
    {
        float launch = Mathf.Pow(Mathf.Max(0, t - 2.8f) / 1.2f, 2);
        for (int i = 0; i < 4; i++)
        {
            float arrive = Ease((t - i * 0.14f) / 0.65f);
            Vector2 p = new(180 + i * 295 - (1 - arrive) * 250 + launch * (1650 - i * 190),
                330 + (i % 2) * 35 - t * 7 - launch * 190);
            DrawFlight(i, p, i == 3 ? 390 : 350, alpha * arrive);
            DrawShotArt(i, p + new Vector2(115, 35), 50, -0.1f, alpha * arrive);
        }
    }

    private void Background(Texture2D texture, float zoom, Vector2 offset, float alpha)
    {
        Vector2 size = texture.GetSize();
        size *= Mathf.Max(1280 / size.X, 720 / size.Y) * zoom;
        DrawTextureRect(texture, new Rect2((Screen.Size - size) * 0.5f + offset, size), false, Fade(Colors.White, alpha));
    }

    private void DrawCutin(int index, float t, float alpha)
    {
        switch (index)
        {
            case 0:
            {
                float enter = Ease(t / 0.32f);
                float pull = Ease((t - 0.18f) / 0.46f);
                float leave = Ease((t - 0.93f) / 0.35f);
                if (leave >= 1) return;
                float shift = (1 - enter) * 330 + leave * 760;
                DrawRect(Screen, new Color(0.025f, 0.012f, 0.008f, alpha * enter * (1 - leave) * 0.48f));
                float top = Mathf.Lerp(224, 22, pull), bottom = Mathf.Lerp(352, 698, pull);
                Vector2[] mask = { new(640 + shift, top), new(1280 + shift, top),
                    new(1280 + shift, bottom), new(490 + shift, bottom) };
                DrawPortrait(0, new Vector2(945 + shift - pull * 32, Mathf.Lerp(282, 226, pull)),
                    Mathf.Lerp(1700, 1100, pull), mask, alpha * enter * (1 - leave));
                break;
            }
            case 1:
            {
                float enter = Ease(t / 0.68f), leave = Ease((t - 1.22f) / 0.48f);
                if (leave >= 1) return;
                float rise = (1 - enter) * 200 - leave * 110;
                DrawRect(Screen, new Color(0.013f, 0.027f, 0.022f, alpha * enter * (1 - leave) * 0.48f));
                DrawPortrait(1, new Vector2(410 - t * 25, 226 + rise), 1030 + t * 45,
                    RectPolygon(new Rect2(0, 698 - enter * 676, 760, enter * 676)), alpha * (1 - leave));
                break;
            }
            case 2:
            {
                float leave = Ease((t - 1.13f) / 0.44f);
                if (leave >= 1) return;
                DrawRect(Screen, new Color(0.028f, 0.012f, 0.021f, alpha * Ease(t / 0.3f) * (1 - leave) * 0.58f));
                for (int panel = 0; panel < 3; panel++)
                {
                    float open = Ease((t - Math.Abs(panel - 1) * 0.13f) / 0.43f) * (1 - leave);
                    float height = open * 676;
                    DrawPortrait(2, new Vector2(694 - t * 24, 215 - t * 9), 1080 + t * 30,
                        RectPolygon(new Rect2(285 + panel * 255, 360 - height / 2, 255, height)), alpha * open);
                }
                break;
            }
            case 3:
            {
                float pull = Ease((t - 0.25f) / 0.7f), leave = Ease((t - 1.13f) / 0.36f);
                if (leave >= 1) return;
                float enter = Ease(t / 0.22f);
                DrawRect(Screen, new Color(0.01f, 0.022f, 0.031f, alpha * enter * (1 - leave) * 0.58f));
                float height = Mathf.Lerp(122, 676, pull);
                DrawPortrait(3, new Vector2(Mathf.Lerp(760, 960, pull), Mathf.Lerp(286, 209, pull)),
                    Mathf.Lerp(2850, 1100, pull), RectPolygon(new Rect2(0, Mathf.Lerp(220, 22, pull), 1280, height)),
                    alpha * enter * (1 - leave));
                break;
            }
        }
    }

    private static Vector2[] RectPolygon(Rect2 rect) => new[]
    {
        rect.Position, new Vector2(rect.End.X, rect.Position.Y), rect.End, new Vector2(rect.Position.X, rect.End.Y),
    };

    private void DrawPortrait(int index, Vector2 focus, float height, Vector2[] mask, float alpha)
    {
        if (alpha <= 0) return;
        Texture2D texture = _cutins[index];
        Vector2 size = texture.GetSize() * (height / texture.GetHeight());
        Rect2 rect = new(focus - size * PortraitFocus[index], size);
        foreach (Vector2[] polygon in Geometry2D.IntersectPolygons(RectPolygon(rect), mask))
        {
            Vector2[] uv = Array.ConvertAll(polygon, point => (point - rect.Position) / size);
            DrawPolygon(polygon, new[] { Fade(Colors.White, alpha) }, uv, texture);
        }
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

    private void DrawFlight(int index, Vector2 position, float height, float alpha, float angle = -0.08f, bool faceLeft = false)
    {
        var texture = _fighters[index];
        Vector2 size = texture.GetSize() * (height / texture.GetHeight());
        DrawSetTransform(position, angle, new Vector2(faceLeft ? -1 : 1, 1));
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
            DrawCaptionShade(canvas, 486, a);
            UiKit.Text(canvas, _filmFont, new Vector2(68, 520), _cast[i].CharacterName, 24, Fade(Accents[i], a));
            DrawQuote(canvas, DailyLines[i], new Vector2(68, 564), 30, a, t - 0.2f);
        }
        if (shot == 4)
        {
            float a = Ease((t - 1) / 0.7f) * (1 - Ease((t - 4.2f) / 0.5f));
            UiKit.Text(canvas, _filmFont, new Vector2(72, 125), "……聞こえました。", 29, Fade(UiKit.PurifyHi, a));
            DrawQuote(canvas, "行きましょう。\nあの声の向こうへ。", new Vector2(72, 176), 29, a, t - 1);
        }
        if (shot == 10)
        {
            float a = Ease(t / 0.35f);
            string title = "Refrain";
            float settle = 1 + (1 - Ease(t / 1.2f)) * 0.06f;
            canvas.DrawRect(Screen, new Color(0.015f, 0.02f, 0.025f, a * 0.38f));
            canvas.DrawSetTransform(new Vector2(640, 289), 0, Vector2.One * settle);
            float w = UiKit.TextW(_titleFont, title, 164);
            UiKit.Text(canvas, _titleFont, new Vector2(-w / 2, -86), title, 164, Fade(new Color("f0eee8"), a));
            canvas.DrawSetTransform(Vector2.Zero);
            string line = "消された言葉は、消えていない。";
            UiKit.Text(canvas, _filmFont, new Vector2((1280 - UiKit.TextW(_filmFont, line, 26)) / 2, 418), line, 26,
                Fade(UiKit.White, Ease((t - 0.6f) / 0.5f)));
        }
        if (shot is >= 5 and <= 8)
        {
            int i = shot - 5;
            float duration = (float)(Cuts[shot + 1] - Cuts[shot]);
            float a = Ease((t - 0.18f) / 0.3f) * (1 - Ease((t - duration + 0.3f) / 0.3f));
            DrawCaptionShade(canvas, 493, a);
            Vector2 namePosition = i == 1 ? new(928, 78) : new(68, 78);
            DrawName(canvas, _cast[i].CharacterName, namePosition, i == 3 ? 72 : 80, t - 0.18f, a, i == 3);
            Vector2 handlePosition = i == 3 ? new(72, 522) : namePosition + new Vector2(4, 106);
            UiKit.Text(canvas, _titleFont, handlePosition, Handle(_cast[i]), 26, Fade(Accents[i], a));
            DrawQuote(canvas, CutinLines[i], new Vector2(i == 1 ? 840 : 72, 573), 30, a, t - 0.48f);
        }
        if (shot == 9)
        {
            float a = Ease((t - 0.5f) / 0.6f) * (1 - Ease((t - 3.5f) / 0.4f));
            DrawCaptionShade(canvas, 530, a);
            string line = "まだ届いていない声が、待っている。";
            UiKit.Text(canvas, _filmFont, new Vector2((1280 - UiKit.TextW(_filmFont, line, 32)) / 2, 590), line, 32, Fade(UiKit.PurifyHi, a));
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

    private static void DrawCaptionShade(Node2D canvas, float top, float alpha)
    {
        UiKit.VGradient(canvas, new Rect2(0, top, 1280, 720 - top),
            new[] { new Color(0.015f, 0.02f, 0.025f, 0), new Color(0.015f, 0.02f, 0.025f, alpha * 0.88f) },
            new[] { 0f, 0.72f });
    }

    private void DrawName(Node2D canvas, string name, Vector2 position, int size, float time, float alpha, bool vertical)
    {
        for (int i = 0; i < name.Length; i++)
        {
            float reveal = Ease((time - i * 0.09f) / 0.4f);
            string letter = name[i].ToString();
            Vector2 offset = vertical ? new(0, i * size) : new(UiKit.TextW(_filmFont, name[..i], size), 0);
            Vector2 point = position + offset + new Vector2(0, (1 - reveal) * 15);
            UiKit.Text(canvas, _filmFont, point + new Vector2(0, 2), letter, size, new Color(0, 0, 0, alpha * reveal * 0.45f));
            UiKit.Text(canvas, _filmFont, point, letter, size, Fade(new Color("f7f3ed"), alpha * reveal));
        }
    }

    private void DrawQuote(Node2D canvas, string text, Vector2 position, int size, float alpha, float time)
    {
        int index = 0;
        foreach (string line in text.Split('\n'))
        {
            float reveal = Ease((time - index * 0.16f) / 0.45f);
            UiKit.Text(canvas, _filmFont, position + new Vector2(0, (1 - reveal) * 8), line, size, Fade(UiKit.White, alpha * reveal));
            position.Y += size + 12;
            index++;
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
        }
    }
}
