using Godot;
using System.Collections.Generic;

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
    // ゲームオーバー時の追加プロンプト（バナー直下）。「リトライ／ハブへ抜ける」の選択肢を出す。
    // *Root.cs が残機0を検知して ShowGameOverPrompt で立て、抜けキー受付中だけ表示する。
    private string _gameOverPrompt = "";

    // R 長押しリトライの充填率（0=非表示）。各 *Root.cs が毎フレーム SetRetryHold で渡す
    //（即発リトライは誤爆しやすい週次PT指摘→長押し化。押した瞬間からチップで進捗を見せる）。
    private float _retryHold;

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

    // ───────── スペル宣言カットイン（吉田明彦：袖から差し込むバストアップ。初回のみ）─────────
    //   既存スペルカード（上中央 DrawSpellCard）は置換せず共存。カットイン（袖）が一拍先に走り、カードが続く二段。
    //   弾フィールドは設計座標 1280x720 の全域（=384x216 を Scale 0.3 で投影）。物理的なベゼル帯が無いので、
    //   左袖に密着配置し「不透明は最初の一拍だけ／以降は速やかにフェード→0」で弾の視認を守る（総尺<1秒）。
    //   who→texture マップに Rei だけ登録。texture が無い who では従来どおりカードのみ（自然に Rei 限定）。
    private Texture2D? _cutinTex;              // 現在のカットイン絵（null の間は描かない）
    private Color _cutinCol = Colors.White;    // リム発光のキーカラー（発動スペルの tint）
    private double _cutinTimer;                // 残り時間（0 で消滅）
    private bool _cutinDoneThisBoss;          // このボス戦で既にカットインを出したか（初回のみ＝true で抑止）
    private string _cutinLine = "";           // カットインに合わせて出すキャラ別バトルセリフ
    private static Dictionary<string, (string path, string line)>? _cutinData; // who → (カットイン絵, セリフ)
    // 演出尺（すべて定数で調整可能）。anticipation→slide-in(BackOut)→着地flash+shake→hold→袖へ抜ける。
    // 5 段構成：slideIn → impactHold(不透明・大きく＝インパクト) → settle(α/位置を半透明・外寄りへ遷移)
    //          → lingerHold(半透明で端に滞在＝弾が透けて読める余韻) → fade(袖へ抜ける)。
    // 総尺は各段の合算（≒1.85s）。「ドンと読ませる→端で薄く余韻→抜ける」を尺・α・位置の定数で調整可能に。
    private const double CutinSlideDur   = 0.34;  // スライドイン（BackOut でオーバーシュート着地）。気持ち遅く
    private const double CutinImpactDur  = 0.40;  // 着地後の不透明ホールド一拍（インパクト＝読ませる）
    private const double CutinSettleDur  = 0.22;  // 不透明→半透明・着地→外寄りへ移る遷移
    private const double CutinLingerDur  = 0.55;  // 半透明で端に滞在（弾が透ける余韻）
    private const double CutinFadeDur    = 0.34;  // フォロースルー（袖へ抜けつつ α→0）
    private const double CutinDur        = CutinSlideDur + CutinImpactDur + CutinSettleDur + CutinLingerDur + CutinFadeDur; // 総尺（≒1.85s）
    private const float  CutinRenderH    = 460f;  // 描画高（設計座標1280x720）。袖に縦差しするバストアップ
    private const float  CutinSlideX     = 84f;   // 袖外→着地までの横移動量（px・設計座標）
    private const float  CutinHoldA      = 0.92f; // インパクト時の不透明（弾を完全には隠さない上限）
    private const float  CutinLingerA    = 0.30f; // 滞在時の半透明（α≈0.3＝弾が透けて見える）
    private const float  CutinLingerX    = 150f;  // 滞在時にさらに外（左）へ寄せる量（footprint 縮小）

    // フラッシュ
    private float _flashAlpha;
    private Color _flashRgb = new(1f, 1f, 1f);
    private double _hurtEdge; // 被弾エッジの残り時間

    // ヒカゲスキル
    private bool _skillHas, _skillReady;
    private bool _dodgeReady = true; // 回避がCD明けで使えるか（操作ガイドの点灯/淡色に使う。既定は使える）

    // ショットモード（現在モード表示＋切替トースト・設計書 §3-5）
    private GameManager.ShotMode _shotMode = GameManager.ShotMode.Rapid;
    private double _shotModeToast;
    private const double ShotModeToastDur = 2.0;

    // 会話／メッセージ
    private string _dlgText = "";       // 現在行の全文（ログ／既読判定用・ページ分割前）
    private string _dlgSpeaker = "";
    private Color _dlgSpeakerCol = Colors.White;
    private bool _dlgIsDialog;          // true=シネマバー / false=ナレーション（中央）
    private Texture2D? _dlgPortrait;
    private double _messageTimer;
    private float _dlgRevealed;         // タイプライター表示済み文字数（＝現在ページ内の文字数）

    // ページ送り（テキストボックスは全般2行固定。2行を超える行は2行ずつのページに割り、送りで続きを読ませる）。
    //   セリフ本文は削らず、WrapLines（禁則つき）で確定した行を DlgMaxLines 行ずつ束ねて1ページにする。
    //   DialogRevealed（この行を送ってよいか）＝「現在ページを出し切った かつ 最終ページ」。
    //   RevealDialogNow（Zの1段目）＝現在ページ未完なら全文表示／完了かつ非最終なら次ページへ。
    //   ＝各シーンの Step_Lines は無改造のまま、2段目送りが自然に「次ページ送り」に回る（既存契約を保つ）。
    public const int DlgMaxLines = 2;   // 1ページに収める最大行数（全ボックス共通）
    private readonly System.Collections.Generic.List<string> _dlgPages = new();
    private int _dlgPage;               // 現在表示中のページ index
    private string CurPageText => (_dlgPages.Count > 0 && _dlgPage < _dlgPages.Count) ? _dlgPages[_dlgPage] : _dlgText;
    private bool OnLastPage => _dlgPages.Count == 0 || _dlgPage >= _dlgPages.Count - 1;
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

    // ───────── 左上HUDの自動退避（視認性：弾と自機は常に最前面で見える）─────────
    //   HUD は CanvasLayer なので World の弾(Node2D)より必ず前面に描かれる。左上から降る雨弾は
    //   LIFE/BOMB・各チップの footprint を通過する間だけガラスパネル裏に隠れ「急に被弾」に見える。
    //   そこで「左上クラスタの占有域に敵弾が入っている間だけ、そのクラスタを薄く（半透明化）する」。
    //   弾が抜ければ元の不透明に滑らかに戻る＝可読性は通常時そのまま、弾が来た時だけ透ける。
    //   当たり判定・難易度・弾の挙動には一切触れない（見た目のフェードのみ）。
    private float _topLeftFade = 1f;            // 1=通常表示／TopLeftFadeMin まで弾接近で下がる
    private const float TopLeftFadeMin = 0.18f; // 退避時の最小α（うっすら残して位置だけ把握できる）
    private const float TopLeftFadeSpeed = 6f;  // 薄くなる方の追従速度（MoveToward/秒。速め）
    // 戻る（濃くなる）方はゆっくり。急に戻ってまた薄くなる“しゃくり”を抑える（フリッカ対策の主役その2）。
    private const float TopLeftRestoreSpeed = 1.6f;
    // 退避の保持（ヒステリシス）：弾がゾーンに居るフレームでこの秒数にリセットし、毎フレーム減算。
    // 0 より大きい間は弾が一瞬途切れても薄いまま＝弾の切れ目で濃くならずチカチカしない（フリッカ対策の主役）。
    private double _topLeftHold;
    private const double TopLeftHoldDur = 0.6;  // 弾が完全に途切れてから濃く戻り始めるまでの保持時間（秒）
    // 左上クラスタの占有域（設計座標1280x720）。LIFE/BOMB・ショット・やさしさ・目標・スキルの
    // 各チップを内包する矩形。弾がこの域に入っている間だけクラスタを薄くして弾を透かす。
    private static readonly Rect2 TopLeftZone = new Rect2(10, 12, 240, 240);

    // 操作ガイド：プレイ中ずっと右端に常駐する縦パネル（一度きりの旧タイマー方式は廃止）。
    // 会話・チュートリアル中は透過を下げて弾と説明の邪魔をしない（後述 DrawControls 参照）。
    // 出現は「操作を握った瞬間」から：開幕会話前の一瞬で出さないよう、会話を見た後 or 1.2秒経過後にフェードイン。
    private float _controlsAlpha;   // 0→1 へ滑らかに立ち上げる常駐パネルの基準α
    private double _sceneTime;
    private bool _sawDialogue;

    // チュートリアルの常駐指示（操作させる区間に下部へ出す小帯）。会話と違い敵/自機は止めない
    //（ShowMessage は BubblePaused を立ててしまうため、止めない専用の表示を用意する）。
    // 値が空でない間だけ描画する。チュートリアル中は既存 DrawControls（6.5秒一覧）を抑止する。
    private string _tutorialHint = "";
    public bool TutorialActive { get; set; }
    public void SetTutorialHint(string text) => _tutorialHint = text ?? "";
    public void ClearTutorialHint() => _tutorialHint = "";

    // チュートリアル（ステージ0）：今のステップの操作に割り当たった“全ボタン”を指示帯の上にバッジで出す。
    // StageZero が操作名（"move"/"shot"/"focus"/"dodge"/"bomb"/"kind"）をセット → ここで All* トークンに展開して描く。
    // KB/パッドの出し分けは Pad に従い、KB でも複数キー（Z/Space/Enter 等）はバッジ内に並べて全部見せる。
    // 説明会話中も実践中も出す（会話の停止/非停止に依らず、操作名が空でなければ描画）。
    private string _tutorialOp = "";
    public void SetTutorialOp(string op) => _tutorialOp = op ?? "";
    public void ClearTutorialOp() => _tutorialOp = "";

    // チュートリアル（ステージ0）のスポットライト暗転：全画面を暗幕で覆い、対象矩形だけ“避けて”見せる。
    // 説明会話中（停止中）だけ ON。MurkVignette は弾より奥なので流用不可＝CanvasLayer のここで描く（弾・自機より前面）。
    // α上限 0.55（弾・自機・ダミーが暗転で見えなくならないこと最優先）。
    private bool _spotActive;
    private Rect2 _spotRect;     // 設計座標(1280x720)。Size≈0 なら穴なし＝全画面を一様に暗転。
    private float _spotAlpha;
    public void SetSpot(Rect2 designRect, float darkAlpha)
    {
        _spotActive = true;
        _spotRect = designRect;
        _spotAlpha = Mathf.Min(0.55f, Mathf.Max(0f, darkAlpha));
    }
    public void ClearSpot() => _spotActive = false;

    // 操作子トークン（操作表示モードで KB / パッドを出し分け。パッドは Pad.Style に従い Xbox/PS 表記）。
    // 単体チップ（BOMB残数横・モード切替・スキル）用＝代表1表記。
    private static string TokShot  => Pad.UsingPad ? Pad.Face(JoyButton.A)            : "Z";
    private static string TokFocus => Pad.UsingPad ? Pad.Face(JoyButton.LeftShoulder) : "Shift";
    private static string TokBomb  => Pad.UsingPad ? Pad.Face(JoyButton.X)            : "X";
    private static string TokMode  => Pad.UsingPad ? Pad.Face(JoyButton.B)            : "V";
    private static string TokSkill => Pad.UsingPad ? Pad.Face(JoyButton.Y)            : "C";
    private static string TokMove  => Pad.UsingPad ? "L"                              : "WASD";
    private static string TokKind  => Pad.UsingPad ? Pad.Face(JoyButton.RightStick)   : "Ctrl";
    private static string TokDodge => Pad.UsingPad ? Pad.Face(JoyButton.LeftStick)    : "Alt";

    // 操作子トークン（全割り当て版）：選択中の表示モードに属する割り当てを“全部”並べる。
    // プレイ中HUDの操作ヒント（DrawControls）が使う。視認性のため区切りは細い「/」。
    private static string AllShot  => Pad.UsingPad ? Pad.Face(JoyButton.A)            : "Z / Space / Enter";
    private static string AllMove  => Pad.UsingPad ? "L"                              : "矢印 / WASD";
    private static string AllFocus => Pad.UsingPad ? $"{Pad.Face(JoyButton.LeftShoulder)} / {Pad.Face(JoyButton.RightShoulder)}" : "Shift";
    private static string AllBomb  => Pad.UsingPad ? Pad.Face(JoyButton.X)            : "X";
    private static string AllMode  => Pad.UsingPad ? Pad.Face(JoyButton.B)            : "V";
    private static string AllSkill => Pad.UsingPad ? Pad.Face(JoyButton.Y)            : "C";
    private static string AllKind  => Pad.UsingPad ? Pad.Face(JoyButton.RightStick)   : "Ctrl";
    // 回避ダッシュは Player.cs では Alt / Pad L3(LeftStick) の2系統。Tok* と違い“全部”を見せる版。
    private static string AllDodge => Pad.UsingPad ? Pad.Face(JoyButton.LeftStick)    : "Alt";

    // ティッカー（降ってくる言葉）＝「Xの川」の共有ノイズプール。
    // 「下に流れているコメント」と「投稿弾」が同じ“声”を出すため、投稿弾もここから引く（PostBullets）。
    // #11 文面改稿（maeda）：バズ・断片・広告っぽい軽さ7 : 沈む一言3。個人特定・死の直接言及・ボス本人の声は入れない
    //（旧「あたしのせいだ」「なんで庇ったの」は本人特定に近いため撤去）。ハンドル空欄はティッカー側で幅を詰める（TickerHandleW）。
    private double _t;
    public static readonly (string h, string w)[] TickerWords =
    {
        ("", "それな"), ("", "拡散希望"), ("", "バズる呪文おしえて"), ("", "【広告】幸せ、届きます"),
        ("", "はいはい優勝優勝"), ("", "だれか、みてる?"), ("", "どうせ、とどかない"), ("", "きえたい"),
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
        if (_bossBarFlash > 0) _bossBarFlash -= delta; // バー1本割れの白フラッシュ減衰（#26）

        // 既読高速送り中は現在ページを即時全表示し、後続ページも自動で進める（行送り自体は Step_Lines が FastForwarding を見て進める）。
        if (FastForwarding)
        {
            _dlgRevealed = CurPageText.Length;
            if (!OnLastPage) AdvanceDialogPage();   // 既読は全ページを一気に抜けて最終ページ完了状態へ
        }

        // タイプライター送り（現在ページ内の文字数を進める）
        if (_messageTimer > 0 && _dlgText.Length > 0 && _dlgRevealed < CurPageText.Length)
        {
            _dlgRevealed = Mathf.Min(CurPageText.Length, _dlgRevealed + (float)delta * (_game?.MsgCharsPerSec ?? CharsPerSec));
            // 文字が新たに出た瞬間だけ、TypeStride 文字に1回、話者の音色で送り音（Voiceバス）。
            // ナレ（Narration）は PlayType 側で無音。即時全文表示（RevealDialogNow）は差分が一気に増えるが
            // 「1ストライド境界を跨いだか」だけで判定するので、増分の数だけ連打しない＝大量再生を防ぐ。
            int rev = Mathf.FloorToInt(_dlgRevealed);
            if (rev > _typePrevRevealed)
            {
                if (rev < CurPageText.Length && rev / TypeStride != _typePrevRevealed / TypeStride)
                    Audio.Instance?.PlayType(_dlgKind);
                _typePrevRevealed = rev;
            }
        }

        // 立ち絵の生命感タイマー（見た目のみ・進行に無影響）。
        if (_portraitFadeT > 0) _portraitFadeT -= delta;        // 表情クロスフェードの残り
        if (_nodT > 0) _nodT -= delta;                          // うなずきの残り
        // うなずき：その行のタイプ送りが「いま完了した瞬間」だけ1回トリガ。
        bool revealDone = _messageTimer > 0 && _dlgIsDialog && _dlgPortrait != null
                          && _dlgText.Length > 0 && _dlgRevealed >= CurPageText.Length;
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
        // カットインも会話バブル中は時間を止める（カードと同じく“戦闘の瞬間”に確実に見せる）。
        if (_cutinTimer > 0 && !BubblePaused) { _cutinTimer -= delta; if (_cutinTimer <= 0) _cutinTex = null; }
        if (_shotModeToast > 0) { _shotModeToast -= delta; }

        // 操作ガイド（常駐）：プレイ中ずっと右端に出す。立ち上げは「操作を握った瞬間」から。
        //   ・会話を一度見た後、または会話なしステージでは1.2秒経過後、かつ自機が存在する間。
        //   ・会話／チュートリアル中は α を絞って弾と説明を邪魔しない（親切設計に追従）。
        _sceneTime += delta;
        if (BubblePaused) _sawDialogue = true;
        bool wantControls = (_sawDialogue || _sceneTime > 1.2)
                            && GetTree().GetFirstNodeInGroup("player") != null;
        // 目標α：通常=1.0／会話中=0.22（裏で薄く残す）／チュートリアル中=0.0（個別指導と重複を避け完全に引く）。
        float targetAlpha = !wantControls ? 0f
                          : TutorialActive ? 0f
                          : BubblePaused ? 0.22f
                          : 1f;
        _controlsAlpha = Mathf.MoveToward(_controlsAlpha, targetAlpha, (float)delta * 3.2f);

        // やさしさゲージの演出更新（全開トースト＋グレイズで貯まる手応え）
        if (_game?.JustOverloaded ?? false) { _overloadToast = 1.4; Audio.Instance?.PlayOverload(); } // ⑥ピークの告知
        if (_overloadToast > 0) _overloadToast -= delta;
        float kNow = _game?.Kindness ?? 0f;
        if (!(_game?.IsOverload ?? false) && kNow > _prevKind + 0.001f) _kindPulse = 1f;
        _prevKind = kNow;
        if (_kindPulse > 0) _kindPulse = Mathf.Max(0f, _kindPulse - (float)delta * 4f);

        // 左上HUDの自動退避：敵弾が左上クラスタの占有域に入っている間だけ薄くする（弾を透かす）。
        // 弾位置は World 座標(384x216)。設計座標の占有域を World へ畳んで内外判定する。
        UpdateTopLeftFade((float)delta);

        _canvas.QueueRedraw();
    }

    private void ClearEnemyBullets()
    {
        var pool = GetNodeOrNull<BulletPool>("/root/Pool");
        if (pool == null) return;
        foreach (Node n in GetTree().GetNodesInGroup("enemy_bullets"))
            if (n is Bullet b && b.Active) pool.Despawn(b);
    }

    // 左上クラスタに敵弾が掛かっているかを判定し、掛かっていれば _topLeftFade を下げる（透かす）。
    // 占有域 TopLeftZone は設計座標(1280x720)。弾は World 座標(384x216)なので Scale(0.3) で世界へ畳む。
    // 余白 BulletPad は弾半径＋αで、弾がパネル縁に差し掛かった瞬間から透け始める“先読み”。
    private void UpdateTopLeftFade(float delta)
    {
        // 占有域を World 座標へ変換（design * Scale）。
        Rect2 zone = new Rect2(TopLeftZone.Position * UiKit.Scale, TopLeftZone.Size * UiKit.Scale);
        const float bulletPad = 6f; // 弾半径ぶんの先読み余白（World px）
        zone = zone.Grow(bulletPad);

        bool occluded = false;
        foreach (Node n in GetTree().GetNodesInGroup("enemy_bullets"))
        {
            if (n is Bullet b && b.Active && zone.HasPoint(b.GlobalPosition)) { occluded = true; break; }
        }

        // ヒステリシス：弾が居れば保持タイマーを満タンに。居なくても保持が残っている間は薄いまま。
        // これで弾の切れ目（数フレームの空白）で濃く戻らず＝チカチカが消える。
        if (occluded) _topLeftHold = TopLeftHoldDur;
        else if (_topLeftHold > 0) _topLeftHold -= delta;

        bool stayFaded = occluded || _topLeftHold > 0;
        float target = stayFaded ? TopLeftFadeMin : 1f;
        // 薄くなる時は速く、濃く戻る時はゆっくり（戻り中の再退避による“しゃくり”を抑える）。
        float speed = (_topLeftFade > target) ? TopLeftFadeSpeed : TopLeftRestoreSpeed;
        _topLeftFade = Mathf.MoveToward(_topLeftFade, target, delta * speed);
    }

    private void ClearDialog()
    {
        _dlgText = ""; _dlgSpeaker = ""; _dlgPortrait = null; _dlgRevealed = 0;
        _dlgPortraitPrev = null; _portraitFadeT = 0; _nodT = 0; _revealWasDone = false;
        _dlgPages.Clear(); _dlgPage = 0;
    }

    // ───────── テキストボックスの行の種類 ─────────
    public enum LineKind { Boy = 0, Mina = 1, Other = 2, Narration = 3, Post = 4, Relay = 5 }

    // ───────── 会話ログ（バックログ）─────────
    // ストーリー重視ゲームの読み返し用に、表示済みの会話/ナレ/投稿を蓄積する（ADV のバックログ相当）。
    // SetDialog を通る全行（who=話者名/text/col=話者色/kind=種別）をここに積む。Backlog 画面が参照する。
    // シーンを跨いで保持したいので static（ゲーム1周ぶん）。古い行は上限で先頭から捨てる。
    public readonly record struct LogLine(string Speaker, string Text, Color Color, LineKind Kind);
    private static readonly List<LogLine> _backlog = new();
    private const int BacklogMax = 200;
    public static System.Collections.Generic.IReadOnlyList<LogLine> Backlog => _backlog;

    // 1行を会話ログへ積む（空テキスト＝クリアは積まない／直前と完全同一の連続行も積まない）。
    private static void PushBacklog(string speaker, string text, Color col, LineKind kind)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (_backlog.Count > 0)
        {
            var last = _backlog[^1];
            if (last.Text == text && last.Speaker == speaker) return; // 同一行の二重表示は弾く
        }
        _backlog.Add(new LogLine(speaker, text, col, kind));
        if (_backlog.Count > BacklogMax) _backlog.RemoveRange(0, _backlog.Count - BacklogMax);
    }
    public static void ClearBacklog() => _backlog.Clear();

    // 種別→ログ表示色（既存トークン/UiKit に合わせる。ShowDialog(LineKind) の色分けと一致）。
    public static Color KindColor(LineKind k) => k switch
    {
        LineKind.Boy   => UiKit.Info,
        LineKind.Mina  => UiKit.Mina,
        LineKind.Other => UiKit.Kegare,
        LineKind.Relay => UiKit.Info,
        LineKind.Post  => UiKit.Text3,
        _              => UiKit.Text2, // Narration（ナレ＝ミナの語り）は淡色
    };

    // 会話ログに出す話者ラベル。speaker が空（素の ShowDialog 経路）でも種別から補う。
    private static string BacklogSpeaker(LineKind k, string speaker)
    {
        if (!string.IsNullOrEmpty(speaker)) return speaker;
        return k switch
        {
            LineKind.Boy   => "少年",
            LineKind.Mina  => "ミナ",
            LineKind.Relay => "少年（ミナの声）",
            LineKind.Post  => "Ｘ 投稿",
            LineKind.Narration => "ナレーション",
            _              => "",
        };
    }

    public void ShowMessage(string text)
    {
        // 立ち絵なしの中央メッセージ＝ナレ扱い（地の文。会話ログにもナレとして残す）。
        SetDialog(text, "", default, dialog: false, portrait: "", kind: LineKind.Narration);
        _messageTimer = 4.5;
    }

    // 立ち絵付きの素の会話（少年/ヒカゲ等、LineKind を取らない旧経路）。
    // 送り音は従来どおり無音（Narration）に保つが、会話ログには発話として残るよう logKind=Boy で積む。
    public void ShowDialog(string text) => ShowDialog(text, "res://char/algo_cutout.png");

    public void ShowDialog(string text, string portraitResPath)
    {
        SetDialog(text, "", default, dialog: true, portrait: portraitResPath, kind: LineKind.Narration, logKind: LineKind.Boy);
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
        SetDialog(text, speaker, color, dialog, portraitToUse, kind);
        _messageTimer = 6.0;
    }

    // kind   … 送り音の音色（＝表示中の話者。Narration は無音）。
    // logKind … 会話ログに残すときの種別（既定で kind と同じ。送り音は無音にしたいが
    //            ログ上は発話として残したい旧経路（少年/ヒカゲの ShowDialog(string)）で使い分ける）。
    private void SetDialog(string text, string speaker, Color speakerCol, bool dialog, string portrait,
        LineKind kind = LineKind.Narration, LineKind? logKind = null)
    {
        // 表示前に会話ログ（バックログ）へ積む。話者色は未指定（default＝ナレ）のとき種別から補う。
        // ※既読スキップ（高速送り）で飛ばした行もここを通る＝バックログには必ず残る。
        LineKind lk = logKind ?? kind;
        Color logCol = speakerCol.A <= 0f ? KindColor(lk) : speakerCol;
        PushBacklog(BacklogSpeaker(lk, speaker), text, logCol, lk);
        // 既読スキップ（#22）：この行が過去に表示済みかを先に控え（＝高速送りの可否は表示前の状態で決める）、
        // 表示と同時に既読へ記録する（read.json・全スロット共有）。
        _dlgReadBefore = _game?.IsLineRead(text) ?? false;
        _game?.MarkLineRead(text);
        _dlgText = text; _dlgSpeaker = speaker; _dlgSpeakerCol = speakerCol;
        _dlgIsDialog = dialog; _dlgRevealed = 0;
        // 新しい行＝送り音の差分検出をリセット。送り音の音色は kind（Narration＝無音）。
        _typePrevRevealed = 0; _dlgKind = kind;
        Texture2D? next = string.IsNullOrEmpty(portrait) ? null : ResourceLoader.Load<Texture2D>(portrait);
        // ページ分割：本文が入る幅を確定し、2行ずつのページへ割る（送り機構は DialogRevealed/RevealDialogNow で駆動）。
        //   幅は DrawDialog のレイアウトと一致させる（ナレ＝中央920 ／ セリフ＝バー幅から話者列・立ち絵を引いた実効幅）。
        BuildDialogPages(dialog, next);
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

    // 本文を DlgMaxLines(=2) 行ずつのページへ分割する。折り返しは DrawDialog の実効幅と一致させる
    //（＝画面に出る行構成と分割位置がズレない）。禁則は WrapLines が担保。ページは元の行を \n で束ねた文字列。
    private void BuildDialogPages(bool dialog, Texture2D? portrait)
    {
        _dlgPages.Clear();
        _dlgPage = 0;
        // DrawDialog と同じジオメトリで本文の折り返し幅を求める。
        float wrapW;
        if (!dialog)
        {
            wrapW = 920f;                                   // ナレ（中央テロップ）
        }
        else
        {
            const float x = 40f, w = 1200f, h = 170f;
            float textX = x + 36f;
            if (portrait != null)
            {
                float ph = h - 8f;
                float pw = ph * portrait.GetWidth() / Mathf.Max(1, portrait.GetHeight());
                textX = x + 10f + pw + 20f;
            }
            wrapW = x + w - textX - 30f;                    // DrawDialog の MultiLeading 幅と一致
        }
        _dlgPages.AddRange(UiKit.Paginate(UiKit.Zen, _dlgText, UiKit.FontHeading, wrapW, DlgMaxLines));
    }

    // 会話送り（ステージの Step_Lines から使う）：現在ページを出し切った かつ 最終ページなら「この行は読了＝次の行へ」。
    //   後続ページが残る間は false を返す＝Step_Lines の2段目送りが RevealDialogNow に回り、次ページへ進む。
    public bool DialogRevealed =>
        _dlgText.Length == 0 || (OnLastPage && _dlgRevealed >= CurPageText.Length);

    // Zの1段目：現在ページが未完なら全文表示。完了していて後続ページがあるなら次ページへ送る。
    public void RevealDialogNow()
    {
        if (_dlgText.Length == 0) return;
        if (_dlgRevealed < CurPageText.Length) { _dlgRevealed = CurPageText.Length; return; }
        if (!OnLastPage) AdvanceDialogPage();
    }

    // 次ページへ（タイプライターを頭から。送り音の差分検出もリセット）。
    private void AdvanceDialogPage()
    {
        _dlgPage++;
        _dlgRevealed = 0;
        _typePrevRevealed = 0;
        _revealWasDone = false;
    }

    public bool AutoAdvance => _game?.AutoAdvanceDialog ?? false;

    // ───────── 既読スキップ（2周目の高速送り・Epic G #22）─────────
    //   Ctrl（左右どちらも）/ パッド RB を「押しっぱなし」の間、既読の行だけ高速送りする。
    //   未読行では効かない＝誤スキップで物語を取りこぼさせない（判定は行単位・表示前の既読状態）。
    //   Ctrl はやさしさ全開と同キーだが、全開はエッジ検出＋会話中(BubblePaused)無効（Player.cs）なので衝突しない。
    //   DemoPilot/QaPilot は Z/X と移動軸しか送出しない＝自動プレイの会話送りとは干渉しない。
    private bool _dlgReadBefore;   // 現在行が「表示された時点で」既読だったか（SetDialog で確定）
    public static bool SkipHeld => Input.IsKeyPressed(Key.Ctrl) || Pad.Pressed(JoyButton.RightShoulder);
    public bool FastForwarding => SkipHeld && _dlgReadBefore && _messageTimer > 0 && _dlgText.Length > 0;

    public void ShowBanner(string text) { _bannerText = text; _bannerTimer = 5.0; _bannerTime = ""; _bannerBest = ""; _epic = false; }

    // FINAL 専用の「格上」タイトルカード。通常バナー（出て消えるだけの一行）とは別の描画経路に入る。
    //   ダサさの正体＝①全ステージ共通のベタ一行で FINAL に重みが無い ②字間0で小さく詰まって見える
    //   ③原色寄りの金1色でベタ塗り＝安い ④間(ため)が無く出た瞬間が頂点 ⑤画面が反応しない。
    // 対処＝黒レターボックス＋横罫で「額装」し、タグと副題を分離。字間を開けた一文字ずつの滲み出し、
    //   色収差(R/Cのズレ)＋走査線＋弱いグロー、そして最後に“ため”てから静かに引く。
    public void ShowEpicBanner(string tag, string sub, Color accent)
    {
        _epic = true; _epicTag = tag; _epicSub = sub; _epicAccent = accent;
        _bannerText = tag + " — " + sub; // バックログ/互換用に文字列は保持
        _bannerTimer = EpicDur; _bannerTime = ""; _bannerBest = "";
    }

    private bool _epic;
    private string _epicTag = "", _epicSub = "";
    private Color _epicAccent = UiKit.Kegare;
    private const double EpicDur = 5.2;   // 0.0 暗転寄せ → 1.0 タグ合わせ → 2.4 副題滲み → 3.4 ため → 5.2 引き


    // ゲームオーバー中の追加プロンプト（バナー直下）。空文字でクリア。*Root.cs が毎フレーム立てる。
    public void ShowGameOverPrompt(string text) { _gameOverPrompt = text; }

    // R 長押しリトライの充填率（0..1）。*Root.cs が毎フレーム渡す（0 で非表示）。
    public void SetRetryHold(float frac) { _retryHold = Mathf.Clamp(frac, 0f, 1f); }

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
        // 初回のみ：そのボス戦で最初の単独スペル宣言に、専用絵を持つ who だけ袖カットインを乗せる。
        TryShowSpellCutin(who, col);
    }

    // このボス戦で「最初の1回だけ」カットインを出す。who に専用カットイン絵が登録されていなければ何もしない
    //（＝texture の有無で自然に Rei 限定になる）。DemoPilot/QA では出さない（自動プレイの尺を汚さない）。
    private void TryShowSpellCutin(string who, Color col)
    {
        if (_cutinDoneThisBoss) return;
        if (IsAutoplay()) return;                       // --demo/--qa はスキップ
        // who → (専用カットイン絵, カットインに合わせたバトルセリフ)。絵が無い who はカードのみ（自然にスキップ）。
        _cutinData ??= new Dictionary<string, (string, string)>
        {
            ["レイ"]   = ("res://char/cutin_rei.png",    "——追いつけるものなら、どうぞ？"),
            ["あかり"] = ("res://char/cutin_akari.png",  "ねえ……まだ、そこにいる？"),
            ["こはる"] = ("res://char/cutin_koharu.png", "ぜんぶ食べてね。のこしちゃ、だめ。"),
            ["ミナ"]   = ("res://char/cutin_mina.png",   "ご主人様……見ていてくださいね。"),
        };
        if (!_cutinData.TryGetValue(who, out var d)) return;
        var tex = ResourceLoader.Load<Texture2D>(d.path);
        if (tex == null) return;                        // 絵が無ければカードのみ（従来どおり）
        _cutinTex = tex; _cutinCol = col; _cutinTimer = CutinDur; _cutinLine = d.line;
        _cutinDoneThisBoss = true;                      // 以降このボス戦では出さない
    }

    // ボス戦開始でフラグをリセット（次のボス戦の初回宣言で再びカットインが出る）。各ボスの ShowBossBar 経由でも可。
    public void ResetSpellCutin() { _cutinDoneThisBoss = false; _cutinTex = null; _cutinTimer = 0; _cutinLine = ""; }

    private static bool IsAutoplay()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") return true;
        return false;
    }

    public void SetHikageSkill(bool has, bool ready) { _skillHas = has; _skillReady = ready; }
    public void SetDodgeReady(bool ready) => _dodgeReady = ready;

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
        _bossTint = null; _bossBarFlash = 0; // 次のボスへ前ボスのスペル色/フラッシュを持ち越さない
        ResetSpellCutin();   // ボス戦開始＝このボス戦のカットイン初回フラグをリセット
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
    // スペル宣告カード（＋袖カットイン）を即時に消す。会話バブル中は _spellTimer が停止する仕様のため、
    // 改心開始（各ボス OnCryStart）で明示的に消さないと、宣告カードが改心演出〜帰還会話まで残留する。
    public void HideSpellCard() { _spellTimer = 0; _spellGlow = 0; _cutinTimer = 0; _cutinTex = null; }

    // ── フェーズ移行の可視化（#26）──
    // 現行スペルの色をHPバーへ連動させる（各ボスの ApplySpell が呼ぶ）。null=既定の穢れ色。
    private Color? _bossTint;
    public void SetBossBarTint(Color c) => _bossTint = c;
    // HPバー1本割れの白フラッシュ（Enemy の本体ヒットでバー境界を跨いだ瞬間に焚く）。
    private double _bossBarFlash;
    private const double BossBarFlashDur = 0.32;
    public void FlashBossBarBreak() => _bossBarFlash = BossBarFlashDur;

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
        if (_cutinTimer > 0 && _cutinTex != null) DrawSpellCutin(ci); // 袖カットイン（カードより先＝上中央カードを侵さない）
        if (_spellTimer > 0) DrawSpellCard(ci);
        DrawShotMode(ci);
        DrawKindness(ci);
        DrawGoal(ci);
        if (_skillHas) DrawSkill(ci);
        DrawTicker(ci);
        if (_tutorialHint.Length > 0) DrawTutorialHint(ci);
        if (_tutorialOp.Length > 0) DrawTutorialKeys(ci);
        if (_controlsAlpha > 0.01f) DrawControls(ci);
        if (_shotModeToast > 0) DrawShotModeToast(ci);
        if (_overloadToast > 0) DrawOverloadToast(ci);
        // チュートリアルのスポット暗転は会話/バナーより前(下)に描く＝会話テキスト・立ち絵は
        // 暗幕の上にフル輝度で読める（暗転がセリフ枠を覆って読みづらい問題への対処 #3）。
        // 弾より前だが、α上限0.55で弾は透ける。会話ボックス矩形も穴抜きの対象にして二重に保護する。
        if (_spotActive) DrawTutorialSpot(ci);
        if (_dlgText.Length > 0) DrawDialog(ci);
        if (_bossLineTimer > 0 && _bossLine.Length > 0) DrawBossLine(ci);
        if (_bannerTimer > 0) DrawBanner(ci);
        if (_gameOverPrompt.Length > 0) DrawGameOverPrompt(ci);
        if (_retryHold > 0f) DrawRetryHoldChip(ci, _retryHold, "R 長押しでリトライ");
        // 被弾エッジ
        if (_hurtEdge > 0)
            UiKit.Box(ci, new Rect2(8, 8, 1280 - 16, 720 - 16), null, 18f, new Color(0.9f, 0.16f, 0.16f, 0.5f * (float)(_hurtEdge / 0.9)), 14f);
        // フラッシュ（全画面・最前面）
        if (_flashAlpha > 0f)
            ci.DrawRect(new Rect2(0, 0, 1280, 720), new Color(_flashRgb.R, _flashRgb.G, _flashRgb.B, _flashAlpha));
        UiKit.EndDesign(ci);
    }

    private void GlassPanel(HudCanvas ci, Rect2 r, Color? border = null)
        => UiKit.Box(ci, r, Fa(new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.62f)), 16f, Fa(border ?? new Color(1, 1, 1, 0.12f)), 1f);

    // 左上クラスタの自動退避用：色のαに _topLeftFade を乗じる（弾接近時だけ薄くなる）。
    // 左上の5要素（LIFE/BOMB・ショット・やさしさ・目標・スキル）の描画色はこれを通す。
    private Color Fa(Color c) => new Color(c.R, c.G, c.B, c.A * _topLeftFade);

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
        // LIFE/BOMB は最重要情報のため弾接近の自動退避(fade)を掛けず常時不透明(α=1)で描く。
        // 他の左上要素(やさしさ/目標/ショット/スキル)は従来どおり Fa() で透ける。
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.62f), 16f,
            low ? new Color(1f, 0.35f, 0.42f, 0.4f) : new Color(1, 1, 1, 0.12f), 1f);
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
        KeyBadge(ci, new Vector2(x + w - badgeW - 10, y + 50), TokBomb, UiKit.Mina, 1f);
    }

    private void DrawPurify(HudCanvas ci)
    {
        float prog = _game?.StageProgress ?? 0f;
        bool full = prog >= 0.999f;
        float capW = 420, x = 640 - capW / 2f, y = 20, h = 30;
        // 前のめり可視化（数字なし・控えめ）：自機が右へ寄る（＝ゲージが速く伸びる）ほど縁の光を速く/強く脈動させる。
        //   posFactor 0.55(左端)→1.60(右端) を 0..1 に均し、脈動Hz(2.4→7.0)と縁の明るさに薄く乗せる。full 時は従来演出優先。
        float posF = _game?.CurrentPosFactor ?? 1.075f;
        float lean = Mathf.Clamp((posF - 0.55f) / 1.05f, 0f, 1f); // 左端0 → 右端1
        float pulseHz = Mathf.Lerp(2.4f, 7.0f, lean);
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_t * pulseHz);
        Color edge = full
            ? new Color(UiKit.PurifyHi, 0.9f)
            : new Color(UiKit.Purify, Mathf.Lerp(0.12f, 0.12f + 0.30f * lean, pulse)); // 右ほど明滅の振れ幅が大きい
        UiKit.Box(ci, new Rect2(x, y, capW, h), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.62f), 15f,
            edge, full ? 1.5f : 1f);
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
        bool showCombo = combo >= 2;
        string c1 = "♥ " + UiKit.Abbrev(imp);
        string c2 = showCombo ? $"× {combo}" : UiKit.Abbrev(_game?.Followers ?? 0);
        float cy = y + 44;
        // 右チップ：コンボ未満はフォロワー＝極小「人」を添えて正体を示す（フォロワーはハブで常時見えるので控えめに）。
        string c2suffix = showCombo ? "" : "人";
        float c2w = 30 + UiKit.TextW(UiKit.Mono, c2, 11) + (c2suffix.Length > 0 ? UiKit.TextW(UiKit.Zen, c2suffix, 9) + 2 : 0);
        float c2x = 1280 - 22 - c2w;
        UiKit.Box(ci, new Rect2(c2x, cy, c2w, 22f), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.5f), 11f, new Color(UiKit.Mina, 0.4f), 1f);
        UiKit.Text(ci, UiKit.Mono, new Vector2(c2x + 12, cy + 5), c2, 11, new Color("c8b0ec"));
        if (c2suffix.Length > 0)
            UiKit.Text(ci, UiKit.Zen, new Vector2(c2x + 12 + UiKit.TextW(UiKit.Mono, c2, 11) + 2, cy + 7), c2suffix, 9, new Color("c8b0ec", 0.75f));
        // 左チップ：♥＝通貨（浄化した心）。無ラベルだと通貨と分からないので極小「心」を1字添える。
        const string c1suffix = "心";
        float c1w = 30 + UiKit.TextW(UiKit.Mono, c1, 11) + UiKit.TextW(UiKit.Zen, c1suffix, 9) + 2;
        float c1x = c2x - 7 - c1w;
        UiKit.Box(ci, new Rect2(c1x, cy, c1w, 22f), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.5f), 11f, new Color(UiKit.Purify, 0.4f), 1f);
        UiKit.Text(ci, UiKit.Mono, new Vector2(c1x + 12, cy + 5), c1, 11, UiKit.Info);
        UiKit.Text(ci, UiKit.Zen, new Vector2(c1x + 12 + UiKit.TextW(UiKit.Mono, c1, 11) + 2, cy + 7), c1suffix, 9, new Color(UiKit.Info, 0.8f));
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
        // バー/pip の色は現行スペルの色に連動（#26 フェーズ移行の可視化。未設定なら既定の穢れ色）。
        Color barCol = _bossTint ?? UiKit.Kegare;
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(tx, y + 36), "穢れ", 10, new Color("f0a8cf"));
        float pipsW = _bossBarsTotal * 9f;
        float barX = tx + 34, barW = w - (barX - x) - 66 - pipsW, barY = y + 37;
        UiKit.Box(ci, new Rect2(barX, barY, barW, 10f), new Color(1, 1, 1, 0.07f), 5f);
        if (_bossFrac > 0) UiKit.Box(ci, new Rect2(barX, barY, barW * _bossFrac, 10f), barCol, 5f);
        // バー1本割れの白フラッシュ（割れた一拍を「ゲージが光る」で読ませる）。
        if (_bossBarFlash > 0)
        {
            float f = (float)(_bossBarFlash / BossBarFlashDur);
            UiKit.Box(ci, new Rect2(barX, barY, barW, 10f), new Color(1f, 1f, 1f, 0.7f * f), 5f);
        }
        // 残バー pip（左から「残っている本数」を満たす）。
        float pipX = barX + barW + 8f;
        for (int i = 0; i < _bossBarsTotal; i++)
            ci.DrawCircle(new Vector2(pipX + i * 9f + 3f, barY + 5f), 3f,
                i < barsLeft ? barCol : new Color(barCol, 0.22f));
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

    // スペル宣言の袖カットイン（吉田明彦：anticipation→slide-in→着地flash+shake→hold→袖へ抜ける）。
    //   左袖に密着し、不透明は着地〜ホールドの一拍だけ。以降はフェード＋袖へ戻りつつ消える＝弾の視認を守る。
    //   設計座標 1280x720。弾フィールドは全域だが、左端密着＋短命フェードで実プレイ妨害を最小化する。
    private void DrawSpellCutin(HudCanvas ci)
    {
        if (_cutinTex == null) return;
        double age = CutinDur - _cutinTimer;                 // 経過（0→CutinDur）

        // 5 段のリズム。slideIn(BackOut でオーバーシュート＝決め) → impactHold(不透明・着地で大きく＝読ませる)
        // → settle(不透明→半透明・着地→外寄りへ遷移) → lingerHold(半透明で端に滞在＝弾が透ける余韻)
        // → fade(さらに袖へ抜けつつ α→0)。位相境界をタイムラインで先に決める。
        const double tImpactEnd = CutinSlideDur + CutinImpactDur;                 // 不透明区間の終わり
        const double tSettleEnd = tImpactEnd + CutinSettleDur;                    // 半透明遷移の終わり
        const double tLingerEnd = tSettleEnd + CutinLingerDur;                    // 滞在の終わり（以降 fade）

        float a;       // 不透明度
        float dx;      // 横オフセット（負＝袖の外へ。0＝インパクト着地。さらに負＝外へ逃がす）
        bool justLanded = false;
        if (age < CutinSlideDur)
        {
            float p = (float)(age / CutinSlideDur);          // 0→1
            float bo = BackOut(p);
            dx = -CutinSlideX * (1f - bo);                   // 袖外(-Slide)→着地(0)。終端で気持ち食い込んで戻る
            a = Mathf.Clamp(p / 0.4f, 0f, 1f) * CutinHoldA;  // 立ち上がりは速く
        }
        else if (age < tImpactEnd)
        {
            dx = 0f; a = CutinHoldA;                          // 不透明・大きく見せるインパクト一拍
            justLanded = age < CutinSlideDur + 0.05;
        }
        else if (age < tSettleEnd)
        {
            float p = (float)((age - tImpactEnd) / CutinSettleDur); // 0→1
            float e = p * p * (3f - 2f * p);                  // smoothstep
            dx = -CutinLingerX * e;                           // 着地(0)→外寄り(-LingerX) で footprint 縮小
            a = Mathf.Lerp(CutinHoldA, CutinLingerA, e);      // 不透明→半透明
        }
        else if (age < tLingerEnd)
        {
            dx = -CutinLingerX; a = CutinLingerA;             // 端で半透明滞在（弾が透けて読める余韻）
        }
        else
        {
            float p = (float)(_cutinTimer / CutinFadeDur);   // 1→0
            dx = -CutinLingerX - CutinSlideX * 0.7f * (1f - p); // さらに袖へ抜けながら
            a = CutinLingerA * p;                            // 半透明から α→0
        }

        // 着地の瞬間：白フラッシュ1F＋既存 Shake（演出ビートの“止め／決め”）。描画ループ内なので一度だけ立てる。
        if (justLanded && !_cutinLandedFlashed)
        {
            _cutinLandedFlashed = true;
            _flashRgb = new Color(1f, 1f, 1f); _flashAlpha = 0.28f; // 控えめな白フラッシュ（弾を飛ばさない程度）
            GameCamera.Instance?.Shake(2.2f, 0.16f);
        }
        if (age < CutinSlideDur) _cutinLandedFlashed = false;       // 次回着地で再びフラッシュできるよう戻す

        // 描画寸法：高さ CutinRenderH を基準に元アスペクトで幅算出。左端に密着（x の左端 = base + dx）。
        float ph = CutinRenderH;
        float pw = ph * _cutinTex.GetWidth() / Mathf.Max(1, _cutinTex.GetHeight());
        // 縦位置：LIFE/BOMB・モードチップ（左上 y<240）を避け、やや下へ。画面下に少し掛けて“袖から覗く”量感。
        float baseX = -10f;                                  // 左端を少しだけ画面外に出して密着感
        float x = baseX + dx;
        float y = 720f - ph - 6f;                            // 下端を画面下に寄せる（袖から立ち上がる構図）

        // リムの色を一点：着地直後（インパクト区間）だけスペル tint の加算グローを輪郭背後に薄く（弾は隠さない）。
        // 半透明滞在では glow を消す（余韻は静かに・弾を侵さない）。
        float glow = Mathf.Clamp((float)((tImpactEnd - age) / CutinImpactDur), 0f, 1f);
        if (glow > 0.001f)
            UiKit.RadialGlow(ci, new Vector2(x + pw * 0.45f, y + ph * 0.4f), 150f, _cutinCol, 0.16f * glow);

        // 本体（α込み・キーカラーをほんのり乗算して“この技の色”に染める）。弾の視認を侵さないよう淡く。
        Color tint = new Color(
            Mathf.Lerp(1f, _cutinCol.R, 0.10f),
            Mathf.Lerp(1f, _cutinCol.G, 0.10f),
            Mathf.Lerp(1f, _cutinCol.B, 0.10f), a);
        ci.DrawTextureRect(_cutinTex, new Rect2(x, y, pw, ph), false, tint);

        // カットインに合わせたバトルセリフ（バストアップの右・中央高さ）。カットインの不透明に同調してフェード。
        if (!string.IsNullOrEmpty(_cutinLine))
        {
            float sa = Mathf.Clamp(a / CutinHoldA, 0f, 1f);
            float sx = x + pw * 0.82f;
            float sy = 348f;
            // 左に色アクセントの縦バー（この技の色）＋影＋本体（大きめ ZenBold）。
            ci.DrawRect(new Rect2(sx - 14f, sy - 4f, 4f, 40f), new Color(_cutinCol.R, _cutinCol.G, _cutinCol.B, 0.85f * sa));
            UiKit.Text(ci, UiKit.ZenBold, new Vector2(sx + 2f, sy + 2f), _cutinLine, UiKit.FontTitle, new Color(0f, 0f, 0f, 0.5f * sa));
            UiKit.Text(ci, UiKit.ZenBold, new Vector2(sx, sy), _cutinLine, UiKit.FontTitle, new Color(0.97f, 0.96f, 1f, sa));
        }
    }
    private bool _cutinLandedFlashed;

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
        UiKit.Box(ci, new Rect2(x, y, w, h), Fa(new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.6f)), 11f, Fa(new Color(accent, 0.5f)), 1f);
        float bw = KeyBadge(ci, new Vector2(x + 12, y + 3), TokSkill, accent, _topLeftFade);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 12 + bw + 8, y + 5), label, 13, Fa(accent));
    }

    // 現在のショットモードチップ（LIFE/BOMB の直下・常時表示）。光=シアン基調。
    private void DrawShotMode(HudCanvas ci)
    {
        string name = _game?.ShotModeName(_shotMode) ?? "連射";
        string label = "ショット  " + name;
        const float padL = 16f, h = 24f;
        float w = padL + 10 + UiKit.TextW(UiKit.ZenBold, label, 13) + 14;
        float x = 22, y = 104;
        UiKit.Box(ci, new Rect2(x, y, w, h), Fa(new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.6f)), 11f, Fa(new Color(UiKit.Info, 0.45f)), 1f);
        ci.DrawCircle(new Vector2(x + padL, y + h / 2f), 4.5f, Fa(UiKit.Info));
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + padL + 10, y + 5), label, 13, Fa(UiKit.PurifyHi));
        // 切替キーのバッジ（KB=V / パッド=B を出し分け）
        float bw = KeyBadge(ci, new Vector2(x + w + 8, y + 3), TokMode, UiKit.Info, _topLeftFade);
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + w + 8 + bw + 6, y + 6), "切替", 10, Fa(UiKit.Text3));
    }

    // モード切替トースト（画面中央上に短時間スウィープ＝Shot Upgrades の modeSweep 相当）。
    private void DrawShotModeToast(HudCanvas ci)
    {
        float a = Mathf.Clamp((float)(_shotModeToast / 0.45), 0f, 1f); // 終わり際にフェード
        string name = _game?.ShotModeName(_shotMode) ?? "連射";
        string t = "MODE ▸ " + name;
        float w = UiKit.TextW(UiKit.ZenBlack, t, UiKit.FontTitle) + 90;
        float x = 640 - w / 2f, y = 150;
        UiKit.Box(ci, new Rect2(x, y, w, 54f), new Color(0.06f, 0.10f, 0.14f, 0.9f * a), 15f, new Color(UiKit.Info, 0.6f * a), 1.4f);
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + 22, y + 9), "MODE", UiKit.FontSmall, new Color(UiKit.Info, a));
        UiKit.Text(ci, UiKit.ZenBlack, new Vector2(x, y + 13), name, UiKit.FontTitle, new Color(UiKit.PurifyHi, a), HorizontalAlignment.Center, w);
    }

    // やさしさゲージ（ショットチップの直下）。蓄積＝紫、全開＝金で残時間が減る。満タン手前でふち明滅。
    private void DrawKindness(HudCanvas ci)
    {
        float fill = Mathf.Clamp(_game?.Kindness ?? 0f, 0f, 1f);
        bool over = _game?.IsOverload ?? false;
        float x = 22, y = 132, w = 168, h = 20;
        float pulse = (!over && fill >= 0.85f) ? (0.5f + 0.5f * Mathf.Sin((float)_t * 9f)) : 0f;
        Color border = over ? new Color(UiKit.Gold, 0.9f) : new Color(UiKit.Mina, 0.12f + 0.6f * pulse);
        UiKit.Box(ci, new Rect2(x, y, w, h), Fa(new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.6f)), 10f, Fa(border), 1f);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 12, y + 4), "やさしさ", 12, Fa(over ? UiKit.Gold : UiKit.Mina));
        float barX = x + 62, barW = w - 62 - 12;
        UiKit.Box(ci, new Rect2(barX, y + h / 2f - 4, barW, 8f), Fa(new Color(1, 1, 1, 0.08f)), 4f);
        if (fill > 0)
        {
            float bh = 8f + _kindPulse * 4f;
            UiKit.Box(ci, new Rect2(barX, y + h / 2f - bh / 2f, barW * fill, bh), Fa(over ? UiKit.Gold : UiKit.Mina), 4f);
        }
        if (over) UiKit.Text(ci, UiKit.Mono, new Vector2(x + w + 8, y + 5), "全開!", 11, Fa(UiKit.Gold));
        else if (_game?.KindnessReady ?? false) // 満タン＝手動発動できる（Ctrl/R3）。点滅で誘導。
        {
            float ba = 0.55f + 0.45f * Mathf.Sin((float)_t * 7f);
            UiKit.Text(ci, UiKit.Mono, new Vector2(x + w + 8, y + 5), "満タン！" + AllKind, 11, Fa(new Color(UiKit.Gold, ba)));
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
        UiKit.Box(ci, new Rect2(x, y, w, h), Fa(new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.5f)), 11f, Fa(new Color(UiKit.Purify, 0.26f)), 1f);
        // 救った人 ◯/3（=HeartsSaved＝到達度マクロ目標）。通貨「浄化した心」と名前で分離する。
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 12, y + 8), "救った人", 11, Fa(UiKit.Info));
        for (int i = 0; i < total; i++)
        {
            Color c = i < saved ? UiKit.Purify : new Color(UiKit.Purify, 0.20f);
            UiKit.Heart(ci, new Vector2(x + 104 + i * 16, y + 13), 6f, Fa(c));
        }
        UiKit.Text(ci, UiKit.Mono, new Vector2(x + w - 34, y + 7), $"{saved}/{total}", 12, Fa(UiKit.PurifyHi));
        // 汚染ゲージ（救うほど濁る＝目標の対カウンター）
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 12, y + 29), "汚染", 10, Fa(new Color(UiKit.Kegare, 0.95f)));
        float barX = x + 44, barW = w - 44 - 14, barY = y + 31;
        UiKit.Box(ci, new Rect2(barX, barY, barW, 6f), Fa(new Color(1, 1, 1, 0.08f)), 3f);
        if (contam > 0) UiKit.Box(ci, new Rect2(barX, barY, barW * contam, 6f), Fa(UiKit.Kegare), 3f);
    }

    // やさしさ全開の瞬間トースト（DrawShotModeToast と同系。中央上に短時間）。
    private void DrawOverloadToast(HudCanvas ci)
    {
        float a = Mathf.Clamp((float)(_overloadToast / 0.4), 0f, 1f);
        const string t = "やさしさ全開";
        float w = UiKit.TextW(UiKit.ZenBlack, t, UiKit.FontTitle) + 90;
        float x = 640 - w / 2f, y = 150;
        UiKit.Box(ci, new Rect2(x, y, w, 54f), new Color(0.10f, 0.08f, 0.04f, 0.9f * a), 15f, new Color(UiKit.Gold, 0.6f * a), 1.4f);
        UiKit.Text(ci, UiKit.ZenBlack, new Vector2(x, y + 13), t, UiKit.FontTitle, new Color(UiKit.Gold, a), HorizontalAlignment.Center, w);
    }

    // 操作ガイド（常駐）：プレイ中ずっと画面「右下」に「ボタン→動作」を横一列で出す。
    //   座席：右下（右端 22px ＋下端アンカー）。下の Esc メニューヒント(上端≈652)の直上に右寄せで収め、
    //         中央の弾フィールドにも左ゲージ群にも掛けない。半透明ピル＋小キー枠で上品にまとめる。
    //   表記：Pad.Display(KB/PS/Xbox) に追従する代表トークン Tok*（移動=WASD/L 等、コンパクト優先）。
    //   出し分け：ヒカゲ技は所持時のみ点灯、非所持時は淡色で「技（未所持）」と添える。
    //             モード切替はモード解放時のみ。全体αは _controlsAlpha（会話=薄/チュートリアル=消）。
    private void DrawControls(HudCanvas ci)
    {
        float a = _controlsAlpha;
        if (a <= 0.01f) return;

        // 行データ：トークン・動作名・有効か（無効は淡色＝まだ使えない/未解放を自然に示す）。
        bool hasModes = (_game?.IsModeUnlocked(GameManager.ShotMode.Spread) ?? false)
                     || (_game?.IsModeUnlocked(GameManager.ShotMode.Homing) ?? false);
        var items = new System.Collections.Generic.List<(string tok, string label, bool on)>
        {
            // 同じ動作に複数の割り当てがあるものは All*（全部列挙）。単一割り当ては Tok* のまま。
            (AllMove,  "移動",  true),
            (AllShot,  "撃つ",  true),
            (AllFocus, "低速",  true),
            (TokDodge, "回避",  _dodgeReady), // 低速の隣（共に回避手段）。CD中は淡色＝使える時だけ点灯
            (AllBomb,  "ボム",  true),
            (AllMode,  "切替",  hasModes),  // ショットモード未解放なら淡く
        };
        // 「技」（C/Y＝ヒカゲ大波）はヒカゲが仲間の時だけ出す。本編ではヒカゲは加入しない＝
        // 常時表示すると“使えないボタン”になるため、仲間にいる時だけ列に加える（W0 等）。
        if (_skillHas) items.Add((AllSkill, "技", true));
        items.Add((AllKind, "全開", true));

        // レイアウト（設計1280x720）：右下に横一列。各アイテム＝[キー枠][動作名]、右寄せで並べる。
        const float labelSize = 12f, badgeGap = 5f, itemGap = 16f, padX = 14f, padY = 7f, badgeH = 18f;

        // 右寄せのため、先に各アイテム幅と総幅を測る（KeyBadge と同じ式でバッジ幅を算出）。
        string Label(string label, bool on) => (label == "技" && !on) ? "技(仲間時)" : label;
        float[] iw = new float[items.Count];
        float contentW = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            float badgeW = UiKit.TextW(UiKit.Mono, items[i].tok, 11) + 12f;
            float labelW = UiKit.TextW(UiKit.ZenBold, Label(items[i].label, items[i].on), (int)labelSize);
            iw[i] = badgeW + badgeGap + labelW;
            contentW += iw[i];
        }
        contentW += itemGap * (items.Count - 1);

        float h = padY * 2 + badgeH;
        float w = contentW + padX * 2;
        float x = 1280 - 22 - w;   // 右下・右端 22px に揃える
        float y = 648f - h;        // 下の Esc メニューヒント(上端≈652) の直上

        // 背景ピル（薄め＝弾より目立たない・文字は読める明度）。
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(0.05f, 0.04f, 0.09f, 0.62f * a), h * 0.5f,
            new Color(UiKit.Info, 0.34f * a), 1f);

        // 左→右に [キー枠][動作名] を並べる。無効（未解放/未所持）はαを落として淡く。
        float cx = x + padX, by = y + padY;
        for (int i = 0; i < items.Count; i++)
        {
            var (tok, label, on) = items[i];
            float ra = a * (on ? 1f : 0.4f);
            Color accent = on ? UiKit.PurifyHi : UiKit.Text3;
            float bw = KeyBadge(ci, new Vector2(cx, by), tok, accent, ra);
            UiKit.Text(ci, UiKit.ZenBold, new Vector2(cx + bw + badgeGap, by + 3), Label(label, on), (int)labelSize,
                new Color(0.92f, 0.92f, 0.97f, ra));
            cx += iw[i] + itemGap;
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

    // チュートリアルの「対応ボタン一覧」帯（指示帯の真上・下部中央）。
    // _tutorialOp（操作名）を All*（全割り当て）トークンに展開し、[操作名][キーバッジ群] を1行で示す。
    // KB 表示時は "Z / Space / Enter" のように複数キーがトークン内に並ぶ＝1キーしか出ない不親切を解消する。
    private void DrawTutorialKeys(HudCanvas ci)
    {
        // 操作名 → (見出し, 全割り当てトークン, アクセント色)。Player.cs の入力判定と一致させる。
        (string label, string tok, Color accent) info = _tutorialOp switch
        {
            "move"  => ("移動",       AllMove,  UiKit.Info),
            "shot"  => ("撃つ",       AllShot,  UiKit.Purify),   // 浄化ステップも板を“撃って”祓う＝ショット表記
            "focus" => ("低速",       AllFocus, UiKit.Info),
            "dodge" => ("回避",       AllDodge, UiKit.Gold),
            "bomb"  => ("ボム",       AllBomb,  UiKit.Mina),
            "kind"  => ("やさしさ全開", AllKind,  UiKit.PurifyHi),
            _       => ("",           "",       UiKit.White),
        };
        if (info.label.Length == 0) return;

        const int labelSize = 14, tokSize = 12;
        float pulse = 0.6f + 0.4f * Mathf.Sin((float)_t * 4f);

        float labelW = UiKit.TextW(UiKit.ZenBold, info.label, labelSize);
        float badgeW = UiKit.TextW(UiKit.Mono, info.tok, tokSize) + 16f;
        const float gap = 12f, padX = 16f, h = 30f;
        float contentW = labelW + gap + badgeW;
        float w = contentW + padX * 2f;
        float x = 640 - w / 2f, y = 462f; // 指示帯(y=500)の真上。会話ボックスより上で弾/セリフと干渉しにくい。

        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.88f), 10f,
            new Color(info.accent, 0.4f + 0.4f * pulse), 1.3f);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + padX, y + 7), info.label, labelSize, new Color(0.94f, 0.92f, 0.99f));
        // キーバッジ（KeyBadge と同寸・高さ18・縦中央寄せ）。KB なら複数キーがトークン内に並ぶ。
        KeyBadge(ci, new Vector2(x + padX + labelW + gap, y + (h - 18f) / 2f), info.tok, info.accent);
    }

    // チュートリアルのスポット暗転：全画面を暗幕で覆い、_spotRect だけ避けて帯で描く（MurkVignette の四分割テクの矩形版）。
    // 「どこが明るいか」が一目で分かるよう、穴の縁を太い黄色枠でくっきり囲み、ゆっくり明滅（パルス）させ、
    //  「ここ！」の小ラベルを添える（#2 スポットを分かりやすく）。会話ボックス矩形も暗転の対象外に抜く（#3 セリフを暗くしない）。
    // Size≈0 の矩形なら穴なし＝全画面を一様に暗転（ステップ0の導入用、ただし会話矩形だけは抜く）。
    private void DrawTutorialSpot(HudCanvas ci)
    {
        if (_spotAlpha <= 0.001f) return;
        var dark = new Color(0.03f, 0.03f, 0.06f, _spotAlpha);
        const float W = 1280f, Hh = 720f;

        // 会話表示中はそのボックス矩形を暗幕から除外する（セリフ・立ち絵がフル輝度で読める）。
        bool hasDlg = _dlgText.Length > 0;
        Rect2 dlgBox = _dlgIsDialog ? new Rect2(40, 520, 1200, 170) : new Rect2(140, 590, 1000, 96);

        // 穴なし＝全画面を一様に覆う（会話矩形だけは避ける）。
        if (_spotRect.Size.X <= 1f || _spotRect.Size.Y <= 1f)
        {
            if (hasDlg) FillExcept(ci, new Rect2(0, 0, W, Hh), dlgBox, dark);
            else        ci.DrawRect(new Rect2(0, 0, W, Hh), dark);
            return;
        }

        // 穴に少し余白を足して、ゲージ全体がはっきり見えるようにする。
        Rect2 hole = _spotRect.Grow(12f);
        float l = Mathf.Clamp(hole.Position.X, 0, W);
        float t = Mathf.Clamp(hole.Position.Y, 0, Hh);
        float r = Mathf.Clamp(hole.Position.X + hole.Size.X, 0, W);
        float b = Mathf.Clamp(hole.Position.Y + hole.Size.Y, 0, Hh);

        // 四分割の帯で穴を避けて全画面を覆う（上・下・左・右）。会話矩形は各帯からさらに抜く。
        DarkBand(ci, new Rect2(0, 0, W, t), dlgBox, hasDlg, dark);              // 上帯
        DarkBand(ci, new Rect2(0, b, W, Hh - b), dlgBox, hasDlg, dark);        // 下帯
        DarkBand(ci, new Rect2(0, t, l, b - t), dlgBox, hasDlg, dark);         // 左帯
        DarkBand(ci, new Rect2(r, t, W - r, b - t), dlgBox, hasDlg, dark);     // 右帯

        // ── 明部の強調 ──
        // ゆっくりした明滅(パルス)。0.5〜1.0 の範囲で脈打たせる。
        float pulse = 0.5f + 0.5f * (0.5f + 0.5f * Mathf.Sin((float)_t * 3.4f));
        // 太い黄色枠でくっきり囲む（2本：外側に細い白、内側に太い黄）。
        var glowY = new Color(1.0f, 0.86f, 0.18f, 0.55f + 0.40f * pulse);
        var glowW = new Color(1.0f, 1.0f, 1.0f, 0.35f + 0.30f * pulse);
        float thick = 5f;
        // 内側の太い黄枠（穴の縁ぴったり）。
        ci.DrawRect(new Rect2(l, t, r - l, thick), glowY);                 // 上
        ci.DrawRect(new Rect2(l, b - thick, r - l, thick), glowY);         // 下
        ci.DrawRect(new Rect2(l, t, thick, b - t), glowY);                 // 左
        ci.DrawRect(new Rect2(r - thick, t, thick, b - t), glowY);         // 右
        // 外側の細い白枠（パルスでにじむ）。
        float go = thick + 4f;
        ci.DrawRect(new Rect2(l - go, t - go, (r - l) + go * 2f, 2f), glowW);
        ci.DrawRect(new Rect2(l - go, b + go - 2f, (r - l) + go * 2f, 2f), glowW);
        ci.DrawRect(new Rect2(l - go, t - go, 2f, (b - t) + go * 2f), glowW);
        ci.DrawRect(new Rect2(r + go - 2f, t - go, 2f, (b - t) + go * 2f), glowW);

        // 「ここ！」の指差しラベル＋下向き三角を穴の上に添える（穴の真上に余白があれば）。
        float labA = 0.7f + 0.3f * pulse;
        string tag = "ここ！";
        float fs = 18f, padX = 10f, padY = 5f;
        float tw = UiKit.TextW(UiKit.ZenBold, tag, (int)fs);
        float boxW = tw + padX * 2f, boxH = fs + padY * 2f;
        float cx = Mathf.Clamp((l + r) * 0.5f, boxW * 0.5f + 4f, W - boxW * 0.5f - 4f);
        float labY = t - go - 8f - boxH - 8f; // 三角ぶんの隙間
        if (labY < 4f) labY = b + go + 14f;   // 上に入らなければ下に出す
        var labBg = new Color(0.12f, 0.10f, 0.04f, 0.92f);
        UiKit.Box(ci, new Rect2(cx - boxW * 0.5f, labY, boxW, boxH), labBg, 7f, new Color(1.0f, 0.86f, 0.18f, labA), 1.5f);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(cx - tw * 0.5f, labY + padY - 1f), tag, (int)fs, new Color(1.0f, 0.92f, 0.5f, labA));
        // 穴へ向かう小さな三角（上ラベルなら下向き／下ラベルなら上向き）。
        float ty = labY < t ? labY + boxH : labY - 9f;
        float dir = labY < t ? 1f : -1f;
        ci.DrawColoredPolygon(new[]
        {
            new Vector2(cx - 7f, ty),
            new Vector2(cx + 7f, ty),
            new Vector2(cx, ty + 9f * dir),
        }, new Color(1.0f, 0.86f, 0.18f, labA));
    }

    // 暗幕の1帯を描く。会話矩形 dlg と交差する分は抜く（hasDlg のときだけ）。
    private void DarkBand(HudCanvas ci, Rect2 band, Rect2 dlg, bool hasDlg, Color dark)
    {
        if (band.Size.X <= 0f || band.Size.Y <= 0f) return;
        if (hasDlg && band.Intersects(dlg)) FillExcept(ci, band, dlg, dark);
        else ci.DrawRect(band, dark);
    }

    // area から hole（会話矩形）を避けて、最大4枚の矩形で塗る。
    private void FillExcept(HudCanvas ci, Rect2 area, Rect2 hole, Color col)
    {
        float al = area.Position.X, at = area.Position.Y;
        float ar = al + area.Size.X, ab = at + area.Size.Y;
        float hl = Mathf.Max(al, hole.Position.X), ht = Mathf.Max(at, hole.Position.Y);
        float hr = Mathf.Min(ar, hole.Position.X + hole.Size.X), hb = Mathf.Min(ab, hole.Position.Y + hole.Size.Y);
        if (hr <= hl || hb <= ht) { ci.DrawRect(area, col); return; } // 交差なし
        if (ht > at) ci.DrawRect(new Rect2(al, at, ar - al, ht - at), col);   // 上
        if (hb < ab) ci.DrawRect(new Rect2(al, hb, ar - al, ab - hb), col);   // 下
        if (hl > al) ci.DrawRect(new Rect2(al, ht, hl - al, hb - ht), col);   // 左
        if (hr < ar) ci.DrawRect(new Rect2(hr, ht, ar - hr, hb - ht), col);   // 右
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
        foreach (var (h, wd) in TickerWords) block += TickerHandleW(h) + UiKit.TextW(UiKit.Zen, wd, 14) + gap;
        float scroll = ((float)_t * 70f) % block;
        float cx = startX - scroll + block; // 1ブロック先行
        // ───────── コメントの入退場演出（ログイン/ログ アウト風／SNSの接続・切断の手触り）─────────
        //   ティッカーは連続スクロールなので「右端で接続して入る／左ラベル際で切断して抜ける」を
        //   各セルの横位置から導く。入＝右端域でα0→1にポップ＋ハンドル頭に小さな接続ドットが点灯し
        //   外周リングが一拍広がる（ログイン）。出＝左ラベル際でα1→0へ薄れつつ僅かに上へスッと退く（ログアウト）。
        //   弾の視認は損なわない（下部ティッカー帯の中だけ・加算グローは極小・本数を増やさない）。
        const float bandL = 150f, bandR = 1280f;     // 可視帯（左ラベル境界〜右端）
        const float inSpan = 130f;                    // 右端からこの幅ぶんが「接続中（入場）」
        const float outSpan = 96f;                    // 左ラベル際このぶんが「切断中（退場）」
        float midY = y + barH / 2f;
        for (int rep = 0; rep < 3; rep++)
        {
            foreach (var (h, wd) in TickerWords)
            {
                float hw = TickerHandleW(h);
                float cellW = hw + UiKit.TextW(UiKit.Zen, wd, 14);
                float cellL = cx, cellR = cx + cellW;
                if (cellR > bandL && cellL < bandR)
                {
                    // 入場t：右端 inSpan に入った瞬間 0、抜け切ったら 1（ログイン進捗）
                    float tIn = Mathf.Clamp((bandR - cellL) / inSpan, 0f, 1f);
                    // 退場t：左ラベル際 outSpan に入ると 1→0（ログアウト進捗）
                    float tOut = Mathf.Clamp((cellR - bandL) / outSpan, 0f, 1f);
                    float life = Mathf.Min(tIn, tOut);              // 0=端／1=安定表示
                    float alpha = Mathf.SmoothStep(0f, 1f, life);
                    // ログイン：入場側だけ下からスッと持ち上げる小さなポップ。退場側は上へ抜ける。
                    float rise = (1f - tIn) * 5f;                   // 入＝下から
                    float exitLift = (1f - tOut) * 4f;             // 出＝上へ
                    float dy = rise - exitLift;
                    float th = y + barH / 2f - 7 - dy;
                    if (hw > 0f) UiKit.Text(ci, UiKit.Mono, new Vector2(cx, th), h, 12, new Color(UiKit.Text3, alpha));
                    UiKit.Text(ci, UiKit.Zen, new Vector2(cx + hw, th - 1), wd, 14, new Color(UiKit.Text2, alpha));
                    // 接続ドット：ハンドル頭の左に小点。入場の一拍だけ光って“ログインした”を示す。
                    float dotX = cx - 9f, dotY = midY - dy;
                    // 入場の立ち上がり（tIn が 0→~0.5）で外周リングが広がるログイン・パルス。
                    if (tIn < 0.55f)
                    {
                        float p = tIn / 0.55f;                      // 0→1
                        float ringR = 3f + p * 7f;                 // 広がる
                        float ringA = (1f - p) * 0.5f * alpha;     // 薄れる
                        ci.DrawArc(new Vector2(dotX, dotY), ringR, 0, Mathf.Tau, 18, new Color("8fe9c0", ringA), 1.3f, true);
                    }
                    // 接続インジケータ本体（緑＝オンライン）。退場側では赤寄りに転じ消灯（切断）。
                    Color dotCol = tOut < 0.5f ? new Color("ff7a90") : new Color("7fe6b0");
                    ci.DrawCircle(new Vector2(dotX, dotY), 2.2f, new Color(dotCol, alpha));
                }
                cx += cellW + gap;
            }
        }
    }

    // ティッカー1件ぶんのハンドル列幅。空欄（#11 文面改稿＝ハンドル無し投稿）は 0 を返して本文を詰める
    //（旧実装は空欄でも +6px のギャップが残った）。block 計算とセル描画の両方でこれを使い、幅を一致させる。
    private static float TickerHandleW(string h)
        => string.IsNullOrEmpty(h) ? 0f : UiKit.TextW(UiKit.Mono, h, 12) + 6f;

    private void DrawDialog(HudCanvas ci)
    {
        // 現在ページのテキストを、その表示済み文字数ぶんだけ描く（全ボックス 2行固定＝DlgMaxLines）。
        string page = CurPageText;
        int n = Mathf.Clamp(Mathf.FloorToInt(_dlgRevealed), 0, page.Length);
        string shown = page.Substring(0, n);
        // ページ継続サイン：現在ページを出し切っていて、まだ後続ページがあるとき「▼」を点滅（Zで続きへ）。
        bool morePages = !OnLastPage && _dlgRevealed >= page.Length;

        if (!_dlgIsDialog)
        {
            // ナレーション：中央寄せの淡いテロップ（バー無し）。行間を足して詰まりを解消。2行に統一。
            UiKit.Box(ci, new Rect2(140, 590, 1000, 96), new Color(0.04f, 0.03f, 0.07f, 0.7f), 12f);
            UiKit.MultiLeading(ci, UiKit.Zen, new Vector2(180, 606), shown, UiKit.FontHeading, new Color(0.9f, 0.9f, 0.95f), 920, NarrLeading, DlgMaxLines);
            if (FastForwarding) DrawSkipChip(ci, new Vector2(140 + 1000 - 20, 598));
            if (morePages && ((int)(_t * 2f) % 2) == 0)
                UiKit.Text(ci, UiKit.ZenBold, new Vector2(140 + 1000 - 32, 590 + 96 - 26), "▼", UiKit.FontLabel, new Color(1f, 1f, 1f, 0.7f));
            return;
        }

        // シネマ下部バー（少し背を高く・行間を足して読みやすく）
        float x = 40, y = 520, w = 1200, h = 170;
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
            UiKit.Text(ci, UiKit.ZenBold, new Vector2(textX, y + 16), _dlgSpeaker, UiKit.FontSpeaker, _dlgSpeakerCol);
        // 本文：行間(DlgLeading)を足して詰まりを解消。全ボックス 2行固定（DlgMaxLines）＝はみ出し防止＋箇所ごとの行数差を解消。
        UiKit.MultiLeading(ci, UiKit.Zen, new Vector2(textX, y + 48), shown, UiKit.FontHeading, new Color(0.95f, 0.95f, 0.98f),
            x + w - textX - 30, DlgLeading, DlgMaxLines);
        // 既読高速送り中の控えめな表示（バー右上・#22）。
        if (FastForwarding) DrawSkipChip(ci, new Vector2(x + w - 20, y + 12));
        // ページ継続サイン：後続ページがあるとき「▼」を点滅（Zで続きへ）。
        if (morePages && ((int)(_t * 2f) % 2) == 0)
            UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + w - 34, y + h - 30), "▼", UiKit.FontLabel, new Color(1f, 1f, 1f, 0.7f));
    }

    // 既読スキップ中インジケータ「▶▶」（右上アンカー基準・控えめ）。他シーンの独自レンダラからも呼べるよう static。
    public static void DrawSkipChip(CanvasItem ci, Vector2 rightTop)
    {
        const string t = "▶▶";
        float tw = UiKit.TextW(UiKit.ZenBold, t, 14);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(rightTop.X - tw, rightTop.Y), t, 14, new Color(UiKit.Info, 0.75f));
    }

    // R 長押しリトライの進捗チップ（下部中央・設計座標）。長押し中だけ出て、離すと消える
    // ＝「押した瞬間に何が起きるか」を見せつつキャンセルの余地を残す（誤爆防止の長押し化とセット）。
    // カットシーン（Prologue/Final/Epilogue）の独自レンダラからも呼べるよう static。
    public static void DrawRetryHoldChip(CanvasItem ci, float frac, string label)
    {
        float tw = UiKit.TextW(UiKit.ZenBold, label, 14);
        const float barW = 90f, gap = 12f, h = 34f;
        float w = 18f + tw + gap + barW + 18f;
        float x = (UiKit.DesignW - w) / 2f, y = 600f;
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.92f), 10f, new Color(UiKit.Info, 0.55f), 1.2f);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 18f, y + 8f), label, 14, UiKit.Text2);
        float bx = x + 18f + tw + gap, by = y + h / 2f - 3f;
        ci.DrawRect(new Rect2(bx, by, barW, 6f), new Color(1, 1, 1, 0.14f));
        ci.DrawRect(new Rect2(bx, by, barW * Mathf.Clamp(frac, 0f, 1f), 6f), UiKit.Info);
    }

    // 会話本文の行間（leading・px）。フォント既定より少し開けて読みやすく。
    private const float DlgLeading = 9f;
    private const float NarrLeading = 8f;

    // 無防備窓サイクルの短い字幕（弾を止めない）。下部・話者色つきの一行カード。
    private void DrawBossLine(HudCanvas ci)
    {
        float a = Mathf.Clamp((float)_bossLineTimer * 2f, 0f, 1f);
        string sp = _bossLineSpeaker.Length > 0 ? _bossLineSpeaker + "  " : "";
        float spW = UiKit.TextW(UiKit.ZenBold, sp, UiKit.FontSpeaker);
        float tw = UiKit.TextW(UiKit.ZenBold, _bossLine, UiKit.FontHeading);
        float w = spW + tw + 36, x = 640 - w / 2f, y = 540, h = 38;
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(16 / 255f, 14 / 255f, 26 / 255f, 0.74f * a), 12f,
            new Color(_bossLineCol, 0.55f * a), 1.2f);
        if (sp.Length > 0)
            UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 18, y + 10), sp, UiKit.FontSpeaker, new Color(_bossLineCol, a));
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 18 + spW, y + 9), _bossLine, UiKit.FontHeading, new Color(UiKit.White, a));
    }

    // ── FINAL タイトルカード（格上の見せ方）──────────────────────────────
    // 時間軸（t = 経過秒 / EpicDur=6.4）：
    //   0.00-0.55 暗転が上下から寄る（レターボックスが閉じる）＝間をつくる
    //   0.35-1.30 タグ("FINAL")が「裂けて」出る：上下2枚に割れた同じ文字が中央でぶつかって合わさる
    //   1.10-2.40 副題が一文字ずつ滲み出る（字ごとに遅延＋にじみ＝ブラー代わりの多重描画）
    //   2.40-4.60 ため：静止。色収差と走査線だけが微かに息をする
    //   4.60-6.40 罫線が閉じ、レターボックスが開き、文字は上へ抜けながら消える
    private void DrawEpicBanner(HudCanvas ci)
    {
        float t = (float)(EpicDur - _bannerTimer);          // 経過秒
        float cx = 640f;

        // ① レターボックス（引き算：飾らずに“画面の格”を上げる）。開閉は ease で。
        float close = Mathf.Clamp(t / 0.55f, 0f, 1f);
        float open = Mathf.Clamp((t - 3.4f) / 1.6f, 0f, 1f);
        float lb = Mathf.Max(0f, Ease(close) - Ease(open));
        // 上帯は HUD 行(LIFE/BOMB/浄化)を完全には飲まない高さに留める＝カード中も自機の状態は読める。
        float barH = 104f * lb;
        if (barH > 0.5f)
        {
            ci.DrawRect(new Rect2(0, 0, 1280, barH), new Color(0.02f, 0.015f, 0.04f, 0.96f));
            ci.DrawRect(new Rect2(0, 720 - barH, 1280, barH), new Color(0.02f, 0.015f, 0.04f, 0.96f));
        }
        // 中央帯もわずかに沈める（弾の視認は残す＝α0.42まで）。
        // 中央帯の沈み：弾の視認を守るため上限0.34、かつ帯の開きより一足早く抜く（戦闘が始まる前に消える）。
        float mid = lb * (1f - Ease(Mathf.Clamp((t - 3.0f) / 1.0f, 0f, 1f)));
        if (mid > 0.01f) ci.DrawRect(new Rect2(0, barH, 1280, 720 - barH * 2f), new Color(0.02f, 0.015f, 0.04f, 0.34f * mid));
        if (lb <= 0.01f) return;

        // ② 横罫（額装）。中央から左右に伸び、最後に閉じる。原色を避けアクセントを淡く。
        float rule = Mathf.Clamp((t - 0.3f) / 0.9f, 0f, 1f) * (1f - Ease(open));
        float rw = 470f * Ease(rule);
        var ruleCol = new Color(_epicAccent, 0.55f * rule);
        ci.DrawRect(new Rect2(cx - rw, 268f, rw * 2f, 1.4f), ruleCol);
        ci.DrawRect(new Rect2(cx - rw * 0.72f, 424f, rw * 1.44f, 1.4f), ruleCol);

        float rise = -26f * Ease(open); // 最後に上へ抜ける
        float fade = 1f - Ease(open);

        // ③ タグ（"FINAL"）＝裂けて合わさる。字間を大きく開けて“銘板”にする（詰まって見える対策）。
        float split = 1f - Mathf.Clamp((t - 0.35f) / 0.95f, 0f, 1f);
        float sp = Ease(split);
        float tagA = Mathf.Clamp((t - 0.35f) / 0.5f, 0f, 1f) * fade;
        float tagY = 288f + rise;
        if (sp > 0.01f)
        {
            // 割れた2枚（上下）が中央へ寄って合わさる。合わさるほど濃く。
            var ghost = new Color(_epicAccent, tagA * 0.55f * sp);
            DrawTracked(ci, UiKit.ZenBlack, _epicTag, UiKit.FontTitle, cx, tagY - 26f * sp, 16f, ghost);
            DrawTracked(ci, UiKit.ZenBlack, _epicTag, UiKit.FontTitle, cx, tagY + 26f * sp, 16f, ghost);
        }
        DrawTracked(ci, UiKit.ZenBlack, _epicTag, UiKit.FontTitle, cx, tagY, 16f,
            new Color(_epicAccent, tagA * (1f - 0.55f * sp)));

        // ④ 副題＝一文字ずつ滲み出る。字ごとに 0.055s ずつ遅延、出かけは大きく淡いコピーを重ねて“滲み”に。
        //    色は白寄り（原色の金ベタをやめる）＋アクセントの色収差で厚みを出す。
        float baseY = 330f + rise;
        int size = UiKit.FontDisplay;
        float track = 7f;
        float total = TrackedW(UiKit.ZenBlack, _epicSub, size, track);
        float x = cx - total / 2f;
        var f = UiKit.ZenBlack;
        for (int i = 0; i < _epicSub.Length; i++)
        {
            string ch = _epicSub[i].ToString();
            float cw = UiKit.TextW(f, ch, size);
            float lt = Mathf.Clamp((t - 1.10f - i * 0.055f) / 0.55f, 0f, 1f);
            if (lt > 0f)
            {
                float e = Ease(lt);
                float a = e * fade;
                // 滲み：出かけほど大きく淡いコピー（3枚）を後ろに敷く
                float bl = (1f - e) * 9f;
                if (bl > 0.2f)
                    for (int k = 0; k < 3; k++)
                    {
                        float ang = Mathf.Tau * k / 3f + t;
                        UiKit.Text(ci, f, new Vector2(x + Mathf.Cos(ang) * bl, baseY + Mathf.Sin(ang) * bl), ch, size,
                            new Color(_epicAccent, 0.16f * a));
                    }
                // 色収差：アクセントを左、シアンを右に 1.5px ずらす（微かに息をする）
                float ab = 1.5f + 0.5f * Mathf.Sin(t * 2.1f + i);
                UiKit.Text(ci, f, new Vector2(x - ab, baseY), ch, size, new Color(_epicAccent, 0.42f * a));
                UiKit.Text(ci, f, new Vector2(x + ab, baseY), ch, size, new Color(UiKit.Purify, 0.34f * a));
                UiKit.Text(ci, f, new Vector2(x, baseY + 2f), ch, size, new Color(0f, 0f, 0f, 0.55f * a));
                UiKit.Text(ci, f, new Vector2(x, baseY), ch, size, new Color(0.98f, 0.96f, 1f, a));
            }
            x += cw + track;
        }

        // ⑤ 走査線（安いグローの代わりに“質感”で持たせる）。タイトル帯のみに薄く。
        float scanA = 0.10f * fade * Mathf.Clamp(t / 0.8f, 0f, 1f);
        for (float y = 268f; y < 424f; y += 3f)
            ci.DrawRect(new Rect2(cx - rw, y, rw * 2f, 1f), new Color(0f, 0f, 0f, scanA));
    }

    private static float Ease(float x) => 1f - Mathf.Pow(1f - Mathf.Clamp(x, 0f, 1f), 3f); // out-cubic

    private static float TrackedW(Font f, string s, int size, float track)
    {
        float w = 0f;
        for (int i = 0; i < s.Length; i++) w += UiKit.TextW(f, s[i].ToString(), size) + track;
        return w - (s.Length > 0 ? track : 0f);
    }

    // 字間つき中央寄せ描画（銘板用。字間を開けて“詰まって見える”のを断つ）。
    private static void DrawTracked(HudCanvas ci, Font f, string s, int size, float cx, float y, float track, Color col)
    {
        if (col.A <= 0.01f) return;
        float x = cx - TrackedW(f, s, size, track) / 2f;
        for (int i = 0; i < s.Length; i++)
        {
            string ch = s[i].ToString();
            UiKit.Text(ci, f, new Vector2(x, y), ch, size, col);
            x += UiKit.TextW(f, ch, size) + track;
        }
    }

    private void DrawBanner(HudCanvas ci)
    {
        if (_epic) { DrawEpicBanner(ci); return; }
        float a = Mathf.Clamp((float)_bannerTimer, 0f, 1f);
        float w = UiKit.TextW(UiKit.ZenBlack, _bannerText, UiKit.FontDisplay);
        UiKit.Text(ci, UiKit.ZenBlack, new Vector2(640 - w / 2f, 300), _bannerText, UiKit.FontDisplay, new Color(UiKit.Light, a),
            HorizontalAlignment.Left, -1);
        // クリアリザルトのタイム行（見出しの下）。
        if (_bannerTime.Length > 0)
        {
            UiKit.Text(ci, UiKit.Mono, new Vector2(0, 366), _bannerTime, UiKit.FontTitle, new Color(UiKit.PurifyHi, a),
                HorizontalAlignment.Center, 1280);
            if (_bannerBest.Length > 0)
            {
                Color bc = _bannerNewBest ? UiKit.Gold : UiKit.Text2;
                UiKit.Text(ci, UiKit.ZenBold, new Vector2(0, 402), _bannerBest, UiKit.FontHeading, new Color(bc, a),
                    HorizontalAlignment.Center, 1280);
            }
        }
    }

    // ゲームオーバー時の選択肢プロンプト（バナー直下）。バナーのフェードに依らず常時表示。
    private void DrawGameOverPrompt(HudCanvas ci)
    {
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(0, 372), _gameOverPrompt, UiKit.FontHeading, new Color(UiKit.Text2, 1f),
            HorizontalAlignment.Center, 1280);
    }
}

// HUD 描画用ノード（Hud にぶら下げ、Hud.DrawAll を呼ぶだけ）。
public partial class HudCanvas : Node2D
{
    public Hud Hud = null!;
    public override void _Draw() => Hud?.DrawAll(this);
}
