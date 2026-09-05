using Godot;

// WorldGrade : ステージ進行度(StageProgress 0..1)に応じて「汚染→浄化」を4段階でくっきり切り替える
//   フルスクリーンの色グレーディング層（方式B＝背景テクスチャは差し替えず、色・光の節目を上に足す）。
//
//   既存の連続 Tint（CanvasModulate Cold→Warm, Warmth 駆動）と StageImagery のフェードとは役割が別：
//   あちらは「滑らかに世界が暖まる連続変化」、こちらは「空気が変わる“節目”＝くっきりした段階切替」。
//   二重に暗くならないよう blend を段階で分ける：
//     段階0 汚染 …… Normal で寒色の濁りを乗せる（色が死ぬ）
//     段階1〜3 ……… Add で暖色の光を差す（ひび割れの光→晴れ→満ちる光）
//   → Add は暗部だけ持ち上げるので既存の乗算的 Tint と喧嘩しない。段階0の Normal 濁りだけ端αを抑える。
//
//   ┌ 4段階（桜井が閾値確定）───────────────────────────────
//   │ 0 汚染   0.00–0.25  濁った空・色が死んでいる（冷たい）      Normal 藍鼠      端α0.16
//   │ 1 揺らぎ 0.25–0.55  ひび割れから光が差し始める              Add    淡い金    端α0.10
//   │ 2 兆し   0.55–0.82  色が戻り、空が晴れていく                Add    淡い陽    端α0.13
//   │ 3 浄化   0.82–1.00  暖かい光に満ちる（ボス直前で晴れる）    Add    琥珀の光  端α0.16
//   └───────────────────────────────────────────
//   ・段階間はクロスフェード（0.6s で lerp）。ヒステリシス±0.03で閾値付近のチラつきを止める。
//   ・弾（濃ピンク/黒グリフ）の視認最優先：中央(±ClearR)は端αの ~0.35 倍まで薄める（MurkVignette と同じラジアル思想）。
//   ・左右ヒント：右端寄りに薄い赤み（危険）、左端寄りに薄い青み（落ち着き）。nx連動・端1/4のみ・α≤0.10。
//
//   ZIndex は StageImagery(-50) の上・MurkVignette(-45)/弾(0..) の奥（-48/-47）。中央は薄いので弾を隠さない。
//   Player を group("player") から引いて nx=clamp(x/384,0,1) を算出。GameManager.PlayerNormX が公開されたら
//   そちらを優先する（参照は Nx プロパティ1箇所に集約）。
public partial class WorldGrade : Node2D
{
    private const float W = 384f, H = 216f;
    private static readonly Vector2 Center = new Vector2(W * 0.5f, H * 0.5f);
    private const float ClearR = 60f;   // ここより内側は端αの CenterMul 倍（中央の弾を隠さない）
    private const float FullR = 236f;   // ここより外で端αに達する（画面端）
    private const float CenterMul = 0.35f;

    // 4段階の overlay。Add=true は加算合成（光を差す）、false は通常合成（濁りを乗せる）。
    // Col は端で乗せる色、EdgeA はその端での上限α（中央は EdgeA*CenterMul まで落とす）。
    private struct Grade { public Color Col; public float EdgeA; public bool Add; }
    private static readonly Grade[] Grades =
    {
        // 0 汚染：沈んだ藍鼠を Normal で乗せ、彩度と明度を落として“死んだ色”に。MurkTint より一段暗い藍。
        new Grade { Col = new Color(0.28f, 0.30f, 0.42f), EdgeA = 0.16f, Add = false },
        // 1 揺らぎ：ごく淡い金を Add。暗部だけ持ち上がる＝ひび割れから差す弱い光。
        new Grade { Col = new Color(0.16f, 0.15f, 0.10f), EdgeA = 0.10f, Add = true },
        // 2 兆し：淡い陽を Add。暖色相へ寄せて全体が明るく色づく＝空が晴れていく。
        new Grade { Col = new Color(0.20f, 0.19f, 0.13f), EdgeA = 0.13f, Add = true },
        // 3 浄化：夜明けの琥珀を Add（StageImagery 反転の warm と同系）。ふわっと光が満ちる。中央も少し明るく。
        new Grade { Col = new Color(0.26f, 0.22f, 0.14f), EdgeA = 0.16f, Add = true },
    };

    // 段階の境界（上げ側の閾値）。ヒステリシス用に ±Hyst で判定する。
    private static readonly float[] Thresholds = { 0.25f, 0.55f, 0.82f };
    private const float Hyst = 0.03f;
    private const float FadeSpeed = 1f / 0.6f; // クロスフェード 0.6s（段階の重みが 0→1 に動く速さ）

    private int _stage;            // 現在の（確定した）段階 0..3
    private float _blend;          // 前段階→現段階のクロスフェード（0=前段階のまま, 1=現段階に到達）
    private int _prevStage;        // クロスフェード元の段階

    // 左右ヒント（弾の視認を侵さない薄さ）。中央帯には出さず端1/4だけに滲ませる。
    private static readonly Color HintRight = new Color(0.85f, 0.35f, 0.32f); // 右＝危険（薄い赤み）
    private static readonly Color HintLeft = new Color(0.40f, 0.55f, 0.85f);  // 左＝落ち着き（薄い青み）
    private float _nx = 0.5f;      // なめらかに追従したプレイヤーの正規化X

    private AddLayer _addLayer = null!;

    public override void _Ready()
    {
        ZIndex = -47;                 // StageImagery(-50)/ScrollFx の上・MurkVignette(-45)/弾(0..) の奥
        ZAsRelative = false;
        AddToGroup("worldgrade");

        // 加算合成の段階（1〜3の光）は専用の子ノードに載せる。DrawSetBlendMode は _Draw に無いので
        // ノードに CanvasItemMaterial{Add} を付けてこの子の描画だけ加算にする（親の Normal と分離）。
        _addLayer = new AddLayer
        {
            Name = "Add",
            Host = this,
            ZIndex = -48,             // Normal 層(-47)の直下（順序は α なので視覚上は問題なし）
            ZAsRelative = false,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        AddChild(_addLayer);

        // 開始段階を進行度から確定（フェードなしで即座に合わせる）。
        float p = Progress;
        _stage = StageFromProgress(p, 0);
        _prevStage = _stage;
        _blend = 1f;
    }

    private float Progress => Mathf.Clamp(GetNodeOrNull<GameManager>("/root/Game")?.StageProgress ?? 0f, 0f, 1f);

    // プレイヤーの正規化X（0=左端/1=右端）。GameManager.PlayerNormX が公開されていればそれを使い、
    // 無ければ Player を group から引いて x/W で算出（参照はここ1箇所に集約）。
    private float Nx => BgScroll.PlayerNx(this);

    // ボス突入中に加算の光を弱める係数（1=道中のまま / 0.5=ボスで半減）。
    //   層背景(BgLayers)の暗転は L1〜L3 を 0.45 倍に落とすが、浄化が進んだ面では段階3の琥珀の加算光が
    //   その上に乗って暗転を打ち消してしまい「空気が変わった」と見えなかった（あかり面のスクショで確認）。
    //   ボスの暗転しきり(BgLayers.BossDimK)に合わせて加算αを 1→0.5 へ落とし、暗転を相殺しない。
    //   Brighten の面（レイ）は BgLayers 側が層セットごと金の光を足すので、半減してもそちらが勝つ。
    private const float BossAddMul = 0.5f;
    private float _addMul = 1f;   // 実際に掛ける係数（_Process で BossDimK から更新）

    // ヒステリシス付きで進行度→段階を決める。cur は現在段階（境界近くの揺れを cur 側に寄せる）。
    private static int StageFromProgress(float p, int cur)
    {
        int s = 0;
        for (int i = 0; i < Thresholds.Length; i++)
        {
            // 上げる時は閾値+Hyst を超えたら、下げる時は閾値-Hyst を割ったら段階を跨ぐ。
            // 進行度は基本単調増加なので主に「境界での揺れ」対策。
            float th = Thresholds[i] + (cur > i ? -Hyst : Hyst);
            if (p >= th) s = i + 1;
        }
        return s;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // 段階判定（ヒステリシス）。段階が変わったらクロスフェードを開始（今の見た目を prev に固定）。
        int target = StageFromProgress(Progress, _stage);
        if (target != _stage)
        {
            _prevStage = _stage;
            _stage = target;
            _blend = 0f;
        }
        if (_blend < 1f) _blend = Mathf.MoveToward(_blend, 1f, dt * FadeSpeed);

        // ボス突入の暗転に合わせて加算の光を弱める（層背景を敷いている面だけ。1枚絵の面は常に 1）。
        var bgLayers = GetTree().GetFirstNodeInGroup("bglayers") as BgLayers;
        _addMul = bgLayers == null ? 1f : Mathf.Lerp(1f, BossAddMul, bgLayers.BossDimK);

        // プレイヤー位置はなめらかに追従（急な左右移動でヒントがちらつかない）。
        _nx = Mathf.Lerp(_nx, Nx, 1f - Mathf.Exp(-8f * dt));

        QueueRedraw();
        _addLayer.QueueRedraw();
    }

    // ── Normal 層（この _Draw）：段階0の濁り＋左右ヒント。加算の光(1〜3)は _addLayer が描く。──
    public override void _Draw()
    {
        // 段階0（Add=false）の重みを算出：現段階か前段階が0のとき、そのフェード重みで濁りを乗せる。
        // Normal 層はこの WorldGrade 自身が draw コンテキスト＝target に this を渡す。
        float murk = GradeWeightNormal();
        if (murk > 0.001f)
            DrawRadialOverlay(this, Grades[0].Col, Grades[0].EdgeA * murk);

        DrawHints();
    }

    // 段階0（Normal 濁り）の現在の重み。前段階と現段階のうち Add=false のものの寄与を合成。
    private float GradeWeightNormal()
    {
        float w = 0f;
        if (!Grades[_prevStage].Add) w += (1f - _blend) * (_prevStage == 0 ? 1f : 0f);
        if (!Grades[_stage].Add) w += _blend * (_stage == 0 ? 1f : 0f);
        return w;
    }

    // 左右ヒント：右端寄りに薄い赤み・左端寄りに薄い青み。端1/4だけに縦帯グラデで滲ませ、中央には出さない。
    private void DrawHints()
    {
        // 右（危険）：nx>0.6 で滲み始め右端で最大 0.10。画面右 1/4 に、右端ほど濃い横グラデ。
        float rA = 0.10f * Smooth(0.6f, 1.0f, _nx);
        if (rA > 0.002f)
        {
            const float band = W * 0.25f;          // 右 1/4
            const int steps = 6;
            for (int i = 0; i < steps; i++)
            {
                float t0 = (float)i / steps;        // 0(内)..1(端)
                float x0 = W - band + band * t0;
                float a = rA * t0;                  // 内側は0・右端で最大
                if (a <= 0.002f) continue;
                DrawRect(new Rect2(x0, 0f, band / steps + 1f, H), new Color(HintRight, a));
            }
        }
        // 左（落ち着き）：nx<0.4 で滲み始め左端で最大 0.08。画面左 1/4 に、左端ほど濃い横グラデ。
        float lA = 0.08f * Smooth(0.4f, 0.0f, _nx);
        if (lA > 0.002f)
        {
            const float band = W * 0.25f;          // 左 1/4
            const int steps = 6;
            for (int i = 0; i < steps; i++)
            {
                float t0 = (float)i / steps;        // 0(内)..1(端)
                float x0 = band - band * t0 - band / steps;
                float a = lA * t0;                  // 内側は0・左端で最大
                if (a <= 0.002f) continue;
                DrawRect(new Rect2(Mathf.Max(0f, x0), 0f, band / steps + 1f, H), new Color(HintLeft, a));
            }
        }
    }

    // 中央を薄く抜いたラジアル矩形リングで overlay を乗せる（MurkVignette と同じ同心リング法）。
    // 中央(±ClearR)は EdgeA*CenterMul、端(FullR)で EdgeA。col の α は既に段階の重みを掛けた実効値。
    //   ★描画コマンドは「いま _draw() が開いている CanvasItem」に発行しなければならない。Normal 層は
    //   WorldGrade 自身、Add 層は AddLayer 自身が draw コンテキストなので、target を引数で受けて
    //   target.DrawRect/DrawRing に発行する（親のメソッドとして描くと Drawing is only allowed... で拒否される）。
    private static void DrawRadialOverlay(CanvasItem target, Color col, float edgeA)
    {
        if (edgeA <= 0.001f) return;
        const int rings = 12;
        for (int i = 0; i < rings; i++)
        {
            float t0 = (float)i / rings;
            float t1 = (float)(i + 1) / rings;
            float r0 = Mathf.Lerp(ClearR, FullR, t0);
            float r1 = Mathf.Lerp(ClearR, FullR, t1);
            // 中央から端へ CenterMul→1.0 で濃くなる。内側リングほど薄い。
            float a = edgeA * Mathf.Lerp(CenterMul, 1f, t1);
            if (a <= 0.001f) continue;
            DrawRing(target, r0, r1, new Color(col.R, col.G, col.B, a));
        }
        // 中央のコア（±ClearR 内）も CenterMul で薄く塗る＝段階3の「浄化された明るさ」を中央にも少し届ける。
        float ca = edgeA * CenterMul;
        if (ca > 0.001f)
            target.DrawRect(new Rect2(Center.X - ClearR, Center.Y - ClearR, ClearR * 2f, ClearR * 2f),
                new Color(col.R, col.G, col.B, ca));
    }

    // 半径 r0..r1 の正方形リング（4枚の矩形）を Center 中心に描く（中央±r0 は抜ける）。MurkVignette と同法。
    //   target ＝ いま _draw() が開いている CanvasItem（WorldGrade 自身 or AddLayer）。
    private static void DrawRing(CanvasItem target, float r0, float r1, Color col)
    {
        float l0 = Center.X - r0, r0x = Center.X + r0, t0 = Center.Y - r0, b0 = Center.Y + r0;
        float l1 = Center.X - r1, r1x = Center.X + r1, t1 = Center.Y - r1, b1 = Center.Y + r1;
        target.DrawRect(new Rect2(l1, t1, r1x - l1, t0 - t1), col);   // 上
        target.DrawRect(new Rect2(l1, b0, r1x - l1, b1 - b0), col);   // 下
        target.DrawRect(new Rect2(l1, t0, l0 - l1, b0 - t0), col);    // 左
        target.DrawRect(new Rect2(r0x, t0, r1x - r0x, b0 - t0), col); // 右
    }

    private static float Smooth(float e0, float e1, float x)
    {
        float t = Mathf.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // 加算合成の段階（1〜3の光）だけをこの子ノードで描く（親の Normal と分離）。
    public partial class AddLayer : Node2D
    {
        public WorldGrade Host = null!;

        public override void _Draw()
        {
            if (Host == null) return;
            // 前段階・現段階のうち Add=true のものを、それぞれの重みで加算する（クロスフェード）。
            // Add 層は「この AddLayer 自身」が draw コンテキスト＝target に this を渡す（親に描くと拒否される）。
            int ps = Host._prevStage, cs = Host._stage;
            float b = Host._blend;
            float m = Host._addMul;   // ボス中は加算の光を弱める（層背景の暗転を相殺しない）
            if (Grades[ps].Add)
            {
                float w = 1f - b;
                if (w > 0.001f) DrawRadialOverlay(this, Grades[ps].Col, Grades[ps].EdgeA * w * m);
            }
            if (Grades[cs].Add && cs != ps)
            {
                if (b > 0.001f) DrawRadialOverlay(this, Grades[cs].Col, Grades[cs].EdgeA * b * m);
            }
            else if (Grades[cs].Add && cs == ps)
            {
                // フェード完了後（prev==cur）は現段階を全重みで（初期化直後・定常状態）。
                DrawRadialOverlay(this, Grades[cs].Col, Grades[cs].EdgeA * m);
            }
        }
    }
}
