using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// 検証ハーネス（QA・2026-09-27）：戦闘中の自機を「中身（不透明部分）の高さ」でそろえ、「中身の水平中心と
//   足元」をポーズ・衣装に依らず固定した改修（Player.DisplayH / ScaleFor / FitOffsetFor / ApplyBodyFit /
//   ApplyBodyOffset）を確かめる。発端は追加衣装（char/player/<id>/costume_v1）の素材に透明余白があり、
//   旧方式（36/テクスチャ高・Offset なし）だと
//     ・中身の高さが 29.2〜35.6px（既定衣装は 36.0px）＝衣装を替えると自機が最大 19% 小さく見える
//     ・回避8コマの間に見た目の中心が横へ、足元が縦へブレる（当たり半径 2px の自機に対して無視できない）
//   という状態だったこと。カスタマイズ／ショップのプレビュー（UiKit.ContentRect / PortraitScale）と同じ土台に寄せた。
//
//   見るもの（4キャラ × 既定/追加衣装 × 全ポーズ＝通常・照準5方向・回避8コマ・各左右反転）:
//     (a) 中身の高さが 36px（±0.5px）
//     (b) 同じキャラ・同じ衣装ならポーズを替えても中身の水平中心と下端（足元）が動かない（±0.5px）
//         ＝中身の中心が自機のグローバル座標（当たり判定の芯）に乗り続ける
//     (c) 既定衣装は修正前と完全に同じ＝倍率が 36/テクスチャ高 と一致し Offset が (0,0)、中身の外接が
//         旧方式の計算と一致する（修正前の数値をハードコードせず「旧式の式と一致」で押さえる＝プレイ感は不変）
//     (d) 当たり判定（HitShape 半径・レイヤー/マスク・PlayerHitDot の位置と半径・グレイズ円）が不変
//     (e) 回避の残像（SpawnTrail）が倍率・反転・Offset を写している（写さないと残像だけ余白ぶん横へずれる）
//   旧方式の外接も同じ走行で計算して出す＝「修正前どれだけブレていたか」を同じログで突き合わせられる。
//
//   使い方（実セーブを汚さないよう APPDATA=build/qa_story/playerscale_appdata を渡す）:
//     ヘッドレス : Godot --headless --path . res://tools/qa_player_scale.tscn
//     撮影つき（窓あり。ヘッドレスでは描画結果を読めない）:
//                  Godot --path . res://tools/qa_player_scale.tscn -- --pls-shot [--pls-out <絶対パス>]
//       → build/shots_player_scale/dodge_strip_<id>.png       追加衣装の回避8コマ（足元の基準線を横断で引く）
//          build/shots_player_scale/costume_compare_<id>.png   既定（上段）／追加（下段）の通常＋照準5方向
public partial class PlayerScaleQa : Node
{
    private const BindingFlags IPriv = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Read<T>(object o, string n) => (T)o.GetType().GetField(n, IPriv)!.GetValue(o)!;
    private static void Write(object o, string n, object v) => o.GetType().GetField(n, IPriv)!.SetValue(o, v);
    private static object? Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, IPriv)!.Invoke(o, a);

    private const float DisplayH = 36f;   // 期待する中身の高さ（Player.DisplayH と同じ意味の値）
    private const float Tol = 0.5f;       // 高さ・中心・足元の許容(px)
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
    }

    private static CosmeticItem Costume(JobTuning job, bool paid) => paid
        ? Array.Find(Cosmetics.All, i => i.Kind == CosmeticKind.Costume && i.Character == job.Id && i.Price > 0)!
        : Cosmetics.DefaultCostume(job.Id);

    // ── 素材の中身（ContentRect）と、旧方式で出ていた見かけの高さ ──
    //   既定衣装は生成時に切り詰め済み（中身＝テクスチャ全体）＝旧方式でもぴったり 36px。この事実が
    //   「既定衣装のプレイ感は変わらない」の根拠なので、全ポーズぶん明示的に押さえる。
    private void MaterialInfo()
    {
        foreach (var job in Jobs.All)
            foreach (bool paid in new[] { false, true })
            {
                var item = Costume(job, paid);
                float lo = float.MaxValue, hi = 0f;
                int trimmed = 0;
                foreach (string pose in Poses)
                {
                    var tex = GD.Load<Texture2D>(item.PosePath(pose));
                    var c = UiKit.ContentRect(tex);
                    float oldH = c.Size.Y * (DisplayH / tex.GetHeight());   // 旧方式での中身の高さ
                    lo = Mathf.Min(lo, oldH); hi = Mathf.Max(hi, oldH);
                    if (c == new Rect2I(0, 0, tex.GetWidth(), tex.GetHeight())) trimmed++;
                    else if (paid)
                        GD.Print($"[PLS] info 素材 {Label(item)} {pose}: tex {tex.GetWidth()}x{tex.GetHeight()} "
                            + $"中身 {c.Size.X}x{c.Size.Y}@({c.Position.X},{c.Position.Y}) 旧方式の見かけ高 {oldH:0.00}px");
                }
                if (paid)
                    Check($"{Label(item)}：追加衣装の素材は余白つき（旧方式の見かけ高 {lo:0.00}〜{hi:0.00}px）",
                        trimmed == 0 && lo < DisplayH - 0.05f);
                else
                    Check($"{Label(item)}：既定衣装の素材は全{Poses.Length}ポーズ切り詰め済み＝旧方式でも 36px"
                        + $"（見かけ高 {lo:0.00}〜{hi:0.00}px）",
                        trimmed == Poses.Length && Mathf.Abs(lo - DisplayH) < 0.01f && Mathf.Abs(hi - DisplayH) < 0.01f);
            }
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

    // ── (a)(b)(c)：測った外接から判定と、修正前との突き合わせ ──
    private void Verdict(string label, bool paid, List<Sample> samples)
    {
        // 自機のグローバル座標を原点にした相対値で見る（中身の中心が当たり判定の芯に乗り続けるか）。
        float hLo = float.MaxValue, hHi = 0f, cLo = float.MaxValue, cHi = float.MinValue, fLo = float.MaxValue, fHi = float.MinValue;
        float ohLo = float.MaxValue, ohHi = 0f, ocLo = float.MaxValue, ocHi = float.MinValue, ofLo = float.MaxValue, ofHi = float.MinValue;
        foreach (var s in samples)
        {
            float c = s.Now.GetCenter().X - s.At.X, f = s.Now.End.Y - s.At.Y;
            hLo = Mathf.Min(hLo, s.Now.Size.Y); hHi = Mathf.Max(hHi, s.Now.Size.Y);
            cLo = Mathf.Min(cLo, c); cHi = Mathf.Max(cHi, c);
            fLo = Mathf.Min(fLo, f); fHi = Mathf.Max(fHi, f);
            float oc = s.Old.GetCenter().X - s.At.X, of = s.Old.End.Y - s.At.Y;
            ohLo = Mathf.Min(ohLo, s.Old.Size.Y); ohHi = Mathf.Max(ohHi, s.Old.Size.Y);
            ocLo = Mathf.Min(ocLo, oc); ocHi = Mathf.Max(ocHi, oc);
            ofLo = Mathf.Min(ofLo, of); ofHi = Mathf.Max(ofHi, of);
            // (a) 中身の高さ
            Check($"{label} {s.Tag}：中身の高さが {DisplayH:0} px（{s.Now.Size.Y:0.###} 倍率 {s.Scale:0.0000} "
                + $"Offset {Fmt(s.Offset)}）", Mathf.Abs(s.Now.Size.Y - DisplayH) <= Tol);
            // (b) 中身の中心が自機の芯（＝当たり判定の中心）に乗っている＝中心も足元も動かない
            Check($"{label} {s.Tag}：中身の水平中心 {c:0.###} ／ 足元 {f:0.###}（期待 0 ／ {DisplayH / 2f:0.#}）",
                Mathf.Abs(c) <= Tol && Mathf.Abs(f - DisplayH / 2f) <= Tol);
            // (c) 既定衣装は修正前とまったく同じ（倍率＝36/テクスチャ高・Offset なし・外接が旧式の計算と一致）
            if (!paid)
                Check($"{label} {s.Tag}：修正前と同じ（倍率 {s.Scale:0.00000} == 36/{s.Tex.GetHeight()}・Offset (0,0)・外接一致）",
                    Mathf.IsEqualApprox(s.Scale, DisplayH / s.Tex.GetHeight()) && s.Offset == Vector2.Zero
                    && s.Now.Position.IsEqualApprox(s.Old.Position) && s.Now.Size.IsEqualApprox(s.Old.Size));
        }
        // ポーズを替えたときのブレ幅（修正後／修正前）。報告の表はこの行を並べたもの。
        GD.Print($"[PLS] info ブレ幅 {label}（{samples.Count} ポーズ）"
            + $" 中身の高さ {hLo:0.00}〜{hHi:0.00}px／中心 {cHi - cLo:0.00}px／足元 {fHi - fLo:0.00}px"
            + $"　←旧方式 高さ {ohLo:0.00}〜{ohHi:0.00}px／中心 {ocHi - ocLo:0.00}px／足元 {ofHi - ofLo:0.00}px");
        Check($"{label}：ポーズを替えても中身の水平中心が動かない（ブレ {cHi - cLo:0.###}px 旧 {ocHi - ocLo:0.###}px）",
            cHi - cLo <= Tol);
        Check($"{label}：ポーズを替えても足元が動かない（ブレ {fHi - fLo:0.###}px 旧 {ofHi - ofLo:0.###}px）",
            fHi - fLo <= Tol);
        Check($"{label}：中身の高さがポーズを通して同一（{hLo:0.###}〜{hHi:0.###}px 旧 {ohLo:0.###}〜{ohHi:0.###}px）",
            hHi - hLo <= Tol);
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
        var aim = new[] { "idle", "aim_u", "aim_ur", "aim_r", "aim_dr", "aim_d" };
        foreach (var job in Jobs.All)
        {
            var costume = _kept[$"{job.CharacterId}_costume"];
            var dflt = _kept[$"{job.CharacterId}_default"];
            // 回避8コマ（追加衣装）。旧方式でいちばん跳ねていた並び。
            await Strip($"dodge_strip_{job.CharacterId}", Pick(costume, Enumerable.Range(0, 8).Select(i => $"spin{i}")), 8);
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
