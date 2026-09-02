using Godot;
using System.Collections.Generic;

// ChoiceOverlay : 会話中の2択選択UI（「言いかけの言葉」演出・中央整列版）。
//   旧メニュー箱（フラット暗幕＋UiKit.Box）は使わないが、断片を世界に溶かしすぎて選択の記号性を失った
//   反省（プレイ評: 位置がわかりづらい・文字が小さい）から、断片は**画面中央に大きく縦積み**し、
//   ▸マーカー＋光の下線で「並んだ選択リスト」に見せる。没入は暗幕でなく**周辺減光（ビネット）**で作り、
//   戦場（QuietVeil の鈍色）は見えたまま中央へ視線を集める。Hud(CanvasLayer) にぶら下げて全画面に描く。
//
// 使い方（StageKoharu.Step_MidChoice 参照）── 外部APIは旧版と不変:
//   _choice = ChoiceOverlay.Show(Hud, new[] { "選択肢A", "選択肢B" }, defaultSel: 1);
//   ... _choice.Decided が立ったら _choice.Selected を読み、QueueFree する（後始末は呼び出し側の責務）。
//
// 演出タイムライン（予備動作→本動作→余韻。yoshida style §4）:
//   出現 0〜0.7s … ビネットが0.3sでフェードインし、光の粒が集まりながら1文字ずつ滲んで現れる。完了まで決定不可。
//   待機         … 中央 y≈240/330 に2本整列。漂いは±2px（読みやすさ優先）。
//                  選択中＝明るく・わずかに拡大・▸マーカー＋光の下線＋呼吸の明滅。非選択＝輝度40%。
//   決定 0〜0.5s … 選ばれた断片が吹き出しの方へふわりと昇って光に溶ける（＝彼女の言葉になる連続性）。
//                  選ばれなかった断片は淡い光の粒に散る（浄化と同じ語彙）。ビネットも一緒に明ける。
//                  解散が終わってから Decided を立てる。
//   音           … カーソル移動＝SfxUiMove を小さく低く／決定＝会話送りと同じミナのタイプ音（TypMina）。
//                  メニュー確認音（PlayUiConfirm）は使わない＝これはメニューではなく「言葉を選ぶ」行為。
//
// 操作ヒント: 出現直後から下部に小さく表示（迷わせない）。入力が始まったら薄める（消しはしない）。
// 沈黙も選択: 無入力が14秒続くと「ひきさがる」（末尾の選択肢）がひとりでに柔らかく灯りはじめ、
//   20秒で自動的にそれが選ばれる（演出は通常決定と同じ・少し静かに）。ツリーポーズ中は _Process ごと
//   止まるのでタイマーも停止する。
//
// 仕様（docs/20260831/会話選択_層2_プロト仕様.md）:
//   ・X キャンセルは付けない＝必ずどちらかを選ばせる（収束型2択）。
//   ・パッドは十字↑↓＋A（ui_up/ui_down は十字キーにマップ済み）。
//   ・マウス（Settings/Hub と同じ流儀）: UiKit.BeginHotspots＋Hotspot＋HoveredId＋Pad.MouseClick。
//     断片の行矩形ホバーで選択移動（Pad.UsingMouse 中のみ）、その上で左クリック＝決定。
//     矩形外のクリックは何もしない。0.25s ゲート＝表示直後の押下（会話送りの残り）を拾わない。
//   ・提示中は呼び出し側が会話バブルを保持（HoldBubble）＝Hud.BubblePaused 継続で弾・敵は止まったまま。
//
// 自動プレイ互換（--qa/--demo）: QaPilot/DemoPilot は Hud.BubblePaused 中に Z をパルスし続ける。
//   出現完了（0.7s）までの Z は無視されるが、パルスは続くので直後の1発で確定し、解散演出（0.5s）後に
//   Decided が立って先へ進む＝ソフトロックしない（沈黙タイマーの自動決定はその遥か手前で無関係）。
public partial class ChoiceOverlay : Control
{
    public bool Decided { get; private set; }   // 決定済みか（解散演出が終わってから立つ。立ったら Selected を読む）
    public int Selected { get; private set; }   // 現在カーソル／確定した選択肢の添字

    private string[] _choices = System.Array.Empty<string>();
    private string[] _disp = System.Array.Empty<string>();   // 表示用（鉤括弧付き）。文言そのものは変えない
    private Vector2[] _pos = System.Array.Empty<Vector2>();  // 各断片の基本テキスト左上（設計座標・浮遊前）
    private float[] _w = System.Array.Empty<float>();        // 各断片の基本テキスト幅（FontSize 時）

    private double _t;
    // 生成フレームからの押しっぱなしをエッジ扱いしない（直前の会話送りZで即決させない）。
    private bool _navHeld = true;
    private bool _zHeld = true;

    // 決定後の解散演出（この間に Decided はまだ立てない）。
    private bool _deciding;
    private double _decideT;
    private bool _quiet;          // 沈黙の自動決定＝少し静かに（音を絞り・粒を減らす）
    private float _trailAcc;

    // 操作ヒント（出現直後から表示・入力が始まったら薄める）と沈黙タイマー（14秒で灯り・20秒で自動決定）。
    private bool _inputSeen;
    private float _hintA;
    private double _silenceT;
    private Vector2 _lastMouse;

    // 光の粒（解散の散り・昇りのトレイル）。設計座標で保持・描画。
    private struct Mote { public Vector2 P, V; public float Life, Max, R; public Color C; }
    private readonly List<Mote> _motes = new();
    private readonly RandomNumberGenerator _rng = new();

    // スクショ検証（--shot）時のみ、決定ゲートを 4s に延ばして「提示状態」を撮影可能にする。
    //   --demo の合成Zは 0.3s 周期＝通常は出現直後に即決してしまい、静止画に提示中がほぼ写らないため。
    //   視覚・通常プレイ・QA（--qa は --shot を伴わない）には一切影響しない（Shot.cs と同じ引数検出）。
    private static bool _shotHold;
    private static bool _shotHoldChecked;

    private const float AppearDur = 0.7f;     // 出現（粒の集合＋1文字ずつの滲み）
    private const float ShotHoldGate = 4.0f;  // --shot 時の決定ゲート（撮影窓の確保）
    private const float CharStagger = 0.05f;  // 文字ごとの出現ずらし
    private const float CharFade = 0.15f;     // 1文字の滲み時間
    private const float DissolveDur = 0.5f;   // 決定→解散（この後に Decided）
    private const float VignetteIn = 0.3f;    // ビネットのフェードイン
    private const double SilenceWarm = 14.0;  // ここから「ひきさがる」が灯りはじめる
    private const double SilenceAuto = 20.0;  // 自動決定
    private const int FontSize = 38;          // 会話文（FontHeading=20）の約2倍＝選択肢だと一目で分かる大きさ
    private const float CenterX = UiKit.DesignW * 0.5f;
    private static readonly float[] RowY = { 240f, 330f };  // 2本の縦積み（吹き出し y=520 帯とHUDに被らない）
    private static readonly Vector2 BubbleTarget = new(640f, 535f); // 吹き出し（Hud.DrawDialog: 40,520,1200,170）の上辺中央

    // 周辺減光テクスチャ（中心透明→縁が暗い放射グラデ）。毎フレーム new しない（UiKit._gradCache と同じ理由）。
    private static GradientTexture2D? _vignetteTex;
    // 光の下線テクスチャ（透明→白→透明の横グラデ1枚を使い回し、色・αは modulate で動かす）。
    //   UiKit.HGradient は色をキーにテクスチャをキャッシュするため、呼吸で毎フレーム変わるαを渡すと
    //   キャッシュが際限なく増える＝ここでは使わない（UiKit._gradCache のコメント参照）。
    private static GradientTexture2D? _lineTex;

    public static ChoiceOverlay Show(Node parent, string[] choices, int defaultSel)
    {
        var c = new ChoiceOverlay { Name = "ChoiceOverlay", _choices = choices, Selected = defaultSel };
        parent.AddChild(c);
        return c;
    }

    public override void _Ready()
    {
        // 実画面(384x216)全域に重ねる。描画は UiKit.BeginDesign で設計座標(1280x720)に変換して行う。
        Size = new Vector2(384f, 216f);
        MouseFilter = MouseFilterEnum.Ignore;
        _rng.Randomize();
        _lastMouse = Pad.MousePos();
        if (!_shotHoldChecked)
        {
            _shotHoldChecked = true;
            foreach (var a in OS.GetCmdlineUserArgs())
                if (a == "--shot") { _shotHold = true; break; }
        }

        int n = _choices.Length;
        _disp = new string[n]; _pos = new Vector2[n]; _w = new float[n];
        for (int i = 0; i < n; i++)
        {
            _disp[i] = "「" + _choices[i] + "」";
            _w[i] = UiKit.TextW(UiKit.ZenBold, _disp[i], FontSize);
            // 画面中央に中央揃えで縦積み。3本以上は下へ 90px 刻みで続ける（現行は2本）。
            float y = i < RowY.Length ? RowY[i] : RowY[RowY.Length - 1] + 90f * (i - RowY.Length + 1);
            _pos[i] = new Vector2(CenterX - _w[i] * 0.5f, y);
        }
    }

    public override void _Process(double delta)
    {
        _t += delta;
        QueueRedraw();
        if (Decided) return;
        float dt = (float)delta;
        UpdateMotes(dt);

        // ── 解散演出中：入力は受けず、選ばれた断片のトレイルを撒きながら完了を待つ ──
        if (_deciding)
        {
            _decideT += delta;
            _trailAcc += dt;
            float step = _quiet ? 0.05f : 0.025f;
            while (_trailAcc >= step) { _trailAcc -= step; SpawnTrail(); }
            if (_decideT >= DissolveDur) Decided = true; // 解散が終わってから決定を通知（連続性の担保）
            return;
        }

        // 出現完了までは決定を受け付けない（アンティシペーション）。--shot 検証時のみ撮影窓ぶん延長。
        bool appeared = _t >= (_shotHold ? ShotHoldGate : AppearDur);

        // ↑↓（十字含む）で2択トグル。移動音は小さく柔らかく（SfxUiMove を絞って低く）。
        bool nav = Input.IsActionPressed("ui_up") || Input.IsActionPressed("ui_down");
        if (nav && !_navHeld)
        {
            Selected = (Selected + 1) % _choices.Length;
            if (Audio.Instance is { } au1) au1.Se(au1.SfxUiMove, volDb: -27f, pitch: 0.8f);
        }
        _navHeld = nav;

        // マウス：断片の行矩形をホットスポット登録し、ホバーで選択追従（Pad.UsingMouse 中のみ）。
        //   座標系は RowRect＝設計座標(1280×720)で、Pad.MousePos() も設計座標を返すため換算不要。
        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < _choices.Length; i++)
            UiKit.Hotspot(RowRect(i), i);
        int hov = UiKit.HoveredId();
        if (Pad.UsingMouse && hov >= 0 && hov != Selected)
        {
            Selected = hov;
            if (Audio.Instance is { } au2) au2.Se(au2.SfxUiMove, volDb: -27f, pitch: 0.8f);
        }
        // 断片の上で左クリック＝決定。外のクリックは何もしない（誤爆防止）。0.25s ゲート維持。
        bool click = Pad.MouseClick();
        if (click && hov >= 0 && _t >= 0.25 && appeared)
        {
            Selected = hov;
            StartDissolve(quiet: false);
            return;
        }

        // Z/Enter/Pad A で決定（会話送りと違いマウス左クリックは含めない＝上の矩形クリック経路に分離）。
        // X キャンセルは意図的に無し（必ず選ばせる）。ポーズ中はツリーポーズで本 _Process ごと止まる。
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter)
                 || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;
        if (zEdge && _t >= 0.25 && appeared)
        {
            StartDissolve(quiet: false);
            return;
        }

        // ── 操作ヒント（出現直後から表示・入力が始まったら薄める）と沈黙タイマー ──
        var mp = Pad.MousePos();
        bool mouseMoved = (mp - _lastMouse).Length() > 6f;
        _lastMouse = mp;
        if (nav || z || click || mouseMoved) { _inputSeen = true; _silenceT = 0; }
        else _silenceT += delta;
        float target = (_inputSeen ? 0.55f : 1f) * Mathf.Clamp((float)_t / VignetteIn, 0f, 1f);
        _hintA = Mathf.MoveToward(_hintA, target, dt * 3f);

        // 沈黙も選択：20秒で「ひきさがる」（末尾）を自動決定。演出は通常決定と同じ・少し静かに。
        if (_silenceT >= SilenceAuto)
        {
            Selected = _choices.Length - 1;
            StartDissolve(quiet: true);
        }
    }

    // ── 決定→解散を開始。決定音は会話送りと同じミナのタイプ音（メニュー確認音は使わない）──
    private void StartDissolve(bool quiet)
    {
        _deciding = true;
        _decideT = 0;
        _quiet = quiet;
        _trailAcc = 0;
        if (Audio.Instance is { } au)
        {
            if (quiet) au.VoiceSe(au.TypMina, volDb: -26f);       // 沈黙の自動決定＝さらに静かに
            else au.PlayType(Hud.LineKind.Mina);                  // 会話送り（ミナの声＝ガラス）と同じ音
        }
        // 選ばれなかった断片＝淡い光の粒に散る（浄化と同じ語彙）。粒はすべて DissolveDur 内に消える寿命。
        int per = quiet ? 1 : 2;
        for (int i = 0; i < _choices.Length; i++)
        {
            if (i == Selected) continue;
            var basePos = FragBasePos(i);
            for (int j = 0; j < _disp[i].Length; j++)
            {
                float cx = basePos.X + UiKit.TextW(UiKit.ZenBold, _disp[i].Substring(0, j), FontSize) + FontSize * 0.45f;
                for (int m = 0; m < per; m++)
                {
                    float ang = _rng.RandfRange(-Mathf.Pi * 0.85f, -Mathf.Pi * 0.15f); // 上方向へ散る
                    float spd = _rng.RandfRange(26f, 70f);
                    _motes.Add(new Mote
                    {
                        P = new Vector2(cx, basePos.Y + FontSize * 0.6f),
                        V = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd,
                        Life = _rng.RandfRange(0.28f, 0.46f),
                        Max = 0.46f,
                        R = _rng.RandfRange(1.8f, 3.8f),
                        C = UiKit.PurifyHi.Lerp(UiKit.Mina, _rng.Randf() * 0.5f),
                    });
                }
            }
        }
    }

    // 選ばれた断片の昇りに沿って光の粒を1つ撒く（トレイル＝言葉が光へほどけていく余韻）。
    private void SpawnTrail()
    {
        float d = Mathf.Clamp((float)(_decideT / DissolveDur), 0f, 1f);
        var p = ChosenPos(d);
        _motes.Add(new Mote
        {
            P = p + new Vector2(_rng.RandfRange(0f, _w[Selected]), _rng.RandfRange(8f, FontSize * 1.1f)),
            V = new Vector2(_rng.RandfRange(-8f, 8f), _rng.RandfRange(-24f, -10f)),
            Life = _rng.RandfRange(0.20f, 0.34f),
            Max = 0.34f,
            R = _rng.RandfRange(1.6f, 3.0f),
            C = UiKit.PurifyHi,
        });
    }

    private void UpdateMotes(float dt)
    {
        for (int k = _motes.Count - 1; k >= 0; k--)
        {
            var m = _motes[k];
            m.Life -= dt;
            if (m.Life <= 0f) { _motes.RemoveAt(k); continue; }
            m.V *= Mathf.Exp(-2.6f * dt);   // 減衰
            m.V.Y -= 18f * dt;              // 光はゆっくり浮き上がる（浄化の語彙）
            m.P += m.V * dt;
            _motes[k] = m;
        }
    }

    // 断片の行矩形（設計座標）。▸マーカー〜下線まで含む大きめの帯。_Draw と _Process のホットスポット
    // 登録で共有＝座標系ずれを防ぐ。浮遊（±2px）はパディング内に収まるので矩形は固定でよい。
    private Rect2 RowRect(int i)
        => new(_pos[i].X - 56f, _pos[i].Y - 12f, _w[i] + 88f, FontSize * 1.5f + 20f);

    // 浮遊込みの基本位置（静かな上下ドリフト。読みやすさ優先で ±2px・周期≒3.3s）。
    private Vector2 FragBasePos(int i)
        => _pos[i] + new Vector2(0f, 2f * Mathf.Sin((float)_t * 1.9f + i * 2.6f));

    // 決定後の選ばれた断片の位置：ふわりと持ち上がり（+30px の山）、吹き出しの方へ滑らかに寄りながら溶ける。
    // 全行程は移動しきらず 55% 付近で光に溶け切る＝「届く途中で言葉になる」余韻。
    private Vector2 ChosenPos(float d)
    {
        var p = FragBasePos(Selected);
        var target = new Vector2(BubbleTarget.X - _w[Selected] * 0.5f, BubbleTarget.Y);
        float k = d * d; // ease-in：ゆっくり離れて、光に吸われるように加速
        p = p.Lerp(target, k * 0.55f);
        p.Y -= 30f * Mathf.Sin(Mathf.Min(1f, d * 1.3f) * Mathf.Pi);
        return p;
    }

    // 出現の進捗：文字 j の滲みα（0=未出現〜1=定着）。
    private float CharAppear(int j)
        => Mathf.Clamp(((float)_t - (0.06f + j * CharStagger)) / CharFade, 0f, 1f);

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        float dis = _deciding ? Mathf.Clamp((float)(_decideT / DissolveDur), 0f, 1f) : 0f;

        // ── 周辺減光（ビネット）：フラット暗幕の代わり。戦場は見えたまま中央へ視線を集める。──
        //   出現時 0.3s でフェードイン、決定/解散時は断片と一緒に明ける。テクスチャは1枚を使い回し
        //   （毎フレーム new は RID 競合の実績あり＝UiKit._gradCache コメント参照）、αは modulate で動かす。
        _vignetteTex ??= new GradientTexture2D
        {
            Gradient = new Gradient
            {
                Offsets = new[] { 0.40f, 1f },
                Colors = new[] { new Color(0, 0, 0, 0), new Color(0, 0, 0, 0.72f) },
            },
            Width = 256, Height = 256,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
        };
        float vigA = Mathf.Clamp((float)_t / VignetteIn, 0f, 1f) * (1f - dis);
        if (vigA > 0.01f)
            DrawTextureRect(_vignetteTex, new Rect2(0, 0, UiKit.DesignW, UiKit.DesignH), false, new Color(1, 1, 1, vigA));

        float breath = 0.5f + 0.5f * Mathf.Sin((float)_t * 2.5f);
        for (int i = 0; i < _choices.Length; i++)
        {
            bool sel = i == Selected;
            bool chosen = _deciding && sel;
            bool dropped = _deciding && !sel;

            // 全体α
            float alpha = 1f;
            if (chosen) alpha = 1f - Mathf.SmoothStep(0.25f, 1f, dis);            // 後半で光に溶ける
            if (dropped) alpha = Mathf.Clamp(1f - (float)_decideT / 0.22f, 0f, 1f); // 散る側は素早く淡く（粒が引き継ぐ）
            if (alpha <= 0f) continue;

            // 色：ミナの台詞色。選択中は明るく＋呼吸のようにゆっくり明滅、非選択は輝度40%へ沈む。
            Color col = sel ? UiKit.Mina.Lerp(UiKit.White, 0.40f + 0.20f * breath)
                            : UiKit.Mina.Darkened(0.60f);
            // 沈黙の灯り：14秒から末尾の断片（ひきさがる）がひとりでに柔らかく灯りはじめる。
            float warm = 0f;
            if (!sel && !_deciding && i == _choices.Length - 1 && _silenceT > SilenceWarm)
            {
                warm = Mathf.Clamp((float)((_silenceT - SilenceWarm) / (SilenceAuto - SilenceWarm)), 0f, 1f);
                col = col.Lerp(UiKit.Light, warm * 0.55f);
            }

            // サイズ：選択中はわずかに拡大。選ばれた断片は昇りながらさらに少し伸びる（光に近づく）。
            int size = sel ? FontSize + 2 : FontSize;
            if (chosen) size = FontSize + 2 + (int)(4f * dis);
            float wNow = UiKit.TextW(UiKit.ZenBold, _disp[i], size);
            // 位置：中央揃え（サイズが変わっても中心を保つ）。決定中の選ばれた断片だけ昇りの軌道へ。
            float bobY = FragBasePos(i).Y;
            Vector2 p = chosen ? ChosenPos(dis) : new Vector2(CenterX - wNow * 0.5f, bobY);
            var center = new Vector2(p.X + wNow * 0.5f, p.Y + size * 0.62f);

            // やわらかい発光（気配）。選択中は呼吸、決定中は溶ける光へ膨らむ。
            float appearK = Mathf.Clamp((float)_t / AppearDur, 0f, 1f);
            float glowA = sel ? 0.10f + 0.05f * breath : 0.05f;
            if (chosen) glowA = 0.10f + 0.30f * dis;
            UiKit.RadialGlow(this, center, wNow * 0.7f + 40f, UiKit.Mina, glowA * alpha * appearK);
            if (warm > 0f)
                UiKit.RadialGlow(this, center, wNow * 0.6f, UiKit.Light, 0.10f * warm);

            // 選択の記号性：▸マーカー（選択中のみ）＋テキスト下の光のライン（非選択も薄く＝リストに見せる）。
            float lineY = p.Y + size * 1.32f;
            float lineA = (sel ? 0.55f + 0.20f * breath : 0.12f) * alpha * appearK;
            var lineCol = sel ? UiKit.Mina.Lerp(UiKit.White, 0.35f) : UiKit.Mina;
            float half = wNow * 0.5f + 18f;
            _lineTex ??= new GradientTexture2D
            {
                Gradient = new Gradient
                {
                    Offsets = new[] { 0f, 0.5f, 1f },
                    Colors = new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) },
                },
                Width = 256, Height = 8,
                Fill = GradientTexture2D.FillEnum.Linear,
                FillFrom = Vector2.Zero, FillTo = new Vector2(1f, 0f),
            };
            DrawTextureRect(_lineTex, new Rect2(center.X - half, lineY, half * 2f, 2.5f), false,
                new Color(lineCol, lineA));
            if (sel && !dropped)
            {
                float mk = (chosen ? alpha : 1f) * appearK;
                UiKit.Text(this, UiKit.ZenBold, new Vector2(p.X - 45f, p.Y + 3.5f), "▸", size - 6,
                    new Color(0f, 0f, 0f, 0.6f * mk)); // 影
                UiKit.Text(this, UiKit.ZenBold, new Vector2(p.X - 46f, p.Y + 2f), "▸", size - 6,
                    new Color(UiKit.PurifyHi, (0.80f + 0.20f * breath) * mk));
            }

            // 1文字ずつ：出現中は光の粒が集まりながら滲む。定着後は縁取り＋本体（箱なしでも太く読める）。
            for (int j = 0; j < _disp[i].Length; j++)
            {
                float a = CharAppear(j);
                float cx = p.X + UiKit.TextW(UiKit.ZenBold, _disp[i].Substring(0, j), size);
                var cc = new Vector2(cx + size * 0.45f, p.Y + size * 0.6f);

                // 集まる粒：文字の定着前後だけ、周囲から渦を巻いて寄ってくる（決定論ハッシュ＝ちらつかない）。
                if (a < 1f && _t < AppearDur + 0.2)
                {
                    float pre = Mathf.Clamp(((float)_t - (0.06f + j * CharStagger - 0.16f)) / 0.16f, 0f, 1f);
                    for (int m = 0; m < 3; m++)
                    {
                        float h1 = Hash(i * 131 + j * 17 + m * 7);
                        float h2 = Hash(i * 57 + j * 29 + m * 13 + 999);
                        float ang = h1 * Mathf.Tau + (float)_t * (0.6f + h2);
                        float rad = (1f - a) * (20f + 16f * h2) + 3f;
                        var mpnt = cc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                        float ma = pre * (1f - a) * 0.6f;
                        if (ma > 0.01f)
                            DrawCircle(mpnt, 2.0f, new Color(UiKit.PurifyHi, ma * alpha));
                    }
                }
                if (a <= 0f) continue;

                string ch = _disp[i][j].ToString();
                float ca = a * a * alpha; // 滲み＝ゆっくり濃くなる
                // 濃い縁取り（4方向）＋落ち影：QuietVeil の上でも太く読める（ビネットと合わせ可読性を担保）。
                var ink = new Color(0f, 0f, 0f, 0.75f * ca);
                UiKit.Text(this, UiKit.ZenBold, new Vector2(cx - 1.5f, p.Y), ch, size, ink);
                UiKit.Text(this, UiKit.ZenBold, new Vector2(cx + 1.5f, p.Y), ch, size, ink);
                UiKit.Text(this, UiKit.ZenBold, new Vector2(cx, p.Y - 1.5f), ch, size, ink);
                UiKit.Text(this, UiKit.ZenBold, new Vector2(cx, p.Y + 1.5f), ch, size, ink);
                UiKit.Text(this, UiKit.ZenBold, new Vector2(cx + 2.2f, p.Y + 2.2f), ch, size,
                    new Color(0f, 0f, 0f, 0.45f * ca));
                UiKit.Text(this, UiKit.ZenBold, new Vector2(cx, p.Y), ch, size, new Color(col, ca));
            }
        }

        // 光の粒（散り・トレイル）
        foreach (var m in _motes)
        {
            float ma = Mathf.Clamp(m.Life / m.Max, 0f, 1f) * 0.8f;
            DrawCircle(m.P, m.R, new Color(m.C, ma));
        }

        // 操作ヒント：出現直後から小さく表示（入力が始まったら薄める）。
        //   位置は吹き出し（y=520〜690）の直上＝最下部ティッカー帯（y≈696〜）と重ねない。
        //   背景（棚のシルエット等）の上でも読めるよう薄い落ち影を敷く。
        if (_hintA > 0.01f && !_deciding)
        {
            string hint = "↑↓ / マウス えらぶ　" + Pad.ConfirmToken + " けってい";
            UiKit.Text(this, UiKit.Mono, new Vector2(1.2f, 465.2f), hint, UiKit.FontSmall,
                new Color(0f, 0f, 0f, 0.6f * _hintA), HorizontalAlignment.Center, UiKit.DesignW);
            UiKit.Text(this, UiKit.Mono, new Vector2(0f, 464f), hint, UiKit.FontSmall,
                new Color(UiKit.Text2, 0.95f * _hintA), HorizontalAlignment.Center, UiKit.DesignW);
        }

        UiKit.EndDesign(this);
    }

    // 決定論ハッシュ（0〜1）。出現の集合粒を毎フレーム同じ軌道で描くために使う。
    private static float Hash(int a)
    {
        unchecked
        {
            uint x = (uint)a * 2654435761u;
            x ^= x >> 13; x *= 1274126177u; x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777215f;
        }
    }
}
