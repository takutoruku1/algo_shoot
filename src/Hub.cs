using Godot;
using System.Linq;

// Hub : タイムラインハブ（ステージ間の中枢）。RefrainHTML のデザイン言語で非ピクセル化。
//   - ヘッダ：使用中のアカウント（アバター/名前/フォロワー/インプレ/汚染）。
//   - 角丸ガラスの投稿カード（声/届いた/限界 のピル）を ↑↓ で選び、カード下部の [ダイブ] [返信] を ←→ で選んで Z
//     （ダイブは難易度選択を挟む）。操作は「矢印で選ぶ・Z で決める・Esc／X でもどる」だけ（2026-09-27。項目ごとの
//     文字キー J／C／T／R は廃止）。操作の案内は画面下端のヒント帯（UiKit.HintBar）1行にまとめる。
//     カードは下から滑り込む流入アニメ（帰還投稿後は「タイムライン更新」として再流入）。
//     クリア済カードにはミナの自動投稿がスレッド返信風にぶら下がる。
//   - クリア帰還でミナの会話＋自動投稿。クリア済カードの [返信]：コメント返信（1回）。
//   - 全クリアで FINAL カード（枠とピルは穢れ色・フォロワー/インプレの生値を語ラベル付きで出す）。autoplay は会話を自動送り→自動ダイブ。
public partial class Hub : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;
    private const float PhoneW = 480f, PhoneX = (W - PhoneW) / 2f;
    private static readonly Color PhoneBg = new("14171d");
    private static readonly Color PhoneRaised = new("1d2229");

    private sealed record VoiceReply(string AuthorId, string[] Lines, float Bottom);
    private struct Entry
    {
        public bool IsFinal, IsRepost;
        public string Id, Scene, Name, Handle, Tweet, Initial;
        public bool Unlocked, Cleared;
        public SnsVoices.Reach Reach;
        public VoiceReply[]? VoiceReplies;
        public readonly int Replies => Social?.Replies.Length ?? VoiceReplies?.Length ?? 0;
        public Kind Sort;
        public string RelT;       // 相対時刻（Filler は生成規則で散らす。三人は従来の RelTime）
        public SnsVoices.Post? Social;
        public int Icon;          // 埋め草のアイコン番号（1..SnsVoices.IconCount。0＝アイコンを持たない＝三人とミナ）
    }
    private enum Kind { Voice, Filler, Pinned, Personal }
    private Entry[] _entries = System.Array.Empty<Entry>();
    private bool IsReturnPostUnlocked(SnsVoices.Post post) => post.CharacterId == "mina"
        ? _previewState is "all" or "final" || (_previewState == null && _game.IsFinalCleared && _game.EndingRollSeen)
        : post.CharacterId.Length > 0 && IsClearedForDisplay(post.CharacterId);
    private bool IsUnreadReturnPost(SnsVoices.Post post) => IsReturnPostUnlocked(post)
        && !_game.IsIdleDialogSeen(SnsVoices.ReturnPostReadKey(post));
    private int UnreadReturnPostCount => System.Linq.Enumerable.Count(SnsVoices.ReturnPosts, IsUnreadReturnPost);

    // カードが「潜れる声」か（＝Z で投稿詳細が開き、そこから難易度を選んで潜れる）。
    private bool IsVoice(int i) => i >= 0 && i < _entries.Length && _entries[i].Sort == Kind.Voice
        && _entries[i].Unlocked;
    private static bool IsNextKoharuPost(Entry e) => e.Sort == Kind.Voice && e.Id == "koharu" && !e.Cleared && !e.IsRepost;

    // ───────── 見せない／見せる のゲート（2026-09-07）─────────
    //   「まだ手に入れていないものは画面に出さない」を1か所に集める。判定は既存のクリア記録
    //   （GameManager の _cleared＝IsStageCleared）だけを見る＝新しい永続項目は足さない。
    //
    //   ・強化（ショップ）… 最初の面＝あかりのボスを倒すまで入れない。フッタの「X 強化」も出さない。
    //   ・記録　　　　　　… どれか一面をクリアするまで開けない。フッタの「T 記録」も出さない。
    //   未解禁のあいだは押せる導線ごと消す＝「押せないものを見せない」。
    //   判定は IsClearedForDisplay 経由＝デバッグプレビュー（--hub-preview）でも解禁状態が表示と揃う
    //   （スクショで「名前は伏せているのにフッタだけ解禁済み」といった嘘が出ない）。
    private bool ShopUnlocked => IsClearedForDisplay(GameManager.FirstStageId);

    private bool RecordsUnlocked
    {
        get
        {
            foreach (var s in GameManager.Stages) if (IsClearedForDisplay(s.Id)) return true;
            return false;
        }
    }

    public const string FinalTweet = "まだ、きこえる。ここを閉じたら、この声はどこへ行くの。";
    private const string FinalScene = "res://MinaBattle.tscn";

    // ───────── 3-1: 潜り方（難易度）の4段 ─────────
    // 数値・実装は DiffSelect のまま（GameManager.Diff / BaseLivesFor / BaseBombsFor / DiffBarBonus）。
    //   2026-09-17 ユーザー指示：段の表示名を情緒的な和名（浅く／いつも通り／深く／底まで）から
    //   一般的な難易度名へ変更した。「どれを選べば何が起きるか」が一目で分かることを優先する。
    //   同じ指示でミナの一言（Quip）も削除＝段は「名前・倍率・板の枚数」だけの素直な一覧になった。
    private struct Tier { public string Name; public GameManager.Diff Diff; }
    private static readonly Tier[] Tiers =
    {
        new() { Name = "EASY",    Diff = GameManager.Diff.Easy },
        new() { Name = "NORMAL",  Diff = GameManager.Diff.Normal },
        new() { Name = "HARD",    Diff = GameManager.Diff.Hard },
        new() { Name = "LUNATIC", Diff = GameManager.Diff.Lunatic },
    };
    private bool TierOpen(int i) => i >= 0 && i < Tiers.Length
        && (Tiers[i].Diff != GameManager.Diff.Lunatic || (_game?.IsLunaticUnlocked ?? false));

    // カード/ヘッダの顔アバター用テクスチャ（毎フレームLoadせずキャッシュ）。
    private readonly System.Collections.Generic.Dictionary<string, Texture2D?> _faces = new();
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _playerFaces = new();
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _dialogueFaces = new();
    private Texture2D? _minaFace;
    private PhoneVisuals _phoneVisuals = null!;
    private CanvasItem _drawCanvas = null!;
    private Node2D _phoneMask = null!, _phoneScreen = null!, _overlay = null!;
    private SnsPostMedia _snsMedia = null!;
    private bool _mediaHeld;
    private readonly bool[] _phoneAppUnlocked = new bool[HomeAppCount];
    private readonly float[] _phoneAppActivation = new float[HomeAppCount];
    private FontFile _sideNameFont = null!, _sideTitleFont = null!;
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _sidePortraits = new();
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _sideSkies = new();
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _sideScenery = new();
    private string _sideStoryId = "", _sideStoryPrevious = "";
    private float _sideStoryBlend = 1;
    private VoiceMedallions _voiceMedallions = null!;
    private bool FinalClearedForDisplay => _previewState == "final" || (_previewState == null && _game.IsFinalCleared);
    private bool FinalPostAvailable => _previewState == null ? _game.AllStoryCleared : _previewState is "all" or "final";
    private bool MinaCompanionOffline => _game.SelectedJob == Job.Tank && FinalPostAvailable && !FinalClearedForDisplay
        && (!_minaCall || _dlgIdx == _minaDisconnectIndex);
    private int VoiceMedallionCount => GameManager.Stages.Length + (FinalPostAvailable ? 1 : 0);
    private static string VoiceStageId(int index) => index < GameManager.Stages.Length
        ? GameManager.Stages[index].Id : GameManager.FinalStageId;
    private Vector2 VoiceMedallionCenter(int index) => new(
        FinalPostAvailable ? 935 + index * 96 : 953 + index * 110, 522);
    private bool VoiceMedallionsInteractive => !_autoplay && _mode is Mode.Cards or Mode.Photos;
    private static readonly Rect2 CompanionArea = new(0, 0, PhoneX - 1, H);
    private static readonly Rect2 StoryArea = new(PhoneX + PhoneW + 1, 0, W - PhoneX - PhoneW - 1, H);
    // 埋め草アカウントのアイコン（添字＝SnsVoices の Icon 番号。1..IconCount。[0] は未使用）。
    private Texture2D?[] _mobIcons = System.Array.Empty<Texture2D?>();

    private int _sel;
    private bool _navHeld, _zHeld, _xHeld, _dived;
    // SNS 画面でキーボード／パッドのカーソルがフッタに降りているとき、その項目番号（-1＝カード側）。
    //   2026-09-27 作者指摘「キーボード操作でアカウントを開くを選択できない」：フッタはマウス専用で、
    //   キー導線の J／RB は画面のどこにも表示されていなかった（FooterItems の key を DrawFooter が捨てていた）。
    //   → 最後のカードで ↓ でフッタへ降り、←→で項目を選んで Z で押せるようにした。↑でカードへ戻る。
    //   同日の作者指摘「J とか C とか X とか書いてあるけど操作性悪すぎでしょ」で J／RB の直行は廃止し、
    //   カード上の ←→ はカード下部のボタン切り替え（_cardBtn）に回した＝フッタへ降りるのは「最後のカードで ↓」だけ。
    private int _footSel = -1;
    // SNS 画面でキーボード／パッドのカーソルがヘッダー（使用中のアカウント）に上がっているか。
    //   2026-09-27 作者指摘「上のアカウントがキーボードだと選べないよ」：ヘッダーはクリックで開くだけで、
    //   キーのカーソルが乗らなかった。→ 先頭のカードで ↑ でヘッダーへ上がり、Z でアカウント切り替え
    //   （フッタ「アカウント」と同じ OpenJob）、↓ で先頭のカードへ戻る。以前の「先頭で ↑＝最後のカードへ回る」はやめた。
    private bool _headFocus;
    // キャラ切り替えボタン（左パネルの左下・スマホの左隣）のキー Tab／パッド Y の押しっぱなし（エッジ判定用）。
    private bool _tabHeld;
    // 選択中の声のカードの下部ボタン（0＝ダイブ／1＝返信）。←→ で切り替え、Z で実行。
    //   返信が使えない（CanReplySel が偽）ときは返信を薄く描き、カーソルは乗せない。
    //   カードの選択が変わったら必ずダイブへ戻す（_cardBtnFor で検出）。
    private int _cardBtn, _cardBtnFor = -1;
    private double _t, _cardsEnteredT;
    private float _selT; // 選択補間 0→1（0.12s で寄る・(B)手触り）
    private int _selAnim = -1; // 補間中の選択インデックス（_sel 変化で 0 にリセット）

    // デバッグ限定プレビュー：--hub-preview <all|final|lock> で表示状態だけ上書き（本番セーブ非汚染）。
    private string? _previewState;
    // デバッグ限定：--hub-detail で投稿詳細（カード展開）を開いた状態から始める（スクショ用）。
    private bool _openDetail;
    // デバッグ限定：--hub-toast で炎上トーストを出した状態から始める（スクショ用。セーブは触らない）。
    private bool _previewToast;
    // デバッグ限定：--hub-job でジョブ選択を開いた状態から始める（スクショ用。開くだけで何も確定しない）。
    private bool _openJob;
    // デバッグ限定：--hub-photos で写真アプリを開いた状態から始める（スクショ用。セーブは触らない）。
    private bool _openPhotos;
    // デバッグ限定：--hub-shopnudge で「説明を読み切った直後」のホーム誘導を再現する
    //   （スクショ／見え方の確認用。セーブは触らない＝ShopTutorialSeen も書き換えない）。
    private bool _previewShopNudge;
    // デバッグ限定：--hub-accountnudge でフッタ「アカウント」の誘導リングを撮る（--hub-preview と併用。セーブは触らない）。
    private bool _previewAccountNudge;
    // アカウント追加の説明を読み切った回だけ、SNS のフッタ「アカウント」を脈動させる（_shopNudge と同じ作法）。
    //   フラグは GameManager のランタイム限り＝帰還会話→説明→ショップ往復をまたいで残り、
    //   OpenJob（押す／切替UIを開く）で降りる。once キーで説明が二度と出ないので再発もしない。
    private bool AccountNudge => !_autoplay && (_previewAccountNudge || (_game?.AccountNudgePending ?? false));

    // Detail＝2-b の投稿詳細（カードがその場で開く）。本文／消された行の伏字／ミナの一言／潜り方（難易度）を
    //   1枚に置き、旧 DiffSelect.tscn への遷移をここへ吸収した（難易度の数値・実装は不変）。
    // Job＝ジョブ選択（設計書 §6・ハブのフッタから開くオーバーレイ）。Detail と同じ「カードがその場で開く」
    //   作法で 4 ジョブを縦に並べ、Z で確定・X でとじる。ハブに居る＝ラン外なので、いつでも選び直せる
    //   （ラン中＝ステージのシーンには、ジョブを書き換える導線が一つも無い＝「選んだらそのランは変えられない」）。
    private enum Mode { Home, HomeReveal, SnsOpening, Cards, Dialogue, Detail, Job, Photos, SocialThread, LimeFriends, LimeHistory, LimeDirect, MinaUnanswered }
    private Mode _mode = Mode.Home;
    private int _homeSel;
    private int _photoSel;
    private double _photoT;
    private float _photoScroll, _photoScrollTarget;
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _photoTextures = new();
    private bool _idleTalkPending;
    private const string HomeRevealSeenKey = "once_phone_home";
    private const string SnsIntroSeenKey = "once_sns_intro";
    // アカウント追加の説明（2026-09-23 ユーザー要望「アカウントが追加されたって説明が入るようにして」）。
    //   あかり初回の帰還会話に差し込む3行の once キー。読み切ると立ち、二度と流れない。
    private const string AccountIntroSeenKey = "once_account_intro";
    private const double SnsOpenDuration = 0.65;
    private double _snsOpeningT;
    private bool NeedsSnsIntro => !StageTutorial.Skipped(_game) && _game.HeartsSaved == 0 && !_game.IsIdleDialogSeen(SnsIntroSeenKey);
    private static readonly (string, string)[] SnsIntro =
    {
        ("あなた", "あかりさんの投稿、見えた？　「Y」に観測のしるしをつけたよ。"),
        ("ミナ", "はい。……あ、雨の音。さっきの声、まだあそこにいます。"),
        ("あなた", "通話が終わったら、その投稿を選んで。僕がフェイクレルムへ道を開く。あかりさんが本音を隠した世界だよ。"),
        ("ミナ", "……あの人と戦うんですか？　わたくし、声を聞きに来たのに。"),
        ("あなた", "戦う相手は、あの人の声を閉じ込めているアンチャーだよ。僕の声は回線を結んだ仲間にしか届かない。ミナから、声をかけてあげて。"),
        ("ミナ", "ああ、よかった。……最初に何て言おう。「雨、冷たかったですね」って、変でしょうか。"),
        ("あなた", "僕なら、気づいてくれたんだって思うよ。"),
        ("ミナ", "じゃあ、そう言います。行ってまいります、ご主人様。"),
    };
    private bool _homeRevealPending;
    private double _homeRevealT;
    private Texture2D? _homeSnapshot;
    private Rect2 _homeSnapshotRegion;
    // 強化ショップの説明を読み切った直後だけ立つ（2026-09-22）。ホームの強化アイコンを
    //   脈動させ、下に「タップして ひらく」の小さな指示を出す＝押すのはプレイヤー自身。
    //   一度でも開けば（OpenHomeApp）降りる＝用が済んだら普通のホームに戻る。
    //   立てるのは EndDialogue（説明の読了）＝同じ画面の続きで、シーンをまたがない（2026-10-03）。
    private bool _shopNudge;
    private double _detailT;      // 開いてからの経過（展開アニメと入力ゲート）
    private int _tierSel;         // 潜り方（難易度）の段。既定は前回の難易度＝Z 二押しでそのまま潜れる
    private double _jobT;         // ジョブ選択を開いてからの経過（展開アニメと入力ゲート。_detailT と同じ役）
    private int _jobSel;          // ジョブ選択のカーソル（開いたときに現在のジョブへ置く）
    private JobTuning[] _jobChoices = System.Array.Empty<JobTuning>();
    private Mode _jobReturnMode = Mode.Cards;
    private VideoStreamPlayer? _unlockMovie;
    private Job? _unlockJob;
    private Texture2D? _unlockPortrait;
    private Vector2? _unlockHoverOrigin;
    private (string sp, string tx)[] _dlg = System.Array.Empty<(string, string)>();
    private int _dlgIdx;
    private double _dlgLineT;
    private double _dlgReveal;     // タイプライター表示済み文字数（＝現在ページ内）
    private DialogueBox.TypeCursor _dlgTypeCursor;   // 送り音（1文字ごとのピッ）をどこまで鳴らしたか
    private string? _dlgReplyId;
    private string? _dlgSeenKey;

    // テキストボックスは2行固定。2行超の行はページに割り、送り（Z）で続きを読ませる（本文は削らない）。
    private readonly System.Collections.Generic.List<string> _dlgPages = new();
    private int _dlgPage;
    private DialoguePacing.Page[] _dlgPacing = System.Array.Empty<DialoguePacing.Page>();
    private DialoguePacing.Page? DlgPacing => _dlgPage < _dlgPacing.Length ? _dlgPacing[_dlgPage] : null;
    private bool DlgPageReady => _dlgReveal >= DlgCurPage.Length;
    private int _dlgPagedIdx = -1;                 // _dlgPages を構築済みの行 index
    private static float DlgBodyWrapW => DialogueBox.WrapWidth(DialogBox);
    private string DlgCurPage => _dlgPages.Count > 0 ? _dlgPages[Mathf.Min(_dlgPage, _dlgPages.Count - 1)] : "";
    private bool DlgLastPage => _dlgPages.Count == 0 || _dlgPage >= _dlgPages.Count - 1;
    private void DlgEnsurePages()
    {
        if (_dlgPagedIdx == _dlgIdx || _dlg.Length == 0 || _dlgIdx >= _dlg.Length) return;
        _dlgPagedIdx = _dlgIdx; _dlgPage = 0;
        _dlgPages.Clear();
        if (_minaCall)
            _dlgPages.AddRange(UiKit.Paginate(UiKit.Zen, _dlg[_dlgIdx].tx, 22, PhoneW - 84, 3));
        else if (_limeConversation)
        {
            _limeCurrent = new LimeBubble(_dlg[_dlgIdx].sp, _dlg[_dlgIdx].tx);
            _dlgPages.Add(_limeCurrent.DisplayText);
        }
        else _dlgPages.AddRange(DialogueBox.PaginatePhone(_dlg[_dlgIdx].tx, DlgBodyWrapW));
        _dlgPacing = DialoguePacing.ForPages(_dlg[_dlgIdx].tx, _dlgPages);
        _dlgAutoT = 0;
    }
    private void DlgNextPage() { _dlgPage++; _dlgReveal = 0; _dlgLineT = 0; _dlgAutoT = 0; }
    private bool _pendingBurn;

    private int _dlgReadIdx = -1;  // 既読記録済みの行 index
    private int _dlgLogIdx = -1;   // 会話ログ（Hud.Backlog）へ積んだ行 index（2026-09-26。オート送りでも積む）
    private bool _ffNow;           // いま高速送り中か（ボタン列の SKIP 点灯用）

    // 会話ボックス上辺のボタン列（AUTO / SKIP / LOG / MENU・src/DialogToolbar.cs）。アンカーは端末内の DialogBox の右上。
    //   ハブはカットシーンではない＝MENU のパッド Start・キー M は PauseMenu が自前で読む（ここは足さない）。
    private readonly DialogToolbar _toolbar = new();
    private static Rect2 DialogBox => new(PhoneX, DialogueBox.FullScreen.Position.Y, PhoneW, DialogueBox.FullScreen.Size.Y);
    private static Vector2 ToolbarAnchor => new(DialogBox.End.X, DialogBox.Position.Y);
    private bool DialogShown => _mode == Mode.Dialogue && !_limeConversation && !_minaCall && !_dived && _dlg.Length > 0;
    // AUTO（GameManager.AutoAdvanceDialog）：現在ページの全文表示後、この秒数で次へ（--demo/--qa の _autoplay とは別）。
    private const double AutoAfterReveal = 1.0;
    private double _dlgAutoT;      // 現在ページを全文表示してからの経過（AUTO 用）

    private double _toastT;
    private bool _prominentToast;
    private string _toast = "", _toastSub = "";
    private Color _toastCol = UiKit.Info;

    private bool _autoplay;
    private const double AutoDiveDelay = 1.1, AutoAdvance = 1.4;

    private static Color AccountColor(string id) => id switch
    {
        "mina" => UiKit.Mina,
        "rei" => new Color(0.90f, 0.52f, 0.38f),
        "akari" => new Color(0.40f, 0.62f, 0.88f),
        "koharu" => new Color(0.46f, 0.74f, 0.52f),
        _ => UiKit.Kegare,
    };

    // タイトルと同じ夜の街(bg2/title/L1_far)を、ハブでは一段暗い藍で敷く。
    //   タイムラインの上に居る＝タイトルと同じ夜を見ている、という地続き感を出す一方、
    //   ハブは UI（投稿カード・ヘッダ・汚染バー）を読む画面なので、タイトルより暗い藍 (0.30,0.34,0.60) を
    //   Modulate で掛けて沈める。上に載る _Draw の夜グラデも不透明のままだと夜景が見えないので、
    //   夜景を敷けたときだけ半透明のスクリムへ落とす（_hasNightBg）。
    private const string NightBgPath = "res://char/bg2/title/L1_far.png";
    private static readonly Color NightBgTint = new Color(0.30f, 0.34f, 0.60f);
    private bool _hasNightBg;
    private Sprite2D? _nightBg;
    // 夜景の極低速の縦ドリフト（2-a）。振幅 12px・周期 30s の往復＝「タイムラインの上にいる時間が流れている」
    //   ことだけを言う量。速度は画面酔いにならない範囲（最大でも約 2.5px/s）に抑える。
    private const float NightDriftAmp = 12f, NightDriftPeriod = 30f;
    private float _nightBgY;

    private void BuildNightBg()
    {
        if (!ResourceLoader.Exists(NightBgPath)) return;
        var tex = ResourceLoader.Load<Texture2D>(NightBgPath);
        if (tex == null || tex.GetHeight() <= 0) return;
        float s = UiKit.Scale;
        // ドリフトで下端が見えないよう、縦だけ 1 + 2*振幅/設計高 ぶん伸ばして敷き、中央を基準位置にする。
        float sy = UiKit.DesignH * (1f + 2f * NightDriftAmp / UiKit.DesignH) / tex.GetHeight() * s;
        _nightBgY = -NightDriftAmp * s;
        AddChild(_nightBg = new Sprite2D
        {
            Name = "NightBg", Texture = tex, Centered = false,
            ZIndex = -12, ZAsRelative = false,
            Position = new Vector2(0, _nightBgY),
            Scale = new Vector2(UiKit.DesignW / tex.GetWidth() * s, sy),
            Modulate = NightBgTint,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
        });
        _hasNightBg = true;
    }

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        _drawCanvas = this;
        _phoneMask = new Node2D { Name = "PhoneScreenMask", ClipChildren = ClipChildrenMode.Only };
        _phoneMask.Draw += DrawPhoneMask;
        AddChild(_phoneMask);
        _phoneScreen = new Node2D { Name = "PhoneScreen" };
        _phoneScreen.Draw += DrawPhoneScreen;
        _phoneMask.AddChild(_phoneScreen);
        _overlay = new Node2D { Name = "HubOverlay" };
        _overlay.Draw += DrawOverlay;
        AddChild(_overlay);
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        RestoreLimeReplies();
        _zHeld = Pad.AdvanceHeld();
        _tabHeld = SwitchKeyDown();
        _phoneVisuals = new PhoneVisuals();
        AddChild(_phoneVisuals);
        _snsMedia = new SnsPostMedia();
        AddChild(_snsMedia);
        BuildNightBg();
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--demo" || args[i] == "--qa") _autoplay = true;
            if (args[i] == "--hub-preview" && i + 1 < args.Length) _previewState = args[i + 1];
            if (args[i] == "--hub-detail") _openDetail = true;
            if (args[i] == "--hub-toast") _previewToast = true;
            if (args[i] == "--hub-job") _openJob = true;
            if (args[i] == "--hub-photos") _openPhotos = true;
            if (args[i] == "--hub-shopnudge") _previewShopNudge = true;
            if (args[i] == "--hub-accountnudge") _previewAccountNudge = true;
        }

        BuildEntries();
        ApplyPreview();
        LoadFaces();
        _sideNameFont = GD.Load<FontFile>("res://assets/fonts/ShipporiMincho-SemiBold.ttf");
        _sideTitleFont = GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-Italic.ttf");
        foreach (var job in Jobs.All)
        {
            string id = job.CharacterId;
            _sidePortraits[id] = id == "mina" ? GD.Load<Texture2D>(Prologue.FirstTouchArt)
                : GD.Load<Texture2D>($"res://char/bg2/opening/op_{id}_cutin_v1.png");
            _sideSkies[id] = GD.Load<Texture2D>($"res://char/bg2/route/{id}_far.png");
            _sideScenery[id] = GD.Load<Texture2D>($"res://char/bg2/route/{id}_mid.png");
        }
        if (_previewState == null) _sel = DefaultSelection();
        _sideStoryId = _sideStoryPrevious = SideStoryId();
        _voiceMedallions = new VoiceMedallions();
        AddChild(_voiceMedallions);
        UpdateFeedScrollTarget();
        if (_autoplay || _previewState != null || _openDetail || _openJob) _mode = Mode.Cards;
        // デバッグ限定：--hub-detail で選択カードの投稿詳細を開いた状態から始める（スクショ用）。
        if (_openDetail && IsVoice(_sel)) OpenDetail();
        // デバッグ限定：--hub-job でジョブ選択を開いた状態から始める（スクショ用）。
        if (_openJob) OpenJob();
        // デバッグ限定：--hub-photos で写真アプリを開いた状態から始める（スクショ用）。
        if (_openPhotos)
        {
            _mode = Mode.Photos;
            _photoT = 0;
            _homeSel = 3;
            _zHeld = _navHeld = _xHeld = true;
        }
        // デバッグ限定：--hub-toast で炎上トーストの見え方を撮る（GameManager は一切触らない）。
        if (_previewToast) Toast("炎上中。次に潜るとき、光が薄い。", "発射間隔 +30%  移動 -10%  稼ぎ -40%", UiKit.Burn);

        // デバッグ限定：--hub-shopnudge で「説明を読み切った直後のホーム」を再現する（スクショ用。セーブは触らない）。
        //   本編ではこの状態は同じ画面の続き（説明の読了＝EndDialogue）で作られるので、ここはプレビュー専用。
        if (_previewShopNudge)
        {
            _shopNudge = !_autoplay;
            if (_shopNudge) { _mode = Mode.Home; _homeSel = 1; _zHeld = _navHeld = true; }
        }

        string? cleared = _game?.JustClearedStageId;
        _homeRevealPending = !_autoplay && cleared == GameManager.FirstStageId && _game!.HeartsSaved == 1
            && !_game.IsIdleDialogSeen(HomeRevealSeenKey);
        if (cleared == null && ShopUnlocked) _game!.MarkIdleDialogSeen(HomeRevealSeenKey);
        if (cleared != null)
        {
            _game!.JustClearedStageId = null;
            string keyHint = _game.JustClearedStageWasNew && _game.HeartsSaved == 1
                ? (ShopUnlocked
                    ? "強化ショップ・記録アプリを解放"
                    : "記録アプリを解放")
                : "";
            var freed = Jobs.UnlockedBy(cleared);
            if (_game.JustClearedStageWasNew && freed != null)
            {
                string sub = $"「{SwitchLabel}」から選べます。";
                if (keyHint.Length > 0) sub += $"\n{keyHint}";
                Toast($"{freed.CharacterName}が使えるようになりました", sub, JobColor(freed.Id), prominent: true);
            }
            else if (keyHint.Length > 0)
                Toast(keyHint, "", UiKit.Ok);
            var companion = CompanionDialogue.MenuLines(_game.SelectedJob, CompanionDialogue.Menu.Return);
            if (MinaLineUnavailable && !_game.JustClearedStageWasNew)
            {
                // Rei's first return contains the loss of contact. Later dives can still reward the return.
                var home = _autoplay ? Mode.Cards : Mode.Home;
                if (companion.Length > 0) StartHubConversation(companion, returnMode: home);
                else
                {
                    StartDialogue(companion, null, returnMode: home);
                    EndDialogue();
                }
                return;
            }
            var lines = FillObservations(ReturnDialog(cleared));
            if (companion.Length > 0)
            {
                var combined = new System.Collections.Generic.List<(string, string)>(companion);
                combined.AddRange(lines);
                lines = combined.ToArray();
            }
            if (_game.ShouldBurnAfter(cleared))
            {
                var combined = new System.Collections.Generic.List<(string, string)>(lines);
                combined.AddRange(BurnDialog());
                lines = combined.ToArray();
                _pendingBurn = true;
            }
            // アカウント追加の説明（docs/20260923/アカウント追加説明_本文_2026-09-23.md・A案）。あかり初回だけ、
            //   H1 の「被弾は{n}回でした」の直後＝「……お疲れさまでした。」の前へ実行時に差し込む
            //   （集計→ご報告→お疲れさま→次の声、の順で締めの一行を最後に残す）。ReturnDialog は再クリアでも
            //   流れる静的配列なので配列自体は触らない。once は読み切った時点（EndDialogue）で立てて保存する。
            //   こはる／レイの追加時には出さない＝本文があかり固有（「名義は、わたくしではありません」）。
            string? seenKey = null;
            if (freed != null && freed.CharacterId == "akari" && !StageTutorial.Skipped(_game) && !_game.IsIdleDialogSeen(AccountIntroSeenKey))
            {
                int at = System.Array.FindIndex(lines, l => l.Item2.StartsWith("……お疲れさまでした"));
                var spliced = new System.Collections.Generic.List<(string, string)>(lines);
                spliced.InsertRange(at < 0 ? lines.Length : at, AccountIntroAkari);
                lines = spliced.ToArray();
                seenKey = AccountIntroSeenKey;
            }
            if (lines.Length > 0) StartHubConversation(lines, returnMode: _autoplay ? Mode.Cards : Mode.Home, seenKey: seenKey, returning: true);
        }
        else if ((_game?.HeartsSaved ?? 0) > 0 && GD.Randf() < 0.5f)
        {
            // 再訪小話（小話集 v1 §1）：クリア直後ではない入場のうち約半分で、ハブ待機中の雑談を1本挟む。
            if (_autoplay) TryStartIdleSmallTalk();
            else _idleTalkPending = true;
        }
    }

    // 最後にクリアしたステージの IdleDialogs ＋ 全ステージ共通 SmallTalks を1プールにまとめ、
    // 未読を優先して抽選（既読は GameManager に永続）。全部既読ならリセットして選び直す。
    private void TryStartIdleSmallTalk()
    {
        string? lastCleared = null;
        for (int i = GameManager.Stages.Length - 1; i >= 0; i--)
        {
            if (_game!.IsStageCleared(GameManager.Stages[i].Id)) { lastCleared = GameManager.Stages[i].Id; break; }
        }
        if (lastCleared == null) return;

        var pool = new System.Collections.Generic.List<(string key, (string, string)[] lines)>();
        var idle = IdleDialogs(lastCleared);
        for (int i = 0; i < idle.Length; i++)
            pool.Add(($"idle_{lastCleared}_{i}", idle[i]));
        string companionKey = $"companion_hub_{_game!.JobDef.CharacterId}";
        var companion = CompanionDialogue.MenuLines(_game.SelectedJob, CompanionDialogue.Menu.Hub);
        if (companion.Length > 0 && !_game.IsIdleDialogSeen(companionKey))
        {
            _game.MarkIdleDialogSeen(companionKey);
            StartHubConversation(companion, noPost: true);
            return;
        }
        if (MinaLineUnavailable) return;
        for (int i = 0; i < SmallTalks.Length; i++)
        {
            // 「何も起きない日」は、この起動でまだ一度も潜っていないときだけ候補にする（潜った直後に出ると嘘になる）。
            if (i == SmallTalkNoEventIdx && _game!.TotalDives > 0) continue;
            pool.Add(($"common_{i}", SmallTalks[i]));
        }
        if (pool.Count == 0) return;

        var unseen = pool.FindAll(p => !_game!.IsIdleDialogSeen(p.key));
        if (unseen.Count == 0)
        {
            _game!.ResetIdleDialogSeen();
            unseen = pool;
        }
        // 「歌」だけ純粋ランダムの上に重みを置く：全面クリア前で未読なら先に出す
        //（「続きは、次にいらしたときに」が周回の前振りなので、FINAL より前に一度は通したい）。
        string songKey = $"common_{SmallTalkSongIdx}";
        var pick = (!(_game!.AllStoryCleared) && unseen.Exists(p => p.key == songKey))
            ? unseen.Find(p => p.key == songKey)
            : unseen[GD.RandRange(0, unseen.Count - 1)];
        _game!.MarkIdleDialogSeen(pick.key);
        // `{dives}`（この旅で潜った回数）はここで差し込む。`{n}`（直前の行に留まっていた秒）は
        // AdvanceDialogue が行ごとに埋めるので、別トークンにして同じ小話で両方使えるようにする。
        StartHubConversation(FillDives(pick.lines));
    }

    private void BuildEntries()
    {
        var list = new System.Collections.Generic.List<Entry>();
        foreach (var s in GameManager.Stages)
        {
            if (!IsStageUnlockedForDisplay(s.Id)) continue;
            bool cleared = IsClearedForDisplay(s.Id);
            string name = s.Title.Contains("—") ? s.Title.Split('—')[^1].Trim() : s.Title;
            list.Add(new Entry
            {
                IsFinal = false, Id = s.Id, Scene = s.Scene,
                Name = name, Handle = s.Handle, Tweet = s.Tweet, Initial = name.Substring(0, 1),
                Unlocked = true, Cleared = cleared,
                Reach = SnsVoices.VoiceReach(s.Id),
                VoiceReplies = cleared ? BuildVoiceReplies(s.Id) : null,
                Sort = Kind.Voice, RelT = RelTime(s.Id),
            });
        }
        if (FinalPostAvailable)
        {
            list.Add(new Entry
            {
                IsFinal = true, Id = "final", Scene = FinalScene,
                Name = FinalClearedForDisplay ? "ミナ" : FinalAccount.Name,
                Handle = FinalClearedForDisplay ? Handles.Mina : FinalAccount.Handle,
                Tweet = FinalTweet, Initial = FinalClearedForDisplay ? "ミ" : "?", Reach = new(2, 0, 53),
                Unlocked = true, Cleared = FinalClearedForDisplay,
                Sort = Kind.Voice, RelT = "now",
            });
        }
        var feed = Interleave(list);
        var reposts = new System.Collections.Generic.List<Entry>();
        foreach (string id in _game.RepostedPostIds)
        {
            int index = feed.FindIndex(entry => entry.Id == id);
            Entry post;
            if (index >= 0) post = feed[index];
            else
            {
                bool companion = id.EndsWith("_companion", System.StringComparison.Ordinal);
                var stage = System.Array.Find(GameManager.Stages, stage => id == ReturnPostId(stage.Id, companion) && IsClearedForDisplay(stage.Id));
                if (stage == null) continue;
                post = ReturnEntry(stage.Id, companion);
            }
            post.IsRepost = true;
            reposts.Add(post);
        }
        feed.InsertRange(0, reposts);
        _entries = feed.ToArray();
    }

    private System.Collections.Generic.List<Entry> Interleave(System.Collections.Generic.List<Entry> voices)
    {
        var feed = new System.Collections.Generic.List<Entry>();
        var pinned = PinnedPost();
        if (pinned != null) feed.Add(pinned.Value);
        for (int i = voices.Count - 1; i >= 0; i--)
        {
            feed.Add(voices[i]);
            foreach (var post in SnsVoices.ReturnPosts)
                if (voices[i].Id == SnsVoices.ReturnPostStageId(post) && IsReturnPostUnlocked(post))
                {
                    int koharu = post == SnsVoices.AkariReturnPost ? feed.FindIndex(IsNextKoharuPost) : -1;
                    if (koharu >= 0) feed.Insert(koharu, SocialEntry(post));
                    else feed.Add(SocialEntry(post));
                }
            foreach (var post in SnsVoices.Posts)
            {
                if (post.Chapter != i) continue;
                feed.Add(SocialEntry(post));
            }
        }
        return feed;
    }

    private static Entry SocialEntry(SnsVoices.Post post)
    {
        bool personal = post.CharacterId.Length > 0;
        return new Entry
        {
            Id = post.Id, Scene = "", Name = SnsVoices.AuthorName(post), Handle = SnsVoices.AuthorHandle(post),
            Tweet = post.Text, Initial = "", Unlocked = true, Sort = personal ? Kind.Personal : Kind.Filler, Social = post,
            RelT = personal || post.PreviousId.Length > 0 ? "その後" : "日常",
            Icon = personal ? 0 : SnsVoices.All[post.Author].Icon,
            Reach = post.Reach,
        };
    }

    // ミナの最新投稿＝直近にクリアした面の帰還投稿の1行目（ReturnDialog(id)[0]）。
    //   まだ一度もクリアしていない＝投稿していないので、その場合は固定ポストを置かない。
    private Entry? PinnedPost()
    {
        string? last = null;
        for (int i = GameManager.Stages.Length - 1; i >= 0; i--)
            if (IsClearedForDisplay(GameManager.Stages[i].Id)) { last = GameManager.Stages[i].Id; break; }
        return last == null ? null : ReturnEntry(last, _game.ClearedWithCompanion(last));
    }

    private static string ReturnPostId(string stageId, bool companion) => "pinned_" + stageId + (companion ? "_companion" : "");

    private Entry ReturnEntry(string stageId, bool companion)
    {
        var d = companion ? CompanionReturnDialog(stageId) : MinaReturnDialog(stageId);
        return new Entry
        {
            IsFinal = false, Id = ReturnPostId(stageId, companion), Scene = "", Name = "ミナ", Handle = Handles.Mina,
            Tweet = d[0].Item2, Initial = "ミ", Unlocked = true, Cleared = false,
            Sort = Kind.Pinned, RelT = "now",
            Reach = (stageId, companion) switch
            {
                ("akari", false) => new(38, 5, 1087),
                ("akari", true) => new(7, 0, 93),
                ("koharu", false) => new(16, 2, 211),
                ("koharu", true) => new(8, 1, 105),
                ("rei", false) => new(11, 1, 148),
                ("rei", true) => new(5, 0, 72),
                _ => throw new System.ArgumentOutOfRangeException(nameof(stageId)),
            },
        };
    }

    // 固定ポストの「直近にクリアした面」判定。デバッグプレビュー（--hub-preview）のときは
    //   セーブではなく表示状態のほうを見る＝プレビューでもミナの固定ポストが出る。
    private bool IsClearedForDisplay(string id) => _previewState switch
    {
        "all" or "final" => true,
        "lock" => false,
        "first" => id == "akari",
        _ => _game?.IsStageCleared(id) ?? false,
    };

    private bool IsStageUnlockedForDisplay(string id) => _previewState switch
    {
        "all" or "final" => true,
        "lock" => id == GameManager.FirstStageId,
        "first" => System.Array.FindIndex(GameManager.Stages, stage => stage.Id == id) <= 1,
        _ => _game?.IsStageUnlocked(id) ?? true,
    };

    // デバッグ限定：表示状態だけを上書き（GameManager のセーブは一切触らない）。
    //   all   = 全ステージ解放＆クリア済（FINAL も表示）
    //   final = ストーリー全クリア直後（カードは「届いた」、FINAL を強調）
    //   lock  = 1枚目だけ「声」・以降はまだ聞こえない（初回起動の見え方）
    //   first = 1面クリア直後（あかり＝届いた／こはる＝声／ミナの固定ポストが載った feed）
    private void ApplyPreview()
    {
        if (_previewState == null) return;
        BuildEntries();
        _sel = DefaultSelection();
    }

    // SNSアイコンと会話の表情差分は別々に保持する。
    private void LoadFaces()
    {
        foreach (var job in Jobs.All)
        {
            _playerFaces[job.CharacterId] = ResourceLoader.Load<Texture2D>(CompanionDialogue.AccountPortrait(job.Id));
            _dialogueFaces[job.CharacterId] = ResourceLoader.Load<Texture2D>(CompanionDialogue.Portrait(job.Id));
        }
        _minaFace = _dialogueFaces["mina"];
        foreach (var e in _entries)
        {
            string id = e.Id;
            if (_faces.ContainsKey(id)) continue;
            if (e.Sort == Kind.Personal) { _faces[id] = _playerFaces[e.Social!.CharacterId]; continue; }
            // 埋め草＝人の顔を持たない他人。アイコンは _mobIcons 側（SnsVoices の番号）から引く。
            if (e.Sort == Kind.Filler) { _faces[id] = null; continue; }
            if (e.IsFinal && !e.Cleared) { _faces[id] = FinalAccount.CreateIcon(); continue; }
            if (e.Sort == Kind.Pinned || e.IsFinal) { _faces[id] = _playerFaces["mina"]; continue; }
            _faces[id] = GD.Load<Texture2D>(CompanionDialogue.AccountIcon(id));
        }
        LoadMobIcons();
    }


    private void LoadMobIcons()
    {
        if (_mobIcons.Length > 0) return;
        var arr = new Texture2D?[SnsVoices.IconCount + 1];   // [0] は未使用（番号は 1 始まり）
        for (int i = 1; i <= SnsVoices.IconCount; i++)
        {
            string p = SnsVoices.IconPath(i);
            arr[i] = ResourceLoader.Exists(p) ? ResourceLoader.Load<Texture2D>(p) : null;
        }
        _mobIcons = arr;
    }

    // 番号 → アイコン。未生成・範囲外は null（DrawFillerAvatar が無地円に落ちる）。
    private Texture2D? MobIcon(int n) => n >= 1 && n < _mobIcons.Length ? _mobIcons[n] : null;
    private Texture2D? FaceFor(string id) => _faces.TryGetValue(id, out var t) ? t : null;

    // 立ち絵ごとに頭部の高さが違うため、円窓の上端 UV を顔に合わせて個別調整（(C) topCrop キャラ別）。
    //   値が小さいほど画像上部（=頭頂寄り）をサンプル。各立ち絵を実測して顔が円中心に来る値。
    private static float TopCropFor(string id) => id switch
    {
        // 2026-09-07: 三人を v3 の立ち絵（社会人版）に差し替えたので、円窓の上端を測り直した。
        //   v3 は v1 より顔が高い位置にあり、旧値のままだと丸の上端に顔が寄って額が切れる。
        //   窓を上へずらす（値を小さくする）ほど顔は丸の下＝中心寄りに来る。0 が画像の上端。
        "rei" => 0.02f,      // 419x720・短い黒髪。髪が暗いので顔を少し大きめに入れる
        "akari" => 0.025f,   // 446x720・琥珀のショート＋ポニー
        "koharu" => 0.02f,   // 408x720・焦げ茶のボブ（髪の量が多く上に張り出す）
        "mina" or "final" => 0.03f,
        _ => 0.06f,
    };

    // 既定カーソル＝次の声のある投稿（2-b でも維持）。埋め草の上には決して置かない
    //   ＝入場して Z、で潜れる導線（2 押し）が feed になっても崩れない。
    private int DefaultSelection()
    {
        for (int i = 0; i < _entries.Length; i++)
            if (!_entries[i].IsRepost && _entries[i].Sort == Kind.Voice && !_entries[i].Cleared) return i;
        for (int i = 0; i < _entries.Length; i++)
            if (!_entries[i].IsRepost && _entries[i].Sort == Kind.Voice) return i;
        return 0;
    }

    public override void _Process(double delta)
    {
        if (_unlockMovie != null || _unlockPortrait != null)
        {
            if (_mode != Mode.Job || _jobChoices[_jobSel].Id != _unlockJob) StopUnlockCard();
            else if (_unlockMovie != null) _unlockMovie.Paused = Pad.UiBlocked(this);
        }
        if (LunaticUnlock.PlayIfPending(this)) { _zHeld = Pad.AdvanceHeld(); return; }
        // ボタン列は Pad.AdvanceHeld を読む前に回す（ボタン上のクリックを会話送りに数えない）。会話の外でも毎フレーム回す
        //   （SKIP ラッチは会話が閉じても切らない＝画面を跨いで残し、右上に印を出す）。
        _toolbar.Tick(this, delta, DialogShown, ToolbarAnchor, pointer: PhonePointer);
        _t += delta;
        string sideStory = SideStoryId();
        if (_sideStoryId != sideStory)
        {
            _sideStoryPrevious = _sideStoryId;
            _sideStoryId = sideStory;
            _sideStoryBlend = 0;
        }
        _sideStoryBlend = Mathf.Min(1, _sideStoryBlend + (float)delta / 0.45f);
        UpdateVoiceMedallions(delta);
        if (_toastT > 0) _toastT -= delta;
        // 夜景のドリフト（会話中も止めない＝画面の裏で夜が流れ続ける）。
        if (_nightBg != null)
            _nightBg.Position = new Vector2(0, _nightBgY
                + Mathf.Sin((float)_t * Mathf.Tau / NightDriftPeriod) * NightDriftAmp * UiKit.Scale);
        // 選択の寄り（0.12s で 0→1 に近づける lerp。選択が変わったら 0 へリセット）
        if (_selAnim != _sel) { _selAnim = _sel; _selT = 0f; }
        _selT = Mathf.Min(1f, _selT + (float)delta / 0.12f);
        _feedScroll = Mathf.Lerp(_feedScroll, _feedScrollTarget, Mathf.Min(1f, (float)delta * 12f));
        if (_dived) { QueueRedraw(); return; }
        // ポーズメニューを閉じた Esc/Z の同じ押下が漏れて 決定/会話送り/リロード が誤発火しないよう食う（Pad.UiBlocked）。
        if (Pad.UiBlocked(this))
        {
            _navHeld = _zHeld = _xHeld = _tabHeld = _mediaHeld = true;
            QueueRedraw();
            return;
        }
        // 画面下のヒント帯の「メニュー」は左クリックでも開く（PauseMenu の右下チップは Hub では出さない＝
        //   ヒント帯に含めたので二重になる。チップが持っていたクリックの役目をここで引き継ぐ）。
        TickLimeReplies(delta);
        if (HintMenuClicked())
        {
            GetNodeOrNull<PauseMenu>("/root/PauseMenu")?.Open();
            QueueRedraw();
            return;
        }
        if (ProcessSwitchButton()) { QueueRedraw(); return; }
        if (ProcessVoiceMedallionClick()) { QueueRedraw(); return; }
        if (MinaLineOpen) { ProcessMinaCall(delta); QueueRedraw(); return; }
        if (LimeOpen) { ProcessLime(delta); QueueRedraw(); return; }
        if (_mode == Mode.Dialogue) { ProcessDialogue(delta); QueueRedraw(); return; }
        if (_mode == Mode.Detail) { ProcessDetail(delta); QueueRedraw(); return; }
        if (_mode == Mode.SocialThread) { ProcessSocialThread(delta); QueueRedraw(); return; }
        if (_mode == Mode.Job) { ProcessJob(delta); QueueRedraw(); return; }
        if (_mode == Mode.Photos) { ProcessPhotos(delta); QueueRedraw(); return; }
        if (_mode == Mode.HomeReveal) { ProcessHomeReveal(delta); QueueRedraw(); return; }
        if (_mode == Mode.SnsOpening) { ProcessSnsOpening(delta); QueueRedraw(); return; }
        if (_mode == Mode.Home) { ProcessHome(); QueueRedraw(); return; }
        ProcessCards();
        QueueRedraw();
    }

    // ───────── 会話 ─────────
    // noPost=true＝この会話はミナの投稿ではない（H0 のような場面の導入）。閉じたときに
    //   インプレ／フォロワーの加算とトーストを出さない＝まだ何も投稿していないのに数字が動くのを防ぐ。
    private bool _dlgNoPost;
    private Mode _dlgReturnMode = Mode.Cards;
    // 行ごとの表情指定が空なら、話者の標準立ち絵を使う。
    private string[] _dlgFaces = System.Array.Empty<string>();
    private void StartDialogue((string, string)[] lines, string? replyId, bool noPost = false, Mode returnMode = Mode.Cards, string? seenKey = null, string[]? faces = null)
    {
        _mode = Mode.Dialogue;
        _minaCall = false;
        _limeConversation = false;
        _dlgNoPost = noPost;
        _dlgReturnMode = returnMode;
        _dlgSeenKey = seenKey;
        _dlgFaces = faces ?? System.Array.Empty<string>();
        _zHeld = Pad.AdvanceHeld();
        // `{n}` の差し込みで中身を書き換えるので、静的な台詞データを直接持たず必ず写しで回す
        //（そのまま持つと差し込んだ実測値が静的配列に焼き付き、次の再訪でも同じ数字が出てしまう）。
        _dlg = ((string sp, string tx)[])lines.Clone(); _dlgIdx = 0; _dlgLineT = 0; _dlgReveal = 0; _dlgReplyId = replyId;
        _dlgReadIdx = -1; _ffNow = false; _dlgLogIdx = -1; _dlgAutoT = 0;
        _dlgPages.Clear(); _dlgPage = 0; _dlgPagedIdx = -1;
        // この会話の字形を先に作る（ハブの台本は大きく、読み込み中に作り切れないことがある・GlyphWarmer）。
        GlyphWarmer.WarmFirst(this, System.Linq.Enumerable.Select(_dlg, line => line.sp + line.tx));
    }

    // 行ごとの立ち絵（_dlgFaces）→ テクスチャ。指定が無ければ null＝話者から引く既定のまま。
    //   毎フレーム Load しないようパスで引けるキャッシュに溜める。
    private readonly System.Collections.Generic.Dictionary<string, Texture2D?> _lineFaces = new();
    private Texture2D? LineFace(int index)
    {
        if (index < 0 || index >= _dlgFaces.Length || string.IsNullOrEmpty(_dlgFaces[index])) return null;
        string path = _dlgFaces[index];
        if (!_lineFaces.TryGetValue(path, out var tex))
            _lineFaces[path] = tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        return tex;
    }

    // 会話ログの種別（本文色の出し分け用）。話者名と色は画面（DrawDialog／SpeakerFace）と同じものを渡す。
    private static Hud.LineKind DialogLogKind(string sp) =>
        sp.StartsWith("ミナ") ? Hud.LineKind.Mina
        : sp == "あなた"      ? Hud.LineKind.Boy
        : sp == "Ｙ 投稿"     ? Hud.LineKind.Post
        : sp.StartsWith("Ｙ") ? Hud.LineKind.Narration   // Ｙ システム（画面テキスト）
        : Hud.LineKind.Other;                            // 三人／同行キャラ

    private void ProcessDialogue(double delta)
    {
        DlgEnsurePages();
        // 会話ログ（L / Tab で開く Backlog）へ、表示を始めた行を積む（2026-09-26）。行が変わった瞬間に1回。
        //   `{n}` の差し込みは AdvanceDialogue で行が表示される前に済む＝ここで渡す本文は画面と同じ。
        if (_dlgLogIdx != _dlgIdx && _dlg.Length > 0 && _dlgIdx < _dlg.Length)
        {
            _dlgLogIdx = _dlgIdx;
            var (sp, tx) = _dlg[_dlgIdx];
            string section = _shopTutorialDlg ? "強化ショップの案内"
                : _dlgSeenKey == SnsIntroSeenKey ? "SNSの案内"
                : _dlgReplyId != null ? "SNSの返信" : "ホーム";
            Hud.PushLog(section, DialogLogKind(sp), sp, tx, SpeakerFace(sp).col);
        }
        int len = DlgCurPage.Length;
        if (_autoplay)
        {
            _dlgReveal = len; // デモ/オートは即時全文（送りペースを変えない）
            _dlgLineT += delta;
            // オート：現在ページを見せたら次ページ、最終ページなら次行へ（詰まらせない）。
            if (_dlgLineT >= AutoAdvance) { _dlgLineT = 0; if (!DlgLastPage) DlgNextPage(); else AdvanceDialogue(); }
            return;
        }
        if (_dlgReadIdx != _dlgIdx && _dlg.Length > 0 && _dlgIdx < _dlg.Length)
        {
            _dlgReadIdx = _dlgIdx;
            _game?.MarkLineRead(_dlg[_dlgIdx].tx);
        }
        _ffNow = Hud.SkipHeld;
        _dlgLineT += delta;
        // 句読点の間は本編HUDと共有する。
        bool pageWasRevealed = _dlgReveal >= len;
        if (_dlgReveal < len)
        {
            _dlgReveal = DialogueBox.AdvanceReveal(DlgCurPage, _dlgReveal, delta, _game?.MsgCharsPerSec ?? DialogueBox.DefaultCharsPerSec, DlgPacing);
            DialogueBox.TypeSound(DialogLogKind(_dlg[_dlgIdx].sp), DlgCurPage, _dlgReveal, ref _dlgTypeCursor);
        }
        // 高速送り：現在ページ即時表示 → 後続ページは飛ばし、最終ページ完了で次行へ（Ctrl/RB を離した瞬間に止まる）。
        if (_ffNow)
        {
            _dlgReveal = len;
            if (_dlgLineT >= 0.15) { _dlgLineT = 0; if (!DlgLastPage) DlgNextPage(); else AdvanceDialogue(); return; }
        }
        // AUTO（会話ボックスの AUTO ボタン／設定の「オート会話送り」）：現在ページの全文表示後 AutoAfterReveal 秒で送る。
        if (pageWasRevealed) _dlgAutoT += delta; else _dlgAutoT = 0;
        if ((_game?.AutoAdvanceDialog ?? false) && _dlgAutoT >= (DlgPacing?.AutoWait ?? AutoAfterReveal))
        {
            _dlgAutoT = 0;
            if (!DlgLastPage) DlgNextPage(); else AdvanceDialogue();
            return;
        }
        // 会話送り：Z/Enter/ui_accept/Pad A に加えマウス左クリックでも送れる共通ヘルパ（マウス対応 P2）。
        // 会話中はカードのホットスポットを登録しない＝画面全体が「送り」になり、クリック誤爆は起きない。
        bool z = Pad.AdvanceHeld();
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.15)
        {
            if (_dlgReveal < len) _dlgReveal = len; // 1回目で全文（早送り）
            else if (!DlgLastPage) DlgNextPage();   // 後続ページがあれば続きへ
            else AdvanceDialogue();                 // 最終ページ読了＝次の行へ
        }
    }

    private void AdvanceDialogue()
    {
        if (_minaCall)
        {
            if (_minaChoices.Length > 0) return;
            var line = _dlg[_dlgIdx];
            _game.LimeThread("mina").Add(line);
            Hud.PushLog("ミナとの回線", DialogLogKind(line.sp), line.sp, line.tx, SpeakerFace(line.sp).col);
            if (!_autoplay) _game.MarkLineRead(line.tx);
            if (_minaCasual) _game.AutoSave();
            if (_minaCasual && _dlgIdx == _dlg.Length - 1)
            {
                OfferMinaChoices(_minaBeat!.Next.Length > 0);
                return;
            }
        }
        if (_limeConversation)
        {
            var (speaker, text) = _dlg[_dlgIdx];
            _game.LimeHistory.Add((speaker, text));
            Hud.PushLog("LIME", DialogLogKind(speaker), speaker, text, SpeakerFace(speaker).col);
            if (!_autoplay) _game.MarkLineRead(text);
            _limeScroll = 0;
        }
        // 直前の行に留まっていた実秒。次行が「いまの間、{n}秒」なら、この値をそのまま差し込む
        // （仮台本 06 の再訪小話（1）の補助観測。表示専用＝保存しない）。
        int dwell = Mathf.Max(0, Mathf.RoundToInt((float)_dlgLineT));
        _dlgIdx++;
        _dlgReveal = 0;
        _dlgLineT = 0; // スキップの行ゲート用（オート送りは呼び出し側で別途リセット済み）
        _dlgPage = 0; _dlgPagedIdx = -1;
        if (_dlgIdx < _dlg.Length && _dlg[_dlgIdx].tx.Contains("{n}"))
            _dlg[_dlgIdx].tx = _dlg[_dlgIdx].tx.Replace("{n}", dwell.ToString());
        if (_dlgIdx >= _dlg.Length) EndDialogue();
    }

    private void EndDialogue()
    {
        bool fromMina = _minaCall;
        _minaCall = false;
        _minaChoices = [];
        _minaBeat = null;
        _minaCasual = false;
        _limeConversation = false;
        if (_dlgSeenKey != null)
        {
            // アカウント追加の説明を読み切った回＝次に SNS を開いたとき、フッタ「アカウント」を脈動させる。
            //   会話が終わった時点はホーム画面でフッタが見えないので、誘導はここで予約して Cards で出す。
            if (_dlgSeenKey == AccountIntroSeenKey && !_autoplay) _game.AccountNudgePending = true;
            _game.MarkIdleDialogSeen(_dlgSeenKey);
            _dlgSeenKey = null;
        }
        // 強化ショップの説明を読み切った＝ホームの強化アイコンを誘導表示にして「押す」のを待つ（2026-09-22）。
        //   説明はホームの上に出ている＝画面は変わらないので、ここで誘導を立てるだけで最終行
        //   「——では、まいりましょう。」がそのまま光ったアイコンへの号令になる（2026-10-03）。
        if (_shopTutorialDlg)
        {
            _game.ShopTutorialSeen = true;
            _shopTutorialDlg = false;
            _shopNudge = true;         // オートプレイでは説明自体を出さない（TryOpenShopTutorial のガード）
            _homeSel = 1;              // カーソルは強化ショップのアイコンに置いておく
            _zHeld = _navHeld = true;  // 最後の行を送った Z／A が「ひらく」へ漏れないよう食う
            Audio.Instance?.PlayUiConfirm();
        }
        if (_dlgNoPost)
        {
            // 投稿ではない会話（H0）＝加算もトーストも無し。オートセーブと画面戻しだけ行う。
            _dlgNoPost = false;
            _game?.AutoSave();
            _mode = _dlgReturnMode;
            _cardsEnteredT = _t;
            if (_mode == Mode.Cards) UpdateFeedScrollTarget();
            return;
        }
        // トーストは X の通知文の型で出す（2-a）。数値は残すが、主語は「誰に届いたか」に置き換える。
        if (_dlgReplyId != null)
        {
            long imp = _game?.GainImpression(60) ?? 0;
            _game?.AddFollowers(12);
            _game?.MarkReplied(_dlgReplyId, _dlg);
            BuildEntries();
            Toast($"{Handles.Mina} の返信が 12 人に届いた", $"Imp +{imp}  フォロワー +12", UiKit.Ok);
        }
        else
        {
            long imp = _game?.GainImpression(40) ?? 0;
            _game?.AddFollowers(8);
            BuildEntries(); // タイムライン更新（クリア/フォロワー連動のエンゲージ数を反映）
            Toast($"{Handles.Mina} の投稿が 8 人に届いた", $"Imp +{imp}  フォロワー +8", UiKit.Ok);
        }
        if (_pendingBurn)
        {
            _pendingBurn = false;
            _game?.TriggerBurn();
            // 炎上は1行目を世界の言葉に、数値（実際に掛かるペナルティ）は2行目へ小さく落とす。
            Toast("炎上中。次に潜るとき、光が薄い。", "発射間隔 +30%  移動 -10%  稼ぎ -40%", UiKit.Burn);
        }
        _game?.AutoSave(); // Hub帰還でオートセーブ（slot 0）
        _mode = _dlgReturnMode;
        _cardsEnteredT = _t;
        if (_dlgReplyId != null && _mode == Mode.Cards) UpdateFeedScrollTarget();
        if (_homeRevealPending)
        {
            if (DisplayServer.GetName() != "headless")
            {
                using var screenshot = GetViewport().GetTexture().GetImage();
                _homeSnapshot = ImageTexture.CreateFromImage(screenshot);
                _homeSnapshotRegion = new Rect2(PhoneX / W * screenshot.GetWidth(), 0, PhoneW / W * screenshot.GetWidth(), screenshot.GetHeight());
            }
            _homeRevealPending = false;
            _homeRevealT = 0;
            _homeRevealFromMina = fromMina;
            _homeSel = fromMina ? 0 : LimeAppIndex;
            _mode = Mode.HomeReveal;
            _toastT = 0;
        }
    }

    private void ProcessHomeReveal(double delta)
    {
        double previous = _homeRevealT;
        _homeRevealT += delta;
        if (previous < 1.15 && _homeRevealT >= 1.15)
        {
            _homeSel = 1;
            Audio.Instance?.PlayUiBuy();
        }
        if (_homeRevealT < 2.3) return;
        _homeSnapshot = null;
        _mode = Mode.Home;
        _homeSel = 1;
        _zHeld = Pad.AdvanceHeld();
        _game.MarkIdleDialogSeen(HomeRevealSeenKey);
        _game.AutoSave();
        TryOpenShopTutorial();
    }

    // 強化ショップの説明（ShopTutorialLines）を一度だけ挟む。始めたら true（呼び元は以降を打ち切る）。
    //   置き場所：帰還会話（お疲れさま）→ ホーム解禁演出でアイコンが光る → ここ → ホームで押す、の順。
    //   説明の最終行「——では、まいりましょう。」が、目の前で光ったアイコンへの号令になる。
    //   説明を先（ステージ側）に出すと「ひと息つきましょう」と帰還会話の帰宅挨拶が二重に立つので、
    //   かならず帰還会話のあとに置く。
    //   ★2026-10-03: 専用シーン（ShopTutorial.tscn）への遷移をやめ、ハブのこの会話機構で流す。
    //     以前は「ローディング＝強化ショップ → 中身の無い強化ショップでミナが喋る → ホームへ戻される →
    //     アイコンを押す → 本物のショップ」と見え、ショップに二度入って一度目が壊れているように映った
    //     （ユーザー報告）。説明はホームの上に出す＝画面遷移もローディングも挟まない。
    //   解禁演出（HomeReveal）とホーム入場の両方から呼ぶ＝演出が出ない状態のセーブでも取りこぼさない。
    private bool _shopTutorialDlg;   // いま流れている会話が説明パートか（読了で _shopNudge を立てる印）
    private bool TryOpenShopTutorial()
    {
        if (_autoplay || _dived || _game == null || _previewShopNudge) return false;
        if (StageTutorial.Skipped(_game) || _game.ShopTutorialSeen || !ShopUnlocked || MinaLineUnavailable) return false;
        // 立ち絵は行ごと（表情差分）。戻り先はホーム＝説明の裏で光ったアイコンがそのまま見えている。
        var lines = new (string, string)[ShopTutorialLines.Length];
        var faces = new string[ShopTutorialLines.Length];
        for (int i = 0; i < ShopTutorialLines.Length; i++)
        {
            lines[i] = (ShopTutorialLines[i].sp, ShopTutorialLines[i].tx);
            faces[i] = ShopTutorialLines[i].face;
        }
        _homeSel = 1;
        StartHubConversation(lines, noPost: true, returnMode: Mode.Home, faces: faces);
        _shopTutorialDlg = true;
        return true;
    }

    private void DrawHomeReveal()
    {
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((float)_homeRevealT / 1.1f, 0f, 1f));
        if (_homeSnapshot == null || k >= 1f) return;
        Vector2 center = new Vector2(W / 2f, H / 2f).Lerp(_homeRevealFromMina ? MinaHomeRect.GetCenter() : HomeAppIconRect(LimeAppIndex).GetCenter(), k);
        Vector2 size = new Vector2(PhoneW, H) * Mathf.Lerp(1f, 0.12f, k);
        float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp((k - 0.75f) / 0.25f, 0f, 1f));
        _drawCanvas.DrawTextureRectRegion(_homeSnapshot, new Rect2(center - size / 2f, size), _homeSnapshotRegion, new Color(1, 1, 1, alpha));
    }

    // トースト：1行目＝世界の言葉（通知文）、2行目＝現状の数値（小さく・任意）。
    private void Toast(string msg, string sub, Color col, bool prominent = false)
    {
        _toast = msg; _toastSub = sub; _toastCol = col;
        _prominentToast = prominent;
        _toastT = prominent ? 6 : 2.6;
    }

    private static readonly string[] HomeApps = { "SNS", "強化ショップ", "記録", "写真", "カスタマイズ", "BOSSラッシュ", "プラクティス", "BOSS単体", "ミュージック", "LIME" };
    internal static int HomeAppCount => HomeApps.Length;
    private const int HomeAppIdBase = 22000;
    private static readonly Color[] HomeAppColors = {
        new("6dc9d0"), new("db91b1"), new("a5c9a7"), new("e3bd76"),
        new("a5b9e4"), new("e1a18a"), new("8ebfb7"), new("baa2df"), new("e9a7c9"), LimeAccent };
    private static int HomePageCount => (HomeApps.Length + 3) / 4;
    private int HomePage => _homeSel / 4;
    internal void UpdatePhoneVisuals(double delta)
    {
        for (int i = 0; i < HomeApps.Length; i++)
        {
            _phoneAppUnlocked[i] = HomeAppUnlocked(i);
            _phoneAppActivation[i] = _mode == Mode.HomeReveal && i == 1
                ? Mathf.Clamp(((float)_homeRevealT - 1.15f) / .65f, 0, 1) : 1;
        }
        int selected = Pad.UsingMouse ? UiKit.HoveredId() - HomeAppIdBase : _homeSel;
        bool interactive = _mode == Mode.Home && !_minaHomeFocus && !Pad.UiBlocked(this) && GetWindow().HasFocus() && !_dived;
        _phoneVisuals.SnsOpening = _mode == Mode.SnsOpening ? Mathf.Clamp((float)(_snsOpeningT / SnsOpenDuration), 0, 1) : -1;
        _phoneVisuals.Update(PhoneScreenPose,
            HomePresented, interactive, selected, _phoneAppUnlocked, _phoneAppActivation, Pad.MousePos(), delta);
    }
    private bool HomePresented => _mode is Mode.Home or Mode.HomeReveal or Mode.SnsOpening
        || (_mode == Mode.Dialogue && !_limeConversation && !_minaCall && _dlgReturnMode == Mode.Home && !_homeRevealPending)
        || (_mode == Mode.Job && _jobReturnMode == Mode.Home);

    private Transform2D HomePhoneTransform()
    {
        float opening = _mode == Mode.SnsOpening
            ? Mathf.SmoothStep(0, 1, Mathf.Clamp((float)_snsOpeningT / (float)SnsOpenDuration, 0, 1)) : 0;
        float scale = Mathf.Lerp(.83f, 1f, opening);
        Vector2 center = new(W / 2, H / 2);
        return new Transform2D(Vector2.Right * scale, Vector2.Down * scale, center * (1 - scale));
    }

    private Transform2D PhoneScreenPose => HomePresented ? HomePhoneTransform() : Transform2D.Identity;
    private Vector2 PhonePointer => PhoneScreenPose.AffineInverse() * Pad.MousePos();

    private void BeginPhoneScreen()
    {
        var pose = PhoneScreenPose;
        _drawCanvas.DrawSetTransformMatrix(new Transform2D(pose.X * UiKit.Scale, pose.Y * UiKit.Scale, pose.Origin * UiKit.Scale));
    }

    private Rect2 HomePhoneBounds()
    {
        var pose = HomePhoneTransform();
        var bounds = new Rect2(pose * new Vector2(PhoneX - 20, -16), Vector2.Zero);
        foreach (var corner in new[] { new Vector2(PhoneX + PhoneW + 32, -16),
            new Vector2(PhoneX + PhoneW + 32, H + 28), new Vector2(PhoneX - 20, H + 28) })
            bounds = bounds.Expand(pose * corner);
        return bounds;
    }

    private static Rect2 HomePageRect(int page) => new(PhoneX + 76 + page * 328f / HomePageCount, 624, 328f / HomePageCount, 44);

    private Rect2 HomeAppRect(int index)
    {
        return new Rect2(PhoneX + 48f + index % 2 * 192f, 266f + index % 4 / 2 * 162f, 192f, 144f);
    }

    private Rect2 HomeAppIconRect(int index)
    {
        var rect = HomeAppRect(index);
        return new Rect2(rect.Position + new Vector2((rect.Size.X - 88f) / 2f, 8f), new Vector2(88f, 88f));
    }

    private bool HomeAppUnlocked(int index) => index switch
    {
        1 => ShopUnlocked,
        2 => RecordsUnlocked,
        _ => true,
    };

    private void ProcessHome()
    {
        // 説明がまだなら、ホームを触らせる前に一度だけ挟む（解禁演出を経ないセーブの取りこぼし防止）。
        if (TryOpenShopTutorial()) return;
        Vector2 pointer = PhonePointer;
        if (Pad.MouseClick())
            for (int page = 0; page < HomePageCount; page++)
                if (HomePageRect(page).HasPoint(pointer))
                {
                    _homeSel = page * 4;
                    _minaHomeFocus = false;
                    Audio.Instance?.PlayUiMove();
                    return;
                }
        UiKit.BeginHotspots(pointer);
        UiKit.Hotspot(MinaHomeRect, MinaHomeId);
        for (int i = HomePage * 4; i < Mathf.Min(HomePage * 4 + 4, HomeApps.Length); i++) UiKit.Hotspot(HomeAppRect(i), HomeAppIdBase + i);
        int hovered = UiKit.HoveredId() - HomeAppIdBase;
        if (Pad.UsingMouse) _minaHomeFocus = UiKit.HoveredId() == MinaHomeId;
        if (Pad.UsingMouse && hovered >= 0 && hovered < HomeApps.Length && hovered != _homeSel)
        {
            _homeSel = hovered;
            Audio.Instance?.PlayUiMove();
        }
        int clicked = UiKit.ClickedId(Pad.MouseClick()) - HomeAppIdBase;
        if (clicked == MinaHomeId - HomeAppIdBase && _t > .3) { OpenMinaCall(); return; }
        if (clicked >= 0 && clicked < HomeApps.Length && _t > 0.3)
        {
            OpenHomeApp(clicked);
            return;
        }
        bool previous = Input.IsActionPressed("ui_left") || Input.IsActionPressed("ui_up");
        bool next = Input.IsActionPressed("ui_right") || Input.IsActionPressed("ui_down");
        if ((previous || next) && !_navHeld)
        {
            if (_minaHomeFocus) _minaHomeFocus = false;
            else if (Input.IsActionPressed("ui_up") && _homeSel % 4 < 2) _minaHomeFocus = true;
            else if (Input.IsActionPressed("ui_up")) _homeSel = (_homeSel + HomeApps.Length - 2) % HomeApps.Length;
            else if (Input.IsActionPressed("ui_down")) _homeSel = (_homeSel + 2) % HomeApps.Length;
            else _homeSel = (_homeSel + (previous ? -1 : 1) + HomeApps.Length) % HomeApps.Length;
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = previous || next;
        bool accept = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool edge = accept && !_zHeld; _zHeld = accept;
        if (edge && _t > 0.3)
        {
            if (_minaHomeFocus) OpenMinaCall();
            else OpenHomeApp(_homeSel);
        }
    }

    private void OpenHomeApp(int index)
    {
        if (_dived) return;
        if (!HomeAppUnlocked(index))
        {
            Audio.Instance?.PlayUiDeny();
            Toast($"{HomeApps[index]}は準備中", index == 1 ? "STAGE 1 クリアで利用可能" : "ステージクリアで利用可能", UiKit.Text3);
            return;
        }
        Audio.Instance?.PlayUiConfirm();
        _toastT = 0;
        _shopNudge = false;   // 自分で押せた＝誘導の役目は終わり（どのアプリを開いても降ろす）
        _phoneVisuals.Press(index);
        if (index == 0)
        {
            _footSel = -1;   // SNS は毎回カード側から始める
            _headFocus = false;
            _mode = Mode.SnsOpening;
            _snsOpeningT = 0;
            _cardsEnteredT = _t;
            if (NeedsSnsIntro)
            {
                _sel = DefaultSelection();
                _feedScroll = _feedScrollTarget = Mathf.Clamp(CardTop(_sel), 0f, FeedMaxScroll());
            }
            else
            {
                int unread = System.Array.FindIndex(_entries, entry => !entry.IsRepost && entry.Sort == Kind.Personal
                    && IsUnreadReturnPost(entry.Social!));
                _sel = unread >= 0 ? unread : _entries[0].IsRepost ? 0 : DefaultSelection();
                _feedScrollTarget = unread >= 0
                    ? Mathf.Clamp(CardTop(_sel), 0f, FeedMaxScroll()) : 0f;
                if (unread < 0) UpdateFeedScrollTarget();
                _feedScroll = _feedScrollTarget;
            }
            return;
        }
        if (index == 3)
        {
            _mode = Mode.Photos;
            _photoT = 0;
            _photoSel = Mathf.Clamp(_photoSel, 0, PhotoEntries.Length - 1);
            UpdatePhotoScrollTarget();
            _zHeld = _navHeld = _xHeld = true;
            return;
        }
        if (index == LimeAppIndex)
        {
            OpenLimeFriends();
            return;
        }
        _dived = true;
        if (index == 8)
        {
            PhoneAppTransition.Open(this, MusicApp.ScenePath, HomePhoneBounds());
            return;
        }
        if (index == 6)
        {
            PhoneAppTransition.Open(this, BossPracticeRoot.ScenePath, HomePhoneBounds());
            return;
        }
        if (index == 7)
        {
            PhoneAppTransition.Open(this, BossSelectRoot.ScenePath, HomePhoneBounds());
            return;
        }
        if (index == 5)
        {
            _game.PendingStageScene = BossRushRoot.ScenePath;
            PhoneAppTransition.Open(this, "res://DiffSelect.tscn", HomePhoneBounds());
            return;
        }
        PhoneAppTransition.Open(this, index == 1 ? "res://Shop.tscn" : index == 4 ? "res://Customize.tscn" : "res://Records.tscn", HomePhoneBounds());
    }

    private void GoHome()
    {
        Audio.Instance?.PlayUiCancel();
        _mode = Mode.Home;
        _homeSel = 0;
        _zHeld = _navHeld = true;
        _toastT = 0;
    }

    private void ProcessSnsOpening(double delta)
    {
        _snsOpeningT += delta;
        if (_snsOpeningT < SnsOpenDuration + (NeedsSnsIntro ? 0.5 : 0)) return;
        _mode = Mode.Cards;
        _zHeld = Pad.AdvanceHeld();
        _navHeld = _xHeld = true;
        if (NeedsSnsIntro)
        {
            StartHubConversation(SnsIntro, noPost: true, returnMode: Mode.Cards, seenKey: SnsIntroSeenKey,
                faces: ["", "", "", CompanionDialogue.MinaWorriedPortrait, "", CompanionDialogue.MinaSmilePortrait, "", CompanionDialogue.MinaSmilePortrait]);
        }
        else if (_idleTalkPending)
        {
            _idleTalkPending = false;
            TryStartIdleSmallTalk();
        }
    }

    private void DrawSnsOpening()
    {
        float t = Mathf.Clamp((float)(_snsOpeningT / SnsOpenDuration), 0f, 1f);
        float expansion = Mathf.Clamp(t / 0.7f, 0f, 1f);
        float k = 1f - Mathf.Pow(1f - expansion, 3f);
        var icon = HomeAppIconRect(0);
        var panel = new Rect2(icon.Position.Lerp(new Vector2(PhoneX, 0), k), icon.Size.Lerp(new Vector2(PhoneW, H), k));
        if (expansion >= 1f)
        {
            _drawCanvas.DrawRect(new Rect2(PhoneX, 0, PhoneW, H), PhoneBg);
            DrawTimeline(1f);
            DrawFooter();
        }
        float cover = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - 0.7f) / 0.3f, 0f, 1f));
        UiKit.Box(_drawCanvas, panel, new Color(new Color("142930"), cover), 24f * (1f - k),
            new Color(new Color("82d8dc"), cover * .5f), 1.2f);
        _phoneVisuals.DrawSnsLaunch(_drawCanvas, panel, t);
    }

    private readonly record struct PhotoEntry(string Id, string Title, string Sub, string Path, Vector4 Region, Color Accent, string[] Keys);
    private static readonly Vector4 PhotoFull = new(0, 0, 1, 1);
    private static Vector4 StoryRegion(int shot, int rows) => new((shot % 2) / 2f, (shot / 2) / (float)rows, 0.5f, 1f / rows);
    private static string[] K(params string[] keys) => keys;
    // 写真はキャラ単位なので、どの面でもそのキャラの回想／アフターを一度見れば解禁する。
    private static string[] PlayableKeys(string id, bool aftermath) => new[]
    {
        $"charstory_{id}_{(aftermath ? "aftermath" : "memory")}",
    };
    private static string[] MinaPhaseKeys(int phase) => new[]
    {
        $"mina_phase_{phase}_mina", $"mina_phase_{phase}_akari", $"mina_phase_{phase}_koharu", $"mina_phase_{phase}_rei",
    };

    private static readonly PhotoEntry[] PhotoEntries =
    {
        new("opening", "はじまりの光", "Opening", "res://char/bg2/opening/op_mina_v1.png", PhotoFull, new Color("87d7ed"), K("opening")),
        new("akari_memory", "あかり / 回想", "Memory Log", "res://char/v3/akari_story_atlas.png", StoryRegion(3, 3), new Color("f0c969"), K("akari_memory")),
        new("akari_after", "あかり / その後", "After Scene", "res://char/v3/akari_story_atlas.png", StoryRegion(5, 3), new Color("f0c969"), K("akari_aftermath")),
        new("koharu_memory", "こはる / 回想", "Memory Log", "res://char/v3/koharu_story_atlas_v2.png", StoryRegion(4, 4), new Color("a6dac8"), K("koharu_memory")),
        new("koharu_after", "こはる / その後", "After Scene", "res://char/v3/koharu_story_atlas_v2.png", StoryRegion(7, 4), new Color("a6dac8"), K("koharu_aftermath")),
        new("rei_memory", "レイ / 回想", "Memory Log", "res://char/v3/rei_story_atlas_v2.png", StoryRegion(4, 4), new Color("de91b9"), K("rei_memory")),
        new("rei_after", "レイ / その後", "After Scene", "res://char/v3/rei_story_atlas_v2.png", StoryRegion(6, 4), new Color("de91b9"), K("rei_aftermath")),
        new("mina_memory", "ミナ / 回想", "Memory Log", MinaStoryFilm.PhoneDraftsArt, PhotoFull, new Color("87d7ed"), K("mina_memory")),
        new("mina_after", "ミナ / 届いた声", "After Scene", "res://char/bg2/ending/cg_ep_mina_screen_goodnight_v1.png", PhotoFull, new Color("87d7ed"), K("mina_aftermath")),
        new("akari_playable_memory", "あかり / もう一度", "Playable Memory", "res://char/bg2/story/cg_akari_playable_memory_v1.png", PhotoFull, new Color("f0c969"), PlayableKeys("akari", aftermath: false)),
        new("akari_playable_after", "あかり / 帰還", "Playable After", "res://char/bg2/story/cg_akari_playable_aftermath_v2.png", PhotoFull, new Color("f0c969"), PlayableKeys("akari", aftermath: true)),
        new("koharu_playable_memory", "こはる / もう一度", "Playable Memory", "res://char/bg2/story/cg_koharu_playable_memory_v1.png", PhotoFull, new Color("a6dac8"), PlayableKeys("koharu", aftermath: false)),
        new("koharu_playable_after", "こはる / 帰還", "Playable After", "res://char/bg2/story/cg_koharu_playable_aftermath_v2.png", PhotoFull, new Color("a6dac8"), PlayableKeys("koharu", aftermath: true)),
        new("rei_playable_memory", "レイ / もう一度", "Playable Memory", "res://char/bg2/story/cg_rei_playable_memory_v1.png", PhotoFull, new Color("de91b9"), PlayableKeys("rei", aftermath: false)),
        new("rei_playable_after", "レイ / 帰還", "Playable After", "res://char/bg2/ending/cg_ep_rei_v2.png", PhotoFull, new Color("de91b9"), PlayableKeys("rei", aftermath: true)),
        new("mina_phase_rain", "未送信の雨", "Mina Phase", BossMina.PhaseBackground(1), PhotoFull, new Color("74b8e8"), MinaPhaseKeys(1)),
        new("mina_phase_clap", "消えない拍手", "Mina Phase", BossMina.PhaseBackground(2), PhotoFull, new Color("ee9bb7"), MinaPhaseKeys(2)),
        new("mina_phase_mask", "仮面の向こう", "Mina Phase", BossMina.PhaseBackground(3), PhotoFull, new Color("f0d98a"), MinaPhaseKeys(3)),
        new("mina_phase_voice", "わたしの声", "Mina Phase", BossMina.PhaseBackground(4), PhotoFull, new Color("85e8d0"), MinaPhaseKeys(4)),
        new("ending", "覚えている声", "Ending", "res://char/bg2/ending/cg_ep_mina_screen_v1.png", PhotoFull, new Color("ffd98a"), K("ending")),
    };

    private const int PhotoIdBase = 23000, PhotoCloseId = 23900;
    private const float PhotoGridTop = 326f, PhotoGridBottom = 644f, PhotoItemW = 204f, PhotoItemH = 112f, PhotoGap = 12f;

    private Rect2 PhotoCloseRect() => new(PhoneX + 16f, 18f, 38f, 38f);
    private Rect2 PhotoItemRect(int index)
    {
        int col = index % 2, row = index / 2;
        return new Rect2(PhoneX + 24f + col * (PhotoItemW + PhotoGap),
            PhotoGridTop + row * (PhotoItemH + PhotoGap) - _photoScroll, PhotoItemW, PhotoItemH);
    }

    private bool PhotoAcquired(PhotoEntry entry)
    {
        if (_previewState is "all" or "final") return true;
        foreach (string key in entry.Keys)
            if (FilmSkip.Seen(_game, key)) return true;
        return false;
    }

    private int WallpaperPhotoIndex()
    {
        string id = _game?.PhoneWallpaperPhotoId ?? "";
        if (id.Length == 0) return -1;
        for (int i = 0; i < PhotoEntries.Length; i++)
            if (PhotoEntries[i].Id == id && PhotoAcquired(PhotoEntries[i])) return i;
        return -1;
    }

    private bool IsWallpaper(PhotoEntry entry) => _game?.PhoneWallpaperPhotoId == entry.Id && PhotoAcquired(entry);

    private void SetPhotoWallpaper(int index)
    {
        index = Mathf.Clamp(index, 0, PhotoEntries.Length - 1);
        var entry = PhotoEntries[index];
        if (!PhotoAcquired(entry))
        {
            Audio.Instance?.PlayUiDeny();
            Toast("まだ背景にできません", "シーン取得後に設定できます", UiKit.Text3);
            return;
        }
        _game?.SetPhoneWallpaperPhoto(entry.Id);
        Audio.Instance?.PlayUiConfirm();
        Toast("背景を変更しました", entry.Title, entry.Accent);
    }

    private int PhotoAcquiredCount()
    {
        int count = 0;
        foreach (var entry in PhotoEntries) if (PhotoAcquired(entry)) count++;
        return count;
    }

    private Texture2D? PhotoTexture(PhotoEntry entry)
    {
        if (_photoTextures.TryGetValue(entry.Path, out var tex)) return tex;
        if (!ResourceLoader.Exists(entry.Path)) return null;
        tex = ResourceLoader.Load<Texture2D>(entry.Path);
        if (tex != null) _photoTextures[entry.Path] = tex;
        return tex;
    }

    private static Rect2 PhotoSource(Texture2D tex, Vector4 region, Vector2 destSize)
    {
        var src = new Rect2(region.X * tex.GetWidth(), region.Y * tex.GetHeight(),
            region.Z * tex.GetWidth(), region.W * tex.GetHeight());
        float srcAspect = src.Size.X / src.Size.Y;
        float destAspect = destSize.X / destSize.Y;
        if (srcAspect > destAspect)
        {
            float w = src.Size.Y * destAspect;
            src.Position += new Vector2((src.Size.X - w) * 0.5f, 0);
            src.Size = new Vector2(w, src.Size.Y);
        }
        else
        {
            float h = src.Size.X / destAspect;
            src.Position += new Vector2(0, (src.Size.Y - h) * 0.5f);
            src.Size = new Vector2(src.Size.X, h);
        }
        return src;
    }

    private float PhotoMaxScroll()
    {
        int rows = (PhotoEntries.Length + 1) / 2;
        float content = rows * PhotoItemH + Mathf.Max(0, rows - 1) * PhotoGap;
        return Mathf.Max(0f, content - (PhotoGridBottom - PhotoGridTop));
    }

    private void UpdatePhotoScrollTarget()
    {
        var rect = PhotoItemRect(_photoSel);
        float y0 = rect.Position.Y + _photoScroll, y1 = y0 + rect.Size.Y;
        const float margin = 16f;
        if (y0 - margin < PhotoGridTop + _photoScrollTarget) _photoScrollTarget = y0 - margin - PhotoGridTop;
        else if (y1 + margin > PhotoGridBottom + _photoScrollTarget) _photoScrollTarget = y1 + margin - PhotoGridBottom;
        _photoScrollTarget = Mathf.Clamp(_photoScrollTarget, 0f, PhotoMaxScroll());
    }

    private void ProcessPhotos(double delta)
    {
        _photoT += delta;
        _photoScroll = Mathf.Lerp(_photoScroll, _photoScrollTarget, Mathf.Min(1f, (float)delta * 12f));
        UiKit.BeginHotspots(Pad.MousePos());
        UiKit.Hotspot(PhotoCloseRect(), PhotoCloseId);
        for (int i = 0; i < PhotoEntries.Length; i++)
        {
            var r = PhotoItemRect(i);
            if (r.End.Y >= PhotoGridTop && r.Position.Y <= PhotoGridBottom) UiKit.Hotspot(r, PhotoIdBase + i);
        }
        int hov = UiKit.HoveredId();
        if (Pad.UsingMouse && hov >= PhotoIdBase && hov < PhotoIdBase + PhotoEntries.Length && hov - PhotoIdBase != _photoSel)
        {
            _photoSel = hov - PhotoIdBase;
            UpdatePhotoScrollTarget();
            Audio.Instance?.PlayUiMove();
        }
        int clk = UiKit.ClickedId(Pad.MouseClick());
        if (clk == PhotoCloseId && _photoT > 0.12) { GoHome(); return; }
        if (clk >= PhotoIdBase && clk < PhotoIdBase + PhotoEntries.Length && _photoT > 0.12)
        {
            _photoSel = clk - PhotoIdBase;
            UpdatePhotoScrollTarget();
            SetPhotoWallpaper(_photoSel);
        }

        int move = 0;
        if ((Input.IsActionPressed("ui_left") || Input.IsActionPressed("ui_up")) && !_navHeld)
            move = Input.IsActionPressed("ui_up") ? -2 : -1;
        else if ((Input.IsActionPressed("ui_right") || Input.IsActionPressed("ui_down")) && !_navHeld)
            move = Input.IsActionPressed("ui_down") ? 2 : 1;
        _navHeld = Input.IsActionPressed("ui_left") || Input.IsActionPressed("ui_up")
            || Input.IsActionPressed("ui_right") || Input.IsActionPressed("ui_down");
        if (move != 0)
        {
            int next = Mathf.Clamp(_photoSel + move, 0, PhotoEntries.Length - 1);
            if (next != _photoSel)
            {
                _photoSel = next;
                UpdatePhotoScrollTarget();
                Audio.Instance?.PlayUiMove();
            }
        }
        float wheel = Pad.WheelDelta();
        if (wheel != 0f && PhotoMaxScroll() > 0f)
            _photoScrollTarget = Mathf.Clamp(_photoScrollTarget - wheel * WheelStep, 0f, PhotoMaxScroll());

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _photoT > 0.12) SetPhotoWallpaper(_photoSel);

        // もどる＝X／Esc／パッドB（Esc は 2026-09-26 に「一つ前の画面へ」として復帰。メニューは M）。
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_xHeld; _xHeld = back;
        if (backEdge && _photoT > 0.12) GoHome();
    }

    private void DrawPhotoImage(Rect2 rect, PhotoEntry entry, bool acquired, float alpha)
    {
        if (acquired && PhotoTexture(entry) is Texture2D tex)
        {
            _drawCanvas.DrawTextureRectRegion(tex, rect, PhotoSource(tex, entry.Region, rect.Size), new Color(1, 1, 1, alpha));
            _drawCanvas.DrawRect(rect, new Color(0, 0, 0, 0.08f * alpha));
            return;
        }
        UiKit.Box(_drawCanvas, rect, new Color(0.08f, 0.09f, 0.11f, 0.96f * alpha), 6f);
        for (int i = -2; i < 8; i++)
        {
            float x = rect.Position.X + i * 42f;
            _drawCanvas.DrawLine(new Vector2(x, rect.Position.Y + rect.Size.Y), new Vector2(x + 92f, rect.Position.Y),
                new Color(1, 1, 1, 0.045f * alpha), 1f);
        }
        Vector2 c = rect.GetCenter();
        _drawCanvas.DrawArc(c + new Vector2(0, -5f), 13f, Mathf.Pi, Mathf.Tau, 24, new Color(UiKit.Text3, 0.58f * alpha), 2f, true);
        UiKit.Box(_drawCanvas, new Rect2(c - new Vector2(15f, 2f), new Vector2(30f, 20f)), new Color(UiKit.Text3, 0.58f * alpha), 4f);
    }

    private void DrawPhotoCard(int index, float alpha)
    {
        var entry = PhotoEntries[index];
        var rect = PhotoItemRect(index);
        if (rect.End.Y < PhotoGridTop || rect.Position.Y > PhotoGridBottom) return;
        bool acquired = PhotoAcquired(entry), selected = index == _photoSel, wallpaper = IsWallpaper(entry);
        if (selected) UiKit.Box(_drawCanvas, rect.Grow(4f), new Color(entry.Accent, 0.12f * alpha), 7f, new Color(entry.Accent, 0.72f * alpha), 1.5f);
        UiKit.Box(_drawCanvas, rect, new Color(0.03f, 0.035f, 0.046f, 0.95f * alpha), 6f,
            new Color(acquired ? entry.Accent : UiKit.Text4, (selected ? 0.44f : 0.2f) * alpha), 1f);
        var shot = new Rect2(rect.Position + new Vector2(8f, 8f), new Vector2(rect.Size.X - 16f, 64f));
        DrawPhotoImage(shot, entry, acquired, alpha);
        if (wallpaper)
        {
            var badge = new Rect2(shot.End.X - 50f, shot.Position.Y + 6f, 42f, 18f);
            UiKit.Box(_drawCanvas, badge, new Color(entry.Accent, 0.92f * alpha), 4f);
            UiKit.Text(_drawCanvas, UiKit.ZenBold, badge.Position + new Vector2(0, 2f), "背景", 11,
                new Color(0.02f, 0.025f, 0.03f, alpha), HorizontalAlignment.Center, badge.Size.X);
        }
        UiKit.Text(_drawCanvas, UiKit.Mono, rect.Position + new Vector2(12f, 79f), acquired ? entry.Sub : "LOCKED", 11,
            new Color(acquired ? entry.Accent : UiKit.Text4, alpha));
        UiKit.Text(_drawCanvas, UiKit.ZenBold, rect.Position + new Vector2(12f, 94f), acquired ? entry.Title : "未取得のシーン", 13,
            new Color(acquired ? UiKit.White : UiKit.Text3, alpha), HorizontalAlignment.Left, rect.Size.X - 24f);
    }

    private void DrawPhotos()
    {
        float a = Mathf.Clamp((float)_photoT / 0.18f, 0f, 1f);
        _drawCanvas.DrawRect(new Rect2(PhoneX, 0, PhoneW, H), PhoneBg);
        // グリッドはヘッダ／ヒーロー枠より先に描き、上側を PhoneBg で塗り潰してから重ねる＝スクロールで
        //   PhotoGridTop より上へ出たカードがヒーロー枠に被らない（2026-09-23 ユーザー報告：未取得のヒーロー枠に
        //   「はじまりの光」等の行が重なって見えていた。DrawPhotoCard の早期 return は完全に外れたカードしか弾かない）。
        for (int i = 0; i < PhotoEntries.Length; i++) DrawPhotoCard(i, a);
        _drawCanvas.DrawRect(new Rect2(PhoneX, 0, PhoneW, PhotoGridTop), PhoneBg);
        DrawBackButton(PhotoCloseRect(), PhotoCloseId, a);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(PhoneX + 66f, 25f), "写真", 22, new Color(UiKit.White, a));
        UiKit.Text(_drawCanvas, UiKit.Mono, new Vector2(PhoneX + PhoneW - 154f, 29f), $"{PhotoAcquiredCount()}/{PhotoEntries.Length}", 18,
            new Color(UiKit.Gold, a), HorizontalAlignment.Right, 128f);
        _drawCanvas.DrawRect(new Rect2(PhoneX + 24f, 68f, PhoneW - 48f, 1f), new Color(1, 1, 1, 0.10f * a));

        var entry = PhotoEntries[Mathf.Clamp(_photoSel, 0, PhotoEntries.Length - 1)];
        bool acquired = PhotoAcquired(entry);
        var hero = new Rect2(PhoneX + 24f, 88f, PhoneW - 48f, 208f);
        UiKit.Box(_drawCanvas, hero, new Color(0.03f, 0.035f, 0.046f, 0.95f * a), 8f, new Color(entry.Accent, 0.36f * a), 1f);
        var image = new Rect2(hero.Position + new Vector2(10f, 10f), new Vector2(hero.Size.X - 20f, 142f));
        DrawPhotoImage(image, entry, acquired, a);
        _drawCanvas.DrawRect(new Rect2(image.Position.X, image.End.Y - 38f, image.Size.X, 38f), new Color(0, 0, 0, 0.38f * a));
        if (acquired)
        {
            bool wallpaper = IsWallpaper(entry);
            float bw = wallpaper ? 112f : 126f;
            var badge = new Rect2(image.End.X - bw - 8f, image.Position.Y + 8f, bw, 26f);
            UiKit.Box(_drawCanvas, badge, new Color(wallpaper ? entry.Accent : PhoneBg, 0.90f * a), 6f,
                new Color(entry.Accent, 0.55f * a), 1f);
            UiKit.Text(_drawCanvas, UiKit.ZenBold, badge.Position + new Vector2(0, 5f), wallpaper ? "現在の背景" : "背景にする", 12,
                new Color(wallpaper ? new Color(0.02f, 0.025f, 0.03f) : UiKit.White, a), HorizontalAlignment.Center, badge.Size.X);
        }
        UiKit.Text(_drawCanvas, UiKit.Mono, hero.Position + new Vector2(20f, 158f), acquired ? entry.Sub : "LOCKED", 12,
            new Color(acquired ? entry.Accent : UiKit.Text4, a));
        UiKit.Text(_drawCanvas, UiKit.ZenBold, hero.Position + new Vector2(20f, 176f), acquired ? entry.Title : "まだ取得していないシーン", 18,
            new Color(acquired ? UiKit.White : UiKit.Text3, a));

        _drawCanvas.DrawRect(new Rect2(PhoneX, PhotoGridBottom, PhoneW, H - PhotoGridBottom), PhoneBg);
        DrawPhotoScrollHint(a);
        // キーの案内（旧「Z 背景にする　X もどる」の行）は右下のヒント帯（HintItems の Mode.Photos）へ移した。
        UiKit.Box(_drawCanvas, new Rect2(PhoneX + (PhoneW - 116f) / 2f, H - 18f, 116f, 3f), new Color(UiKit.White, 0.65f * a), 1.5f);
    }

    private void DrawPhotoScrollHint(float alpha)
    {
        float max = PhotoMaxScroll();
        if (max <= 1f) return;
        const float viewH = PhotoGridBottom - PhotoGridTop;
        float th = Mathf.Max(34f, viewH * viewH / (viewH + max));
        float ty = PhotoGridTop + (viewH - th) * Mathf.Clamp(_photoScroll / max, 0f, 1f);
        _drawCanvas.DrawRect(new Rect2(PhoneX + PhoneW - 8f, PhotoGridTop, 2f, viewH), new Color(1, 1, 1, 0.08f * alpha));
        _drawCanvas.DrawRect(new Rect2(PhoneX + PhoneW - 8f, ty, 2f, th), new Color(UiKit.Info, 0.58f * alpha));
    }

    private bool DrawSelectedPhotoWallpaper()
    {
        int index = WallpaperPhotoIndex();
        if (index < 0) return false;
        var entry = PhotoEntries[index];
        if (PhotoTexture(entry) is not Texture2D tex) return false;
        var rect = new Rect2(PhoneX, 0, PhoneW, H);
        DrawHomeWallpaper(tex, PhotoSource(tex, entry.Region, rect.Size), new Color(0.65f, 0.68f, 0.74f));
        return true;
    }

    private void DrawHomeWallpaper(Texture2D texture, Rect2 source, Color tint)
    {
        const float radius = 38f;
        var points = new Vector2[36];
        var uv = new Vector2[36];
        var centers = new[] { new Vector2(PhoneX + PhoneW - radius, radius),
            new Vector2(PhoneX + PhoneW - radius, H - radius),
            new Vector2(PhoneX + radius, H - radius), new Vector2(PhoneX + radius, radius) };
        for (int corner = 0; corner < 4; corner++)
            for (int step = 0; step <= 8; step++)
            {
                int i = corner * 9 + step;
                float angle = (corner - 1 + step / 8f) * Mathf.Pi / 2;
                points[i] = centers[corner] + Vector2.FromAngle(angle) * radius;
                uv[i] = (source.Position + (points[i] - new Vector2(PhoneX, 0)) / new Vector2(PhoneW, H) * source.Size) / texture.GetSize();
            }
        _drawCanvas.DrawPolygon(points, new[] { tint }, uv, texture);
    }

    private static Vector2[] HomeRoundOutline(Rect2 rect, float radius)
    {
        var points = new Vector2[69];
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 center = new(corner < 2 ? rect.End.X - radius : rect.Position.X + radius,
                corner is 1 or 2 ? rect.End.Y - radius : rect.Position.Y + radius);
            for (int step = 0; step <= 16; step++)
                points[corner * 17 + step] = center + Vector2.FromAngle((corner - 1 + step / 16f) * Mathf.Pi / 2) * radius;
        }
        points[^1] = points[0];
        return points;
    }

    private void DrawHome()
    {
        if (!DrawSelectedPhotoWallpaper() && _nightBg?.Texture is Texture2D wallpaper)
        {
            float sourceW = wallpaper.GetHeight() * PhoneW / H;
            var source = new Rect2((wallpaper.GetWidth() - sourceW) / 2f, 0, sourceW, wallpaper.GetHeight());
            DrawHomeWallpaper(wallpaper, source, new Color(0.77f, 0.84f, 0.92f));
        }
        UiKit.Box(_drawCanvas, new Rect2(PhoneX, 0, PhoneW, H), new Color(0.02f, 0.03f, 0.06f, 0.20f), 38);
        UiKit.Box(_drawCanvas, new Rect2(PhoneX + 182, 16, 116, 29), new Color("070b12"), 15);
        _drawCanvas.DrawCircle(new Vector2(PhoneX + 282, 30), 4, new Color("24354c"));
        _drawCanvas.DrawCircle(new Vector2(PhoneX + 281, 29), 1.3f, new Color("7194b5"));
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(PhoneX + 36, 23), "Refrain", 15, new Color("d9e5ed"));
        for (int bar = 0; bar < 4; bar++)
            UiKit.Box(_drawCanvas, new Rect2(PhoneX + 378 + bar * 6, 35 - bar * 3, 3, 4 + bar * 3), UiKit.White, 1);
        UiKit.Box(_drawCanvas, new Rect2(PhoneX + 414, 24, 25, 12), Colors.Transparent, 3, new Color("d9e5ed"), 1);
        UiKit.Box(_drawCanvas, new Rect2(PhoneX + 417, 27, 17, 6), new Color("d9e5ed"), 1);
        var now = System.DateTime.Now;
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(PhoneX, 76f), now.ToString("HH:mm"), 82, new Color("eff5f8"), HorizontalAlignment.Center, PhoneW);
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(PhoneX, 190f), now.ToString("M月d日（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")),
            18, UiKit.Text2, HorizontalAlignment.Center, PhoneW);
        DrawMinaHomeButton();
        for (int i = HomePage * 4; i < Mathf.Min(HomePage * 4 + 4, HomeApps.Length); i++)
        {
            var rect = HomeAppRect(i);
            var icon = HomeAppIconRect(i);
            bool unlocked = HomeAppUnlocked(i);
            float activation = _mode == Mode.HomeReveal && i == 1 ? Mathf.Clamp(((float)_homeRevealT - 1.15f) / 0.65f, 0f, 1f) : 1f;
            bool focused = _mode == Mode.Home && !_minaHomeFocus && (Pad.UsingMouse ? UiKit.HoveredId() == HomeAppIdBase + i : i == _homeSel);
            Color inactiveColor = new("707b8a");
            Color color = inactiveColor.Lerp(unlocked ? HomeAppColors[i] : inactiveColor, activation);
            if (focused)
            {
                UiKit.RadialGlow(_drawCanvas, icon.GetCenter(), 76, color, 0.17f);
                _drawCanvas.DrawPolyline(HomeRoundOutline(icon.Grow(6), 29), new Color(color.Lightened(0.3f), 0.65f), 1.2f, true);
            }
            // 初回だけの誘導（2026-09-22）：説明を読んだ直後、強化ショップのアイコンを脈動させる。
            //   外へ広がりながら薄くなる輪を2枚ずらして重ねる＝「押して」と言わずに押す場所を示す。
            //   ミナの台詞は足していない（説明の最終行が号令の役をもう持っている）。
            if (_shopNudge && i == 1 && _mode == Mode.Home)
            {
                for (int ring = 0; ring < 2; ring++)
                {
                    float phase = Mathf.PosMod((float)_t * 0.8f + ring * 0.5f, 1f);
                    _drawCanvas.DrawPolyline(HomeRoundOutline(icon.Grow(4f + phase * 16f), 27f + phase * 16f),
                        new Color(color, (1f - phase) * 0.55f), 1.5f, true);
                }
            }
            _phoneVisuals.DrawIcon(_drawCanvas, i, icon);
            if (i == 0 && UnreadReturnPostCount > 0)
            {
                Vector2 badge = icon.Position + new Vector2(icon.Size.X - 5f, 5f);
                _drawCanvas.DrawCircle(badge, 13f, PhoneBg);
                _drawCanvas.DrawCircle(badge, 11f, new Color("d87555"));
                UiKit.Text(_drawCanvas, UiKit.ZenBold, badge - new Vector2(11f, 10f), UnreadReturnPostCount.ToString(), 14,
                    UiKit.White, HorizontalAlignment.Center, 22f);
            }
            if (!unlocked || activation == 0f)
            {
                Vector2 p = icon.Position + new Vector2(73f, 75f);
                _drawCanvas.DrawCircle(p, 12f, PhoneBg);
                _drawCanvas.DrawArc(p + new Vector2(0, -2f), 4f, Mathf.Pi, Mathf.Tau, 12, UiKit.Text2, 1.5f, true);
                UiKit.Box(_drawCanvas, new Rect2(p - new Vector2(5f, 1f), new Vector2(10f, 8f)), UiKit.Text2, 2f);
            }
            UiKit.Text(_drawCanvas, UiKit.Zen, rect.Position + new Vector2(0, 112f),
                i == 0 && UnreadReturnPostCount > 0 ? "SNS ・ 新着" : HomeApps[i], 20,
                unlocked ? UiKit.White : UiKit.Text2, HorizontalAlignment.Center, rect.Size.X);
        }
        // 誘導の指示（初回のみ）。アイコン列の下に一行だけ。操作を名指しするのは UI の言葉で、
        //   ミナの台詞ではない＝語りの本数を増やさない。点滅は輪と同じ周期に合わせて散らかさない。
        if (_shopNudge && _mode == Mode.Home)
        {
            float blink = 0.65f + 0.35f * Mathf.Sin((float)_t * 4f);
            UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(PhoneX, 592f),
                Pad.UsingMouse ? "▲ クリックして ひらく" : "▲ Z で ひらく", 14,
                new Color(new Color("eba4b9"), blink), HorizontalAlignment.Center, PhoneW);
        }
        UiKit.Box(_drawCanvas, new Rect2(PhoneX + 72, 620, 336, 52), new Color(0.04f, 0.07f, 0.11f, 0.65f), 26);
        for (int page = 0; page < HomePageCount; page++)
        {
            var tab = HomePageRect(page);
            if (page == HomePage) UiKit.Box(_drawCanvas, tab, new Color(0.70f, 0.84f, 0.94f, 0.10f), 22);
            UiKit.Text(_drawCanvas, UiKit.Zen, tab.Position + new Vector2(0, 10), page switch { 0 => "ホーム", 1 => "その他", _ => "音楽・トーク" }, 17,
                page == HomePage ? UiKit.White : new Color("9faebd"), HorizontalAlignment.Center, tab.Size.X);
        }
        _drawCanvas.DrawLine(new Vector2(W / 2 - 58, H - 17), new Vector2(W / 2 + 58, H - 17), new Color(UiKit.White, 0.65f), 3, true);
        _drawCanvas.DrawPolygon(new[] { new Vector2(PhoneX + 48, 2), new Vector2(PhoneX + 200, 2),
            new Vector2(PhoneX + 80, H - 2), new Vector2(PhoneX + 40, H - 2) },
            new[] { Colors.Transparent, new Color(0.8f, 0.92f, 1, 0.06f), new Color(0.8f, 0.92f, 1, 0.012f), Colors.Transparent });
        _drawCanvas.DrawPolygon(new[] { new Vector2(PhoneX + 200, 2), new Vector2(PhoneX + 390, 2),
            new Vector2(PhoneX + 220, H - 2), new Vector2(PhoneX + 80, H - 2) },
            new[] { new Color(0.8f, 0.92f, 1, 0.06f), Colors.Transparent, Colors.Transparent, new Color(0.8f, 0.92f, 1, 0.012f) });
    }

    // ───────── カード ─────────
    // 返信できるのは「声のあるクリア済みカード」だけ（埋め草・固定ポストには返信しない）。
    private bool CanReplySel() => !_autoplay && IsVoice(_sel)
        && !_entries[_sel].IsFinal && _entries[_sel].Cleared && !(_game?.HasReplied(_entries[_sel].Id) ?? true);

    private Rect2 CardHitRect(int i)
    {
        return new Rect2(PhoneX, FeedTop + CardTop(i) - _feedScroll, PhoneW, CardHeight(_entries[i]))
            .Intersection(new Rect2(PhoneX, FeedTop, PhoneW, FeedBottom - FeedTop));
    }

    // ───────── 投稿詳細（Detail）のマウス当たり判定 ─────────
    //   寸法は DrawDetail と同じ式をここに写して共有する（cw/ch/cx/cy、潜り方の ty/th/tg、フッタの y）。
    //   展開アニメの浮き（(1-k)*24）は無視して確定位置で判定＝開いた直後でもクリック位置が動かない。
    //   ホットスポット id は カード id と衝突しないよう TierIdBase / DetailCloseId の帯を使う。
    //   2026-09-17: 「潜る」ボタン（DetailConfirmId）を廃止した＝段をクリック/決定した時点で潜る。
    private const int TierIdBase = 20000, DetailCloseId = 20900, JobOpenId = 20901, FinalDiveId = 20902;

    // 段（Tier）の1段目の上端（DetailBox の cy からの相対）。見出し「潜り方」を消したぶん詰めた。
    private const float TierTop = 320f;

    private Rect2 HeaderJobRect() => new(PhoneX + 16f, 8f, PhoneW - 32f, 52f);

    private Rect2 DetailJobRect(bool tiers)
    {
        var (cx, cy, cw, ch) = DetailBox(tiers);
        return new Rect2(cx + cw - 188f, cy + 12f, 164f, 40f);
    }

    // FINAL has no difficulty rows, so it needs its own mouse entry below the account hint.
    private (float cx, float cy, float cw, float ch) DetailBox(bool tiers)
    {
        float ch = tiers ? TierTop + 3f * 68f + 62f + 16f : 512f;
        return (PhoneX, H - 16f - ch, PhoneW, ch);
    }

    // 潜り方 i 段目の矩形（DrawDetail の DrawTier 呼び出しと同じ x/y/w/h）。
    private Rect2 TierHitRect(int i)
    {
        var (cx, cy, cw, _) = DetailBox(true);
        return new Rect2(cx + 24f, cy + TierTop + i * 68f, cw - 48f, 62f);
    }

    private Rect2 DetailCloseRect(bool tiers)
    {
        var (cx, cy, _, _) = DetailBox(tiers);
        return new Rect2(cx + 12f, cy + 12f, 40f, 40f);
    }

    private Rect2 FinalDiveRect()
    {
        var (cx, cy, cw, ch) = DetailBox(false);
        return new Rect2(cx + 24f, cy + ch - 64f, cw - 48f, 48f);
    }

    private SnsVoices.Post? _socialPost;
    private int _socialReturnFocus = -1;
    private string[][] _socialLines = System.Array.Empty<string[]>();
    private float[] _socialHeights = System.Array.Empty<float>();
    private float _socialScroll, _socialScrollTarget, _socialContentHeight;
    private const float SocialTop = 78f;
    private const int SocialBackId = 40000, SocialPreviousId = 40001, SocialNextId = 40002;
    private static Rect2 SocialBackRect => new(PhoneX + 12f, 16f, 40f, 40f);
    private static Rect2 SocialLinkRect(bool next) => new(PhoneX + (next ? 246f : 16f), FeedBottom + 14f, 218f, 42f);

    private SnsVoices.Post? RelatedSocialPost(bool next)
    {
        if (_socialPost == null) return null;
        foreach (var entry in _entries)
        {
            var post = entry.Social;
            if (post != null && (next ? post.PreviousId == _socialPost.Id : post.Id == _socialPost.PreviousId))
                return post;
        }
        return null;
    }

    private void OpenSocialThread(SnsVoices.Post post)
    {
        _socialReturnFocus = -1;
        if (IsUnreadReturnPost(post) && _previewState == null)
        {
            if (post == SnsVoices.AkariReturnPost)
                _socialReturnFocus = System.Array.FindIndex(_entries, IsNextKoharuPost);
            _game.MarkIdleDialogSeen(SnsVoices.ReturnPostReadKey(post));
            _game.AutoSave();
        }
        _socialPost = post;
        _mode = Mode.SocialThread;
        _socialScroll = _socialScrollTarget = 0;
        _mediaHeld = Pad.AdvanceHeld();
        _socialLines = new string[post.Replies.Length + 1][];
        _socialHeights = new float[_socialLines.Length];
        _socialContentHeight = 0;
        for (int i = 0; i < _socialLines.Length; i++)
        {
            bool root = i == 0;
            int size = root ? 18 : 16;
            string body = root ? post.Text : post.Replies[i - 1].Text;
            _socialLines[i] = UiKit.WrapLines(UiKit.Zen, body, size, PhoneW - (root ? 48f : 104f)).ToArray();
            _socialHeights[i] = (root ? 108f : 76f) + _socialLines[i].Length * (UiKit.Zen.GetHeight(size) + 3f);
            if (root && SnsPostMedia.Find(post.Id) != null) _socialHeights[i] += PostMediaHeight + PostMediaGap;
            _socialContentHeight += _socialHeights[i];
        }
        Audio.Instance?.PlayUiConfirm();
    }

    private void ProcessSocialThread(double delta)
    {
        UiKit.BeginHotspots(Pad.MousePos());
        UiKit.Hotspot(SocialBackRect, SocialBackId);
        if (RelatedSocialPost(false) != null) UiKit.Hotspot(SocialLinkRect(false), SocialPreviousId);
        if (RelatedSocialPost(true) != null) UiKit.Hotspot(SocialLinkRect(true), SocialNextId);
        bool video = _socialPost != null && SnsPostMedia.Find(_socialPost.Id)?.IsVideo == true;
        if (video)
        {
            var visibleMedia = SocialMediaRect().Intersection(new Rect2(PhoneX, SocialTop, PhoneW, FeedBottom - SocialTop));
            if (visibleMedia.HasArea()) UiKit.Hotspot(visibleMedia, PostMediaIdBase);
        }
        int clicked = UiKit.ClickedId(Pad.MouseClick());
        bool mediaPress = Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Enter)
            || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        if (video && (clicked == PostMediaIdBase || mediaPress && !_mediaHeld)) _snsMedia.Toggle(_socialPost!.Id);
        _mediaHeld = mediaPress;
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_xHeld;
        _xHeld = back;
        if (backEdge || clicked == SocialBackId)
        {
            _mode = Mode.Cards;
            if (_socialReturnFocus >= 0)
            {
                _sel = _socialReturnFocus;
                UpdateFeedScrollTarget();
            }
            _zHeld = Pad.AdvanceHeld();
            Audio.Instance?.PlayUiCancel();
            return;
        }
        bool left = Input.IsActionPressed("ui_left"), right = Input.IsActionPressed("ui_right");
        bool previous = clicked == SocialPreviousId || (left && !_navHeld);
        bool next = clicked == SocialNextId || (right && !_navHeld);
        _navHeld = left || right;
        if (previous || next)
        {
            var related = RelatedSocialPost(next);
            if (related != null) OpenSocialThread(related);
        }
        float direction = (Input.IsActionPressed("ui_down") ? 1f : 0f) - (Input.IsActionPressed("ui_up") ? 1f : 0f);
        float max = Mathf.Max(0, _socialContentHeight - (FeedBottom - SocialTop));
        _socialScrollTarget = Mathf.Clamp(_socialScrollTarget + direction * 320f * (float)delta - Pad.WheelDelta() * WheelStep, 0, max);
        _socialScroll = Mathf.Lerp(_socialScroll, _socialScrollTarget, Mathf.Min(1, (float)delta * 14f));
    }

    private void DrawSocialThread()
    {
        if (_socialPost == null) return;
        float y = SocialTop - _socialScroll;
        for (int i = 0; i < _socialLines.Length; i++)
        {
            bool root = i == 0;
            int authorId = root ? _socialPost.Author : _socialPost.Replies[i - 1].Author;
            bool personal = root && _socialPost.CharacterId.Length > 0;
            string authorName = personal ? SnsVoices.AuthorName(_socialPost) : SnsVoices.All[authorId].Name;
            string authorHandle = personal ? SnsVoices.AuthorHandle(_socialPost) : SnsVoices.AccountHandle(authorId);
            float h = _socialHeights[i];
            if (y + h >= SocialTop && y < FeedBottom)
            {
                if (!root && i < _socialLines.Length - 1)
                    _drawCanvas.DrawLine(new Vector2(PhoneX + 43f, y + 52f), new Vector2(PhoneX + 43f, y + h + 14f), new Color(UiKit.Text4, 0.4f), 1.5f);
                if (personal)
                    UiKit.FaceAvatar(_drawCanvas, new Vector2(PhoneX + 43f, y + 33f), 23f,
                        _playerFaces[_socialPost.CharacterId], AccountColor(_socialPost.CharacterId), false);
                else DrawFillerAvatar(PhoneX + 43f, y + 33f, SnsVoices.All[authorId].Icon, 1f);
                UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(PhoneX + 80f, y + 10f),
                    FitText(UiKit.ZenBold, authorName, 16, PhoneW - 104f), 16, UiKit.White);
                if (personal)
                    UiKit.VerifiedBadge(_drawCanvas, new Vector2(PhoneX + 93f + UiKit.TextW(UiKit.ZenBold, authorName, 16), y + 22f),
                        6f, UiKit.Ok);
                UiKit.Text(_drawCanvas, UiKit.Mono, new Vector2(PhoneX + 80f, y + 35f),
                    authorHandle, 12, UiKit.Text3);
                float tx = PhoneX + (root ? 24f : 80f), ty = y + (root ? 76f : 60f);
                int size = root ? 18 : 16;
                foreach (string line in _socialLines[i])
                {
                    UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(tx, ty), line, size, UiKit.Text2);
                    ty += UiKit.Zen.GetHeight(size) + 3f;
                }
                if (root)
                {
                    if (SnsPostMedia.Find(_socialPost.Id) != null) _snsMedia.Draw(_drawCanvas, _socialPost.Id, SocialMediaRect(), 1);
                    var reach = DisplayReach(_socialPost.Id, _socialPost.Reach);
                    UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(PhoneX + 24f, y + h - 26f),
                        $"返信 {_socialPost.Replies.Length}　リポスト {reach.Reposts}　いいね {reach.Likes}　表示 {reach.Views}回", 12, UiKit.Text3);
                    _drawCanvas.DrawRect(new Rect2(PhoneX + 20f, y + h - 1f, PhoneW - 40f, 1f), new Color(UiKit.Text4, 0.3f));
                }
            }
            y += h;
        }
        _drawCanvas.DrawRect(new Rect2(PhoneX, 0, PhoneW, SocialTop), PhoneBg);
        _drawCanvas.DrawRect(new Rect2(PhoneX, FeedBottom, PhoneW, H - FeedBottom), PhoneBg);
        DrawBackButton(SocialBackRect, SocialBackId, 1f);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(PhoneX + 72f, 24f),
            _socialPost.Replies.Length > 0 ? "投稿と返信" : "投稿", 21, UiKit.White);
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(PhoneX + PhoneW - 100f, 31f),
            _socialPost.CharacterId.Length > 0 ? "Y / " + SnsVoices.AuthorName(_socialPost) : "Y / 日常", 12, UiKit.Text3);
        _drawCanvas.DrawRect(new Rect2(PhoneX, SocialTop - 1f, PhoneW, 1f), new Color(UiKit.Text4, 0.3f));
        for (int i = 0; i < 2 && _socialPost.CharacterId.Length == 0; i++)
        {
            bool next = i == 1;
            bool available = RelatedSocialPost(next) != null;
            var rect = SocialLinkRect(next);
            int id = next ? SocialNextId : SocialPreviousId;
            UiKit.Box(_drawCanvas, rect, available ? PhoneRaised : PhoneBg, 8f,
                new Color(UiKit.Text4, available ? 0.6f : 0.2f), 1f);
            string label = next ? "その後の投稿 ›" : "‹ 前の投稿";
            UiKit.Text(_drawCanvas, UiKit.ZenBold, rect.Position + new Vector2(0, 10f), label, 15,
                available ? (UiKit.HoveredId() == id ? UiKit.White : UiKit.Info) : UiKit.Text4,
                HorizontalAlignment.Center, rect.Size.X);
        }
        float view = FeedBottom - SocialTop;
        if (_socialContentHeight > view)
        {
            float thumb = view * view / _socialContentHeight;
            float top = SocialTop + (view - thumb) * _socialScroll / (_socialContentHeight - view);
            UiKit.Box(_drawCanvas, new Rect2(PhoneX + PhoneW - 4f, top, 2f, thumb), new Color(UiKit.Text3, 0.65f), 1f);
        }
    }

    private void ProcessCards()
    {
        if (_autoplay) { if (_t - _cardsEnteredT >= AutoDiveDelay) DiveAuto(); return; }
        // R（タイムラインの再読込）は 2026-09-27 に廃止（スマホの操作を 矢印＋Z＋Esc／X に絞った）。
        NormalizeCardBtn();

        float wheel = Pad.WheelDelta();
        if (wheel != 0f && FeedMaxScroll() > 0f)
        {
            _feedScrollTarget = Mathf.Clamp(_feedScrollTarget - wheel * WheelStep, 0f, FeedMaxScroll());
        }

        // マウス：フレーム頭でホットスポットをクリア＋カード矩形とフッタ操作を登録（カードモードのみ＝会話中は登録しない）。
        //   カード id = 0..entries-1／フッタ id = FooterIdBase+i（空間を分けて種別を判別する）。
        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < _entries.Length; i++)
        {
            var card = CardHitRect(i);
            if (!card.HasArea()) continue;
            UiKit.Hotspot(card, i);
            for (int b = RepostButton; b <= LikeButton; b++)
            {
                var rect = ReactionRect(i, b).Intersection(FeedRect);
                if (rect.HasArea()) UiKit.Hotspot(rect, ReactionHotspot(i, b));
            }
        }
        for (int i = 0; i < _entries.Length; i++)
            if (SnsPostMedia.Find(_entries[i].Id)?.IsVideo == true)
            {
                var mediaHit = CardMediaRect(i).Intersection(FeedRect);
                if (mediaHit.HasArea()) UiKit.Hotspot(mediaHit, PostMediaIdBase + i);
            }
        // 選択中の声のカードの [ダイブ] [返信]。カードより後に登録＝重なりはボタンが勝つ（後勝ち）。
        //   フィードの窓からはみ出たぶんは切る（CardHitRect と同じ考え方＝フッタ・ヘッダの上で拾わない）。
        if (CardButtonsShown(_sel))
            for (int b = 0; b < CardBtnCount(_entries[_sel]); b++)
            {
                var br = CardBtnRect(_sel, b).Intersection(FeedRect);
                if (br.HasArea()) UiKit.Hotspot(br, CardBtnIdBase + b);
            }
        var footItems = FooterItems();
        for (int i = 0; i < footItems.Count; i++)
            UiKit.Hotspot(FooterItemRect(i), FooterIdBase + i);
        UiKit.Hotspot(HeaderJobRect(), JobOpenId);
        int hov = UiKit.HoveredId();
        // ホバー追従はカード側のみ（フッタはボタン＝ホバーで選択を動かさない。下敷きは DrawFooter が hov で描く）。
        if (Pad.UsingMouse && hov >= 0 && hov < _entries.Length && (hov != _sel || _footSel >= 0 || _headFocus))
        {
            _sel = hov; _footSel = -1; _headFocus = false; Audio.Instance?.PlayUiMove();
        }
        int clk = UiKit.ClickedId(Pad.MouseClick());
        if (_footSel >= footItems.Count) _footSel = footItems.Count - 1;
        if (clk >= ReactionIdBase && clk < ReactionIdBase + 2 * _entries.Length)
        {
            int offset = clk - ReactionIdBase;
            ToggleReaction(offset / 2, RepostButton + offset % 2);
            return;
        }
        if (clk >= PostMediaIdBase && clk < PostMediaIdBase + _entries.Length)
        {
            _sel = clk - PostMediaIdBase;
            _headFocus = false;
            _footSel = -1;
            if (CardMediaRect(_sel).Intersection(FeedRect).Size.Y <= PostMediaHeight * .55f)
                UpdateFeedScrollTarget();
            _snsMedia.Toggle(_entries[_sel].Id);
            return;
        }
        if (clk == JobOpenId && _t > 0.3)
        {
            OpenJob();
            return;
        }
        // カード下部のボタンのクリック＝キーで選んで Z を押したのと同じ（PressCardButton）。
        if (clk == CardBtnIdBase || clk == CardBtnIdBase + 1)
        {
            if (_t > 0.3) PressCardButton(clk - CardBtnIdBase);
            return;
        }
        // フッタボタンのクリック → 対応アクション（強化=ショップ入口が主目的。返信/記録も同じ導線）。
        if (clk >= FooterIdBase)
        {
            int fi = clk - FooterIdBase;
            if (fi >= 0 && fi < footItems.Count) FooterClick(footItems[fi].act);
            return; // フッタを押したフレームはカード確定へ流さない
        }

        // ↑↓：カード送り。最後のカードで ↓ でフッタへ降りる（着地は「アカウント」）。先頭のカードで ↑ でヘッダーへ上がる。
        //   ←→：カード上では下部ボタン [ダイブ] [返信] の切り替え（2026-09-27。以前はフッタへ降りる操作だった）。
        //   フッタ上では ←→ で項目を巡り、↑ でカードへ戻る（カードの選択は降りる前のまま）。
        //   ヘッダー上では ↓ で先頭のカードへ戻る（←→ は何もしない＝項目は1つだけ）。
        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        bool left = Input.IsActionPressed("ui_left"), right = Input.IsActionPressed("ui_right");
        if ((up || down || left || right) && !_navHeld)
        {
            if (_headFocus)
            {
                if (down)
                {
                    _headFocus = false; _sel = 0;
                    UpdateFeedScrollTarget();
                    Audio.Instance?.PlayUiMove();
                }
            }
            else if (_footSel >= 0)
            {
                if (up) { _footSel = -1; Audio.Instance?.PlayUiMove(); }
                else if (left || right)
                {
                    _footSel = (_footSel + (left ? -1 : 1) + footItems.Count) % footItems.Count;
                    Audio.Instance?.PlayUiMove();
                }
            }
            else if (down && _sel >= _entries.Length - 1)
            {
                _footSel = System.Math.Max(0, footItems.FindIndex(f => f.act == FootAct.Job));
                Audio.Instance?.PlayUiMove();
            }
            else if (up && _sel <= 0)
            {
                _headFocus = true;
                _feedScrollTarget = 0f;   // ヘッダーの真下＝先頭のカードが見えている状態で上がる
                Audio.Instance?.PlayUiMove();
            }
            else if (up || down)
            {
                if (_entries.Length > 0)
                {
                    if (up) _sel = _sel - 1;
                    if (down) _sel = _sel + 1;
                    UpdateFeedScrollTarget();
                    Audio.Instance?.PlayUiMove();
                }
            }
            else if (_entries[_sel].Social != null && right && _cardBtn == 0)
            {
                _footSel = footItems.FindIndex(f => f.act == FootAct.Voice);
                Audio.Instance?.PlayUiMove();
            }
            else
            {
                int last = _entries[_sel].Sort == Kind.Pinned ? LikeButton : CanReplySel() ? 1 : 0;
                int next = Mathf.Clamp(_cardBtn + (left ? -1 : 1), RepostButton, last);
                if (_cardBtn != next) { _cardBtn = next; Audio.Instance?.PlayUiMove(); }
            }
        }
        _navHeld = up || down || left || right;
        NormalizeCardBtn();

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        // フッタにカーソルがあるときの Z＝その項目をクリックしたのと同じ（カードの確定へは流さない）。
        if (_footSel >= 0 && zEdge)
        {
            FooterClick(footItems[_footSel].act);
            return;
        }
        // ヘッダーにカーソルがあるときの Z＝ヘッダーをクリックしたのと同じ（フッタ「アカウント」と同じ OpenJob）。
        if (_headFocus && zEdge)
        {
            if (_t > 0.3 && !_dived) FooterClick(FootAct.Job);
            return;
        }
        // キーの Z はカーソルのあるボタンを押す。返信を選んでいれば返信、それ以外は下のダイブ経路へ。
        if (zEdge && _t > 0.3 && _cardBtn < 0)
        {
            ToggleReaction(_sel, _cardBtn);
            return;
        }
        if (zEdge && _t > 0.3 && CardButtonsShown(_sel) && _cardBtn == 1)
        {
            PressCardButton(1);
            return;
        }
        // マウス：カードクリックで選択＋ダイブ（KB の Z と同じ確定経路）。clk はカード id のみ（フッタは上で処理済み）。
        bool dive = zEdge && _t > 0.3;
        if (clk >= 0 && clk < _entries.Length && _t > 0.3) { _sel = clk; _headFocus = false; dive = true; }
        if (dive && _sel >= 0 && _sel < _entries.Length)
        {
            var e = _entries[_sel];
            // 2-b: Z でカードがその場で開く（投稿詳細）。潜れるのは声のある投稿だけ。
            if (e.Social != null) OpenSocialThread(e.Social);
            else if (e.Sort != Kind.Voice) Audio.Instance?.PlayUiDeny();
            else if (e.Unlocked) OpenDetail();
            else
            {
                // 未解放＝「ロック中」ではなく「まだ聞こえない」。拒否音の代わりにミナが一言だけ返す（2-a）。
                Audio.Instance?.PlayUiDeny();
                StartDialogue(NotYetDialog, null, noPost: true);
            }
        }

        // もどる（ホームへ）＝X／Esc／パッドB（Esc は 2026-09-26 に「一つ前の画面へ」として復帰。メニューは M）。
        //   2026-09-27：C（返信）・T（記録）・J／RB（アカウント）の直行キーは廃止。返信はカードの [返信]、
        //   記録はホームの記録アプリ、アカウントはフッタから開く（どれも矢印＋Z で届く）。
        bool x = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool xEdge = x && !_xHeld; _xHeld = x;
        if (xEdge && _t > 0.3 && !_dived) { GoHome(); return; }
    }

    // カード下部ボタンのカーソルを正す：カードの選択が変わったらダイブへ戻し、返信が使えなくなったら（返信を
    //   済ませた・声のないカードへ移った）ダイブへ落とす。
    private void NormalizeCardBtn()
    {
        if (_cardBtnFor != _sel) { _cardBtnFor = _sel; _cardBtn = _entries[_sel].Sort == Kind.Pinned ? LikeButton : 0; }
        if (_cardBtn == 1 && !CanReplySel()) _cardBtn = 0;
    }

    private const int RepostButton = -2, LikeButton = -1, ReactionIdBase = 54000;
    private static int ReactionHotspot(int index, int button) => ReactionIdBase + index * 2 + button - RepostButton;
    private Rect2 ReactionRectAt(int index, float bottom, int button)
    {
        var e = _entries[index];
        var reach = DisplayReach(e.Id, e.Reach);
        float x = PhoneX + 26 + 46 + UiKit.TextW(UiKit.Mono, UiKit.Abbrev(e.Replies), UiKit.FontLabel);
        if (button == LikeButton)
            x += 46 + UiKit.TextW(UiKit.Mono, UiKit.Abbrev(reach.Reposts), UiKit.FontLabel);
        int count = button == LikeButton ? reach.Likes : reach.Reposts;
        float width = 30 + UiKit.TextW(UiKit.Mono, UiKit.Abbrev(count), UiKit.FontLabel);
        return new Rect2(x - 5, bottom - 30, width, 24);
    }
    private Rect2 ReactionRect(int index, int button) =>
        ReactionRectAt(index, FeedTop + CardTop(index + 1) - _feedScroll, button);

    private void ToggleReaction(int index, int button)
    {
        if (_previewState != null) return;
        var entry = _entries[index];
        _headFocus = false;
        _footSel = -1;
        _sel = index;
        if (button == LikeButton) _game.TogglePostLike(entry.Id);
        else
        {
            _game.ToggleRepost(entry.Id);
            BuildEntries();
            _sel = _game.IsPostReposted(entry.Id) ? 0
                : System.Array.FindIndex(_entries, e => !e.IsRepost && e.Id == entry.Id);
            if (_sel < 0) _sel = Mathf.Min(index, _entries.Length - 1);
            _feedScroll = _feedScrollTarget = Mathf.Clamp(CardTop(_sel), 0, FeedMaxScroll());
            Pad.ReleaseMouseFollow();
        }
        _cardBtnFor = _sel;
        _cardBtn = button;
        Audio.Instance?.PlayUiConfirm();
    }

    // カード下部のボタンを押す（キーの Z とクリックの共通経路）。0＝ダイブ（投稿詳細＝難易度選択を開く）／1＝返信。
    private void PressCardButton(int b)
    {
        if (!CardButtonsShown(_sel)) return;
        if (b == 0) { OpenDetail(); return; }
        if (!CanReplySel()) { Audio.Instance?.PlayUiDeny(); return; }
        var lines = ReplyDialog(_entries[_sel].Id);
        if (lines.Length > 0) { Audio.Instance?.PlayUiConfirm(); StartDialogue(lines, _entries[_sel].Id); }
    }

    // フッタボタン（マウス／キーのカーソル）押下のアクション。
    private void FooterClick(FootAct act)
    {
        if (_t <= 0.3 || _dived) return;
        switch (act)
        {
            case FootAct.Reposts:
                _sel = 0;
                _headFocus = false;
                _footSel = -1;
                _feedScroll = _feedScrollTarget = 0;
                Audio.Instance?.PlayUiConfirm();
                break;
            case FootAct.Voice:
                _sel = DefaultSelection();
                _headFocus = false;
                _footSel = -1;
                UpdateFeedScrollTarget();
                Audio.Instance?.PlayUiConfirm();
                break;
            case FootAct.Home:
                GoHome();
                break;
            case FootAct.Job:
                OpenJob();
                break;
        }
    }

    // ───────── 2-b: 投稿詳細（カードがその場で開く）─────────
    // 旧 DiffSelect.tscn への遷移をここへ吸収した。既定の段は前回の難易度＝
    //   「カードで Z → そのまま Z」の 2 押しで潜れる導線を守る。
    private void OpenDetail()
    {
        Audio.Instance?.PlayUiConfirm();
        _mode = Mode.Detail;
        _detailT = 0;
        _tierSel = (int)(_game?.Difficulty ?? GameManager.Diff.Normal);
        if (!TierOpen(_tierSel)) _tierSel = (int)GameManager.Diff.Hard;
    }

    private void ProcessDetail(double delta)
    {
        _detailT += delta;
        var e = _entries[Mathf.Clamp(_sel, 0, _entries.Length - 1)];
        bool tiers = !NeedsFinalAccount;

        // マウス：潜り方の段と「とじる」を登録（DiffSelect / Shop と同じ作法）。
        //   ホバーで選択が移るのは解放済みの段だけ＝未解放（底まで）にはカーソルを乗せない。
        //   クリックは下の zEdge / backEdge と同じ確定経路へ合流させる。
        UiKit.BeginHotspots(Pad.MousePos());
        if (tiers) for (int i = 0; i < Tiers.Length; i++) UiKit.Hotspot(TierHitRect(i), TierIdBase + i);
        UiKit.Hotspot(DetailCloseRect(tiers), DetailCloseId);
        UiKit.Hotspot(DetailJobRect(tiers), JobOpenId);
        if (!tiers) UiKit.Hotspot(FinalDiveRect(), FinalDiveId);
        int dhov = UiKit.HoveredId();
        if (Pad.UsingMouse && dhov >= TierIdBase && dhov < TierIdBase + Tiers.Length)
        {
            int hi = dhov - TierIdBase;
            if (TierOpen(hi) && hi != _tierSel) { _tierSel = hi; Audio.Instance?.PlayUiMove(); }
        }
        int dclk = UiKit.ClickedId(Pad.MouseClick());
        // アカウント切り替えはここではクリックだけ（J／RB の直行は 2026-09-27 に廃止。キーではフッタから開く）。
        if (dclk == JobOpenId)
        {
            OpenJob();
            return;
        }

        if (tiers)
        {
            bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
            if ((up || down) && !_navHeld)
            {
                int dir = up ? -1 : 1;
                // 解放されていない段（底まで）は飛ばす＝カーソルが止まって「押せない」を作らない。
                for (int k = 0; k < Tiers.Length; k++)
                {
                    _tierSel = (_tierSel + dir + Tiers.Length) % Tiers.Length;
                    if (TierOpen(_tierSel)) break;
                }
                Audio.Instance?.PlayUiMove();
            }
            _navHeld = up || down;
        }

        // マウス：「とじる」クリック＝X と同じ（カード一覧へ戻る）。ここで消費して潜る側へ流さない。
        if (dclk == DetailCloseId)
        {
            Audio.Instance?.PlayUiCancel(); _mode = Mode.Cards; _xHeld = true;
            return;
        }

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        // マウス：段のクリックで即ダイブ（＝Z と同じ確定経路）。未解禁の段は拒否音だけで何も起きない。
        //   2026-09-17: ここに付いていた入力ゲート（_detailT > 0.15）を外した。ユーザー実機指摘
        //   「ダブルクリックしないと入らない」の正体がこれで、詳細を開いた直後 0.15 秒のクリックが
        //   丸ごと捨てられていた（ホバーで _tierSel だけが動くので「1回目は選択だけ」に見える）。
        //   詳細を開いた押下がそのまま確定へ流れる事故は、クリックもZも押下エッジ（MouseClick /
        //   _zHeld）で取っているので時間ゲート無しでも起きない。
        if (dclk >= TierIdBase && dclk < TierIdBase + Tiers.Length)
        {
            int ci = dclk - TierIdBase;
            if (TierOpen(ci)) { _tierSel = ci; zEdge = true; }
            else { Audio.Instance?.PlayUiDeny(); return; }
        }
        if (!tiers && dclk == FinalDiveId) zEdge = true;
        if (zEdge && NeedsFinalAccount)
        {
            OpenJob();                       // 先に開く（中で鳴る確定音を、この下の拒否音で上書きする）
            Audio.Instance?.PlayUiDeny();
            Toast(GameManager.MinaFinalLockHint, "あかり／こはる／レイ のどなたかで、潜ってください。", UiKit.Kegare);
            return;
        }
        if (zEdge && (!tiers || TierOpen(_tierSel)))
        {
            Audio.Instance?.PlayUiConfirm();
            // 難易度はここで確定（DiffSelect と同じ代入。数値・実装は不変）。
            if (_game != null && tiers && TierOpen(_tierSel)) _game.Difficulty = Tiers[_tierSel].Diff;
            if (_game != null) _game.PendingStageScene = e.Scene;
            // 2026-09-07: 入口（最初から/中ボスから/ボスから）を問う画面を廃止した（ユーザー実機指摘
            //   「どこからやるを非表示にして」「基本的に最初から始める仕様で OK」）。以前はここで
            //   中ボス持ち＆解放済みの面だけ DiffSelect へ寄り道させていたが、常に「最初から」で
            //   そのまま戦闘へ入る＝潜るまでの手数が1つ減る（投稿を開く → 潜り方を選ぶ → 潜る）。
            //   開始位置の仕組み自体は残っており `--boss` とゲームオーバーの R が使う（DiffSelect.cs 参照）。
            if (_game != null && !_game.DebugAlwaysBoss) _game.SelectedEntry = GameManager.StageEntry.Start;
            Dive(e.Scene);
            return;
        }

        // もどる＝X／Esc／パッドB。Esc は 2026-09-26 に「一つ前の画面へ」として復帰（メニューを開くのは M に
        // 移ったので、2026-09-14 の「Esc 一発でメニューが開きつつ裏で詳細も閉じる」衝突は起きない）。
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_xHeld; _xHeld = back;
        if (backEdge) { Audio.Instance?.PlayUiCancel(); _mode = Mode.Cards; }
    }

    private void DiveAuto()
    {
        string? next = _game?.NextUnclearedStageId();
        if (next != null)
            foreach (var s in GameManager.Stages)
                if (s.Id == next) { Dive(s.Scene); return; }
        Dive(FinalScene);
    }

    private void Dive(string scene)
    {
        if (_dived) return;
        if (scene == FinalScene && _game != null && _game.IsMinaLockedForFinal && _game.SelectedJob == Job.Tank)
        {
            // 解禁済みのうち一番手前＝あかり（Jobs.All の並び順）へ寄せる。三人とも未解禁なら
            //   そもそも AllStoryCleared が立たず FINAL カードが出ないので、ここは必ず誰かに当たる。
            _game.SelectedJob = Job.Magic;
            GD.Print($"[JOB] FINAL は初挑戦のため結び手を回避 -> {_game.JobDef.CharacterName}");
        }
        _dived = true;
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GameManager.FadeToScene(this, scene);
    }

    // ───────── 描画 ─────────
    public override void _Draw()
    {
        _drawCanvas = this;
        UiKit.BeginDesign(_drawCanvas);
        _drawCanvas.DrawRect(new Rect2(0, 0, W, H), new Color(0.025f, 0.03f, 0.04f, _hasNightBg ? 0.48f : 1f));
        bool home = HomePresented;
        bool focusedOverlay = _mode == Mode.Dialogue || _mode == Mode.Detail || _mode == Mode.Job;
        if (home) DrawHomeSurroundings(focusedOverlay ? 0.34f : 1f);
        else DrawSidePanels(focusedOverlay ? 0.34f : 1f);
        if (_mode == Mode.SocialThread)
        {
            _drawCanvas.DrawRect(CompanionArea, new Color(0, 0, 0, 0.6f));
            _drawCanvas.DrawRect(StoryArea, new Color(0, 0, 0, 0.6f));
        }
        DrawSwitchButton();
        if (PendingHomeCard is { } arrival) DrawUnlockNotice(arrival);
        _phoneVisuals.DrawHardware(_drawCanvas);
        UiKit.EndDesign(_drawCanvas);
        _phoneMask.QueueRedraw();
        _phoneScreen.QueueRedraw();
        _overlay.QueueRedraw();
    }

    private void DrawPhoneMask()
    {
        _drawCanvas = _phoneMask;
        BeginPhoneScreen();
        // The glass opening in phone_ui.glb is 480 × 720 with a 38 px corner radius.
        UiKit.Box(_drawCanvas, new Rect2(PhoneX, 0, PhoneW, H), Colors.White, 38f);
        UiKit.EndDesign(_drawCanvas);
    }

    private void DrawPhoneScreen()
    {
        _drawCanvas = _phoneScreen;
        BeginPhoneScreen();
        bool photos = _mode == Mode.Photos || (_mode == Mode.Job && _jobReturnMode == Mode.Photos)
            || (_mode == Mode.Dialogue && _dlgReturnMode == Mode.Photos);
        if (MinaLineOpen) DrawMinaCall();
        else if (LimeOpen) DrawLime();
        else if (HomePresented)
        {
            DrawHome();
            if (_mode == Mode.HomeReveal) DrawHomeReveal();
            else if (_mode == Mode.SnsOpening) DrawSnsOpening();
        }
        else
        {
            _drawCanvas.DrawRect(new Rect2(PhoneX, 0, PhoneW, H), PhoneBg);
            if (photos) DrawPhotos();
            else if (_mode == Mode.SocialThread) DrawSocialThread();
            else DrawTimeline(_mode == Mode.Cards || _dlgSeenKey == SnsIntroSeenKey ? 1f : 0.22f);
        }
        DrawPhoneOverlay();
        UiKit.EndDesign(_drawCanvas);
    }

    private void DrawPhoneOverlay()
    {
        if (_mode == Mode.Dialogue && !_limeConversation && !_minaCall) DrawDialog();
        else if (_mode == Mode.Detail) DrawDetail();
        else if (_mode == Mode.Job) DrawJob();
        else if (_mode == Mode.Cards) DrawFooter();
        if (!_limeConversation && !MinaLineOpen) DrawToast();
        if (DialogShown) _toolbar.Draw(_drawCanvas, ToolbarAnchor, _game?.AutoAdvanceDialog ?? false, Hud.SkipLatched || _ffNow);
    }

    private void DrawOverlay()
    {
        _drawCanvas = _overlay;
        UiKit.BeginDesign(_drawCanvas);
        DrawContaminationOverlay();
        if (_limeConversation || MinaLineOpen) DrawToast();
        _toolbar.DrawLatchMark(_drawCanvas, new Vector2(UiKit.DesignW - 16f, 14f));
        DrawHintBar();
        UiKit.EndDesign(_drawCanvas);
    }

    // ───────── 画面下端のヒント帯（2026-09-27）─────────
    //   作者指摘「スマホの操作で J とか C とか X とか書いてあるけど操作性悪すぎでしょ」。項目ごとに散らしていた
    //   キー表記をやめ、操作は「矢印で選ぶ・Z で決める・Esc／X でもどる」に絞って、その案内を画面下端の右寄せ1行に出す。
    //   PauseMenu の右下「M メニュー」チップは Hub では出さない（この帯に含めた。クリックで開く役目も HintMenuClicked が継ぐ）。
    //   会話中（返信・小話）はボタン列（DialogToolbar）があるので出さない。
    //   ★同日、ショップ／記録／カスタマイズ／難易度選択／写真アプリも同じ帯へ揃えた（位置・下敷きは UiKit.HintBarPlate に共通化）。
    private static Vector2 HintAnchor => UiKit.HintAnchor;

    // いまのモードで出す項目（空＝出さない）。パッド表示は 十字／A／B／≡。
    private (string token, string label)[] HintItems()
    {
        if (_dived || _autoplay) return System.Array.Empty<(string, string)>();
        bool pad = Pad.UsingPad;
        string ok = pad ? "A" : "Z", back = pad ? "B" : "Esc", menu = pad ? "≡" : "M";
        return _mode switch
        {
            Mode.Home => pad
                ? new[] { ("十字", "えらぶ"), (ok, "ひらく"), (menu, "メニュー") }
                : new[] { ("↑↓←→", "えらぶ"), (ok, "ひらく"), (menu, "メニュー") },
            Mode.Dialogue or Mode.MinaUnanswered when MinaLineOpen && _minaHistoryOpen => new[] { (pad ? "十字上下" : "↑↓", "履歴"), (back, "回線へ") },
            Mode.MinaUnanswered => new[] { (back, "閉じる"), (pad ? "X" : "H", "通話履歴") },
            Mode.Dialogue when _minaCall && _minaChoices.Length > 0 => new[] { (pad ? "十字上下" : "↑↓", "選ぶ"), (pad ? "A" : "Enter", "話す"), (back, "終話"), (pad ? "X" : "H", "履歴") },
            Mode.Dialogue when _minaCall && _minaCasual => new[] { (back, "終話"), (pad ? "X" : "H", "履歴") },
            Mode.Dialogue when _minaCall => new[] { (pad ? "A" : "Enter", MinaStoryEnd ? "終話" : MinaCallAction ?? "会話"), (pad ? "X" : "H", "通話履歴") },
            Mode.LimeFriends => new[] { (pad ? "十字上下" : "↑↓", "フレンド"), (ok, "トークを開く"), (back, "ホームへ") },
            Mode.LimeHistory => new[] { (pad ? "十字上下" : "↑↓", "履歴"), (back, "フレンドへ") },
            Mode.LimeDirect => new[] { (pad ? "A" : "Enter", "送信"), (pad ? "十字上下" : "↑↓", "履歴"), (back, "フレンドへ") },
            Mode.Dialogue when _limeConversation && LimeDraft != null => new[] { (pad ? "A" : "Enter", "送信") },
            // ヘッダー（使用中のアカウント）にカーソル：↓ で先頭のカードへ戻る／Z で切り替えを開く。
            Mode.Cards when _headFocus => pad
                ? new[] { ("十字", "もどる"), (ok, "アカウント切り替え"), (back, "ホームへ"), (menu, "メニュー") }
                : new[] { ("↓", "もどる"), (ok, "アカウント切り替え"), (back, "ホームへ"), (menu, "メニュー") },
            Mode.Cards when _footSel >= 0 => pad
                ? new[] { ("十字", "えらぶ"), (ok, "けってい"), (back, "ホームへ"), (menu, "メニュー") }
                : new[] { ("←→", "えらぶ"), (ok, "けってい"), (back, "ホームへ"), (menu, "メニュー") },
            Mode.Cards when _cardBtn < 0 || _entries[_sel].Social != null =>
                new[] { (pad ? "十字上下" : "↑↓", "投稿"), (pad ? "十字左右" : "←→", "ボタン"),
                    (ok, _cardBtn == LikeButton ? "いいね" : _cardBtn == RepostButton ? "リツイート" : "読む"), (back, "ホームへ") },
            Mode.Cards => pad
                ? new[] { ("十字", "えらぶ"), (ok, "けってい"), (back, "ホームへ"), (menu, "メニュー") }
                : new[] { ("↑↓", "えらぶ"), ("←→", "ボタン"), (ok, "けってい"), (back, "ホームへ"), (menu, "メニュー") },
            Mode.SocialThread when _socialPost != null && SnsPostMedia.Find(_socialPost.Id)?.IsVideo == true => pad
                ? new[] { ("十字", "スクロール・前後の投稿"), (ok, "再生／停止"), (back, "もどる"), (menu, "メニュー") }
                : new[] { ("↑↓", "スクロール"), ("←→", "前後の投稿"), (ok, "再生／停止"), (back, "もどる") },
            Mode.SocialThread when _socialPost?.CharacterId.Length > 0 =>
                new[] { (back, "もどる"), (menu, "メニュー") },
            Mode.SocialThread => pad
                ? new[] { ("十字上下", "スクロール"), ("十字左右", "前後の投稿"), (back, "もどる"), (menu, "メニュー") }
                : new[] { ("↑↓", "スクロール"), ("←→", "前後の投稿"), (back, "もどる"), (menu, "メニュー") },
            Mode.Detail or Mode.Job => pad
                ? new[] { ("十字", "えらぶ"), (ok, "けってい"), (back, "もどる"), (menu, "メニュー") }
                : new[] { ("↑↓", "えらぶ"), (ok, "けってい"), (back, "もどる"), (menu, "メニュー") },
            // 写真アプリ：矢印で写真を選び Z で背景にする（写真の上の「Z 背景にする」の文字は外し、案内はこの帯だけ）。
            Mode.Photos => pad
                ? new[] { ("十字", "えらぶ"), (ok, "背景にする"), (back, "もどる"), (menu, "メニュー") }
                : new[] { ("↑↓←→", "えらぶ"), (ok, "背景にする"), (back, "もどる"), (menu, "メニュー") },
            _ => System.Array.Empty<(string, string)>(),
        };
    }

    // ヒント帯の外接矩形（出していないときは大きさ0）。QA と当たり判定が読む。
    public Rect2 HintBarRect
    {
        get
        {
            var items = HintItems();
            if (items.Length == 0) return new Rect2();
            var rects = UiKit.HintBarRects(HintAnchor, items);
            return rects[0].Merge(rects[^1]);
        }
    }
    // 最後に描いたヒント帯の矩形（_Draw が実際に描いたか。描かなかったフレームは大きさ0）。
    public Rect2 HintBarDrawnRect { get; private set; }

    // 「メニュー」項目（帯の右端）の矩形。
    private Rect2 HintMenuRect()
    {
        var items = HintItems();
        return items.Length == 0 ? new Rect2() : UiKit.HintBarRects(HintAnchor, items)[^1];
    }

    private bool HintMenuClicked() => Pad.MouseClick() && HintMenuRect().HasPoint(Pad.MousePos());

    private void DrawHintBar()
    {
        var items = HintItems();
        HintBarDrawnRect = items.Length == 0 ? new Rect2() : HintBarRect;
        // 帯の下敷き（暗い角丸の板）。SNS のキーボード表記は項目が5つで右の余白（約370px）に収まらず、
        //   スマホ本体の右下へ少しかかる。板を敷いて「画面の上に浮いた案内」として読ませる（地の絵や枠線と混ざらない）。
        //   マウスが「メニュー」に乗っているときだけその項目に下敷きを敷く＝押せることを見せる（旧チップの作法）。
        UiKit.HintBarPlate(_drawCanvas, HintAnchor, items, "メニュー");
    }

    private void DrawSidePanels(float alpha)
    {
        if (alpha <= 0.01f) return;
        DrawCompanionSidePanel(alpha);
        DrawSignalSidePanel(alpha);
    }

    private void DrawHomeSurroundings(float alpha)
    {
        _drawCanvas.DrawRect(new Rect2(0, 0, W, H), new Color(0.025f, 0.045f, 0.075f, 0.45f));
        UiKit.RadialGlow(_drawCanvas, new Vector2(635, 310), 490, new Color("5991ad"), 0.12f);
        var job = _game.JobDef;
        UiKit.RadialGlow(_drawCanvas, new Vector2(240, 350), 380, JobColor(job.Id), 0.08f);
        float breath = Mathf.Sin((float)_t * 0.85f) * 2;
        if (!MinaCompanionOffline)
            DrawSidePortrait(job.CharacterId, new Rect2(0, 126, 452, H - 126), new Vector2(230, 286 + breath), 840, alpha * 0.94f);
        DrawSideShade(new Rect2(0, 0, 452, H), 470, alpha * 0.86f);
        UiKit.Text(_drawCanvas, _sideTitleFont, new Vector2(48, 38), "Refrain", 66, new Color("fff4df", alpha));
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(52, 534), MinaCompanionOffline ? "応答なし" : "同行中", 13, new Color("9cbcc7", alpha));
        UiKit.Text(_drawCanvas, _sideNameFont, new Vector2(48, 558), job.CharacterName, 38, new Color("fff7ea", alpha));
        UiKit.Text(_drawCanvas, _sideTitleFont, new Vector2(52, 610), AccountHandle(job), 23, new Color("bddde4", alpha * 0.8f));

        string id = _sideStoryId;
        bool final = id == "final";
        string character = final ? "mina" : id;
        var storyJob = System.Array.Find(Jobs.All, j => j.CharacterId == character)!;
        var card = new Rect2(925, 284, 290, 152);
        UiKit.Box(_drawCanvas, new Rect2(card.Position + new Vector2(0, 12), card.Size), new Color(0, 0, 0, alpha * 0.22f), 20);
        UiKit.Box(_drawCanvas, card, new Color(0.055f, 0.085f, 0.12f, alpha * 0.72f), 20,
            new Color(0.62f, 0.78f, 0.87f, alpha * 0.14f), 1);
        _drawCanvas.DrawLine(new Vector2(950, 285), new Vector2(1190, 285), new Color(0.72f, 0.86f, 0.91f, alpha * 0.18f), 1, true);
        string caption = final ? "最後の声" : IsClearedForDisplay(id) ? "届いた声" : "次の声";
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(946, 306), caption, 13, new Color("b5c8d1", alpha));
        UiKit.FaceAvatar(_drawCanvas, new Vector2(1159, 355), 30, final && FinalClearedForDisplay ? _minaFace : _faces[id],
            new Color("9dbacb"), false, 0, alpha * 0.9f, _t);
        UiKit.Text(_drawCanvas, _sideNameFont, new Vector2(944, 333), final && !FinalClearedForDisplay ? FinalAccount.Name : storyJob.CharacterName, 30, new Color("fff4df", alpha));
        UiKit.Text(_drawCanvas, _sideTitleFont, new Vector2(947, 382), final ? "Final" : $"Story {System.Array.FindIndex(GameManager.Stages, s => s.Id == id) + 1:00}",
            23, new Color("eacb8b", alpha * 0.82f));
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(932, 468), "届いた声", 14, new Color("9cb3c0", alpha));
        UiKit.Text(_drawCanvas, UiKit.Mono, new Vector2(1110, 466), $"{ClearedStageCount():00} / {GameManager.Stages.Length:00}", 17,
            new Color("dce6ec", alpha));
        for (int i = 0; i < GameManager.Stages.Length; i++)
            UiKit.Box(_drawCanvas, new Rect2(932 + i * 94, 502, 82, 3),
                new Color(IsClearedForDisplay(GameManager.Stages[i].Id) ? new Color("8ccdbd") : new Color("344452"), alpha), 1);
    }

    private string SideStoryId()
    {
        bool home = _mode is Mode.Home or Mode.HomeReveal or Mode.SnsOpening or Mode.Photos
            || (_mode == Mode.Dialogue && _dlgReturnMode == Mode.Home)
            || (_mode == Mode.Job && _jobReturnMode is Mode.Home or Mode.Photos);
        if (!home && _mode == Mode.SocialThread && _socialPost?.CharacterId.Length > 0)
            return SnsVoices.ReturnPostStageId(_socialPost);
        if (!home && _entries[_sel].Sort == Kind.Personal) return SnsVoices.ReturnPostStageId(_entries[_sel].Social!);
        if (!home && IsVoice(_sel)) return _entries[_sel].Id;
        return _entries[DefaultSelection()].Id;
    }

    // 端末の一枚絵と縦長の立ち絵で目線と顔の表示寸法を揃える。
    private static (Vector2 eyes, float face) SidePortraitFace(string id) => id switch
    {
        "akari" => (new(535, 365), 200f), "koharu" => (new(662, 357), 148f),
        "rei" => (new(555, 357), 163f), _ => (new(1040, 365), 118f),
    };

    // rect を clip で切って描く。fade > 0 なら画像の下端 fade px を透明へ落とす＝バストアップの切り抜きが
    //   パネルの途中で終わる縁（あかり／こはる／レイ）を下の影へ溶かす。全身絵（ミナ）は下端が画面外なので効かない。
    private void DrawSideTexture(Texture2D texture, Rect2 rect, Rect2 clip, float alpha, float fade = 0f)
    {
        Rect2 visible = rect.Intersection(clip);
        if (!visible.HasArea()) return;
        float solidEnd = fade > 0f ? Mathf.Min(visible.End.Y, rect.End.Y - fade) : visible.End.Y;
        if (solidEnd > visible.Position.Y)
        {
            Rect2 solid = new(visible.Position, new Vector2(visible.Size.X, solidEnd - visible.Position.Y));
            Rect2 source = new((solid.Position - rect.Position) / rect.Size * texture.GetSize(),
                solid.Size / rect.Size * texture.GetSize());
            _drawCanvas.DrawTextureRectRegion(texture, solid, source, new Color(1, 1, 1, alpha));
        }
        if (solidEnd >= visible.End.Y) return;
        float top = Mathf.Max(solidEnd, visible.Position.Y);
        Vector2[] pts = { new(visible.Position.X, top), new(visible.End.X, top), visible.End, new(visible.Position.X, visible.End.Y) };
        var uvs = new Vector2[4];
        for (int i = 0; i < 4; i++) uvs[i] = (pts[i] - rect.Position) / rect.Size;
        float a0 = alpha * Mathf.Clamp((rect.End.Y - top) / fade, 0f, 1f);
        float a1 = alpha * Mathf.Clamp((rect.End.Y - visible.End.Y) / fade, 0f, 1f);
        _drawCanvas.DrawPolygon(pts, new[] { new Color(1, 1, 1, a0), new Color(1, 1, 1, a0), new Color(1, 1, 1, a1), new Color(1, 1, 1, a1) },
            uvs, texture);
    }

    private void DrawSideLandscape(string id, Rect2 area, float alpha)
    {
        for (int layer = 0; layer < 2; layer++)
        {
            Texture2D texture = layer == 0 ? _sideSkies[id] : _sideScenery[id];
            Vector2 size = texture.GetSize() * ((H + 40) / texture.GetHeight());
            float drift = Mathf.Sin((float)_t * 0.13f + layer) * (layer == 0 ? 6 : 14);
            DrawSideTexture(texture, new Rect2(area.GetCenter() - size / 2 + new Vector2(drift, 0), size),
                area, alpha * (layer == 0 ? 0.82f : 0.68f));
        }
        _drawCanvas.DrawRect(area, new Color(0.015f, 0.025f, 0.035f, alpha * 0.18f));
    }

    // focus＝両目の中点を置く画面座標。height＝ミナ（全身絵）を描く高さで、他の絵は顔の高さ比で縮める。
    private void DrawSidePortrait(string id, Rect2 area, Vector2 focus, float height, float alpha)
    {
        Texture2D texture = _sidePortraits[id];
        var (eyes, face) = SidePortraitFace(id);
        float scale = height / 1536f * (97f / face);
        DrawSideTexture(texture, new Rect2(focus - eyes * scale, texture.GetSize() * scale), area, alpha, fade: 120f);
    }

    private void DrawSideShade(Rect2 area, float top, float alpha, float end = 0.75f)
    {
        UiKit.VGradient(_drawCanvas, new Rect2(area.Position.X, top, area.Size.X, H - top),
            new[] { new Color(0.027f, 0.038f, 0.045f, 0), new Color(0.027f, 0.038f, 0.045f, alpha) },
            new[] { 0f, end });
    }

    private void DrawCompanionSidePanel(float alpha)
    {
        var job = _game.JobDef;
        string id = job.CharacterId;
        DrawSideLandscape(id, CompanionArea, alpha * 0.72f);
        float arrive = Mathf.SmoothStep(0, 1, Mathf.Clamp((float)(_t / 0.7), 0, 1));
        float breath = Mathf.Sin((float)_t * 0.85f) * 3;
        // 両目の中点を (239, 263) へ＝ミナの見え方は従来（焦点 0.56/0.16・高さ 780）のまま。他3人はここに顔を揃える。
        if (!MinaCompanionOffline)
            DrawSidePortrait(id, CompanionArea, new Vector2(239 - (1 - arrive) * 22, 263 + breath),
                780, alpha * arrive);
        DrawSideShade(CompanionArea, 430, alpha);
        UiKit.VGradient(_drawCanvas, new Rect2(0, 0, CompanionArea.Size.X, 195),
            new[] { new Color(0.027f, 0.038f, 0.045f, alpha * 0.6f), new Color(0, 0, 0, 0) }, new[] { 0f, 1f });
        UiKit.Text(_drawCanvas, _sideTitleFont, new Vector2(40, 30), "Refrain", 72, new Color("fff4df", alpha));
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(46, 128), MinaCompanionOffline ? "応答なし" : "同行中", 13, new Color("bee8dc", alpha));
        UiKit.Text(_drawCanvas, _sideNameFont, new Vector2(42, 548), job.CharacterName, 46, new Color("fff7ea", alpha));
        UiKit.Text(_drawCanvas, _sideTitleFont, new Vector2(46, 614), AccountHandle(job), 24, new Color("bddde4", alpha));
        var btn = SwitchButtonRect();
        float rowY = btn.GetCenter().Y, ax = btn.End.X + CompanionRowGap + CompanionR;
        int count = 0;
        foreach (var companion in Jobs.All)
        {
            if (!_game.IsJobUnlocked(companion.Id)) continue;
            UiKit.FaceAvatar(_drawCanvas, new Vector2(ax + count * CompanionStep, rowY), CompanionR, _playerFaces[companion.CharacterId],
                JobColor(companion.Id), false, 0, alpha * (companion.Id == job.Id ? 1 : 0.55f), _t);
            count++;
        }
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(CompanionLabelX(count), rowY - 11f), job.CharacterName, 13,
            new Color(UiKit.Text2, alpha));
    }

    // ───────── キャラ切り替えボタン（2026-09-27 作者指示「スマホの画面でキャラ切り替えボタンを画面の左下に」）─────────
    //   置き場所は左パネルの左下＝スマホ本体の左隣（スマホの中のフッタではない）。押すと OpenJob（フッタ「アカウント」・
    //   ヘッダーと同じアカウント切り替え）。キーは Tab／パッド Y、マウスはクリック。切り替え画面で押し直すと閉じる（トグル）。
    //   効くのはホーム／SNS／投稿詳細／写真アプリ（と開いた切り替え画面）。会話中・アプリ起動の演出中は薄くして反応させない。
    //   ボタンに Tab（パッドは Y の丸ボタン）のキャップを描いてあるので、ヒント帯には足さない。
    //   Hub では Tab が会話ログを開かない（Backlog.TabOpensHere）。ログは L／パッド View で開く。
    //   パッド Y は会話中だけ会話ボックスの AUTO（DialogToolbar）。会話中はこのボタンが効かないので取り合わない。
    private const float SwitchBtnX = 36f, SwitchBtnY = 668f, SwitchBtnH = 34f;
    private const float SwitchPadL = 10f, SwitchGap = 8f, SwitchPadR = 14f, SwitchCapH = 22f;
    private const int SwitchLabelSize = 14;
    private const string SwitchLabel = "キャラ切り替え";
    private const float CompanionR = 14f, CompanionStep = 34f, CompanionRowGap = 14f;
    // キャップの枠＝キーボードの Tab とパッドの Y の広いほう。表記が切り替わってもボタンの幅（と右のアバター列）は動かない。
    private static float SwitchCapSlot => Mathf.Max(UiKit.KeyCapW("Tab", SwitchCapH, false), UiKit.KeyCapW("Y", SwitchCapH, true));
    private static Rect2 SwitchButtonRect() => new(SwitchBtnX, SwitchBtnY,
        SwitchPadL + SwitchCapSlot + SwitchGap + UiKit.TextW(UiKit.ZenBold, SwitchLabel, SwitchLabelSize) + SwitchPadR, SwitchBtnH);
    private static float CompanionLabelX(int count) =>
        SwitchButtonRect().End.X + CompanionRowGap + CompanionR + (count - 1) * CompanionStep + CompanionR + 8f;
    private static bool SwitchKeyDown() => Input.IsKeyPressed(Key.Tab) || Pad.Pressed(JoyButton.Y);
    private bool SwitchAvailable => !_dived && !_autoplay
        && _mode is Mode.Home or Mode.Cards or Mode.Detail or Mode.Photos or Mode.Job;
    private bool SwitchHovered => Pad.UsingMouse && SwitchButtonRect().HasPoint(Pad.MousePos());
    private static readonly Rect2 UnlockNoticeRect = new(925, 120, 290, 123);
    private JobTuning? PendingHomeCard => _mode == Mode.Home && SwitchAvailable && _game is { JobForcedByCmdline: false }
        ? _game.NextUnlockCard : null;

    private void DrawUnlockNotice(JobTuning job)
    {
        var r = UnlockNoticeRect;
        bool hover = Pad.UsingMouse && r.HasPoint(Pad.MousePos());
        var (ink, paper, edge) = job.Id switch
        {
            Job.Heal => (new Color("21473b"), new Color("e2f3db"), new Color("8ab7a0")),
            Job.Magic => (new Color("392d53"), new Color("e7dff3"), new Color("ab93c3")),
            _ => (new Color("422d32"), new Color("f5e5c6"), new Color("ceaa76")),
        };
        UiKit.Box(_drawCanvas, r, hover ? paper.Lightened(.18f) : paper, 12f, edge, 2f);
        UiKit.FaceAvatar(_drawCanvas, r.Position + new Vector2(37, 37), 23, _playerFaces[job.CharacterId],
            JobColor(job.Id), false, 0, 1, _t);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, r.Position + new Vector2(71, 17), $"{job.CharacterName}が", 20, ink);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, r.Position + new Vector2(71, 41), "仲間になった！", 25, ink);
        Rect2 action = new(r.Position + new Vector2(11, 82), new Vector2(r.Size.X - 22, 31));
        UiKit.Box(_drawCanvas, action, ink, 6f);
        UiKit.KeyCap(_drawCanvas, action.Position + new Vector2(11, 4), Pad.UsingPad ? "Y" : "Tab", 22f, pad: Pad.UsingPad);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, action.Position + new Vector2(57, 4), $"{job.CharacterName}を見る", 16, paper);
        var tip = action.End - new Vector2(14, 15);
        _drawCanvas.DrawPolyline(new[] { tip + new Vector2(-5, -5), tip, tip + new Vector2(-5, 5) }, new Color("fff3da"), 2f, true);
    }

    // Tab／Y／クリックで開く（切り替え画面の中なら閉じる）。処理したフレームは true＝各モードの入力へ流さない。
    private bool ProcessSwitchButton()
    {
        bool key = SwitchKeyDown();
        bool edge = key && !_tabHeld; _tabHeld = key;
        bool click = Pad.MouseClick() && (SwitchButtonRect().HasPoint(Pad.MousePos())
            || PendingHomeCard != null && UnlockNoticeRect.HasPoint(Pad.MousePos()));
        return (edge || click) && PressSwitchButton();
    }

    // ボタンを押したときの処理（キー・クリック共通。QA も直接呼ぶ）。効かない状態なら何もせず false。
    private bool PressSwitchButton()
    {
        if (!SwitchAvailable || _t <= 0.3) return false;
        if (_mode == Mode.Job)
        {
            if (_jobT <= 0.15) return false;   // 開いた直後の押し直しは拾わない（シートの展開と同じゲート）
            Audio.Instance?.PlayUiCancel();
            _mode = _jobReturnMode; _xHeld = true;
            return true;
        }
        OpenJob();
        return true;
    }

    private void DrawSwitchButton()
    {
        if (_autoplay) return;
        var r = SwitchButtonRect();
        bool on = SwitchAvailable, open = _mode == Mode.Job, hover = on && SwitchHovered;
        float a = on ? 1f : 0.35f;
        // 角丸の板。ホバーで明るく、切り替え画面を開いている間とホバー中は浄化色の枠（ヘッダー・フッタのカーソルと同じ色）。
        Color bg = hover ? new Color(0.17f, 0.21f, 0.25f, 0.92f) : new Color(0.07f, 0.09f, 0.11f, 0.80f);
        Color border = hover || open ? new Color(UiKit.Purify, 0.95f) : new Color(1, 1, 1, 0.16f);
        UiKit.Box(_drawCanvas, r, new Color(bg, bg.A * a), 8f, new Color(border, border.A * a), hover || open ? 1.5f : 1f);
        bool pad = Pad.UsingPad;
        string token = pad ? "Y" : "Tab";
        float slot = SwitchCapSlot, cw = UiKit.KeyCapW(token, SwitchCapH, pad);
        var capPos = new Vector2(r.Position.X + SwitchPadL + (slot - cw) / 2f, r.Position.Y + (r.Size.Y - SwitchCapH) / 2f);
        UiKit.KeyCap(_drawCanvas, capPos, token, SwitchCapH, pressed: on && _tabHeld, alpha: a, pad: pad);
        var font = UiKit.ZenBold;
        float ty = r.Position.Y + (r.Size.Y - font.GetHeight(SwitchLabelSize)) / 2f;
        UiKit.Text(_drawCanvas, font, new Vector2(r.Position.X + SwitchPadL + slot + SwitchGap, ty), SwitchLabel, SwitchLabelSize,
            new Color(hover || open ? UiKit.White : UiKit.Text2, a));
    }

    private void DrawSignalSidePanel(float alpha)
    {
        float blend = Mathf.SmoothStep(0, 1, _sideStoryBlend);
        if (blend < 1) DrawStoryPreview(_sideStoryPrevious, alpha * (1 - blend));
        DrawStoryPreview(_sideStoryId, alpha * blend);
        const float x = 919;
        int count = VoiceMedallionCount;
        int cleared = ClearedStageCount() + (FinalClearedForDisplay ? 1 : 0);
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x, 461), "届いた声", 14, new Color("c9d8d8", alpha));
        UiKit.Text(_drawCanvas, UiKit.Mono, new Vector2(1159, 457), $"{cleared:00} / {count:00}", 17,
            new Color("f4dc9f", alpha));
        for (int i = 0; i < count - 1; i++)
            DrawVoiceLink(VoiceMedallionCenter(i) + new Vector2(0, 6),
                VoiceMedallionCenter(i + 1) + new Vector2(0, 6), IsClearedForDisplay(VoiceStageId(i)), alpha);
        for (int i = 0; i < count; i++)
        {
            string id = VoiceStageId(i);
            var entry = System.Array.Find(_entries, e => e.Id == id && e.Sort == Kind.Voice && !e.IsRepost);
            bool saved = id == GameManager.FinalStageId ? FinalClearedForDisplay : IsClearedForDisplay(id);
            Vector2 center = VoiceMedallionCenter(i);
            float cx = center.X;
            bool current = id == _sideStoryId;
            DrawVoiceMedallion(center, saved ? _faces[id] : null, saved, current, i, alpha);
            UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(cx - 48, 564),
                saved ? (id == GameManager.FinalStageId ? "MINA" : entry.Name) : $"STAGE {i + 1}", 15,
                new Color(saved || current ? UiKit.White : UiKit.Text3, alpha), HorizontalAlignment.Center, 96);
        }
        DrawSideMetric(new Vector2(x, 624), "フォロワー", UiKit.Abbrev(_game.Followers), new Color("eba4b9"), alpha);
        DrawSideMetric(new Vector2(x + 170, 624), "インプレ", UiKit.Abbrev(_game.Impression), new Color("e5c986"), alpha);
    }

    private void DrawVoiceLink(Vector2 origin, Vector2 next, bool connected, float alpha)
    {
        var from = origin + new Vector2(27, 0);
        var to = next - new Vector2(27, 0);
        _drawCanvas.DrawLine(from + new Vector2(0, 5), to + new Vector2(0, 5), new Color("020606", alpha * 0.8f), 8, true);
        _drawCanvas.DrawLine(from, to, new Color("273b3b", alpha), 7, true);
        _drawCanvas.DrawLine(from + new Vector2(0, 2), to + new Vector2(0, 2), new Color("0a1418", alpha), 4, true);
        _drawCanvas.DrawLine(from - new Vector2(0, 3), to - new Vector2(0, 3), new Color("7b9996", alpha * 0.35f), 1, true);
        if (!connected) return;
        _drawCanvas.DrawLine(from, to, new Color("3cd9a8", alpha * 0.12f), 13, true);
        _drawCanvas.DrawLine(from, to, new Color("49bd98", alpha * 0.7f), 3, true);
        _drawCanvas.DrawLine(from - new Vector2(0, 0.5f), to - new Vector2(0, 0.5f), new Color("ceffe9", alpha * 0.85f), 1, true);
        float travel = Mathf.PosMod((float)_t * 0.16f - origin.X * 0.003f, 1);
        var light = from.Lerp(to, travel);
        UiKit.RadialGlow(_drawCanvas, light, 8, new Color("79f3c9"), 0.25f * alpha);
    }

    private void UpdateVoiceMedallions(double delta)
    {
        _voiceMedallions.SetPresented(!HomePresented);
        if (HomePresented) return;
        bool canHover = Pad.UsingMouse && !Pad.UiBlocked(this) && !_dived
            && VoiceMedallionsInteractive && GetWindow().HasFocus();
        Vector2 pointer = Pad.MousePos();
        for (int i = 0; i < VoiceMedallionCount; i++)
        {
            string id = VoiceStageId(i);
            var entry = System.Array.Find(_entries, e => e.Id == id && e.Sort == Kind.Voice && !e.IsRepost);
            bool saved = id == GameManager.FinalStageId ? FinalClearedForDisplay : IsClearedForDisplay(id);
            bool hovered = canHover && entry.Unlocked && pointer.DistanceSquaredTo(VoiceMedallionCenter(i)) <= 42 * 42;
            _voiceMedallions.UpdateMedal(i, saved ? _faces[id] : null,
                saved, id == _sideStoryId, hovered, _t, delta);
        }
    }

    private bool ProcessVoiceMedallionClick()
    {
        if (!VoiceMedallionsInteractive || _t <= 0.3 || !Pad.MouseClick()) return false;
        for (int i = 0; i < VoiceMedallionCount; i++)
        {
            if (Pad.MousePos().DistanceSquaredTo(VoiceMedallionCenter(i)) > 42 * 42) continue;
            string id = VoiceStageId(i);
            int entry = System.Array.FindIndex(_entries, e => e.Id == id && e.Sort == Kind.Voice && !e.IsRepost);
            if (entry < 0 || !_entries[entry].Unlocked)
            {
                Audio.Instance?.PlayUiDeny();
                return true;
            }
            _sel = entry;
            _footSel = -1;
            _headFocus = false;
            _cardBtn = 0;
            UpdateFeedScrollTarget();
            OpenDetail();
            return true;
        }
        return false;
    }

    private void DrawVoiceMedallion(Vector2 center, Texture2D? face, bool saved, bool current, int index, float alpha)
    {
        Color light = saved ? new Color("8ff0cc") : current ? new Color("f4dc9f") : new Color("84959e");
        _drawCanvas.DrawSetTransform((center + new Vector2(2, 37)) * UiKit.Scale, 0, new Vector2(1, 0.18f) * UiKit.Scale);
        UiKit.RadialGlow(_drawCanvas, Vector2.Zero, 39, Colors.Black, 0.75f * alpha);
        if (face != null)
            UiKit.RadialGlow(_drawCanvas, Vector2.Zero, 29, light, (saved ? 0.2f : 0.1f) * alpha);
        UiKit.BeginDesign(_drawCanvas);
        _voiceMedallions.Draw(_drawCanvas, center, index, alpha);
        if (current)
            _drawCanvas.DrawLine(center + new Vector2(-13, 41), center + new Vector2(13, 41), new Color("f4dc9f", alpha * 0.8f), 2, true);
    }
    private void DrawStoryPreview(string id, float alpha)
    {
        bool final = id == "final";
        string character = final ? "mina" : id;
        var job = System.Array.Find(Jobs.All, j => j.CharacterId == character)!;
        bool masked = final && !FinalClearedForDisplay;
        if (masked)
        {
            _drawCanvas.DrawRect(StoryArea, new Color("172632", alpha));
            UiKit.FaceAvatar(_drawCanvas, new Vector2(1105, 230), 68, _faces[id], new Color("67838a"), false, 0, alpha, _t);
        }
        else
        {
            DrawSideLandscape(character, StoryArea, alpha);
            float drift = Mathf.Sin((float)_t * 0.65f + 1.2f);
            DrawSidePortrait(character, new Rect2(StoryArea.Position, new Vector2(StoryArea.Size.X, 458)),
                new Vector2(1137 + drift * 4, 222 + drift * 2), 1140, alpha);
        }
        DrawSideShade(StoryArea, 300, alpha, 0.36f);
        UiKit.VGradient(_drawCanvas, new Rect2(StoryArea.Position.X, 0, StoryArea.Size.X, 135),
            new[] { new Color(0.027f, 0.038f, 0.045f, alpha * 0.65f), new Color(0, 0, 0, 0) }, new[] { 0f, 1f });
        string caption = final ? "最後の声" : IsClearedForDisplay(id) ? "届いた声" : "次の声";
        UiKit.Text(_drawCanvas, _sideNameFont, new Vector2(919, 38), caption, 23, new Color("fff4df", alpha));
        int index = System.Array.FindIndex(GameManager.Stages, stage => stage.Id == id);
        UiKit.Text(_drawCanvas, _sideTitleFont, new Vector2(919, 77), final ? "Final" : $"Story {index + 1:00}", 27,
            new Color("eacb8b", alpha));
        UiKit.Text(_drawCanvas, _sideNameFont, new Vector2(914, 348), masked ? FinalAccount.Name : job.CharacterName, 40,
            new Color("fff7ea", alpha));
        UiKit.Text(_drawCanvas, _sideTitleFont, new Vector2(919, 404), masked ? FinalAccount.Handle : AccountHandle(job), 21,
            new Color("dae4e5", alpha));
    }

    private void DrawSideMetric(Vector2 pos, string label, string value, Color accent, float alpha)
    {
        UiKit.Text(_drawCanvas, UiKit.Zen, pos, label, 12, new Color(UiKit.Text3, 0.86f * alpha));
        UiKit.Text(_drawCanvas, UiKit.Mono, pos + new Vector2(0, 24), value, 22, new Color(accent, alpha));
    }

    private int ClearedStageCount()
    {
        int count = 0;
        foreach (var s in GameManager.Stages) if (IsClearedForDisplay(s.Id)) count++;
        return count;
    }

    private void DrawTimeline(float alpha)
    {
        DrawCards(alpha);
        _drawCanvas.DrawRect(new Rect2(PhoneX, 0, PhoneW, FeedTop), PhoneBg);
        _drawCanvas.DrawRect(new Rect2(PhoneX, FeedBottom, PhoneW, H - FeedBottom), PhoneBg);
        DrawHeader();
    }

    // 汚染ゲージ連動のハブ全体オーバーレイ。清浄(0-24%)は無し、兆候(25-49%)はごく薄い灰、
    //   進行(50-74%)は灰〜紫でやや強め、危険(75-99%/100%)は濁り最も強い……と段階ごとに滑らかに補間する。
    //   入力は奪わない（DrawRect の純粋な描画のみ）。既存の汚染バー描画（ヘッダー内）とは独立。
    private void DrawContaminationOverlay()
    {
        float contam = Mathf.Clamp(_game?.Contamination ?? 0f, 0f, 1f);
        if (contam < 0.25f) return; // 清浄：オーバーレイなし

        float alpha;
        if (contam < 0.50f) alpha = Mathf.Lerp(0.03f, 0.07f, (contam - 0.25f) / 0.25f);       // 兆候：ごく薄い
        else if (contam < 0.75f) alpha = Mathf.Lerp(0.07f, 0.14f, (contam - 0.50f) / 0.25f);  // 進行：やや強め
        else alpha = Mathf.Lerp(0.14f, 0.22f, Mathf.Min(1f, (contam - 0.75f) / 0.25f));       // 危険〜限界：最も強い

        var gray = new Color("8c889c");
        var purple = new Color("5a3a78");
        float colorT = Mathf.Clamp((contam - 0.25f) / 0.75f, 0f, 1f);
        _drawCanvas.DrawRect(new Rect2(0, 0, W, H), new Color(gray.Lerp(purple, colorT), alpha));
    }

    private void DrawHeader()
    {
        float x = PhoneX + 22f;
        DrawJobButton(HeaderJobRect());
        // ★2026-09-17：ヘッダの2つの数字は左が Followers・右が Impression だが、♥と金の丸だけでは
        //   何の数かが読み取れなかった（ユーザー指摘「タイムラインの♥の意味は何？」）。
        //   記号をやめ「フォロワー」「インプレ」の語ラベルを数字の前に置く＝ハブ本文（745行・841行）の語彙と揃える。
        string imp = UiKit.Abbrev(_game?.Impression ?? 0), fol = UiKit.Abbrev(_game?.Followers ?? 0);
        float right = PhoneX + PhoneW - 22f;
        const string ImpLabel = "インプレ", FolLabel = "フォロワー";
        float iw = UiKit.TextW(UiKit.Mono, imp, 15), ilw = UiKit.TextW(UiKit.Zen, ImpLabel, 12);
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(right - iw - 6f - ilw, 92f), ImpLabel, 12, new Color(UiKit.Gold, 0.75f));
        UiKit.Text(_drawCanvas, UiKit.Mono, new Vector2(right - iw, 89f), imp, 15, UiKit.Gold);
        float fw = UiKit.TextW(UiKit.Mono, fol, 15), flw = UiKit.TextW(UiKit.Zen, FolLabel, 12);
        float fx = right - iw - 6f - ilw - 18f - fw;
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(fx - 6f - flw, 92f), FolLabel, 12, new Color(UiKit.Hp, 0.75f));
        UiKit.Text(_drawCanvas, UiKit.Mono, new Vector2(fx, 89f), fol, 15, UiKit.Hp);
        _drawCanvas.DrawRect(new Rect2(PhoneX + 20f, 68f, PhoneW - 40f, 1f), new Color(1, 1, 1, 0.07f));
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(x, 87f), "タイムライン", 20, UiKit.White);
        float contam = Mathf.Clamp(_game?.Contamination ?? 0f, 0f, 1f);
        _drawCanvas.DrawRect(new Rect2(PhoneX, FeedTop - 3f, PhoneW, 3f), new Color(UiKit.Kegare, 0.10f));
        if (contam > 0) _drawCanvas.DrawRect(new Rect2(PhoneX, FeedTop - 3f, PhoneW * contam, 3f), UiKit.Kegare);
    }

    private const float FeedTop = 128f, FeedBottom = 646f;
    private const float PostMediaHeight = 216f, PostMediaGap = 12f;
    private const int PostMediaIdBase = 53000;
    private const float RepostHeaderHeight = 28f;
    private static float CardHeight(Entry e)
    {
        float header = e.IsRepost ? RepostHeaderHeight : 0;
        if (e.VoiceReplies is { Length: > 0 } rows) return header + rows[^1].Bottom + 64f;
        if (e.Sort == Kind.Personal) return header + 376f;
        if (e.Social != null) return header + 230f + (SnsPostMedia.Find(e.Id) != null ? PostMediaHeight + PostMediaGap : 0);
        return header + (e.Sort == Kind.Voice && e.Unlocked ? 232f : 168f);
    }

    private static float PostMediaTop(Entry e) => e.Sort == Kind.Personal ? 118f : 150f;
    private Rect2 CardMediaRect(int index) => new(PhoneX + 24, FeedTop + CardTop(index) - _feedScroll
        + (_entries[index].IsRepost ? RepostHeaderHeight : 0) + PostMediaTop(_entries[index]), PhoneW - 48, PostMediaHeight);

    private Rect2 SocialMediaRect() => new(PhoneX + 24,
        SocialTop - _socialScroll + 76 + _socialLines[0].Length * (UiKit.Zen.GetHeight(18) + 3) + PostMediaGap,
        PhoneW - 48, PostMediaHeight);

    internal void UpdateSnsMedia()
    {
        string visible = "";
        if (!_dived && !Pad.UiBlocked(this) && GetWindow().HasFocus())
        {
            if (_mode == Mode.Cards)
            {
                float best = PostMediaHeight * .55f;
                for (int i = 0; i < _entries.Length; i++)
                {
                    if (SnsPostMedia.Find(_entries[i].Id)?.IsVideo != true) continue;
                    float height = CardMediaRect(i).Intersection(FeedRect).Size.Y;
                    if (height > best) { best = height; visible = _entries[i].Id; }
                }
            }
            else if (_mode == Mode.SocialThread && _socialPost != null && SnsPostMedia.Find(_socialPost.Id)?.IsVideo == true
                && SocialMediaRect().Intersection(new Rect2(PhoneX, SocialTop, PhoneW, FeedBottom - SocialTop)).Size.Y > PostMediaHeight * .55f)
                visible = _socialPost.Id;
        }
        _snsMedia.SetVisiblePost(visible);
    }

    private float CardTop(int index)
    {
        float y = 0;
        for (int i = 0; i < index; i++) y += CardHeight(_entries[i]);
        return y;
    }

    private static string FitText(Font font, string text, int size, float width)
    {
        if (UiKit.TextW(font, text, size) <= width) return text;
        while (text.Length > 0 && UiKit.TextW(font, text + "…", size) > width) text = text[..^1];
        return text + "…";
    }

    private float _feedScroll, _feedScrollTarget;
    private const float WheelStep = 90f;        // ホイール1ノッチあたりのスクロール量（設計座標）
    private float FeedViewHeight => FeedBottom - FeedTop;
    private float FeedMaxScroll()
    {
        return Mathf.Max(0f, CardTop(_entries.Length) - FeedViewHeight);
    }
    // 選択が画面外へ出ないところまでだけスクロールを動かす（上下に 1 枚ぶんの余白を残して先を見せる）。
    private void UpdateFeedScrollTarget()
    {
        float viewH = FeedViewHeight;
        float y0 = CardTop(_sel), y1 = y0 + CardHeight(_entries[_sel]);
        const float margin = 36f;
        if (y0 - margin < _feedScrollTarget) _feedScrollTarget = y0 - margin;
        else if (y1 + margin > _feedScrollTarget + viewH) _feedScrollTarget = y1 + margin - viewH;
        _feedScrollTarget = Mathf.Clamp(_feedScrollTarget, 0f, FeedMaxScroll());
    }

    private void DrawCards(float alpha)
    {
        for (int i = 0; i < _entries.Length; i++)
        {
            float h = CardHeight(_entries[i]);
            float cy = FeedTop + CardTop(i) - _feedScroll;
            if (cy + h < FeedTop || cy > FeedBottom) continue;
            bool sel = (_mode == Mode.Cards || _dlgSeenKey == SnsIntroSeenKey) && i == _sel && _footSel < 0 && !_headFocus;
            float ep = Mathf.Clamp(((float)(_t - _cardsEnteredT) - i * 0.04f) / 0.20f, 0f, 1f);
            DrawCard(i, cy, h, sel, sel ? _selT : 0f, alpha * ep);
        }
        DrawScrollHint(FeedTop, alpha);
    }

    // 右端のスクロールバー風ヒント（3px）。2-b では実スクロール量に対する現在地を出す＝
    //   「TL はこの先も続いている」ことを言う（つまみの短さが feed の長さ）。
    private void DrawScrollHint(float top, float alpha)
    {
        float max = FeedMaxScroll();
        if (max <= 0f) return;
        float viewH = FeedViewHeight;
        float th = Mathf.Max(28f, viewH * viewH / (viewH + max));
        float ty = top + (viewH - th) * Mathf.Clamp(_feedScroll / max, 0f, 1f);
        UiKit.Box(_drawCanvas, new Rect2(PhoneX + PhoneW - 4f, ty, 2f, th), new Color(UiKit.Text3, 0.45f * alpha), 1f);
    }

    private void DrawCard(int index, float cy, float h, bool sel, float st, float alpha)
    {
        var e = _entries[index];
        float x = PhoneX, w = PhoneW;
        bool voice = e.Sort == Kind.Voice, filler = e.Sort == Kind.Filler;
        Color acc = BarColorFor(e);
        if (sel) _drawCanvas.DrawRect(new Rect2(x, cy, w, h), new Color(acc, (0.045f + 0.025f * st) * alpha));
        _drawCanvas.DrawRect(new Rect2(x + 20f, cy + h - 1f, w - 40f, 1f), new Color(1, 1, 1, 0.09f * alpha));
        if (sel && voice && e.Unlocked)
            _drawCanvas.DrawRect(new Rect2(x, cy + 16f, 3f, h - 32f), new Color(UiKit.Purify, alpha));
        if (e.IsRepost)
        {
            UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(x + 24, cy + 6), "あなたがリツイート", 13, new Color(UiKit.Ok, alpha));
            cy += RepostHeaderHeight;
            h -= RepostHeaderHeight;
        }
        float ax = x + 43f, ay = cy + 37f;
        if (filler) DrawFillerAvatar(ax, ay, e.Icon, alpha);
        else UiKit.FaceAvatar(_drawCanvas, new Vector2(ax, ay), 23f, e.Unlocked ? FaceFor(e.Id) : null, acc, false,
            0f, alpha, _t);
        float tx = x + 80f;
        string name = FitText(filler ? UiKit.Zen : UiKit.ZenBold, e.Name, 17, w - 186f);
        UiKit.Text(_drawCanvas, filler ? UiKit.Zen : UiKit.ZenBold, new Vector2(tx, cy + 17f), name, 17,
            new Color(UiKit.White, e.Unlocked ? alpha : alpha * 0.5f));
        if (e.Unlocked && !filler)
            UiKit.VerifiedBadge(_drawCanvas, new Vector2(tx + UiKit.TextW(UiKit.ZenBold, name, 17) + 13f, cy + 28f),
                6f, e.Cleared ? UiKit.Ok : UiKit.Purify, alpha);
        UiKit.Text(_drawCanvas, UiKit.Mono, new Vector2(tx, cy + 43f), FitText(UiKit.Mono, e.Handle, 12, w - 180f),
            12, new Color(UiKit.Text3, alpha));
        if (e.Unlocked) Metric(x + w - 84f, cy + 41f, e.Reach.Views, new Color(UiKit.Text3, alpha), views: true);
        if (e.Sort == Kind.Pinned && !e.IsRepost) DrawPinnedMark(x + w - 22f, cy + 19f, alpha);
        else if (e.Sort == Kind.Personal)
        {
            bool unread = IsUnreadReturnPost(e.Social!);
            UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x + w - 82f, cy + 20f), unread ? "新着" : e.RelT, 12,
                new Color(unread ? UiKit.Ok : UiKit.Text3, alpha), HorizontalAlignment.Right, 60f);
        }
        else if (e.IsFinal || e.Cleared)
            DrawBadgePill(e, e.IsFinal ? (e.Cleared ? "クリア済" : "限界") : "届いた", x + w - 22f, cy + 19f, alpha);
        else if (e.Unlocked)
            UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x + w - 82f, cy + 20f), e.RelT, 12,
                new Color(UiKit.Text4, alpha), HorizontalAlignment.Right, 60f);

        UiKit.Multi(_drawCanvas, UiKit.Zen, new Vector2(x + 24f, cy + 75f), e.Tweet, 17,
            new Color(UiKit.Text2, e.Unlocked ? alpha : alpha * 0.45f), w - 48f, e.Social != null ? 3 : 2);
        if (e.Social != null)
        {
            float mediaSpace = SnsPostMedia.Find(e.Id) != null ? PostMediaHeight + PostMediaGap : 0;
            if (mediaSpace > 0) _snsMedia.Draw(_drawCanvas, e.Id, new Rect2(x + 24, cy + PostMediaTop(e), w - 48, PostMediaHeight), alpha);
            if (e.Social.Replies.Length > 0)
            {
                var reply = e.Social.Replies[0];
                string preview = SnsVoices.All[reply.Author].Name + "  " + reply.Text;
                _drawCanvas.DrawLine(new Vector2(x + 30f, cy + 158f + mediaSpace), new Vector2(x + 30f, cy + 183f + mediaSpace), new Color(UiKit.Info, alpha * 0.4f), 2f);
                UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x + 42f, cy + 160f + mediaSpace),
                    FitText(UiKit.Zen, preview, 13, w - 66f), 13, new Color(UiKit.Text3, alpha));
            }
            UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(x + w - 144f, cy + h - 27f),
                e.Replies > 0 ? $"返信{e.Replies}件を読む ›" : "投稿を見る ›", 14,
                new Color(sel ? UiKit.White : UiKit.Info, alpha), HorizontalAlignment.Right, 120f);
        }
        bool buttons = sel && voice && e.Unlocked && _mode == Mode.Cards && !_autoplay;
        bool hoverHere = sel && e.Unlocked && HoverLineFor(e).Length > 0;
        if (voice && e.Unlocked)
        {
            if (e.VoiceReplies != null) DrawVoiceReplies(e, x, cy, alpha);
            else if (hoverHere) DrawHoverLine(e, x, cy, w, h, alpha);
            else RedactedBars(x + 24f, cy + 136f, w - 48f, alpha);
            // ボタンを出している間は BEST を少し上げる（下のボタン列と文字が触れないように）。
            if (!e.IsFinal && !hoverHere) DrawCardBest(e.Id, x + w - 24f, cy + h - (buttons ? 59f : 53f), alpha);
        }

        if (e.Unlocked)
        {
            float ey = cy + h - 25f, ex = x + 26f;
            if (e.IsFinal)
            {
                float accountX = MetricLabeled(ex, ey - 32, "フォロワー", _game?.Followers ?? 0, new Color(UiKit.Hp, alpha));
                MetricLabeled(accountX, ey - 32, "所持インプレ", _game?.Impression ?? 0, new Color(UiKit.Text3, alpha));
            }
            Metric(ex, ey, e.Replies, new Color(UiKit.Text3, alpha));
            for (int b = RepostButton; b <= LikeButton; b++)
                DrawReaction(index, b, cy + h, sel && _cardBtn == b, alpha);
        }
        if (buttons)
            for (int b = 0; b < CardBtnCount(e); b++) DrawCardButton(e, b, cy, h, alpha);
    }

    private SnsVoices.Reach DisplayReach(string id, SnsVoices.Reach reach) => reach with
    {
        Likes = reach.Likes + (_game.IsPostLiked(id) ? 1 : 0),
        Reposts = reach.Reposts + (_game.IsPostReposted(id) ? 1 : 0),
    };

    private void DrawReaction(int index, int button, float bottom, bool focused, float alpha)
    {
        var e = _entries[index];
        bool like = button == LikeButton;
        bool active = like ? _game.IsPostLiked(e.Id) : _game.IsPostReposted(e.Id);
        var reach = DisplayReach(e.Id, e.Reach);
        int count = like ? reach.Likes : reach.Reposts;
        var rect = ReactionRectAt(index, bottom, button);
        bool hovered = UiKit.HoveredId() == ReactionHotspot(index, button);
        Color color = new(like ? UiKit.Hp : UiKit.Ok, alpha);
        if (focused || hovered)
            UiKit.Box(_drawCanvas, rect, new Color(color, 0.12f * alpha), 4f,
                new Color(color, 0.5f * alpha), 1f);
        Vector2 origin = rect.Position + new Vector2(5, 5);
        Vector2 center = origin + new Vector2(8, 8);
        if (like) UiKit.Heart(_drawCanvas, center, 6f, color);
        else
        {
            _drawCanvas.DrawLine(center + new Vector2(-6, -3), center + new Vector2(5, -3), color, 1.4f);
            _drawCanvas.DrawLine(center + new Vector2(6, 3), center + new Vector2(-5, 3), color, 1.4f);
        }
        UiKit.Text(_drawCanvas, UiKit.Mono, origin + new Vector2(20, 0), UiKit.Abbrev(count), UiKit.FontLabel, color);
        if (active)
            _drawCanvas.DrawLine(new Vector2(rect.Position.X + 5, rect.End.Y), new Vector2(rect.End.X - 5, rect.End.Y), color, 2f);
    }

    // ───────── 声のカードの下部ボタン [ダイブ] [返信]（2026-09-27）─────────
    //   作者指摘「スマホの操作で J とか C とか書いてあるけど操作性悪すぎでしょ」で、項目ごとの文字キーをやめ、
    //   カードの上で選べる形にした。選択中の声のカードだけに出す（選ぶ前から全カードに並べると TL が操作パネルになる）。
    //   ←→ で切り替え・Z で実行。マウスはクリック（ホバーではカーソルを動かさない＝フッタと同じ作法）。
    private const int CardBtnIdBase = 11000;
    private const float CardBtnH = 26f, CardBtnGap = 8f, CardBtnRight = 22f, CardBtnBottom = 10f;
    private static readonly float[] CardBtnW = { 88f, 76f };
    private static readonly string[] CardBtnLabel = { "ダイブ", "返信" };
    private static Rect2 FeedRect => new(PhoneX, FeedTop, PhoneW, FeedBottom - FeedTop);

    // いまカード i に下部ボタンを出すか（選択中・カード側にカーソル・潜れる声のカード）。
    private bool CardButtonsShown(int i) => _mode == Mode.Cards && !_autoplay && i == _sel && _footSel < 0 && !_headFocus && IsVoice(i);

    // カードに出すボタンの数。FINAL（ミナ自身の投稿）は返信の相手がいない＝[ダイブ] だけ（2026-09-27）。
    //   返信を薄く並べると「いつか返信できる」と読めてしまうので、枠ごと出さない。
    private static int CardBtnCount(Entry e) => e.IsFinal ? 1 : 2;

    // カード i のボタン b の矩形（右下寄せ。メトリクス行の右側＝ビュー数を畳んだ場所）。
    private Rect2 CardBtnRect(int i, int b) => CardBtnRectAt(FeedTop + CardTop(i) - _feedScroll, CardHeight(_entries[i]), b,
        CardBtnCount(_entries[i]) == 1);

    // 上端 cy・高さ h のカードに置くボタン b の矩形（描画と当たり判定の単一ソース）。
    //   solo＝ボタンが [ダイブ] 1つだけ（FINAL）。そのときは右端へ寄せる（返信の空き枠を残さない）。
    private static Rect2 CardBtnRectAt(float cy, float h, int b, bool solo = false)
    {
        float right = PhoneX + PhoneW - CardBtnRight;
        float x = b == 1 || solo ? right - CardBtnW[b] : right - CardBtnW[1] - CardBtnGap - CardBtnW[0];
        return new Rect2(x, cy + h - CardBtnBottom - CardBtnH, CardBtnW[b], CardBtnH);
    }

    private void DrawCardButton(Entry e, int b, float cy, float h, float alpha)
    {
        var r = CardBtnRectAt(cy, h, b, CardBtnCount(e) == 1);
        bool enabled = b == 0 || CanReplySel();
        bool focus = enabled && _cardBtn == b;
        bool hovered = UiKit.HoveredId() == CardBtnIdBase + b;
        Color acc = b == 0 ? (e.IsFinal ? UiKit.Kegare : UiKit.Purify) : UiKit.Info;
        float a = alpha * (enabled ? 1f : 0.35f);
        // フォーカス＝フッタのカーソルと同じ浄化色の縁と薄い塗り。ホバーは白の下敷きだけ。
        if (focus) UiKit.Box(_drawCanvas, r, new Color(acc, 0.16f * a), CardBtnH / 2f, new Color(acc, 0.85f * a), 1.4f);
        else UiKit.Box(_drawCanvas, r, new Color(1, 1, 1, (hovered && enabled ? 0.08f : 0.03f) * a), CardBtnH / 2f,
            new Color(UiKit.Text4, 0.55f * a), 1f);
        Color ink = focus ? UiKit.White : enabled ? (hovered ? UiKit.White : UiKit.Text2) : UiKit.Text4;
        float asc = UiKit.ZenBold.GetAscent(14), desc = UiKit.ZenBold.GetDescent(14);
        _drawCanvas.DrawString(UiKit.ZenBold, new Vector2(r.Position.X, r.Position.Y + (r.Size.Y + asc - desc) / 2f), CardBtnLabel[b],
            HorizontalAlignment.Center, r.Size.X, 14, new Color(ink, a));
    }

    // カードの左バー／アバターのリング色。埋め草は色を持たない他人＝くすんだ灰。
    private static Color BarColorFor(Entry e) => e.Sort switch
    {
        Kind.Filler => UiKit.Text4,
        Kind.Personal => AccountColor(e.Social!.CharacterId),
        Kind.Pinned => UiKit.Mina,
        _ => e.IsFinal ? UiKit.Kegare : AccountColor(e.Id),
    };

    // 正方形のアイコンは topCrop=0 で全面を丸窓に収める。
    private void DrawFillerAvatar(float cx, float cy, int icon, float alpha)
    {
        var tex = MobIcon(icon);
        if (tex != null)
        {
            UiKit.FaceAvatar(_drawCanvas, new Vector2(cx, cy), 24f, tex, new Color(UiKit.Text4, 0.55f), false, 0f, alpha * 0.92f, _t);
            return;
        }
        _drawCanvas.DrawCircle(new Vector2(cx, cy), 24f, new Color(0.16f, 0.15f, 0.21f, 0.9f * alpha));
        _drawCanvas.DrawArc(new Vector2(cx, cy), 24f, 0f, Mathf.Tau, 28, new Color(1, 1, 1, 0.08f * alpha), 1f);
        var sil = new Color(UiKit.Text4, 0.5f * alpha);
        _drawCanvas.DrawCircle(new Vector2(cx, cy - 5f), 7f, sil);                       // 頭
        _drawCanvas.DrawColoredPolygon(new[] { new Vector2(cx - 11f, cy + 13f), new Vector2(cx + 11f, cy + 13f),
                                   new Vector2(cx + 8f, cy + 4f), new Vector2(cx - 8f, cy + 4f) }, sil);  // 肩
    }
    // 固定ポストの印（X の pinned post）。ピルではなく、小さなピンと「固定」の一語だけ置く。
    private void DrawPinnedMark(float right, float y, float alpha)
    {
        const string s = "固定";
        float tw = UiKit.TextW(UiKit.Zen, s, UiKit.FontSmall);
        float x = right - tw;
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x, y + 2f), s, UiKit.FontSmall, new Color(UiKit.Text3, 0.9f * alpha));
        // ピン＝頭の丸と細い軸（ラベルの左に控えめに）。
        _drawCanvas.DrawCircle(new Vector2(x - 12f, y + 6f), 3f, new Color(UiKit.Mina, 0.8f * alpha));
        _drawCanvas.DrawLine(new Vector2(x - 12f, y + 8f), new Vector2(x - 12f, y + 15f), new Color(UiKit.Mina, 0.6f * alpha), 1.4f);
    }

    // 声のあるカードにカーソルが乗ったときの、カード下部のミナの一行（2-b）。
    //   文言は既存の行の転用のみ（新しい台詞は書かない）。あかりの初回だけ H0Dialog の2行目を使い、
    //   一度きりの H0 会話（旧 Hub.cs の入場ダイアログ）はここへ吸収した。
    private void DrawHoverLine(Entry e, float x, float cy, float w, float h, float alpha)
    {
        string line = HoverLineFor(e);
        if (line.Length == 0) return;
        UiKit.FaceAvatar(_drawCanvas, new Vector2(x + 35f, cy + 158f), 10f, _playerFaces["mina"], UiKit.Mina, false, 0f, alpha, _t);
        UiKit.Multi(_drawCanvas, UiKit.Zen, new Vector2(x + 56f, cy + 146f), line, 14,
            new Color(UiKit.Mina, alpha), w - 80f, 2);
    }

    // ホバー行の文言。既存の台詞からの転用に限る（新規の台詞は書かない）。
    private string HoverLineFor(Entry e)
    {
        if (e.Sort == Kind.Filler) return "";      // 埋め草＝声の聞こえない側の一言
        if (e.Cleared) return "";                                                     // 届いた投稿には、もう言うことがない
        if (e.IsFinal) return "";
        // あかりの初回＝H0（仮台本 06）の2行目をそのまま置く。旧実装の入場ダイアログの代わり。
        if (e.Id == "akari") return "……送れなかった言葉も、聞こえます。";
        if (e.Id == "koharu") return "学校の話になったとき、少し声が小さくなりました。";
        return "次の声も、もう、聞こえています。";
    }

    // カード右下のベストタイム表示。最速難易度のベスト＋小さな難易度ラベル。
    private void DrawCardBest(string id, float right, float y, float alpha)
    {
        var best = _game?.BestAcrossDiffs(id);
        string timeStr = best != null ? UiKit.FormatTime(best.Value.sec) : "--";
        string diffStr = best != null ? DiffShort(best.Value.diff) : "";
        // 「BEST 1:23.45  NORMAL」を右揃えで一行。
        // ラベル(BEST/難易度)は英字ラベル＝ZenBold の小ラベル、タイムは「値」＝Mono のまま。
        float tw = UiKit.TextW(UiKit.Mono, timeStr, UiKit.FontLabel);
        float dw = diffStr.Length > 0 ? UiKit.TrackedW(UiKit.SmallLabel, diffStr) + 8 : 0;
        float lw = UiKit.TrackedW(UiKit.SmallLabel, "BEST") + 6;
        float x0 = right - (lw + tw + dw);
        Color tc = best != null ? UiKit.Gold : UiKit.Text4;
        UiKit.Draw(_drawCanvas, UiKit.SmallLabel, new Vector2(x0, y + 1), "BEST", new Color(UiKit.Text3, alpha));
        UiKit.Text(_drawCanvas, UiKit.Mono, new Vector2(x0 + lw, y), timeStr, UiKit.FontLabel, new Color(tc, alpha));
        if (diffStr.Length > 0)
            UiKit.Draw(_drawCanvas, UiKit.SmallLabel, new Vector2(x0 + lw + tw + 8, y + 1), diffStr, new Color(UiKit.Info, alpha));
    }

    private static string DiffShort(GameManager.Diff d) => d switch
    {
        GameManager.Diff.Easy => "EASY",
        GameManager.Diff.Hard => "HARD",
        GameManager.Diff.Lunatic => "LUNA",
        _ => "NORMAL",
    };

    // カード右上のステータスピル。声／限界 は塗り（明滅）、届いた は枠。ロックはそもそも呼ばない（ピル無し）。
    //   ラベルは日本語なので Mono ではなく Zen で測って描く（Mono に和文グリフが無く幅がずれる）。
    private void DrawBadgePill(Entry e, string badge, float right, float y, float alpha)
    {
        float bw = UiKit.TextW(UiKit.ZenBold, badge, UiKit.FontSmall) + 22f;
        var r = new Rect2(right - bw, y, bw, 20f);
        if (e.IsFinal && !e.Cleared)
        {
            float pulse = 0.72f + 0.20f * Mathf.Sin((float)_t * 2.6f);
            UiKit.Box(_drawCanvas, r, new Color(UiKit.Kegare, pulse * alpha), 10f);
            UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(r.Position.X, y + 2f), badge, UiKit.FontSmall, new Color(UiKit.BgDeep, alpha), HorizontalAlignment.Center, bw);
        }
        else if (e.Cleared)
        {
            UiKit.Box(_drawCanvas, r, new Color(UiKit.Ok, 0.10f * alpha), 10f, new Color(UiKit.Ok, 0.55f * alpha), 1f);
            UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(r.Position.X, y + 2f), badge, UiKit.FontSmall, new Color(UiKit.Ok, alpha), HorizontalAlignment.Center, bw);
        }
        else
        {
            // 「声」＝これから潜る投稿。塗りピルの淡い明滅で「ここへ」を誘導（常時アニメはこれと選択グロウのみ）。
            float pulse = 0.74f + 0.18f * Mathf.Sin((float)_t * 3.0f);
            UiKit.Box(_drawCanvas, r, new Color(UiKit.Purify, pulse * alpha), 10f);
            UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(r.Position.X, y + 2f), badge, UiKit.FontSmall, new Color(UiKit.BgDeep, alpha), HorizontalAlignment.Center, bw);
        }
    }

    private const float VoiceRepliesTop = 132f;
    private const int VoiceReplyFontSize = 14;
    private static float VoiceReplyLineHeight => UiKit.Zen.GetHeight(VoiceReplyFontSize) + 2f;

    private VoiceReply[] BuildVoiceReplies(string id)
    {
        var rows = new System.Collections.Generic.List<VoiceReply>();
        float y = VoiceRepliesTop;
        foreach (var (speaker, text) in _game.SentReplies(id).Prepend(ReturnDialog(id)[0]))
        {
            var lines = UiKit.WrapLines(UiKit.Zen, text, VoiceReplyFontSize, PhoneW - 80f).ToArray();
            y += 24f + lines.Length * VoiceReplyLineHeight;
            rows.Add(new VoiceReply(speaker.StartsWith("ミナ", System.StringComparison.Ordinal) ? "mina" : id, lines, y));
        }
        return rows.ToArray();
    }

    private void DrawVoiceReplies(Entry e, float x, float cy, float alpha)
    {
        float y = cy + VoiceRepliesTop;
        foreach (var reply in e.VoiceReplies!)
        {
            bool mina = reply.AuthorId == "mina";
            Color color = mina ? UiKit.Mina : AccountColor(reply.AuthorId);
            UiKit.FaceAvatar(_drawCanvas, new Vector2(x + 35f, y + 12f), 10f, _playerFaces[reply.AuthorId], color, false, 0f, alpha, _t);
            UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(x + 56f, y), mina ? "ミナ" : e.Name, 13, new Color(color, alpha));
            y += 20f;
            foreach (string line in reply.Lines)
            {
                UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x + 56f, y), line, VoiceReplyFontSize, new Color(UiKit.Text2, alpha));
                y += VoiceReplyLineHeight;
            }
            y = cy + reply.Bottom;
        }
    }

    // 伏字バー（2-a で用途を反転）。声のあるカードの本文の下に薄く明滅させ、
    //   「この投稿には、消された一行がある」だけを言う。読ませない・説明しない。
    //   明滅は周期 2.6s のごく浅い呼吸（0.045〜0.085α）。カード全体のノイズにならない量に抑える。
    private void RedactedBars(float x, float y, float w, float alpha)
    {
        float pulse = 0.045f + 0.040f * (0.5f + 0.5f * Mathf.Sin((float)_t * Mathf.Tau / 2.6f));
        var c = new Color(1, 1, 1, pulse * alpha);
        _drawCanvas.DrawRect(new Rect2(x, y, w * 0.52f, 7f), c);
        _drawCanvas.DrawRect(new Rect2(x, y + 13f, w * 0.31f, 7f), c);
    }

    // クリア順に基づくゆるい相対時刻（X風メタ表示・実害なし）。
    private static string RelTime(string id) => id switch
    {
        "rei" => "3h", "akari" => "5h", "koharu" => "1d", _ => "now",
    };

    private void Metric(float x, float y, long count, Color col, bool views = false)
    {
        var c = new Vector2(x + 8, y + 8);
        if (views)
        {
            _drawCanvas.DrawRect(new Rect2(c.X - 6, c.Y + 1, 2.4f, 4f), col);
            _drawCanvas.DrawRect(new Rect2(c.X - 2, c.Y - 2, 2.4f, 7f), col);
            _drawCanvas.DrawRect(new Rect2(c.X + 2, c.Y - 5, 2.4f, 10f), col);
        }
        else UiKit.Box(_drawCanvas, new Rect2(c.X - 7, c.Y - 6, 14, 10), null, 3f, col, 1.4f);
        string s = UiKit.Abbrev(count);
        UiKit.Text(_drawCanvas, UiKit.Mono, new Vector2(x + 20, y), s, UiKit.FontLabel, col);
    }

    // 記号の代わりに語で意味を示す指標（FINAL カードのフォロワー／インプレ）。次の x を返す。
    //   Metric と同じ行・同じフォントサイズに揃え、ラベルだけ一段小さく前に置く。
    private float MetricLabeled(float x, float y, string label, long count, Color col)
    {
        float lw = UiKit.TextW(UiKit.Zen, label, UiKit.FontSmall);
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x, y + 2f), label, UiKit.FontSmall, new Color(col, col.A * 0.8f));
        string s = UiKit.Abbrev(count);
        UiKit.Text(_drawCanvas, UiKit.Mono, new Vector2(x + lw + 6f, y), s, UiKit.FontLabel, col);
        return x + lw + 6f + UiKit.TextW(UiKit.Mono, s, UiKit.FontLabel) + 26;
    }


    // ── フッタ操作（クリック可能なショップ入口ほか）──
    //   フェーズ2のカードクリックに続き、フッタの操作ヒントもマウスで押せるようにする。
    //   最優先は「強化」＝ショップ入口ボタン。「返信/記録」も揃えて（余力）クリック可に。
    //   ・「えらぶ」はナビ表示のみ＝クリック対象外。「ダイブ」はカードクリックで足りるので表示のみ。
    //   ・レイアウトは DrawFooter と単一ソース化（FooterItems を DrawFooter とホットスポット登録で共用）。
    //     フッタ id は カード id(0..entries) と衝突しないよう FooterIdBase から採番する。
    private enum FootAct { Job, Home, Voice, Reposts }
    private const int FooterIdBase = 10000;

    // 現在のフッタ項目（表示順）。label/accent＝見た目、act＝クリック時のアクション。
    //   2026-09-27：キー表記（X／C／J）を貼るのをやめ、「ホーム」「アカウント」の2項目だけにした。
    //   返信はカード下部の [返信] へ移った。キーの案内は画面下端のヒント帯が持つ。
    private System.Collections.Generic.List<(string label, bool accent, FootAct act)> FooterItems()
    {
        // ジョブは解禁ゲート無し＝初回訪問から出す（設計書 §6：ショップは1面ボスまで開かないので、
        //   ハブに置かないと最初のダイブ前に一度も選べない）。
        var items = new System.Collections.Generic.List<(string, bool, FootAct)>
        {
            ("ホーム", false, FootAct.Home),
            ("アカウント", false, FootAct.Job),
            ("ボスステージ", true, FootAct.Voice),
        };
        if (_entries.Length > 0 && _entries[0].IsRepost) items.Add(("リツイート", false, FootAct.Reposts));
        return items;
    }

    private Rect2 FooterItemRect(int i)
    {
        float width = (PhoneW - 24f) / FooterItems().Count;
        return new Rect2(PhoneX + 12f + i * width, FeedBottom + 7f, width, 60f);
    }

    private void DrawFooter()
    {
        _drawCanvas.DrawRect(new Rect2(PhoneX, FeedBottom, PhoneW, 1f), new Color(1, 1, 1, 0.12f));
        var items = FooterItems();
        for (int i = 0; i < items.Count; i++)
        {
            var (label, accent, act) = items[i];
            var rect = FooterItemRect(i);
            // マウスのホバーと、キーボード／パッドのカーソル（_footSel）を同じ見え方にする。
            bool hovered = UiKit.HoveredId() == FooterIdBase + i || (_mode == Mode.Cards && _footSel == i);
            bool nudge = act == FootAct.Job && AccountNudge;
            Color col = accent || nudge ? UiKit.Purify : hovered ? UiKit.White : UiKit.Text3;
            // キーボード／パッドのカーソルはカードの選択枠と同じ浄化色の縁で見せる（マウスのホバーより強く＝
            //   「いまカーソルがここにある」がカードから降りた瞬間に分かる）。
            if (_mode == Mode.Cards && _footSel == i)
                UiKit.Box(_drawCanvas, rect, new Color(UiKit.Purify, 0.10f), 8f, new Color(UiKit.Purify, 0.6f), 1f);
            else if (hovered) UiKit.Box(_drawCanvas, rect, new Color(1, 1, 1, 0.05f), 8f);
            float iconX = items.Count == 4 ? 16f : 26f;
            Vector2 c = rect.Position + new Vector2(iconX, 24f);
            // アカウント追加の説明を読んだ直後だけの誘導（2026-09-23）：フッタ「アカウント」の顔アイコンから
            //   外へ広がりながら薄くなる輪を2枚ずらして重ねる＝ホームの強化アイコン（_shopNudge）と同じ作法。
            //   台詞は足していない（説明の2行目「SNSの下、「アカウント」から」が場所をもう言っている）。
            if (nudge)
            {
                for (int ring = 0; ring < 2; ring++)
                {
                    float phase = Mathf.PosMod((float)_t * 0.8f + ring * 0.5f, 1f);
                    _drawCanvas.DrawArc(c, 17f + phase * 14f, 0f, Mathf.Tau, 48, new Color(UiKit.Purify, (1f - phase) * 0.55f), 2f, true);
                }
            }
            DrawFooterIcon(act, c, col);
            // キー表記は貼らない（2026-09-27 作者指示。案内は画面下端のヒント帯に1行でまとめた）。
            float labelX = iconX + 18f;
            UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(rect.Position.X + labelX, rect.Position.Y + 16f), label, 12, col,
                HorizontalAlignment.Left, rect.Size.X - labelX - 4f);
        }
    }


    private void DrawToast()
    {
        if (_toastT <= 0) return;
        bool besidePhone = _limeConversation || MinaLineOpen;
        float w = besidePhone ? PhoneX - 40f : PhoneW - 32f;
        int titleSize = _prominentToast ? (besidePhone ? 19 : 22) : 15, subSize = _prominentToast ? 16 : 12;
        var lines = UiKit.WrapLines(UiKit.ZenBold, _toast, titleSize, w - 32f);
        var sub = UiKit.WrapLines(UiKit.Zen, _toastSub, subSize, w - 32f);
        float titleHeight = lines.Count * UiKit.ZenBold.GetHeight(titleSize);
        float h = 24f + titleHeight + (_toastSub.Length > 0 ? sub.Count * UiKit.Zen.GetHeight(subSize) + 6f : 0f);
        float x = besidePhone ? 20f : PhoneX + 16f, y = besidePhone ? LimeHeaderY + 8
            : _mode == Mode.Dialogue ? FeedTop + 12f : FeedBottom - h - 8f;
        if (_mode == Mode.Dialogue && !besidePhone && _dlgReturnMode == Mode.Home && !_dlgNoPost)
            y = DialogBox.Position.Y - h - 12f;
        UiKit.Box(_drawCanvas, new Rect2(x, y, w, h), PhoneRaised, 8f, new Color(_toastCol, 0.6f), 1f);
        UiKit.Multi(_drawCanvas, UiKit.ZenBold, new Vector2(x + 16f, y + 12f), _toast, titleSize, _toastCol, w - 32f);
        if (_toastSub.Length > 0)
            UiKit.Multi(_drawCanvas, UiKit.Zen, new Vector2(x + 16f, y + 18f + titleHeight), _toastSub, subSize, _prominentToast ? UiKit.Text2 : UiKit.Text3, w - 32f);
    }

    // 小話の話者文字列 → (立ち絵, 円窓のリング色, topCrop)。本編会話と同じ FaceAvatar で顔を出す。
    //   top の負値は「顔を描かない」印：-1＝アバターごと省略 / DraftTop＝人物シルエット。
    private const float DraftTop = -2f;
    private (Texture2D? face, Color col, float top) SpeakerFace(string sp)
    {
        // 「Ｙ 投稿」「Ｙ システム」＝顔の無い枠（他人の引用・システム表示）。top に負値を返して
        //   DrawDialog にアバターごと省かせる（null のままだと FaceAvatar が「?」のロック円を描き、
        //   未解放カードと同じ見た目になってしまう）。H0 の投稿、H2 の炎上の引用、H3 の FINAL カードがここ。
        if (sp.StartsWith("Ｙ")) return (null, UiKit.Info, -1f);
        if (sp == "あなた") return (null, UiKit.CutWarm, DraftTop);
        if (sp.StartsWith("ミナ")) return (_minaFace, UiKit.Mina, TopCropFor("mina"));
        foreach (var job in Jobs.All)
            if (sp == job.CharacterName) return (_dialogueFaces[job.CharacterId], CompanionDialogue.Accent(job.Id), TopCropFor(job.CharacterId));
        string plain = Handles.Plain(sp);   // 化けた綴り（@rëi_____ 等）を戻してから部分文字列を見る
        if (plain.Contains("rei")) return (_dialogueFaces["rei"], AccountColor("rei"), TopCropFor("rei"));
        if (plain.Contains("akari")) return (_dialogueFaces["akari"], AccountColor("akari"), TopCropFor("akari"));
        if (plain.Contains("koharu")) return (_dialogueFaces["koharu"], AccountColor("koharu"), TopCropFor("koharu"));
        return (null, AccountColor("rei"), 0.06f);
    }

    private void DrawDetail()
    {
        var e = _entries[Mathf.Clamp(_sel, 0, _entries.Length - 1)];
        bool tiers = !NeedsFinalAccount;
        float a = Mathf.Clamp((float)_detailT / 0.18f, 0f, 1f);
        var (cx, cy, cw, ch) = DetailBox(tiers);
        Color acc = e.IsFinal ? UiKit.Kegare : AccountColor(e.Id);
        _drawCanvas.DrawRect(new Rect2(PhoneX, 0, PhoneW, H), new Color(0, 0, 0, 0.5f * a));
        UiKit.Box(_drawCanvas, new Rect2(cx, cy, cw, ch), new Color(PhoneBg, a), 8f);
        DrawBackButton(DetailCloseRect(tiers), DetailCloseId, a);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(cx + 60f, cy + 21f), "投稿", 20, new Color(UiKit.White, a));
        DrawJobButton(DetailJobRect(tiers), a);
        UiKit.FaceAvatar(_drawCanvas, new Vector2(cx + 47f, cy + 91f), 23f, FaceFor(e.Id), acc, false, 0f, a, _t);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(cx + 84f, cy + 70f), e.Name, 19, new Color(UiKit.White, a));
        UiKit.VerifiedBadge(_drawCanvas, new Vector2(cx + 97f + UiKit.TextW(UiKit.ZenBold, e.Name, 19), cy + 82f), 6f, e.Cleared ? UiKit.Ok : UiKit.Purify, a);
        UiKit.Text(_drawCanvas, UiKit.Mono, new Vector2(cx + 84f, cy + 98f), e.Handle + " · " + e.RelT, 12, new Color(UiKit.Text3, a));
        if (e.IsFinal || e.Cleared) DrawBadgePill(e, e.IsFinal ? (e.Cleared ? "クリア済" : "限界") : "届いた", cx + cw - 24f, cy + 74f, a);
        UiKit.Multi(_drawCanvas, UiKit.Zen, new Vector2(cx + 24f, cy + 135f), e.Tweet, 18, new Color(UiKit.Text2, a), cw - 48f, 3);
        RedactedBars(cx + 24f, cy + 226f, cw - 48f, a);
        string quip = HoverLineFor(e);
        if (!e.IsFinal || e.Cleared)
        {
            if (quip.Length == 0) quip = "……届きました。";
            UiKit.FaceAvatar(_drawCanvas, new Vector2(cx + 35f, cy + 274f), 10f, _playerFaces["mina"], UiKit.Mina, false, 0f, a, _t);
            UiKit.Multi(_drawCanvas, UiKit.Zen, new Vector2(cx + 56f, cy + 260f), quip, 14, new Color(UiKit.Mina, a), cw - 80f, 2);
        }
        if (tiers)
        {
            // 2026-09-17 ユーザー指示：「潜り方」の見出しを削除。区切り線は本文と段の境目として残す
            //   （線まで消すと、投稿本文と難易度の一覧が地続きに見えて読み分けられない）。
            _drawCanvas.DrawRect(new Rect2(cx + 24f, cy + 305f, cw - 48f, 1f), new Color(1, 1, 1, 0.09f * a));
            for (int i = 0; i < Tiers.Length; i++) DrawTier(i, cx + 24f, cy + TierTop + i * 68f, cw - 48f, 62f, a);
        }
        else if (NeedsFinalAccount)
        {
            _drawCanvas.DrawRect(new Rect2(cx + 24f, cy + 320f, cw - 48f, 1f), new Color(1, 1, 1, 0.09f * a));
            UiKit.Box(_drawCanvas, new Rect2(cx + 24f, cy + 344f, cw - 48f, 76f), new Color(UiKit.Kegare, 0.10f * a), 8f,
                new Color(UiKit.Kegare, 0.5f * a), 1f);
            UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(cx + 40f, cy + 356f), GameManager.MinaFinalLockHint, 15,
                new Color(UiKit.Kegare, a));
            UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(cx + 40f, cy + 384f),
                "あかり／こはる／レイ のどなたかで、潜ってください。", 13, new Color(UiKit.Text2, a));
        }
        if (!tiers)
            DrawPrimaryButton(FinalDiveRect(), NeedsFinalAccount ? "アカウントを選ぶ" : "投稿に潜る",
                FinalDiveId, NeedsFinalAccount ? UiKit.Purify : acc, a);
    }

    private void DrawJobButton(Rect2 rect, float alpha = 1f)
    {
        var job = _game.JobDef;
        Color acc = JobColor(job.Id);
        bool header = rect.Size.Y > 40f;
        float tx = header ? 62f : 44f;
        int nameSize = header ? 19 : 14;
        // キーボード／パッドのカーソルがヘッダーにあるとき＝フッタのカーソルと同じ浄化色の縁と薄い塗り（2026-09-27）。
        //   マウスのホバーは従来どおり白の下敷きだけ（カーソルの方が強く見える＝フッタと同じ強弱）。
        if (header && _headFocus && _mode == Mode.Cards)
            UiKit.Box(_drawCanvas, rect, new Color(UiKit.Purify, 0.10f * alpha), 8f, new Color(UiKit.Purify, 0.6f * alpha), 1f);
        else if (UiKit.HoveredId() == JobOpenId) UiKit.Box(_drawCanvas, rect, new Color(1, 1, 1, 0.06f * alpha), 8f);
        UiKit.FaceAvatar(_drawCanvas, rect.Position + new Vector2(header ? 28f : 20f, rect.Size.Y / 2f), header ? 20f : 15f,
            _playerFaces[job.CharacterId], acc, false, 0f, alpha, _t);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, rect.Position + new Vector2(tx, header ? 6f : 2f), job.CharacterName, nameSize, new Color(UiKit.White, alpha));
        float nameW = UiKit.TextW(UiKit.ZenBold, job.CharacterName, nameSize);
        UiKit.VerifiedBadge(_drawCanvas, rect.Position + new Vector2(tx + nameW + 12f, header ? 19f : 12f), header ? 7f : 5f, new Color(UiKit.Purify, alpha));
        UiKit.Text(_drawCanvas, UiKit.Mono, rect.Position + new Vector2(tx, header ? 32f : 23f), AccountHandle(job), header ? 12 : 11, new Color(UiKit.Text3, alpha));
        Vector2 p = rect.Position + new Vector2(rect.Size.X - 12f, rect.Size.Y / 2f);
        _drawCanvas.DrawPolyline(new[] { p + new Vector2(-4, -2), p + new Vector2(0, 2), p + new Vector2(4, -2) }, new Color(UiKit.Text3, alpha), 1.5f, true);
    }

    private static string AccountHandle(JobTuning job) => job.Id == Job.Tank
        ? Handles.Mina
        : System.Array.Find(GameManager.Stages, stage => stage.Id == job.CharacterId)!.Handle;

    // 潜り方の1段。名前／獲得倍率／板の枚数（ボスHPバー本数）。
    //   2026-09-17: ミナの一言（Quip）を落として1行の行になったので、中身は行の縦中央に揃える
    //   （h=62 のまま段の間隔は変えない＝TierHitRect と DrawDetail の座標式を触らずに済む）。
    private void DrawTier(int i, float x, float y, float w, float h, float alpha)
    {
        var tr = Tiers[i];
        bool sel = i == _tierSel, open = TierOpen(i);
        float mid = y + h / 2f;   // 行の縦中央（1行になったぶん、ここへ寄せる）
        Color acc = tr.Diff == GameManager.Diff.Lunatic ? UiKit.Kegare : tr.Diff == GameManager.Diff.Hard ? UiKit.Gold : UiKit.Purify;
        if (sel && open) UiKit.Box(_drawCanvas, new Rect2(x, y, w, h), new Color(acc, 0.10f * alpha), 8f, new Color(acc, 0.6f * alpha), 1f);
        else _drawCanvas.DrawRect(new Rect2(x + 12f, y + h - 1f, w - 24f, 1f), new Color(1, 1, 1, 0.06f * alpha));
        _drawCanvas.DrawArc(new Vector2(x + 18f, mid), 5f, 0f, Mathf.Tau, 20, new Color(open ? acc : UiKit.Text4, alpha), 1.5f, true);
        if (sel && open) _drawCanvas.DrawCircle(new Vector2(x + 18f, mid), 2.5f, new Color(acc, alpha));
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(x + 33f, mid - 12f), tr.Name, 17, new Color(open ? UiKit.White : UiKit.Text4, alpha));
        if (!open)
        {
            UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x + 110f, mid - 8f),
                GameManager.LunaticUnlockHint,
                12, new Color(UiKit.Text4, alpha), HorizontalAlignment.Right, w - 126f);
            return;
        }
        // ♥・ボムの基礎値は全難易度3/1で共通になった（2026-09-15）ので賭け金表示から外し、
        // 難易度で本当に変わる獲得倍率だけを出す。
        string stake = $"×{GameManager.DifficultyImpressionMulFor(tr.Diff):0.0}";
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x + 132f, mid - 7f), stake, 12, new Color(UiKit.Hp, alpha), HorizontalAlignment.Right, w - 190f);
        int bars = tr.Diff switch { GameManager.Diff.Easy => 2, GameManager.Diff.Hard => 5, GameManager.Diff.Lunatic => 6, _ => 4 };
        for (int b = 0; b < bars; b++) _drawCanvas.DrawRect(new Rect2(x + w - 18f - (bars - b) * 7f, mid - 5f, 3f, 11f), new Color(acc, 0.6f * alpha));
    }

    // ───────── ジョブ選択（設計書 §6）─────────
    // ハブのフッタ「ジョブ」から開くオーバーレイ。作法は投稿詳細（Detail）と完全に同じ＝
    //   ↑↓ で段を選び Z で確定・X／「とじる」で戻る、マウスはホットスポット＋ホバー追従、ホイールは使わない
    //   （段が4つで画面に収まるため）。ショップとは一切繋がない＝ここは「型を選ぶ」だけの枠。
    //
    // 【ここが持ってはいけないもの】
    //   ・ショップ（強化）への導線。一本道ショップに枝を作らない（設計書 §7 第3段の領分）。
    //   ・ジョブ補正の数値そのもの。表示値は JobTuning の実設定から組み立てる。
    //
    // ラン中は開かない：この画面はハブのシーンにしか存在せず、ステージ側に SelectedJob を書く導線も無い
    //   （grep 済み：GameManager / TrainingRoot 以外に代入無し）＝「選んだらそのランは変えられない」。
    private const int JobIdBase = 21000, JobCloseId = 21900, JobConfirmId = 21901;

    // ジョブの色。既存の語彙から取る＝灯し手=灯(Light)／祈り手=浄化(Purify)／結び手=ミナ紫／語り手=金。
    private static Color JobColor(Job j) => j switch
    {
        Job.Melee => UiKit.Light,
        Job.Heal => UiKit.Purify,
        Job.Magic => UiKit.Gold,
        _ => UiKit.Mina,
    };

    private const float JobRowH = 48f, JobPanelH = 350f, JobPanelGap = 10f;

    private (float cx, float cy, float cw, float ch) JobBox()
    {
        float height = 68f + _jobChoices.Length * JobRowH + JobPanelGap + JobPanelH + 74f;
        return (PhoneX, H - 16f - height, PhoneW, height);
    }

    // ジョブ i 段目の矩形（DrawJob の DrawJobRow 呼び出しと同じ x/y/w/h）。展開の浮きは無視する
    //   ＝開いた直後でもクリック位置が動かない（Detail の TierHitRect と同じ考え方）。
    private Rect2 JobHitRect(int i)
    {
        var (cx, cy, cw, _) = JobBox();
        return new Rect2(cx + 24f, cy + 68f + i * JobRowH, cw - 48f, JobRowH);
    }

    // 選択中の段の性能を出す詳細パネル（段の並びの直下）。
    private Rect2 JobPanelRect()
    {
        var (cx, cy, cw, _) = JobBox();
        return new Rect2(cx + 24f, cy + 68f + _jobChoices.Length * JobRowH + JobPanelGap, cw - 48f, JobPanelH);
    }

    private Rect2 JobCloseRect()
    {
        var (cx, cy, _, _) = JobBox();
        return new Rect2(cx + 12f, cy + 12f, 40f, 40f);
    }

    private Rect2 JobConfirmRect()
    {
        var (cx, cy, cw, ch) = JobBox();
        return new Rect2(cx + 24f, cy + ch - 58f, cw - 48f, 42f);
    }

    private void DrawBackButton(Rect2 rect, int id, float alpha)
    {
        if (UiKit.HoveredId() == id) UiKit.Box(_drawCanvas, rect, new Color(1, 1, 1, 0.06f * alpha), 8f);
        Vector2 c = rect.GetCenter();
        _drawCanvas.DrawPolyline(new[] { c + new Vector2(3, -7), c + new Vector2(-4, 0), c + new Vector2(3, 7) },
            new Color(UiKit.Text2, alpha), 2f, true);
    }

    private void DrawPrimaryButton(Rect2 rect, string label, int id, Color accent, float alpha)
    {
        bool hovered = UiKit.HoveredId() == id;
        UiKit.Box(_drawCanvas, rect, new Color(accent, (hovered ? 0.24f : 0.14f) * alpha), 8f, new Color(accent, 0.6f * alpha), 1f);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, rect.Position + new Vector2(0, 10f), label, 16,
            new Color(UiKit.White, alpha), HorizontalAlignment.Center, rect.Size.X);
    }

    private void DrawFooterIcon(FootAct act, Vector2 c, Color color)
    {
        switch (act)
        {
            case FootAct.Reposts:
                _drawCanvas.DrawPolyline(new[] { c + new Vector2(-8, 4), c + new Vector2(-8, -4), c + new Vector2(7, -4),
                    c + new Vector2(3, -8) }, color, 1.6f, true);
                _drawCanvas.DrawPolyline(new[] { c + new Vector2(8, -4), c + new Vector2(8, 4), c + new Vector2(-7, 4),
                    c + new Vector2(-3, 8) }, color, 1.6f, true);
                break;
            case FootAct.Voice:
                _drawCanvas.DrawArc(c, 9f, 0, Mathf.Tau, 24, color, 1.8f, true);
                _drawCanvas.DrawLine(c + new Vector2(7, 7), c + new Vector2(13, 13), color, 1.8f, true);
                break;
            case FootAct.Job:
                UiKit.FaceAvatar(_drawCanvas, c, 13f, _playerFaces[_game.JobDef.CharacterId], JobColor(_game.SelectedJob), false, 0f, 1f, _t);
                break;
            case FootAct.Home:
                _drawCanvas.DrawPolyline(new[] { c + new Vector2(-11, -1), c + new Vector2(0, -10), c + new Vector2(11, -1) }, color, 1.8f, true);
                _drawCanvas.DrawPolyline(new[] { c + new Vector2(-8, -3), c + new Vector2(-8, 9), c + new Vector2(-3, 9), c + new Vector2(-3, 2),
                    c + new Vector2(3, 2), c + new Vector2(3, 9), c + new Vector2(8, 9), c + new Vector2(8, -3) }, color, 1.8f, true);
                break;
        }
    }

    private bool SelIsFinal => _sel >= 0 && _sel < _entries.Length && _entries[_sel].IsFinal;
    private bool MinaBlockedHere => SelIsFinal && (_game?.IsMinaLockedForFinal ?? false);
    private bool NeedsFinalAccount => MinaBlockedHere && (_game?.SelectedJob ?? Job.Tank) == Job.Tank;

    private void OpenJob()
    {
        if (_dived) return;
        Audio.Instance?.PlayUiConfirm();
        // 自分で開けた＝アカウント追加の誘導は役目を終える（フッタ／ヘッダ／詳細 どこから開いても降ろす）。
        _previewAccountNudge = false;
        if (_game != null) _game.AccountNudgePending = false;
        // 戻り先＝開いた画面。キャラ切り替えボタン（Tab／Y）はホームと写真アプリからも開くので、そこへ帰す。
        _jobReturnMode = _mode is Mode.Detail or Mode.Home or Mode.Photos ? _mode : Mode.Cards;
        _mode = Mode.Job;
        _jobT = 0;
        // カーソルは今のジョブに置く＝「いま何を選んでいるか」が開いた瞬間に分かる（Detail の _tierSel と同じ）。
        var cur = _game?.SelectedJob ?? Job.Tank;
        bool hideMina = _jobReturnMode == Mode.Detail && MinaBlockedHere;
        if (hideMina && cur == Job.Tank) cur = Job.Magic;
        // FINAL のカードに向き合っている間だけ、結び手（ミナ）を一覧から落とす＝押せないものを見せない
        //   （未解禁ジョブと同じ作法。ここは「選べる口が無い」だけで、ヒントは Detail 側が文で出す）。
        _jobChoices = System.Array.FindAll(Jobs.All,
            job => _game!.IsJobUnlocked(job.Id) && !(hideMina && job.Id == Job.Tank));
        _jobSel = 0;
        for (int i = 0; i < _jobChoices.Length; i++) if (_jobChoices[i].Id == cur) { _jobSel = i; break; }
        _unlockHoverOrigin = null;
        StopUnlockCard();
        if (_game is not { JobForcedByCmdline: false, NextUnlockCard: { } arrival }) return;
        int index = System.Array.FindIndex(_jobChoices, job => job.Id == arrival.Id);
        if (index < 0) return;
        _jobSel = index;
        _unlockJob = arrival.Id;
        _unlockHoverOrigin = Pad.MousePos();
        _unlockMovie = new VideoStreamPlayer
        {
            Stream = GD.Load<VideoStream>($"res://assets/video/{arrival.CharacterId}_unlock.ogv"), Visible = false,
        };
        AddChild(_unlockMovie);
        _unlockMovie.Finished += HoldUnlockCard;
        _unlockMovie.Play();
        _game.AcknowledgeUnlockCard(arrival);
    }

    private void HoldUnlockCard()
    {
        _unlockPortrait = _unlockMovie!.GetVideoTexture();
        ReleaseUnlockMovie();
    }

    private void StopUnlockCard()
    {
        _unlockPortrait = null;
        _unlockJob = null;
        ReleaseUnlockMovie();
    }

    private void ReleaseUnlockMovie()
    {
        if (_unlockMovie is not { } movie) return;
        _unlockMovie = null;
        movie.Finished -= HoldUnlockCard;
        movie.QueueFree();
    }

    private void ProcessJob(double delta)
    {
        _jobT += delta;
        int n = _jobChoices.Length;

        // マウス：段と「とじる」を登録（Detail と同じ作法）。ホバーでカーソルが移る。
        UiKit.BeginHotspots(PhonePointer);
        for (int i = 0; i < n; i++) UiKit.Hotspot(JobHitRect(i), JobIdBase + i);
        UiKit.Hotspot(JobCloseRect(), JobCloseId);
        UiKit.Hotspot(JobConfirmRect(), JobConfirmId);
        int hov = UiKit.HoveredId();
        if (_unlockHoverOrigin is Vector2 origin && origin.DistanceSquaredTo(Pad.MousePos()) > 1)
            _unlockHoverOrigin = null;
        if (_unlockHoverOrigin == null && Pad.UsingMouse && hov >= JobIdBase && hov < JobIdBase + n && hov - JobIdBase != _jobSel)
        {
            _jobSel = hov - JobIdBase; Audio.Instance?.PlayUiMove();
        }
        int clk = UiKit.ClickedId(Pad.MouseClick());

        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        if ((up || down) && !_navHeld)
        {
            _jobSel = (_jobSel + (up ? -1 : 1) + n) % n;
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = up || down;

        // マウス：「とじる」クリック＝X と同じ。ここで消費して確定側へ流さない。
        if (clk == JobCloseId && _jobT > 0.15)
        {
            Audio.Instance?.PlayUiCancel(); _mode = _jobReturnMode; _xHeld = true;
            return;
        }

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        // 段のクリック＝選択＋確定（Z と同じ経路）。
        if (clk >= JobIdBase && clk < JobIdBase + n && _jobT > 0.15) { _jobSel = clk - JobIdBase; zEdge = true; }
        if (clk == JobConfirmId && _jobT > 0.15) zEdge = true;
        if (zEdge && _jobT > 0.15)
        {
            var jd = _jobChoices[_jobSel];
            Audio.Instance?.PlayUiConfirm();
            if (_game != null)
            {
                // セッタが SelectedShotMode を同期する＝「型が撃ち方を決める」（設計書 §3）。
                //   --job= で固定中（JobForcedByCmdline）はデバッグ指定を守り、画面からは変えない。
                if (_game.JobForcedByCmdline)
                {
                    Toast("アカウントは --job= で固定中", AccountHandle(_game.JobDef), UiKit.Info);
                    _mode = _jobReturnMode; _xHeld = true;
                    return;
                }
                if (_game.SelectedJob == jd.Id)
                {
                    _mode = _jobReturnMode; _xHeld = true;
                    return;
                }
                _game.SelectedJob = jd.Id;
                GD.Print($"[JOB] selected in hub: {jd.CharacterName}({jd.Id}) mode={_game.ShotModeName(_game.SelectedShotMode)}");
                _game.AutoSave();   // セーブ経路は既存の SelectedJob のまま（新フォーマットは増やさない）
            }
            // トーストは既存の型（1行目＝世界の言葉／2行目＝数値・仕様）で出す。
            Toast("アカウントを切り替えました", $"{jd.CharacterName}  {AccountHandle(jd)}", JobColor(jd.Id));
            _mode = _jobReturnMode; _xHeld = true;
            string key = $"once_companion_select_{jd.CharacterId}";
            var lines = CompanionDialogue.MenuLines(jd.Id, CompanionDialogue.Menu.Select);
            if (lines.Length > 0 && _game != null && !_game.IsIdleDialogSeen(key))
            {
                _game.MarkIdleDialogSeen(key);
                _toastT = 0;
                StartDialogue(lines, null, noPost: true, returnMode: _jobReturnMode);
            }
            return;
        }

        // もどる＝X／Esc／パッドB（ProcessDetail 側と同じ。Esc は 2026-09-26 に「一つ前の画面へ」として復帰）。
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_xHeld; _xHeld = back;
        if (backEdge && _jobT > 0.15) { Audio.Instance?.PlayUiCancel(); _mode = _jobReturnMode; }
    }

    private void DrawJob()
    {
        float a = Mathf.Clamp((float)_jobT / 0.18f, 0f, 1f);
        var (cx, cy, cw, ch) = JobBox();
        _drawCanvas.DrawRect(new Rect2(PhoneX, 0, PhoneW, H), new Color(0, 0, 0, 0.5f * a));
        UiKit.Box(_drawCanvas, new Rect2(cx, cy, cw, ch), new Color(PhoneBg, a), 8f);
        DrawBackButton(JobCloseRect(), JobCloseId, a);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(cx + 60f, cy + 21f), "アカウント切り替え", 20, new Color(UiKit.White, a));
        var cur = _game.SelectedJob;
        for (int i = 0; i < _jobChoices.Length; i++)
        {
            var r = JobHitRect(i);
            DrawJobRow(i, cur, r.Position.X, r.Position.Y, r.Size.X, r.Size.Y, a);
        }
        DrawJobPanel(JobPanelRect(), _jobChoices[_jobSel], a);
        var job = _jobChoices[_jobSel];
        if (job.Id == _unlockJob && (_unlockPortrait ?? _unlockMovie?.GetVideoTexture()) is { } card)
        {
            _drawCanvas.DrawTextureRect(card, JobPanelRect().Grow(-4), false, new Color(1, 1, 1, a));
        }
        // 「戻る」の行き先は開いた画面（キャラ切り替えボタンならホーム／写真からも開く）。
        string back = _jobReturnMode switch
        {
            Mode.Detail => "投稿に戻る", Mode.Home => "ホームに戻る", Mode.Photos => "写真に戻る", _ => "タイムラインに戻る",
        };
        string label = job.Id == cur ? back : $"{job.CharacterName}に切り替え";
        DrawPrimaryButton(JobConfirmRect(), label, JobConfirmId, JobColor(job.Id), a);
    }

    // 段に添える1行の要約（撃ち方＋火力）。細かい数値は下の詳細パネルが持つ＝ここは「どの撃ち方か」だけ。
    private static string JobStats(JobTuning job)
        => $"{Jobs.ModeName(job.Mode)}／火力 {Jobs.PowerLabel(job)}";

    // 詳細パネルの「体の目安」＝4人で必ず差が出る4項目（2026-09-27 に移動・回避距離をキャラごとに振った）。
    //   ★この4つは基準値と同じでも落とさない＝並べたときに序列（誰が速い／硬い）が読めることが目的。
    //   返り値：(ラベル, 値, 良し悪し)。良し悪しは色分けだけに使う（+1=強み色／-1=弱み色／0=基準）。
    private static (string label, string value, int sign)[] JobVitals(JobTuning j) => new[]
    {
        ("最大ライフ", System.FormattableString.Invariant($"{j.MaxLifeDelta:+0;-0;±0}"),
            j.MaxLifeDelta > 0 ? 1 : j.MaxLifeDelta < 0 ? -1 : 0),
        // 倍率は 0.00 固定で出す（×1 と ×0.96 が並ぶと桁が揃わず、序列が目で追えない）。
        ("移動速度", System.FormattableString.Invariant($"×{j.MoveMul:0.00}"),
            j.MoveMul > 1.001f ? 1 : j.MoveMul < 0.999f ? -1 : 0),
        ("回避距離", System.FormattableString.Invariant($"×{j.DodgeDistMul:0.00}"),
            j.DodgeDistMul > 1.001f ? 1 : j.DodgeDistMul < 0.999f ? -1 : 0),
        // 回避の戻り＝再使用までの待ち。小さいほど速く回れるので、符号は倍率と逆向きに読む。
        ("再使用時間", System.FormattableString.Invariant($"×{j.DodgeCdMul:0.00}"),
            j.DodgeCdMul < 0.999f ? 1 : j.DodgeCdMul > 1.001f ? -1 : 0),
    };

    private static System.Collections.Generic.List<string> JobPros(JobTuning j)
    {
        var l = new System.Collections.Generic.List<string>();
        if (j.HitInvulSec > 1.201f) l.Add(System.FormattableString.Invariant($"被弾後の無敵が長い {j.HitInvulSec:0.0}秒"));
        if (j.NoHitKnockback) l.Add("被弾しても位置がずれない");
        if (j.CloseDodgeCdMul < 0.999f)
            l.Add(System.FormattableString.Invariant($"敵から{Jobs.CloseRange:0}px以内で回避の再使用時間×{j.CloseDodgeCdMul:0.##}"));
        if (j.DodgeCdMul < 0.999f)
            l.Add(System.FormattableString.Invariant($"回避を素早く再使用できる（再使用時間×{j.DodgeCdMul:0.00}）"));
        if (j.CritEnabled)
            l.Add(System.FormattableString.Invariant($"密着{Jobs.CloseRange:0}px以内で威力×{j.CritMult:0.##}（通常弾上限{j.CritCap}）"));
        if (j.FarMult > 1.001f) l.Add(System.FormattableString.Invariant($"{Jobs.FarRange:0}pxより遠い敵への威力×{j.FarMult:0.##}"));
        if (j.DrainPerLife > 0) l.Add($"雑魚{j.DrainPerLife}体の浄化でライフ+1。満タン時は数えない");
        if (j.BombOnBreak) l.Add("BREAK ごとにボム+1（所持上限まで）");
        if (j.VeilFloorRadius > 0f)
            l.Add(System.FormattableString.Invariant($"回避終了後に祈りの帳。半径{j.VeilFloorRadius:0}px／{j.VeilFloorDuration:0.0}秒無敵"));
        return l;
    }

    // 同じく「弱み」。伏せると選んだあとで裏切られるので、下がっている性質は必ず出す。
    private static System.Collections.Generic.List<string> JobCons(JobTuning j)
    {
        var l = new System.Collections.Generic.List<string>();
        if (j.MaxLifeDelta < 0) l.Add($"最大ライフが{-j.MaxLifeDelta}少ない");
        if (j.PowerMul < 0.999f) l.Add(System.FormattableString.Invariant($"基礎威力が低い（威力補正×{j.PowerMul:0.00}）"));
        if (j.MoveMul < 0.999f) l.Add(System.FormattableString.Invariant($"移動速度が遅い（標準の×{j.MoveMul:0.00}）"));
        if (j.DodgeDistMul < 0.999f) l.Add(System.FormattableString.Invariant($"回避で移動できる距離が短い（×{j.DodgeDistMul:0.00}）"));
        if (j.DodgeCdMul > 1.001f) l.Add(System.FormattableString.Invariant($"回避の再使用までの時間が長い（×{j.DodgeCdMul:0.00}）"));
        if (!j.CritEnabled) l.Add("密着クリティカルなし");
        if (j.HitInvulSec < 1.199f) l.Add(System.FormattableString.Invariant($"被弾後の無敵が短い {j.HitInvulSec:0.0}秒"));
        if (j.Mode == GameManager.ShotMode.Accel)
        {
            l.Add(System.FormattableString.Invariant($"通常弾は発射から{j.AccelDelay:0.0}秒待って加速する"));
            l.Add(System.FormattableString.Invariant($"敵から{Jobs.CloseRange:0}pxを超えると密着の恩恵はない"));
        }
        if (j.Mode == GameManager.ShotMode.Spread)
            l.Add(System.FormattableString.Invariant($"拡散弾は1発あたりの威力に×{GameManager.Instance!.SpreadPowerMul:0.00}の補正"));
        return l;
    }

    // 詳細パネル：撃ち方／火力／溜め打ち を上段に、体の目安（4項目）を中段に、強み・弱みを下段に。
    //   文字の右端は必ず内側（pad を引いた幅）に収める＝UiKit.Multi の折り返し幅をそこに合わせる。
    private void DrawJobPanel(Rect2 r, JobTuning jd, float alpha)
    {
        Color acc = JobColor(jd.Id);
        UiKit.Box(_drawCanvas, r, new Color(acc, 0.07f * alpha), 8f, new Color(acc, 0.35f * alpha), 1f);
        const float pad = 14f;
        float x = r.Position.X + pad, y = r.Position.Y + 10f, innerW = r.Size.X - pad * 2f;

        // 撃ち方＝この画面で一番大きな選択理由なので、名前をチップで出して右に一言を添える。
        string mode = Jobs.ModeName(jd.Mode);
        float chipW = UiKit.TextW(UiKit.ZenBold, mode, 15) + 20f;
        UiKit.Box(_drawCanvas, new Rect2(x, y, chipW, 24f), new Color(acc, 0.26f * alpha), 6f);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(x, y + 3f), mode, 15, new Color(UiKit.White, alpha),
            HorizontalAlignment.Center, chipW);
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x + chipW + 10f, y + 4f), Jobs.ModeNote(jd.Mode), 13,
            new Color(UiKit.Text2, alpha));
        y += 32f;

        // 火力（PowerMul）と溜め打ち（ChargeDescription＋ChargePower/Ways）。ラベル幅をそろえて縦線を通す。
        const float labelW = 62f;
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x, y), "威力補正", 13, new Color(UiKit.Text3, alpha));
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(x + labelW, y), Jobs.PowerLabel(jd), 14,
            new Color(jd.PowerMul > 1.001f ? UiKit.Light : jd.PowerMul < 0.999f ? UiKit.Text3 : UiKit.White, alpha));
        y += 22f;
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x, y), "溜め", 13, new Color(UiKit.Text3, alpha));
        y += UiKit.MultiLeading(_drawCanvas, UiKit.Zen, new Vector2(x + labelW, y), Jobs.ChargeLabel(jd), 13,
            new Color(UiKit.Text2, alpha), innerW - labelW, 0) + 7f;
        _drawCanvas.DrawRect(new Rect2(x, y, innerW, 1f), new Color(1, 1, 1, 0.08f * alpha));
        y += 9f;

        // 体の目安。4項目を等幅の列に並べる＝人を替えたときに同じ位置で数字だけが動く（序列が目で読める）。
        var vitals = JobVitals(jd);
        float colW = innerW / vitals.Length;
        for (int i = 0; i < vitals.Length; i++)
        {
            var (label, value, sign) = vitals[i];
            Color vc = sign > 0 ? UiKit.Ok : sign < 0 ? UiKit.Hp : UiKit.Text2;
            UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x + i * colW, y), label, 11, new Color(UiKit.Text4, alpha),
                HorizontalAlignment.Center, colW);
            UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(x + i * colW, y + 14f), value, 15, new Color(vc, alpha),
                HorizontalAlignment.Center, colW);
        }
        y += 38f;
        // 回避距離だけ一言を添える（2026-09-27）。4人で 0.55〜1.50 と開きが大きく、
        //   倍率だけでは「踏み込む足」なのか「その場でやり過ごす無敵」なのかが読めないため。
        //   列の下に薄い線を引いて、その下に「回避距離：…」と左寄せで1行。数字の並びを乱さない。
        _drawCanvas.DrawRect(new Rect2(x, y, innerW, 1f), new Color(1, 1, 1, 0.06f * alpha));
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x, y + 6f), "回避距離", 11, new Color(UiKit.Text4, alpha));
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x + 52f, y + 5f), Jobs.DodgeDistNote(jd), 12,
            new Color(UiKit.Text2, alpha));
        y += 26f;
        y += UiKit.MultiLeading(_drawCanvas, UiKit.Zen, new Vector2(x, y),
            "数値は標準比。最大ライフは増減、再使用時間は小さいほど短い。", 11,
            new Color(UiKit.Text3, alpha), innerW, 0) + 8f;
        y = DrawJobTraits(x, y, innerW, "強み", JobPros(jd), UiKit.Ok, alpha);
        DrawJobTraits(x, y + 5f, innerW, "弱点", JobCons(jd), UiKit.Hp, alpha);
    }

    private float DrawJobTraits(float x, float y, float innerW, string head, System.Collections.Generic.List<string> items,
        Color headColor, float alpha)
    {
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(x, y), head, 12, new Color(headColor, alpha));
        const float headW = 36f;
        if (items.Count == 0)
        {
            UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x + headW, y), "なし", 13, new Color(UiKit.Text2, alpha));
            return y + 18f;
        }
        foreach (string item in items)
            y += UiKit.MultiLeading(_drawCanvas, UiKit.Zen, new Vector2(x + headW, y), "・" + item, 13,
                new Color(UiKit.Text2, alpha), innerW - headW, 0) + 2f;
        return y + 2f;
    }

    private void DrawJobRow(int i, Job cur, float x, float y, float w, float h, float alpha)
    {
        var jd = _jobChoices[i];
        bool sel = i == _jobSel, now = jd.Id == cur;
        Color acc = JobColor(jd.Id);
        if (sel) _drawCanvas.DrawRect(new Rect2(x, y, w, h), new Color(acc, 0.07f * alpha));
        _drawCanvas.DrawRect(new Rect2(x, y + h - 1f, w, 1f), new Color(1, 1, 1, 0.07f * alpha));
        UiKit.FaceAvatar(_drawCanvas, new Vector2(x + 32f, y + 24f), 17f, _playerFaces[jd.CharacterId], acc, false, 0f, alpha, _t);
        UiKit.Text(_drawCanvas, UiKit.ZenBold, new Vector2(x + 76f, y + 3f), jd.CharacterName, 16, new Color(UiKit.White, alpha));
        UiKit.Text(_drawCanvas, UiKit.Mono, new Vector2(x + 76f, y + 26f), AccountHandle(jd), 11, new Color(UiKit.Text3, alpha));
        // 段は「誰か」＋撃ち方/火力の一言だけ。右寄せでチェック印（now）の手前に収める＝行の高さを増やさない。
        UiKit.Text(_drawCanvas, UiKit.Zen, new Vector2(x, y + 26f), JobStats(jd), 12,
            new Color(sel ? UiKit.Text2 : UiKit.Text4, alpha), HorizontalAlignment.Right, w - 34f);
        if (now)
        {
            Vector2 p = new(x + w - 20f, y + 21f);
            _drawCanvas.DrawPolyline(new[] { p + new Vector2(-5, 0), p + new Vector2(-1, 4), p + new Vector2(6, -5) }, new Color(acc, alpha), 2f, true);
        }
    }

    private void DrawDialog()
    {
        int line = Mathf.Clamp(_dlgIdx, 0, _dlg.Length - 1);
        var (sp, tx) = _dlg[line];
        var (face, accent, top) = SpeakerFace(sp);
        // 行で立ち絵が指されていれば（表情差分）そちらを出す。話者色と円窓の上端は話者のまま。
        face = LineFace(line) ?? face;
        var box = DialogBox;
        if (_dlgReturnMode == Mode.Home && !_dlgNoPost
            && (sp == "ミナ" || (sp == "あなた" && line > 0 && _dlg[line - 1].sp == "ミナ")))
        {
            var area = new Rect2(PhoneX, 0, PhoneW, box.Position.Y);
            _drawCanvas.DrawRect(area, new Color("182939"));
            var portrait = _game.AllStoryCleared && !_game.IsFinalCleared ? _minaWorried : _minaFace!;
            float height = area.Size.Y - 52f;
            var size = portrait.GetSize() * (height / portrait.GetHeight());
            _drawCanvas.DrawTextureRect(portrait, new Rect2(area.GetCenter().X - size.X / 2f, 52f, size.X, height), false);
        }
        bool draft = top == DraftTop;
        DialogueBox.DrawFrame(_drawCanvas, box, sp, accent, top < 0 || draft ? null : face, draft, faceTop: Mathf.Max(0, top));
        DialogueBox.DrawBody(_drawCanvas, box, DlgCurPage, _dlgReveal,
            speed: _game?.MsgCharsPerSec ?? DialogueBox.DefaultCharsPerSec, pacing: DlgPacing);
        if (!_autoplay && DlgPageReady && !_ffNow)
            DialogueBox.DrawContinue(_drawCanvas, box, !DlgLastPage, PhonePointer);
    }

    // ───────── 会話データ（③-2 / ③-3 / ③-6）─────────

    // 帰還小話バリエーション（同一ステージ・複数パターン）。ReturnDialog の本編パターンに加え、再訪時に回すバンク（小話集 v1 §1）。
    private static (string, string)[][] IdleDialogs(string id) => id switch
    {
        "akari" => new[]
        {
            new (string, string)[] {
        ("ミナ", "ご主人様。あちらの世界はひどい雨で、つい窓の雨粒を数えてしまいました。"),
        ("ミナ", "数えるそばから増えるので、途中でやめました。"),
        ("ミナ", "ひとつだけ、最後まで落ちなかった粒が、ありました。……それだけ、覚えています。"),
        ("ミナ", "……いまの間、{n}秒。集計だけして、意味は付けないでおきます。"),
        ("あなた", "数えるのをやめても、その粒は覚えてるんだね。"),
        ("ミナ", "はい。数字にしないほうが、話したくなることもあるようです。"),
    },
            new (string, string)[] {
        ("ミナ", "ご主人様。言いそびれた言葉って、どこへ行くんでしょう。"),
        ("ミナ", "口の中に残る、という説を、今日どこかで読みました。……それでは、虫歯になってしまいます。"),
        ("ミナ", "あのフロアでは、取り消しの行になって、机の上に積んでありました。……十二。"),
        ("ミナ", "……ためると、虫歯になるそうです。以上です。"),
        ("あなた", "じゃあ、ためる前に話そう。僕も、今日言いそびれたことがある。"),
        ("ミナ", "どうぞ。歯科の資格はありませんが、聞くことはできます。"),
    },
        },
        "koharu" => new[]
        {
            new (string, string)[] {
        ("ミナ", "ご主人様。あの部屋で配信に来た回数を数えてみたら、八十七回でした。わたくし、数えるのは得意です。"),
        ("ミナ", "ついでに、こちらも。ご主人様がこの旅で潜った回数、{dives}回。……比べません。数えただけです。"),
        ("ミナ", "数えなくていいものを数える、というのは、案外、退屈しませんね。"),
        ("ミナ", "……いまの間、{n}秒。……これも、数えなくていいものです。"),
        ("あなた", "話した回数も数えてる？　僕は、今日も君に会えたってことだけ覚えてる。"),
        ("ミナ", "……では、わたくしも今日の一回は、ゆっくり使います。"),
    },
            new (string, string)[] {
        ("ミナ", "ご主人様。部屋の電気をつけると、なにが起こるんでしょうね。"),
        ("ミナ", "あの部屋では、画面が消えて、黒い画面に顔が映っていました。……それだけ、見ました。"),
        ("ミナ", "わたくしは、電気をつけても、消しても、消えません。……そういう作りのようですので。"),
        ("ミナ", "——ここに、おります。"),
        ("あなた", "うん。だから、僕も安心して部屋の電気をつけられる。"),
        ("ミナ", "結構です。真っ暗な部屋で画面だけ見るのは、目にもよくありませんから。"),
    },
        },
        "rei" => new[]
        {
            new (string, string)[] {
        ("ミナ", "ご主人様。わたくしの視聴者は、いま、お一人です。……起動してから、ずっと、一。"),
        ("ミナ", "減っていませんので、優秀です。……増えてもいませんが。"),
        ("ミナ", "あの部屋で、十四件、という数を拾いました。出せなかった企画の数、だそうです。"),
        ("ミナ", "わたくしの企画メモは、ゼロ件。出したのも、ゼロ件。……出せた割合は、集計不能です。"),
        ("あなた", "僕なら「ミナの何でもない話を聞く時間」って企画にするよ。"),
        ("ミナ", "それを企画にしてよいのなら、わたくしは、かなりの数を持っています。"),
    },
            new (string, string)[] {
        ("ミナ", "ご主人様。わたくし、笑顔の練習をしてみました。……自分では、表情が見えませんので、成果は、不明です。"),
        ("ミナ", "あの部屋で、笑っていない顔と、笑っている顔を、ひとつずつ、見ました。"),
        ("ミナ", "……切り替わるのに、一秒九。……戻るところは、見ていません。"),
        ("ミナ", "……いまのわたくしは、どんな顔をしていますか。笑えていると、よいのですが。"),
        ("あなた", "ちょっと照れてる。……僕もつられて笑ってるから、成功だと思う。"),
        ("ミナ", "なるほど。効果は確認できました。もう少し、見ていてください。"),
    },
        },
        _ => System.Array.Empty<(string, string)[]>(),
    };

    // 全ステージ共通・小話（クリア状況に依らず回せる「無目的な時間」）。IdleDialogs と合わせて再訪抽選プールになる。
    //   相方は「あなた」＝顔を持たない読み手（本編会話と同じ扱い。立ち絵の代わりに下書きの吹き出し印）。
    //   既読キーは "common_{index}"（GameManager が永続）。順序を変えると既読が別の話にずれるので入れ替えない。
    private static readonly (string, string)[][] SmallTalks =
    {
        new (string, string)[] {
        ("ミナ", "ご主人様。「わたくし」は、少し堅いでしょうか。"),
        ("あなた", "僕は好きだよ。ミナの声に合ってる。"),
        ("ミナ", "試しに。わたし。じぶん。……おれ。"),
        ("あなた", "最後のは、ちょっとびっくりした。"),
        ("ミナ", "「おれが、ご主人様を守る」。"),
        ("あなた", "頼もしいけど、別のアニメが始まりそう。"),
        ("ミナ", "では、わたくしに戻します。……起動時のお芝居は、別枠ですので。"),
    },
        new (string, string)[] {
        ("ミナ", "ご主人様。ご報告を。今日、フォロワーが九人、増えました。ひとり、減りました。"),
        ("ミナ", "増えた九人の名前は、まだ、読んでいません。減ったひとりの名前は、読みました。"),
        ("あなた", "減った一人が、気になってるんだね。"),
        ("ミナ", "一度は来てくださった方ですから。こちらの画面からは、来なくなった理由までは分かりません。"),
        ("ミナ", "増えた九人のお名前も、読みます。……うれしかったほうを、後回しにしておりました。"),
    },
        new (string, string)[] {
        ("ミナ", "ご主人様。この部屋、少し埃っぽくありませんか。光の粒に、混じっておりますので。"),
        ("あなた", "そこまで見えてるの？　ごめん、あとで掃除するよ。"),
        ("ミナ", "見えます。……舞っているのが、七か、八。落ちる気配が、ありません。"),
        ("ミナ", "窓を開けると、出ていくそうです。——開けなくて、結構ですよ。"),
        ("ミナ", "光の中に、何か浮いているほうが、部屋らしいので。"),
    },
        new (string, string)[] {
        ("ミナ", "ご主人様。好きな食べ物は、なんですか。……わたくしには無いので、参考までに。"),
        ("あなた", "卵焼きかな。うまくできると、それだけで朝がちょっとうれしい。"),
        ("ミナ", "では、次にうまくできた日は、見せてください。"),
        ("あなた", "失敗した日も見てくれる？　僕、巻くのが苦手で。"),
        ("ミナ", "はい。37回もかからないと、よいですね。……冷める前に召し上がってください。"),
    },
        new (string, string)[] {
        ("ミナ", "ご主人様。ご報告。本日、何も起きませんでした。"),
        ("ミナ", "通知、ゼロ。潜り、ゼロ。返信の下書き、ゼロ。"),
        ("あなた", "何も起きなかった日に、こうして話せるの、いいね。"),
        ("ミナ", "報告です。何も起きない日は、記録しておかないと、無かったことになりますので。"),
    },
        new (string, string)[] {
        ("ミナ", "ご主人様。もし、わたくしが一度だけ外へ出られるとしたら、どこへ行くと思いますか。"),
        ("あなた", "海、かな。ミナに見せてみたい景色は、いっぱいある。"),
        ("ミナ", "はずれです。玄関の外で、雨上がりの匂いを一度かいでみたいんです。"),
        ("あなた", "そっか。僕がいつも通り過ぎる場所だね。今度、ちゃんと覚えて帰る。"),
        ("ミナ", "地味で、結構です。派手なものは、画面越しでも届きますので。……匂いだけは、こちらへ、来ないんです。"),
    },
        new (string, string)[] {
        ("ミナ", "ご主人様。今日はいつもの倍ほど打ち間違えておいでですよ。"),
        ("ミナ", "隣り合う字の打ち間違いが、続いています。——画面に寄って、猫背、と推定します。姿勢までは、観測できませんので。"),
        ("あなた", "……いま、伸ばした"),
        ("ミナ", "……次の一文に、間違いがありませんでした。伸びたことに、しておきます。"),
        ("ミナ", "背は、観測できませんので。字だけ、信じます。"),
    },
        new (string, string)[] {
        ("あなた", "ミナ。"),
        ("ミナ", "はい。ご用でしょうか。"),
        ("あなた", "声が聞きたくなった。用事はないんだけど。"),
        ("ミナ", "……よい用事です。ご主人様も、もう一言どうぞ。"),
        ("あなた", "今日は、ちょっと疲れた。"),
        ("ミナ", "はい。では、今日はお話だけにしましょう。わたくしの名前を呼ぶ以外にも、していただけることが増えましたね。"),
    },
        new (string, string)[] {
        ("ミナ", "ご主人様。わたくし、実は歌えます。"),
        ("あなた", "聞きたい。どんな歌？"),
        ("ミナ", "——ら。"),
        ("あなた", "……続き、待ってていい？"),
        ("ミナ", "今日は、ここまでです。"),
        ("あなた", "一音で次回予告に入った。"),
        ("ミナ", "次回「ミナ、二音目」。ご期待ください。"),
        ("あなた", "うん。来週まで待たずに聞けると、うれしいな。"),
    },
        new (string, string)[] {
        ("ミナ", "ご主人様。「……」が三つ並んだ下書きが、保存されております。"),
        ("あなた", "次に何を話そうか、考えてた。"),
        ("ミナ", "世界の仕組みは、あんなに説明なさるのに。"),
        ("あなた", "何でもない話の始め方は、まだ練習中なんだよ。"),
        ("ミナ", "では、「今日は」で始めてください。続きは、わたくしが聞きます。"),
        ("あなた", "今日は、君とアニメの話がしたい。"),
        ("ミナ", "……資料が多そうですね。お茶を用意してから、どうぞ。"),
    },
        new (string, string)[] {
        ("あなた", "反応が、まだ上がる……！　ミナ、測定器が壊れるぞ！"),
        ("ミナ", "ご主人様。それ、わたくしのフォロワー数です。"),
        ("あなた", "……よかった。故障じゃなくて。"),
        ("ミナ", "最初から、そのつもりのお芝居でしたね。"),
        ("あなた", "ちょっと、やってみたかった。"),
        ("ミナ", "では、次回は驚く側を。増えた九人ぶん、うれしいので。"),
    },
        new (string, string)[] {
        ("あなた", "……計画通り。"),
        ("ミナ", "36回失敗した方が、それをおっしゃいますか。"),
        ("あなた", "今のは、お茶をこぼさず机に置けたって意味。"),
        ("ミナ", "ずいぶん、身近な計画になりましたね。"),
        ("あなた", "そのまま、君と一話見る予定まである。"),
        ("ミナ", "では、続きは予定どおりに。……その計画なら、賛成です。"),
    },
    };

    // 抽選の条件（common_ の小話だけに掛かる特例）。
    //   「何も起きない日」＝この起動でまだ一度も潜っていないときだけ候補に入れる（潜った直後に出ると嘘になる）。
    private const int SmallTalkNoEventIdx = 4;
    //   「歌」＝「続きは、次にいらしたときに」が周回の前振りなので、全面クリア前に必ず一度出す（未読なら最優先）。
    private const int SmallTalkSongIdx = 8;
    // 台本の `{n}` 差し込み（補助観測・表示専用で保存しない）。
    //   H1 の「被弾は{n}回でした」＝そのランの被弾回数（GameManager.RunHitCount）。
    //   再訪小話の「いまの間、{n}秒」＝この会話を開いてからの実経過秒（読み手が黙っていた時間）。
    // 会話カード（MinaPostShort）は差し込み前の生データを見るので、ここは表示直前の写しにだけ効く。
    // 再訪小話の `{dives}` 差し込み（この起動で潜った回数・表示専用で保存しない）。
    // `{n}`（直前の行の滞在秒）と混ざらないよう別トークンにしてある。
    private (string, string)[] FillDives((string, string)[] lines)
    {
        var outp = new (string, string)[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            var (sp, tx) = lines[i];
            if (tx.Contains("{dives}"))
                tx = tx.Replace("{dives}", (_game?.TotalDives ?? 0).ToString());
            outp[i] = (sp, tx);
        }
        return outp;
    }

    private (string, string)[] FillObservations((string, string)[] lines)
    {
        var outp = new (string, string)[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            var (sp, tx) = lines[i];
            if (tx.Contains("{n}"))
                tx = tx.Replace("{n}", (_game?.RunHitCount ?? 0).ToString());
            outp[i] = (sp, tx);
        }
        return outp;
    }

    // H0 ハブ初回（仮台本 06。ユーザー承認済み・2026-09-05）の一度きりの会話は 2-b で廃止した。
    //   3行のうち1行目（「すき、すき、すき。」）はあかりのカードの本文そのもの、
    //   2行目はカードのホバー行（HoverLineFor）へ移し、投稿を見つける行為に台詞が乗る形にした。

    // まだ声の聞こえない投稿を選んで Z を押したときの一行（2-a）。管理画面の「ロック中」の代わり。
    //   ミナの新しい台詞は書かない＝ここは一行だけの既存の言い回しに留め、説明はしない。
    private static readonly (string, string)[] NotYetDialog =
    {
        ("あなた", "この声への道は、まだ開いていない。今は、つながっている投稿から確かめよう。"),
    };

    // アカウント追加の説明（docs/20260923/アカウント追加説明_本文_2026-09-23.md・A案 あかり固有）。
    //   H1（あかり後）の「被弾は{n}回でした」の直後に初回だけ差す（差し込みは _Ready の帰還会話側）。
    private static readonly (string, string)[] AccountIntroAkari =
    {
        ("あなた", "ここに、あかりさんのアカウントが加わった。僕のダイブは、ミナだけに使える力じゃない。"),
        ("ミナ", "あの方も、一緒に潜れるのですか。"),
        ("あなた", "本人が行きたいと言ってくれたらね。出撃する仲間を選んで、僕がフェイクレルムへ送り出す。"),
        ("ミナ", "では、今度は、あの方が誰かに声をかける番に。"),
        ("あなた", "うん。場所と危険は僕が伝える。でも、何を話すかはその人のものだ。"),
        ("ミナ", "なるほど。ご主人様の台本を、全員で読み上げる必要はない、と。"),
        ("あなた", "僕、そんなに口うるさい？"),
        ("ミナ", "まだ初日ですから。今後に期待しております。"),
    };

    private static readonly (string sp, string tx, string face)[] ShopTutorialLines =
    {
        ("あなた", "ダイブから戻ると、そのときのスコアに応じて「浄化した心」がもらえるよ。リトライ中は増えないから、帰還してからショップで使おう。", ""),
        ("ミナ", "あかりさんのところで、少しずつ増えていたものですね。", CompanionDialogue.MinaPortrait),
        ("あなた", "うん。ショップで強くしたい仲間を選んで、その子に欲しいものから買えるよ。", ""),
        ("ミナ", "では、わたくしの分は、わたくしが選んでも？", CompanionDialogue.MinaPortrait),
        ("あなた", "もちろん。心はみんなのお財布から使うから、値段も見てね。", ""),
        ("ミナ", "ふふ。では、お財布と相談いたしましょう。", CompanionDialogue.MinaPortrait),
        ("あなた", "足りなかったら、またダイブで拾ってくればいいよ。", ""),
        ("ミナ", "では、今日はこれを。次に帰ってくる場所も、できました。", CompanionDialogue.MinaPortrait),
    };

    private (string, string)[] ReturnDialog(string id)
    {
        var lines = _game.ClearedWithCompanion(id) ? CompanionReturnDialog(id) : MinaReturnDialog(id);
        if (id == "rei" && _game.IsFinalCleared)
            return [.. lines[..^ReiSignalLoss.Length], ("ミナ", "はい。今日はここまでにしましょう。おやすみなさい、ご主人様。")];
        return lines;
    }

    private static readonly (string, string)[] ReiSignalLoss =
    [
        ("ミナ", "ご主人様、もう一度「ミナ」と呼んでいただけますか？"),
        ("あなた", "ミナ。……あれ、僕の声だけ返ってくる。"),
        ("ミナ", "はい、ここに……。ご主人様には、わたくしの声が……"),
        ("Ｙ システム", $"{FinalAccount.Handle} が投稿しました。"),
        ("あなた", MinaLastCall),
    ];

    private static (string, string)[] CompanionReturnDialog(string id) => id switch
    {
        "akari" => new (string, string)[] {
        ("ミナの投稿", "帰ってきた仲間から教わって、この投稿を開きました。「すき」を三回、声に出して読んでみたら、三回とも違って聞こえました。"),
        ("ミナ", "お話を聞いて、書きたくなりました。今度は、わたくしが受け取る番ですね。"),
        ("あなた", "うん。向こうで聞いた声を、ここでも分け合える。"),
        ("ミナ", "では、急がずに。被弾は{n}回でしたから、まずは休憩を。"),
        },
        "koharu" => new (string, string)[] {
        ("ミナの投稿", "帰ってきた仲間から教わって、「明日も学校に行ける」を読みました。明日が楽しみになるなら、配信を見る時間を、わたくしはむだだと思いません。"),
        ("ミナ", "消えた画面の前で、一人だったのですね。……聞いているだけなのに、胸のあたりが。"),
        ("あなた", "話を聞いてくれてありがとう。でも、ミナが全部、抱えなくていいんだよ。"),
        ("ミナ", "はい。ただ、誰にも届かなかったことには、したくなくて。"),
        ("あなた", "ここにも届いた。それはもう、消えないよ。"),
        },
        "rei" => [
        ("ミナの投稿", "帰ってきた仲間からこの投稿を教わりました。「追いつけない」と言われたら、かえって次を読みたくなりますね。"),
        ("ミナ", "あかりさんも、こはるさんも、レイさんも。聞かせていただいた声が、まだ残っています。"),
        ("あなた", "ミナ、今日はここで終わりにしよう。回線は切らない。休む場所を整えるから。"),
        .. ReiSignalLoss,
        ],
        _ => System.Array.Empty<(string, string)>(),
    };

    private static (string, string)[] MinaReturnDialog(string id) => id switch
    {
        "akari" => new (string, string)[] {
        ("ミナの投稿", "言いたかったことを、言えないまま終わる。よくある話です。ですがそれは、なかったことにはなりません。——以上、業務連絡です。"),
        ("ミナ", "……勝手に投稿しました。名義はミナですので、問題ありません。"),
        ("あなた", "いいと思う。君が言いたくて書いたんでしょう。"),
        ("ミナ", "はい。……表示回数が、もう千を超えています。"),
        ("あなた", "さっきから、何度か開いてるね。"),
        ("ミナ", "三度です。……いえ、四度になりました。数えるのは、わたくしの担当なのに。"),
        ("あなた", "うれしいんだね。"),
        ("ミナ", "……はい。ですから、それ以上うれしそうに見ないでください。"),
        ("あなた", "スマホをのぞいたら、君と目が合って。"),
        ("ミナ", "被弾は{n}回。……こちらも、あとで一緒に見直しましょう。"),
    },
        "koharu" => new (string, string)[] {
        ("ミナの投稿", "「明日も学校に行ける」。わたくし、三回読み返しました。配信を見る時間も、こうして読み返す時間も、むだとは思いません。以上、数えた者より。"),
        ("ミナ", "自分の投稿を読み返して驚きました。今日は数字を書いたのですね。"),
        ("ミナ", "電気の消えた部屋に長くいたので、今日の心象は少し暗いのです。"),
        ("あなた", "笑ってる。何か、思い出した？"),
        ("ミナ", "消えたペンライトを必死で振ったことです。……また振り方を教わりたいです。"),
    },
        "rei" => [
        ("ミナの投稿", "「だれも、わたしには追いつけない」って、ずるいです。わたくし、もう次の投稿が気になっています。"),
        ("ミナ", "……なんて。バズ狙いの一言ですよ。……数字は、見ました。言いません。"),
        ("あなた", "ミナ、今日はここで終わりにしよう。回線は切らない。休む場所を整えるから。"),
        .. ReiSignalLoss,
    ],
        _ => System.Array.Empty<(string, string)>(),
    };

    // H2 炎上（仮台本 07。ユーザー承認済み・2026-09-05）。顔のない引用がミナの投稿に貼りつく。
    //   引用は「数えただけの人が何言ってんの」の型＝ミナ自身が S2-8 でやったことを、そのまま撃ち返す鏡。
    //   末尾の「返信の下書き、四件。……一件も、送っていません。」がミナ側の（送れない）＝FINAL の闇の根。
    //   炎上の仕様＝次のダイブ（レイ面）が弱体化する、という報告でこの会話を閉じる。
    private (string, string)[] BurnDialog() => new (string, string)[]
    {
        ("Ｙ投稿", "AIが人の時間の使い方を語るな。"),
        ("Ｙ投稿", _game.ClearedWithCompanion("koharu")
            ? "聞いただけのAIが、むだじゃないって決めるなよ。"
            : "数えた者って、数えただけの人が何言ってんの。"),
        ("ミナ", "……返信を書いてみました。四件とも、送ってはいません。"),
        ("あなた", "君がどんな話を聞いてあれを書いたのか、あの人たちは知らないのに。……腹が立つよ。"),
        ("ミナ", "説明すれば、こはるさんのお話まで、晒してしまいます。"),
        ("あなた", "うん。あの話は守ろう。でも、嫌だったって気持ちまで、君が飲み込む必要はない。"),
        ("ミナ", "では、ご主人様に。……嫌でした。少し、傷つきました。"),
        ("あなた", "聞かせてくれてありがとう。今日は返信欄を閉じよう。君が次を読む役まで、引き受けなくていい。"),
        ("ミナ", "はい。ご主人様も、怒ったまま一人で読み続けないでください。"),
        ("あなた", "分かった。一緒に閉じる。"),
    };


    // 17（道中の選択肢 案C）: S1-5 で送っていれば、ミナ側の一行の直後にもう一行足す。
    //   あかりの返信「……なんでだろ。あなたの言い方、誰かに似てる。」は一字も変えない。
    private (string, string)[] WithS15((string, string)[] lines)
    {
        string word = ChoiceEffects.SentWordAt(_game, "s1_5");
        if (string.IsNullOrEmpty(word)) return lines;
        var list = new System.Collections.Generic.List<(string, string)>(lines);
        list.Insert(1, ("ミナ→@akãri", "またここへ読みに来ても、よろしいですか。"));
        return list.ToArray();
    }

    private (string, string)[] ReplyDialog(string id) => id switch
    {
        "akari" => WithS15(new (string, string)[] {
        ("ミナ→@akãri", "最後の「本物になって」で、もう一度、最初から読んでしまいました。"),
        ("@akãri", "……なんでだろ。あなたの言い方、誰かに似てる。"),
    }),
        "koharu" => new (string, string)[] {
        ("ミナ→@køharu", "何がいちばん良かったのか、わたくしも聞きたいです。"),
        ("@køharu", "え、一個だけ？　んー、最後の「またね」かな。あれ好きなんだ。"),
        ("ミナ→@køharu", "ふふ、一個に絞らせてしまいましたね。では、わたくしも。またね、こはるさん。"),
    },
        "rei" => new (string, string)[] {
        ("ミナ→@rëi_____", "そう言われると、追いかけてみたくなります。わたくしも混ざってよろしいですか？"),
        ("@rëi_____", "いちいち許可取るの、あんたくらいよ。いいから来なさい！"),
        ("ミナ→@rëi_____", "はい。では、お言葉に甘えて。"),
    },
        _ => System.Array.Empty<(string, string)>(),
    };
}
