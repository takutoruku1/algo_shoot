using Godot;
using System;

public partial class OpeningFilm : Node2D
{
    private const double PhoneDuration = 10.5;
    public const double Duration = PhoneDuration + 37.5;
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
    private Texture2D[] _daily = null!, _fighters = null!, _cores = null!, _cutins = null!;
    private JobTuning[] _cast = null!;
    private Texture2D _city = null!, _light = null!, _space = null!, _message = null!;
    private Sprite2D _mina = null!;
    private ShaderMaterial _wind = null!;
    private FilmOverlay _overlay = null!;
    private bool _inputArmed, _musicRaised, _musicFading;
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
        }
        _city = GD.Load<Texture2D>("res://char/bg2/title/L1_far.png");
        _light = GD.Load<Texture2D>("res://char/bg2/title/L4_light_warm.png");
        _space = GD.Load<Texture2D>("res://char/bg2/prologue/bg_p4_unsent.png");
        _message = GD.Load<Texture2D>("res://char/v3/fx/rei/bubble_empty_1.png");
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
        Audio.Instance?.PlayUiConfirm();
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
            int previousShot = Shot;
            Elapsed = Math.Min(Duration, Elapsed + delta);
            bool held = SkipHeld();
            if (!held) _inputArmed = true;
            _skipHold = _inputArmed && held && Elapsed >= 0.6 ? _skipHold + delta : 0;
            if (_skipHold >= 0.65 || (Elapsed > 1 && Elapsed < Duration - 1.3
                && SkipRect.HasPoint(Pad.MousePos()) && Pad.MouseClick()))
                RequestSkip();
            if (!_leaving && !_musicRaised && Elapsed >= Cuts[4])
            {
                _musicRaised = true;
                Audio.Instance?.Music(Audio.Instance.BgmBossRei, 1.0f);
            }
            if (!_leaving && Shot != previousShot && Shot is >= 5 and <= 8)
                Audio.Instance?.PlaySpell();
            if (!_leaving && Shot != previousShot && Shot == 9)
                Audio.Instance?.PlayPurify();
            if (!_leaving && !_musicFading && Elapsed >= Duration - 1.3)
            {
                _musicFading = true;
                Audio.Instance?.StopMusic(1.3f);
            }
            if (Elapsed >= Duration) { Complete(); return; }
        }
        UpdateMina();
        QueueRedraw();
        _overlay.QueueRedraw();
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
    // キャラの X ハンドル（Hub.AccountHandle と同じ引き方。ミナだけステージ表に居ないので直書き）。
    private static string Handle(JobTuning job) => job.Id == Job.Tank
        ? "@mina_ai_"
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
            Background(_space, 1.08f, new Vector2(-12 + t * 3, 0), alpha * 0.65f);
            DrawRect(Screen, new Color(0.025f, 0.03f, 0.035f, alpha * 0.62f));
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
            Background(i < 3 ? _daily[i] : _city, 1.2f, new Vector2(25 - t * 16, 0), alpha * 0.6f);
            DrawRect(Screen, new Color(0.025f, 0.025f, 0.035f, alpha * 0.4f));
            DrawSpeed(t, Accents[i], alpha);
            float arrive = Ease(t / 0.45f);
            Vector2 pos = new(Mathf.Lerp(190, 310, arrive) + t * 14, 300 - t * 10);
            for (int j = 0; j < 7; j++)
            {
                float travel = Mathf.PosMod(t * 480 + j * 170, 1500);
                Vector2 p = new(pos.X + 70 + travel, pos.Y - 65 + Mathf.Sin(j * 1.4f) * 115);
                DrawLine(p - new Vector2(90, -10), p, Fade(Accents[i], alpha * 0.55f), 2, true);
                Icon(_cores[i], p, 46, alpha);
            }
            DrawCutin(i, t, alpha);
            DrawFlight(i, pos, 360, alpha);
        }
        else
        {
            Background(_city, 1.1f, new Vector2((float)Elapsed * 2 - 55, 18), alpha);
            Background(_light, 1.16f, new Vector2((float)Elapsed * 6 - 175, -6), alpha * 0.8f);
            DrawSpeed(t * 0.45f, Accents[3], alpha * (shot == 9 ? 0.55f : 0.18f));
            if (shot == 9)
            {
                for (int i = 0; i < 4; i++)
                {
                    float arrive = Ease((t - i * 0.16f) / 0.8f);
                    Vector2 p = new(180 + i * 296 - (1 - arrive) * 100, 399 + (i % 2) * 27 - t * 6);
                    DrawFlight(i, p, i == 3 ? 405 : 370, alpha * arrive);
                    Icon(_cores[i], p + new Vector2(118, 18), 34, alpha * arrive);
                }
            }
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
        float enter = Ease(t / 0.45f);
        float shift = (1 - enter) * 260;
        Vector2[] band = { new(778 + shift, 22), new(1280, 22), new(1280, 698), new(585 + shift, 698) };
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
            new(Mathf.Min(rect.End.X, 1280), rect.Position.Y),
            new(Mathf.Min(rect.End.X, 1280), rect.End.Y),
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

    private void DrawPhone(float t, float alpha)
    {
        float pull = Ease(t / (float)PhoneDuration);
        float scale = Mathf.Lerp(1.12f, 0.94f, pull);
        DrawSetTransform(new Vector2(640, 364), -0.035f + pull * 0.025f, Vector2.One * scale);
        UiKit.Box(this, new Rect2(-161, -282, 322, 564), new Color(0.055f, 0.065f, 0.075f, alpha), 25, new Color(0.38f, 0.44f, 0.47f, alpha), 1.8f);
        UiKit.Box(this, new Rect2(-149, -268, 298, 536), new Color(0.02f, 0.03f, 0.035f, alpha), 18);
        UiKit.Box(this, new Rect2(-38, -258, 76, 9), new Color(0.1f, 0.13f, 0.15f, alpha), 4);
        UiKit.Text(this, UiKit.Zen, new Vector2(-124, -213), "下書き", 17, Fade(UiKit.Text3, alpha));
        var draft = PhoneDraft(t);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(-124, -110), draft.Text, 31, Fade(UiKit.White, alpha));
        if (draft.Caret)
        {
            float x = -123 + UiKit.TextW(UiKit.ZenBold, draft.Text, 31);
            DrawLine(new Vector2(x, -105), new Vector2(x, -75), Fade(UiKit.Info, alpha), 1.5f);
        }
        DrawLine(new Vector2(-124, 174), new Vector2(124, 174), new Color(0.35f, 0.46f, 0.49f, alpha * 0.4f), 1);
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 9 - row; col++)
                UiKit.Box(this, new Rect2(-122 + col * 28 + row * 9, 192 + row * 17, 21, 11), new Color(0.25f, 0.31f, 0.33f, alpha * 0.5f), 2);
        DrawSetTransform(Vector2.Zero);
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
            UiKit.Text(canvas, UiKit.ZenBold, new Vector2(72, 125), "あの声は、", 27, Fade(UiKit.PurifyHi, a));
            UiKit.Text(canvas, UiKit.ZenBold, new Vector2(72, 166), "わたくしが、覚えておきます。", 27, Fade(UiKit.PurifyHi, a));
        }
        if (shot == 10)
        {
            float a = Ease(t / 0.8f);
            string title = "Refrain";
            float w = UiKit.TextW(UiKit.ZenBlack, title, 112);
            UiKit.Text(canvas, UiKit.ZenBlack, new Vector2((1280 - w) / 2, 240), title, 112, Fade(UiKit.PurifyHi, a));
            string line = "消された言葉は、消えていない。";
            UiKit.Text(canvas, UiKit.Zen, new Vector2((1280 - UiKit.TextW(UiKit.Zen, line, 23)) / 2, 391), line, 23, Fade(UiKit.White, a));
            float reach = Ease(t / 1.1f) * 340;
            canvas.DrawLine(new Vector2(640 - reach, 376), new Vector2(640 + reach, 376), Fade(UiKit.Info, a * 0.8f), 1.5f, true);
        }
        if (shot is >= 5 and <= 8)
        {
            int i = shot - 5;
            float a = Ease((t - 0.28f) / 0.25f) * (1 - Ease((t - 3.3f) / 0.2f));
            canvas.DrawRect(new Rect2(0, 480, 590, 205), new Color(0.025f, 0.025f, 0.03f, a * 0.85f));
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
            float a = Ease((t - 0.8f) / 0.7f);
            string line = "送れなかった言葉を、今度こそ。";
            UiKit.Text(canvas, UiKit.ZenBold, new Vector2((1280 - UiKit.TextW(UiKit.ZenBold, line, 27)) / 2, 623), line, 27, Fade(UiKit.PurifyHi, a));
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
}
