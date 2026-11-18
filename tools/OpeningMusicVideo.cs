using Godot;
using System;
using System.Reflection;
using System.Text.Json;

public partial class OpeningMusicVideo : OpeningFilm
{
    private const double MovieDuration = 72.6;
    private const string Folder = "res://char/bg2/opening/anime_v2/";
    private const string Output = "res://build/qa_story/opening_layers_20261101";
    private const BindingFlags PrivateReflection = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo SourceShot = typeof(OpeningFilm).GetMethod("DrawShot", PrivateReflection)!;
    private static readonly PropertyInfo SourceTime = typeof(OpeningFilm).GetProperty(nameof(Elapsed))!;
    private static readonly Rect2 Frame = new(0, 0, 1280, 720);
    private static readonly double[] Edit = { 0, 4.94, 6.56, 8.18, 9, 12.3, 15.5, 18.8, 22, 25.35,
        28.58, 31.84, 35.15, 35.91, 36.67, 37.42, 40.7, 44.76, 48.24, 51.48, 52.58, 53.69,
        54.8, 55.92, 57.04, 58.16, 61.36, 66.16, MovieDuration };
    private readonly Texture2D[] _art = new Texture2D[6];
    private readonly Texture2D[] _rooms = new Texture2D[3];
    private readonly Texture2D[] _insert = new Texture2D[3];
    private readonly Texture2D[] _portrait = new Texture2D[4];
    private readonly Texture2D[] _battlePortrait = new Texture2D[4];
    private readonly Texture2D[] _realm = new Texture2D[3];
    private static readonly Color[] LayerColors = { new("EDBC83"),new("C9AAEB"),new("B3B4ED"),new("9AD7E5") };
    private Texture2D _minaRoute = null!;
    private Texture2D _minaHall = null!;
    private Texture2D _sparkle = null!;
    private float[] _musicEnergy = null!;
    private FontFile _roman = null!;
    private MovieFrame _frame = null!;
    private double _time;
    private bool _stills, _check, _ending;
    private int _lastCut = -1;
    private int Cut => Array.FindLastIndex(Edit, t => t <= Math.Min(_time, MovieDuration - .001));
    private static float Ease(float t) { t = Mathf.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    private static T Field<T>(object owner, string name) =>
        (T)typeof(OpeningFilm).GetField(name, PrivateReflection)!.GetValue(owner)!;

    public override async void _Ready()
    {
        if (!OS.GetUserDataDir().Replace('\\', '/').Contains("/build/qa_story/"))
            throw new Exception("Opening capture requires an isolated save profile");
        base._Ready();
        Field<Node2D>(this, "_overlay").Visible = false;
        Field<Sprite2D>(this, "_mina").Visible = false;
        Audio.Instance?.StopMusic(0);
        Audio.Instance?.StopVoice();
        foreach (var child in GetNode<Audio>("/root/Audio").GetChildren())
            if (child is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
        string[] names = { "akari_unsent", "koharu_late", "rei_before_onair",
            "akari_realm", "koharu_realm", "rei_realm" };
        for (int i = 0; i < names.Length; i++) _art[i] = GD.Load<Texture2D>(Folder + names[i] + ".png");
        for (int i = 0; i < 3; i++)
        {
            _rooms[i] = GD.Load<Texture2D>($"res://char/bg2/opening/op_{new[] { "akari", "koharu", "rei" }[i]}_v1.png");
            _insert[i] = GD.Load<Texture2D>($"res://char/bg2/opening/layers_v3/{new[] { "akari_phone", "koharu_study", "rei_stream" }[i]}_insert.png");
        }
        for (int i = 0; i < 4; i++)
        {
            string id = new[] { "akari", "koharu", "rei", "mina" }[i];
            _portrait[i] = GD.Load<Texture2D>($"res://char/bg2/opening/layers_v3/{id}_{(i == 3 ? "profile" : "daily")}_layer.png");
            _battlePortrait[i] = GD.Load<Texture2D>($"res://char/bg2/opening/op_{id}_cutin_v1.png");
            if (i < 3) _realm[i] = GD.Load<Texture2D>($"res://char/bg2/route/{id}_far.png");
        }
        _minaRoute = GD.Load<Texture2D>("res://char/bg2/route/blender/mina_far.png");
        _minaHall = GD.Load<Texture2D>("res://char/bg2/boss/mina_far_v2.png");
        _sparkle = GD.Load<Texture2D>("res://char/v3/fx/rei/star_small.png");
        _musicEnergy = JsonSerializer.Deserialize<float[]>(Godot.FileAccess.GetFileAsString(Folder + "music_envelope.json"))!;
        _roman = GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-Italic.ttf");
        _frame = new MovieFrame { Film = this, ZIndex = 10 };
        AddChild(_frame);
        bool motionStills = Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--motion-stills");
        _stills = motionStills || Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--stills");
        _check = Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--check");
        for (int i = 1; i < Edit.Length; i++)
            if (Edit[i] <= Edit[i - 1]) throw new Exception("Opening edit is not ordered");
        Refresh();
        GD.Print($"[OpeningLayers] Ready: {MovieDuration:F1}s, {Edit.Length - 1} shots, 4 portrait layers, 3 scene inserts, 11 dialogue captions, game Mina backgrounds, separated name area, no lyrics");
        if (!_stills) return;
        string folder = ProjectSettings.GlobalizePath(Output + "/stills");
        DirAccess.MakeDirRecursiveAbsolute(folder);
        for (int i = 0; i < Edit.Length - 1; i++)
        {
            if (motionStills && i is not (4 or 5 or 7 or 8 or 9 or 15 or 16 or 17 or 18 or 22 or 26)) continue;
            foreach (float phase in motionStills ? new[] { .12f, .48f, .82f } : new[] { .55f })
            {
                _time = Edit[i] + (Edit[i + 1] - Edit[i]) * phase;
                Refresh();
                for (int step = 0; step < 3; step++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var screenshot = GetViewport().GetTexture().GetImage();
                screenshot.SavePng(motionStills ? $"{folder}/{i:D2}_{phase * 100:00}.png" : $"{folder}/{i:D2}.png");
            }
        }
        GD.Print("[OpeningAnime] Stills complete");
        Finish();
    }

    public override void _Process(double delta)
    {
        if (_stills || _ending || _frame == null) return;
        _time += _check ? .5 : delta;
        Refresh();
        if (_lastCut != Cut)
        {
            _lastCut = Cut;
            GD.Print($"[OpeningAnime] Shot {Cut:D2} at {_time:F3}s");
        }
        if (_time >= MovieDuration + .1) Finish();
    }

    private void Refresh()
    {
        SourceTime.SetValue(this, _time);
        QueueRedraw();
        _frame.QueueRedraw();
    }

    private async void Finish()
    {
        _ending = true;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print("[OpeningAnime] PASS movie complete");
        GetTree().Quit();
    }

    public override void _Draw()
    {
        if (_frame == null) return;
        DrawRect(Frame, Colors.Black);
        float local = (float)(_time - Edit[Cut]);
        float transition = Cut is 8 or 9 or 10 or 11 or 26 or 27 ? .45f : .12f;
        if (Cut > 0 && local < transition)
            DrawScene(Cut - 1, (float)(Edit[Cut] - Edit[Cut - 1]), 1);
        DrawScene(Cut, local, Cut == 0 ? 1 : Ease(local / transition));
    }

    private void DrawScene(int shot, float local, float alpha)
    {
        float p = Mathf.Clamp(local / (float)(Edit[shot + 1] - Edit[shot]), 0, 1);
        switch (shot)
        {
            case 0: Phone(local * .78f, alpha); break;
            case 1: case 2: case 3: DailyLayers(shot - 1, local, p, alpha, true); break;
            case 4: DailyLayers(0,local,p,alpha); break;
            case 5: DailyLayers(2,local,p,alpha); break;
            case 6: Phone(3.85f+p*1.95f,alpha); PhoneMemories(p,alpha*.6f); break;
            case 7: DailyLayers(1,local,p,alpha); break;
            case 8: MinaLayers(0,local,p,alpha); break;
            case 9: MinaLayers(1,local,p,alpha); break;
            case 10: MinaLayers(2,local,p,alpha); break;
            case 11: MinaLayers(3,local,p,alpha); break;
            case 12: case 13: case 14: FlashPortrait(shot-12,p,alpha); break;
            case 15: case 16: case 17: case 18: BattleLayers(shot-15,local,p,alpha); break;
            case 19: case 20: case 21:
                Photo(_insert[shot-19],1.03f,1.15f,new(.5f,.5f),p,alpha);
                GraphicRules(shot-19,local,alpha);
                break;
            case 22: case 23: case 24: Triptych(shot-22,(float)(_time-54.8),alpha); break;
            case 25: Phone(6.3f+p*3.2f,alpha); PhoneMemories(p,alpha*.8f); break;
            case 26: MinaLayers(3,local,p,alpha); break;
            default:
                Photo(_minaRoute,1.04f,1.08f,new(.5f,.5f),p,alpha);
                Portrait(_portrait[3],110-p*40,20,920,new Color(.55f,.78f,.9f,alpha*.13f));
                GuidingLight(local+8,alpha*.4f);
                break;
        }
    }

    private void Portrait(Texture2D art,float center,float top,float height,Color tint)
    {
        Vector2 size=art.GetSize()*(height/art.GetHeight());
        DrawTextureRect(art,new Rect2(new Vector2(center-size.X/2,top),size),false,tint);
    }

    private void IllustratedPanel(Texture2D art,Vector2[] mask,Vector2 focus,float zoom,Color tint,bool border=true)
    {
        Vector2 min=mask[0],max=mask[0];
        foreach(var point in mask) { min=min.Min(point); max=max.Max(point); }
        Vector2 bounds=max-min;
        float scale=Mathf.Max(bounds.X/art.GetWidth(),bounds.Y/art.GetHeight())*zoom;
        Vector2 size=art.GetSize()*scale;
        Vector2 origin=(min+max)/2-focus*size;
        origin.X=Mathf.Clamp(origin.X,max.X-size.X,min.X);
        origin.Y=Mathf.Clamp(origin.Y,max.Y-size.Y,min.Y);
        var uv=Array.ConvertAll(mask,point=>(point-origin)/size);
        DrawPolygon(mask,new[]{tint},uv,art);
        if(border)
        {
            var outline=new Vector2[mask.Length+1];
            Array.Copy(mask,outline,mask.Length);
            outline[^1]=mask[0];
            DrawPolyline(outline,new Color(.93f,.96f,1,tint.A*.68f),2,true);
        }
    }

    private static Vector2[] Slant(float left,float top,float width,float height,float slant=65) =>
        new[]{new Vector2(left+slant,top),new(left+width,top),new(left+width-slant,top+height),new(left,top+height)};

    private void PhotoTile(Texture2D art,Vector2 center,Vector2 size,float angle,float alpha,Vector2 focus)
    {
        DrawSetTransform(center,angle);
        DrawRect(new Rect2(-size/2-new Vector2(5,5),size+new Vector2(10,10)),new Color(.91f,.95f,.98f,alpha*.86f));
        IllustratedPanel(art,Slant(-size.X/2,-size.Y/2,size.X,size.Y,0),focus,1.03f,new Color(1,1,1,alpha),false);
        DrawSetTransform(Vector2.Zero);
    }

    private void GraphicRules(int id,float time,float alpha,bool reverse=false)
    {
        Color accent=LayerColors[id];
        for(int i=0;i<4;i++)
        {
            float x=Mathf.PosMod(time*(32+i*17)+i*370,1580)-150;
            if(reverse) x=1280-x;
            DrawLine(new(x,-20),new(x-190,740),new Color(accent,alpha*(i%2==0?.09f:.17f)),i%2==0?30:2,true);
        }
        DrawLine(new(0,35),new(1280,35),new Color(accent,alpha*.38f),1,true);
        DrawLine(new(0,688),new(1280,688),new Color(accent,alpha*.38f),1,true);
    }

    private void CharacterName(int id,Vector2 at,float local,float alpha,bool compact=false)
    {
        float enter=Ease((local-.15f)/.5f);
        string name=new[]{"Akari","Koharu","Rei","Mina"}[id];
        string japanese=new[]{"あかり","こはる","レイ","ミナ"}[id];
        Color accent=LayerColors[id];
        at.X+=(1-enter)*-48;
        float a=alpha*enter;
        int size=compact?70:108;
        UiKit.Text(this,_roman,at+new Vector2(2,3),name,size,new Color(.015f,.025f,.05f,a*.7f));
        UiKit.Text(this,_roman,at,name,size,new Color(.96f,.97f,1,a));
        float width=UiKit.TextW(_roman,name,size);
        DrawLine(at+new Vector2(2,size+6),at+new Vector2(2+width*enter,size+6),new Color(accent,a*.85f),2,true);
        UiKit.Text(this,UiKit.Zen,at+new Vector2(4,size+15),japanese,compact?18:24,new Color(accent,a));
    }

    private void DailyLayers(int id,float time,float progress,float alpha,bool brief=false)
    {
        bool left=id!=0;
        Photo(_rooms[id],1.02f,1.1f,new(.5f,.5f),progress,alpha);
        DrawRect(Frame,new Color(.018f,.025f,.055f,alpha*.57f));
        GraphicRules(id,time,alpha,left);
        float enter=Ease(time/.45f);
        float slide=(1-enter)*170;
        Vector2[] panel=left
            ? Slant(590+slide,135,735,440,-75)
            : Slant(-55-slide,80,795,485,85);
        IllustratedPanel(_insert[id],panel,new(.5f,.5f),1.02f+progress*.04f,new Color(1,1,1,alpha));
        if(!brief)
        {
            if(id==0) DraftWindow(time,alpha*.85f);
            else if(id==1) StreamWindow(time,alpha*.85f);
            else VoiceWindow(time,alpha*.85f);
        }
        float center=left?295-slide*.75f+progress*16:935+slide*.75f-progress*16;
        Portrait(_portrait[id],center,26+(1-enter)*32,880,new Color(1,1,1,alpha*enter));
        CharacterName(id,left?new Vector2(768,18):new Vector2(405,555),time,alpha,true);
        Motes(id,time,alpha*.5f);
    }

    private void FlashPortrait(int id,float progress,float alpha)
    {
        Color accent=LayerColors[id];
        DrawRect(Frame,new Color(accent,alpha*.24f));
        Photo(_art[3+id],1.03f,1.13f,new(.5f,.5f),progress,alpha);
        GraphicRules(id,progress*3,alpha);
    }

    private void BattleLayers(int id,float time,float progress,float alpha)
    {
        bool left=id==1;
        Color accent=LayerColors[id];
        Photo(id<3?_realm[id]:_minaRoute,1.05f,1.16f,new(.5f,.5f),progress,alpha);
        DrawRect(Frame,new Color(.02f,.025f,.055f,alpha*.35f));
        float enter=Ease(time/.42f);
        float offset=(1-enter)*240;
        var backgroundBand=left?Slant(615+offset,55,760,445,-95):Slant(-70-offset,50,800,450,95);
        IllustratedPanel(id<3?_insert[id]:_minaHall,backgroundBand,new(.5f,.5f),
            1.02f+progress*.06f,new Color(.92f,.94f,1,alpha*.88f));
        UiKit.HGradient(this,new Rect2(left?730:0,500,550,205),
            new Color(.015f,.025f,.045f,alpha*.6f),new Color(.015f,.025f,.045f,0));
        GraphicRules(id,time+3,alpha,left);
        if(id==0) Letters(time,alpha*.7f);
        else if(id==1) PenlightTrails(time,alpha*.65f);
        else if(id==2) Constellation(time,alpha*.65f);
        else GuidingLight(time,alpha*.65f);
        float center=left?340-offset*.5f+progress*25:925+offset*.5f-progress*25;
        Portrait(_battlePortrait[id],center,28,895+progress*15,new Color(1,1,1,alpha*enter));
        CharacterName(id,left?new(795,515):new(75,515),time,alpha);
        if(time<.6f)
        {
            float streak=Ease(time/.6f);
            DrawLine(new(1280*streak-120,0),new(1280*streak-310,720),
                new Color(accent,alpha*Mathf.Sin(streak*Mathf.Pi)*.24f),20,true);
        }
    }

    private void MinaLayers(int mode,float time,float progress,float alpha)
    {
        Photo(mode==2?_minaHall:_minaRoute,1.04f,1.1f,new(.5f,.5f),progress,alpha);
        DrawRect(Frame,new Color(.015f,.025f,.055f,alpha*.32f));
        GraphicRules(3,time,alpha);
        GuidingLight(time+mode,alpha*.65f);
        float enter=Ease(time/.55f);
        if(mode==1)
        {
            for(int i=0;i<3;i++)
            {
                float appear=Ease((time-i*.15f)/.5f);
                Vector2 at=new(235+i*405+(1-appear)*(i%2==0?-180:180),
                    345+(i%2==0?-1:1)*(65+progress*20));
                PhotoTile(_insert[i],at,new(420,280),(i-1)*.09f+Mathf.Sin(time*.5f+i)*.018f,
                    alpha*appear,new(.5f,.5f));
            }
            return;
        }
        if(mode==2)
        {
            for(int i=0;i<3;i++)
                IllustratedPanel(_realm[i],Slant(620+i*185-progress*15,85+i*40,245,430,48),new(.5f,.5f),1.02f,
                    new Color(.82f,.9f,1,alpha*.75f));
            Portrait(_portrait[3],305-(1-enter)*170+progress*22,10,930,new Color(1,1,1,alpha*enter));
            CharacterName(3,new(805,540),time,alpha,true);
            return;
        }
        else if(mode==3)
        {
            PhotoTile(_insert[0],new(245-progress*35,245),new(520,290),-.055f,alpha*.87f,new(.5f,.5f));
            PhotoTile(_rooms[2],new(440+progress*25,470),new(430,240),.06f,alpha*.88f,new(.5f,.5f));
        }
        else
        {
            for(int i=0;i<3;i++)
            {
                float show=Ease((time-i*.19f)/.5f);
                Vector2 at=new(295+(i%2==0?24:-15)+(1-show)*-200,
                    155+i*194+(progress-.5f)*(i%2==0?22:-22));
                PhotoTile(_art[i],at,new(465,170),(i-1)*.045f,alpha*show,new(.61f,.43f));
            }
        }
        Portrait(_portrait[3],960+(1-enter)*170-progress*22,26,900,new Color(1,1,1,alpha*enter));
        CharacterName(3,new(570,32),time,alpha,true);
    }

    private void Triptych(int active,float time,float alpha)
    {
        DrawRect(Frame,new Color(.035f,.045f,.08f,alpha));
        for(int i=0;i<3;i++)
        {
            float enter=Ease((time-i*.13f)/.38f);
            float x=i*438-35+(1-enter)*-380;
            var shape=Slant(x,0,490,720,60);
            float intensity=i==active?1:.5f;
            IllustratedPanel(_realm[i],shape,new(.5f,.45f),1,
                new Color(.55f,.62f,.74f,alpha*.75f),false);
            IllustratedPanel(_battlePortrait[i],shape,new(.5f,.32f),1.08f,
                new Color(intensity,intensity,intensity,alpha*enter),false);
            DrawLine(new(x+490,0),new(x+430,720),new Color(LayerColors[i],alpha*.9f),3,true);
            CharacterName(i,new(x+65,545),time-i*.13f,alpha*intensity,true);
        }
    }

    private void PhoneMemories(float progress,float alpha)
    {
        float appear=Ease(progress/.3f);
        var left=Slant(-90,180,360,430,45);
        var right=Slant(1040,130,360,430,45);
        IllustratedPanel(_insert[1],left,new(.6f,.58f),1.03f,new Color(.7f,.8f,.9f,alpha*appear*.6f));
        IllustratedPanel(_insert[2],right,new(.55f,.4f),1.03f,new Color(.7f,.8f,.9f,alpha*appear*.6f));
    }

    private void Phone(float time, float alpha) =>
        SourceShot.Invoke(this, new object[] { 0, time, alpha });

    private void Photo(Texture2D art, float from, float to, Vector2 focus, float progress, float alpha)
    {
        float scale = Mathf.Max(1280f / art.GetWidth(), 720f / art.GetHeight()) * Mathf.Lerp(from, to, Ease(progress));
        Vector2 size = art.GetSize() * scale;
        Vector2 origin = new Vector2(640, 360) - focus * size;
        origin.X = Mathf.Clamp(origin.X, 1280 - size.X, 0);
        origin.Y = Mathf.Clamp(origin.Y, 720 - size.Y, 0);
        DrawTextureRect(art, new Rect2(origin, size), false, new Color(1, 1, 1, alpha));
    }

    private void Motes(int palette, float time, float alpha)
    {
        Color tint = palette switch { 0 => new("FFE2B6"), 1 => new("E6C9FF"), 2 => new("CDBDFF"), _ => new("C8EEFF") };
        for (int i = 0; i < 23; i++)
        {
            float depth = .25f + (i % 5) * .18f;
            float x = Mathf.PosMod(i * 157.7f - time * (15 + depth * 40), 1440) - 80;
            float y = Mathf.PosMod(i * 83.1f + Mathf.Sin(time * .5f + i * 2.1f) * 23, 820) - 50;
            float opacity = (.18f + .16f * Mathf.Sin(time * 1.2f + i)) * alpha;
            float radius = .7f + depth * 1.4f;
            DrawCircle(new(x,y), radius * 4, new Color(tint, opacity * .09f));
            DrawCircle(new(x,y), radius, new Color(tint, opacity));
        }
    }

    private float Energy(float time)
    {
        float frame = Mathf.Clamp(time * 30, 0, _musicEnergy.Length - 1);
        int index = (int)frame;
        return Mathf.Lerp(_musicEnergy[index], _musicEnergy[Math.Min(index + 1, _musicEnergy.Length - 1)], frame - index);
    }

    private void Panel(Vector2 size, Color accent, float alpha)
    {
        var rect = new Rect2(-size * .5f, size);
        UiKit.Box(this, new Rect2(rect.Position + new Vector2(0, 8), size), new Color(.005f,.012f,.025f,alpha * .2f), 18);
        UiKit.Box(this, rect, new Color(.025f,.045f,.075f,alpha * .87f), 18,
            new Color(accent,alpha * .44f), 1);
        DrawLine(rect.Position + new Vector2(20,43), rect.Position + new Vector2(size.X - 20,43),
            new Color(accent,alpha * .16f), 1, true);
    }

    private void Halo(Vector2 at, float radius, Color color, float alpha)
    {
        for (int ring = 6; ring >= 1; ring--)
            DrawCircle(at, radius * ring / 6, new Color(color,alpha * .045f * (7 - ring)));
    }

    private void DraftWindow(float time, float alpha)
    {
        float arrive = Ease(time / .48f);
        Color accent = new("CBDDF7");
        DrawSetTransform(new Vector2(205 - (1 - arrive) * 120, 615), -.035f,
            Vector2.One * (.88f + arrive * .12f) * .7f);
        float a = alpha * arrive;
        Panel(new(350,190),accent,a);
        UiKit.Text(this,UiKit.Zen,new(-151,-79),"下書き",18,new Color(accent,a * .9f));
        UiKit.Text(this,UiKit.Zen,new(97,-77),"未送信",13,new Color(accent,a * .5f));
        const string message = "おめでとう。";
        int count = time < 2.05f
            ? Mathf.Clamp((int)((time - .48f) / .2f),0,message.Length)
            : Mathf.Clamp(message.Length - (int)((time - 2.05f) / .13f),0,message.Length);
        string typed = message[..count];
        UiKit.Text(this,UiKit.Zen,new(-145,-20),typed,26,new Color(.94f,.97f,1,a));
        float cursor = -145 + UiKit.TextW(UiKit.Zen,typed,26) + 3;
        if (Mathf.PosMod(time,.8f) < .5f)
            DrawLine(new(cursor,-15),new(cursor,13),new Color(accent,a),1.8f,true);
        UiKit.Box(this,new Rect2(-150,50,300,25),new Color(.3f,.45f,.65f,a * .1f),8);
        for (int dot=0; dot<3; dot++)
            DrawCircle(new(-130+dot*9,62 - Mathf.Max(0,Mathf.Sin(time*5-dot)) * 3),2.5f,
                new Color(accent,a * .7f));
        float erasing=Ease((time-2.05f)/.15f)*(1-Ease((time-2.85f)/.2f));
        DrawLine(new(120,56),new(134,69),new Color(accent,a * (.3f+erasing*.5f)),1.4f,true);
        DrawLine(new(134,56),new(120,69),new Color(accent,a * (.3f+erasing*.5f)),1.4f,true);
        DrawSetTransform(Vector2.Zero);
    }

    private void VoiceWindow(float time, float alpha)
    {
        float arrive=Ease(time/.5f);
        Color accent=new("B8E2F6");
        float a=alpha*arrive;
        DrawSetTransform(new Vector2(1030+(1-arrive)*100,570),-.025f,Vector2.One*(.93f+.07f*arrive));
        Panel(new(350,190),accent,a);
        UiKit.Text(this,UiKit.Zen,new(-150,-78),"VOICE",17,new Color(accent,a*.85f));
        DrawCircle(new(144,-67),4,new Color(.85f,.7f,.85f,a));
        for (int i=0;i<42;i++)
        {
            float energy=Energy((float)_time-(41-i)*.019f);
            float height=(6+energy*60)*(1-Ease((time-2.55f)/.4f));
            height=Mathf.Max(3,height);
            DrawLine(new(-145+i*7,12-height/2),new(-145+i*7,12+height/2),new Color(accent,a*(.45f+.5f*energy)),3,true);
        }
        float scan=Mathf.PosMod(time*.36f,1);
        DrawLine(new(-148+scan*296,-35),new(-148+scan*296,54),new Color(.92f,.98f,1,a*.45f),1,true);
        UiKit.Box(this,new Rect2(-148,71,296,2),new Color(accent,a*.18f),1);
        UiKit.Box(this,new Rect2(-148,71,296*Ease(time/2.7f),2),new Color(accent,a*.7f),1);
        DrawSetTransform(Vector2.Zero);
    }

    private void StreamWindow(float time, float alpha)
    {
        float arrive=Ease(time/.45f);
        float a=alpha*arrive;
        Color accent=new("DBBFFF");
        DrawSetTransform(new Vector2(1030+(1-arrive)*160,555),-.025f,Vector2.One*(.92f+.08f*arrive));
        Panel(new(350,250),accent,a);
        DrawCircle(new(-145,-98),4,new Color(.95f,.54f,.74f,a));
        UiKit.Text(this,UiKit.Zen,new(-130,-110),"LIVE",17,new Color(accent,a));
        for(int row=0;row<5;row++)
        {
            float y=-58+Mathf.PosMod(row*36-time*53,180);
            float edge=Ease((y+58)/18)*(1-Ease((y-82)/20));
            float rowAlpha=a*edge;
            DrawCircle(new(-142,y),8,new Color(accent,rowAlpha*.5f));
            UiKit.Box(this,new Rect2(-120,y-4,95+(row%3)*36,5),new Color(.89f,.85f,.97f,rowAlpha*.6f),2);
            UiKit.Box(this,new Rect2(-120,y+6,64+(row%2)*33,3),new Color(accent,rowAlpha*.22f),1);
        }
        for(int i=0;i<4;i++)
        {
            float p=Mathf.PosMod(time*.42f+i*.27f,1);
            float x=138+Mathf.Sin(p*6+i)*8;
            float y=95-p*145;
            float fade=Mathf.Sin(p*Mathf.Pi)*a;
            Heart(new(x,y),5+p*3,new Color(.91f,.67f,.93f,fade*.7f));
        }
        DrawSetTransform(Vector2.Zero);
    }

    private void Heart(Vector2 at,float size,Color tint)
    {
        var points=new Vector2[24];
        for(int i=0;i<points.Length;i++)
        {
            float a=i*Mathf.Tau/points.Length;
            points[i]=at+new Vector2(Mathf.Pow(Mathf.Sin(a),3),
                -(13*Mathf.Cos(a)-5*Mathf.Cos(2*a)-2*Mathf.Cos(3*a)-Mathf.Cos(4*a))/16)*size;
        }
        DrawColoredPolygon(points,tint);
    }

    private void Letters(float time,float alpha)
    {
        Color warm=new("F8E4CB");
        for(int i=0;i<13;i++)
        {
            float p=Mathf.PosMod(time*.29f+i*.083f,1);
            float near=p*p;
            float x=870+Mathf.Sin(i*2.37f)*180+near*(i%2==0?410:-160);
            float y=275+(i%3-1)*65+near*(i%2==0?490:-420);
            float a=alpha*Ease(p/.13f)*(1-Ease((p-.88f)/.12f));
            float width=35+near*170;
            float angle=-.2f+Mathf.Sin(time*1.1f+i)*.35f;
            Envelope(new(x,y),width,angle,Mathf.Sin(p*Mathf.Pi),warm,a*.86f);
        }
        for(int i=0;i<2;i++)
        {
            float p=Mathf.PosMod(time*.42f+i*.5f,1);
            Vector2 at=new(760+p*500,590-p*140);
            Halo(at,18,warm,alpha*.3f*Mathf.Sin(p*Mathf.Pi));
        }
    }

    private void Envelope(Vector2 at,float width,float angle,float open,Color tint,float alpha)
    {
        DrawSetTransform(at,angle);
        float h=width*.59f;
        Vector2[] corners={new(-width/2,-h/2),new(width/2,-h/2),new(width/2,h/2),new(-width/2,h/2)};
        DrawColoredPolygon(corners,new Color(tint,alpha));
        DrawPolyline(new[]{corners[0],new Vector2(0,h*.16f),corners[1]},new Color(.54f,.46f,.46f,alpha*.5f),Mathf.Max(1,width*.009f),true);
        DrawPolyline(new[]{corners[3],new Vector2(0,-h*.08f),corners[2]},new Color(.64f,.54f,.5f,alpha*.3f),Mathf.Max(1,width*.008f),true);
        Vector2 peak=new(0,Mathf.Lerp(h*.15f,-h*.9f,open*.65f));
        DrawColoredPolygon(new[]{corners[0],corners[1],peak},new Color(1,.95f,.87f,alpha*.85f));
        DrawSetTransform(Vector2.Zero);
    }

    private void PenlightTrails(float time,float alpha)
    {
        Vector2 source=new(290,85);
        Color violet=new("D8B4FC");
        float music=Energy((float)_time);
        Halo(source,36+music*24,violet,alpha*.6f);
        for(int ribbon=0;ribbon<3;ribbon++)
        {
            float head=Mathf.PosMod(time*.44f+ribbon*.32f,1.35f);
            var points=new Vector2[35];
            for(int j=0;j<points.Length;j++)
            {
                float p=Mathf.Clamp(head-.32f+j*.32f/(points.Length-1),0,1);
                points[j]=source.BezierInterpolate(new(-100,170+ribbon*75),
                    new(130,670+ribbon*45),new(970+ribbon*160,680-ribbon*20),p);
            }
            float a=alpha*Ease(head/.18f)*(1-Ease((head-1)/.35f));
            DrawPolyline(points,new Color(violet,a*.07f),16,true);
            DrawPolyline(points,new Color(violet,a*.23f),5,true);
            DrawPolyline(points,new Color(.94f,.85f,1,a*.8f),1.5f,true);
            var lead=points[^1];
            Halo(lead,16,violet,a*.7f);
            DrawCircle(lead,3,new Color(1,.92f,1,a));
        }
        for(int i=0;i<9;i++)
        {
            float p=Mathf.PosMod(time*.34f+i*.121f,1);
            var at=new Vector2(80+i*48+Mathf.Sin(time+i)*35,760-p*600);
            float a=alpha*Mathf.Sin(p*Mathf.Pi)*.6f;
            Heart(at,3+p*6,new Color(violet,a));
        }
    }

    private void Constellation(float time,float alpha)
    {
        Color violet=new("D8CBFF");
        var points=new Vector2[7];
        for(int i=0;i<points.Length;i++)
        {
            float angle=i*.87f+time*.48f;
            points[i]=new Vector2(948+Mathf.Cos(angle)*(190+i%2*65),
                360+Mathf.Sin(angle)*(155+i%3*25));
        }
        for(int i=0;i<points.Length;i++)
        {
            float show=Ease((time-i*.14f)/.35f);
            Vector2 end=points[i].Lerp(points[(i+2)%points.Length],show);
            DrawLine(points[i],end,new Color(violet,alpha*show*.3f),1,true);
            float size=(18+i%3*7)*( .85f+Energy((float)_time-i*.03f)*.3f);
            var rect=new Rect2(points[i]-Vector2.One*size/2,Vector2.One*size);
            DrawTextureRect(_sparkle,rect,false,new Color(.85f,.8f,1,alpha*show*.9f));
            Halo(points[i],size*.7f,violet,alpha*show*.35f);
        }
        for(int i=0;i<3;i++)
        {
            float p=Mathf.PosMod(time*.35f+i*.34f,1);
            Vector2 from=new(1240,80+i*150),to=new(710,300+i*95);
            Vector2 head=from.Lerp(to,p);
            float a=alpha*Mathf.Sin(p*Mathf.Pi);
            DrawLine(head-(to-from).Normalized()*85,head,new Color(violet,a*.5f),2,true);
            DrawCircle(head,2,new Color(.95f,.9f,1,a));
        }
    }

    private void GuidingLight(float time,float alpha)
    {
        Color cyan=new("BCEDFC");
        for(int thread=0;thread<3;thread++)
        {
            float head=Mathf.PosMod(time*.24f+thread*.33f,1.3f);
            var points=new Vector2[41];
            for(int i=0;i<points.Length;i++)
            {
                float p=Mathf.Clamp(head-.32f+i*.32f/(points.Length-1),0,1);
                points[i]=new Vector2(-90,540+thread*30).BezierInterpolate(new(200,230-thread*65),
                    new(540,680),new(780,410-thread*20),p);
            }
            float a=alpha*Ease(head/.16f)*(1-Ease((head-1)/.3f));
            DrawPolyline(points,new Color(cyan,a*.07f),13,true);
            DrawPolyline(points,new Color(cyan,a*.25f),4,true);
            DrawPolyline(points,new Color(.86f,.96f,1,a*.8f),1.4f,true);
            Halo(points[^1],20,cyan,a*.7f);
            DrawCircle(points[^1],3,new Color(.94f,.99f,1,a));
        }
    }

    private void DrawFrame(Node2D canvas)
    {
        string dialogue = Cut switch
        {
            4 => "……それしか、送れなかった。",
            5 => "今日はちょっと、疲れてて……。",
            7 => "明日も、学校なのに。……配信、最後まで見なきゃ。",
            8 => "……投稿できなかった言葉が、聞こえます。",
            10 => "消された言葉は、消えていない。",
            11 => "ご主人様、道を探してください。",
            15 => "まだ、伝えてないことがある。",
            16 => "好きなはずなのに、休むのが、怖い。",
            17 => "笑っていないと、誰も聞いてくれない？",
            18 => "ご主人様が開いた道を、わたくしが進みます。",
            26 => "あの人が言えなかったことを、一緒に聞きに行きましょう。",
            _ => ""
        };
        if (dialogue.Length > 0)
        {
            float a = Ease((float)(_time - Edit[Cut]) / .3f) * Ease((float)(Edit[Cut + 1] - _time) / .25f);
            string quote = $"「{dialogue}」";
            float x = (1280 - UiKit.TextW(UiKit.Zen, quote, 29)) / 2;
            canvas.DrawRect(new Rect2(0, 665, 1280, 55), new Color(.01f, .018f, .04f, a * .8f));
            UiKit.Text(canvas, UiKit.Zen, new(x + 1, 677), quote, 29, new Color(0, 0, 0, a));
            UiKit.Text(canvas, UiKit.Zen, new(x, 676), quote, 29, new Color(.96f, .98f, 1, a));
        }
        if (Cut == Edit.Length - 2)
        {
            float a = Ease((float)(_time - 66.16) / 1.1f);
            canvas.DrawRect(Frame, new Color(.008f,.018f,.03f,a * .45f));
            float reveal = Ease((float)(_time - 66.4) / 1.2f);
            string title = "Refrain"[..Mathf.Clamp((int)(reveal * 7.99f),0,7)];
            float titleLeft = (1280 - UiKit.TextW(_roman,"Refrain",170)) / 2;
            UiKit.Text(canvas, _roman, new Vector2(titleLeft, 230), title, 170,
                new Color(.94f,.96f,.98f,a));
            if (reveal < 1)
            {
                float caret = titleLeft + UiKit.TextW(_roman,title,170) + 8;
                canvas.DrawLine(new(caret,265),new(caret,390),new Color(.65f,.86f,.92f,a*.7f),2,true);
            }
            canvas.DrawLine(new Vector2(498,432), new Vector2(498+284*reveal,432), new Color(.46f,.76f,.85f,a * .7f), 1);
        }
        float fade = Mathf.Max(1 - Ease((float)_time / .7f), Ease((float)(_time - 71.1) / 1.4f));
        canvas.DrawRect(Frame, new Color(0,0,0,fade));
    }

    private partial class MovieFrame : Node2D
    {
        public OpeningMusicVideo Film = null!;
        public override void _Draw() => Film.DrawFrame(this);
    }
}


