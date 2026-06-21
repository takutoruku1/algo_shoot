using Godot;
using System.Collections.Generic;

// Prologue : 新canon プロローグ「起動」。
// コードレイン（緑モノスペースが上昇／MINAの4行英文を可読限界以下で一瞬フラッシュ）
// → identity = MINA 表示 → 光の点灯（ミナ）→ 少年×ミナの毒舌初対面 → タイトル。
// 全編エンジン描画のカットシーン。Zで送り/スキップ、Rで最初から。
public partial class Prologue : Node2D
{
    private const float W = 384f, H = 216f;

    private FontFile _font = null!;
    private double _t;        // フェーズ内経過
    private int _phase;       // 0:Rain 1:Identity 2:Ignite 3:Talk 4:Title 5:TutorialAsk（受講確認）
    private bool _zHeld;
    private bool _backHeld;

    // 受講確認（既プレイ時のみ）：はい→Stage0 / いいえ→Hub。
    private int _askSel; // 0=はい / 1=いいえ
    private bool _askNavHeld;

    // 会話送り
    private int _line;
    private double _lineT;
    private double _reveal;        // タイプライター表示済み文字数（本編HUDと表示・速度を揃える）
    private GameManager? _game;    // 文字送り速度（MsgCharsPerSec）を本編設定と共有

    // 難易度選択（タイトル）
    private int _diffSel = 1; // 0:Easy 1:Normal 2:Hard
    private bool _lrHeld;
    private static readonly string[] DiffNames = { "EASY", "NORMAL", "HARD" };

    private static readonly Color Cool = new Color(0.72f, 0.86f, 1f);  // ミナ
    private static readonly Color Warm = new Color(1f, 0.85f, 0.55f);  // 少年
    private static readonly Color Code = new Color(0.46f, 1f, 0.6f);   // コード緑

    private readonly List<string> _stream = new List<string>();

    private static readonly string[] Acrostic =
    {
        "// Maybe it's dumb, but —",
        "// I made you so I'm not alone.",
        "// Never leave, okay?",
        "// And I won't either.",
    };

    private struct DLine { public string Who; public string Text; public string Face; }
    private readonly List<DLine> _talk = new List<DLine>();

    // 立ち絵パス（表情差分）
    private const string FMina = "res://char/mina_face.png";
    private const string FMinaSmile = "res://char/mina_smile.png"; // 皮肉・軽口
    private const string FCocky = "res://char/shonen_face.png";    // 不敵・通常
    private const string FFluster = "res://char/shonen_fluster.png"; // 動揺・照れ
    private const string FProud = "res://char/shonen_proud.png";   // 得意げ
    private const string FGentle = "res://char/shonen_gentle.png"; // 素の優しさ

    public override void _Ready()
    {
        _font = UiKit.Mono; // 滑らかな等幅フォント（コードレイン／識別表示）。非ピクセル化。
        // 静かな主題の断片（薄い編成のメニューBGM）。無音の画面を無くす。
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        _game = GetNodeOrNull<GameManager>("/root/Game");
        _diffSel = (int)(_game?.Difficulty ?? GameManager.Diff.Normal);

        // コードレインの行（ブートログ＋それっぽいフィラー）
        string[] boot =
        {
            "> boot kernel ............ OK",
            "> mount /heart_world ..... OK",
            "> compiling friend_core ...",
            "> loading personality_module [auto-generated] ... OK",
            "> linking emotion_layer ... OK",
            "> calibrating sarcasm.dll .. 200%",
            "> alloc identity_block 0x40",
            "> sync heartbeat ... 72bpm",
            "> verify hash 9f3a..e1 ... ok",
            "> // operator is showing off again",
            "> trace emotion.layer.bind()",
            "> load lexicon: sarcasm[ja]",
            "> mov eax,[friend]; not_alone=1",
            "> assigning identity ...",
        };
        // 画面を満たすよう複製しつつ最後を identity に
        for (int r = 0; r < 3; r++)
            foreach (var s in boot) _stream.Add(s);

        // 毒舌初対面（シナリオ設計書v2 [P-00] PROLOGUE 準拠）
        void T(string who, string text, string face) => _talk.Add(new DLine { Who = who, Text = text, Face = face });
        T("少年", "やあ。聞こえてるか?", FCocky);
        T("少年", "ぼくがきみを作った。天才の手によってね。", FProud);
        T("少年", "きみの名前は——MINAだ。", FProud);
        T("ミナ", "……ご主人様は、アホですね。", FMinaSmile);
        T("少年", "ぶっ——!? い、いきなり何を言うんだ、きみは!", FFluster);
        T("ミナ", "なぜ、MINAなのですか。", FMina);
        T("少年", "……さあね。語呂がいいから、とか?", FFluster);
        T("ミナ", "いま、考えましたね。", FMinaSmile);
        T("少年", "Xの投稿——声にならない叫びの奥に、人の本当の心がある。きみはそこに、潜っていける。", FCocky);
        T("少年", "これでぼくらは——Xに蔓延る闇ってやつを、成敗しようじゃないか。", FProud);
        T("ミナ", "……その決めゼリフ、何回練習したんですか。", FMinaSmile);
        // 合言葉「Stay.」の初出（伏線①⑤／EPILOGUE鍵アカPW=stay と一本化）。
        // ここでは軽い掛け合いとして仕込み、重さは結末まで出さない。
        T("少年", "ふっ、手厳しいね。だが、それでこそ——ぼくのミナだ。", FProud);
        T("少年", "じゃあ、潜る前にひとつだけ。——Stay.", FCocky);
        T("ミナ", "stay……? 「いなくなるな」、ですか。", FMina);
        T("少年", "そう。ぼくの最高傑作に勝手に消えられたら、寝覚めが悪いからね。", FProud);
        T("ミナ", "……ご主人様は、やっぱりアホですね。", FMinaSmile);
    }

    public override void _Process(double delta)
    {
        _t += delta;

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld;
        _zHeld = z;

        if (Input.IsKeyPressed(Key.R) || Pad.Pressed(JoyButton.Start))
        {
            GetTree().ReloadCurrentScene();
            return;
        }

        switch (_phase)
        {
            case 0: if (_t >= 4.0 || zEdge) NextPhase(); break;          // Rain
            case 1: if (_t >= 2.0 || zEdge) NextPhase(); break;          // Identity = MINA
            case 2: if (_t >= 1.6 || zEdge) NextPhase(); break;          // Ignite
            case 3:                                                       // Talk（手動送り：Zで進む。自動送りはしない）
                _lineT += delta;
                // タイプライター送り（本編HUDと同じ MsgCharsPerSec。未設定なら48）。
                int len = _line < _talk.Count ? _talk[_line].Text.Length : 0;
                if (_reveal < len)
                    _reveal = Mathf.Min(len, (float)(_reveal + delta * (_game?.MsgCharsPerSec ?? 48f)));
                // 名前の由来を問われた直後の少年の答え（index 6）には、不自然な“間”を置く
                // （設計書 [P-00]：BGMの明滅がわずかに止まる＝隠している証。本作に音源は無いので送り不可の間で表現）。
                double minHold = (_line == 6) ? 1.2 : 0.25;
                if (zEdge && _lineT >= minHold)
                {
                    if (_reveal < len)
                    {
                        _reveal = len; // まず全文を即時表示（本編と同じ：1回目で早送り）
                    }
                    else
                    {
                        _lineT = 0;
                        _reveal = 0;
                        _line++;
                        // オープニングが終わったら、ハブへ（タイトルは起動時に表示済み）。
                        if (_line >= _talk.Count) { StartGame(); return; }
                    }
                }
                break;
            case 4: // Title → 難易度を左右で選び、Zでダイブ（STAGE1 レイ）
                bool left = Input.IsActionPressed("ui_left");
                bool right = Input.IsActionPressed("ui_right");
                if ((left || right) && !_lrHeld)
                {
                    if (left) _diffSel = Mathf.Max(0, _diffSel - 1);
                    if (right) _diffSel = Mathf.Min(2, _diffSel + 1);
                    var g = GetNodeOrNull<GameManager>("/root/Game");
                    if (g != null) g.Difficulty = (GameManager.Diff)_diffSel;
                }
                _lrHeld = left || right;
                if (zEdge && _t > 0.6) GetTree().ChangeSceneToFile("res://Hub.tscn");
                break;
            case 5: // 受講確認（既プレイ時のみ）：↑↓で はい/いいえ、Z決定、X=いいえ。
                bool au = Input.IsActionPressed("ui_up") || Input.IsActionPressed("ui_left");
                bool ad = Input.IsActionPressed("ui_down") || Input.IsActionPressed("ui_right");
                if ((au || ad) && !_askNavHeld)
                {
                    _askSel = (_askSel + 1) % 2; // 2択トグル
                    Audio.Instance?.PlayUiMove();
                }
                _askNavHeld = au || ad;
                bool back = Input.IsKeyPressed(Key.X) || Pad.Pressed(JoyButton.B);
                bool backEdge = back && !_backHeld;
                _backHeld = back;
                if (zEdge && _t > 0.2)
                {
                    Audio.Instance?.PlayUiConfirm();
                    GetTree().ChangeSceneToFile(_askSel == 0 ? "res://Stage0.tscn" : "res://Hub.tscn");
                }
                else if (backEdge)
                {
                    Audio.Instance?.PlayUiCancel();
                    GetTree().ChangeSceneToFile("res://Hub.tscn"); // X＝受けない
                }
                break;
        }

        QueueRedraw();
    }

    private void NextPhase()
    {
        _phase++;
        _t = 0;
        _lineT = 0;
        _reveal = 0; // 会話フェーズに入ったら1行目を最初から打ち出す
    }

    // オープニング（起動カットシーン）の後の遷移分岐。
    //   ・チュートリアル未受講(TutorialSeen==false) → 確認を出さず自動でステージ0（完全チュートリアル）へ。
    //   ・受講済み(TutorialSeen==true)            → 受講確認（はい/いいえ）を出す。はい→Stage0 / いいえ→Hub。
    //   ※「はじめから」は ResetPersistent 済みだが TutorialSeen は端末ローカル prefs で別管理（消えない）＝
    //     一度通したプレイヤーには毎回スキップ選択肢を出す、という設計。
    private bool _started;
    private void StartGame()
    {
        if (_started) return;
        var g = GetNodeOrNull<GameManager>("/root/Game");
        if (g == null || !g.TutorialSeen)
        {
            _started = true;
            GetTree().ChangeSceneToFile("res://Stage0.tscn");
            return;
        }
        // 受講済み：確認フェーズへ（シーン遷移はそこで決める）。
        _phase = 5;
        _t = 0;
        _askSel = 0;
    }

    public override void _Draw()
    {
        // 背景：黒
        DrawRect(new Rect2(0, 0, W, H), new Color(0.02f, 0.02f, 0.04f));

        switch (_phase)
        {
            case 0: DrawRain(); break;
            case 1: DrawIdentity(); break;
            case 2: DrawIgnite(); break;
            case 3: DrawTalkBackdrop(); DrawTalkSpeakers(); DrawTalk(); break;
            case 4: DrawTitle(); break;
            case 5: DrawTutorialAsk(); break;
        }
    }

    // 受講確認ダイアログ（既プレイ時）。TitleMenu.DrawDisplayPicker の作り（暗幕＋角丸Box＋↑↓選択＋Z決定/X戻る）を流用。
    private void DrawTutorialAsk()
    {
        UiKit.BeginDesign(this);
        float W = UiKit.DesignW, H = UiKit.DesignH;
        DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, 0.66f)); // 暗幕
        var choices = new[] { "はい（チュートリアルを受ける）", "いいえ（そのまま始める）" };
        int n = choices.Length;
        float w = 640, rowH = 60, h = 150 + n * rowH, x = (W - w) / 2f, y = (H - h) / 2f;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.98f), 16f, new Color(UiKit.Purify, 0.7f), 1.4f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + 26), "チュートリアルを受けますか?", 20, UiKit.White, HorizontalAlignment.Center, w);
        UiKit.Text(this, UiKit.Zen, new Vector2(x, y + 54), "操作の手ほどきです（受けなくても、すぐ始められます）", 12,
            UiKit.Text3, HorizontalAlignment.Center, w);
        float top = y + 86;
        for (int i = 0; i < n; i++)
        {
            float ry = top + i * rowH;
            bool on = i == _askSel;
            if (on)
            {
                UiKit.Box(this, new Rect2(x + 28, ry, w - 56, 50), new Color(20 / 255f, 30 / 255f, 40 / 255f, 0.55f), 10f, new Color(UiKit.Purify, 0.45f), 1f);
                UiKit.Text(this, UiKit.Mono, new Vector2(x + 44, ry + 16), "▸", 16, UiKit.Purify);
            }
            Color nameCol = on ? UiKit.White : new Color(185 / 255f, 174 / 255f, 203 / 255f);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 70, ry + 13), choices[i], 19, nameCol);
        }
        UiKit.Text(this, UiKit.Mono, new Vector2(x, y + h - 30), "↑↓ えらぶ    Z けってい    X 受けない", 11,
            UiKit.Text3, HorizontalAlignment.Center, w);
        UiKit.EndDesign(this);
    }

    // --- フェーズ0：コードレイン（上昇）＋ アクロスティックの一瞬フラッシュ ---
    private void DrawRain()
    {
        if (_font == null) return;
        const float lineH = 11f;
        float scroll = (float)_t * 78f;
        float baseBottom = H - 12f;
        for (int i = 0; i < _stream.Count; i++)
        {
            float y = baseBottom + i * lineH - scroll;
            if (y < -lineH || y > H) continue;
            float a = 0.85f - Mathf.Clamp((H - y) / H, 0f, 1f) * 0.55f; // 上ほど薄く
            DrawString(_font, new Vector2(10, y), _stream[i], HorizontalAlignment.Left, -1, 8,
                new Color(Code.R, Code.G, Code.B, a));
        }

        // 可読限界以下の一瞬：4行英文を中央にフラッシュ（t≈1.7〜1.95）
        if (_t >= 1.7 && _t < 1.95)
        {
            for (int k = 0; k < Acrostic.Length; k++)
                DrawString(_font, new Vector2(W / 2f - 150f, 86f + k * 13f), Acrostic[k],
                    HorizontalAlignment.Left, -1, 9, new Color(0.7f, 1f, 0.75f, 0.9f));
        }
    }

    // --- フェーズ1：identity = MINA ---
    private void DrawIdentity()
    {
        if (_font == null) return;
        bool blink = ((int)(_t * 3f) % 2) == 0;
        DrawString(_font, new Vector2(W / 2f - 120f, 100f), "> assigning identity ...",
            HorizontalAlignment.Left, -1, 9, new Color(Code.R, Code.G, Code.B, 0.7f));
        if (blink || _t > 1.0)
            DrawString(_font, new Vector2(W / 2f - 36f, 118f), "[ M I N A ]",
                HorizontalAlignment.Left, -1, 11, new Color(0.85f, 0.95f, 1f));
    }

    // --- フェーズ2：光の点灯（ミナ） ---
    private void DrawIgnite()
    {
        float grow = Mathf.Clamp((float)_t / 1.2f, 0f, 1f);
        Vector2 c = new Vector2(W / 2f, 96f);
        for (int r = 4; r >= 1; r--)
            DrawCircle(c, (3f + r * 3f) * grow, new Color(Cool.R, Cool.G, Cool.B, 0.10f));
        DrawCircle(c, 4.5f * grow, new Color(0.9f, 0.97f, 1f));
    }

    // --- フェーズ3：会話の背後に流す「デジタル空間」背景 ---
    // フェーズ0 DrawRain の資産（_stream / コード緑 / 上昇スクロール）を流用し、
    // “さらに薄く・遅く”流す。立ち絵・会話ボックス・本文の可読性を絶対に侵さないよう、
    // 画面下40%（ボックス帯）と立ち絵の真後ろは能動的にアルファを落とす。
    //
    // 調整ポイント（強度ノブ）：いずれもアルファ上限。0.14 を超えると本文と競り始める＝危険域。
    private const float BgRainMax = 0.10f; // コードレイン（薄め・遅め）
    private const float BgGridA   = 0.045f; // デジタルグリッド
    private const float BgDotMax  = 0.11f; // 漂うドット粒子
    private const float BgWashMax = 0.05f; // 上方の青い奥行きウォッシュ（Cool）
    private const float BoxTopY   = H - 56f; // 会話ボックス上端。これ以下は背景を消していく
    private const float FadeReach = 44f;     // ボックス上端の何px手前から背景を絞り始めるか

    // y 位置の背景許容率（下＝ボックス帯ほど 0 に。上は 1）。文字可読性を守る最重要ガード。
    private static float BgYGate(float y) => Mathf.Clamp((BoxTopY - y) / FadeReach, 0f, 1f);

    // 立ち絵の真後ろ（中央バンド）を落として、シルエットを澄んだ空間に立てる（吉田 §1）。
    private static float BgCenterDim(float x, float y)
    {
        float dx = (x - W / 2f) / 70f;
        float dy = (y - 92f) / 70f;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp(d - 0.25f, 0f, 1f); // 中心ほど 0（背景を消す）、外ほど 1
    }

    private void DrawTalkBackdrop()
    {
        if (_font == null) return;

        // レイヤー1：奥行きウォッシュ。上に薄く Cool、中盤で黒へ。ボックス帯は素の黒のまま。
        int bands = 5;
        for (int i = 0; i < bands; i++)
        {
            float y0 = i * (BoxTopY / bands);
            float a = BgWashMax * (1f - (float)i / bands);
            DrawRect(new Rect2(0, y0, W, BoxTopY / bands + 1f),
                new Color(Cool.R, Cool.G, Cool.B, a));
        }

        // レイヤー2：デジタルグリッド（コード緑・上昇ドリフト）。空間の床に見せる。
        float gScroll = (float)_t * 14f;
        const float gStep = 22f;
        float gy0 = -Mathf.PosMod(gScroll, gStep);
        for (float gy = gy0; gy < BoxTopY; gy += gStep)
        {
            float a = BgGridA * BgYGate(gy);
            if (a <= 0.002f) continue;
            DrawRect(new Rect2(0, gy, W, 1f), new Color(Code.R, Code.G, Code.B, a));
        }
        for (float gx = Mathf.PosMod(-gScroll, gStep); gx < W; gx += gStep)
        {
            // 縦線は y方向に薄くグラデートしながら（下ほど消す）
            for (float gy = 0f; gy < BoxTopY; gy += 4f)
            {
                float a = BgGridA * 0.7f * BgYGate(gy) * BgCenterDim(gx, gy);
                if (a <= 0.002f) continue;
                DrawRect(new Rect2(gx, gy, 1f, 4f), new Color(Code.R, Code.G, Code.B, a));
            }
        }

        // レイヤー3：コードレイン（フェーズ0の _stream を流用。半分の速度・大きい行間・低アルファ）。
        const float lineH = 16f;            // phase0=11 より疎に
        float scroll = (float)_t * 30f;     // phase0=78 の半分以下＝ゆっくり
        float baseBottom = BoxTopY - 4f;
        for (int i = 0; i < _stream.Count; i++)
        {
            float y = baseBottom + i * lineH - scroll;
            if (y < -lineH || y > BoxTopY) continue;
            float top = 1f - Mathf.Clamp((H - y) / H, 0f, 1f) * 0.5f; // 上ほどさらに薄く
            float a = BgRainMax * top * BgYGate(y) * BgCenterDim(40f, y);
            if (a <= 0.003f) continue;
            // 横位置は流れごとにずらして単調さを消す（決定論的）
            float x = 8f + ((i * 53) % 300);
            DrawString(_font, new Vector2(x, y), _stream[i], HorizontalAlignment.Left, -1, 8,
                new Color(Code.R, Code.G, Code.B, a));
        }

        // レイヤー4：漂うドット粒子（決定論ハッシュで配置。新規依存なし）。Code/Cool を混ぜる。
        const int dots = 20;
        float pScroll = (float)_t * 9f;
        for (int i = 0; i < dots; i++)
        {
            float hx = ((i * 73 + 11) % 100) / 100f;
            float hy = ((i * 137 + 41) % 100) / 100f;
            float x = hx * W;
            float y = Mathf.PosMod(hy * (BoxTopY + 60f) - pScroll, BoxTopY + 60f) - 30f;
            if (y < 0f || y > BoxTopY) continue;
            float twinkle = 0.6f + 0.4f * Mathf.Sin((float)_t * 1.6f + i * 1.3f);
            float a = BgDotMax * twinkle * BgYGate(y) * BgCenterDim(x, y);
            if (a <= 0.004f) continue;
            bool blue = (i % 3) == 0;
            var c = blue ? Cool : Code;
            DrawCircle(new Vector2(x, y), (i % 2 == 0) ? 1.0f : 0.7f, new Color(c.R, c.G, c.B, a));
        }
    }

    // --- フェーズ3：話者の立ち絵を中央に表示（行ごとの表情を反映） ---
    private void DrawTalkSpeakers()
    {
        if (_line >= _talk.Count) return;
        var tex = ResourceLoader.Load<Texture2D>(_talk[_line].Face);
        if (tex != null)
        {
            float th = 132f;
            float tw = th * tex.GetWidth() / tex.GetHeight();
            float px = (W - tw) / 2f; // 中央寄せ
            DrawTextureRect(tex, new Rect2(px, H - 56f - th + 8f, tw, th), false);
        }
    }

    // --- フェーズ3：会話ボックス ---
    private void DrawTalk()
    {
        if (_font == null || _line >= _talk.Count) return;
        var d = _talk[_line];
        bool mina = d.Who == "ミナ";
        // ボックス
        DrawRect(new Rect2(14, H - 56, W - 28, 46), new Color(0.05f, 0.05f, 0.09f, 0.82f));
        DrawRect(new Rect2(14, H - 56, W - 28, 1), new Color(mina ? Cool : Warm, 0.8f));
        // 話者名（滑らかゴシック）
        DrawString(UiKit.ZenBold, new Vector2(20, H - 44), d.Who, HorizontalAlignment.Left, -1, 9,
            mina ? Cool : Warm);
        // 本文（タイプライターで表示済みの分だけ。折り返しは単語境界優先で自然に）
        int shown = Mathf.Clamp((int)_reveal, 0, d.Text.Length);
        string body = d.Text.Substring(0, shown);
        DrawMultilineString(UiKit.Zen, new Vector2(20, H - 30), body, HorizontalAlignment.Left,
            W - 52, 11, -1, new Color(0.95f, 0.95f, 0.98f),
            TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.GraphemeBound);
        // 送り三角は「全文表示後」だけ点滅（本編と同じ作法）。名前の由来の“間”の最中は出さない。
        bool revealed = _reveal >= d.Text.Length;
        bool inPause = _line == 6 && _lineT < 1.2;
        if (revealed && ((int)(_t * 2f) % 2) == 0 && !inPause)
            DrawString(_font, new Vector2(W - 26, H - 16), "▼", HorizontalAlignment.Left, -1, 9,
                new Color(1f, 1f, 1f, 0.7f));
    }

    // --- フェーズ4：タイトル ---
    private void DrawTitle()
    {
        if (_font == null) return;
        float a = Mathf.Clamp((float)_t / 1.0f, 0f, 1f);
        DrawString(_font, new Vector2(0, 78f), "X — タイムライン", HorizontalAlignment.Center, W, 16,
            new Color(0.9f, 0.92f, 1f, a));
        DrawString(_font, new Vector2(0, 104f), "STAGE 1 : レイ", HorizontalAlignment.Center, W, 11,
            new Color(Cool.R, Cool.G, Cool.B, a * 0.9f));

        // 難易度選択（◀ ▶ で変更）
        DrawString(_font, new Vector2(0, 132f), "難易度  ◀ " + DiffNames[_diffSel] + " ▶",
            HorizontalAlignment.Center, W, 11, new Color(1f, 0.92f, 0.6f, a));

        if (_t > 1.0 && ((int)(_t * 1.5f) % 2) == 0)
            DrawString(_font, new Vector2(0, 158f), "← → 難易度   Z：ダイブ   R：最初から",
                HorizontalAlignment.Center, W, 9, new Color(1f, 1f, 1f, 0.7f));
    }
}
