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
    private HudAddCanvas _addCanvas = null!;   // 加算ブレンド層（被弾のライフ砕け散り・2026-09-16）
    // 会話の吹き出し（会話バー・ナレ箱・ボスの一行字幕・スペル宣告カード）を弾より奥に描く世界側の層（2026-09-26）。
    //   作者指摘「吹き出しで自機と弾が隠れる」への対処。BubbleLayer が _Ready で自分を登録し、居る間はその3つを
    //   DrawAll（最前面）では描かず DrawBubbles（世界の ZIndex -7）で描く。状態は従来どおりここが持つ＝二重に持たない。
    //   居ない場面（保険）は従来どおり最前面。→ src/BubbleLayer.cs
    public BubbleLayer? Bubbles { get; set; }

    // 吹き出し表示中は敵を止める（他クラスから参照）
    public static bool BubblePaused = false;
    public bool CinematicMode { get; private set; }
    private static UiKit.TextStyle FilmBody => new(UiKit.Zen, 24, 0, 1.55f);
    private const float FilmTextWidth = 1056f;
    private bool _cinematicBubble;
    private bool _hasCinematicAccent;
    private Color _cinematicAccent;
    private float FilmTextX => _cinematicBubble ? 216f : 112f;
    private float FilmWrapWidth => _cinematicBubble ? 928f : FilmTextWidth;

    public void SetCinematicMode(bool active, bool dialogueBubble = false, Color? accent = null)
    {
        CinematicMode = active;
        _cinematicBubble = active && dialogueBubble;
        _hasCinematicAccent = active && !_cinematicBubble && accent.HasValue;
        _cinematicAccent = accent ?? UiKit.Info;
        UpdateDialoguePause();
    }
    public bool HoldBubble = false;

    private int _lives = 3;
    private bool _livesInited;   // 初回 SetLives（ステージ開始の初期値渡し）では砕け散り演出を出さない
    private readonly Dictionary<Job, Texture2D> _lifeMarks = new();
    private readonly Dictionary<Job, Texture2D> _accountFaces = new();
    private Texture2D _bombMark = null!;

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

    // ── ボスカードのXアイコン（2026-09-17）──
    //   ボスバーのアバターは長らく「穢れ色の無地の円」で、Xのプロフィールカードを模した意匠なのに
    //   肝心のアイコン画像が入っていなかった。ハブの投稿カードと同じ素材（char/v3/{id}_face.png、
    //   ミナだけ char/mina_face.png）を同じ UiKit.FaceAvatar で出し、「いま戦っている相手＝TLで見た
    //   あのアカウント」を一目で結ぶ。
    //   誰かの判定は ShowBossBar に渡る handle（全呼び出し元が BossHandles の定数）から引く。
    //   名前は本ボスだとフェーズ名（"あふれるわたし" 等）で本人名を含まないため、handle が唯一確実な鍵。
    private string _bossFaceId = "";                      // "akari"/"koharu"/"rei"/"mina"。空＝顔なし（ヒカゲ等）
    private readonly Dictionary<string, Texture2D?> _bossFaces = new();   // id → 顔（初回だけロードして保持）
    // 改心（HideBossBar）で即消しにせず、穢れが晴れる一拍だけカードを残す。
    //   0 → 穢れたまま／1 → 浄化しきり。PurifyFadeDur かけて 0→1 に上げ、上がりきってからカードを畳む。
    private double _bossPurify;            // 0..1
    private double _bossCardFade = 1f;     // カード全体の不透明（浄化後の見送りで 1→0）
    private const double BossPurifyDur = 0.75;   // 穢れが剥がれるまで
    private const double BossCardFadeDur = 0.45; // そのあとカードが消えるまで

    // バナー
    private string _bannerText = "";
    private double _bannerTimer;
    private int _startStage;
    private string _startName = "";
    private Color _startAccent;
    private const double StageStartDur = 2.2;
    private bool _bannerRewardLife, _bannerRewardBomb;
    private const double RewardBannerDur = 2.3;
    // クリアリザルトのタイム行（バナー直下）。空なら描かない。
    private string _bannerTime = "";     // 例 "TIME 1:23.45"
    private string _bannerBest = "";     // 例 "NEW BEST!" or "BEST 1:20.00"
    private bool _bannerNewBest;
    // クリアリザルトのスコア行（タイム行と同じ様式）。空なら描かない。
    private string _bannerScore = "";     // 例 "SCORE 12,345"
    private string _bannerScoreBest = ""; // 例 "NEW BEST!" or "BEST 12,000"
    private bool _bannerScoreNewBest;
    // ゲームオーバーの選択UI（GameManager の ChoiceOverlay）に添える2つの文字列。
    //   Title  … 選択肢の上（y=150）に出す見出し「くじけちゃった…」。旧 ShowBanner（y=300）は
    //            3択の2行目と重なるので使わない（Player.GameOver のコメント参照）。
    //   Prompt … 選択肢の下（y=496）に出す、R/Shift+R/Q の控えめな添え書き。
    // どちらも *Root.cs / GameManager が残機0のあいだ毎フレーム立て、復帰時に空文字でクリアする。
    private string _gameOverTitle = "";
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

    // ── 割り込み演出中の戦闘テロップ抑制（ボス字幕 DrawBossLine ＋ スペルカットイン DrawSpellCutin）──
    //   レイ面のボス戦中割り込み（S3-7 の下書き選択。StageRei.SetQuietVeil の ON/OFF に同期）中、
    //   カットインのバトルセリフ（y≈348）が選択肢と、字幕（y=540）が吹き出しと重なって双方読めなくなるため、
    //   区間中は 0.2s でフェードアウトして消す（「弾が止まり静けさが残る」演出意図とも一致）。
    //   消え切った時点で実体も消去＝区間明けに残り時間ぶんが再表示されない（次の台詞・次のスペルからは通常）。
    public bool SuppressCallouts;
    private float _calloutA = 1f;
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
    // 集中モード（一本道 #10「集中モード」）のチップ状態。持っているあいだだけ出す。
    //   _focusHas   … 所持しているか（未所持なら行ごと出さない＝画面を空けておく）
    //   _focusReady … いま撃てるか（CDが明けている）
    //   _focusRatio … 発動中は残り持続 1→0、それ以外は CD の充填 0→1（下のバーに出す）
    private bool _focusHas, _focusReady, _focusOn;
    private float _focusRatio;

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
    private bool _dlgDraftMark;         // true=立ち絵の代わりに下書きの吹き出し印を出す（LineKind.Boy＝あなた。顔が無い）
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
    // ───────── 戦闘中の回想のテンポ（2026-09-26 作者指摘「まだ戦っている最中なのに長すぎる」）─────────
    //   ボス戦の途中（HP 閾値）に戦闘を止めて挟む回想＝StoryFilm の memory／CharacterStoryTalk の間だけ立てる。
    //   立っている間は
    //   ・タイプライターの文字送りを MemoryRevealScale 倍にする（既定 48cps → 72cps）。
    //   ・FastForwarding が既読条件（_dlgReadBefore）を要らなくなる＝初見でも Ctrl／RB 押しっぱなしで早送り。
    //     本作の既読スキップは「未読行では効かない＝取りこぼさせない」が原則だが、戦闘の最中に挟む回想は
    //     プレイヤーが**戻りたい戦闘**を待たせている。押し続けている人にだけ道を開ける（行は全部バックログに
    //     残る）。撃破後のアフター（StoryFilm の aftermath）や道中の会話には及ばない。
    //   立てる／降ろすのは回想の駆動側（StoryFilm._Ready／Restore、CharacterStoryTalk.Start／Finish）。
    public bool BattleMemoryTempo;
    private const float MemoryRevealScale = 1.5f;
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

    // ※「左上HUDの自動退避」(_topLeftFade / TopLeftZone / UpdateTopLeftFade) は 2026-09-07 に撤去した。
    //   常設HUDをサイドパネル（設計 x 0..373）へ移して弾と原理的に重ならなくなり、透かす動機が消えた
    //   （docs/20260906/HUD整理_案.md §4「恒久的にプレイ領域に居座る UI はボスバー1つだけになる」）。

    // ※「プレイ中の常駐操作ガイド」(_controlsAlpha / DrawControls) は 2026-09-07 に撤去した。
    //   盤面の右下に「移動／撃つ／低速／回避／ボム／切替」の帯が常時出て弾と重なっており、
    //   ユーザー実機指摘は「操作の UI を非表示にしてじゃま」＝薄めるのではなく最初から出さない。
    //   操作は Esc メニュー →「あそびかた」(HowToPlay) で参照できる。練習面（StageZero）の
    //   指示帯（DrawTutorialHint / DrawTutorialKeys）は教える画面なので別物として残す。

    // チュートリアルの常駐指示（操作させる区間に下部へ出す小帯）。会話と違い敵/自機は止めない
    //（ShowMessage は BubblePaused を立ててしまうため、止めない専用の表示を用意する）。
    // 値が空でない間だけ描画する（練習面 StageZero 専用＝本番の盤面には操作の案内を出さない）。
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
    private static string TokBomb  => Pad.UsingPad ? Pad.Face(JoyButton.X)            : "X";
    private static string TokCharge => Pad.UsingPad ? Pad.Face(JoyButton.Y)           : "Z"; // 溜め打ち（長押し。2026-09-26 C→Z）
    // ロックオン：Shift 長押し / パッド RB / マウス左クリック（Player.TickLockOn の判定と一致させる）。
    private static string TokLock   => Pad.UsingPad ? Pad.Face(JoyButton.RightShoulder)
                                     : Pad.UsingMouse ? "左クリック" : "Shift";
    // 集中モード：V / パッド LB / マウスのホイール回転・サイドボタン（Player.cs の判定と一致させる）。
    // 単体チップは代表1表記なので、直近デバイスに合わせて1つだけ出す（マウス時は KB 表記へ落ちないよう明示）。
    private static string TokFocus  => Pad.UsingPad ? Pad.Face(JoyButton.LeftShoulder)
                                     : Pad.UsingMouse ? "ホイール" : "V";

    // 操作子トークン（全割り当て版）：選択中の表示モードに属する割り当てを“全部”並べる。
    // 練習面（StageZero）の指示帯（DrawTutorialKeys）が使う。視認性のため区切りは細い「/」。
    // ※ 2026-09-07 に本番プレイ中の常駐操作ガイド（DrawControls）を廃止したので、ここは
    //   「教える画面」専用になった。本番の盤面には操作の案内を一切出さない（Esc メニューと
    //   「あそびかた」で見られる＝弾に案内を重ねない。ユーザー実機指摘「操作の UI がじゃま」）。
    private static string AllShot  => "オート";                                        // 射撃ボタン廃止＝常時オート射撃
    private static string AllMove  => Pad.UsingPad ? "L"                              : "矢印 / WASD";
    // ※低速移動（旧 AllFocus＝Shift / LB）は 2026-09-13 ユーザー決定で機能ごと廃止した。
    private static string AllBomb  => Pad.UsingPad ? Pad.Face(JoyButton.X)            : "X";
    // 回避ダッシュは Player.cs では Alt / Pad L3(LeftStick) の2系統。Tok* と違い“全部”を見せる版。
    private static string AllDodge => Pad.UsingPad ? Pad.Face(JoyButton.LeftStick)    : "Alt";

    // ティッカー（降ってくる言葉）＝「Xの川」のノイズ。
    // 「下に流れているコメント」と「投稿弾」が同じ“声”を出すため、どちらも PostPool から引く。
    // 正典は wiki/08_仮台本/09_投稿文集_X風.md（ユーザー承認済み・2026-09-05）の「言葉弾の文言リスト」で、
    // 面のテーマ（あかり／こはる／レイ／FINAL）ごとに層1（日常）：層2（病みサイン）：層3（本人）を
    // 09 の比率で混ぜる。旧・共通8語のハードコードは廃止した。
    // 帯は毎フレーム全語の幅を測るので、面ごとに一度だけ組んだ固定の並びをキャッシュして使う
    //（毎フレーム抽選しない＝帯が踊らない）。ハンドル空欄は帯側で幅を詰める（TickerHandleW）。
    private double _t;
    private (string h, string w)[]? _ticker;
    private (string h, string w)[] TickerWords => _ticker ??= PostPool.Words(PostPool.CurrentTheme(this));

    public override void _Ready()
    {
        AddToGroup("hud");
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        foreach (var job in Jobs.All)
        {
            _lifeMarks[job.Id] = GD.Load<Texture2D>($"res://char/player/{job.CharacterId}/{job.CharacterId}_core_v1.png");
            _accountFaces[job.Id] = GD.Load<Texture2D>(CompanionDialogue.AccountPortrait(job.Id));
        }
        _bombMark = GD.Load<Texture2D>("res://char/ui/bomb_v2.png");
        PostPool.ResetHistory();   // 面の入り口で語の直近履歴を空ける（前の面の履歴で最初の数枚が偏らない）
        _canvas = new HudCanvas { Name = "HudCanvas", Hud = this };
        AddChild(_canvas);
        // 加算ブレンド専用の子キャンバス（被弾のライフ砕け散り・2026-09-16）。DrawSetBlendMode は
        // _Draw に無いので、WorldGrade と同じく CanvasItemMaterial{Add} を子ノードに載せて分離する。
        _addCanvas = new HudAddCanvas
        {
            Name = "HudAddCanvas", Hud = this,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        AddChild(_addCanvas);
    }

    public override void _Process(double delta)
    {
        _t += delta;

        if (_flashAlpha > 0f) _flashAlpha = Mathf.Max(0f, _flashAlpha - (float)delta * 2.2f);
        if (_hurtEdge > 0) _hurtEdge -= delta;
        if (_bossBarFlash > 0) _bossBarFlash -= delta; // バー1本割れの白フラッシュ減衰（#26）
        if (_lifeShatterT > 0) _lifeShatterT -= delta; // ライフ砕け散りの残り時間（2026-09-16）

        // 会話ボックスのボタン列（AUTO/SKIP/LOG/MENU）。SKIP ラッチの自動解除も含むので FastForwarding を読む前に回す。
        TickDialogToolbar(delta);

        // 既読高速送り中は現在ページを即時全表示し、後続ページも自動で進める（行送り自体は Step_Lines が FastForwarding を見て進める）。
        if (FastForwarding)
        {
            _dlgRevealed = CurPageText.Length;
            if (!OnLastPage) AdvanceDialogPage();   // 既読は全ページを一気に抜けて最終ページ完了状態へ
        }

        // タイプライター送り（現在ページ内の文字数を進める）
        if (_messageTimer > 0 && _dlgText.Length > 0 && _dlgRevealed < CurPageText.Length)
        {
            _dlgRevealed = Mathf.Min(CurPageText.Length, _dlgRevealed + (float)delta * (_game?.MsgCharsPerSec ?? CharsPerSec)
                                                                       * (BattleMemoryTempo ? MemoryRevealScale : 1f));
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

        UpdateDialoguePause();

        if (_bannerTimer > 0 && (_startStage == 0 || !BubblePaused)) { _bannerTimer -= delta; }
        if (_bossLineTimer > 0) { _bossLineTimer -= delta; if (_bossLineTimer <= 0) _bossLine = ""; }
        // スペル宣言は会話バブル表示中は時間を止める＝発動の宣言を“戦闘が始まる瞬間”に確実に見せる。
        // （ボス _Ready の宣言が開幕イントロのバブルに食われて見落とされていた問題への対処。
        //   発動＝宣言の同期はそのままに、見せ場だけ非バブル中に揃える。）
        if (_spellTimer > 0 && !BubblePaused) { _spellTimer -= delta; }
        if (_spellGlow > 0 && !BubblePaused) { _spellGlow = Mathf.Max(0.0, _spellGlow - delta / 0.45); } // 約0.45秒で収束
        // カットインも会話バブル中は時間を止める（カードと同じく“戦闘の瞬間”に確実に見せる）。
        if (_cutinTimer > 0 && !BubblePaused) { _cutinTimer -= delta; if (_cutinTimer <= 0) _cutinTex = null; }
        if (_shotModeToast > 0) { _shotModeToast -= delta; }
        // 改心の見送り：穢れが剥がれる（_bossPurify 0→1）→ カードが引く（_bossCardFade 1→0）→ 非表示。
        if (_bossVisible && _bossPurify > 0)
        {
            if (_bossPurify < 1) _bossPurify = System.Math.Min(1.0, _bossPurify + delta / BossPurifyDur);
            else
            {
                _bossCardFade -= delta / BossCardFadeDur;
                if (_bossCardFade <= 0) { _bossCardFade = 1f; _bossPurify = 0; _bossVisible = false; }
            }
        }

        // 割り込み演出中の戦闘テロップ抑制（フィールド宣言部のコメント参照）。0.2s フェード→消去。
        _calloutA = Mathf.MoveToward(_calloutA, SuppressCallouts ? 0f : 1f, (float)delta * 5f);
        if (SuppressCallouts && _calloutA <= 0.001f)
        {
            if (_bossLineTimer > 0) { _bossLineTimer = 0; _bossLine = ""; }
            if (_cutinTimer > 0) { _cutinTimer = 0; _cutinTex = null; _cutinLine = ""; }
            // 宣告カードも一緒に消す。BubblePaused 中は _spellTimer が止まる仕様なので、
            // 割り込み（S3-7）に入る直前のカードが区間いっぱい貼りついたままになるため。
            if (_spellTimer > 0) { _spellTimer = 0; _spellGlow = 0; }
        }

        _canvas.QueueRedraw();
        _addCanvas.QueueRedraw();
    }

    private void UpdateDialoguePause()
    {
        bool paused = CinematicMode || (_messageTimer > 0 && _dlgText.Length > 0);
        if (paused && !BubblePaused)
        {
            GetNode<BulletPool>("/root/Pool").DespawnAll();
            Audio.Instance?.PlayCalm();
        }
        BubblePaused = paused;
        if (IsInstanceValid(FxLayer.Instance?.ScoreDrops)) FxLayer.Instance.ScoreDrops.UpdateDialogueVisibility();
    }

    public override void _ExitTree()
    {
        BubblePaused = false;
        SkipLatched = false;   // SKIP ラッチはシーンを跨がない（次のシーンの既読行を勝手に飛ばさない）
        // 面を抜ける（リトライ・ハブ帰還・タイトル）ときは【激情】も必ず畳む。改心を見ずに抜けた場合に
        //   FuryActive が立ったまま残ると、次の画面まで縦メーターが付いてくる。
        GameManager.Instance?.EndFury();
    }

    private void ClearDialog()
    {
        _dlgText = ""; _dlgSpeaker = ""; _dlgPortrait = null; _dlgDraftMark = false; _dlgRevealed = 0;
        _dlgPortraitPrev = null; _portraitFadeT = 0; _nodT = 0; _revealWasDone = false;
        _dlgPages.Clear(); _dlgPage = 0;
        UpdateDialoguePause();
    }

    // ───────── テキストボックスの行の種類 ─────────
    public enum LineKind { Boy = 0, Mina = 1, Other = 2, Narration = 3, Post = 4, Relay = 5, Companion = 6 }

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
        LineKind.Companion => CompanionDialogue.Accent(GameManager.Instance?.SelectedJob ?? Job.Tank),
        LineKind.Relay => UiKit.Info,
        LineKind.Post  => UiKit.Text3,
        _              => UiKit.Text2, // Narration（ナレ＝ミナの語り）は淡色
    };

    // ミナの話者ラベル。名前が決まる前（Prologue の P3 命名まで）は「？」で伏せる
    //   ＝命名の3択（ミナ／超絶最強無敵ハイパーAIちゃんMk-Ⅱ／送らない）の意味を先に潰さない。
    //   立ち絵・話者色（UiKit.Mina）は変えない＝「誰か」は見えていて、名前だけが無い状態。
    //   ゲートは GameManager.MinaNamed（セーブしない static・既定 true）。プロローグ以外は常に「ミナ」。
    public static string MinaLabel => GameManager.MinaNamed ? "ミナ" : "？";

    // 会話ログに出す話者ラベル。speaker が空（素の ShowDialog 経路）でも種別から補う。
    private static string BacklogSpeaker(LineKind k, string speaker)
    {
        if (!string.IsNullOrEmpty(speaker)) return speaker;
        return k switch
        {
            LineKind.Boy   => "あなた",
            LineKind.Mina  => MinaLabel,
            LineKind.Relay => "あなた（ミナの声）",
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
        UpdateDialoguePause();
    }

    // 立ち絵付きの素の会話（少年/ヒカゲ等、LineKind を取らない旧経路）。
    // 送り音は従来どおり無音（Narration）に保つが、会話ログには発話として残るよう logKind=Boy で積む。
    public void ShowDialog(string text) => ShowDialog(text, "res://char/algo_cutout.png");

    public void ShowDialog(string text, string portraitResPath)
    {
        SetDialog(text, "", default, dialog: true, portrait: portraitResPath, kind: LineKind.Narration, logKind: LineKind.Boy);
        _messageTimer = 6.0;
        UpdateDialoguePause();
    }

    public void ShowDialog(LineKind kind, string text, string portrait = "", string otherName = "")
    {
        string speaker; Color color; bool dialog = true; string portraitToUse = portrait;
        switch (kind)
        {
            // Boy＝プレイヤー本人（案C に少年は居ない）。顔が無いので立ち絵は出さず、
            // 空いた枠には下書きの吹き出し風の小さな印だけを置く（DrawDialog の _dlgDraftMark）。
            case LineKind.Boy:   speaker = "あなた"; color = UiKit.Info; portraitToUse = ""; break;
            case LineKind.Mina:  speaker = MinaLabel; color = UiKit.Mina; break;
            case LineKind.Other: speaker = otherName; color = UiKit.Kegare; break;
            case LineKind.Companion:
                var job = _game?.SelectedJob ?? Job.Tank;
                // 話者名は素の名前（2026-09-15）。他ジョブ潜行の専用ストーリーでは潜行キャラ本人が
                //   語り手なので「（同行）」の添え書きを外す。ボス側（Other）とは色と立ち絵で区別が付く。
                speaker = Jobs.Get(job).CharacterName;
                color = CompanionDialogue.Accent(job);
                // face 指定行（CharacterStory の表情差分＝akari_face/_cry 等）は渡された画像をそのまま出す。
                // 空欄だけ同行キャラの会話用ポートレートで補う。
                if (string.IsNullOrEmpty(portraitToUse)) portraitToUse = CompanionDialogue.Portrait(job);
                break;
            case LineKind.Relay: speaker = "あなた（ミナの声）"; color = UiKit.Info; break;
            case LineKind.Post:  speaker = "Ｘ 投稿"; color = UiKit.Text3; portraitToUse = ""; break;
            default:             speaker = ""; color = default; portraitToUse = ""; dialog = false; break;
        }
        if (CinematicMode && !_cinematicBubble) color = _hasCinematicAccent ? _cinematicAccent : UiKit.Text2;
        SetDialog(text, speaker, color, dialog, portraitToUse, kind, draftMark: kind == LineKind.Boy);
        _messageTimer = 6.0;
        UpdateDialoguePause();
    }

    // kind   … 送り音の音色（＝表示中の話者。Narration は無音）。
    // logKind … 会話ログに残すときの種別（既定で kind と同じ。送り音は無音にしたいが
    //            ログ上は発話として残したい旧経路（少年/ヒカゲの ShowDialog(string)）で使い分ける）。
    private void SetDialog(string text, string speaker, Color speakerCol, bool dialog, string portrait,
        LineKind kind = LineKind.Narration, LineKind? logKind = null, bool draftMark = false)
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
        bool sameSpeaker = _dlgSpeaker == speaker && _dlgKind == kind;
        _dlgText = text; _dlgSpeaker = speaker; _dlgSpeakerCol = speakerCol;
        _dlgIsDialog = dialog; _dlgRevealed = 0;
        // 新しい行＝送り音の差分検出をリセット。送り音の音色は kind（Narration＝無音）。
        _typePrevRevealed = 0; _dlgKind = kind;
        Texture2D? next = string.IsNullOrEmpty(portrait) ? null : ResourceLoader.Load<Texture2D>(portrait);
        _dlgDraftMark = draftMark && next == null;
        // ページ分割：本文が入る幅を確定し、2行ずつのページへ割る（送り機構は DialogRevealed/RevealDialogNow で駆動）。
        //   幅は DrawDialog のレイアウトと一致させる（ナレ＝中央920 ／ セリフ＝バー幅から話者列・立ち絵を引いた実効幅）。
        BuildDialogPages(dialog, next, _dlgDraftMark);
        // 表情クロスフェード：face テクスチャが実際に変わる瞬間だけ、旧絵を短時間重ねて移ろわせる。
        // 同一立ち絵の続き（同じ話者の連続行）はクロスフェードせず、無からの登場/退場もハード切替で十分。
        if (sameSpeaker && next != null && _dlgPortrait != null && next != _dlgPortrait)
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
    private void BuildDialogPages(bool dialog, Texture2D? portrait, bool draftMark = false)
    {
        _dlgPages.Clear();
        _dlgPage = 0;
        if (CinematicMode)
        {
            _dlgPages.AddRange(UiKit.Paginate(FilmBody, _dlgText, FilmWrapWidth, DlgMaxLines));
            return;
        }
        // DrawDialog と同じジオメトリで本文の折り返し幅を求める。
        float wrapW;
        if (!dialog)
        {
            wrapW = NarrWrapW;                              // ナレ（中央テロップ）
        }
        else
        {
            const float x = DlgBoxX, h = 170f;   // DrawDialog と同じバー座標（DlgBoxX/W が唯一の定義元）
            float textX = x + 36f;
            if (portrait != null)
            {
                float ph = h - 8f;
                float pw = ph * portrait.GetWidth() / Mathf.Max(1, portrait.GetHeight());
                textX = x + 10f + pw + 20f;
            }
            else if (draftMark) textX = x + 10f + DraftMarkW + 20f;
            wrapW = DlgWrapW(textX);                        // DrawDialog の本文幅と同じ式（DlgBoxX/W 由来）
        }
        _dlgPages.AddRange(UiKit.Paginate(UiKit.BattleBody, _dlgText, wrapW, DlgMaxLines));   // DrawDialog と同じ書体・サイズで割る
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
    //   例外は戦闘中の回想（BattleMemoryTempo）だけ＝初見でも押しっぱなしで早送りできる（理由はそこのコメント）。
    //   Ctrl は既読スキップ専用（やさしさ全開は撤去済み＝衝突する相手がいない）。
    //   DemoPilot/QaPilot は Z/X と移動軸しか送出しない＝自動プレイの会話送りとは干渉しない。
    private bool _dlgReadBefore;   // 現在行が「表示された時点で」既読だったか（SetDialog で確定）
    //   SkipLatched … 会話ボックス上の SKIP ボタン（S／RB 押し離し／クリック）のラッチ。ON の間は押しっぱなしと同じ扱い
    //   ＝Prologue／Epilogue／StoryFilm など SkipHeld を読む側は無改造で追従する。切れ方は src/DialogToolbar.cs。
    public static bool SkipHeld => Input.IsKeyPressed(Key.Ctrl) || Pad.Pressed(JoyButton.RightShoulder) || SkipLatched;
    public bool FastForwarding => SkipHeld && (_dlgReadBefore || BattleMemoryTempo) && _messageTimer > 0 && _dlgText.Length > 0;

    public void ShowBanner(string text) { _bannerText = text; _bannerTimer = 5.0; _bannerTime = ""; _bannerBest = ""; _bannerScore = ""; _bannerScoreBest = ""; _epic = false; _bannerRewardLife = false; _bannerRewardBomb = false; _startStage = 0; }

    public void ShowStageStart(int stage, string name, Color accent)
    {
        ShowBanner($"STAGE {stage} START");
        _startStage = stage;
        _startName = name;
        _startAccent = accent;
        _bannerTimer = StageStartDur;
    }

    public void ShowRewardBanner(bool life, bool bomb)
    {
        ShowBanner(life && bomb ? "LIFE +1  BOMB +1" : life ? "LIFE +1" : "BOMB +1");
        _bannerRewardLife = life; _bannerRewardBomb = bomb;
        _bannerTimer = RewardBannerDur;
    }

    // FINAL 専用の「格上」タイトルカード。通常バナー（出て消えるだけの一行）とは別の描画経路に入る。
    //   ダサさの正体＝①全ステージ共通のベタ一行で FINAL に重みが無い ②字間0で小さく詰まって見える
    //   ③原色寄りの金1色でベタ塗り＝安い ④間(ため)が無く出た瞬間が頂点 ⑤画面が反応しない。
    // 対処＝黒レターボックス＋横罫で「額装」し、タグと副題を分離。字間を開けた一文字ずつの滲み出し、
    //   色収差(R/Cのズレ)＋走査線＋弱いグロー、そして最後に“ため”てから静かに引く。
    public void ShowEpicBanner(string tag, string sub, Color accent)
    {
        _startStage = 0;
        _epic = true; _epicTag = tag; _epicSub = sub; _epicAccent = accent;
        _bannerText = tag + " — " + sub; // バックログ/互換用に文字列は保持
        _bannerTimer = EpicDur; _bannerTime = ""; _bannerBest = ""; _bannerScore = ""; _bannerScoreBest = "";
    }

    private bool _epic;
    public bool EpicBannerActive => _epic && _bannerTimer > 0;
    private string _epicTag = "", _epicSub = "";
    private Color _epicAccent = UiKit.Kegare;
    private const double EpicDur = 5.2;   // 0.0 暗転寄せ → 1.0 タグ合わせ → 2.4 副題滲み → 3.4 ため → 5.2 引き


    // ゲームオーバーの選択UIに添える見出し／添え書き。どちらも空文字でクリア。
    public void ShowGameOverTitle(string text) { _gameOverTitle = text; }
    public void ShowGameOverPrompt(string text) { _gameOverPrompt = text; }

    // R 長押しリトライの充填率（0..1）。*Root.cs が毎フレーム渡す（0 で非表示）。
    public void SetRetryHold(float frac) { _retryHold = Mathf.Clamp(frac, 0f, 1f); }

    // クリアリザルト用バナー：見出し＋ TIME 行（＋自己ベスト更新なら NEW BEST! / でなければ旧ベスト併記）
    //   ＋ SCORE 行（タイムと同じ様式）。
    //   seconds=今回タイム、isBest=自己ベスト更新か、prevBest=更新前のベスト（初回 null）。
    //   score=今回スコア、scoreIsBest=自己ベスト更新か、prevScore=更新前のベスト（初回 null）。
    public void ShowClearBanner(string text, float seconds, bool isBest, float? prevBest,
        long score, bool scoreIsBest, long? prevScore)
    {
        ShowBanner(text);
        _bannerTime = "TIME " + UiKit.FormatTime(seconds);
        _bannerNewBest = isBest;
        if (isBest) _bannerBest = "NEW BEST!";
        else if (prevBest != null) _bannerBest = "BEST " + UiKit.FormatTime(prevBest.Value);
        else _bannerBest = "";
        _bannerScore = "SCORE " + UiKit.FormatScore(score);
        _bannerScoreNewBest = scoreIsBest;
        if (scoreIsBest) _bannerScoreBest = "NEW BEST!";
        else if (prevScore != null) _bannerScoreBest = "BEST " + UiKit.FormatScore(prevScore.Value);
        else _bannerScoreBest = "";
    }

    // 無防備窓サイクル用の短い字幕（弾を止めない＝テンポ維持）。BREAK の合図・RECLOSE の弱気セリフに使う。
    // 通常の会話バブル(ShowDialog)は BubblePaused を立てて弾を止めるため、これとは別経路。
    private string _bossLine = "";
    private string _bossLineSpeaker = "";
    private Color _bossLineCol = Colors.White;
    private double _bossLineTimer;
    private double _bossLineDuration;
    private bool _bossLineBreak;
    public void ShowBossLine(string speaker, string text, Color col, double dur, bool shieldBreak = false)
    {
        _bossLineSpeaker = speaker; _bossLine = text; _bossLineCol = col; _bossLineTimer = dur;
        _bossLineDuration = dur;
        _bossLineBreak = shieldBreak;
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
            ["レイ"]   = ("res://char/v3/cutin_rei_gawa_a.png", "初見さん、いらっしゃい!"),   // ボス＝ガワ（笑顔固定）。仮台本 07 の S3-6
            ["あかり"] = ("res://char/v3/cutin_akari.png",  "ねえ……まだ、そこにいる？"),
            ["こはる"] = ("res://char/v3/cutin_koharu.png", "ちゃんとしなきゃ。……みんな、見てるもん。"),
            ["ミナ"]   = ("res://char/v3/boss_mina_body_attack.png", "……おやめください。あなたまで、汚したくない……。"),
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

    // W0 専用・非正典。正典導線からは到達しない（2026-09-06 ユーザー決定: ヒカゲは使わない）。以後この系統への追加投資はしない。
    // 集中モードの状態を Player から毎フレーム受ける（旧 SetHikageSkill の置き換え）。
    public void SetFocusMode(bool has, bool ready, bool on, float ratio)
    { _focusHas = has; _focusReady = ready; _focusOn = on; _focusRatio = Mathf.Clamp(ratio, 0f, 1f); }

    // 現在のショットモードを設定。announce=true で切替トーストを表示。
    //   ★2026-09-13 ジョブ導入で V の切替が無くなり、現在の呼び出し元（Player の初回通知）は
    //     announce=false しか渡さない＝トースト経路は今のところ休眠している。消さずに残すのは、
    //     ハブのジョブ選択画面（第1段の残り）が「このランは◯◯で行く」を同じ語彙で出せるため。
    public void SetShotMode(GameManager.ShotMode m, bool announce)
    {
        _shotMode = m;
        if (announce) _shotModeToast = ShotModeToastDur;
    }

    public void ShowBossBar(string bossName) => ShowBossBar(bossName, "");

    // 頭上ゲージ（BossGauge）が読む状態のスナップショット。Tint はスペル色（未設定なら穢れ色）。
    public readonly record struct BossGaugeState(bool Visible, float Frac, int Index, int Total, Color Tint, float Fade, float Flash, float Purify);
    public BossGaugeState GaugeState => new(_bossVisible, _bossFrac, _bossBarIndex, _bossBarsTotal, _bossTint ?? UiKit.Kegare,
        (float)_bossCardFade, Mathf.Max(0f, (float)(_bossBarFlash / BossBarFlashDur)), (float)_bossPurify);
    private BossGauge? _bossGauge;

    // handle を明示すると固有ハンドルで表示（X世界観の没入＝§11）。空なら名前から自動生成（日本語名は @boss）。
    // owner＝バーの主（ボス／中ボス本体）。渡されたら頭上ゲージをその子として付ける（2026-09-27）。
    //   前のゲージ（中ボス→本ボス等）が残っていれば外す。owner 無し（旧呼び出し）なら状態だけ更新する。
    public void ShowBossBar(string bossName, string handle, Enemy? owner = null)
    {
        if (owner != null)
        {
            if (_bossGauge != null && IsInstanceValid(_bossGauge)) _bossGauge.QueueFree();
            _bossGauge = BossGauge.Attach(this, owner);
        }
        _bossName = bossName; _bossVisible = true;
        _bossTint = null; _bossBarFlash = 0; // 次のボスへ前ボスのスペル色/フラッシュを持ち越さない
        _bossPurify = 0; _bossCardFade = 1f; // 前のボスの「浄化しきった見送り」を持ち越さない
        ResetSpellCutin();   // ボス戦開始＝このボス戦のカットイン初回フラグをリセット
        if (!string.IsNullOrEmpty(handle))
        {
            _bossHandle = handle;
            _bossFaceId = FaceIdFor(handle, bossName);
            return;
        }
        string auto = "@" + System.Text.RegularExpressions.Regex.Replace(bossName, "[^A-Za-z0-9]", "").ToLower();
        if (auto.Length <= 1) auto = "@boss";
        _bossHandle = Handles.Garble(auto);   // 自動生成も化かす（ASCII のハンドルを画面に出す経路を残さない）
        _bossFaceId = FaceIdFor(_bossHandle, bossName);
    }

    // ハンドル（＋保険で名前）から顔ID を引く。BossHandles の定数はすべて "@akãri…" "@køharu…"
    //   "@rëi…"/"@hoshiai_rëi…" "@mïna…" の形なので、Handles.Plain で化けを戻せばキャラ名の部分文字列で確実に決まる。
    //   中ボス（CameoBoss）はそのステージの本人が出るのが実装（StageRei→"レイ"/@rëi_____6390 等）＝
    //   本ボスと同じ顔でよい。別人格の別アイコンにはしない（同じ人が道中で先に立ち塞がる話のため）。
    //   該当なし（W0 のヒカゲ等）は "" ＝従来どおり無地の穢れ円に落ちる。
    private static string FaceIdFor(string handle, string name)
    {
        string h = Handles.Plain(handle).ToLowerInvariant();   // 化けた綴り（akãri 等）を戻してから見る
        if (h.Contains("akari")) return "akari";
        if (h.Contains("koharu")) return "koharu";
        if (h.Contains("rei")) return "rei";
        if (h.Contains("mina")) return "mina";
        // 保険：ハンドルが未知でも日本語名から拾う（FINAL のフェーズ名は "穢れたわたし" 等で拾えない）。
        if (name.Contains("あかり")) return "akari";
        if (name.Contains("こはる")) return "koharu";
        if (name.Contains("レイ")) return "rei";
        if (name.Contains("ミナ")) return "mina";
        return "";
    }

    // ボスのプロフィールもハブと同じSNSアカウントを表示する。
    private Texture2D? BossFace(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_bossFaces.TryGetValue(id, out var cached)) return cached;
        var tex = GD.Load<Texture2D>(CompanionDialogue.AccountIcon(id));
        _bossFaces[id] = tex;
        return tex;
    }

    // アカウント色（Hub.AccountColor と同じ）。浄化しきった縁の色に使う。
    private static Color BossAccent(string id) => id switch
    {
        "mina" => UiKit.Mina,
        "rei" => new Color(0.90f, 0.52f, 0.38f),
        "akari" => new Color(0.40f, 0.62f, 0.88f),
        "koharu" => new Color(0.46f, 0.74f, 0.52f),
        _ => UiKit.Kegare,
    };
    // 1本リフィル方式：メインバーは「現在の1本ぶん」を 0〜1 で描く。残バー数は pip と「残/総」で示す。
    public void UpdateBossBar(int barIndex, int totalBars, float frac)
    {
        _bossBarsTotal = Mathf.Max(1, totalBars);
        _bossBarIndex = Mathf.Clamp(barIndex, 0, _bossBarsTotal - 1);
        _bossFrac = Mathf.Clamp(frac, 0f, 1f);
    }
    // 改心（各ボス OnCryStart）で呼ばれる。顔が出るボスは即消しにせず、
    //   「穢れが剥がれてアイコンが晴れる」一拍（BossPurifyDur）を見せてから畳む（見せ場）。
    //   顔が無いボス（ヒカゲ等）と、そもそも出ていない場合は従来どおり即 Hide。
    public void HideBossBar()
    {
        if (!_bossVisible || string.IsNullOrEmpty(_bossFaceId) || BossFace(_bossFaceId) == null)
        { _bossVisible = false; return; }
        if (_bossPurify <= 0) _bossPurify = 0.0001;   // 0 のままだと「浄化中」に入らないので種を置く
    }
    // スペル宣告カード（＋袖カットイン）を即時に消す。会話バブル中は _spellTimer が停止する仕様のため、
    // 改心開始（各ボス OnCryStart）で明示的に消さないと、宣告カードが改心演出〜帰還会話まで残留する。
    public void HideSpellCard() { _spellTimer = 0; _spellGlow = 0; _cutinTimer = 0; _cutinTex = null; }

    // ── フェーズ移行の可視化（#26）──
    // 現行スペルの色をHPバーへ連動させる（各ボスの ApplySpell が呼ぶ）。null=既定の穢れ色。
    private Color? _bossTint;
    public void SetBossBarTint(Color c) => _bossTint = c;
    public void SetBossPhaseName(string name) => _bossName = name;
    // HPバー1本割れの白フラッシュ（Enemy の本体ヒットでバー境界を跨いだ瞬間に焚く）。
    private double _bossBarFlash;
    private const double BossBarFlashDur = 0.32;
    public void FlashBossBarBreak() => _bossBarFlash = BossBarFlashDur;

    // 残機セット。減った瞬間（被弾）は、消える核マーク1個を「砕けて散る」で見送る（2026-09-16）。
    //   消えるのは index=新残数 のマーク（点灯は 0.._lives-1 なので、境界の1個が暗転する）。
    public void SetLives(int n)
    {
        n = Mathf.Max(0, n);
        if (_livesInited && n < _lives) StartLifeShatter(n);
        _lives = n;
        _livesInited = true;
    }

    public void Flash() { _flashRgb = new Color(1f, 1f, 1f); _flashAlpha = 0.55f; }
    public void HitFlash() { _flashRgb = new Color(1f, 0.2f, 0.28f); _flashAlpha = 0.7f; _hurtEdge = 0.9; }

    // ───────── 描画（子 HudCanvas から呼ばれる。設計座標 1280x720）─────────
    public void DrawAll(HudCanvas ci)
    {
        UiKit.BeginDesign(ci);
        if (CinematicMode)
        {
            if (_dlgText.Length > 0) { DrawDialog(ci); DrawDialogToolbar(ci); }
            UiKit.EndDesign(ci);
            return;
        }
        DrawSidePanel(ci);   // 最初に描く＝背景(384幅のまま)の上に不透明の板を被せてプレイ領域を切り出す
        DrawLifeBomb(ci);
        DrawPurify(ci);
        DrawScore(ci);
        DrawTimer(ci);
        DrawLockOn(ci);
        DrawCombo(ci);
        // ボスの体力は画面上端のカード（DrawBossCard）ではなく、ボス本体の頭上の簡略ゲージ（src/BossGauge.cs）が描く
        //   （2026-09-27 作者指示）。状態はこの Hud が持ち、BossGauge は GaugeState を読む。DrawBossCard は呼ばない。
        // 【激情】メーターは HUD ではなく、盤面の奥（ZIndex -44）にボスの下書き（入力欄）として敷く → src/FuryDial.cs。
        // 会話バー・ボスの一行字幕・スペル宣告カードは、BubbleLayer（世界側・弾より奥）が居ればそちらが描く
        //（作者指摘：文字枠が自機と弾を隠す）。居ない場面（保険）だけ従来どおりここ＝最前面に描く。
        bool sunk = Bubbles != null;
        if (_cutinTimer > 0 && _cutinTex != null) DrawSpellCutin(ci); // 袖カットイン（カードより先＝上中央カードを侵さない）
        if (!sunk && _spellTimer > 0) DrawSpellCard(ci);
        DrawShotMode(ci);
        DrawPowerups(ci);
        if (_focusHas) DrawFocusChip(ci);
        if (TickerEnabled) DrawTicker(ci);
        if (_tutorialHint.Length > 0) DrawTutorialHint(ci);
        if (_tutorialOp.Length > 0) DrawTutorialKeys(ci);
        if (_shotModeToast > 0) DrawShotModeToast(ci);
        // チュートリアルのスポット暗転は会話/バナーより前(下)に描く＝会話テキスト・立ち絵は
        // 暗幕の上にフル輝度で読める（暗転がセリフ枠を覆って読みづらい問題への対処 #3）。
        // 弾より前だが、α上限0.55で弾は透ける。会話ボックス矩形も穴抜きの対象にして二重に保護する。
        if (_spotActive) DrawTutorialSpot(ci);
        if (!sunk && _dlgText.Length > 0) DrawDialog(ci);
        // ボタン列は会話バーが弾の奥（BubbleLayer）に沈んでいても最前面のここに描く＝弾に隠れずクリックできる。
        if (_dlgText.Length > 0) DrawDialogToolbar(ci);
        if (!sunk && _bossLineTimer > 0 && _bossLine.Length > 0) DrawBossLine(ci);
        if (_bannerTimer > 0) DrawBanner(ci);
        if (_gameOverTitle.Length > 0) DrawGameOverTitle(ci);
        if (_gameOverPrompt.Length > 0) DrawGameOverPrompt(ci);
        if (_retryHold > 0f) DrawRetryHoldChip(ci, _retryHold, "R 長押しでリトライ");
        // 被弾エッジ
        if (_hurtEdge > 0)
            UiKit.Box(ci, new Rect2(Field.DLeft + 8, 8, Field.DWidth - 16, 720 - 16), null, 18f, new Color(0.9f, 0.16f, 0.16f, 0.5f * (float)(_hurtEdge / 0.9)), 14f);
        // フラッシュ（全画面・最前面）
        if (_flashAlpha > 0f)
            ci.DrawRect(new Rect2(Field.DLeft, 0, Field.DWidth, 720), new Color(_flashRgb.R, _flashRgb.G, _flashRgb.B, _flashAlpha));
        UiKit.EndDesign(ci);
    }

    // ───────── 吹き出し（世界側の BubbleLayer から呼ばれる。設計座標 1280x720・弾より奥）─────────
    //   DrawAll と同じ順（宣告カード → 会話 → 一行字幕）。見た目・位置は Hud に居た時と同じで、層だけが弾の奥。
    //   カットシーン（CinematicMode）は弾が無いので従来どおり DrawAll（最前面）が描き、ここでは何も描かない。
    public void DrawBubbles(CanvasItem ci)
    {
        if (CinematicMode) return;
        UiKit.BeginDesign(ci);
        if (_spellTimer > 0) DrawSpellCard(ci);
        if (_dlgText.Length > 0) DrawDialog(ci);
        if (_bossLineTimer > 0 && _bossLine.Length > 0) DrawBossLine(ci);
        UiKit.EndDesign(ci);
    }

    private static readonly Color SideSurface = new("242629");
    private static readonly Color SideRaised = new("2c2f33");
    private static readonly Color SideInk = new("f0f1f3");
    private static readonly Color SideMuted = new("adb5bd");
    private static readonly Color SideRule = new("40464d");
    private static readonly Color SideTeal = new("7bcbbc");
    private static readonly Color SideRose = new("f28bab");
    private Color AccountAccent => _game.SelectedJob switch
    {
        Job.Melee => new Color("e9bd7c"),
        Job.Heal => new Color("83cfb0"),
        Job.Magic => new Color("a0cdee"),
        _ => new Color("bdb1e1"),
    };

    // Stage art extends behind the HUD, so the sidebar must remain opaque.
    private void DrawSidePanel(HudCanvas ci)
    {
        const float pw = Field.PanelW;
        ci.DrawRect(new Rect2(0, 0, pw, UiKit.DesignH), SideSurface);
        ci.DrawRect(new Rect2(0, 0, pw, 156f), SideRaised.Lerp(AccountAccent, 0.05f));
        ci.DrawRect(new Rect2(0, 0, pw, 4f), AccountAccent);
        ci.DrawRect(new Rect2(0, 156f, pw, 1f), SideRule);
        ci.DrawRect(new Rect2(pw - 1f, 0, 1f, UiKit.DesignH), SideRule);
        ci.DrawRect(new Rect2(pw, 0, 8f, UiKit.DesignH), new Color(0, 0, 0, 0.12f));
        ci.DrawRect(new Rect2(Field.DLeft, 0, 1f, UiKit.DesignH), new Color(1f, 1f, 1f, 0.07f));
        ci.DrawRect(new Rect2(PanelX, 330f, PanelInnerW, 1f), SideRule);
        ci.DrawRect(new Rect2(PanelX, 411f, PanelInnerW, 1f), SideRule);
        ci.DrawRect(new Rect2(PanelX, 554f, PanelInnerW, 1f), SideRule);
    }

    private const float PanelX = 26f;
    private const float PanelInnerW = Field.PanelW - PanelX * 2f;
    private const float RowLifeBomb = 180f;
    private const float RowPurify = 350f;
    private const float RowScore = 428f;
    private const float RowTime = 519f;
    // 2026-09-26: TIME の下に LOCK-ON 行を足した（作者指示「ステータス欄でロックオンモードが分かるように」）。
    //   COMBO 576→606・集中 649→660 に詰めて場所を空けた（集中のバーの下端 698 ＜ 720）。
    private const float RowLock = 576f;
    private const float RowCombo = 606f;
    private const float RowFocus = 660f;

    // 操作子バッジの寸法（先に幅を測ってレイアウトする呼び出し側と KeyBadge 本体で必ず同じ式を使う）。
    private const float KeyBadgeH = 21f;
    private static float KeyBadgeW(string token) => UiKit.TrackedW(UiKit.SmallLabel, token) + 14f;

    // 操作子バッジ（小さなキー枠）。情報の隣に添えて「どのボタンか」を一目で示す。描いた幅を返す。
    private float KeyBadge(HudCanvas ci, Vector2 p, string token, Color accent, float a = 1f, bool sidebar = false)
    {
        // キー名は英字ラベル＝小ラベル(ZenBold 13・字間+0.5)。旧 Mono 11 は実効3.3pxで読めなかった。
        float w = KeyBadgeW(token), h = KeyBadgeH;
        UiKit.Box(ci, new Rect2(p.X, p.Y, w, h), sidebar ? SideRaised : new Color(0.10f, 0.09f, 0.16f, 0.92f * a),
            4f, sidebar ? SideRule : new Color(accent, 0.75f * a), 1f);
        UiKit.Draw(ci, UiKit.SmallLabel, new Vector2(p.X + 7, p.Y + 3), token, new Color(accent, 0.98f * a));
        return w;
    }

    private void DrawLifeBomb(HudCanvas ci)
    {
        int maxLives = Mathf.Max(_lives, (GetTree().GetFirstNodeInGroup("player") as Player)?.MaxLives ?? _game.StartLives);
        int bombs = _game?.Bombs ?? 0;
        int maxBombs = Mathf.Max(bombs, _game?.StartBombs ?? 4);
        bool low = _lives <= 2;

        float x = PanelX, y = RowLifeBomb, w = PanelInnerW;
        Color lifeColor = low ? SideRose : SideMuted;
        if (low) ci.DrawRect(new Rect2(0, y - 4, 4f, 62f), SideRose);
        UiKit.Draw(ci, UiKit.PanelLabel, new Vector2(x, y), "LIFE", lifeColor);
        UiKit.DrawRight(ci, UiKit.SmallValue, x + w, y + 2f, $"{_lives:D2} / {maxLives:D2}", lifeColor);
        float hStep = Mathf.Min(42f, w / Mathf.Max(1, maxLives));
        var mark = _lifeMarks[_game!.SelectedJob];
        var markSize = mark.GetSize();
        markSize *= Mathf.Min(38f, hStep - 4f) / Mathf.Max(markSize.X, markSize.Y);
        for (int i = 0; i < maxLives; i++)
        {
            var center = new Vector2(x + i * hStep + hStep / 2f, y + 40f);
            ci.DrawTextureRect(mark, new Rect2(center - markSize / 2f, markSize), false,
                new Color(1, 1, 1, i < _lives ? 1f : 0.22f));
        }
        UiKit.Draw(ci, UiKit.PanelLabel, new Vector2(x, y + 78f), "BOMB", SideMuted);
        float badgeW = KeyBadgeW(TokBomb);
        KeyBadge(ci, new Vector2(x + w - badgeW, y + 77f), TokBomb, SideMuted, sidebar: true);
        float bStep = Mathf.Min(44f, w / Mathf.Max(1, maxBombs));
        var bombSize = _bombMark.GetSize();
        bombSize *= Mathf.Min(40f, bStep - 4f) / Mathf.Max(bombSize.X, bombSize.Y);
        for (int i = 0; i < maxBombs; i++)
        {
            var center = new Vector2(x + i * bStep + bStep / 2f, y + 118f);
            ci.DrawTextureRect(_bombMark, new Rect2(center - bombSize / 2f, bombSize), false,
                new Color(1, 1, 1, i < bombs ? 1f : 0.22f));
        }
    }

    // ───────── 被弾のライフ演出（2026-09-16）─────────
    //   減った核マーク1個が「砕けて飛び散って消える」。加算ブレンドの専用子キャンバス（_addCanvas）に
    //   描くので、サイドパネル内で完結し盤面の弾は一切隠さない。約0.55秒・欠片12枚＋割れた瞬間の短命グロー。
    private const double LifeShatterDur = 0.55;
    private const int LifeShardCount = 12;
    private struct LifeShard { public float Ang, Spd, Size, Rot, Spin; }
    private readonly LifeShard[] _lifeShards = new LifeShard[LifeShardCount];
    private int _lifeLostIndex = -1;      // 散っているマークの index（SetLives 減少時＝新残数の位置）
    private double _lifeShatterT;         // 残り秒（0 で非表示）
    private readonly RandomNumberGenerator _fxRng = new();

    private void StartLifeShatter(int lostIndex)
    {
        _lifeLostIndex = lostIndex;
        _lifeShatterT = LifeShatterDur;
        for (int i = 0; i < LifeShardCount; i++)
        {
            // 全周へ散らす。角度は等分＋ゆらぎ＝偏りなく「割れた」形に見せる。
            _lifeShards[i] = new LifeShard
            {
                Ang = Mathf.Tau * i / LifeShardCount + _fxRng.RandfRange(-0.25f, 0.25f),
                Spd = _fxRng.RandfRange(30f, 64f),     // 飛距離（設計座標px）。サイドパネル内にほぼ収まる
                Size = _fxRng.RandfRange(2.5f, 5.5f),
                Rot = _fxRng.RandfRange(0f, Mathf.Tau),
                Spin = _fxRng.RandfRange(-6f, 6f),
            };
        }
    }

    // 加算キャンバスの描画（HudAddCanvas._Draw から）。今はライフ砕け散りのみ。
    public void DrawAdditive(HudAddCanvas ci)
    {
        if (_lifeShatterT <= 0 || _lifeLostIndex < 0 || CinematicMode) return;
        UiKit.BeginDesign(ci);
        // 消えたマークの中心＝DrawLifeBomb と同じレイアウト式（レイアウト変更に自動追随）。
        int maxLives = Mathf.Max(_lives, (GetTree().GetFirstNodeInGroup("player") as Player)?.MaxLives ?? _game.StartLives);
        float hStep = Mathf.Min(42f, PanelInnerW / Mathf.Max(1, maxLives));
        var center = new Vector2(PanelX + _lifeLostIndex * hStep + hStep / 2f, RowLifeBomb + 40f);
        float t = 1f - (float)(_lifeShatterT / LifeShatterDur);   // 0→1
        float fly = 1f - Mathf.Pow(1f - t, 3f);                   // out-cubic＝はじけて減速
        float a = (1f - t) * (1f - t);                            // 早めに減衰＝派手にしない
        Color col = SideRose.Lerp(Colors.White, 0.35f);
        // 割れた瞬間の芯グロー（一拍で消える）。
        if (t < 0.35f)
        {
            float g = 1f - t / 0.35f;
            UiKit.RadialGlow(ci, center, 10f + 24f * g, col, 0.45f * g);
        }
        // 欠片：小さな回転矩形が外へ飛びつつ、わずかに落ちて薄れる（「こぼれた」感）。
        foreach (var s in _lifeShards)
        {
            var pos = center + new Vector2(Mathf.Cos(s.Ang), Mathf.Sin(s.Ang)) * (s.Spd * fly)
                      + new Vector2(0f, 14f * t * t);
            float sz = s.Size * (1f - 0.5f * t);
            ci.DrawSetTransform(pos * UiKit.Scale, s.Rot + s.Spin * t, new Vector2(UiKit.Scale, UiKit.Scale));
            ci.DrawRect(new Rect2(-sz / 2f, -sz / 2f, sz, sz), new Color(col, a));
        }
        UiKit.EndDesign(ci);
    }

    private void DrawPurify(HudCanvas ci)
    {
        float prog = _game?.StageProgress ?? 0f;
        bool full = prog >= 0.999f;
        float x = PanelX, y = RowPurify, w = PanelInnerW;
        // Keep the forward-position pulse on the fill without flashing the whole sidebar.
        float posF = _game?.CurrentPosFactor ?? 1.075f;
        float lean = Mathf.Clamp((posF - 0.55f) / 1.05f, 0f, 1f); // 左端0 → 右端1
        float pulseHz = Mathf.Lerp(2.4f, 7.0f, lean);
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_t * pulseHz);
        UiKit.Draw(ci, UiKit.PanelLabel, new Vector2(x, y), "浄化", SideTeal);
        UiKit.DrawRight(ci, UiKit.PanelValueMid, x + w, y - 1f, $"{Mathf.RoundToInt(prog * 100f)}%", SideTeal);
        UiKit.Box(ci, new Rect2(x, y + 34f, w, 8f), SideRule, 4f);
        if (prog > 0)
            UiKit.Box(ci, new Rect2(x, y + 34f, w * prog, 8f),
                full ? SideTeal : SideTeal.Lerp(new Color("9bdfd0"), pulse * lean * 0.5f), 4f);
    }

    private void DrawScore(HudCanvas ci)
    {
        long score = _game?.Score ?? 0;
        string scoreStr = score.ToString("000,000");
        float x = PanelX, y = RowScore;
        UiKit.Draw(ci, UiKit.PanelLabel, new Vector2(x, y), "SCORE", SideMuted);
        int size = 36;
        while (size > 13 && UiKit.TextW(UiKit.Mono, scoreStr, size) > PanelInnerW) size--;
        UiKit.Text(ci, UiKit.Mono, new Vector2(x, y + 29f), scoreStr, size, SideInk);
    }

    private void DrawTimer(HudCanvas ci)
    {
        string t = UiKit.FormatTime(_elapsed);
        UiKit.Draw(ci, UiKit.PanelLabel, new Vector2(PanelX, RowTime), "TIME", SideMuted);
        UiKit.DrawRight(ci, UiKit.PanelValueMid, PanelX + PanelInnerW, RowTime - 2f, t, SideInk);
    }

    // ロックオンモードの行（2026-09-26 作者指示「画面から敵が消えてもロックオン解除しないで、
    //   ステータス欄でロックオンモードかを分かるように」）。Player.LockArmed（狙う意思）と LockedOn（今掴んでいる）を分けて出す。
    //   OFF ＝ 灰 ／ 待機（モードは立っているが画面に敵が居ない）＝ 青緑でゆっくり明滅 ／ 追尾中 ＝ ジョブ色。
    //   待機は盤面に照準マーカーが出ないので、ここが唯一の手がかり。
    private void DrawLockOn(HudCanvas ci)
    {
        var player = GetTree().GetFirstNodeInGroup("player") as Player;
        bool armed = player?.LockArmed ?? false;
        bool on = player?.LockedOn ?? false;
        float x = PanelX, y = RowLock, w = PanelInnerW;
        Color accent = on ? AccountAccent : armed ? SideTeal : SideMuted;
        UiKit.Draw(ci, UiKit.PanelLabel, new Vector2(x, y), "LOCK-ON", accent);
        float lw = UiKit.TrackedW(UiKit.PanelLabel, "LOCK-ON");
        KeyBadge(ci, new Vector2(x + lw + 12f, y - 2f), TokLock, SideMuted, sidebar: true);
        string status = on ? "追尾中" : armed ? "待機" : "OFF";
        float a = armed && !on ? 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin((float)_t * 4f)) : 1f;
        float sw = UiKit.TrackedW(UiKit.SmallLabel, status);
        ci.DrawCircle(new Vector2(x + w - sw - 14f, y + 10f), 4f, new Color(accent, armed ? a : 0.35f));
        UiKit.DrawRight(ci, UiKit.SmallLabel, x + w, y + 2f, status, new Color(accent, a));
    }

    private void DrawCombo(HudCanvas ci)
    {
        int combo = _game?.Combo ?? 0;
        if (combo < 2) return;
        float x = PanelX, y = RowCombo, w = PanelInnerW;
        UiKit.Draw(ci, UiKit.PanelLabel, new Vector2(x, y), "COMBO", SideRose);
        UiKit.Text(ci, UiKit.Mono, new Vector2(x, y - 7f), $"×{combo:D2}", 28, SideRose, HorizontalAlignment.Right, w);
        float comboRatio = Mathf.Clamp(_game?.ComboTimeRatio ?? 0f, 0f, 1f);
        float cbY = y + 34f, cbH = 4f;
        UiKit.Box(ci, new Rect2(x, cbY, w, cbH), SideRule, 2f);
        if (comboRatio > 0)
            UiKit.Box(ci, new Rect2(x, cbY, w * comboRatio, cbH), UiKit.Burn.Lerp(SideRose, comboRatio), 2f);
    }

    private void DrawBossCard(HudCanvas ci)
    {
        // 盤面の上端に残す唯一の常設UI（docs/20260906/HUD整理_案.md §4）。中心は盤面の中心、
        // 高さ 60→44・y 60→8 に詰めて、ボスの真上の薄い帯だけを使う。幅は盤面幅の 8 割。
        // 2026-09-16: y 8→24（内部座標で約5px下げ）。上端に張り付いて見づらい実機指摘への対処。
        //   中ボス（CameoBoss）も本ボスもこのカード共通＝両方下がる。スペル宣告カード（DrawSpellCard）の
        //   y も連動して 66→82 に下げた。
        float w = Mathf.Min(560f, Field.DWidth * 0.8f), x = Field.DCenterX - w / 2f, y = 24f, h = 44f;
        float ca = (float)_bossCardFade;   // 改心の見送りでカードごと引く不透明
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(18 / 255f, 12 / 255f, 22 / 255f, 0.62f * ca), 16f, new Color(UiKit.Kegare, 0.4f * ca), 1.2f);
        DrawBossAvatar(ci, new Vector2(x + 34, y + h / 2f), ca);
        // 名前＋ハンドル＋リプ
        float tx = x + 70;
        var rose = new Color("f0a8cf") with { A = ca };
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(tx, y + 4), _bossName, 17, UiKit.White with { A = ca });
        float nw = UiKit.TextW(UiKit.ZenBold, _bossName, 17);
        UiKit.Text(ci, UiKit.Mono, new Vector2(tx + nw + 10, y + 7), _bossHandle, 13, UiKit.Text3 with { A = ca });
        // 残バー数（=index+1）と総バー数。リプ数は総HP比で減らす（演出）。
        int barsLeft = _bossBarIndex + 1;
        float overall = (_bossBarIndex + _bossFrac) / _bossBarsTotal;
        string rep = UiKit.Abbrev((long)(_bossReplies * overall));
        UiKit.DrawRight(ci, UiKit.SmallValue, x + w - 16, y + 7, rep, rose);
        // 穢れバー（現在の1本ぶん）＋残バー数の● pip。
        // バー/pip の色は現行スペルの色に連動（#26 フェーズ移行の可視化。未設定なら既定の穢れ色）。
        Color barCol = (_bossTint ?? UiKit.Kegare) with { A = ca };
        UiKit.Draw(ci, UiKit.SmallLabel, new Vector2(tx, y + 25), "穢れ", rose);
        float pipsW = _bossBarsTotal * 9f;
        float barX = tx + UiKit.TrackedW(UiKit.SmallLabel, "穢れ") + 8f, barW = w - (barX - x) - 66 - pipsW, barY = y + 28;
        UiKit.Box(ci, new Rect2(barX, barY, barW, 10f), new Color(1, 1, 1, 0.07f * ca), 5f);
        if (_bossFrac > 0) UiKit.Box(ci, new Rect2(barX, barY, barW * _bossFrac, 10f), barCol, 5f);
        // バー1本割れの白フラッシュ（割れた一拍を「ゲージが光る」で読ませる）。
        if (_bossBarFlash > 0)
        {
            float f = (float)(_bossBarFlash / BossBarFlashDur);
            UiKit.Box(ci, new Rect2(barX, barY, barW, 10f), new Color(1f, 1f, 1f, 0.7f * f * ca), 5f);
        }
        // 残バー pip（左から「残っている本数」を満たす）。
        float pipX = barX + barW + 8f;
        for (int i = 0; i < _bossBarsTotal; i++)
            ci.DrawCircle(new Vector2(pipX + i * 9f + 3f, barY + 5f), 3f,
                i < barsLeft ? barCol : barCol with { A = 0.22f * ca });
        // 「残/総」表示。
        UiKit.DrawRight(ci, UiKit.SmallValue, x + w - 16, y + 24, $"{barsLeft}/{_bossBarsTotal}", rose);
    }

    // ── ボスカードのアバター（X のプロフィールアイコン）──
    // 2026-09-17: ここは長らく「穢れ色の無地の円」だった。X のプロフィールカードを模した意匠なのに、
    //   本来アイコンが入る座が空で、誰と戦っているのかが名前の文字だけに頼っていた。
    //   ハブの投稿カードと同じ顔素材・同じ UiKit.FaceAvatar（円クリップ＋topCrop の顔位置合わせ）で
    //   出し、「TL で見たあのアカウントが、いま目の前で暴れている」を結ぶ。
    //
    // 穢れの表現（＝平常時のハブのアイコンと必ず見分けが付くこと）：
    //   ① 顔の上に穢れ色のベール（乗算寄りの暗い紫を被せて沈める）＝顔は判るが血の気が無い
    //   ② リングは穢れ色（アカウント色ではない）＝ハブの平常アイコンは各自のアカウント色
    //   ③ 背面の穢れグロウが呼吸で脈打つ＝「まだ穢れている」
    // 改心（HideBossBar）後：_bossPurify 0→1 でベールが剥がれ、リングが穢れ色→アカウント色へ、
    //   グロウが穢れ色→浄化色へ抜ける。顔が晴れる一拍を見せてからカードごと引く。
    private void DrawBossAvatar(HudCanvas ci, Vector2 ac, float ca)
    {
        const float R = 22f;
        var face = BossFace(_bossFaceId);
        float p = (float)_bossPurify;                      // 0=穢れたまま 1=浄化しきり
        float pe = p * p * (3f - 2f * p);                  // smoothstep（剥がれ際を滑らかに）

        // 背面グロウ。穢れの間は脈打ち、浄化で色が抜けて広がる。
        Color glowCol = UiKit.Kegare.Lerp(UiKit.PurifyHi, pe);
        float pulse = 0.4f + 0.10f * Mathf.Sin((float)_t * 3.2f) * (1f - pe);
        UiKit.RadialGlow(ci, ac, (28f + 10f * pe), glowCol, (pulse + 0.35f * pe) * ca);

        if (face == null)
        {
            // 顔素材が無いボス（W0 ヒカゲ等）は従来どおりの無地の穢れ円。
            ci.DrawCircle(ac, R, new Color(0.35f, 0.13f, 0.27f, ca));
        }
        else
        {
            // 顔本体。リングは穢れ色→アカウント色へ。topCrop はハブと同じ実測値＝頭が切れない。
            Color ring = UiKit.Kegare.Lerp(BossAccent(_bossFaceId), pe);
            UiKit.FaceAvatar(ci, ac, R, face, ring, false, 0f, ca, _t);
            // 穢れのベール：顔の上に穢れ色を被せて血の気を落とす。浄化で引いていく。
            //   濃さは 0.38。実測（2026-09-17 スクショ）で 0.52 だと髪の暗いレイ／こはるが
            //   シルエットに潰れて誰か判らなくなった。顔が判る／でも明らかに病んでいる、の境目がここ。
            float veil = 0.38f * (1f - pe);
            if (veil > 0.002f) ci.DrawCircle(ac, R, new Color(0.34f, 0.07f, 0.26f, veil * ca));
            // 浄化しきった瞬間の白い抜け（顔が晴れる一拍）。中盤で最大、終わりに消える。
            float flash = Mathf.Sin(pe * Mathf.Pi);
            if (flash > 0.01f) ci.DrawCircle(ac, R, new Color(UiKit.PurifyHi, 0.40f * flash * ca));
        }

        // 認証バッジ（右下）。顔と重なる位置だが X の実物と同じ置き方で、r=9 は顔の縁にかかるだけ。
        //   下敷きを一段暗く敷いてから穢れ色→浄化色の丸を置き、✓ が顔の柄に埋もれないようにする。
        Vector2 bc = ac + new Vector2(15, 15);
        ci.DrawCircle(bc, 10.5f, new Color(0.07f, 0.05f, 0.10f, 0.9f * ca));
        ci.DrawCircle(bc, 9f, UiKit.Kegare.Lerp(UiKit.Purify, pe) with { A = ca });
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(ac.X + 11, ac.Y + 6), "✓", 11, UiKit.White with { A = ca });
    }

    // スペル宣言オーバーレイ（X のスペル発動ツイート＋通知）。ボスカードの直下に出る。
    private void DrawSpellCard(CanvasItem ci)   // CanvasItem＝HudCanvas（保険）と BubbleLayer（通常）の両方から描ける
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
        float headW = UiKit.TextW(UiKit.ZenBold, _spellWho, 15) + UiKit.TextW(UiKit.Mono, _spellHandle, 13) + 96f;
        // 幅の上限は盤面幅（880px）に収める＝サイドパネルへはみ出さない。
        float w = Mathf.Clamp(Mathf.Max(titleW, headW) + 84f, 380f, Mathf.Min(780f, Field.DWidth - 40f));
        float h = 60f;
        float x = Field.DCenterX - w / 2f, y = 82f + slide;   // ボスバー(y=24,h=44)の直下（2026-09-16 バーの下げに連動）
        Vector2 center = new(Field.DCenterX, y + h / 2f);

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
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(tx, top + 9), _spellWho, 15, new Color(1, 1, 1, a));
        float nw = UiKit.TextW(UiKit.ZenBold, _spellWho, 15);
        UiKit.Text(ci, UiKit.Mono, new Vector2(tx + nw + 8, top + 11), _spellHandle, 13, new Color(UiKit.Text3, a));
        // 右肩「● スペル発動」（発動直後は明滅で“今来た”を主張）
        string tag = "スペル発動";
        float tagW = UiKit.TrackedW(UiKit.SmallLabel, tag) + 14;
        float tagPulse = 0.7f + 0.3f * Mathf.Sin((float)age * 12f);
        ci.DrawCircle(new Vector2(-w / 2f + w - tagW - 8, top + 15), 3.4f, new Color(col, a * tagPulse));
        UiKit.Draw(ci, UiKit.SmallLabel, new Vector2(-w / 2f + w - tagW, top + 8), tag, new Color(col, a * tagPulse));
        // スペル名（少し大きく・明るく＝視認のピーク）
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(tx, top + 31), title, 17,
            new Color(0.96f + 0.04f * glow, 0.92f, 0.97f, a));

        // 設計スケールへ戻す（後続描画に影響させない）。
        ci.DrawSetTransform(Vector2.Zero, 0f, new Vector2(UiKit.Scale, UiKit.Scale));
    }

    // スペル宣言の袖カットイン（吉田明彦：anticipation→slide-in→着地flash+shake→hold→袖へ抜ける）。
    //   2026-09-07：左にサイドパネルができたので、袖を左→右へ反転した（左から差すと板と衝突する）。
    //   盤面の右端に密着し、不透明は着地〜ホールドの一拍だけ。以降はフェード＋袖へ戻りつつ消える＝弾の視認を守る。
    //   dx の符号はそのまま「袖の外へ逃がす量」を意味し、右袖では +x が外側になるので描画時に反転する。
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

        a *= _calloutA; // 割り込み中は抑制フェード（バトルセリフ sa も a 由来なので一緒に消える）

        // 着地の瞬間：白フラッシュ1F＋既存 Shake（演出ビートの“止め／決め”）。描画ループ内なので一度だけ立てる。
        if (justLanded && !_cutinLandedFlashed)
        {
            _cutinLandedFlashed = true;
            _flashRgb = new Color(1f, 1f, 1f); _flashAlpha = 0.28f; // 控えめな白フラッシュ（弾を飛ばさない程度）
            GameCamera.Instance?.Shake(2.2f, 0.16f);
        }
        if (age < CutinSlideDur) _cutinLandedFlashed = false;       // 次回着地で再びフラッシュできるよう戻す

        // 描画寸法：高さ CutinRenderH を基準に元アスペクトで幅算出。右端に密着（袖は右）。
        float ph = CutinRenderH;
        float pw = ph * _cutinTex.GetWidth() / Mathf.Max(1, _cutinTex.GetHeight());
        // 右袖：絵の右端を画面右端の少し外へ出して密着感。dx（負＝袖の外）は右では +x なので符号を反転する。
        float baseX = UiKit.DesignW + 10f - pw;              // 右端を少しだけ画面外に出して密着感
        float x = baseX - dx;
        float y = 720f - ph - 6f;                            // 下端を画面下に寄せる（袖から立ち上がる構図）

        // リムの色を一点：着地直後（インパクト区間）だけスペル tint の加算グローを輪郭背後に薄く（弾は隠さない）。
        // 半透明滞在では glow を消す（余韻は静かに・弾を侵さない）。
        float glow = Mathf.Clamp((float)((tImpactEnd - age) / CutinImpactDur), 0f, 1f);
        if (glow > 0.001f)
            UiKit.RadialGlow(ci, new Vector2(x + pw * 0.55f, y + ph * 0.4f), 150f, _cutinCol, 0.16f * glow * _calloutA);

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
            // 袖が右になったのでセリフは絵の「左」へ。右端から絵の幅ぶん＋文字幅ぶん左に置き、
            // 盤面の左端（Field.DLeft）より内側には出さない＝サイドパネルへ掛けない。
            float lw = UiKit.TextW(UiKit.ZenBold, _cutinLine, UiKit.FontTitle);
            float sx = Mathf.Max(Field.DLeft + 30f, x + pw * 0.18f - lw);
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

    private void DrawFocusChip(HudCanvas ci)
    {
        Color accent = _focusOn ? AccountAccent : (_focusReady ? SideTeal : SideMuted);
        string status = _focusOn ? "発動中" : _focusReady ? "READY" : "充填中";
        float x = PanelX, y = RowFocus, w = PanelInnerW;
        KeyBadge(ci, new Vector2(x, y), TokFocus, accent, sidebar: true);
        UiKit.DrawRight(ci, UiKit.SmallLabel, x + w, y + 2f, status, accent);
        float barY = y + 34f, barH = 4f;
        UiKit.Box(ci, new Rect2(x, barY, w, barH), SideRule, 2f);
        if (_focusRatio > 0)
            UiKit.Box(ci, new Rect2(x, barY, w * _focusRatio, barH), accent, 2f);
    }

    private void DrawPowerups(HudCanvas ci)
    {
        if (GetTree().GetFirstNodeInGroup("player") is not Player player) return;
        for (int i = 0; i < 4; i++)
        {
            var kind = (PowerKind)i;
            int level = player.PowerLevel(kind);
            if (level == 0) continue;
            float x = PanelX + i * (PanelInnerW / 4f);
            PowerPickupArt.Draw(ci, new Rect2(x, 119f, 25f, 25f), kind);
            for (int pip = 0; pip < Player.PowerLevelCap; pip++)
                ci.DrawRect(new Rect2(x + 32f + pip * 10f, 130f, 6f, 10f),
                    pip < level ? PowerPickupArt.ColorFor(kind) : SideRule);
        }
    }

    private void DrawShotMode(HudCanvas ci)
    {
        var job = _game.JobDef;
        string handle = job.Id == Job.Tank ? Handles.Mina
            : System.Array.Find(GameManager.Stages, stage => stage.Id == job.CharacterId)!.Handle;
        UiKit.FaceAvatar(ci, new Vector2(PanelX + 34f, 80f), 33f, _accountFaces[job.Id], AccountAccent, false, 0f);
        float tx = PanelX + 86f;
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(tx, 57f), job.CharacterName, 24, SideInk);
        UiKit.Text(ci, UiKit.Mono, new Vector2(tx, 90f), handle, 12, SideMuted);
    }

    // モード切替トースト（画面中央上に短時間スウィープ＝Shot Upgrades の modeSweep 相当）。
    private void DrawShotModeToast(HudCanvas ci)
    {
        float a = Mathf.Clamp((float)(_shotModeToast / 0.45), 0f, 1f); // 終わり際にフェード
        string name = _game?.ShotModeName(_shotMode) ?? "連射";
        string t = "MODE ▸ " + name;
        float w = UiKit.TextW(UiKit.ZenBlack, t, UiKit.FontTitle) + 90;
        float x = Field.DCenterX - w / 2f, y = 150;   // 盤面の中心（左のサイドパネルへ掛けない）
        UiKit.Box(ci, new Rect2(x, y, w, 54f), new Color(0.06f, 0.10f, 0.14f, 0.9f * a), 15f, new Color(UiKit.Info, 0.6f * a), 1.4f);
        UiKit.Draw(ci, UiKit.PanelLabel, new Vector2(x + 22, y + 9), "MODE", new Color(UiKit.Info, a));
        UiKit.Text(ci, UiKit.ZenBlack, new Vector2(x, y + 13), name, UiKit.FontTitle, new Color(UiKit.PurifyHi, a), HorizontalAlignment.Center, w);
    }

    // チュートリアルの常駐指示帯（操作させる区間・下部中央）。会話バーより上、ティッカーの上に出す。
    // ミナ色のふちで「今やること」を一行で示す。会話と違い敵/自機を止めないのが肝。
    private void DrawTutorialHint(HudCanvas ci)
    {
        float pulse = 0.6f + 0.4f * Mathf.Sin((float)_t * 4f);
        float w = UiKit.TextW(UiKit.ZenBold, _tutorialHint, 15) + 56;
        float x = Field.DCenterX - w / 2f, y = 500, h = 38;   // 盤面の中心
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
            "dodge" => ("回避",       AllDodge, UiKit.Gold),
            "bomb"  => ("ボム",       AllBomb,  UiKit.Mina),
            _       => ("",           "",       UiKit.White),
        };
        if (info.label.Length == 0) return;

        const int labelSize = 15;
        float pulse = 0.6f + 0.4f * Mathf.Sin((float)_t * 4f);

        float labelW = UiKit.TextW(UiKit.ZenBold, info.label, labelSize);
        float badgeW = KeyBadgeW(info.tok);
        const float gap = 12f, padX = 16f, h = 30f;
        float contentW = labelW + gap + badgeW;
        float w = contentW + padX * 2f;
        float x = Field.DCenterX - w / 2f, y = 462f; // 盤面の中心・指示帯(y=500)の真上。会話ボックスより上で弾/セリフと干渉しにくい。

        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.88f), 10f,
            new Color(info.accent, 0.4f + 0.4f * pulse), 1.3f);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + padX, y + 7), info.label, labelSize, new Color(0.94f, 0.92f, 0.99f));
        // キーバッジ（KeyBadge と同寸・縦中央寄せ）。KB なら複数キーがトークン内に並ぶ。
        KeyBadge(ci, new Vector2(x + padX + labelW + gap, y + (h - KeyBadgeH) / 2f), info.tok, info.accent);
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
        Rect2 dlgBox = _dlgIsDialog ? new Rect2(DlgBoxX, 520, DlgBoxW, 170) : new Rect2(NarrBoxX, 590, NarrBoxW, 96);

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

    // 2026-09-16: 下部ティッカー（降ってくる言葉）は表示OFF（ユーザー指摘＝画面下部の帯を消す）。
    //   投稿弾（PostBullets）が同じ PostPool の“声”を降らせるので情報は失われない。
    //   コードは復活可能な形で残置（TickerEnabled のフラグだけで止める）。
    private static readonly bool TickerEnabled = false;
    private void DrawTicker(HudCanvas ci)
    {
        // 帯は盤面の中だけに敷く（サイドパネルの上を横切らせない）。左端＝盤面の左端。
        float barH = 38, y = 720 - barH;
        UiKit.VGradient(ci, new Rect2(Field.DLeft, y, Field.DWidth, barH),
            new[] { new Color(10 / 255f, 8 / 255f, 16 / 255f, 0f), new Color(10 / 255f, 8 / 255f, 16 / 255f, 0.82f) }, new[] { 0f, 1f });
        ci.DrawRect(new Rect2(Field.DLeft, y, Field.DWidth, 1f), new Color(UiKit.Kegare, 0.18f));
        // ラベル
        ci.DrawRect(new Rect2(Field.DLeft + 1f, y, 150, barH), new Color(UiKit.Kegare, 0.14f));
        UiKit.Draw(ci, UiKit.SmallLabel, new Vector2(Field.DLeft + 16, y + barH / 2f - 8), "降ってくる言葉", new Color("f0a8cf"));
        // スクロール
        float startX = Field.DLeft + 164, gap = 40;
        float block = 0f;
        foreach (var (h, wd) in TickerWords) block += TickerHandleW(h) + UiKit.TextW(UiKit.Zen, wd, 14) + gap;
        float scroll = ((float)_t * 70f) % block;
        float cx = startX - scroll + block; // 1ブロック先行
        // ───────── コメントの入退場演出（ログイン/ログ アウト風／SNSの接続・切断の手触り）─────────
        //   ティッカーは連続スクロールなので「右端で接続して入る／左ラベル際で切断して抜ける」を
        //   各セルの横位置から導く。入＝右端域でα0→1にポップ＋ハンドル頭に小さな接続ドットが点灯し
        //   外周リングが一拍広がる（ログイン）。出＝左ラベル際でα1→0へ薄れつつ僅かに上へスッと退く（ログアウト）。
        //   弾の視認は損なわない（下部ティッカー帯の中だけ・加算グローは極小・本数を増やさない）。
        const float bandL = Field.DLeft + 150f, bandR = Field.DRight; // 可視帯（左ラベル境界〜盤面右端）
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
                    if (hw > 0f) UiKit.Text(ci, UiKit.Mono, new Vector2(cx, th), h, UiKit.FontSmall, new Color(UiKit.Text3, alpha));
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
        => string.IsNullOrEmpty(h) ? 0f : UiKit.TextW(UiKit.Mono, h, UiKit.FontSmall) + 6f;

    public const float DraftMarkW = 74f;
    public const float DraftMarkH = 120f;
    private static Texture2D? _draftArt;
    public static void DrawDraftMark(CanvasItem ci, Vector2 leftCenter, Color col, double t = 0)
    {
        _draftArt ??= GD.Load<Texture2D>("res://char/ui/dialogue_you_v1.png");
        var size = new Vector2(DraftMarkW, DraftMarkW * _draftArt.GetHeight() / _draftArt.GetWidth());
        ci.DrawTextureRect(_draftArt, new Rect2(leftCenter + new Vector2(0, -size.Y / 2f), size), false);
    }

    private void DrawDialog(CanvasItem ci)   // CanvasItem＝HudCanvas（カットシーン・保険）と BubbleLayer（戦闘中）の両方から描ける
    {
        // 現在ページのテキストを、その表示済み文字数ぶんだけ描く（全ボックス 2行固定＝DlgMaxLines）。
        string page = CurPageText;
        int n = Mathf.Clamp(Mathf.FloorToInt(_dlgRevealed), 0, page.Length);
        var lines = new List<string>(page.Split('\n'));
        // ページ継続サイン：現在ページを出し切っていて、まだ後続ページがあるとき「▼」を点滅（Zで続きへ）。
        bool morePages = !OnLastPage && _dlgRevealed >= page.Length;

        if (CinematicMode)
        {
            if (_cinematicBubble)
            {
                var accent = _dlgSpeakerCol;
                var fill = new Color(0.035f, 0.04f, 0.055f, 0.98f);
                float tailX = _dlgKind == LineKind.Mina ? 640 : 160;
                var tail = new[] { new Vector2(tailX - 10, 531), new Vector2(tailX, 519), new Vector2(tailX + 10, 531) };
                ci.DrawColoredPolygon(tail, fill);
                ci.DrawPolyline(tail, new Color(accent, 0.55f), 1.2f, true);
                UiKit.Box(ci, new Rect2(112, 530, 1056, 166), fill, 8f, new Color(accent, 0.55f), 1.2f);
                if (_dlgPortrait != null)
                    UiKit.FaceAvatar(ci, new Vector2(160, 580), 32, _dlgPortrait, accent, false);
                else if (_dlgDraftMark)
                {
                    _draftArt ??= GD.Load<Texture2D>("res://char/ui/dialogue_you_v1.png");
                    var size = _draftArt.GetSize() * (56f / Mathf.Max(_draftArt.GetWidth(), _draftArt.GetHeight()));
                    ci.DrawTextureRect(_draftArt, new Rect2(new Vector2(160, 580) - size / 2, size), false);
                }
            }
            if (_dlgSpeaker.Length > 0)
                UiKit.Text(ci, UiKit.ZenBold, new Vector2(FilmTextX, 542), _dlgSpeaker, 21,
                    _cinematicBubble ? _dlgSpeakerCol : new Color(_dlgSpeakerCol, 0.96f));
            UiKit.TypewriterLines(ci, UiKit.Zen, lines,
                new Vector2(FilmTextX, 588 + UiKit.Zen.GetAscent(FilmBody.Size)), FilmWrapWidth,
                FilmBody.Size, new Color(0.965f, 0.97f, 0.99f), n, extraLeading: FilmBody.ExtraLeading);
            // 既読早送り中の表示は上辺のボタン列（SKIP の点灯）が担う＝旧「▶▶」チップは出さない（二重表示を避ける）。
            if (morePages && !FastForwarding) UiKit.Text(ci, UiKit.Zen, new Vector2(1136, 664), "▼", 14,
                _hasCinematicAccent ? new Color(_cinematicAccent, 0.9f) : Colors.White);
            return;
        }

        if (!_dlgIsDialog)
        {
            // ナレーション：中央寄せの淡いテロップ（バー無し）。行間を足して詰まりを解消。2行に統一。
            UiKit.Box(ci, new Rect2(NarrBoxX, 590, NarrBoxW, 96), new Color(0.04f, 0.03f, 0.07f, 0.7f), 12f);
            UiKit.TypewriterLines(ci, UiKit.Zen, lines,
                new Vector2(NarrBoxX + 40, 602 + UiKit.Zen.GetAscent(UiKit.FontBattle)), NarrWrapW,
                UiKit.FontBattle, new Color(0.9f, 0.9f, 0.95f), n, extraLeading: UiKit.BattleBody.ExtraLeading);
            if (morePages && ((int)(_t * 2f) % 2) == 0)
                UiKit.Text(ci, UiKit.ZenBold, new Vector2(NarrBoxX + NarrBoxW - 32, 590 + 96 - 26), "▼", UiKit.FontLabel, new Color(1f, 1f, 1f, 0.7f));
            return;
        }

        // シネマ下部バー（盤面の中に収める。板は覆わない）。座標は DlgBoxX/W に集約し、
        // 折り返し幅（DlgWrapW）とページ分割（BuildDialogPages）が同じ数字を見るようにする。
        float x = DlgBoxX, y = 520, w = DlgBoxW, h = 170;
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(0.05f, 0.04f, 0.09f, 0.95f), 16f, new Color(_dlgSpeakerCol, 0.5f), 1.4f);
        float textX = x + 36;
        // 立ち絵（あれば左に）。常時の微細な生命感：呼吸（上下揺れ）＋表情クロスフェード＋うなずき。
        // ここで描く立ち絵＝いま発話中の話者なので、揺れは「話者だけ」に自然に閉じる。
        if (_dlgPortrait == null && _dlgDraftMark)
        {
            // 「あなた」には顔が無い。立ち絵の代わりに下書き欄（入力欄）を置く
            //（＝画面に人が増えず、それでも誰が喋ったかの居場所は残る）。
            DrawDraftMark(ci, new Vector2(x + 10, y + h / 2f), _dlgSpeakerCol, _t);
            textX = x + 10 + DraftMarkW + 20;
        }
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
            UiKit.Draw(ci, UiKit.DialogSpeaker, new Vector2(textX, y + 16), _dlgSpeaker, _dlgSpeakerCol);
        // 本文：BattleBody（22px・行間1.5倍。2026-09-26 に 17px から拡大）。全ボックス 2行固定（DlgMaxLines）
        //   ＝はみ出し防止＋箇所ごとの行数差を解消。折り返し幅は BuildDialogPages と同じ式（DlgWrapW）。
        UiKit.TypewriterLines(ci, UiKit.Zen, lines,
            new Vector2(textX, y + 48 + UiKit.Zen.GetAscent(UiKit.FontBattle)), DlgWrapW(textX),
            UiKit.FontBattle, new Color(0.95f, 0.95f, 0.98f), n, extraLeading: UiKit.BattleBody.ExtraLeading);
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
        float x = Field.DCenterX - w / 2f, y = 600f;   // 盤面の中心（左のサイドパネルへ掛けない）
        UiKit.Box(ci, new Rect2(x, y, w, h), new Color(0.06f, 0.05f, 0.10f, 0.92f), 10f, new Color(UiKit.Info, 0.55f), 1.2f);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 18f, y + 8f), label, 14, UiKit.Text2);
        float bx = x + 18f + tw + gap, by = y + h / 2f - 3f;
        ci.DrawRect(new Rect2(bx, by, barW, 6f), new Color(1, 1, 1, 0.14f));
        ci.DrawRect(new Rect2(bx, by, barW * Mathf.Clamp(frac, 0f, 1f), 6f), UiKit.Info);
    }

    // 会話本文の折り返し幅。DrawDialog（描画）と BuildDialogPages（ページ分割）の両方から必ずこれを通す
    //   ＝幅がズレると「画面に出る行構成」と「ページの切れ目」が食い違い、送りで文字が飛ぶ/重なる。
    //   行間は UiKit.DialogBody.Leading(1.55) が持つ（旧 DlgLeading/NarrLeading の px 直指定は廃止）。
    //   会話バーとナレ箱の矩形。盤面（設計 x 400..1280）の中に収める＝サイドパネルを覆わない。
    //   ここを直せば描画・折り返し・ページ分割が同時に追随する（式が2か所にあるとページの切れ目がズレる）。
    public const float DlgBoxX = Field.DLeft + 20f;          // 420
    public const float DlgBoxW = Field.DWidth - 40f;         // 840
    private const float NarrBoxX = Field.DLeft + 60f;        // 460（ナレは会話バーより一段内側）
    private const float NarrBoxW = Field.DWidth - 120f;      // 760
    private const float NarrWrapW = NarrBoxW - 80f;          // ナレ本文（箱の内側・左右40pxずつ空ける）
    private static float DlgWrapW(float textX) => DlgBoxX + DlgBoxW - textX - 30f; // セリフ（バーの内側）

    private void DrawBossLine(CanvasItem ci)   // CanvasItem＝HudCanvas（保険）と BubbleLayer（通常）の両方から描ける
    {
        if (BubblePaused || CinematicMode) return;
        float enter = Ease((float)(_bossLineDuration - _bossLineTimer) / 0.2f);
        float a = Mathf.Clamp((float)_bossLineTimer / 0.3f, 0f, 1f) * enter * _calloutA;
        const float w = 736f;
        // 本文 24px（2026-09-26 に 20 から拡大）。行送り 31。
        var lines = UiKit.WrapLines(UiKit.ZenBold, _bossLine, 24, w - 44);
        float h = 43f + lines.Count * 31f;
        float x = Field.DCenterX - w / 2f + (1f - enter) * 18f, y = 605f - h;
        ci.DrawRect(new Rect2(x, y, w, h), new Color(0.04f, 0.055f, 0.07f, 0.86f * a));
        ci.DrawLine(new Vector2(x, y), new Vector2(x, y + h), new Color(_bossLineCol, a), 3f);
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 20, y + 9), _bossLineSpeaker, 15, new Color(_bossLineCol, a));
        if (_bossLineBreak)
            UiKit.Text(ci, UiKit.Mono, new Vector2(x + w - 177, y + 10), "SHIELD BREAK", 16, new Color("c7f4f1", a));
        for (int i = 0; i < lines.Count; i++)
            UiKit.Text(ci, UiKit.ZenBold, new Vector2(x + 20, y + 33 + i * 31), lines[i], 24, new Color("f5f8fa", a));
        float remaining = Mathf.Clamp((float)(_bossLineTimer / _bossLineDuration), 0f, 1f);
        ci.DrawLine(new Vector2(x, y + h), new Vector2(x + w, y + h), new Color(_bossLineCol, 0.15f * a), 1f);
        ci.DrawLine(new Vector2(x, y + h), new Vector2(x + w * remaining, y + h), new Color(_bossLineCol, 0.65f * a), 1f);
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

    private void DrawStageStart(HudCanvas ci)
    {
        if (BubblePaused || _gameOverTitle.Length > 0) return;
        float t = (float)(StageStartDur - _bannerTimer);
        float arrive = Ease(t / 0.32f);
        float leave = Mathf.SmoothStep(1.55f, (float)StageStartDur, t);
        float alpha = arrive * (1f - leave);
        float cx = Field.DCenterX;
        float y = 252f - leave * 14f;
        float half = 254f * arrive;
        var ink = new Color("151b23");
        var white = new Color("f5fcff");

        ci.DrawColoredPolygon(new Vector2[]
        {
            new(cx - half - 28, y - 39), new(cx + half + 28, y - 59),
            new(cx + half - 12, y + 44), new(cx - half - 50, y + 64),
        }, new Color(ink, 0.42f * alpha));
        ci.DrawLine(new Vector2(cx - half - 22, y + 63), new Vector2(cx + half + 10, y + 43),
            new Color(_startAccent, 0.78f * alpha), 1.5f, true);
        ci.DrawLine(new Vector2(cx - half + 10, y - 61), new Vector2(cx + half - 18, y - 61),
            new Color(_startAccent, 0.34f * alpha), 1f, true);

        float metaAlpha = Ease((t - 0.18f) / 0.3f) * (1f - leave);
        float metaY = y - 101f;
        string stage = $"STAGE {_startStage:00}";
        UiKit.Text(ci, UiKit.Mono, new Vector2(cx - 165, metaY), stage, 22,
            new Color(_startAccent, metaAlpha));
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(cx + 164 - UiKit.TextW(UiKit.ZenBold, _startName, 20), metaY),
            _startName, 20, new Color(white, metaAlpha));

        const string word = "START";
        const int size = 78;
        float width = UiKit.TextW(UiKit.ZenBlack, word, size);
        float x = -width / 2f;
        for (int i = 0; i < word.Length; i++)
        {
            string letter = word[i].ToString();
            float local = t - 0.08f - i * 0.035f;
            float settle = Ease(local / 0.26f);
            float fade = Mathf.Clamp(local / 0.09f, 0f, 1f) * (1f - leave);
            float trail = 1f - settle;
            var position = new Vector2(cx + x + trail * 42f - leave * 18f, y + trail * 12f);
            // Compose with the HUD design transform so the lettering stays inside the playfield at any window size.
            ci.DrawSetTransformMatrix(new Transform2D(new Vector2(UiKit.Scale, 0),
                new Vector2(-0.16f * UiKit.Scale, UiKit.Scale), position * UiKit.Scale));
            var baseline = new Vector2(0, (UiKit.ZenBlack.GetAscent(size) - UiKit.ZenBlack.GetDescent(size)) * 0.5f);
            if (trail > 0.01f)
                ci.DrawStringOutline(UiKit.ZenBlack, baseline + new Vector2(16f * trail, 0), letter,
                    fontSize: size, size: 1, modulate: new Color(_startAccent, 0.38f * fade * trail));
            ci.DrawStringOutline(UiKit.ZenBlack, baseline + new Vector2(2, 4), letter,
                fontSize: size, size: 5, modulate: new Color(ink, 0.9f * fade));
            ci.DrawStringOutline(UiKit.ZenBlack, baseline, letter,
                fontSize: size, size: 2, modulate: new Color(_startAccent, 0.9f * fade));
            ci.DrawString(UiKit.ZenBlack, baseline, letter, fontSize: size, modulate: new Color(white, fade));
            x += UiKit.TextW(UiKit.ZenBlack, letter, size);
        }
        UiKit.BeginDesign(ci);

        float sweep = Mathf.Clamp((t - 0.1f) / 0.48f, 0f, 1f);
        float flare = Mathf.Sin(sweep * Mathf.Pi) * (1f - leave);
        float sweepX = Mathf.Lerp(cx - 280, cx + 280, sweep);
        if (flare > 0.01f)
        {
            ci.DrawLine(new Vector2(sweepX - 46, y + 60), new Vector2(sweepX + 10, y + 58),
                new Color(_startAccent, flare * 0.3f), 7f, true);
            ci.DrawLine(new Vector2(sweepX - 28, y + 60), new Vector2(sweepX + 10, y + 58),
                new Color(white, flare), 1.6f, true);
        }
        for (int i = 0; i < 3; i++)
        {
            float dx = 208f + i * 12f + leave * 25f;
            float a = (0.65f - i * 0.16f) * alpha;
            ci.DrawLine(new Vector2(cx - dx - 9, y + 15), new Vector2(cx - dx + 3, y - 15),
                new Color(_startAccent, a), 2f, true);
            ci.DrawLine(new Vector2(cx + dx - 3, y + 15), new Vector2(cx + dx + 9, y - 15),
                new Color(_startAccent, a), 2f, true);
        }
    }

    private void DrawClearBanner(HudCanvas ci)
    {
        float t = 5f - (float)_bannerTimer;
        float enter = Ease(t / 0.45f);
        float a = enter * Mathf.Clamp((float)_bannerTimer / 0.7f, 0, 1);
        float cx = Field.DCenterX, y = 292f + (1f - enter) * 22f;
        var accent = new Color("94e5da");
        var white = new Color("f5fcff");
        ci.DrawRect(new Rect2(Field.DLeft, y - 91, Field.DWidth, 272), new Color(0.04f, 0.065f, 0.08f, a * 0.82f));
        ci.DrawLine(new Vector2(cx - 302 * enter, y - 91), new Vector2(cx + 302 * enter, y - 91), new Color(accent, a * 0.65f), 1f);
        ci.DrawLine(new Vector2(cx - 302 * enter, y + 181), new Vector2(cx + 302 * enter, y + 181), new Color(accent, a * 0.4f), 1f);
        UiKit.Text(ci, UiKit.Mono, new Vector2(cx - 280, y - 68), _bannerText, 18, new Color(accent, a));
        const string word = "CLEAR";
        float width = UiKit.TextW(UiKit.ZenBlack, word, 74);
        ci.DrawSetTransformMatrix(new Transform2D(new Vector2(UiKit.Scale, 0),
            new Vector2(-0.16f * UiKit.Scale, UiKit.Scale), new Vector2(cx - width / 2, y + 17) * UiKit.Scale));
        ci.DrawStringOutline(UiKit.ZenBlack, new Vector2(2, 4), word, fontSize: 74, size: 4, modulate: new Color(accent, a * 0.24f));
        ci.DrawString(UiKit.ZenBlack, Vector2.Zero, word, fontSize: 74, modulate: new Color(white, a));
        UiKit.BeginDesign(ci);
        float rowA = Ease((t - 0.2f) / 0.4f) * a;
        for (int i = 0; i < 2; i++)
        {
            float x = cx - 280 + i * 300;
            string value = i == 0 ? _bannerTime : _bannerScore;
            string best = i == 0 ? _bannerBest : _bannerScoreBest;
            bool isBest = i == 0 ? _bannerNewBest : _bannerScoreNewBest;
            int size = 24;
            while (UiKit.TextW(UiKit.Mono, value, size) > 278 && size > 14) size--;
            UiKit.Text(ci, UiKit.Mono, new Vector2(x, y + 67), value, size, new Color(white, rowA));
            UiKit.Text(ci, UiKit.Mono, new Vector2(x, y + 112), best, 16, new Color(isBest ? accent : UiKit.Text2, rowA));
        }
        ci.DrawLine(new Vector2(cx, y + 70), new Vector2(cx, y + 135), new Color(white, 0.15f * rowA), 1f);
        if ((_game?.ReplayMul ?? 1f) < 1f)
        {
            string note = $"周回逓減 ×{_game!.ReplayMul:0.0}（連続{_game.RepeatStreak + 1}回目・別ステージ/難度アップでリセット）";
            UiKit.Text(ci, UiKit.ZenBold, new Vector2(Field.DLeft, y + 198), note, UiKit.FontSmall,
                new Color(UiKit.Text2, a), HorizontalAlignment.Center, Field.DWidth);
        }
    }

    private void DrawBanner(HudCanvas ci)
    {
        if (_startStage > 0) { DrawStageStart(ci); return; }
        if (_epic) { DrawEpicBanner(ci); return; }
        float a = Mathf.Clamp((float)_bannerTimer, 0f, 1f);
        if (_bannerRewardLife || _bannerRewardBomb) { DrawRewardBanner(ci); return; }
        if (_bannerTime.Length > 0) { DrawClearBanner(ci); return; }
        float w = UiKit.TextW(UiKit.ZenBlack, _bannerText, UiKit.FontDisplay);
        UiKit.Text(ci, UiKit.ZenBlack, new Vector2(Field.DCenterX - w / 2f, 300), _bannerText,
            UiKit.FontDisplay, new Color(UiKit.White, a));
    }

    private void DrawRewardBanner(HudCanvas ci)
    {
        const float Width = 196f, Gap = 18f, Icon = 48f;
        int count = (_bannerRewardLife ? 1 : 0) + (_bannerRewardBomb ? 1 : 0);
        float t = (float)(RewardBannerDur - _bannerTimer);
        float leave = 1f - Mathf.Clamp((float)_bannerTimer / 0.4f, 0f, 1f);
        float total = count * Width + (count - 1) * Gap;
        var ink = new Color("171c23");
        var white = new Color("f6fcff");
        for (int i = 0; i < count; i++)
        {
            bool life = _bannerRewardLife && i == 0;
            var tex = life ? _lifeMarks[_game.SelectedJob] : _bombMark;
            var accent = life ? SideRose : SideTeal;
            float localT = Mathf.Max(0f, t - i * 0.08f);
            float enter = Ease(localT / 0.24f);
            float a = enter * (1f - leave);
            float pop = Mathf.Sin(Mathf.Clamp(localT / 0.38f, 0f, 1f) * Mathf.Pi);
            var origin = new Vector2(Field.DCenterX - total / 2f + i * (Width + Gap)
                - 20f * (1f - enter), 288f + 12f * (1f - enter) - leave * 16f);
            ci.DrawSetTransform(origin * UiKit.Scale, 0f, Vector2.One * UiKit.Scale);

            ci.DrawColoredPolygon(new[] { new Vector2(12, 0), new Vector2(Width, 0),
                new Vector2(Width - 12, 92), new Vector2(0, 92) }, new Color(ink, a * 0.78f));
            ci.DrawLine(new Vector2(14, 0), new Vector2(Width * enter, 0), new Color(accent, a * 0.4f), 1f, true);
            ci.DrawLine(new Vector2(0, 92), new Vector2((Width - 12) * enter, 92), new Color(accent, a * 0.9f), 2f, true);
            UiKit.Text(ci, UiKit.Mono, new Vector2(22, 8), life ? "LIFE UP" : "BOMB UP", 13, new Color(accent, a));

            var size = tex.GetSize();
            size *= Icon * (1f + pop * 0.12f) / Mathf.Max(size.X, size.Y);
            ci.DrawTextureRect(tex, new Rect2(new Vector2(46, 58) - size / 2f, size), false, new Color(1, 1, 1, a));

            float scale = UiKit.Scale * (1f + pop * 0.1f);
            ci.DrawSetTransformMatrix(new Transform2D(new Vector2(scale, 0), new Vector2(-0.12f * scale, scale),
                (origin + new Vector2(90, 76)) * UiKit.Scale));
            ci.DrawStringOutline(UiKit.ZenBlack, new Vector2(-8f * (1f - enter), 0), "+1",
                fontSize: 52, size: 7, modulate: new Color(accent, a * (0.08f + pop * 0.2f)));
            ci.DrawStringOutline(UiKit.ZenBlack, new Vector2(0, 2), "+1",
                fontSize: 52, size: 4, modulate: new Color(ink, a));
            ci.DrawStringOutline(UiKit.ZenBlack, Vector2.Zero, "+1",
                fontSize: 52, size: 1, modulate: new Color(accent, a));
            ci.DrawString(UiKit.ZenBlack, Vector2.Zero, "+1", fontSize: 52, modulate: new Color(white, a));

            ci.DrawSetTransform(origin * UiKit.Scale, 0f, Vector2.One * UiKit.Scale);
            float sweep = Mathf.Clamp((localT - 0.08f) / 0.48f, 0f, 1f);
            float shine = Mathf.Sin(sweep * Mathf.Pi) * a;
            float sx = 18f + (Width - 54f) * sweep;
            ci.DrawLine(new Vector2(sx - 10, 92), new Vector2(sx + 20, 92), new Color(accent, shine * 0.3f), 7f, true);
            ci.DrawLine(new Vector2(sx, 92), new Vector2(sx + 20, 92), new Color(white, shine), 2f, true);
            ci.DrawLine(new Vector2(Width - 12, 25), new Vector2(Width - 6, 13), new Color(accent, a * 0.8f), 2f, true);
        }
        UiKit.BeginDesign(ci);
    }

    // ゲームオーバー時のキー案内。2026-09-07 に主役は ChoiceOverlay（縦積みの選択）へ移り、
    // ここは「R／Shift+R／Q を覚えている人向けの控えめな添え書き」になった。
    //   位置: 3択の最下行（y=375〜）と ChoiceOverlay の操作ヒント（y=464）の下＝y=496。
    //         旧位置 y=372 は選択肢の3行目と真上から重なる。
    //   大きさ: FontHeading → FontSmall、αも落として選択肢より一段引く。
    private void DrawGameOverPrompt(HudCanvas ci)
    {
        UiKit.Text(ci, UiKit.ZenBold, new Vector2(Field.DLeft, 496), _gameOverPrompt, UiKit.FontSmall,
            new Color(UiKit.Text3, 0.75f), HorizontalAlignment.Center, Field.DWidth);
    }

    // ゲームオーバーの見出し（選択肢の上・y=150）。3択の最上行 y=195 より上＝重ならない。
    // 旧 ShowBanner（y=300・FontDisplay）の代わり。大きさは一段落として選択肢に主役を譲る。
    private void DrawGameOverTitle(HudCanvas ci)
    {
        UiKit.Text(ci, UiKit.ZenBlack, new Vector2(Field.DLeft, 150), _gameOverTitle, UiKit.FontTitle,
            new Color(UiKit.Light, 0.92f), HorizontalAlignment.Center, Field.DWidth);
    }
}

// HUD 描画用ノード（Hud にぶら下げ、Hud.DrawAll を呼ぶだけ）。
public partial class HudCanvas : Node2D
{
    public Hud Hud = null!;
    public override void _Draw() => Hud?.DrawAll(this);
}

// HUD の加算ブレンド層（Hud にぶら下げ、CanvasItemMaterial{Add} 付き・2026-09-16）。
// 被弾のライフ砕け散りなど「光る」演出だけをここへ描く（通常層 HudCanvas と分離）。
public partial class HudAddCanvas : Node2D
{
    public Hud Hud = null!;
    public override void _Draw() => Hud?.DrawAdditive(this);
}
