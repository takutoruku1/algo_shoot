using Godot;

// Hud : ゲーム中HUD（CanvasLayer）。RefrainHTML/Refrain HUD A.dc.html を忠実移植（非ピクセル「Clean Glass」）。
//   左上 LIFE/BOMB ガラスパネル・中央上 浄化カプセル・右上 SCORE＋テレメトリ・中央 ボスXカード・
//   下部「降ってくる言葉」ティッカー。被弾＝赤エッジ、浄化100%＝発光。会話はシネマ下部バー（タイプライター）。
//   描画は子 Node2D(_canvas) が UiKit で設計座標(1280x720)に行う。公開APIは従来どおり。
public partial class Hud : CanvasLayer
{
    private GameManager _game = null!;
    private HudCanvas _canvas = null!;

    // 吹き出し表示中は敵を止める（他クラスから参照）
    public static bool BubblePaused = false;
    public bool HoldBubble = false;

    private int _lives = 3;

    // ステージ経過タイム（秒）。各ステージシーンが毎フレーム SetElapsed で渡す。
    // delta基準でステージ側が積算するため、ポーズ中（ツリーpause）は自然に止まる。
    private float _elapsed;
    public void SetElapsed(float sec) => _elapsed = sec;

    // ボス
    private bool _bossVisible;
    private string _bossName = "";
    private string _bossHandle = "";
    private float _bossFrac = 1f;          // 現在の1本ぶん（0〜1）。窓ごとに1本削れて次の本へリフィル。
    private int _bossBarIndex;             // 残バーの先頭インデックス（0始まり）
    private int _bossBarsTotal = 1;        // 総バー数
    private long _bossReplies = 2847;

    // バナー
    private string _bannerText = "";
    private double _bannerTimer;
    // クリアリザルトのタイム行（バナー直下）。空なら描かない。
    private string _bannerTime = "";     // 例 "TIME 1:23.45"
    private string _bannerBest = "";     // 例 "NEW BEST!" or "BEST 1:20.00"
    private bool _bannerNewBest;

    // スペル宣言（Xツイート風オーバーレイ：Refrain Danmaku v3 spellOverlay）
    private string _spellName = "";
    private string _spellWho = "";
    private string _spellHandle = "";
    private Color _spellCol = Colors.White;
    private double _spellTimer;
    private const double SpellShowDur = 5.0;   // 保持を延ばし「必殺技が来た」を見落とさせない（旧3.8）
    private const double SpellPopDur = 0.30;   // ポップイン（アンティシペーション→オーバーシュート着地）
    private const double SpellFadeDur = 0.80;  // フォロースルー（フェード＋わずかにスケールダウン）
    private double _spellGlow;                 // 発動の瞬間に立つ宣言まわりの加算グロー（自前・短命。弾は隠さない）

    // フラッシュ
    private float _flashAlpha;
    private Color _flashRgb = new(1f, 1f, 1f);
    private double _hurtEdge; // 被弾エッジの残り時間

    // ヒカゲスキル
    private bool _skillHas, _skillReady;

    // ショットモード（現在モード表示＋切替トースト・設計書 §3-5）
    private GameManager.ShotMode _shotMode = GameManager.ShotMode.Rapid;
    private double _shotModeToast;
    private const double ShotModeToastDur = 2.0;

    // 会話／メッセージ
    private string _dlgText = "";
    private string _dlgSpeaker = "";
    private Color _dlgSpeakerCol = Colors.White;
    private bool _dlgIsDialog;          // true=シネマバー / false=ナレーション（中央）
    private Texture2D? _dlgPortrait;
    private double _messageTimer;
    private float _dlgRevealed;         // タイプライター表示済み文字数
    private const float CharsPerSec = 48f;
    // 現在行の種類（タイプ送り音の音色＝話者を決める）。LineKind を取らない経路は既定＝Narration（無音）。
    private LineKind _dlgKind = LineKind.Narration;
    private int _typePrevRevealed;      // 直前フレームの revealed 整数部（新しく出た文字を差分検出）
    private const int TypeStride = 2;   // 何文字に1回鳴らすか（毎文字は鳴らしすぎ）

    // ───────── 立ち絵の生命感（吉田明彦：常時の微細な生命感／見た目のみ・進行に無影響）─────────
    // 呼吸：話者の立ち絵だけを Sin で上下に微細に揺らす。やりすぎない。
    private const float BreathPeriod = 3.6f;   // 呼吸周期（秒）
    private const float BreathAmp    = 1.6f;    // 振幅（±px・設計1280x720座標）
    // 表情クロスフェード：face テクスチャ切替の瞬間、旧→新を短時間でα合成。
    private const float PortraitFade = 0.12f;   // クロスフェード秒
    private Texture2D? _dlgPortraitPrev;        // 直前の立ち絵（フェードアウト側）
    private double _portraitFadeT;              // 0..PortraitFade を減算（>0 の間だけ旧絵を重ねる）
    // うなずき：タイプ送り完了の瞬間に立ち絵を 1px ほど下げて戻す相づち。
    private const float NodAmp  = 1.4f;         // うなずき深さ（px）
    private const float NodTime = 0.26f;        // うなずき1往復の所要（秒）
    private double _nodT;                       // 0..NodTime を減算（>0 の間だけうなずく）
    private bool _revealWasDone;                // 直前フレームでタイプ送りが完了していたか（完了の立ち上がり検出）

    // やさしさゲージ（HUD表示用）
    private double _overloadToast;
    private float _kindPulse;
    private float _prevKind;

    // 操作ガイド：プレイ開始時に一度だけ下部へ操作一覧を数秒出す（§9 説明より体験／一度だけで間延びさせない）。
    // オープニング会話が明けて「操作を握った瞬間」に出す（開幕の一瞬で消えないように）。
    private double _controlsTimer;
    private double _sceneTime;
    private bool _sawDialogue;
    private static bool _controlsShown;

    // チュートリアルの常駐指示（操作させる区間に下部へ出す小帯）。会話と違い敵/自機は止めない
    //（ShowMessage は BubblePaused を立ててしまうため、止めない専用の表示を用意する）。
    // 値が空でない間だけ描画する。チュートリアル中は既存 DrawControls（6.5秒一覧）を抑止する。
    private string _tutorialHint = "";
    public bool TutorialActive { get; set; }
    public void SetTutorialHint(string text) => _tutorialHint = text ?? "";
    public void ClearTutorialHint() => _tutorialHint = "";

    // 操作子トークン（操作表示モードで KB / パッドを出し分け。パッドは Pad.Style に従い Xbox/PS 表記）。
    // 単体チップ（BOMB残数横・モード切替・スキル）用＝代表1表記。
    private static string TokShot  => Pad.UsingPad ? Pad.Face(JoyButton.A)            : "Z";
    private static string TokFocus => Pad.UsingPad ? Pad.Face(JoyButton.LeftShoulder) : "Shift";
    private static string TokBomb  => Pad.UsingPad ? Pad.Face(JoyButton.X)            : "X";
    private static string TokMode  => Pad.UsingPad ? Pad.Face(JoyButton.B)            : "V";
    private static string TokSkill => Pad.UsingPad ? Pad.Face(JoyButton.Y)            : "C";
    private static string TokMove  => Pad.UsingPad ? "L"                              : "WASD";

    // 操作子トークン（全割り当て版）：選択中の表示モードに属する割り当てを“全部”並べる。
    // プレイ中HUDの操作ヒント（DrawControls）が使う。視認性のため区切りは細い「/」。
    private static string AllShot  => Pad.UsingPad ? Pad.Face(JoyButton.A)            : "Z / Space / Enter";
    private static string AllMove  => Pad.UsingPad ? "L"                              : "矢印 / WASD";
    private static string AllFocus => Pad.UsingPad ? $"{Pad.Face(JoyButton.LeftShoulder)} / {Pad.Face(JoyButton.RightShoulder)}" : "Shift";
    private static string AllBomb  => Pad.UsingPad ? Pad.Face(JoyButton.X)            : "X";
    private static string AllMode  => Pad.UsingPad ? Pad.Face(JoyButton.B)            : "V";
    private static string AllSkill => Pad.UsingPad ? Pad.Face(JoyButton.Y)            : "C";
    private static string AllKind  => Pad.UsingPad ? Pad.Face(JoyButton.RightStick)   : "Ctrl";

    // ティッカー（降ってくる言葉）
    private double _t;
    private static readonly (string h, string w)[] TickerWords =
    {
        ("@anon_03", "あたしのせいだ"), ("@kako__", "どうせ、とどかない"), ("@nobody_7", "もういない"),
        ("@ame_", "きえたい"), ("@_void", "なんで庇ったの"),
    };

    public override void _Ready()
    {
        AddToGroup("hud");
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        _canvas = new HudCanvas { Name = "HudCanvas", Hud = this };
        AddChild(_canvas);
    }

    public override void _Process(double delta)
    {
        _t += delta;

        if (_flashAlpha > 0f) _flashAlpha = Mathf.Max(0f, _flashAlpha - (float)delta * 2.2f);
        if (_hurtEdge > 0) _hurtEdge -= delta;

        // タイプライター送り
        if (_messageTimer > 0 && _dlgText.Length > 0 && _dlgRevealed < _dlgText.Length)
        {
            _dlgRevealed = Mathf.Min(_dlgText.Length, _dlgRevealed + (float)delta * (_game?.MsgCharsPerSec ?? CharsPerSec));
            // 文字が新たに出た瞬間だけ、TypeStride 文字に1回、話者の音色で送り音（Voiceバス）。
            // ナレ（Narration）は PlayType 側で無音。即時全文表示（RevealDialogNow）は差分が一気に増えるが
            // 「1ストライド境界を跨いだか」だけで判定するので、増分の数だけ連打しない＝大量再生を防ぐ。
            int rev = Mathf.FloorToInt(_dlgRevealed);
            if (rev > _typePrevRevealed)
            {
                if (rev < _dlgText.Length && rev / TypeStride != _typePrevRevealed / TypeStride)
                    Audio.Instance?.PlayType(_dlgKind);
                _typePrevRevealed = rev;
            }
        }

        // 立ち絵の生命感タイマー（見た目のみ・進行に無影響）。
        if (_portraitFadeT > 0) _portraitFadeT -= delta;        // 表情クロスフェードの残り
        if (_nodT > 0) _nodT -= delta;                          // うなずきの残り
        // うなずき：その行のタイプ送りが「いま完了した瞬間」だけ1回トリガ。
        bool revealDone = _messageTimer > 0 && _dlgIsDialog && _dlgPortrait != null
                          && _dlgText.Length > 0 && _dlgRevealed >= _dlgText.Length;
        if (revealDone && !_revealWasDone) _nodT = NodTime;
        _revealWasDone = revealDone;

        if (_messageTimer > 0)
        {
            if (!HoldBubble) _messageTimer -= delta;
            if (_messageTimer <= 0) ClearDialog();
        }

        // 会話・メッセージ表示中は敵を止める（種類を問わず。旧挙動を踏襲）。開始の瞬間に敵弾を一掃。
        bool nowPaused = _messageTimer > 0 && _dlgText.Length > 0;
        if (nowPaused && !BubblePaused) { ClearEnemyBullets(); Audio.Instance?.PlayCalm(); } // ⑦鎮まる音で転換
        BubblePaused = nowPaused;

        if (_bannerTimer > 0) { _bannerTimer -= delta; }
        if (_bossLineTimer > 0) { _bossLineTimer -= delta; if (_bossLineTimer <= 0) _bossLine = ""; }
        // スペル宣言は会話バブル表示中は時間を止める＝発動の宣言を“戦闘が始まる瞬間”に確実に見せる。
        // （ボス _Ready の宣言が開幕イントロのバブルに食われて見落とされていた問題への対処。
        //   発動＝宣言の同期はそのままに、見せ場だけ非バブル中に揃える。）
        if (_spellTimer > 0 && !BubblePaused) { _spellTimer -= delta; }
        if (_spellGlow > 0 && !BubblePaused) { _spellGlow = Mathf.Max(0.0, _spellGlow - delta / 0.45); } // 約0.45秒で収束
        if (_shotModeToast > 0) { _shotModeToast -= delta; }

        // 操作ガイド：オープニング会話が明けて操作を握った瞬間に一度だけ提示。
        //   ・会話があった後の最初の非会話フレーム、または会話なしステージでは1.2秒経過後。
        //   ・開幕（会話前）の一瞬で出して消えてしまうのを防ぐ。
        _sceneTime += delta;
        if (BubblePaused) _sawDialogue = true;
        // チュートリアル中は操作一覧を抑止（チュートリアルが個別に教えるため。2周目以降の通常プレイでのみ出す）。
        if (!_controlsShown && !TutorialActive && !BubblePaused && (_sawDialogue || _sceneTime > 1.2)
            && GetTree().GetFirstNodeInGroup("player") != null)
        {
            _controlsShown = true;
            _controlsTimer = 6.5;
        }
        if (_controlsTimer > 0) _controlsTimer -= delta;

        // やさしさゲージの演出更新（全開トースト＋グレイズで貯まる手応え）
        if (_game?.JustOverloaded ?? false) { _overloadToast = 1.4; Audio.Instance?.PlayOverload(); } // ⑥ピークの告知
        if (_overloadToast > 0) _overloadToast -= delta;
        float kNow = _game?.Kindness ?? 0f;
        if (!(_game?.IsOverload ?? false) && kNow > _prevKind + 0.001f) _kindPulse = 1f;
        _prevKind = kNow;
        if (_kindPulse > 0) _kindPulse = Mathf.Max(0f, _kindPulse - (float)delta * 4f);

        _canvas.QueueRedraw();
    }

    private void ClearEnemyBullets()
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        foreach (Node n in GetTree().GetNodesInGroup("enemy_bullets"))
            if (n is Bullet b && b.Active) pool.Despawn(b);
    }

    private void ClearDialog()
    {
        _dlgText = ""; _dlgSpeaker = ""; _dlgPortrait = null; _dlgRevealed = 0;
        _dlgPortraitPrev = null; _portraitFadeT = 0; _nodT = 0; _revealWasDone = false;
    }

    // ───────── テキストボックスの行の種類 ─────────
    public enum LineKind { Boy = 0, Mina = 1, Other = 2, Narration = 3, Post = 4, Relay = 5 }

    public void ShowMessage(string text)
    {
        SetDialog(text, "", default, dialog: false, portrait: "");
        _messageTimer = 4.5;
    }

    public void ShowDialog(string text) => ShowDialog(text, "res://char/algo_cutout.png");

    public void ShowDialog(string text, string portraitResPath)
    {
        SetDialog(text, "", default, dialog: true, portrait: portraitResPath);
        _messageTimer = 6.0;
    }

    public void ShowDialog(LineKind kind, string text, string portrait = "", string otherName = "")
    {
        string speaker; Color color; bool dialog = true; string portraitToUse = portrait;
        switch (kind)
        {
            case LineKind.Boy:   speaker = "少年"; color = UiKit.Info; break;
            case LineKind.Mina:  speaker = "ミナ"; color = UiKit.Mina; break;
            case LineKind.Other: speaker = otherName; color = UiKit.Kegare; break;
            case LineKind.Relay: speaker = "少年（ミナの声）"; color = UiKit.Info; break;
            case LineKind.Post:  speaker = "Ｘ 投稿"; color = UiKit.Text3; portraitToUse = ""; break;
            default:             speaker = ""; color = default; portraitToUse = ""; dialog = false; break;
        }
        SetDialog(text, speaker, color, dialog, portraitToUse);
        _dlgKind = kind; // 送り音の音色＝話者（ナレは PlayType 側で無音）
        _messageTimer = 6.0;
    }

    private void SetDialog(string text, string speaker, Color speakerCol, bool dialog, string portrait)
    {
        _dlgText = text; _dlgSpeaker = speaker; _dlgSpeakerCol = speakerCol;
        _dlgIsDialog = dialog; _dlgRevealed = 0;
        // 新しい行＝送り音の差分検出をリセット。話者は既定で無音（ナレ）。
        // LineKind を取る ShowDialog だけが直後に _dlgKind を上書きする。
        _typePrevRevealed = 0; _dlgKind = LineKind.Narration;
        Texture2D? next = string.IsNullOrEmpty(portrait) ? null : ResourceLoader.Load<Texture2D>(portrait);
        // 表情クロスフェード：face テクスチャが実際に変わる瞬間だけ、旧絵を短時間重ねて移ろわせる。
        // 同一立ち絵の続き（同じ話者の連続行）はクロスフェードせず、無からの登場/退場もハード切替で十分。
        if (next != null && _dlgPortrait != null && next != _dlgPortrait)
        {
            _dlgPortraitPrev = _dlgPortrait;
            _portraitFadeT = PortraitFade;
        }
        else
        {
            _dlgPortraitPrev = null;
            _portraitFadeT = 0;
        }
        _dlgPortrait = next;
        // 新しい行：うなずきは未完了から仕切り直し。
        _nodT = 0; _revealWasDone = false;
    }

    public void HideBubble() { _messageTimer = 0; ClearDialog(); }

    // 会話送り（ステージの Step_Lines から使う）：全文表示済みか／即時全文表示／オート送りON。
    public bool DialogRevealed => _dlgText.Length == 0 || _dlgRevealed >= _dlgText.Length;
    public void RevealDialogNow() { if (_dlgText.Length > 0) _dlgRevealed = _dlgText.Length; }
    public bool AutoAdvance => _game?.AutoAdvanceDialog ?? false;

    public void ShowBanner(string text) { _bannerText = text; _bannerTimer = 5.0; _bannerTime = ""; _bannerBest = ""; }

    // クリアリザルト用バナー：見出し＋ TIME 行（＋自己ベスト更新なら NEW BEST! / でなければ旧ベスト併記）。
    //   seconds=今回タイム、isBest=自己ベスト更新か、prevBest=更新前のベスト（初回 null）。
    public void ShowClearBanner(string text, float seconds, bool isBest, float? prevBest)
    {
        _bannerText = text; _bannerTimer = 5.0;
        _bannerTime = "TIME " + UiKit.FormatTime(seconds);
        _bannerNewBest = isBest;
        if (isBest) _bannerBest = "NEW BEST!";
        else if (prevBest != null) _bannerBest = "BEST " + UiKit.FormatTime(prevBest.Value);
        else _bannerBest = "";
    }

    // 無防備窓サイクル用の短い字幕（弾を止めない＝テンポ維持）。BREAK の合図・RECLOSE の弱気セリフに使う。
    // 通常の会話バブル(ShowDialog)は BubblePaused を立てて弾を止めるため、これとは別経路。
    private string _bossLine = "";
    private string _bossLineSpeaker = "";
    private Color _bossLineCol = Colors.White;
    private double _bossLineTimer;
    public void ShowBossLine(string speaker, string text, Color col, double dur)
    {
        _bossLineSpeaker = speaker; _bossLine = text; _bossLineCol = col; _bossLineTimer = dur;
    }

    // スペル発動を X のスペル宣言ツイート風に告知（弾幕パターン切替時に各ボスから呼ぶ）。
    public void AnnounceSpell(string who, string handle, string spellName, Color col)
    {
        _spellWho = who; _spellHandle = handle; _spellName = spellName;
        _spellCol = col; _spellTimer = SpellShowDur;
        _spellGlow = 1.0; // 発動の瞬間に立つ加算グロー（宣言まわりだけ・短命）。弾の視認は侵さない。
        Audio.Instance?.PlaySpell(); // ⑩弾幕変化を耳で予告（Alert・被弾の下/グレイズの上）
        // 「溜め→放つ」を体で：ごく短いヒットストップ＋軽い振動（被弾0.09/5.5 より弱く控えめ）。
        GameCamera.Instance?.Hitstop(0.05);
        GameCamera.Instance?.Shake(2.6f, 0.20f);
    }

    public void SetHikageSkill(bool has, bool ready) { _skillHas = has; _skillReady = ready; }

    // 現在のショットモードを設定。announce=true で切替トーストを表示。
    public void SetShotMode(GameManager.ShotMode m, bool announce)
    {
        _shotMode = m;
        if (announce) _shotModeToast = ShotModeToastDur;
    }

    public void ShowBossBar(string bossName) => ShowBossBar(bossName, "");
    // handle を明示すると固有ハンドルで表示（X世界観の没入＝§11）。空なら名前から自動生成（日本語名は @boss）。
    public void ShowBossBar(string bossName, string handle)
    {
        _bossName = bossName; _bossVisible = true;
        if (!string.IsNullOrEmpty(handle))
        {
            _bossHandle = handle;
            return;
        }
        _bossHandle = "@" + System.Text.RegularExpressions.Regex.Replace(bossName, "[^A-Za-z0-9]", "").ToLower();
        if (_bossHandle.Length <= 1) _bossHandle = "@boss";
    }
    // 1本リフィル方式：メインバーは「現在の1本ぶん」を 0〜1 で描く。残バー数は pip と「残/総」で示す。
    public void UpdateBossBar(int barIndex, int totalBars, float frac)
    {
        _bossBarsTotal = Mathf.Max(1, totalBars);
        _bossBarIndex = Mathf.Clamp(barIndex, 0, _bossBarsTotal - 1);
        _bossFrac = Mathf.Clamp(frac, 0f, 1f);
    }
    public void HideBossBar() { _bossVisible = false; }

    public void SetLives(int n) { _lives = Mathf.Max(0, n); }

    public void Flash() { _flashRgb = new Color(1f, 1f, 1f); _flashAlpha = 0.55f; }
    public void HitFlash() { _flashRgb = new Color(1f, 0.2f, 0.28f); _flashAlpha = 0.7f; _hurtEdge = 0.9; }

    // ───────── 描画（子 HudCanvas から呼ばれる。設計座標 1280x720）─────────
    public void DrawAll(HudCanvas ci)
    {
        UiKit.BeginDesign(ci);
        DrawLifeBomb(ci);
        DrawPurify(ci);
        DrawScore(ci);
        DrawTimer(ci);
        if (_bossVisible) DrawBossCard(ci);
        if (_spellTimer > 0) DrawSpellCard(ci);
        DrawShotMode(ci);
        DrawKindness(ci);
        DrawGoal(ci);
        if (_skillHas) DrawSkill(ci);
        DrawTicker(ci);
        if (_tutorialHint.Length > 0) DrawTutorialHint(ci);
        if (_controlsTimer > 0) DrawControls(ci);
        if (_shotModeToast > 0) DrawShotModeToast(ci);
        if (_overloadToast > 0) DrawOverloadToast(ci);
        if (_dlgText.Length > 0) DrawDialog(ci);
        if (_bossLineTimer > 0 && _bossLine.Length > 0) DrawBossLine(ci);
        if (_bannerTimer > 0) DrawBanner(ci);
        // 被弾エッジ
        if (_hurtEdge > 0)
            UiKit.Box(ci, new Rect2(8, 8, 1280 - 16, 720 - 16), null, 18f, new Color(0.9f, 0.16f, 0.16f, 0.5f * (float)(_hurtEdge / 0.9)), 14f);
        // フラッシュ（全画面・最前面）
        if (_flashAlpha > 0f)
            ci.DrawRect(new Rect2(0, 0, 1280, 720), new Color(_flashRgb.R, _flashRgb.G, _flashRgb.B, _flashAlpha));
        UiKit.EndDesign(ci);
    }

    private void GlassPanel(HudCanvas ci, Rect2 r, Color? border = null)
        => UiKit.Box(ci, r, new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.62f), 16f, border ?? new Color(1, 1, 1, 0.12f), 1f);

    // 操作子バッジ（小さなキー枠）。情報の隣に添えて「どのボタンか」を一目で示す。描いた幅を返す。
    private float KeyBadge(HudCanvas ci, Vector2 p, string token, Color accent, float a = 1f)
    {
        float w = UiKit.TextW(UiKit.Mono, token, 11) + 12, h = 18;
        UiKit.Box(ci, new Rect2(p.X, p.Y, w, h), new Color(0.10f, 0.09f, 0.16f, 0.92f * a), 5f, new Color(accent, 0.75f * a), 1f);
        UiKit.Text(ci, UiKit.Mono, new Vector2(p.X + 6, p.Y + 3), token, 11, new Color(accent, 0.98f * a));
        return w;
    }

    private void DrawLifeBomb(HudCanvas ci)
    {
        int maxLives = Mathf.Max(_lives, _game?.StartLives ?? 4);
        int bombs = _game?.Bombs ?? 0;
        int maxBombs = Mathf.Max(bombs, _game?.StartBombs ?? 4);
        bool low = _lives <= 2;

        float x = 22, y = 20, w = 70 + maxLives * 25, h = 78;
        GlassPanel(ci, new Rect2(x, y, w, h), low ? new Color(1f, 0.35f, 0.42f, 0.4f) : null);
        // LIFE
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + 15, y + 14), "LIFE", 12, UiKit.Text2);
        float hx = x + 70;
        for (int i = 0; i < maxLives; i++)
        {
            Color hc = i < _lives ? (low ? new Color("ff5a6a") : UiKit.Hp) : new Color(UiKit.Hp, 0.22f);
            UiKit.Heart(ci, new Vector2(hx + i * 25 + 10, y + 22), 10f, hc);
        }
        ci.DrawRect(new Rect2(x + 14, y + 42, w - 28, 1f), new Color(1, 1, 1, 0.08f));
        // BOMB（残数＋発動キーのバッジ＝「どのボタンで撃つか」を常時提示）
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + 15, y + 52), "BOMB", 11, new Color("c8b0ec"));
        float bx = x + 70;
        for (int i = 0; i < maxBombs; i++)
            ci.DrawCircle(new Vector2(bx + i * 16 + 6, y + 58), 5f, i < bombs ? UiKit.Mina : new Color(UiKit.Mina, 0.28f));
        float badgeW = UiKit.TextW(UiKit.Mono, TokBomb, 11) + 12;
        KeyBadge(ci, new Vector2(x + w - badgeW - 10, y + 50), TokBomb, UiKit.Mina);
    }

    private void DrawPurify(HudCanvas ci)
    {
        float prog = _game?.StageProgress ?? 0f;
        bool full = prog >= 0.999f;
        float capW = 420, x = 640 - capW / 2f, y = 20, h = 30;
        UiKit.Box(ci, new Rect2(x, y, capW, h), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.62f), 15f,
            full ? new Color(UiKit.PurifyHi, 0.9f) : new Color(1, 1, 1, 0.12f), full ? 1.5f : 1f);
        ci.DrawCircle(new Vector2(x + 22, y + h / 2f), 7f, UiKit.Purify);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 38, y + 7), "浄化", 13, UiKit.Info);
        float barX = x + 80, barW = capW - 80 - 56, barY = y + h / 2f - 5;
        UiKit.Box(ci, new Rect2(barX, barY, barW, 10f), new Color(1, 1, 1, 0.08f), 5f);
        if (prog > 0) UiKit.Box(ci, new Rect2(barX, barY, barW * prog, 10f), full ? UiKit.PurifyHi : UiKit.Purify, 5f);
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + capW - 50, y + 6), $"{Mathf.RoundToInt(prog * 100f)}%", 15, UiKit.PurifyHi, HorizontalAlignment.Right, 42);
    }

    private void DrawScore(HudCanvas ci)
    {
        long score = _game?.Score ?? 0;
        float w = 220, x = 1280 - 22 - w, y = 20;
        UiKit.Box(ci, new Rect2(x, y, w, 36f), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.62f), 14f, new Color(UiKit.Gold, 0.3f), 1f);
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + 14, y + 12), "SCORE", 11, new Color("f0d98a"));
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + w - 14 - UiKit.TextW(UiKit.Mono, score.ToString("000,000"), 22), y + 6), score.ToString("000,000"), 22, new Color("f0d98a"));

        // テレメトリ・チップ（♥＝浄化した心 / コンボ or フォロワー）
        long imp = _game?.RunImpression ?? 0;
        int combo = _game?.Combo ?? 0;
        string c1 = "♥ " + UiKit.Abbrev(imp);
        string c2 = combo >= 2 ? $"× {combo}" : UiKit.Abbrev(_game?.Followers ?? 0);
        float cy = y + 44;
        float c2w = 30 + UiKit.TextW(UiKit.Mono, c2, 11);
        float c2x = 1280 - 22 - c2w;
        UiKit.Box(ci, new Rect2(c2x, cy, c2w, 22f), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.5f), 11f, new Color(UiKit.Mina, 0.4f), 1f);
        UiKit.Text(ci, UiKit.Mono, new Vector2(c2x + 12, cy + 5), c2, 11, new Color("c8b0ec"));
        float c1w = 30 + UiKit.TextW(UiKit.Mono, c1, 11);
        float c1x = c2x - 7 - c1w;
        UiKit.Box(ci, new Rect2(c1x, cy, c1w, 22f), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.5f), 11f, new Color(UiKit.Purify, 0.4f), 1f);
        UiKit.Text(ci, UiKit.Mono, new Vector2(c1x + 12, cy + 5), c1, 11, UiKit.Info);
    }

    // ステージ経過タイム（右上・SCORE/テレメトリの直下）。タイムアタック感を出す等幅・発光ふち。
    // SCOREパネル(右上)と同じ右端に揃え、汚染カプセル(中央上)とは干渉しない。
    private void DrawTimer(HudCanvas ci)
    {
        string t = UiKit.FormatTime(_elapsed);
        // 幅は内容から動的に算出：丸(28) + "TIME" + 余白(12) + 値 + 右余白(14)。
        // 分が2桁(例 12:34.56)に伸びても「TIME」と数字が重ならず、右端は従来位置のまま左へ伸びる。
        float labelW = UiKit.TextW(UiKit.Mono, "TIME", 11);
        float valW = UiKit.TextW(UiKit.Mono, t, 17);
        float h = 28;
        float w = 28 + labelW + 12 + valW + 14;
        float x = 1280 - 22 - w, y = 90;
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.6f), 12f, new Color(UiKit.Info, 0.4f), 1f);
        ci.DrawCircle(new Vector2(x + 16, y + h / 2f), 4f, UiKit.Info);
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + 28, y + 8), "TIME", 11, UiKit.Text2);
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + w - 14 - valW, y + 5), t, 17, UiKit.PurifyHi);
    }

    private void DrawBossCard(HudCanvas ci)
    {
        float w = 560, x = 640 - w / 2f, y = 60, h = 60;
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(18 / 255f, 12 / 255f, 22 / 255f, 0.62f), 16f, new Color(UiKit.Kegare, 0.4f), 1.2f);
        // アバター（穢れ）＋認証
        Vector2 ac = new(x + 34, y + h / 2f);
        UiKit.RadialGlow(ci, ac, 28f, UiKit.Kegare, 0.4f);
        ci.DrawCircle(ac, 22f, new Color(0.35f, 0.13f, 0.27f));
        ci.DrawCircle(ac + new Vector2(15, 15), 9f, UiKit.Kegare);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(ac.X + 11, ac.Y + 6), "✓", 11, UiKit.White);
        // 名前＋ハンドル＋リプ
        float tx = x + 70;
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(tx, y + 10), _bossName, 16, UiKit.White);
        float nw = UiKit.TextW(UiKit.ZenBold, _bossName, 16);
        UiKit.Text(ci, UiKit.Mono, new Vector2(tx + nw + 10, y + 14), _bossHandle, 12, UiKit.Text3);
        // 残バー数（=index+1）と総バー数。リプ数は総HP比で減らす（演出）。
        int barsLeft = _bossBarIndex + 1;
        float overall = (_bossBarIndex + _bossFrac) / _bossBarsTotal;
        string rep = UiKit.Abbrev((long)(_bossReplies * overall));
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + w - 16 - UiKit.TextW(UiKit.Mono, rep, 12), y + 12), rep, 12, new Color("f0a8cf"));
        // 穢れバー（現在の1本ぶん）＋残バー数の● pip。
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(tx, y + 36), "穢れ", 10, new Color("f0a8cf"));
        float pipsW = _bossBarsTotal * 9f;
        float barX = tx + 34, barW = w - (barX - x) - 66 - pipsW, barY = y + 37;
        UiKit.Box(ci, new Rect2(barX, barY, barW, 10f), new Color(1, 1, 1, 0.07f), 5f);
        if (_bossFrac > 0) UiKit.Box(ci, new Rect2(barX, barY, barW * _bossFrac, 10f), UiKit.Kegare, 5f);
        // 残バー pip（左から「残っている本数」を満たす）。
        float pipX = barX + barW + 8f;
        for (int i = 0; i < _bossBarsTotal; i++)
            ci.DrawCircle(new Vector2(pipX + i * 9f + 3f, barY + 5f), 3f,
                i < barsLeft ? UiKit.Kegare : new Color(UiKit.Kegare, 0.22f));
        // 「残/総」表示。
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + w - 16 - 40, y + 34), $"{barsLeft}/{_bossBarsTotal}", 12, new Color("f0a8cf"), HorizontalAlignment.Right, 40);
    }

    // スペル宣言オーバーレイ（X のスペル発動ツイート＋通知）。ボスカードの直下に出る。
    private void DrawSpellCard(HudCanvas ci)
    {
        double age = SpellShowDur - _spellTimer;

        // ── 三段のリズム：ポップイン（アンティシペーション→オーバーシュート）→ ホールド → フォロースルー ──
        float a;        // 不透明度
        float scale;    // 中心まわりのスケール
        float slide;    // 縦スライド（上から差して、消えるとき上へ抜ける）
        if (age < SpellPopDur)
        {
            float p = (float)(age / SpellPopDur);                 // 0→1
            a = Mathf.Clamp(p / 0.55f, 0f, 1f);                  // 立ち上がりは速く
            // Back ease-out：0.86 から 1.06 をかすめて 1.0 へ着地（弾性オーバーシュート）
            float bo = BackOut(p);
            scale = 0.86f + 0.14f * bo;
            slide = -14f * (1f - bo);                            // 上から差し込む
        }
        else if (_spellTimer < SpellFadeDur)
        {
            float p = (float)(_spellTimer / SpellFadeDur);        // 1→0
            a = Mathf.Clamp(p, 0f, 1f);
            scale = 0.97f + 0.03f * p;                           // わずかに縮みながら
            slide = -6f * (1f - p);                              // 上へ抜けて消える
        }
        else { a = 1f; scale = 1f; slide = 0f; }                 // ホールド

        float glow = (float)_spellGlow;                          // 発動の瞬間に立つ加算成分（短命）

        string title = "『" + _spellName + "』";
        float titleW = UiKit.TextW(UiKit.ZenBold, title, 17);
        float headW = UiKit.TextW(UiKit.ZenBold, _spellWho, 14) + UiKit.TextW(UiKit.Mono, _spellHandle, 11) + 96f;
        float w = Mathf.Clamp(Mathf.Max(titleW, headW) + 84f, 380f, 780f);
        float h = 60f;
        float x = 640 - w / 2f, y = 126f + slide;
        Vector2 center = new(640f, y + h / 2f);

        Color col = _spellCol;

        // 中心まわりにスケール（弾の視認を侵さないよう、宣言カードだけ拡縮。設計スケールは維持）。
        ci.DrawSetTransform(center * UiKit.Scale, 0f, new Vector2(UiKit.Scale * scale, UiKit.Scale * scale));

        // 発動の瞬間：カード背後に広がる加算グロー（控えめ・短命＝弾は隠さない）。
        if (glow > 0.001f)
            UiKit.RadialGlow(ci, Vector2.Zero, 150f + 40f * glow, col, 0.30f * glow);

        // カード本体（中心ローカル座標）。背景を少し濃く・縁を太く＝背景タイムラインに埋もれない。
        Rect2 box = new(-w / 2f, -h / 2f, w, h);
        UiKit.Box(ci, box, new Color(0.043f, 0.031f, 0.065f, 0.90f * a), 13f,
            new Color(col, (0.55f + 0.45f * glow) * a), 1.4f + 0.8f * glow);

        float left = -w / 2f, top = -h / 2f;
        // アバター＋認証
        Vector2 ac = new(left + 30, 0f);
        UiKit.RadialGlow(ci, ac, 17f, col, (0.45f + 0.4f * glow) * a);
        ci.DrawCircle(ac, 14f, new Color(col.R * 0.45f, col.G * 0.45f, col.B * 0.45f, a));
        ci.DrawCircle(ac + new Vector2(9, 9), 5.8f, new Color(col, a));
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(ac.X + 6, ac.Y + 4), "✓", 8, new Color(1, 1, 1, a));
        // 名前＋ハンドル
        float tx = left + 56;
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(tx, top + 10), _spellWho, 14, new Color(1, 1, 1, a));
        float nw = UiKit.TextW(UiKit.ZenBold, _spellWho, 14);
        UiKit.Text(ci, UiKit.Mono, new Vector2(tx + nw + 8, top + 14), _spellHandle, 11, new Color(UiKit.Text3, a));
        // 右肩「● スペル発動」（発動直後は明滅で“今来た”を主張）
        string tag = "スペル発動";
        float tagW = UiKit.TextW(UiKit.Mono, tag, 10) + 14;
        float tagPulse = 0.7f + 0.3f * Mathf.Sin((float)age * 12f);
        ci.DrawCircle(new Vector2(-w / 2f + w - tagW - 8, top + 15), 3.4f, new Color(col, a * tagPulse));
        UiKit.Text(ci, UiKit.Mono, new Vector2(-w / 2f + w - tagW, top + 9), tag, 10, new Color(col, a * tagPulse));
        // スペル名（少し大きく・明るく＝視認のピーク）
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(tx, top + 31), title, 17,
            new Color(0.96f + 0.04f * glow, 0.92f, 0.97f, a));

        // 設計スケールへ戻す（後続描画に影響させない）。
        ci.DrawSetTransform(Vector2.Zero, 0f, new Vector2(UiKit.Scale, UiKit.Scale));
    }

    // Back ease-out（弾性オーバーシュート）：終端で1.0をわずかに超えて戻る。
    private static float BackOut(float p)
    {
        const float s = 1.70158f;
        p -= 1f;
        return p * p * ((s + 1f) * p + s) + 1f;
    }

    // ヒカゲ専用スキルのチップ（目標パネルの直下）。発動キーのバッジ＋名前＋状態。
    private void DrawSkill(HudCanvas ci)
    {
        Color accent = _skillReady ? UiKit.Hp : UiKit.Text3;
        string label = "ヒカゲの大波  " + (_skillReady ? "OK!" : "充填中…");
        const float h = 24f;
        float badgeW = UiKit.TextW(UiKit.Mono, TokSkill, 11) + 12;
        float w = 12 + badgeW + 8 + UiKit.TextW(UiKit.ZenBold, label, 13) + 12;
        float x = 22, y = 216;
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.6f), 11f, new Color(accent, 0.5f), 1f);
        float bw = KeyBadge(ci, new Vector2(x + 12, y + 3), TokSkill, accent);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 12 + bw + 8, y + 5), label, 13, accent);
    }

    // 現在のショットモードチップ（LIFE/BOMB の直下・常時表示）。光=シアン基調。
    private void DrawShotMode(HudCanvas ci)
    {
        string name = _game?.ShotModeName(_shotMode) ?? "連射";
        string label = "ショット  " + name;
        const float padL = 16f, h = 24f;
        float w = padL + 10 + UiKit.TextW(UiKit.ZenBold, label, 13) + 14;
        float x = 22, y = 104;
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.6f), 11f, new Color(UiKit.Info, 0.45f), 1f);
        ci.DrawCircle(new Vector2(x + padL, y + h / 2f), 4.5f, UiKit.Info);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + padL + 10, y + 5), label, 13, UiKit.PurifyHi);
        // 切替キーのバッジ（KB=V / パッド=B を出し分け）
        float bw = KeyBadge(ci, new Vector2(x + w + 8, y + 3), TokMode, UiKit.Info);
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + w + 8 + bw + 6, y + 6), "切替", 10, UiKit.Text3);
    }

    // モード切替トースト（画面中央上に短時間スウィープ＝Shot Upgrades の modeSweep 相当）。
    private void DrawShotModeToast(HudCanvas ci)
    {
        float a = Mathf.Clamp((float)(_shotModeToast / 0.45), 0f, 1f); // 終わり際にフェード
        string name = _game?.ShotModeName(_shotMode) ?? "連射";
        string t = "MODE ▸ " + name;
        float w = UiKit.TextW(UiKit.ZenBlack, t, 30) + 90;
        float x = 640 - w / 2f, y = 150;
        UiKit.Box(ci, new Rect2(x, y, w, 54f), new Color(0.06f, 0.10f, 0.14f, 0.9f * a), 15f, new Color(UiKit.Info, 0.6f * a), 1.4f);
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + 22, y + 9), "MODE", 12, new Color(UiKit.Info, a));
        UiKit.Text(ci, UiKit.ZenBlack, new Vector2(x, y + 13), name, 30, new Color(UiKit.PurifyHi, a), HorizontalAlignment.Center, w);
    }

    // やさしさゲージ（ショットチップの直下）。蓄積＝紫、全開＝金で残時間が減る。満タン手前でふち明滅。
    private void DrawKindness(HudCanvas ci)
    {
        float fill = Mathf.Clamp(_game?.Kindness ?? 0f, 0f, 1f);
        bool over = _game?.IsOverload ?? false;
        float x = 22, y = 132, w = 168, h = 20;
        float pulse = (!over && fill >= 0.85f) ? (0.5f + 0.5f * Mathf.Sin((float)_t * 9f)) : 0f;
        Color border = over ? new Color(UiKit.Gold, 0.9f) : new Color(UiKit.Mina, 0.12f + 0.6f * pulse);
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.6f), 10f, border, 1f);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 12, y + 4), "やさしさ", 12, over ? UiKit.Gold : UiKit.Mina);
        float barX = x + 62, barW = w - 62 - 12;
        UiKit.Box(ci, new Rect2(barX, y + h / 2f - 4, barW, 8f), new Color(1, 1, 1, 0.08f), 4f);
        if (fill > 0)
        {
            float bh = 8f + _kindPulse * 4f;
            UiKit.Box(ci, new Rect2(barX, y + h / 2f - bh / 2f, barW * fill, bh), over ? UiKit.Gold : UiKit.Mina, 4f);
        }
        if (over) UiKit.Text(ci, UiKit.Mono, new Vector2(x + w + 8, y + 5), "全開!", 11, UiKit.Gold);
        else if (_game?.KindnessReady ?? false) // 満タン＝手動発動できる（Ctrl/R3）。点滅で誘導。
        {
            float ba = 0.55f + 0.45f * Mathf.Sin((float)_t * 7f);
            UiKit.Text(ci, UiKit.Mono, new Vector2(x + w + 8, y + 5), "満タン！" + AllKind, 11, new Color(UiKit.Gold, ba));
        }
    }

    // マクロ目標（表ゴール）を控えめに：救うべき3つの心の進捗＋ミナの汚染ゲージ。左端に小さく。
    // 「気づかせる」原則を守り情緒を削がないよう、淡色・小サイズで常設する。
    private void DrawGoal(HudCanvas ci)
    {
        int total = _game?.HeartGoal ?? 3;
        int saved = _game?.HeartsSaved ?? 0;
        float contam = Mathf.Clamp(_game?.Contamination ?? 0f, 0f, 1f);
        float x = 22, y = 162, w = 190, h = 48;
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.5f), 11f, new Color(UiKit.Purify, 0.26f), 1f);
        // 救った人 ◯/3（=HeartsSaved＝到達度マクロ目標）。通貨「浄化した心」と名前で分離する。
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 12, y + 8), "救った人", 11, UiKit.Info);
        for (int i = 0; i < total; i++)
        {
            Color c = i < saved ? UiKit.Purify : new Color(UiKit.Purify, 0.20f);
            UiKit.Heart(ci, new Vector2(x + 104 + i * 16, y + 13), 6f, c);
        }
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + w - 34, y + 7), $"{saved}/{total}", 12, UiKit.PurifyHi);
        // 汚染ゲージ（救うほど濁る＝目標の対カウンター）
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 12, y + 29), "汚染", 10, new Color(UiKit.Kegare, 0.95f));
        float barX = x + 44, barW = w - 44 - 14, barY = y + 31;
        UiKit.Box(ci, new Rect2(barX, barY, barW, 6f), new Color(1, 1, 1, 0.08f), 3f);
        if (contam > 0) UiKit.Box(ci, new Rect2(barX, barY, barW * contam, 6f), UiKit.Kegare, 3f);
    }

    // やさしさ全開の瞬間トースト（DrawShotModeToast と同系。中央上に短時間）。
    private void DrawOverloadToast(HudCanvas ci)
    {
        float a = Mathf.Clamp((float)(_overloadToast / 0.4), 0f, 1f);
        const string t = "やさしさ全開";
        float w = UiKit.TextW(UiKit.ZenBlack, t, 30) + 90;
        float x = 640 - w / 2f, y = 150;
        UiKit.Box(ci, new Rect2(x, y, w, 54f), new Color(0.10f, 0.08f, 0.04f, 0.9f * a), 15f, new Color(UiKit.Gold, 0.6f * a), 1.4f);
        UiKit.Text(ci, UiKit.ZenBlack, new Vector2(x, y + 13), t, 30, new Color(UiKit.Gold, a), HorizontalAlignment.Center, w);
    }

    // 操作ガイド：プレイ開始直後に一度だけ、下部中央へ「ボタン→動作」の一覧を数秒。
    // 直近デバイスで KB/パッドを出し分け、終わり際にフェード（テンポを侵さない）。
    private void DrawControls(HudCanvas ci)
    {
        float a = Mathf.Clamp((float)(_controlsTimer < 0.8 ? _controlsTimer / 0.8 : 1.0), 0f, 1f);

        // 全割り当てを列挙（選択中の表示モードに属するもの全部）。やさしさ全開も含める。
        var items = new System.Collections.Generic.List<(string tok, string label)>
        {
            (AllMove, "移動"), (AllShot, "撃つ"), (AllFocus, "低速"), (AllBomb, "ボム"),
        };
        bool hasModes = (_game?.IsModeUnlocked(GameManager.ShotMode.Spread) ?? false)
                     || (_game?.IsModeUnlocked(GameManager.ShotMode.Homing) ?? false);
        if (hasModes) items.Add((AllMode, "切替"));
        if (_skillHas) items.Add((AllSkill, "技"));
        items.Add((AllKind, "全開"));

        // 各項目の幅を測り、画面幅に収まるよう必要なら複数行へ折り返す（はみ出し防止・視認性最優先）。
        const float pad = 14f, gap = 16f, itemGap = 8f, rowH = 36f, rowGap = 6f, maxBarW = 1180f;
        var ws = new float[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            float bw = UiKit.TextW(UiKit.Mono, items[i].tok, 11) + 12;
            float lw = UiKit.TextW(UiKit.ZenBold, items[i].label, 13);
            ws[i] = bw + itemGap + lw;
        }

        // 貪欲に行へ詰める：1行が maxBarW を超えそうなら改行。
        var rows = new System.Collections.Generic.List<System.Collections.Generic.List<int>>();
        var cur = new System.Collections.Generic.List<int>();
        float curW = pad * 2;
        for (int i = 0; i < items.Count; i++)
        {
            float add = ws[i] + (cur.Count > 0 ? gap : 0);
            if (cur.Count > 0 && curW + add > maxBarW)
            {
                rows.Add(cur);
                cur = new System.Collections.Generic.List<int>();
                curW = pad * 2;
                add = ws[i];
            }
            cur.Add(i);
            curW += add;
        }
        if (cur.Count > 0) rows.Add(cur);

        float blockH = rows.Count * rowH + (rows.Count - 1) * rowGap;
        float y0 = 470 - (blockH - rowH); // 最下行が元の y=470 に来るよう上方向へ積む
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            float total = pad * 2;
            for (int j = 0; j < row.Count; j++) total += ws[row[j]] + (j > 0 ? gap : 0);
            float x = 640 - total / 2f, y = y0 + r * (rowH + rowGap);
            UiKit.Box(ci, new Rect2(x, y, total, rowH), new Color(0.05f, 0.04f, 0.09f, 0.86f * a), 12f, new Color(UiKit.Info, 0.45f * a), 1.2f);
            float cx = x + pad;
            for (int j = 0; j < row.Count; j++)
            {
                int i = row[j];
                float bw = KeyBadge(ci, new Vector2(cx, y + 9), items[i].tok, UiKit.PurifyHi, a);
                UiKit.Text(ci, UiKit.ZenBold, new Vector2(cx + bw + itemGap, y + 10), items[i].label, 13, new Color(0.92f, 0.92f, 0.97f, a));
                cx += ws[i] + gap;
            }
        }
    }

    // チュートリアルの常駐指示帯（操作させる区間・下部中央）。会話バーより上、ティッカーの上に出す。
    // ミナ色のふちで「今やること」を一行で示す。会話と違い敵/自機を止めないのが肝。
    private void DrawTutorialHint(HudCanvas ci)
    {
        float pulse = 0.6f + 0.4f * Mathf.Sin((float)_t * 4f);
        float w = UiKit.TextW(UiKit.ZenBold, _tutorialHint, 15) + 56;
        float x = 640 - w / 2f, y = 500, h = 38;
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.9f), 12f, new Color(UiKit.Mina, 0.4f + 0.4f * pulse), 1.4f);
        ci.DrawCircle(new Vector2(x + 20, y + h / 2f), 4.5f, new Color(UiKit.Mina, pulse));
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 34, y + 10), _tutorialHint, 15, new Color(0.94f, 0.92f, 0.99f));
    }

    private void DrawTicker(HudCanvas ci)
    {
        float barH = 38, y = 720 - barH;
        UiKit.VGradient(ci, new Rect2(0, y, 1280, barH),
            new[] { new Color(10 / 255f, 8 / 255f, 16 / 255f, 0f), new Color(10 / 255f, 8 / 255f, 16 / 255f, 0.82f) }, new[] { 0f, 1f });
        ci.DrawRect(new Rect2(0, y, 1280, 1f), new Color(UiKit.Kegare, 0.18f));
        // ラベル
        ci.DrawRect(new Rect2(0, y, 150, barH), new Color(UiKit.Kegare, 0.14f));
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(14, y + barH / 2f - 7), "降ってくる言葉", 12, new Color("f0a8cf"));
        // スクロール
        float startX = 164, gap = 40;
        float block = 0f;
        foreach (var (h, wd) in TickerWords) block += UiKit.TextW(UiKit.Mono, h, 12) + 6 + UiKit.TextW(UiKit.Zen, wd, 14) + gap;
        float scroll = ((float)_t * 70f) % block;
        float cx = startX - scroll + block; // 1ブロック先行
        for (int rep = 0; rep < 3; rep++)
        {
            foreach (var (h, wd) in TickerWords)
            {
                if (cx > 150 && cx < 1280)
                {
                    UiKit.Text(ci, UiKit.Mono, new Vector2(cx, y + barH / 2f - 7), h, 12, UiKit.Text3);
                    float hw = UiKit.TextW(UiKit.Mono, h, 12) + 6;
                    UiKit.Text(ci, UiKit.Zen, new Vector2(cx + hw, y + barH / 2f - 8), wd, 14, UiKit.Text2);
                }
                cx += UiKit.TextW(UiKit.Mono, h, 12) + 6 + UiKit.TextW(UiKit.Zen, wd, 14) + gap;
            }
        }
    }

    private void DrawDialog(HudCanvas ci)
    {
        int n = Mathf.Clamp(Mathf.FloorToInt(_dlgRevealed), 0, _dlgText.Length);
        string shown = _dlgText.Substring(0, n);

        if (!_dlgIsDialog)
        {
            // ナレーション：中央寄せの淡いテロップ（バー無し）
            UiKit.Box(ci, new Rect2(140, 600, 1000, 80), new Color(0.04f, 0.03f, 0.07f, 0.7f), 12f);
            UiKit.Multi(ci, UiKit.Zen, new Vector2(180, 618), shown, 20, new Color(0.9f, 0.9f, 0.95f), 920, 2);
            return;
        }

        // シネマ下部バー
        float x = 40, y = 540, w = 1200, h = 150;
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(0.05f, 0.04f, 0.09f, 0.95f), 16f, new Color(_dlgSpeakerCol, 0.5f), 1.4f);
        float textX = x + 36;
        // 立ち絵（あれば左に）。常時の微細な生命感：呼吸（上下揺れ）＋表情クロスフェード＋うなずき。
        // ここで描く立ち絵＝いま発話中の話者なので、揺れは「話者だけ」に自然に閉じる。
        if (_dlgPortrait != null)
        {
            float ph = h - 8, pw = ph * _dlgPortrait.GetWidth() / Mathf.Max(1, _dlgPortrait.GetHeight());
            float px = x + 10;
            // 呼吸：ゆっくりした上下のサイン。基準位置 y+4 を中心に ±BreathAmp。
            float breath = BreathAmp * Mathf.Sin((float)_t * (Mathf.Tau / BreathPeriod));
            // うなずき：完了直後に下→戻る。半周期 Sin の山（下が＋）。タイプ送り完了の相づち。
            float nod = 0f;
            if (_nodT > 0f)
                nod = NodAmp * Mathf.Sin((float)((NodTime - _nodT) / NodTime) * Mathf.Pi);
            float py = y + 4 + breath + nod;
            // 表情クロスフェード：旧絵をフェードアウトしつつ新絵をフェードイン（同じ揺れ位置で重ねる）。
            if (_portraitFadeT > 0f && _dlgPortraitPrev != null)
            {
                float f = Mathf.Clamp((float)(_portraitFadeT / PortraitFade), 0f, 1f); // 1→0
                float pwOld = ph * _dlgPortraitPrev.GetWidth() / Mathf.Max(1, _dlgPortraitPrev.GetHeight());
                ci.DrawTextureRect(_dlgPortraitPrev, new Rect2(px, py, pwOld, ph), false, new Color(1f, 1f, 1f, f));
                ci.DrawTextureRect(_dlgPortrait, new Rect2(px, py, pw, ph), false, new Color(1f, 1f, 1f, 1f - f));
            }
            else
            {
                ci.DrawTextureRect(_dlgPortrait, new Rect2(px, py, pw, ph), false);
            }
            textX = x + 10 + pw + 20;
        }
        if (_dlgSpeaker.Length > 0)
            UiKit.Text(ci, UiKit.ZenBold, new Vector2(textX, y + 18), _dlgSpeaker, 18, _dlgSpeakerCol);
        UiKit.Multi(ci, UiKit.Zen, new Vector2(textX, y + 52), shown, 22, new Color(0.95f, 0.95f, 0.98f), x + w - textX - 30, 3);
    }

    // 無防備窓サイクルの短い字幕（弾を止めない）。下部・話者色つきの一行カード。
    private void DrawBossLine(HudCanvas ci)
    {
        float a = Mathf.Clamp((float)_bossLineTimer * 2f, 0f, 1f);
        string sp = _bossLineSpeaker.Length > 0 ? _bossLineSpeaker + "  " : "";
        float spW = UiKit.TextW(UiKit.ZenBold, sp, 18);
        float tw = UiKit.TextW(UiKit.ZenBold, _bossLine, 20);
        float w = spW + tw + 36, x = 640 - w / 2f, y = 540, h = 38;
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.74f * a), 12f,
            new Color(_bossLineCol, 0.55f * a), 1.2f);
        if (sp.Length > 0)
            UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 18, y + 10), sp, 18, new Color(_bossLineCol, a));
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 18 + spW, y + 9), _bossLine, 20, new Color(UiKit.White, a));
    }

    private void DrawBanner(HudCanvas ci)
    {
        float a = Mathf.Clamp((float)_bannerTimer, 0f, 1f);
        float w = UiKit.TextW(UiKit.ZenBlack, _bannerText, 52);
        UiKit.Text(ci, UiKit.ZenBlack, new Vector2(640 - w / 2f, 300), _bannerText, 52, new Color(UiKit.Light, a),
            HorizontalAlignment.Left, -1);
        // クリアリザルトのタイム行（見出しの下）。
        if (_bannerTime.Length > 0)
        {
            UiKit.Text(ci, UiKit.Mono, new Vector2(0, 366), _bannerTime, 26, new Color(UiKit.PurifyHi, a),
                HorizontalAlignment.Center, 1280);
            if (_bannerBest.Length > 0)
            {
                Color bc = _bannerNewBest ? UiKit.Gold : UiKit.Text2;
                UiKit.Text(ci, UiKit.ZenBold, new Vector2(0, 402), _bannerBest, 20, new Color(bc, a),
                    HorizontalAlignment.Center, 1280);
            }
        }
    }
}

// HUD 描画用ノード（Hud にぶら下げ、Hud.DrawAll を呼ぶだけ）。
public partial class HudCanvas : Node2D
{
    public Hud Hud = null!;
    public override void _Draw() => Hud?.DrawAll(this);
}
