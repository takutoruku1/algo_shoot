using Godot;

// 弾形（RefrainHTML/Refrain Danmaku v3 の弾形7種）。言葉弾は Bullet.Word で別扱い。
public enum BulletShape { Orb, Diamond, Star, Ring, Needle, Rice }

// Bullet : Area2D。Pool により生成・使い回しされる弾。
// 当たり判定（被弾処理）は敵側/自機側で行うため、Bullet 自身は Area 重なり処理を持たない。
// _PhysicsProcess で等速直線移動し、画面外(余白16px)に出たら Pool.Despawn(this)。
public partial class Bullet : Area2D
{
    // 衝突レイヤー/マスク（ビット値, 仕様準拠）
    // PlayerBullet: layer=2, mask=4
    // EnemyBullet:  layer=8, mask=1
    private const uint LayerPlayerBullet = 2;
    private const uint MaskPlayerBullet = 4;
    private const uint LayerEnemyBullet = 8;
    private const uint MaskEnemyBullet = 1;

    // 画面サイズと画面外判定の余白
    private const float ScreenWidth = 384f;
    private const float ScreenHeight = 216f;
    private const float Margin = 16f;

    public Vector2 Velocity;
    public bool IsEnemy;
    public int Damage;
    public bool Active;
    public bool Grazed;  // グレイズ済みか（重複加点防止）
    public string Word = "";  // 非空なら「言葉弾」＝文字そのものが弾（道中の敵。設計書 4）

    // 弾形とスペル色（敵弾のみ反映）。色未指定時は既定の穢れ色。
    public BulletShape Shape = BulletShape.Orb;
    public Color Tint;
    public bool TintSet;

    // ─── ボス別弾幕ギミック（#12 機構側）の弾フラグ ───
    // Erasable: 自機弾で消せる「祈り弾」（こはる FanDown）。消すと双方消滅＋やさしさ微加算。
    // SoftenOnGraze: グレイズすると一度だけ減速×GrazeSoftenMul＋淡色化する「キミ弾」（あかり）。被弾判定は不変。
    public bool Erasable;
    public bool SoftenOnGraze;
    public bool Softened;   // 減速・淡色化が適用済みか（1発につき1回だけ）
    // M2バランス：×0.75 は自機狙い弾をほぼ無力化していた（かすった時点で回避が確定する）ため ×0.85 に緩和。
    // “読める”手応えは残しつつ、グレイズ＝安全化ではなくす。
    public const float GrazeSoftenMul = 0.85f;

    // ホーミング（自機ショットの誘導モード・設計書 §3-2③）。右側の穢れ標的へ最大旋回角つきで曲射。
    public bool Homing;
    private Node2D? _homeTarget;
    private const float HomingTurnRate = 95f; // deg/s（精度控えめ＝外した弾は旋回しきれず画面外へ抜ける＝常駐しない）

    // 言葉弾の文字フォント（全弾で共有。初回だけロード）。
    private static FontFile? _wordFont;
    private static FontFile? WordFont
    {
        get
        {
            _wordFont ??= UiKit.Zen; // 言葉弾も滑らかゴシック（非ピクセル）
            return _wordFont;
        }
    }

    // 言葉弾にする（Spawn 後に呼ぶ）。再描画して文字を反映。
    // handle を渡すと「下に流れるコメント（ティッカー）」と同じ投稿者ハンドルを弾にも乗せ、
    // “下に流れていたコメントが弾として降ってくる”連動感を出す（Hud.TickerWords が共有ソース）。
    public void SetWord(string w, string handle = "")
    {
        Word = w;
        _wordHandle = handle;
        // 晒し投稿らしい“いいね数”を語ごとに固定で付与（X感／毎フレーム再計算しない）。
        int h = 0; foreach (char c in w) h = h * 31 + c;
        int likes = 800 + (Mathf.Abs(h) % 47000);
        _wordLikes = likes >= 10000 ? $"{likes / 10000f:0.0}万" : likes.ToString("#,0");
        QueueRedraw();
    }
    private string _wordLikes = "";
    private string _wordHandle = "";

    public float Radius { get; private set; } = 3f;

    // RefrainScripts_tama / Refrain HUD A.dc.html の弾デザイン。
    // ドット絵ではなく「白ハイライト→中間色→暗エッジのグラデ＋外周グロー」の滑らかな弾。
    // 敵弾: radial-gradient(circle at 35% 30%, #fff, #e072ac 60%, #7a2f5a) + glow rgba(224,114,172,.75)
    private static readonly Color EnemyMid  = new Color(0.882f, 0.447f, 0.675f); // #e072ac ボス穢れ
    private static readonly Color EnemyEdge = new Color(0.478f, 0.184f, 0.353f); // #7a2f5a 暗マゼンタ縁
    private static readonly Color EnemyGlow = new Color(0.878f, 0.447f, 0.675f); // rgba(224,114,172)
    // 自機弾: radial-gradient(circle at 40% 35%, #fff, #6cbcd8 65%) + glow rgba(108,188,216,.8)
    private static readonly Color PlayerMid  = new Color(0.424f, 0.737f, 0.847f); // #6cbcd8 浄化
    private static readonly Color PlayerEdge = new Color(0.247f, 0.490f, 0.604f); // 暗めの水色縁
    private static readonly Color PlayerGlow = new Color(0.424f, 0.737f, 0.847f); // rgba(108,188,216)
    private static readonly Color KegareWord = new Color(0.96f, 0.56f, 0.78f);    // 言葉弾の文字（穢れ系）

    private CollisionShape2D _shape = null!;
    private CircleShape2D _circle = null!;

    public override void _Ready()
    {
        // 子に CollisionShape2D(CircleShape2D) を追加
        _circle = new CircleShape2D { Radius = Radius };
        _shape = new CollisionShape2D { Shape = _circle };
        AddChild(_shape);

        // 「祈り弾」（Erasable）のみ自機弾との重なりを自前処理する（MakeErasable が mask を開く）。
        AreaEntered += OnAreaEntered;

        // 初期状態は非アクティブ
        Deactivate();
    }

    // layer/mask/見た目/位置を設定し、可視化・monitoring 有効化。
    public void Activate(Vector2 pos, Vector2 vel, bool isEnemy, float radius, int damage,
        BulletShape shape = BulletShape.Orb, Color? tint = null, bool homing = false)
    {
        Velocity = vel;
        IsEnemy = isEnemy;
        Damage = damage;
        Radius = radius;
        Active = true;
        Grazed = false;
        Word = "";  // 再利用時に前の言葉を持ち越さない
        Erasable = false;       // ギミックフラグも再利用時に持ち越さない
        SoftenOnGraze = false;
        Softened = false;
        Shape = shape;
        TintSet = tint.HasValue;
        if (tint.HasValue) Tint = tint.Value;
        Homing = homing;
        _homeTarget = null;

        GlobalPosition = pos;

        // グループ登録（ボムの一括消去・グレイズ判定用）
        AddToGroup(isEnemy ? "enemy_bullets" : "player_bullets");

        // 当たり半径を反映
        if (_circle != null)
            _circle.Radius = radius;

        // レイヤー/マスク設定（isEnemy に応じて）
        if (isEnemy)
        {
            CollisionLayer = LayerEnemyBullet; // 8
            CollisionMask = MaskEnemyBullet;   // 1
        }
        else
        {
            CollisionLayer = LayerPlayerBullet; // 2
            CollisionMask = MaskPlayerBullet;   // 4
        }

        // 可視化・検出有効化。衝突状態の変更はシグナル中でも安全なよう遅延設定する
        // （Deactivate と順序を揃え、再利用時の取り違えを防ぐ）。
        Visible = true;
        SetPhysicsProcess(true);
        SetDeferred(Area2D.PropertyName.Monitoring, true);
        SetDeferred(Area2D.PropertyName.Monitorable, true);
        if (_shape != null)
            _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, false);

        QueueRedraw();
    }

    // 非表示・monitoring 無効化・プールへ戻る準備。
    public void Deactivate()
    {
        Active = false;
        Velocity = Vector2.Zero;

        // グループから外す（プール返却時）
        RemoveFromGroup("enemy_bullets");
        RemoveFromGroup("player_bullets");

        Visible = false;
        SetPhysicsProcess(false);
        // 衝突の無効化は遅延で行う。被弾シグナル（OnAreaEntered → Despawn）の最中に
        // monitoring / shape.disabled を直接書き換えると Godot にブロックされ、
        // 「見えないのに当たり判定だけ残る弾」が発生していた。Active=false は即時なので、
        // 消費側（Player/Panel）が Active を見ている限り遅延中の1フレームも安全。
        SetDeferred(Area2D.PropertyName.Monitoring, false);
        SetDeferred(Area2D.PropertyName.Monitorable, false);
        if (_shape != null)
            _shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
    }

    // 「祈り弾」にする（Spawn 後に呼ぶ。こはる FanDown）。自機弾(layer=2)を拾う mask を開き、
    // 消せる合図の淡い暖色ハロを再描画で反映する。Activate が mask/フラグを毎回リセットするので持ち越さない。
    public void MakeErasable()
    {
        Erasable = true;
        CollisionMask |= LayerPlayerBullet;
        QueueRedraw();
    }

    // 祈り弾×自機弾の重なり：双方消して「受け止めた」の手応え＋やさしさ微加算。
    // 自機弾も消費する＝雨を受け止めるぶん本体への火力が落ちる（受け皿のコスト＝リスクとリターン）。
    private void OnAreaEntered(Area2D area)
    {
        if (!Active || !IsEnemy || !Erasable) return;
        if (area is Bullet pb && !pb.IsEnemy && pb.Active)
        {
            var pool = GetNodeOrNull<BulletPool>("/root/Pool");
            pool?.Despawn(pb);
            FxLayer.Instance?.BulletToPetal(GlobalPosition); // 弾が花びらへ＝“祈りを受け止めた”
            Audio.Instance?.PlayStrip();                     // 軽い「コツッ」（剥離と同域＝浄化より一段軽い）
            GetNodeOrNull<GameManager>("/root/Game")?.AddPrayerCleared();
            if (pool != null) pool.Despawn(this); else Deactivate();
        }
    }

    // 「キミ弾」のグレイズ軟化（あかり）：一度だけ減速×GrazeSoftenMul＋淡色化。
    // 当たり判定（半径・被弾処理）は一切変えない＝“安全になる”のではなく“読める”ようになる。
    public void ApplyGrazeSoften()
    {
        if (!SoftenOnGraze || Softened) return;
        Softened = true;
        Velocity *= GrazeSoftenMul;
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Active)
            return;

        // 会話中（吹き出し表示中）は飛んでいる弾も止める＝攻撃を停止
        if (Hud.BubblePaused)
            return;

        // ホーミング：右側の最寄りの穢れ標的へ向きを補間（速度の大きさは一定）。
        if (Homing && !IsEnemy)
            SteerToTarget((float)delta);

        GlobalPosition += Velocity * (float)delta;

        // 画面外(余白16px)に出たら Despawn
        var p = GlobalPosition;
        if (p.X < -Margin || p.X > ScreenWidth + Margin ||
            p.Y < -Margin || p.Y > ScreenHeight + Margin)
        {
            var pool = GetNodeOrNull<BulletPool>("/root/Pool");
            if (pool != null)
                pool.Despawn(this);
            else
                Deactivate();
        }
    }

    // 標的を一度ロックし、消滅/浄化時のみ再探索（毎フレーム全探索は重いので）。
    private void SteerToTarget(float delta)
    {
        var tgt = _homeTarget;
        if (tgt == null || !IsInstanceValid(tgt) || (tgt is Enemy en && en.IsPurified))
            tgt = _homeTarget = AcquireTarget();
        if (tgt == null) return; // 標的が無ければ直進

        float spd = Velocity.Length();
        if (spd < 0.01f) return;
        float cur = Velocity.Angle();
        float want = (tgt.GlobalPosition - GlobalPosition).Angle();
        float maxStep = Mathf.DegToRad(HomingTurnRate) * delta;
        float na = cur + Mathf.Clamp(Mathf.AngleDifference(cur, want), -maxStep, maxStep);
        Velocity = new Vector2(Mathf.Cos(na), Mathf.Sin(na)) * spd;
    }

    // 右側（X が自分より大きい）の未浄化の敵本体から最寄りを選ぶ。
    private Node2D? AcquireTarget()
    {
        Node2D? best = null;
        float bestD = float.MaxValue;
        foreach (Node n in GetTree().GetNodesInGroup("enemies"))
        {
            if (n is Enemy e && !e.IsPurified && e.GlobalPosition.X > GlobalPosition.X - 4f)
            {
                float d = e.GlobalPosition.DistanceSquaredTo(GlobalPosition);
                if (d < bestD) { bestD = d; best = e; }
            }
        }
        return best;
    }

    public override void _Draw()
    {
        if (!Active)
            return;

        float r = Radius;

        // 言葉弾：X の“晒し投稿”そのものが弾。ステージ選択/ボスカード等と同じ X カード語彙
        // （アバター丸＋認証バッジ［色丸＋白✓］＋@ハンドル＋本文＋いいね）に寄せる。
        // ただし弾は極小・大量に出る＝可読性最優先（吉田 §1/§10）。読みの優先度は
        //   ①本文（刺さる言葉）＞②“ポストだ”の記号（アバター＋✓）＞③メタ（@ハンドル/いいね）。
        var wf = WordFont;
        if (!string.IsNullOrEmpty(Word) && wf != null)
        {
            const int fs = 9, hs = 6, ms = 6; // 本文/ハンドル/いいねの文字サイズ
            var sz = wf.GetStringSize(Word, HorizontalAlignment.Left, -1, fs);
            const float av = 5.5f, pad = 3f, gap = 3.5f, metaH = 6.5f;
            bool hasHandle = !string.IsNullOrEmpty(_wordHandle);
            float handleH = hasHandle ? 6.5f : 0f;
            float bodyW = sz.X;
            // ハンドル行ぶんも幅に効かせる（本文より短ければ本文幅で決まる）。
            if (hasHandle) bodyW = Mathf.Max(bodyW, wf.GetStringSize(_wordHandle, HorizontalAlignment.Left, -1, hs).X);
            float cw = pad + av * 2f + gap + bodyW + pad;
            float bodyBlock = handleH + Mathf.Max(av * 2f - handleH, sz.Y);
            float ch = pad + Mathf.Max(av * 2f, bodyBlock) + metaH + pad;
            float x0 = -cw / 2f, y0 = -ch / 2f;

            // 投稿カード（X ダーク・角丸ガラス）＝ハブ/ステージ選択のカードと同じ UiKit.Box で統一。
            // StyleBox はアンチエイリアスが効くので小サイズでも角丸が潰れない。
            UiKit.Box(this, new Rect2(x0, y0, cw, ch), new Color(0.05f, 0.06f, 0.10f, 0.9f), 5f,
                new Color(KegareWord.R, KegareWord.G, KegareWord.B, 0.34f), 1f);

            // アバター（穢れ）＋認証バッジ（右下：小さな桃丸＋白✓）＝ボス/スペルカードと同じ語彙。
            Vector2 ac = new Vector2(x0 + pad + av, y0 + pad + av);
            DrawCircle(ac, av, new Color(0.35f, 0.13f, 0.27f));
            Vector2 bc = ac + new Vector2(av * 0.7f, av * 0.7f);
            DrawCircle(bc, av * 0.5f, KegareWord);
            DrawWordCheck(bc, av * 0.34f, new Color(1f, 1f, 1f, 0.95f)); // 認証✓（白・2線分）

            // 右カラム：①@ハンドル（小・淡）→②本文（晒し言葉・主役）
            float bx = x0 + pad + av * 2f + gap;
            float ty = y0 + pad;
            if (hasHandle)
            {
                DrawString(wf, new Vector2(bx, ty + hs - 1f), _wordHandle, HorizontalAlignment.Left, -1, hs,
                    new Color(0.74f, 0.60f, 0.72f, 0.85f));
                ty += handleH;
            }
            DrawString(wf, new Vector2(bx, ty + sz.Y - 2f), Word, HorizontalAlignment.Left, -1, fs, KegareWord);

            // いいね（小さなハート＋数）＝拡散の重み
            float ly = y0 + ch - 2.2f;
            DrawWordHeart(new Vector2(bx + 2f, ly - 2.4f), 2.2f, new Color(KegareWord.R, KegareWord.G, KegareWord.B, 0.9f));
            DrawString(wf, new Vector2(bx + 8f, ly), _wordLikes, HorizontalAlignment.Left, -1, ms,
                new Color(0.78f, 0.62f, 0.74f, 0.9f));

            // 致命点（カード中心＝当たり判定）。自機の被弾点と同じ記号語彙＝赤コア＋白フチ。
            // 「文字は脅し、刺さるのはこの点」を一目で読ませる（§3 視認性／§7 理不尽回避）。
            DrawCircle(Vector2.Zero, Radius + 1f, new Color(1f, 1f, 1f, 0.95f));
            DrawCircle(Vector2.Zero, Radius, new Color(1f, 0.2f, 0.3f, 1f));
            return;
        }

        if (!IsEnemy)
        {
            // 自機弾は常にガラス円弾（浄化の水色）。
            DrawGlassBullet(r, PlayerMid, PlayerEdge, PlayerGlow);
            return;
        }

        // 敵弾：スペルの色（未指定なら既定の穢れ色）と弾形で描く。
        Color c = TintSet ? Tint : EnemyMid;
        // グレイズ軟化済み（キミ弾）：白へ寄せた淡色＝「和らいだ」を色で読ませる（判定は不変）。
        if (Softened) c = c.Lerp(new Color(1f, 1f, 1f), 0.5f);
        switch (Shape)
        {
            case BulletShape.Diamond: DrawDiamond(r, c); break;
            case BulletShape.Star:    DrawStar(r, c);    break;
            case BulletShape.Ring:    DrawRing(r, c);    break;
            case BulletShape.Needle:  DrawNeedle(r, c);  break;
            case BulletShape.Rice:    DrawRice(r, c);    break;
            default:                  DrawOrb(r, c);     break; // 円弾＝白リング＋暗芯（芯色のみ可変）
        }

        // 当たり芯（#16 見える化）：弾中心の高輝度ドット＝「刺さるのはこの点」。
        // 言葉弾の赤コアと同じ発想を通常弾へ。弾形の色を隠さないよう小さく・白のみ（派手にしない）。
        DrawCircle(Vector2.Zero, Mathf.Min(1.5f, r * 0.42f), new Color(1f, 1f, 1f, 0.9f), true, -1f, true);

        // 祈り弾（消せる弾）の合図：淡い暖白のハロリング＝「自機弾で受け止められる」を一目で。
        if (Erasable)
            DrawArc(Vector2.Zero, r + 2.4f, 0, Mathf.Tau, 24, new Color(1f, 0.95f, 0.8f, 0.55f), 1.1f, true);
    }

    // 認証バッジの白✓。極小サイズではフォント✓が潰れるので2線分のチェック記号で描く。
    private void DrawWordCheck(Vector2 c, float r, Color col)
    {
        Vector2 a = c + new Vector2(-r, 0.05f * r);
        Vector2 b = c + new Vector2(-0.25f * r, 0.75f * r);
        Vector2 d = c + new Vector2(r, -0.7f * r);
        DrawLine(a, b, col, 0.7f, true);
        DrawLine(b, d, col, 0.7f, true);
    }

    // 言葉弾カードの小さなハート（いいね）。2円＋三角で簡易描画。
    private void DrawWordHeart(Vector2 c, float r, Color col)
    {
        DrawCircle(c + new Vector2(-r * 0.45f, -r * 0.2f), r * 0.6f, col);
        DrawCircle(c + new Vector2(r * 0.45f, -r * 0.2f), r * 0.6f, col);
        DrawColoredPolygon(new[]
        {
            c + new Vector2(-r * 0.9f, 0f), c + new Vector2(r * 0.9f, 0f), c + new Vector2(0f, r),
        }, col);
    }

    // 外周グロー（box-shadow 相当）：薄い同心円を外→内に重ねてぼかしを近似。
    private void DrawGlow(float baseR, Color glow, float reach = 1.3f)
    {
        const int gSteps = 5;
        for (int i = gSteps; i >= 1; i--)
        {
            float t = i / (float)gSteps;
            float gr = baseR * (1f + reach * t);
            float a = 0.16f * (1f - t) + 0.04f;
            DrawCircle(Vector2.Zero, gr, new Color(glow.R, glow.G, glow.B, a), true, -1f, true);
        }
    }

    // HTML(Refrain HUD A) のガラス円弾：外周グロー＋白ハイライト→中間→暗エッジのグラデ。
    private void DrawGlassBullet(float r, Color mid, Color edge, Color glow)
    {
        DrawGlow(r, glow);
        DrawCircle(Vector2.Zero, r, edge, true, -1f, true);
        DrawCircle(Vector2.Zero, r * 0.82f, edge.Lerp(mid, 0.6f), true, -1f, true);
        DrawCircle(Vector2.Zero, r * 0.60f, mid, true, -1f, true);
        var hl = new Vector2(-0.28f * r, -0.36f * r);
        DrawCircle(hl, r * 0.34f, new Color(1f, 1f, 1f, 0.95f), true, -1f, true);
    }

    // 円弾：作品準拠の「白リング＋暗芯」。芯色のみスペル色で可変（Danmaku v3 shapeInner orb）。
    private void DrawOrb(float r, Color core)
    {
        DrawGlow(r, core);
        DrawCircle(Vector2.Zero, r, new Color(1f, 1f, 1f, 0.95f), true, -1f, true); // 白リング
        DrawCircle(Vector2.Zero, r * 0.70f, new Color(0.086f, 0.039f, 0.071f), true, -1f, true); // 暗芯リング
        DrawCircle(Vector2.Zero, r * 0.40f, core, true, -1f, true); // 芯色
    }

    // 菱形：45度回転の四角＋グロー（shapeInner diamond）。
    private void DrawDiamond(float r, Color c)
    {
        DrawGlow(r, c, 1.1f);
        float s = r * 1.15f;
        var pts = new[] { new Vector2(0, -s), new Vector2(s, 0), new Vector2(0, s), new Vector2(-s, 0) };
        DrawColoredPolygon(pts, c);
        DrawColoredPolygon(ScalePts(pts, 0.5f), new Color(1f, 1f, 1f, 0.85f)); // 芯の光
    }

    // 星：5芒星（shapeInner star の clip-path 相当）。
    private void DrawStar(float r, Color c)
    {
        DrawGlow(r, c, 1.1f);
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float ang = Mathf.DegToRad(-90f + i * 36f);
            float rad = (i % 2 == 0) ? r * 1.15f : r * 0.48f;
            pts[i] = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
        }
        DrawColoredPolygon(pts, c);
    }

    // リング：中空の輪（shapeInner ring）。
    private void DrawRing(float r, Color c)
    {
        DrawGlow(r, c, 1.0f);
        DrawArc(Vector2.Zero, r * 0.9f, 0, Mathf.Tau, 28, c, Mathf.Max(1.4f, r * 0.42f), true);
        DrawArc(Vector2.Zero, r * 0.9f, 0, Mathf.Tau, 28, new Color(c.R, c.G, c.B, 0.5f), 0.9f, true);
    }

    // 針：進行方向へ伸びる細い弾（shapeInner needle）。
    private void DrawNeedle(float r, Color c)
    {
        float ang = Velocity.LengthSquared() > 0.01f ? Velocity.Angle() : Mathf.Pi / 2f;
        DrawSetTransform(Vector2.Zero, ang, Vector2.One);
        float len = r * 2.8f, w = r * 0.78f;
        DrawGlow(r * 0.8f, c, 0.8f);
        DrawRect(new Rect2(-len * 0.5f, -w * 0.5f, len, w), c);
        DrawCircle(new Vector2(-len * 0.5f, 0), w * 0.5f, c, true, -1f, true); // 後端の丸
        DrawCircle(new Vector2(len * 0.5f, 0), w * 0.5f, c, true, -1f, true);  // 先端の丸
        DrawRect(new Rect2(-len * 0.5f, -w * 0.18f, len, w * 0.36f), new Color(1f, 1f, 1f, 0.55f)); // 中央のハイライト
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    // 粒弾：進行方向へ細長い楕円（shapeInner rice）。
    private void DrawRice(float r, Color c)
    {
        float ang = Velocity.LengthSquared() > 0.01f ? Velocity.Angle() : Mathf.Pi / 2f;
        DrawGlow(r * 0.8f, c, 0.8f);
        DrawSetTransform(Vector2.Zero, ang, new Vector2(1.15f, 0.5f));
        DrawCircle(Vector2.Zero, r, c, true, -1f, true);
        DrawCircle(new Vector2(-r * 0.25f, -r * 0.25f), r * 0.4f, new Color(1f, 1f, 1f, 0.7f), true, -1f, true);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private static Vector2[] ScalePts(Vector2[] pts, float s)
    {
        var o = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) o[i] = pts[i] * s;
        return o;
    }
}
