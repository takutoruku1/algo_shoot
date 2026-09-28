using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public partial class PortraitSizeQa : Node2D
{
    private const BindingFlags SPriv = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags IPriv = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Stat<T>(Type t, string name) => (T)t.GetField(name, SPriv)!.GetValue(null)!;
    private static T Read<T>(object o, string n) => (T)o.GetType().GetField(n, IPriv)!.GetValue(o)!;
    private static void Write(object o, string n, object v) => o.GetType().GetField(n, IPriv)!.SetValue(o, v);
    private static object? Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, IPriv)!.Invoke(o, a);

    // カスタマイズ画面が出すポーズ（通常＝idle／照準＝aim_ur／回避＝spin_00..04 をコマ送り）。
    private static readonly string[] Poses = { "idle", "aim_ur", "spin_00", "spin_01", "spin_02", "spin_03", "spin_04" };
    // 撮る3ポーズ（回避は3コマ目で止める）。_time は「そのコマの先頭」に置き、撮影の数フレームぶん進んでもコマが変わらないようにする。
    //   dodge_flip は回避の7コマ目（spin_02 を左右反転して出すコマ）。反転しても足元と中心が動かないかを見る。
    private static readonly (string Tag, int Pose, double Time)[] ShotPoses =
        { ("idle", 0, 0.3), ("aim", 1, 0.3), ("dodge", 2, 2.0 / 6.0 + 0.005), ("dodge_flip", 2, 1.0 + 0.005) };
    private static CosmeticItem[] Costumes => Array.FindAll(Cosmetics.All, i => i.Kind == CosmeticKind.Costume);

    private int _fail;
    private bool _shot;
    private string _out = "build/shots_portrait";
    // 画素で測るとき用：マゼンタ下地の上に立ち絵1枚だけを描く板（この QA ノード自身が描く）。
    private Texture2D? _probeTex;
    private Vector2 _probeCenter;
    private float _probeHeight = 1f;
    private bool _probeFlip;
    private static readonly Color Plate = new(1f, 0f, 1f);

    public override void _Draw()
    {
        if (_probeTex == null) return;
        UiKit.BeginDesign(this);
        DrawRect(new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH), Plate);
        PlayerArt.Draw(this, _probeTex, _probeCenter, _probeHeight, _probeFlip);
        UiKit.EndDesign(this);
    }

    public override async void _Ready()
    {
        // 全体の --shot（ShotTool 自動ロード＝撮って終了する）と衝突しないよう専用の旗にしてある。
        var args = OS.GetCmdlineUserArgs();
        foreach (var a in args) if (a == "--ps-shot") _shot = true;
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--ps-out") _out = args[i + 1];
        Visible = false;
        TextureFilter = TextureFilterEnum.Linear;   // カスタマイズ画面と同じ補間で測る
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
            GD.Print(_fail == 0 ? "[PS] DONE ok" : $"[PS] DONE fail={_fail}");
            GetTree().Quit(_fail == 0 ? 0 : 1);
        }
        catch (Exception ex) { GD.PushError($"[PS] FAIL {ex}"); GetTree().Quit(1); }
    }

    private async Task Run()
    {
        Check("セーブが隔離されている（APPDATA=build/qa_story/...）",
            OS.GetUserDataDir().Replace(Chr92, '/').Contains("/build/qa_story/"));
        var game = GetNode<GameManager>("/root/Game");
        game.AutoSaveEnabled = false;
        game.ResetPersistent();
        // 4キャラぶんの衣装を一覧に出すため全ステージをクリア済みにする（買わなくてもプレビューはできる）。
        foreach (var stage in GameManager.Stages) Read<HashSet<string>>(game, "_cleared").Add(stage.Id);

        float previewH = Stat<float>(typeof(Customize), "PreviewContentH");
        float thumbH = Stat<float>(typeof(Customize), "ThumbContentH");
        var frame = Stat<Rect2>(typeof(Customize), "PreviewFrame");
        var foot = Stat<Vector2>(typeof(Customize), "PreviewCenter");
        GD.Print($"[PS] info カスタマイズ：枠={Fmt(frame)} 中身の高さ={previewH:0.#} 足元={foot.X:0.#},{foot.Y:0.#} サムネの中身の高さ={thumbH:0.#}");

        int scansBefore = UiKit.ContentRectScans;
        MeasureCost();
        ContentInfo();
        CheckPreview(previewH, frame, foot);
        await CheckThumbs(thumbH);
        CheckShop();
        CheckCache(scansBefore);
        if (_shot) await Shots(game, previewH, foot);
    }

    private const char Chr92 = (char)92;   // Windows のパス区切り

    // ── 初回だけ走る「中身の実測」の値段。画面を開いた1フレームで数枚ぶん走るので、1枚が1フレームを食べないこと ──
    private void MeasureCost()
    {
        // 読み込み（GD.Load）は今までも同じだけ掛かっていたので、増えたぶん＝走査だけを測る。
        var texes = new List<Texture2D>();
        foreach (var item in Costumes)
            foreach (string pose in Poses) texes.Add(GD.Load<Texture2D>(item.PosePath(pose)));
        ulong t0 = Time.GetTicksUsec();
        foreach (var tex in texes) UiKit.ContentRect(tex);
        double ms = (Time.GetTicksUsec() - t0) / 1000.0;
        Check($"中身の実測は1枚あたり1フレーム未満（{texes.Count} 枚 {ms:0.#}ms・1枚 {ms / texes.Count:0.##}ms）", ms / texes.Count < 16.6);
    }

    // ── ① 素材の中身（ContentRect）と、α>0 で測る GetUsedRect との差 ──
    private void ContentInfo()
    {
        float worst = 0f;
        string worstAt = "";
        foreach (var item in Costumes)
            foreach (string pose in Poses)
            {
                var tex = GD.Load<Texture2D>(item.PosePath(pose));
                var content = UiKit.ContentRect(tex);
                Rect2I used;
                using (var img = tex.GetImage()) used = img.GetUsedRect();
                Check($"{item.Id}/{pose} の中身がテクスチャに収まっている（tex {tex.GetWidth()}x{tex.GetHeight()} 中身 {Fmt(content)}）",
                    content.Size.X > 20 && content.Size.Y > 20
                    && content.Position.X >= 0 && content.Position.Y >= 0
                    && content.End.X <= tex.GetWidth() && content.End.Y <= tex.GetHeight());
                Check($"{item.Id}/{pose} の中身は α>0 の外接以下（GetUsedRect {Fmt(used)}）",
                    content.Position.X >= used.Position.X && content.Position.Y >= used.Position.Y
                    && content.End.X <= used.End.X && content.End.Y <= used.End.Y);
                float diff = used.Size.Y - content.Size.Y;
                if (diff > worst) { worst = diff; worstAt = $"{item.Id}/{pose}（{used.Size.Y} → {content.Size.Y}）"; }
                if (item.Price == 0)
                    Check($"{item.Id}/{pose} 既定衣装の素材は切り詰め済み（中身＝テクスチャ全体）",
                        content == new Rect2I(0, 0, tex.GetWidth(), tex.GetHeight()));
                else
                    Check($"{item.Id}/{pose} 追加衣装の素材は余白つき（中身 {content.Size.X}x{content.Size.Y} ／ tex {tex.GetWidth()}x{tex.GetHeight()}）",
                        content.Size.X < tex.GetWidth() || content.Size.Y < tex.GetHeight());
            }
        GD.Print($"[PS] info 薄い縁（α≦{Mathf.RoundToInt(UiKit.ContentAlphaCut * 255f)}/255）を落として高さがいちばん縮んだ絵：{worstAt} = {worst:0}px");
    }

    // ── ②③④ 大プレビュー ──
    private void CheckPreview(float previewH, Rect2 frame, Vector2 foot)
    {
        foreach (var item in Costumes)
            foreach (string pose in Poses)
                foreach (bool flip in new[] { false, true })
                {
                    var tex = GD.Load<Texture2D>(item.PosePath(pose));
                    var fit = PlayerArt.Fit(tex);
                    var rect = PlayerBounds(tex, foot, previewH, flip);
                    Check($"{item.Id}/{pose}: preview fits", Inside(rect, frame));
                    Check($"{item.Id}/{pose}: preview keeps calibrated head size",
                        Mathf.IsEqualApprox(fit.Scale(previewH) * fit.HeadHeight, fit.GameHeadHeight * previewH / 36f));
                }
    }

    private static Rect2 PlayerBounds(Texture2D tex, Vector2 center, float height, bool flip = false)
    {
        var draw = PlayerArt.TextureRect(tex, center, height, flip);
        var content = UiKit.ContentRect(tex);
        float scale = PlayerArt.Fit(tex).Scale(height);
        float x = flip ? tex.GetWidth() - content.End.X : content.Position.X;
        return new Rect2(draw.Position + new Vector2(x, content.Position.Y) * scale, (Vector2)content.Size * scale);
    }

    // ── ⑤a 一覧のサムネ ──
    private async Task CheckThumbs(float thumbH)
    {
        var menu = GD.Load<PackedScene>("res://Customize.tscn").Instantiate<Customize>();
        GetTree().Root.AddChild(menu);
        GetTree().CurrentScene = menu;
        await Frames(4);
        Write(menu, "_tab", 1);
        Call(menu, "RefreshItems");   // 段を切り替えたら必ず一覧も作り直す（画面側と同じ順）
        foreach (var job in Jobs.All)
        {
            int ci = Array.FindIndex(Read<JobTuning[]>(menu, "_characters"), j => j.Id == job.Id);
            Check($"{job.CharacterName} が一覧に出ている", ci >= 0);
            Write(menu, "_character", ci);
            Call(menu, "RefreshItems");
            var items = Read<CosmeticItem[]>(menu, "_items");
            for (int i = 0; i < items.Length; i++)
            {
                var card = (Rect2)Call(menu, "ItemRect", i)!;
                var box = (Rect2)typeof(Customize).GetMethod("ThumbBox", SPriv)!.Invoke(null, new object[] { card })!;
                var tex = GD.Load<Texture2D>(items[i].PosePath("idle"));
                var rect = PlayerBounds(tex, box.GetCenter(), thumbH);
                Check($"サムネ {Label(items[i])}：枠 {Fmt(box)} に収まる（画面 {Fmt(rect)}）", Inside(rect, box));
            }
        }
        // カーソルは立ち絵ではないので枠フィットのまま（中身の高さでそろえると 34x40 の矢印が枠からはみ出す）。
        Write(menu, "_tab", 0);
        Call(menu, "RefreshItems");
        var cursors = Read<CosmeticItem[]>(menu, "_items");
        for (int i = 0; i < cursors.Length; i++)
        {
            var card = (Rect2)Call(menu, "ItemRect", i)!;
            var box = (Rect2)typeof(Customize).GetMethod("ThumbBox", SPriv)!.Invoke(null, new object[] { card })!;
            var tex = GD.Load<Texture2D>(cursors[i].PosePath("idle"));
            float fit = Mathf.Min(box.Size.X / tex.GetWidth(), box.Size.Y / tex.GetHeight());
            var size = tex.GetSize() * fit;
            Check($"カーソル {cursors[i].Name}：枠 {Fmt(box)} に収まる（枠フィット {size.X:0.#}x{size.Y:0.#}）",
                size.X <= box.Size.X + 0.01f && size.Y <= box.Size.Y + 0.01f);
        }
        await Frames(2);
        menu.QueueFree();
        await Frames(4);
        Audio.Instance?.StopMusic(0);
    }

    // ── ⑤b ショップの自機枠 ──
    private void CheckShop()
    {
        var area = Stat<Rect2>(typeof(Shop), "CharArea");
        float h = Stat<float>(typeof(Shop), "CharContentH");
        var foot = (Vector2)typeof(Shop).GetProperty("CharFoot", SPriv)!.GetValue(null)!;
        GD.Print($"[PS] info ショップ：枠={Fmt(area)} 中身の高さ={h:0.#} 足元={foot.X:0.#},{foot.Y:0.#}");
        var heights = new List<float>();
        foreach (var job in Jobs.All)
        {
            var tex = GD.Load<Texture2D>(job.PlayerTexturePath);
            float scale = UiKit.PortraitScale(tex, h);
            var rect = UiKit.PortraitRect(tex, foot, scale);
            var old = OldFitShop(tex, UiKit.ContentRect(tex), area);
            GD.Print($"[PS] info ショップ {job.CharacterName}: 倍率={scale:0.0000} 画面={Fmt(rect)}"
                + $"  ／旧方式 倍率={old.Scale:0.0000} 高さ={old.Rect.Size.Y:0.#} 足元Y={old.Rect.End.Y:0.#}");
            heights.Add(rect.Size.Y);
            Check($"ショップ {job.CharacterName}：中身が枠からはみ出さない（画面 {Fmt(rect)} ⊂ 枠 {Fmt(area)}）", Inside(rect, area));
            Check($"ショップ {job.CharacterName}：足元 Y が基準線（{rect.End.Y:0.###} 期待 {foot.Y:0.###}）",
                Mathf.Abs(rect.End.Y - foot.Y) < 0.01f);
        }
        Check($"ショップ 4キャラの中身の高さが同一（{heights.Min():0.###}〜{heights.Max():0.###} 期待 {h:0.###}）",
            heights.Max() - heights.Min() < 1f && Mathf.Abs(heights.Max() - h) < 0.01f);
    }

    // ── ⑥ キャッシュ ──
    private void CheckCache(int scansBefore)
    {
        // 立ち絵の素材（8アイテム × 7ポーズ）だけを1枚1回ずつ実測しているか。カーソルの絵を走査していたら増える。
        int want = Costumes.Length * Poses.Length;
        Check($"実測したのは立ち絵 {want} 枚ちょうど（実測 {UiKit.ContentRectScans - scansBefore} 枚）",
            UiKit.ContentRectScans - scansBefore == want);
        int before = UiKit.ContentRectScans;
        var first = new List<Rect2I>();
        foreach (var item in Costumes)
            foreach (string pose in Poses) first.Add(UiKit.ContentRect(GD.Load<Texture2D>(item.PosePath(pose))));
        int same = 0, k = 0;
        foreach (var item in Costumes)
            foreach (string pose in Poses)
                if (UiKit.ContentRect(GD.Load<Texture2D>(item.PosePath(pose))) == first[k++]) same++;
        Check($"2周目は GetImage() を呼び直さない（実測回数 {before} → {UiKit.ContentRectScans}）", UiKit.ContentRectScans == before);
        Check($"キャッシュは同じ矩形を返す（{same}/{first.Count}）", same == first.Count);
    }

    // ── ⑦ 撮影と比較画像（窓あり） ──
    private async Task Shots(GameManager game, float previewH, Vector2 foot)
    {
        var frame = Stat<Rect2>(typeof(Customize), "PreviewFrame");
        var cropPreview = new Rect2(frame.Position.X - 4, frame.Position.Y - 8, frame.Size.X + 8, frame.Size.Y + 12);
        int previewFootRow = Mathf.RoundToInt(foot.Y - cropPreview.Position.Y);
        var tiles = new Dictionary<string, Image>();

        var menu = GD.Load<PackedScene>("res://Customize.tscn").Instantiate<Customize>();
        GetTree().Root.AddChild(menu);
        GetTree().CurrentScene = menu;
        await Frames(4);
        Write(menu, "_tab", 1);
        Call(menu, "RefreshItems");
        foreach (var job in Jobs.All)
        {
            Write(menu, "_character", Array.FindIndex(Read<JobTuning[]>(menu, "_characters"), j => j.Id == job.Id));
            Call(menu, "RefreshItems");
            foreach (var (tag, pose, time) in ShotPoses)
            {
                Write(menu, "_selected", 1);   // 追加衣装＝余白の多い素材（旧方式でいちばん縮んでいた側）
                Write(menu, "_pose", pose);
                Write(menu, "_time", time);    // そのコマの先頭に置く（撮影の数フレームぶん進んでもコマが変わらない）
                using var full = await Grab();
                Save(full, $"cz_{job.CharacterId}_{tag}");
                tiles[$"{job.CharacterId}_{tag}"] = Tile(full, cropPreview);
            }
            Write(menu, "_selected", 0);       // 既定衣装の通常＝アイテムを替えても大きさが揃うか
            Write(menu, "_pose", 0);
            using var def = await Grab();
            Save(def, $"cz_{job.CharacterId}_idle_default");
            tiles[$"{job.CharacterId}_default"] = Tile(def, cropPreview);
        }
        menu.QueueFree();
        await Frames(4);
        Audio.Instance?.StopMusic(0);

        var ids = Jobs.All.Select(j => j.CharacterId).ToArray();
        SaveStrip("compare_idle", ids.Select(id => tiles[$"{id}_idle"]).ToList(), 4, previewFootRow);
        SaveStrip("compare_dodge",
            ids.Select(id => tiles[$"{id}_dodge"]).Concat(ids.Select(id => tiles[$"{id}_dodge_flip"])).ToList(), 4, previewFootRow);
        SaveStrip("compare_costume",
            ids.Select(id => tiles[$"{id}_default"]).Concat(ids.Select(id => tiles[$"{id}_idle"])).ToList(), 4, previewFootRow);
        foreach (string id in ids)
            SaveStrip($"compare_poses_{id}", ShotPoses.Select(p => tiles[$"{id}_{p.Tag}"]).ToList(), 4, previewFootRow);

        // ショップ
        var area = Stat<Rect2>(typeof(Shop), "CharArea");
        var shopFoot = (Vector2)typeof(Shop).GetProperty("CharFoot", SPriv)!.GetValue(null)!;
        var cropShop = new Rect2(area.Position.X - 4, area.Position.Y - 4, area.Size.X + 8, area.Size.Y + 8);
        var shopTiles = new List<Image>();
        foreach (var job in Jobs.All)
        {
            game.SelectedJob = job.Id;
            var shop = GD.Load<PackedScene>("res://Shop.tscn").Instantiate<Shop>();
            GetTree().Root.AddChild(shop);
            GetTree().CurrentScene = shop;
            Write(shop, "_t", 3.0);
            await Frames(8);
            Write(shop, "_toastT", 0.0);
            using var full = await Grab();
            Save(full, $"shop_{job.CharacterId}");
            shopTiles.Add(Tile(full, cropShop));
            shop.QueueFree();
            await Frames(4);
            Audio.Instance?.StopMusic(0);
        }
        SaveStrip("compare_shop", shopTiles, 4, Mathf.RoundToInt(shopFoot.Y - cropShop.Position.Y));

        foreach (var t in tiles.Values) t.Dispose();
        foreach (var t in shopTiles) t.Dispose();
        await CheckDrawnPixels(previewH, foot);
    }

    // 実際に塗られた画素の外接が、計算（UiKit.PortraitRect）どおりか。左右反転でも中身の中心は動かない。
    private async Task CheckDrawnPixels(float previewH, Vector2 foot)
    {
        Visible = true;
        foreach (var item in Costumes)
            foreach (var (pose, flip) in new[] { ("idle", false), ("spin_02", false), ("spin_02", true) })
            {
                _probeTex = GD.Load<Texture2D>(item.PosePath(pose));
                _probeCenter = foot;
                _probeHeight = previewH;
                _probeFlip = flip;
                QueueRedraw();
                using var img = await Grab();
                var want = PlayerBounds(_probeTex, foot, _probeHeight, flip);
                var got = InkRect(img);
                // 拡大補間の縁と、α が閾値ぎりぎりの画素ぶんの誤差を見込む。
                float d = Mathf.Max(
                    Mathf.Max(Mathf.Abs(got.Position.X - want.Position.X), Mathf.Abs(got.Position.Y - want.Position.Y)),
                    Mathf.Max(Mathf.Abs(got.End.X - want.End.X), Mathf.Abs(got.End.Y - want.End.Y)));
                Check($"画素で確認 {Label(item)} {pose}{(flip ? "・反転" : "")}：塗った外接 {Fmt(got)} ≒ 計算 {Fmt(want)}（最大ズレ {d:0.#}px）", d <= 4f);
            }
        _probeTex = null;
        Visible = false;
        QueueRedraw();
        await Frames(2);
    }

    // ショップの旧方式は枠より狭い 228 を横の上限に使っていた（Shop.DrawCharacter の元コード）。
    private static (float Scale, Rect2 Rect) OldFitShop(Texture2D tex, Rect2I c, Rect2 area)
    {
        float scale = Mathf.Min(228f / tex.GetWidth(), area.Size.Y / tex.GetHeight());
        var pos = area.GetCenter() - tex.GetSize() * scale / 2f;
        return (scale, new Rect2(pos.X + c.Position.X * scale, pos.Y + c.Position.Y * scale, c.Size.X * scale, c.Size.Y * scale));
    }

    // ── 画面の取得・保存・合成 ──
    private async Task<Image> Grab()
    {
        await Frames(2);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return GetViewport().GetTexture().GetImage();
    }

    private void Save(Image img, string name) => Check($"screenshot {name}.png", img.SavePng($"{_out}/{name}.png") == Error.Ok);

    // 設計座標の矩形で切り抜く（窓の実解像度が 1280x720 でなくても比率で合わせる）。
    private Image Tile(Image full, Rect2 design)
    {
        float sx = full.GetWidth() / UiKit.DesignW, sy = full.GetHeight() / UiKit.DesignH;
        var crop = new Rect2I(Mathf.RoundToInt(design.Position.X * sx), Mathf.RoundToInt(design.Position.Y * sy),
            Mathf.RoundToInt(design.Size.X * sx), Mathf.RoundToInt(design.Size.Y * sy));
        var tile = full.GetRegion(crop);
        if (tile.GetFormat() != Image.Format.Rgba8) tile.Convert(Image.Format.Rgba8);
        return tile;
    }

    private static readonly Color StripBg = new(0.08f, 0.09f, 0.11f), StripLine = new(0.65f, 0.86f, 0.93f);
    private const int StripGap = 4;

    private void SaveStrip(string name, IReadOnlyList<Image> tiles, int cols, int footRow)
    {
        if (tiles.Count == 0) { Check($"compare {name}（タイルなし）", false); return; }
        int tw = tiles[0].GetWidth(), th = tiles[0].GetHeight();
        int rows = (tiles.Count + cols - 1) / cols;
        using var img = Image.CreateEmpty(cols * tw + (cols - 1) * StripGap, rows * th + (rows - 1) * StripGap,
            false, Image.Format.Rgba8);
        img.Fill(StripBg);
        for (int i = 0; i < tiles.Count; i++)
        {
            int cx = i % cols * (tw + StripGap), cy = i / cols * (th + StripGap);
            img.BlitRect(tiles[i], new Rect2I(0, 0, tw, th), new Vector2I(cx, cy));
            // 足元の基準線（タイル横断）。そろっていなければここでズレて見える。
            int row = cy + footRow;
            if (row >= 0 && row < img.GetHeight()) img.FillRect(new Rect2I(cx, row, tw, 1), StripLine);
        }
        Check($"compare {name}.png（{tiles.Count} 枚・{img.GetWidth()}x{img.GetHeight()}）",
            img.SavePng($"{_out}/{name}.png") == Error.Ok);
    }

    // マゼンタ下地の上に塗られた画素の外接（設計座標）。
    private static Rect2 InkRect(Image img)
    {
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        byte[] d = img.GetData();
        int w = img.GetWidth(), h = img.GetHeight();
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (d[i] > 247 && d[i + 1] < 8 && d[i + 2] > 247) continue;   // 下地のまま
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                y1 = y;
            }
        if (x1 < 0) return new Rect2();
        float sx = UiKit.DesignW / w, sy = UiKit.DesignH / h;
        return new Rect2(x0 * sx, y0 * sy, (x1 - x0 + 1) * sx, (y1 - y0 + 1) * sy);
    }

    private static bool Inside(Rect2 inner, Rect2 outer) =>
        inner.Position.X >= outer.Position.X - 0.01f && inner.Position.Y >= outer.Position.Y - 0.01f
        && inner.End.X <= outer.End.X + 0.01f && inner.End.Y <= outer.End.Y + 0.01f;

    private static string Label(CosmeticItem item) =>
        $"{Jobs.Get(item.Character!.Value).CharacterName}／{item.Name}";
    private static string Fmt(Rect2 r) => $"({r.Position.X:0.#},{r.Position.Y:0.#},{r.Size.X:0.#}x{r.Size.Y:0.#})";
    private static string Fmt(Rect2I r) => $"({r.Position.X},{r.Position.Y},{r.Size.X}x{r.Size.Y})";

    private void Check(string name, bool ok)
    {
        if (!ok) _fail++;
        GD.Print($"[PS] {(ok ? "OK " : "NG ")} {name}");
    }

    private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
