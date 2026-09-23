using Godot;
using System.Collections.Generic;

// Callers hold the dialogue until Decided, then read Selected and free the overlay.
public partial class ChoiceOverlay : Control
{
    public bool Decided { get; private set; }   // 決定済みか（解散演出が終わってから立つ。立ったら Selected を読む）
    public int Selected { get; private set; }   // 現在カーソル／確定した選択肢の添字
    private bool _cinematic;

    private string[] _choices = System.Array.Empty<string>();
    private string[] _disp = System.Array.Empty<string>();   // 表示用（Quoted 済み）。文言そのものは変えない
    private Rect2[] _rows = System.Array.Empty<Rect2>();
    private float[] _focus = System.Array.Empty<float>();
    private FontFile _font = null!;
    private int _fontSize;

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

    private const float AppearDur = 0.7f;
    private const float ShotHoldGate = 4.0f;  // --shot 時の決定ゲート（撮影窓の確保）
    private const float DissolveDur = 0.5f;   // 決定→解散（この後に Decided）
    private const float VignetteIn = 0.3f;    // ビネットのフェードイン
    private const double SilenceWarm = 14.0;  // ここから「ひきさがる」が灯りはじめる
    private const double SilenceAuto = 20.0;  // 自動決定
    private const int MaxChoices = 4;         // 想定する上限（これを超えると下が吹き出しに掛かる）

    // On-board choices must stay clear of the permanent left HUD.
    private float _centerX = UiKit.DesignW * 0.5f;
    private float _fieldLeft;
    private float _fieldWidth = UiKit.DesignW;
    private static GradientTexture2D? _lineTex;
    private static GradientTexture2D? _shadeTex;
    private static readonly Color Ink = new("16171f");
    private static readonly Color Paper = new("fff9f3");
    private static readonly Color Muted = new("d3d9df");
    private static readonly Color Ice = new("b7e5e8");
    private static readonly Color Rose = new("f2b9ad");

    // N択（2〜MaxChoices）を出す。choices の並びがそのまま上から下の並びで、Selected はその添字。
    // 沈黙の自動決定は常に**末尾**が選ばれる＝呼び出し側は「引き下がる/何もしない」側を最後に置くこと。
    //
    // onBoard: true＝盤面のある画面（道中の会話。Hud にぶら下げる呼び出し）＝盤面(Field)の中心へ出す。
    //          false＝盤面の無い画面（Prologue/Final/Epilogue のカットシーン）＝画面全体の中心へ出す。
    //          既定を false にしてあるのは、カットシーン側が「従来どおり」で通るようにするため。
    public static ChoiceOverlay Show(Node parent, string[] choices, int defaultSel, bool onBoard = false, bool cinematic = false)
    {
        if (choices.Length > MaxChoices)
            GD.PushWarning($"[ChoiceOverlay] {choices.Length} 択は想定外（上限 {MaxChoices}）。下の行が吹き出しに掛かる。");
        var c = new ChoiceOverlay
        {
            Name = "ChoiceOverlay",
            _choices = choices,
            Selected = Mathf.Clamp(defaultSel, 0, Mathf.Max(0, choices.Length - 1)),
            _centerX = onBoard ? Field.DCenterX : UiKit.DesignW * 0.5f,
            _fieldLeft = onBoard ? Field.DLeft : 0f,
            _fieldWidth = onBoard ? Field.DWidth : UiKit.DesignW,
            _cinematic = cinematic,
        };
        parent.AddChild(c);
        return c;
    }

    public override void _Ready()
    {
        // 実画面(384x216)全域に重ねる。描画は UiKit.BeginDesign で設計座標(1280x720)に変換して行う。
        Size = new Vector2(384f, 216f);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;
        _font = (FontFile)GD.Load<FontFile>("res://assets/fonts/ShipporiMincho-SemiBold.ttf").Duplicate();
        _font.Oversampling = 2;
        _font.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
        _rng.Randomize();
        _lastMouse = Pad.MousePos();
        if (!_shotHoldChecked)
        {
            _shotHoldChecked = true;
            foreach (var a in OS.GetCmdlineUserArgs())
                if (a == "--shot") { _shotHold = true; break; }
        }

        int n = _choices.Length;
        _disp = new string[n]; _rows = new Rect2[n]; _focus = new float[n];
        float pitch = _cinematic ? 52f : 80f;
        float height = _cinematic ? 46f : 68f;
        float width = Mathf.Min(720f, _fieldWidth - 128f);
        float top = (_cinematic ? 603f : 303f) - pitch * (n - 1) * 0.5f - height * 0.5f;
        _fontSize = _cinematic ? 26 : 30;
        for (int i = 0; i < n; i++)
        {
            _disp[i] = Quoted(_choices[i]);
            _rows[i] = new Rect2(_centerX - width * 0.5f, top + pitch * i, width, height);
            _focus[i] = i == Selected ? 1 : 0;
            while (UiKit.TextW(_font, _disp[i], _fontSize) > width - 144 && _fontSize > 1) _fontSize--;
        }
    }

    // 選択肢の表示文字列。「」は「口に出す／送る言葉」の印なので、送らない側（「（送らない）」など
    // 括弧で始まる“行為”の選択肢）には付けない＝『「（送らない）」』という二重括弧を作らない。
    // 全場面（P1 命名・S1-4・S3-7・F4・E6）で同じ扱いになるよう、判定はここ1箇所に閉じる。
    private static string Quoted(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return (s[0] == '（' || s[0] == '(') ? s : "「" + s + "」";
    }

    public override void _Process(double delta)
    {
        _t += delta;
        QueueRedraw();
        if (Decided) return;
        float dt = (float)delta;
        UpdateMotes(dt);
        for (int i = 0; i < _focus.Length; i++)
            _focus[i] = Mathf.Lerp(_focus[i], i == Selected ? 1f : 0f, 1 - Mathf.Exp(-16f * dt));

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

        // ↑↓（十字含む）でカーソル移動。移動音は小さく柔らかく（SfxUiMove を絞って低く）。
        //   N択では方向を見て上下に動かす（両端でラップ）。2択ではどちらを押してももう片方＝従来と同じ挙動。
        bool up = Input.IsActionPressed("ui_up");
        bool down = Input.IsActionPressed("ui_down");
        bool nav = up || down;
        if (nav && !_navHeld)
        {
            int n = _choices.Length;
            Selected = (Selected + (down ? 1 : n - 1)) % n;
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

    private void StartDissolve(bool quiet)
    {
        _deciding = true;
        _decideT = 0;
        _quiet = quiet;
        _trailAcc = 0;
        if (Audio.Instance is { } au)
        {
            if (quiet) au.VoiceSe(au.TypMina, volDb: -26f);
            else au.PlayType(Hud.LineKind.Mina);
        }
        for (int i = 0; i < _choices.Length; i++)
        {
            if (i == Selected) continue;
            Rect2 row = RowRect(i);
            for (int j = 0; j < (quiet ? 3 : 9); j++)
                AddMote(new Vector2(_rng.RandfRange(row.Position.X + 20, row.End.X - 20), row.End.Y),
                    new Vector2(_rng.RandfRange(-12, 12), _rng.RandfRange(-35, -14)), Ice);
        }
    }

    private void AddMote(Vector2 position, Vector2 velocity, Color color)
    {
        float life = _rng.RandfRange(0.22f, 0.4f);
        _motes.Add(new Mote { P = position, V = velocity, Life = life, Max = life,
            R = _rng.RandfRange(1.4f, 2.4f), C = color });
    }

    private void SpawnTrail()
    {
        float dis = Mathf.Clamp((float)(_decideT / DissolveDur), 0, 1);
        Rect2 row = VisualRow(Selected, dis);
        AddMote(new Vector2(Mathf.Lerp(row.Position.X + 16, row.End.X - 16, dis), row.End.Y - 2),
            new Vector2(24, -14), Rose);
    }

    private void UpdateMotes(float dt)
    {
        for (int k = _motes.Count - 1; k >= 0; k--)
        {
            var m = _motes[k];
            m.Life -= dt;
            if (m.Life <= 0) { _motes.RemoveAt(k); continue; }
            m.V *= Mathf.Exp(-2.6f * dt);
            m.P += m.V * dt;
            _motes[k] = m;
        }
    }

    private Rect2 RowRect(int i) => _rows[i];

    private float RowAppear(int i)
    {
        float p = Mathf.Clamp(((float)_t - 0.08f - i * 0.08f) / 0.34f, 0, 1);
        return 1 - Mathf.Pow(1 - p, 3);
    }

    private Rect2 VisualRow(int i, float dis)
    {
        Rect2 row = RowRect(i);
        row.Position += new Vector2(18 * (1 - RowAppear(i)), 0);
        if (_deciding && i == Selected)
            row.Position += new Vector2(0, (535 - row.GetCenter().Y) * 0.18f * dis * dis);
        return row;
    }

    private static Vector2[] Outline(Rect2 row)
    {
        float cut = 7;
        return new[]
        {
            row.Position + new Vector2(cut, 0), new Vector2(row.End.X, row.Position.Y),
            row.End - new Vector2(0, cut), row.End - new Vector2(cut, 0),
            new Vector2(row.Position.X, row.End.Y), row.Position + new Vector2(0, cut),
            row.Position + new Vector2(cut, 0),
        };
    }

    private void DrawMark(Vector2 center, float focus, float alpha)
    {
        Vector2[] points = { center + new Vector2(0, -7), center + new Vector2(5, 0),
            center + new Vector2(0, 7), center + new Vector2(-5, 0), center + new Vector2(0, -7) };
        DrawPolyline(points, new Color(Ice.Lerp(Rose, focus), alpha * (0.45f + 0.55f * focus)), 1, true);
        if (focus > 0.01f) DrawColoredPolygon(points[..^1], new Color(Rose, alpha * focus));
    }

    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        float dis = _deciding ? Mathf.Clamp((float)(_decideT / DissolveDur), 0, 1) : 0;
        float appear = Mathf.Clamp((float)_t / VignetteIn, 0, 1);
        // Modulation keeps animated opacity out of UiKit's gradient cache keys.
        _lineTex ??= new GradientTexture2D
        {
            Gradient = new Gradient
            {
                Offsets = new[] { 0f, 0.5f, 1f },
                Colors = new[] { new Color(1, 1, 1, 0), Colors.White, new Color(1, 1, 1, 0) },
            },
            Width = 256, Height = 8,
            FillFrom = Vector2.Zero, FillTo = new Vector2(1, 0),
        };
        _shadeTex ??= new GradientTexture2D
        {
            Gradient = new Gradient
            {
                Offsets = new[] { 0f, 0.3f, 1f },
                Colors = new[] { new Color(1, 1, 1, 0), Colors.White, Colors.White },
            },
            Width = 8, Height = 128,
            FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1),
        };
        if (_cinematic)
            DrawTextureRect(_shadeTex, new Rect2(0, 470, UiKit.DesignW, 250), false, new Color(Ink, 0.82f * appear * (1 - dis)));
        else
            DrawRect(new Rect2(_fieldLeft, 0, _fieldWidth, 510), new Color(Ink, 0.12f * appear * (1 - dis)));

        for (int i = 0; i < _choices.Length; i++)
        {
            bool chosen = _deciding && i == Selected;
            float alpha = RowAppear(i);
            if (_deciding) alpha *= chosen ? 1 - Mathf.SmoothStep(0.35f, 1, dis) : 1 - Mathf.SmoothStep(0, 0.45f, dis);
            if (alpha <= 0) continue;
            Rect2 row = VisualRow(i, dis);
            Vector2[] outline = Outline(row);
            float focus = chosen ? 1 : _focus[i];
            float warm = !_deciding && i == _choices.Length - 1
                ? Mathf.Clamp((float)((_silenceT - SilenceWarm) / (SilenceAuto - SilenceWarm)), 0, 1) : 0;
            Color edge = Ice.Lerp(Rose, Mathf.Max(focus, warm));
            float pulse = 0.5f + 0.5f * Mathf.Sin((float)_t * 1.8f);
            DrawColoredPolygon(outline[..^1], new Color(Ink, alpha * (0.8f + 0.14f * focus)));
            DrawTextureRect(_lineTex, row, false, new Color(edge, alpha * (0.035f + focus * 0.065f)));
            DrawPolyline(outline, new Color(edge, alpha * (0.18f + focus * 0.46f + warm * 0.15f)), 1, true);
            DrawLine(row.Position + new Vector2(8, 0), row.Position + new Vector2(56, 0),
                new Color(edge, alpha * (0.25f + focus * 0.65f)), 2, true);
            DrawLine(row.End - new Vector2(56, 0), row.End - new Vector2(8, 0),
                new Color(edge, alpha * (0.2f + focus * 0.6f)), 1.5f, true);
            DrawMark(new Vector2(row.Position.X + 31, row.GetCenter().Y), focus, alpha);

            Vector2 text = new(row.Position.X + 65, row.GetCenter().Y - _font.GetHeight(_fontSize) / 2);
            UiKit.Text(this, _font, text, _disp[i], _fontSize, new Color(Muted.Lerp(Paper, focus), alpha));

            if (focus > 0.01f)
            {
                Vector2 arrow = new(row.End.X - 33, row.GetCenter().Y);
                Color color = new(edge, alpha * focus);
                DrawLine(arrow - new Vector2(20, 0), arrow, color, 1.5f, true);
                DrawLine(arrow - new Vector2(6, 5), arrow, color, 1.5f, true);
                DrawLine(arrow - new Vector2(6, -5), arrow, color, 1.5f, true);
                DrawTextureRect(_lineTex, new Rect2(row.Position.X + 12, row.End.Y - 2, row.Size.X - 24, 2), false,
                    new Color(Paper, alpha * focus * (0.35f + pulse * 0.2f)));
            }
            if (chosen)
            {
                float sweep = Mathf.Clamp(dis / 0.6f, 0, 1);
                float x = Mathf.Lerp(row.Position.X + 12, row.End.X - 12, sweep);
                float flash = Mathf.Sin(sweep * Mathf.Pi) * alpha * (_quiet ? 0.25f : 0.65f);
                DrawLine(new Vector2(x - 5, row.Position.Y + 6), new Vector2(x + 5, row.End.Y - 6), new Color(Paper, flash), 2, true);
                DrawTextureRect(_lineTex, new Rect2(x - 24, row.Position.Y + 4, 48, row.Size.Y - 8), false, new Color(Rose, flash * 0.18f));
            }
        }

        foreach (var mote in _motes)
        {
            float alpha = Mathf.Clamp(mote.Life / mote.Max, 0, 1) * 0.65f;
            DrawColoredPolygon(new[] { mote.P + new Vector2(0, -mote.R * 2), mote.P + new Vector2(mote.R, 0),
                mote.P + new Vector2(0, mote.R * 2), mote.P - new Vector2(mote.R, 0) }, new Color(mote.C, alpha));
        }

        if (_hintA > 0.01f && !_deciding && !_cinematic)
        {
            string hint = "↑↓ / マウス えらぶ　" + Pad.ConfirmToken + " けってい";
            UiKit.Text(this, UiKit.Zen, new Vector2(_fieldLeft, 467), hint, UiKit.FontSmall,
                new Color(Muted, 0.8f * _hintA), HorizontalAlignment.Center, _fieldWidth);
        }
        UiKit.EndDesign(this);
    }
}
