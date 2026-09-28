using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class PlayerScaleQa : Node
{
    private const BindingFlags IPriv = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object o, string n) => (T)o.GetType().GetField(n, IPriv)!.GetValue(o)!;
    private static void Write(object o, string n, object v) => o.GetType().GetField(n, IPriv)!.SetValue(o, v);
    private static object? Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, IPriv)!.Invoke(o, a);

    private const float DisplayH = 36f;
    private const float Tol = 0.05f;
    private const byte InkCut = 12;       // 画素で測るときの α しきい（UiKit.ContentAlphaCut=0.05 相当）

    // 全ポーズの素材名（Cosmetics.PosePath に渡す綴り）。
    private static readonly string[] Poses =
        { "idle", "aim_u", "aim_ur", "aim_r", "aim_dr", "aim_d", "spin_00", "spin_01", "spin_02", "spin_03", "spin_04" };
    // 照準は 5枚の素材を左右反転で八方位へ割り当てる（Player.AimSpriteFor）。狙う向きと出るべき素材の対。
    private static readonly (Vector2 Dir, string Pose)[] AimDirs =
    {
        (Vector2.Up, "u"), (new Vector2(1, -1), "ur"), (Vector2.Right, "r"), (new Vector2(1, 1), "dr"),
        (Vector2.Down, "d"), (Vector2.Left, "r"), (new Vector2(-1, -1), "ur"), (new Vector2(-1, 1), "dr"),
    };
    // 回避8ステップが引く素材の番号と反転（Player.SpinFrameIdx / SpinFrameFlip と同じ表）。
    private static readonly int[] SpinIdx = { 0, 1, 2, 3, 4, 3, 2, 1 };

    // 1ポーズぶんの実測。Now＝修正後／Old＝修正前（36/テクスチャ高・Offset なし）の中身の外接（ワールド座標）。
    private readonly record struct Sample(string Tag, Texture2D Tex, float Scale, Vector2 Offset, bool Flip,
        Vector2 At, Rect2 Now, Rect2 Old);

    private int _fail;
    private bool _shot;
    private string _out = "build/shots_player_scale";
    // 撮影用に取り置いたポーズ（キー＝"<キャラ>_<default|costume>"）。
    private readonly Dictionary<string, List<Sample>> _kept = new();

    public override async void _Ready()
    {
        // 全体の --shot（ShotTool 自動ロード＝撮って終了する）と衝突しないよう専用の旗にしてある。
        var args = OS.GetCmdlineUserArgs();
        foreach (string a in args) if (a == "--pls-shot") _shot = true;
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--pls-out") _out = args[i + 1];
        if (_shot)
        {
            DirAccess.MakeDirRecursiveAbsolute(_out);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            await Run();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Frames(4);
            GD.Print(_fail == 0 ? "[PLS] DONE ok" : $"[PLS] DONE fail={_fail}");
            GetTree().Quit(_fail == 0 ? 0 : 1);
        }
        catch (Exception ex) { GD.PushError($"[PLS] FAIL {ex}"); GetTree().Quit(1); }
    }

    private const char Chr92 = (char)92;   // Windows のパス区切り

    private async Task Run()
    {
        Check("セーブが隔離されている（APPDATA=build/qa_story/...）",
            OS.GetUserDataDir().Replace(Chr92, '/').Contains("/build/qa_story/"));
        var game = GetNode<GameManager>("/root/Game");
        game.AutoSaveEnabled = false;
        game.ResetPersistent();
        game.Difficulty = GameManager.Diff.Normal;
        // 4キャラぶんの衣装を触るため全ステージをクリア済みにし（IsJobUnlocked のゲート）、追加衣装を買っておく。
        foreach (var stage in GameManager.Stages) Read<HashSet<string>>(game, "_cleared").Add(stage.Id);
        game.TrainingSetImpression(5000);
        // 回避は 2026-09-22 からショップの段 n_dodge。TryDodge を直に叩くため直書きで付ける
        //（AutoSaveEnabled=false なのでディスクへは漏れない）。
        game.TrainingSetUpgrade("n_dodge", true);
        foreach (var item in Cosmetics.All.Where(i => i.Kind == CosmeticKind.Costume && i.Price > 0))
            Check($"追加衣装を用意（{item.Id}）", game.TryPurchaseCosmetic(item.Id));

        MaterialInfo();

        var training = new TrainingRoot { Name = "TrainingRoot" };
        GetTree().Root.AddChild(training);
        GetTree().CurrentScene = training;
        await Frames(4);
        var dummy = Read<TrainingDummy>(training, "_dummy");

        foreach (var job in Jobs.All)
            foreach (bool paid in new[] { false, true })
                await CheckCombo(game, training, dummy, job, paid);

        if (_shot) await Strips();

        training.QueueFree();
        await Frames(4);
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        Audio.Instance?.StopMusic(0);
        foreach (var audio in GetNode<Audio>("/root/Audio").GetChildren().OfType<AudioStreamPlayer>())
        {
            audio.Stop();
            audio.Stream = null;
        }
        _kept.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(4);
    }

    private static CosmeticItem Costume(JobTuning job, bool paid) => paid
        ? Array.Find(Cosmetics.All, i => i.Kind == CosmeticKind.Costume && i.Character == job.Id && i.Price > 0)!
        : Cosmetics.DefaultCostume(job.Id);

    private void MaterialInfo()
    {
        foreach (var job in Jobs.All)
            foreach (bool paid in new[] { false, true })
            {
                var item = Costume(job, paid);
                float expectedHead = PlayerArt.Fit(GD.Load<Texture2D>(Cosmetics.DefaultCostume(job.Id).PosePath("idle"))).GameHeadHeight;
                foreach (string pose in Poses)
                {
                    var tex = GD.Load<Texture2D>(item.PosePath(pose));
                    var fit = PlayerArt.Fit(tex);
                    Check($"{item.Id}/{pose}: calibrated head inside image",
                        fit.HeadHeight > 0 && fit.Crown.X > 0 && fit.Crown.X < tex.GetWidth()
                        && fit.Crown.Y >= 0 && fit.Crown.Y + fit.HeadHeight < tex.GetHeight());
                    Check($"{item.Id}/{pose}: same head scale across costumes",
                        Mathf.IsEqualApprox(fit.GameHeadHeight, expectedHead));
                    foreach (bool flip in new[] { false, true })
                    {
                        CheckPreview(tex, new Rect2(134, 178, 342, 400), new Vector2(305, 378), 250, flip);
                        if (pose == "idle")
                            CheckPreview(tex, new Rect2(0, 0, 110, 100), new Vector2(55, 50), 86, flip);
                    }
                }
            }
    }

    private void CheckPreview(Texture2D tex, Rect2 frame, Vector2 center, float height, bool flip)
    {
        var rect = PlayerArt.TextureRect(tex, center, height, flip);
        var bounds = new Rect2(rect.Position, rect.Size.Abs());
        Check($"{tex.ResourcePath.GetFile()}: preview inside frame ({height}, flip={flip})",
            frame.Grow(1f).Encloses(bounds));
    }

    // ── 1キャラ1衣装ぶん：全ポーズを出して中身の外接を測る ──
    private async Task CheckCombo(GameManager game, TrainingRoot training, TrainingDummy dummy, JobTuning job, bool paid)
    {
        var item = Costume(job, paid);
        string label = Label(item);
        Check($"{label}：衣装を装備できる", game.EquipCosmetic(item.Id));
        game.SelectedJob = job.Id;
        Call(training, "RebuildPlayer");
        await Frames(3);
        var player = Read<Player>(training, "_player");
        var sprite = player.GetNode<Sprite2D>("Sprite");
        Write(player, "_invincible", false);
        Call(player, "SetSpriteVisible", true);
        // ロック候補は「盤面に映っている敵」だけ＝的を八方位に置いても盤面から出ない位置に立たせる（PlayerJobQa と同じ）。
        player.GlobalPosition = new Vector2(Field.CenterX, 108f);
        Write(player, "_locked", false);
        Write(player, "_facing", 1);
        await Frames(3);
        Check($"{label}：装備した衣装の通常ポーズで出る（{sprite.Texture.ResourcePath.GetFile()}）",
            sprite.Texture.ResourcePath == item.PosePath("idle"));

        var samples = new List<Sample>();
        // 通常。向き反転は 2026-09-06 から無効（Player.FacingFlipEnabled=false）だが、Offset の符号の扱いだけは確かめる。
        foreach (int facing in new[] { 1, -1 })
        {
            Write(player, "_facing", facing);
            await Frames(2);
            samples.Add(Take(player, sprite, facing > 0 ? "idle" : "idle・反転"));
        }
        Write(player, "_facing", 1);

        // 照準（5方向＋左半分の反転3方向）。絵の差し替えは _PhysicsProcess が握るのでフレームを送って待つ。
        Write(player, "_locked", true);
        Write(player, "_lockTarget", dummy);
        foreach (var (dir, pose) in AimDirs)
        {
            dummy.GlobalPosition = player.GlobalPosition + dir.Normalized() * 65f;
            await Frames(3);
            Check($"{label}：{dir} を狙うと aim_{pose}{(dir.X < 0 ? "・反転" : "")}",
                player.LockedOn && sprite.Texture.ResourcePath == item.PosePath("aim_" + pose) && sprite.FlipH == (dir.X < 0));
            samples.Add(Take(player, sprite, $"aim_{pose}{(dir.X < 0 ? "・反転" : "")}"));
        }

        // 回避8コマ（回り向き±・自機の向き±の全組み合わせ）。ApplySpinFrame が絵・反転・倍率・Offset を同時に入れ直す。
        //   1フレームの中で測り切る（間にフレームを送らない）＝タイマー駆動のコマ送りに上書きされない。
        Call(player, "TryDodge", Vector2.Zero);
        await Frames(2);
        Check($"{label}：その場回避が始まっている", Read<float>(player, "_dodgeTimer") > 0f);
        foreach (float sign in new[] { 1f, -1f })
            foreach (int facing in new[] { 1, -1 })
            {
                Write(player, "_dodgeSpinSign", sign);
                Write(player, "_facing", facing);
                for (int i = 0; i < 8; i++)
                {
                    Call(player, "ApplySpinFrame", Mathf.Tau * (i + 0.1f) / 8f);
                    int frame = sign > 0 ? i : (8 - i) % 8;
                    bool flip = (frame >= 5) ^ (facing < 0);
                    Check($"{label}：回避 {i} コマ（回り {sign:+0;-0} 向き {facing:+0;-0}）＝spin_{SpinIdx[frame]:00}"
                        + $"{(flip ? "・反転" : "")}",
                        sprite.Texture.ResourcePath == item.PosePath($"spin_{SpinIdx[frame]:00}") && sprite.FlipH == flip);
                    samples.Add(Take(player, sprite,
                        sign > 0 && facing > 0 ? $"spin{i}" : $"spin{i} 回り{sign:+0;-0} 向き{facing:+0;-0}"));
                }
            }
        Write(player, "_dodgeSpinSign", 1f);
        Write(player, "_facing", 1);

        // (e) 残像：その瞬間の絵・倍率・反転・Offset をそのまま写しているか。
        Call(player, "SpawnTrail");
        var trail = Read<List<Sprite2D>>(player, "_trail").Last();
        Check($"{label}：残像が倍率・反転・Offset を写している（Offset {Fmt(trail.Offset)} 倍率 {trail.Scale.X:0.0000}）",
            trail.Texture == sprite.Texture && trail.Scale.IsEqualApprox(sprite.Scale)
            && trail.FlipH == sprite.FlipH && trail.Offset.IsEqualApprox(sprite.Offset));
        Call(player, "EndDodge");
        await Frames(2);

        // (d) 当たり判定は一切触っていない＝見え方だけを直した。
        var hit = (CircleShape2D)player.GetNode<CollisionShape2D>("HitShape").Shape;
        var dot = player.GetNode<PlayerHitDot>("HitDot");
        var grazeArea = player.GetNode<Area2D>("GrazeArea");
        var graze = (CircleShape2D)grazeArea.GetChildren().OfType<CollisionShape2D>().First().Shape;
        Check($"{label}：当たり判定が不変（半径 {hit.Radius:0.###} layer {player.CollisionLayer} mask {player.CollisionMask}）",
            Mathf.IsEqualApprox(hit.Radius, 2f * game.HitRadiusMul) && player.CollisionLayer == 1 && player.CollisionMask == 12);
        Check($"{label}：芯（PlayerHitDot）が当たり判定と同じ位置・同じ半径（{dot.Radius:0.###}）",
            Mathf.IsEqualApprox(dot.Radius, hit.Radius) && dot.GlobalPosition.IsEqualApprox(player.GlobalPosition)
            && dot.Rotation == 0f && dot.Scale == Vector2.One && dot.ZIndex > sprite.ZIndex);
        Check($"{label}：グレイズ円が不変（半径 {graze.Radius:0.###} layer {grazeArea.CollisionLayer} mask {grazeArea.CollisionMask}）",
            Mathf.IsEqualApprox(graze.Radius, 11f) && grazeArea.CollisionLayer == 0 && grazeArea.CollisionMask == 8);

        Verdict(label, paid, samples);
        // 撮影用に取り置く（通常＋照準5方向＋回避8コマの素直な並び）。
        _kept[$"{job.CharacterId}_{(paid ? "costume" : "default")}"] = samples;
    }

    private void Verdict(string label, bool paid, List<Sample> samples)
    {
        foreach (var s in samples)
        {
            var fit = PlayerArt.Fit(s.Tex);
            var crown = fit.Crown - s.Tex.GetSize() * 0.5f;
            if (s.Flip) crown.X = -crown.X;
            var drawnCrown = (crown + s.Offset) * s.Scale;
            Check($"{label} {s.Tag}: head height {fit.HeadHeight * s.Scale:0.###}",
                Mathf.Abs(fit.HeadHeight * s.Scale - fit.GameHeadHeight) <= Tol);
            Check($"{label} {s.Tag}: body anchor follows head, not props",
                Mathf.Abs(drawnCrown.X) <= Tol
                && Mathf.Abs(drawnCrown.Y + fit.GameHeadHeight * 1.35f) <= Tol);
            Check($"{label} {s.Tag}: bounded gameplay silhouette",
                s.Now.Size.Y < 54f && s.Now.Size.X < 48f);

            var preview = PlayerArt.TextureRect(s.Tex, s.At, DisplayH, s.Flip);
            var gameplay = new Rect2(s.At + (s.Offset - s.Tex.GetSize() * 0.5f) * s.Scale, s.Tex.GetSize() * s.Scale);
            Check($"{label} {s.Tag}: customization and gameplay use identical framing",
                preview.Position.IsEqualApprox(gameplay.Position)
                && preview.Size.Abs().IsEqualApprox(gameplay.Size));
        }
    }

    // 描画後のスプライトから「中身」の外接をワールド座標で引く。
    //   ふわふわ浮遊(bob)・バンク・発射反動・回避の浮きは全ポーズ共通の“揺れ”で、ポーズが持つ位置ではない。
    //   ポーズ間の比較がそれで濁らないよう、スプライトのローカル位置と傾きだけ 0 に固定して測る
    //   （基準は当たり判定と同じ自機のグローバル座標。倍率・Offset・反転・絵はゲームが入れた値のまま読む）。
    private static Sample Take(Player player, Sprite2D s, string tag)
    {
        s.Position = Vector2.Zero;
        s.Rotation = 0f;
        var tex = s.Texture;
        var at = player.GlobalPosition;
        return new Sample(tag, tex, s.Scale.Y, s.Offset, s.FlipH, at,
            Drawn(tex, at, s.Offset, s.Scale, s.FlipH),
            Drawn(tex, at, Vector2.Zero, Vector2.One * (DisplayH / tex.GetHeight()), s.FlipH));
    }

    // Sprite2D(Centered) が実際に置く「中身」の矩形。テクスチャ枠は Offset を中心に置かれ、FlipH はその枠の
    //   中で UV を折り返す（枠自体は動かない）＝中身は枠の中心から ±（中身のズレ）に来る。
    private static Rect2 Drawn(Texture2D tex, Vector2 at, Vector2 offset, Vector2 scale, bool flip)
    {
        var c = UiKit.ContentRect(tex);
        float x0 = c.Position.X - tex.GetWidth() * 0.5f, x1 = x0 + c.Size.X;
        if (flip) (x0, x1) = (-x1, -x0);
        float y0 = c.Position.Y - tex.GetHeight() * 0.5f;
        var box = at + offset * scale;
        return new Rect2(box.X + x0 * scale.X, box.Y + y0 * scale.Y, c.Size.X * scale.X, c.Size.Y * scale.Y);
    }

    // ══════════════════ 撮影（窓あり）══════════════════
    //   透明下地の SubViewport に、ゲームが入れたのと同じ（絵・倍率・Offset・反転）でスプライトを並べて
    //   拡大描画する。塗られた画素の外接を測って計算どおりの位置に出ているかを画素で押さえ、
    //   保存する画像には足元の基準線をタイル横断で、中心の縦線をタイルごとに引く＝ズレていれば一目で分かる。
    private const int Zoom = 4, TileW = 60, TileH = 60, Gap = 8;
    private static readonly Vector2 TileAnchor = new(30f, 34f);   // タイル内で自機の中心（＝中身の中心）を置く場所
    private static readonly Color StripBg = new(0.08f, 0.09f, 0.11f), StripLine = new(0.65f, 0.86f, 0.93f),
        StripMid = new(0.35f, 0.40f, 0.48f);

    private async Task Strips()
    {
        await Strip("all_costumes",
            Jobs.All.Select(job => _kept[$"{job.CharacterId}_default"].First(s => s.Tag == "idle"))
                .Concat(Jobs.All.Select(job => _kept[$"{job.CharacterId}_costume"].First(s => s.Tag == "idle")))
                .ToList(), 4);
        var aim = new[] { "idle", "aim_u", "aim_ur", "aim_r", "aim_dr", "aim_d" };
        foreach (var job in Jobs.All)
        {
            var costume = _kept[$"{job.CharacterId}_costume"];
            var dflt = _kept[$"{job.CharacterId}_default"];
            // 回避8コマ（追加衣装）。旧方式でいちばん跳ねていた並び。
            await Strip($"dodge_strip_{job.CharacterId}",
                Pick(dflt, Enumerable.Range(0, 8).Select(i => $"spin{i}"))
                .Concat(Pick(costume, Enumerable.Range(0, 8).Select(i => $"spin{i}"))).ToList(), 8);
            // 既定（上段）／追加（下段）の通常＋照準5方向。大きさがそろっているか。
            await Strip($"costume_compare_{job.CharacterId}",
                Pick(dflt, aim).Concat(Pick(costume, aim)).ToList(), aim.Length);
        }
    }

    private static List<Sample> Pick(List<Sample> from, IEnumerable<string> tags) =>
        tags.Select(t => from.First(s => s.Tag == t)).ToList();

    private async Task Strip(string name, List<Sample> tiles, int cols)
    {
        int rows = (tiles.Count + cols - 1) / cols;
        int tw = TileW * Zoom, th = TileH * Zoom;
        int w = cols * tw + (cols - 1) * Gap, h = rows * th + (rows - 1) * Gap;
        var viewport = new SubViewport
        {
            Size = new Vector2I(w, h), TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            viewport.AddChild(new Sprite2D
            {
                Texture = t.Tex, Centered = true, FlipH = t.Flip, Offset = t.Offset,
                Scale = Vector2.One * (t.Scale * Zoom),
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,   // ゲームと同じ補間
                Position = Origin(i, cols) + TileAnchor * Zoom,
            });
        }
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var render = viewport.GetTexture().GetImage();
        viewport.QueueFree();
        if (render.GetFormat() != Image.Format.Rgba8) render.Convert(Image.Format.Rgba8);

        // 画素で測る：塗られた外接をワールドpx（自機の中心を原点）に戻して計算値と突き合わせる。
        //   許容は 0.75px ＋「外接の大きさが計算と食い違ったぶん」。素材の縁には孤立した薄い汚れが散っていて
        //   （例：char/player/rei/costume_v1/aim_dr.png は x=0〜3 に α≦68 の点が残っている）、α>0.05 で測る
        //   UiKit.ContentRect には入るが 1/20 前後まで縮小した描画では平均されて消える。外接が片側で d px
        //   狭く出れば中心はその半分ずれる＝ズレの上限が読める形にして、置き方の誤りとは切り分ける。
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            var origin = Origin(i, cols);
            var ink = Ink(render, new Rect2I(origin.X, origin.Y, tw, th));
            if (!ink.HasArea()) { Check($"{name} [{i}] {t.Tag}：画素が塗られている", false); continue; }
            var anchor = origin + TileAnchor * Zoom;   // このタイルで自機の中心（＝中身の中心）が来るべき画素
            float cx = (ink.GetCenter().X - anchor.X) / Zoom, foot = (ink.End.Y - anchor.Y) / Zoom;
            float wide = ink.Size.X / (float)Zoom, hi = ink.Size.Y / (float)Zoom;
            float wantC = t.Now.GetCenter().X - t.At.X, wantF = t.Now.End.Y - t.At.Y;
            float slackC = 0.75f + Mathf.Abs(wide - t.Now.Size.X) * 0.5f, slackF = 0.75f + Mathf.Abs(hi - t.Now.Size.Y);
            Check($"{name} [{i}] {t.Tag}：画素でも中心 {cx:0.##}（計算 {wantC:0.##}）"
                + $"／足元 {foot:0.##}（計算 {wantF:0.##}）／大きさ {wide:0.##}x{hi:0.##}"
                + $"（計算 {t.Now.Size.X:0.##}x{t.Now.Size.Y:0.##}）",
                Mathf.Abs(cx - wantC) <= slackC && Mathf.Abs(foot - wantF) <= slackF);
        }

        // 保存：暗い下地へ重ね、足元の基準線（横断）と中心の縦線を引く。
        using var sheet = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        sheet.Fill(StripBg);
        sheet.BlendRect(render, new Rect2I(0, 0, w, h), Vector2I.Zero);
        for (int r = 0; r < rows; r++)
        {
            int row = r * (th + Gap) + Mathf.RoundToInt((TileAnchor.Y + DisplayH / 2f) * Zoom);
            if (row >= 0 && row < h) sheet.FillRect(new Rect2I(0, row, w, 1), StripLine);
        }
        for (int i = 0; i < tiles.Count; i++)
        {
            var o = Origin(i, cols);
            int col = o.X + Mathf.RoundToInt(TileAnchor.X * Zoom);
            sheet.FillRect(new Rect2I(col, o.Y, 1, th), StripMid);
        }
        Check($"{name}.png（{tiles.Count} 枚・{w}x{h}）", sheet.SavePng($"{_out}/{name}.png") == Error.Ok);
    }

    private static Vector2I Origin(int index, int cols) =>
        new(index % cols * (TileW * Zoom + Gap), index / cols * (TileH * Zoom + Gap));

    // 透明下地に塗られた画素（α>InkCut）の外接（画像画素）。
    private static Rect2I Ink(Image img, Rect2I region)
    {
        byte[] d = img.GetData();
        int w = img.GetWidth();
        int x0 = region.End.X, y0 = region.End.Y, x1 = -1, y1 = -1;
        for (int y = region.Position.Y; y < region.End.Y; y++)
            for (int x = region.Position.X; x < region.End.X; x++)
            {
                if (d[(y * w + x) * 4 + 3] <= InkCut) continue;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                y1 = y;
            }
        return x1 < 0 ? new Rect2I() : new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    private static string Label(CosmeticItem item) => $"{Jobs.Get(item.Character!.Value).CharacterName}／{item.Name}";
    private static string Fmt(Vector2 v) => $"({v.X:0.##},{v.Y:0.##})";

    private void Check(string name, bool ok)
    {
        if (!ok) _fail++;
        GD.Print($"[PLS] {(ok ? "OK " : "NG ")} {name}");
    }

    // 待つのは**物理フレーム**。照準ポーズの差し替えも回避も Player._PhysicsProcess が書くので、描画フレームで
    //   数えると（描画が 60fps より速く回る走行では）物理が1回も進まないまま次の検査へ進んでしまう。
    private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
}
