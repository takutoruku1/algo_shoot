using Godot;

// Hub : タイムラインハブ（ステージ間の中枢）。RefrainHTML のデザイン言語で非ピクセル化。
//   - ヘッダ：使用中のアカウント（アバター/名前/フォロワー/インプレ/汚染）。
//   - 角丸ガラスの投稿カード（声/届いた/限界 のピル）を ↑↓ で選び Z で潜る（通常は難易度選択を挟む）。
//     カードは下から滑り込む流入アニメ（帰還投稿後は「タイムライン更新」として再流入）。
//     エンゲージ数はクリア状態・フォロワー数と連動し、クリア済カードにはミナの自動投稿がスレッド返信風にぶら下がる。
//   - クリア帰還でミナの会話＋自動投稿。クリア済カードで C：コメント返信（1回）。
//   - 全クリアで FINAL カード（枠とピルは穢れ色・フォロワー/インプレの生値を語ラベル付きで出す）。autoplay は会話を自動送り→自動ダイブ。
public partial class Hub : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;
    private const float PhoneW = 480f, PhoneX = (W - PhoneW) / 2f;
    private static readonly Color PhoneBg = new("14171d");
    private static readonly Color PhoneRaised = new("1d2229");

    private struct Entry
    {
        public bool IsFinal;
        public string Id, Scene, Name, Handle, Tweet, Initial;
        public bool Unlocked, Cleared;
        public long Likes, Reposts, Replies;
        // 2-b: タイムラインの種別。Voice＝潜れる（三人＋FINAL）／Filler＝埋め草の他人の投稿／Pinned＝ミナの最新投稿。
        //   Filler と Pinned は潜れない＝カーソルは乗るが Z では開かない。
        public Kind Sort;
        public string RelT;       // 相対時刻（Filler は生成規則で散らす。三人は従来の RelTime）
        public bool Redacted;     // 層2（病みサイン）＝伏字が明滅する埋め草。声のあるカードと同じ印を持つ
        public int Icon;          // 埋め草のアイコン番号（1..SnsVoices.IconCount。0＝アイコンを持たない＝三人とミナ）
    }
    private enum Kind { Voice, Filler, Pinned }
    private Entry[] _entries = System.Array.Empty<Entry>();

    // カードが「潜れる声」か（＝Z で投稿詳細が開き、そこから難易度を選んで潜れる）。
    private bool IsVoice(int i) => i >= 0 && i < _entries.Length && _entries[i].Sort == Kind.Voice
        && _entries[i].Unlocked;

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
    //   フッタの脈打ち（存在に気づかせる合図）は「まだ一度も潜っていない」あいだだけ。一度でも潜れば静かになる。
    private bool JobHintGlow => (_game?.TotalDives ?? 0) == 0 && (_game?.HeartsSaved ?? 0) == 0;

    // このステージのクリアで新しく開いたジョブ（無ければ null）。帰還トーストの見出しに使う。
    private static JobTuning? JobUnlockedBy(string stageId)
    {
        foreach (var j in Jobs.All)
            if (j.UnlockStageId == stageId) return j;
        return null;
    }
    private bool RecordsUnlocked
    {
        get
        {
            foreach (var s in GameManager.Stages) if (IsClearedForDisplay(s.Id)) return true;
            return false;
        }
    }

    // 出会う前の相手の伏せ名（カードの名前・ハンドル欄）。アバターの「?」・本文の伏字と同じ語彙で、
    //   「誰かは分からないが、投稿はそこに並んでいる」だけを言う。新しい意匠は作らない。
    private const string LockedName = "???";
    private const string LockedHandle = "@???";
    // FINAL（ミナ自身の内側）のシーン。カード生成・プレビュー・自動ダイブの3か所が同じ文字列を書いていたので定数へ。
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
    private Texture2D _customizeIcon = null!;
    private Texture2D _hubMina = null!;
    private FontFile _sideNameFont = null!, _sideTitleFont = null!;
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _sidePortraits = new();
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _sideSkies = new();
    private readonly System.Collections.Generic.Dictionary<string, Texture2D> _sideScenery = new();
    private string _sideStoryId = "", _sideStoryPrevious = "";
    private float _sideStoryBlend = 1;
    private static readonly Rect2 CompanionArea = new(0, 0, PhoneX - 1, H);
    private static readonly Rect2 StoryArea = new(PhoneX + PhoneW + 1, 0, W - PhoneX - PhoneW - 1, H);
    // 埋め草アカウントのアイコン（添字＝SnsVoices の Icon 番号。1..IconCount。[0] は未使用）。
    private Texture2D?[] _mobIcons = System.Array.Empty<Texture2D?>();

    private int _sel;
    private bool _navHeld, _zHeld, _xHeld, _cHeld, _tHeld, _jHeld, _dived;
    // SNS 画面でキーボード／パッドのカーソルがフッタに降りているとき、その項目番号（-1＝カード側）。
    //   2026-09-27 作者指摘「キーボード操作でアカウントを開くを選択できない」：フッタはマウス専用で、
    //   キー導線の J／RB は画面のどこにも表示されていなかった（FooterItems の key を DrawFooter が捨てていた）。
    //   → ←→（または最後のカードで ↓）でフッタへ降り、←→で項目を選んで Z で押せるようにした。↑でカードへ戻る。
    private int _footSel = -1;
    private double _t, _cardsEnteredT;
    private float _selT; // 選択補間 0→1（0.12s で寄る・(B)手触り）
    private int _selAnim = -1; // 補間中の選択インデックス（_sel 変化で 0 にリセット）
    // クリア済カードにぶら下げる「ミナの自動投稿」の短縮テキスト（1行に収まるよう省略・キャッシュ）
    private readonly System.Collections.Generic.Dictionary<string, string> _minaPosts = new();

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
    // デバッグ限定：--hub-shopnudge で「説明を読み終えて帰ってきた直後」のホーム誘導を再現する
    //   （スクショ／見え方の確認用。セーブは触らない＝ShopTutorialSeen も書き換えない）。
    private bool _previewShopNudge;
    // デバッグ限定：--hub-accountnudge でフッタ「アカウント」の誘導リングを撮る（--hub-preview と併用。セーブは触らない）。
    private bool _previewAccountNudge;
    // アカウント追加の説明を読み切った回だけ、SNS のフッタ「アカウント」を脈動させる（_shopNudge と同じ作法）。
    //   フラグは GameManager のランタイム限り＝帰還会話→ShopTutorial→ハブ再入場をまたいで残り、
    //   OpenJob（押す／切替UIを開く）で降りる。once キーで説明が二度と出ないので再発もしない。
    private bool AccountNudge => !_autoplay && (_previewAccountNudge || (_game?.AccountNudgePending ?? false));

    // Detail＝2-b の投稿詳細（カードがその場で開く）。本文／消された行の伏字／ミナの一言／潜り方（難易度）を
    //   1枚に置き、旧 DiffSelect.tscn への遷移をここへ吸収した（難易度の数値・実装は不変）。
    // Job＝ジョブ選択（設計書 §6・ハブのフッタから開くオーバーレイ）。Detail と同じ「カードがその場で開く」
    //   作法で 4 ジョブを縦に並べ、Z で確定・X でとじる。ハブに居る＝ラン外なので、いつでも選び直せる
    //   （ラン中＝ステージのシーンには、ジョブを書き換える導線が一つも無い＝「選んだらそのランは変えられない」）。
    private enum Mode { Home, HomeReveal, SnsOpening, Cards, Dialogue, Detail, Job, Photos }
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
    private bool NeedsSnsIntro => _game.HeartsSaved == 0 && !_game.IsIdleDialogSeen(SnsIntroSeenKey);
    private static readonly (string, string)[] SnsIntro =
    {
        ("ミナ", "ご主人様。SNSでは、投稿に埋もれた「助けて」を探します。"),
        ("ミナ", "明るい言葉の裏にも、送れずに消した言葉が残っています。わたくしには、その声が聞こえます。"),
        ("ミナ", "声のある投稿を開いてください。そこから、その人の心へ潜れます。"),
        ("ミナ", "戦うのは、その人を閉じ込めている痛みです。本人を傷つけるためではありません。"),
        ("ミナ", "その人が、もう一度、自分の言葉で話せるように。心をふさぐ言葉や記憶を、ほどいていきましょう。"),
        ("ミナ", "「ひとつでいいから、本物になって」。……まずは、この声のところへ。"),
    };
    private bool _homeRevealPending;
    private double _homeRevealT;
    private Texture2D? _homeSnapshot;
    private Rect2 _homeSnapshotRegion;
    // 強化ショップの説明を読み終えて帰ってきた直後だけ立つ（2026-09-22）。ホームの強化アイコンを
    //   脈動させ、下に「タップして ひらく」の小さな指示を出す＝押すのはプレイヤー自身。
    //   一度でも開けば（OpenHomeApp）降りる＝用が済んだら普通のホームに戻る。
    private bool _shopNudge;
    private double _detailT;      // 開いてからの経過（展開アニメと入力ゲート）
    private int _tierSel;         // 潜り方（難易度）の段。既定は前回の難易度＝Z 二押しでそのまま潜れる
    private double _jobT;         // ジョブ選択を開いてからの経過（展開アニメと入力ゲート。_detailT と同じ役）
    private int _jobSel;          // ジョブ選択のカーソル（開いたときに現在のジョブへ置く）
    private JobTuning[] _jobChoices = System.Array.Empty<JobTuning>();
    private Mode _jobReturnMode = Mode.Cards;
    private (string sp, string tx)[] _dlg = System.Array.Empty<(string, string)>();
    private int _dlgIdx;
    private double _dlgLineT;
    private double _dlgReveal;     // タイプライター表示済み文字数（＝現在ページ内）
    private string? _dlgReplyId;
    private string? _dlgSeenKey;

    // テキストボックスは2行固定。2行超の行はページに割り、送り（Z）で続きを読ませる（本文は削らない）。
    private readonly System.Collections.Generic.List<string> _dlgPages = new();
    private int _dlgPage;
    private int _dlgPagedIdx = -1;                 // _dlgPages を構築済みの行 index
    private const float DlgBodyWrapW = PhoneW - 48f;
    private string DlgCurPage => _dlgPages.Count > 0 ? _dlgPages[Mathf.Min(_dlgPage, _dlgPages.Count - 1)] : "";
    private bool DlgLastPage => _dlgPages.Count == 0 || _dlgPage >= _dlgPages.Count - 1;
    private void DlgEnsurePages()
    {
        if (_dlgPagedIdx == _dlgIdx || _dlg.Length == 0 || _dlgIdx >= _dlg.Length) return;
        _dlgPagedIdx = _dlgIdx; _dlgPage = 0;
        _dlgPages.Clear();
        _dlgPages.AddRange(UiKit.Paginate(UiKit.Zen, _dlg[_dlgIdx].tx, UiKit.FontHeading, DlgBodyWrapW, Hud.DlgMaxLines));
    }
    private void DlgNextPage() { _dlgPage++; _dlgReveal = 0; _dlgLineT = 0; }
    private bool _pendingBurn;

    // 既読スキップ（#22）：Ctrl/RB 長押しで「既読の行だけ」高速送り（本編HUDと同じ作法・ハブ小話用）。
    private int _dlgReadIdx = -1;  // 既読チェック済みの行 index
    private bool _dlgReadBefore;   // 現在行が「表示開始時点で」既読だったか
    private int _dlgLogIdx = -1;   // 会話ログ（Hud.Backlog）へ積んだ行 index（2026-09-26。オート送りでも積む）
    private bool _ffNow;           // いま高速送り中か（▶▶表示用）

    private double _toastT;
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
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        _zHeld = Pad.AdvanceHeld();
        _customizeIcon = GD.Load<Texture2D>("res://char/ui/cursor_refrain_v1.png");
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
        _hubMina = GD.Load<Texture2D>("res://char/ui/hub_mina_v1.png");
        _sideNameFont = GD.Load<FontFile>("res://assets/fonts/ShipporiMincho-SemiBold.ttf");
        _sideTitleFont = GD.Load<FontFile>("res://assets/fonts/CormorantGaramond-Italic.ttf");
        foreach (var job in Jobs.All)
        {
            string id = job.CharacterId;
            _sidePortraits[id] = id == "mina" ? _hubMina
                : GD.Load<Texture2D>($"res://char/bg2/opening/op_{id}_cutin_v1.png");
            _sideSkies[id] = GD.Load<Texture2D>($"res://char/bg2/route/{id}_far.png");
            _sideScenery[id] = GD.Load<Texture2D>($"res://char/bg2/route/{id}_mid.png");
        }
        if (_previewState == null) _sel = DefaultSelection();
        _sideStoryId = _sideStoryPrevious = SideStoryId();
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

        // 説明パート（ShopTutorial）から帰ってきた回：ホーム画面で開き、強化ショップのアイコンを
        //   選択＋誘導表示にして「押す」のを待つ（2026-09-22。押す操作はプレイヤー自身にやらせる）。
        //   フラグはランタイム限りなので、ここで消費すれば以降の入場には持ち越さない。
        if ((_game != null && _game.ShopNudgePending) || _previewShopNudge)
        {
            if (_game != null) _game.ShopNudgePending = false;
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
            // 解禁の告知（2026-09-07）。クリアして帰ってきた回に、新しく開いた導線をトーストで一度だけ出す。
            //   新しい台詞は書いていない：見出しはフッタの語（強化／記録）とキー表記をそのまま並べただけで、
            //   ミナは何も言わない。強化＝最初の面のクリア、記録＝初クリアで開くので、初回は両方が同時に開く。
            //   強化の中身の案内は ShopTutorial（同じ瞬間に一度きり出る説明パート）が持つ。
            //   ※見出しに世界の言葉（例「タイムラインに、新しい操作が増えた」）を添えるかは
            //     scenario 担当の領分なので、ここでは足していない。
            //   ★トーストは1枚しか出ない（_toast は上書き式）。1面クリアの瞬間は「強化／記録が開く」と
            //     「ジョブが増える」が同時に来るので、2枚を重ねずに1枚へまとめる：見出し＝隣に立った子、
            //     副題＝開いた導線のキー。以後の面は見出しだけ（導線はもう開いている）。
            //     回避は 2026-09-22 からショップの段（n_dodge）で買うもの＝1面クリアの瞬間に出していた
            //     HUD バナー（旧 GameManager.GrantDodge）は無くなったので、こことは渋滞しない。
            string keyHint = _game.HeartsSaved == 1
                ? (ShopUnlocked
                    ? "強化ショップ・記録アプリを解放"
                    : "記録アプリを解放")
                : "";
            var freed = JobUnlockedBy(cleared);
            if (freed != null)
            {
                string sub = $"アカウントに {AccountHandle(freed)} を追加";
                if (keyHint.Length > 0) sub += $"    {keyHint}";
                Toast($"{freed.CharacterName}が、隣に立つ", sub, JobColor(freed.Id));
            }
            else if (keyHint.Length > 0)
                Toast(keyHint, "", UiKit.Ok);
            var lines = FillObservations(ReturnDialog(cleared));
            var companion = CompanionDialogue.MenuLines(_game.SelectedJob, CompanionDialogue.Menu.Return);
            if (companion.Length > 0)
            {
                var combined = new System.Collections.Generic.List<(string, string)>(lines);
                combined.AddRange(companion);
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
            if (freed != null && freed.CharacterId == "akari" && !_game.IsIdleDialogSeen(AccountIntroSeenKey))
            {
                int at = System.Array.FindIndex(lines, l => l.Item2.StartsWith("……お疲れさまでした"));
                var spliced = new System.Collections.Generic.List<(string, string)>(lines);
                spliced.InsertRange(at < 0 ? lines.Length : at, AccountIntroAkari);
                lines = spliced.ToArray();
                seenKey = AccountIntroSeenKey;
            }
            if (lines.Length > 0) StartDialogue(lines, null, returnMode: _autoplay ? Mode.Cards : Mode.Home, seenKey: seenKey);
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
        // 道中の下書き選択（17）で送っていれば、その面の再訪小話を1本に固定して先に出す
        //   （S1-2 →「雨粒」／S3-2 →「同接」）。送った言葉が本文に一語混ざる。
        //   既読になれば通常の抽選プールへ戻る＝同じ小話を延々出さない（実質「次のハブで一度」）。
        int pin = ChoiceEffects.PinnedIdleIndex(_game, lastCleared);
        if (pin >= 0 && pin < idle.Length && !_game!.IsIdleDialogSeen($"idle_{lastCleared}_{pin}"))
        {
            string key = $"idle_{lastCleared}_{pin}";
            _game.MarkIdleDialogSeen(key);
            StartDialogue(FillDives(PinnedIdle(_game, lastCleared, idle[pin])), null);
            return;
        }
        string companionKey = $"companion_hub_{_game!.JobDef.CharacterId}";
        var companion = CompanionDialogue.MenuLines(_game.SelectedJob, CompanionDialogue.Menu.Hub);
        if (companion.Length > 0 && !_game.IsIdleDialogSeen(companionKey))
        {
            _game.MarkIdleDialogSeen(companionKey);
            StartDialogue(companion, null, noPost: true);
            return;
        }
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
        StartDialogue(FillDives(pick.lines), null);
    }

    private void BuildEntries()
    {
        var list = new System.Collections.Generic.List<Entry>();
        var counts = new System.Collections.Generic.Dictionary<string, (long, long, long)>
        {
            ["rei"] = (12, 3, 48), ["akari"] = (34, 9, 210), ["koharu"] = (58, 21, 402),
        };
        long fol = _game?.Followers ?? 0;
        foreach (var s in GameManager.Stages)
        {
            bool cleared = _game?.IsStageCleared(s.Id) ?? false;
            bool unlocked = _game?.IsStageUnlocked(s.Id) ?? true;
            string name = s.Title.Contains("—") ? s.Title.Split('—')[^1].Trim() : s.Title;
            var (rep, rt, lk) = counts.TryGetValue(s.Id, out var c) ? c : (0L, 0L, 0L);
            // クリア＝浄化が届いた投稿は伸びる（ミナのフォロワー数と連動。数字に物語の意味を持たせる）。
            if (cleared)
                ApplyClearBoost(ref rep, ref rt, ref lk, fol, _game?.HasReplied(s.Id) ?? false);
            // まだ出会っていない相手は、名前・ハンドル・本文を伏せる（2026-09-07）。
            //   名前が読めると「次に誰が来るか」が最初に割れる＝出会いの驚きが無くなる。
            //   伏せ方は既存の語彙のまま：アバターは FaceAvatar の「?」ロック円（Unlocked=false で出る）、
            //   本文の位置には RedactedBars の伏字が乗る（DrawCard が Tweet 空でも伏字を描く）。
            //   解放された時点で本物の名前・ハンドル・本文が出る＝あかりは初回から今までどおり。
            list.Add(new Entry
            {
                IsFinal = false, Id = s.Id, Scene = s.Scene,
                Name = unlocked ? name : LockedName, Handle = unlocked ? s.Handle : LockedHandle,
                Tweet = unlocked ? s.Tweet : "", Initial = unlocked && name.Length > 0 ? name.Substring(0, 1) : "?",
                Unlocked = unlocked, Cleared = cleared,
                Replies = rep, Reposts = rt, Likes = lk,
                Sort = Kind.Voice, RelT = RelTime(s.Id),
            });
        }
        if (_game?.AllStoryCleared ?? false)
        {
            list.Add(new Entry
            {
                IsFinal = true, Id = "final", Scene = FinalScene, Name = "ミナ", Handle = Handles.Mina,
                Tweet = "——汚染が、限界へ。ミナ自身の内側へダイブする。", Initial = "ミ",
                Unlocked = true, Cleared = false,
                Sort = Kind.Voice, RelT = "now",
            });
        }
        _entries = Interleave(list).ToArray();
    }

    // ───────── 2-b: タイムラインを本物の feed にする ─────────
    // 三人（＋FINAL）の投稿の間に、他人の投稿を混ぜて並べる。遊び手のやることが
    //   「一枚しかないカードで Z を押す」から「並んだ投稿の中から、声のする一本を見つける」に変わる。
    //
    // 埋め草の文面は PostPool の層1（日常）／層2（病みサイン）から引く＝道中の言葉弾・背景カードと同じ語彙
    //   （正典 wiki/08_仮台本/09_投稿文集_X風.md。ここで新しい文面は書かない）。
    //   面のテーマは、その投稿が挟まる位置の前後にいるヒロインに合わせる＝TL がその晩の面の色に寄る。
    // ハンドル・表示名・相対時刻・エンゲージ数は StageImagery の背景カードと同じ決定論生成（Frac(Sin)）で散らす。
    //   毎入場で並びが変わらない＝カーソルの記憶が効く（種は面の解放状況から起こす）。
    private const int FillerMin = 6, FillerMax = 10;

    private System.Collections.Generic.List<Entry> Interleave(System.Collections.Generic.List<Entry> voices)
    {
        var feed = new System.Collections.Generic.List<Entry>();
        // 先頭にミナの最新投稿を固定ポストとして置く（potin: これは「自分の TL だ」と言う一行）。
        var pinned = PinnedPost();
        if (pinned != null) feed.Add(pinned.Value);

        // 埋め草の本数は解放が進むほど増やす（初回の TL は薄く、三人ぶん出そろうと賑やかになる）。
        int cleared = 0;
        foreach (var v in voices) if (v.Cleared) cleared++;
        int fillers = Mathf.Clamp(FillerMin + cleared * 2, FillerMin, FillerMax);

        // 種＝解放状況（クリア数と声の本数）。同じ状況なら毎回同じ TL が並ぶ。
        var rng = new RandomNumberGenerator { Seed = (ulong)(0x5F1D + cleared * 977 + voices.Count * 31) };
        int idx = 0;   // 決定論生成の通し番号（ハンドル/表示名/時刻/数字の種）

        // 声の前後に埋め草を配る。声と声の間に 1〜2 本ずつ、余りは末尾へ。
        int left = fillers;
        for (int i = 0; i < voices.Count; i++)
        {
            int here = (i < voices.Count - 1) ? Mathf.Min(left, 1 + rng.RandiRange(0, 1)) : left;
            // 最後の声の後ろは 2 本までにして、声が画面の下に埋もれないようにする。
            if (i == voices.Count - 1) here = Mathf.Min(here, 2);
            for (int k = 0; k < here; k++) feed.Add(Filler(ThemeNear(voices, i), rng, idx++));
            left -= here;
            feed.Add(voices[i]);
        }
        for (int k = 0; k < left && k < 2; k++) feed.Add(Filler(ThemeNear(voices, voices.Count - 1), rng, idx++));
        return feed;
    }

    // 埋め草の面テーマ＝その位置の直後に来る声の面（FINAL の前後は Common に落とす＝内側の語を TL に出さない）。
    private static PostPool.Theme ThemeNear(System.Collections.Generic.List<Entry> voices, int i)
    {
        if (i < 0 || i >= voices.Count || voices[i].IsFinal) return PostPool.Theme.Common;
        return voices[i].Id switch
        {
            "akari" => PostPool.Theme.Akari,
            "koharu" => PostPool.Theme.Koharu,
            "rei" => PostPool.Theme.Rei,
            _ => PostPool.Theme.Common,
        };
    }

    // 埋め草 1 枚。層は 09 の比率（PostPool.RollLayer）どおりで、層3（本人の声）が出たら層1 に落とす
    //   ＝他人の投稿に本人の言葉を混ぜない。層2 を引いた枚は伏字が明滅する＝声のあるカードと同じ印を持ち、
    //   「病みサインはあるが、まだ潜れない」＝見分けの練習になる。
    private Entry Filler(PostPool.Theme theme, RandomNumberGenerator rng, int i)
    {
        var layer = PostPool.RollLayer(theme, rng);
        if (layer == PostPool.Layer.L3) layer = PostPool.Layer.L1;
        string body = PostPool.Draw(theme, layer, rng);
        return new Entry
        {
            IsFinal = false, Id = $"filler{i}", Scene = "", Name = FillerName(i), Handle = FillerHandle(i),
            Tweet = body, Initial = "", Unlocked = true, Cleared = false,
            Sort = Kind.Filler, RelT = FillerRelTime(i), Redacted = layer == PostPool.Layer.L2,
            Icon = SnsVoices.At(FillerVoice(i)).Icon,
            Replies = FillerCount(i, 0), Reposts = FillerCount(i, 1), Likes = FillerCount(i, 2),
        };
    }

    // ミナの最新投稿＝直近にクリアした面の帰還投稿の1行目（ReturnDialog(id)[0]）。
    //   まだ一度もクリアしていない＝投稿していないので、その場合は固定ポストを置かない。
    private Entry? PinnedPost()
    {
        string? last = null;
        for (int i = GameManager.Stages.Length - 1; i >= 0; i--)
            if (IsClearedForDisplay(GameManager.Stages[i].Id)) { last = GameManager.Stages[i].Id; break; }
        if (last == null) return null;
        var d = ReturnDialog(last);
        if (d.Length == 0) return null;
        return new Entry
        {
            IsFinal = false, Id = "pinned", Scene = "", Name = "ミナ", Handle = Handles.Mina,
            Tweet = d[0].Item2, Initial = "ミ", Unlocked = true, Cleared = false,
            Sort = Kind.Pinned, RelT = "now",
            Likes = _game?.Followers ?? 0, Reposts = (_game?.Followers ?? 0) / 4, Replies = (_game?.Followers ?? 0) / 8,
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

    // ── 埋め草のメタ生成（StageImagery.cs の背景カードと同じ決定論式。並びは通し番号 i で固定）──
    private static float Frac(float v) => v - Mathf.Floor(v);
    // 埋め草の表示名・@ハンドル・アイコンは src/SnsVoices.cs の1枚の表から引く（道中の背景カード
    //   ＝StageImagery.cs と同じ表・同じ式）。表示名／ハンドル／アイコンは必ず同じ添字＝同じ人には
    //   毎回同じ名前と同じ顔が付く。通し番号 i は Interleave() が振る（並びは解放状況で固定）。
    private static int FillerVoice(int i) => (int)(Frac(Mathf.Sin(i * 45.3f) * 10247.7f) * SnsVoices.Count) % SnsVoices.Count;
    private static string FillerHandle(int i)
    {
        int num = 10 + (int)(Frac(Mathf.Sin(i * 91.7f) * 7351.3f) * 8900f);
        return Handles.Mob(SnsVoices.At(FillerVoice(i)).Handle, num);
    }
    private static string FillerName(int i) => SnsVoices.At(FillerVoice(i)).Name;
    private static string FillerRelTime(int i)
    {
        float r = Frac(Mathf.Sin(i * 73.9f) * 4129.7f);
        return r < 0.45f ? $"{1 + (int)(r * 130f)}分" : $"{1 + (int)((r - 0.45f) * 40f)}時間";
    }
    // 返信/リポスト/いいね。埋め草は伸びていない＝二桁までに収める（三人の投稿の数字と混ざらない）。
    private static long FillerCount(int i, int kind)
    {
        float r = Frac(Mathf.Sin((i * 3 + kind) * 127.1f) * 6571.3f);
        return kind switch { 0 => (long)(r * 6f), 1 => (long)(r * 9f), _ => (long)(r * 48f) };
    }

    // クリア済投稿のエンゲージ伸長（浄化が届いた投稿は伸びる）。BuildEntries と ApplyPreview で共用。
    private static void ApplyClearBoost(ref long rep, ref long rt, ref long lk, long fol, bool replied)
    {
        lk = lk * 4 + fol * 2; rt = rt * 3 + fol / 4; rep = rep * 2 + fol / 8;
        if (replied) { lk += 180; rt += 22; rep += 46; } // ミナの返信でさらに伸びた
    }

    // デバッグ限定：表示状態だけを上書き（GameManager のセーブは一切触らない）。
    //   all   = 全ステージ解放＆クリア済（FINAL も表示）
    //   final = ストーリー全クリア直後（カードは「届いた」、FINAL を強調）
    //   lock  = 1枚目だけ「声」・以降はまだ聞こえない（初回起動の見え方）
    //   first = 1面クリア直後（あかり＝届いた／こはる＝声／ミナの固定ポストが載った feed）
    private void ApplyPreview()
    {
        if (_previewState == null) return;
        // 2-b: プレビューは「声」の側だけを組み替え、埋め草と固定ポストは Interleave に組み直させる。
        var list = new System.Collections.Generic.List<Entry>();
        foreach (var e in _entries) if (e.Sort == Kind.Voice && !e.IsFinal) list.Add(e);

        switch (_previewState)
        {
            case "all":
            case "final":
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i]; e.Unlocked = true;
                    if (!e.Cleared) // 実セーブで未クリアの分だけ、クリア連動のエンゲージ伸長も表示に反映
                    {
                        long rep = e.Replies, rt = e.Reposts, lk = e.Likes;
                        ApplyClearBoost(ref rep, ref rt, ref lk, _game?.Followers ?? 0, false);
                        e.Replies = rep; e.Reposts = rt; e.Likes = lk;
                    }
                    e.Cleared = true; list[i] = e;
                }
                list.Add(new Entry
                {
                    IsFinal = true, Id = "final", Scene = FinalScene,
                    Name = "ミナ", Handle = Handles.Mina,
                    Tweet = "——汚染が、限界へ。ミナ自身の内側へダイブする。", Initial = "ミ",
                    Unlocked = true, Cleared = false,
                    Sort = Kind.Voice, RelT = "now",
                });
                break;
            case "lock":
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    e.Cleared = false;
                    e.Unlocked = (i == 0); // 1枚目だけ「声」
                    list[i] = e;
                }
                break;
            case "first":
                // 1面クリア直後：あかり＝届いた／こはる＝次の声／レイ＝まだ聞こえない。
                //   ミナの固定ポストが feed 最上段に載った状態の見え方（2-b）。
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    e.Cleared = i == 0;
                    e.Unlocked = i <= 1;
                    if (e.Cleared)
                    {
                        long rep = e.Replies, rt = e.Reposts, lk = e.Likes;
                        ApplyClearBoost(ref rep, ref rt, ref lk, _game?.Followers ?? 0, false);
                        e.Replies = rep; e.Reposts = rt; e.Likes = lk;
                    }
                    list[i] = e;
                }
                break;
        }
        // プレビューは Unlocked を後から書き換えるので、伏せ名（BuildEntries で入れた ??? ）を
        //   最終的な解放状態に合わせて貼り直す＝プレビューでも「解放＝本名／未解放＝???」が一致する。
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            foreach (var s in GameManager.Stages)
            {
                if (s.Id != e.Id) continue;
                string real = s.Title.Contains("—") ? s.Title.Split('—')[^1].Trim() : s.Title;
                e.Name = e.Unlocked ? real : LockedName;
                e.Handle = e.Unlocked ? s.Handle : LockedHandle;
                e.Tweet = e.Unlocked ? s.Tweet : "";
                e.Initial = e.Unlocked && real.Length > 0 ? real.Substring(0, 1) : "?";
                break;
            }
            list[i] = e;
        }
        _entries = Interleave(list).ToArray();
        // カーソルは本番と同じ考え方で置く（埋め草の上には置かない）。
        //   final = 最後の声（FINAL カード）／それ以外 = 最初の「まだ届いていない声」＝次に潜る投稿。
        _sel = 0;
        bool last = _previewState == "final";
        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Sort != Kind.Voice) continue;
            if (last) { _sel = i; continue; }
            if (_entries[i].Unlocked && !_entries[i].Cleared) { _sel = i; break; }
            if (_sel == 0) _sel = i;   // 未クリアの声が無ければ最初の声へ落とす
        }
    }

    // SNSアイコンと会話の表情差分は別々に保持する。
    private void LoadFaces()
    {
        foreach (var job in Jobs.All)
        {
            _playerFaces[job.CharacterId] = ResourceLoader.Load<Texture2D>(CompanionDialogue.AccountPortrait(job.Id));
            _dialogueFaces[job.CharacterId] = ResourceLoader.Load<Texture2D>(CompanionDialogue.Portrait(job.Id));
        }
        _minaFace = ResourceLoader.Load<Texture2D>("res://char/mina_face.png");
        foreach (var e in _entries)
        {
            string id = e.Id;
            if (_faces.ContainsKey(id)) continue;
            // 埋め草＝人の顔を持たない他人。アイコンは _mobIcons 側（SnsVoices の番号）から引く。
            if (e.Sort == Kind.Filler) { _faces[id] = null; continue; }
            if (e.Sort == Kind.Pinned || e.IsFinal) { _faces[id] = _playerFaces["mina"]; continue; }
            _faces[id] = GD.Load<Texture2D>(CompanionDialogue.AccountIcon(id));
        }
        LoadMobIcons();
    }

    // 埋め草アカウントのアイコン（char/v3/icons/mob_01..12.png）。人の顔ではなく、SNS でよくある
    //   種類（猫・犬・観葉植物・コーヒー・空・海・食べ物・幾何模様・本・カメラ・自転車・月）。
    //   番号は SnsVoices の表が名前と対で持つ＝同じ名前には毎回同じアイコンが付く。
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
        "mina" => 0.05f,     // 474x720・据え置き（v3 の描き直しが無く、この値で合っている）
        "final" => 0.05f,
        _ => 0.06f,
    };

    // 既定カーソル＝次の声のある投稿（2-b でも維持）。埋め草の上には決して置かない
    //   ＝入場して Z、で潜れる導線（2 押し）が feed になっても崩れない。
    private int DefaultSelection()
    {
        string? next = _game?.NextUnclearedStageId();
        if (next != null)
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].Sort == Kind.Voice && !_entries[i].IsFinal && _entries[i].Id == next) return i;
        for (int i = _entries.Length - 1; i >= 0; i--)
            if (_entries[i].Sort == Kind.Voice) return i;
        return 0;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        string sideStory = SideStoryId();
        if (_sideStoryId != sideStory)
        {
            _sideStoryPrevious = _sideStoryId;
            _sideStoryId = sideStory;
            _sideStoryBlend = 0;
        }
        _sideStoryBlend = Mathf.Min(1, _sideStoryBlend + (float)delta / 0.45f);
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
            _navHeld = _zHeld = _xHeld = _cHeld = _tHeld = _jHeld = true;
            QueueRedraw();
            return;
        }
        if (_mode == Mode.Dialogue) { ProcessDialogue(delta); QueueRedraw(); return; }
        if (_mode == Mode.Detail) { ProcessDetail(delta); QueueRedraw(); return; }
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
    private void StartDialogue((string, string)[] lines, string? replyId, bool noPost = false, Mode returnMode = Mode.Cards, string? seenKey = null)
    {
        _mode = Mode.Dialogue;
        _dlgNoPost = noPost;
        _dlgReturnMode = returnMode;
        _dlgSeenKey = seenKey;
        _zHeld = Pad.AdvanceHeld();
        // `{n}` の差し込みで中身を書き換えるので、静的な台詞データを直接持たず必ず写しで回す
        //（そのまま持つと差し込んだ実測値が静的配列に焼き付き、次の再訪でも同じ数字が出てしまう）。
        _dlg = ((string sp, string tx)[])lines.Clone(); _dlgIdx = 0; _dlgLineT = 0; _dlgReveal = 0; _dlgReplyId = replyId;
        _dlgReadIdx = -1; _dlgReadBefore = false; _ffNow = false; _dlgLogIdx = -1;
        _dlgPages.Clear(); _dlgPage = 0; _dlgPagedIdx = -1;
    }

    // 会話ログの種別（本文色の出し分け用）。話者名と色は画面（DrawDialog／SpeakerFace）と同じものを渡す。
    private static Hud.LineKind DialogLogKind(string sp) =>
        sp.StartsWith("ミナ") ? Hud.LineKind.Mina
        : sp == "あなた"      ? Hud.LineKind.Boy
        : sp == "Ｘ 投稿"     ? Hud.LineKind.Post
        : sp.StartsWith("Ｘ") ? Hud.LineKind.Narration   // Ｘ システム（画面テキスト）
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
            Hud.PushLog(DialogLogKind(sp), sp, tx, SpeakerFace(sp).col);
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
        // 既読スキップ（#22）：行の表示開始時に一度だけ「既読か」を控え（＝高速送りの可否）、表示と同時に既読へ記録。
        if (_dlgReadIdx != _dlgIdx && _dlg.Length > 0 && _dlgIdx < _dlg.Length)
        {
            _dlgReadIdx = _dlgIdx;
            _dlgReadBefore = _game?.IsLineRead(_dlg[_dlgIdx].tx) ?? false;
            _game?.MarkLineRead(_dlg[_dlgIdx].tx);
        }
        _ffNow = Hud.SkipHeld && _dlgReadBefore; // 未読行では効かない
        _dlgLineT += delta;
        // タイプライター送り（本編HUDと同じ MsgCharsPerSec。未設定なら48）。現在ページ内を進める。
        if (_dlgReveal < len)
            _dlgReveal = Mathf.Min(len, (float)(_dlgReveal + delta * (_game?.MsgCharsPerSec ?? 48f)));
        // 高速送り：現在ページ即時表示 → 後続ページは飛ばし、最終ページ完了で次行へ（Ctrl/RB を離した瞬間に止まる）。
        if (_ffNow)
        {
            _dlgReveal = len;
            if (_dlgLineT >= 0.15) { _dlgLineT = 0; if (!DlgLastPage) DlgNextPage(); else AdvanceDialogue(); return; }
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
        // 直前の行に留まっていた実秒。次行が「いまの間、{n}秒」なら、この値をそのまま差し込む
        // （仮台本 06 の再訪小話（1）の補助観測。表示専用＝保存しない）。
        int dwell = Mathf.Max(0, Mathf.RoundToInt((float)_dlgLineT));
        _dlgIdx++;
        _dlgReveal = 0;
        _dlgLineT = 0; // 既読スキップの行ゲート用（オート送りは呼び出し側で別途リセット済み）
        _dlgPage = 0; _dlgPagedIdx = -1;
        if (_dlgIdx < _dlg.Length && _dlg[_dlgIdx].tx.Contains("{n}"))
            _dlg[_dlgIdx].tx = _dlg[_dlgIdx].tx.Replace("{n}", dwell.ToString());
        if (_dlgIdx >= _dlg.Length) EndDialogue();
    }

    private void EndDialogue()
    {
        if (_dlgSeenKey != null)
        {
            // アカウント追加の説明を読み切った回＝次に SNS を開いたとき、フッタ「アカウント」を脈動させる。
            //   会話が終わった時点はホーム画面でフッタが見えないので、誘導はここで予約して Cards で出す。
            if (_dlgSeenKey == AccountIntroSeenKey && !_autoplay) _game.AccountNudgePending = true;
            _game.MarkIdleDialogSeen(_dlgSeenKey);
            _dlgSeenKey = null;
        }
        if (_dlgNoPost)
        {
            // 投稿ではない会話（H0）＝加算もトーストも無し。オートセーブと画面戻しだけ行う。
            _dlgNoPost = false;
            _game?.AutoSave();
            _mode = _dlgReturnMode;
            _cardsEnteredT = _t;
            return;
        }
        // トーストは X の通知文の型で出す（2-a）。数値は残すが、主語は「誰に届いたか」に置き換える。
        if (_dlgReplyId != null)
        {
            long imp = _game?.GainImpression(60) ?? 0;
            _game?.AddFollowers(12);
            _game?.MarkReplied(_dlgReplyId);
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
        if (_homeRevealPending)
        {
            using var screenshot = GetViewport().GetTexture().GetImage();
            _homeSnapshot = ImageTexture.CreateFromImage(screenshot);
            _homeSnapshotRegion = new Rect2(PhoneX / W * screenshot.GetWidth(), 0, PhoneW / W * screenshot.GetWidth(), screenshot.GetHeight());
            _homeRevealPending = false;
            _homeRevealT = 0;
            _mode = Mode.HomeReveal;
            _toastT = 0;
        }
    }

    private void ProcessHomeReveal(double delta)
    {
        double previous = _homeRevealT;
        _homeRevealT += delta;
        if (previous < 1.15 && _homeRevealT >= 1.15) Audio.Instance?.PlayUiBuy();
        if (_homeRevealT < 2.3) return;
        _homeSnapshot = null;
        _mode = Mode.Home;
        _homeSel = 1;
        _zHeld = Pad.AdvanceHeld();
        _game.MarkIdleDialogSeen(HomeRevealSeenKey);
        _game.AutoSave();
        TryOpenShopTutorial();
    }

    // 強化ショップの説明パート（ShopTutorial）を一度だけ挟む。開いたら true（呼び元は以降を打ち切る）。
    //   置き場所：帰還会話（お疲れさま）→ ホーム解禁演出でアイコンが光る → ここ → ホームで押す、の順。
    //   説明の最終行「——では、まいりましょう。」が、戻ってきたホームの光ったアイコンへの号令になる。
    //   説明を先（ステージ側）に出すと「ひと息つきましょう」と帰還会話の帰宅挨拶が二重に立つので、
    //   かならず帰還会話のあとに置く。ShopTutorial は読み切るとハブへ戻す（ShopNudgePending を立てて）。
    //   解禁演出（HomeReveal）とホーム入場の両方から呼ぶ＝演出が出ない状態のセーブでも取りこぼさない。
    private bool TryOpenShopTutorial()
    {
        if (_autoplay || _dived || _game == null || _previewShopNudge) return false;
        if (_game.ShopTutorialSeen || !ShopUnlocked) return false;
        _game.ShopTutorialSeen = true;   // 一度きり（直後の AutoSave で永続化）
        _game.AutoSave();
        _dived = true;                   // 遷移中は入力を食う（多重遷移よけ。他の導線と同じ作法）
        GetTree().ChangeSceneToFile("res://ShopTutorial.tscn");
        return true;
    }

    private void DrawHomeReveal()
    {
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((float)_homeRevealT / 1.1f, 0f, 1f));
        if (_homeSnapshot == null || k >= 1f) return;
        Vector2 center = new Vector2(W / 2f, H / 2f).Lerp(HomeAppRect(0).Position + new Vector2(HomeAppRect(0).Size.X / 2f, 48f), k);
        Vector2 size = new Vector2(PhoneW, H) * Mathf.Lerp(1f, 0.12f, k);
        float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp((k - 0.75f) / 0.25f, 0f, 1f));
        DrawTextureRectRegion(_homeSnapshot, new Rect2(center - size / 2f, size), _homeSnapshotRegion, new Color(1, 1, 1, alpha));
    }

    // トースト：1行目＝世界の言葉（通知文）、2行目＝現状の数値（小さく・任意）。
    private void Toast(string msg, string sub, Color col) { _toast = msg; _toastSub = sub; _toastCol = col; _toastT = 2.6; }

    private static readonly string[] HomeApps = { "SNS", "強化ショップ", "記録", "写真", "カスタマイズ" };
    private const int HomeAppIdBase = 22000;

    private Rect2 HomeAppRect(int index)
    {
        float width = (PhoneW - 48f) / 4f;
        return new Rect2(PhoneX + 24f + index % 4 * width, 288f + index / 4 * 144f, width, 132f);
    }

    private Rect2 HomeAppIconRect(int index)
    {
        var rect = HomeAppRect(index);
        return new Rect2(rect.Position + new Vector2((rect.Size.X - 80f) / 2f, 8f), new Vector2(80f, 80f));
    }

    private bool HomeAppUnlocked(int index) => index == 0 || index >= 3 || (index == 1 ? ShopUnlocked : RecordsUnlocked);

    private void ProcessHome()
    {
        // 説明がまだなら、ホームを触らせる前に一度だけ挟む（解禁演出を経ないセーブの取りこぼし防止）。
        if (TryOpenShopTutorial()) return;
        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < HomeApps.Length; i++) UiKit.Hotspot(HomeAppRect(i), HomeAppIdBase + i);
        int hovered = UiKit.HoveredId() - HomeAppIdBase;
        if (Pad.UsingMouse && hovered >= 0 && hovered < HomeApps.Length && hovered != _homeSel)
        {
            _homeSel = hovered;
            Audio.Instance?.PlayUiMove();
        }
        int clicked = UiKit.ClickedId(Pad.MouseClick()) - HomeAppIdBase;
        if (clicked >= 0 && clicked < HomeApps.Length && _t > 0.3)
        {
            OpenHomeApp(clicked);
            return;
        }
        bool previous = Input.IsActionPressed("ui_left") || Input.IsActionPressed("ui_up");
        bool next = Input.IsActionPressed("ui_right") || Input.IsActionPressed("ui_down");
        if ((previous || next) && !_navHeld)
        {
            if (Input.IsActionPressed("ui_up")) { if (_homeSel >= 4) _homeSel -= 4; }
            else if (Input.IsActionPressed("ui_down")) { if (_homeSel < 4) _homeSel = Mathf.Min(HomeApps.Length - 1, _homeSel + 4); }
            else _homeSel = (_homeSel + (previous ? -1 : 1) + HomeApps.Length) % HomeApps.Length;
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = previous || next;
        bool accept = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool edge = accept && !_zHeld; _zHeld = accept;
        if (edge && _t > 0.3) OpenHomeApp(_homeSel);
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
        if (index == 0)
        {
            _footSel = -1;   // SNS は毎回カード側から始める
            _mode = Mode.SnsOpening;
            _snsOpeningT = 0;
            _cardsEnteredT = _t;
            if (NeedsSnsIntro)
            {
                _sel = DefaultSelection();
                _feedScroll = _feedScrollTarget = Mathf.Clamp(CardTop(_sel), 0f, FeedMaxScroll());
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
        _dived = true;
        PhoneAppTransition.Open(this, index == 1 ? "res://Shop.tscn" : index == 4 ? "res://Customize.tscn" : "res://Records.tscn");
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
        _navHeld = _xHeld = _cHeld = _tHeld = _jHeld = true;
        if (NeedsSnsIntro)
        {
            StartDialogue(SnsIntro, null, noPost: true, seenKey: SnsIntroSeenKey);
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
            DrawRect(new Rect2(PhoneX, 0, PhoneW, H), PhoneBg);
            DrawTimeline(1f);
            DrawFooter();
        }
        float cover = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - 0.7f) / 0.3f, 0f, 1f));
        UiKit.Box(this, panel, new Color(new Color("82d8dc"), cover), 8f * (1f - k));
        DrawSnsIcon(panel.GetCenter(), new Color(new Color("242c32"), cover));
    }

    private void DrawSnsIcon(Vector2 center, Color ink)
    {
        UiKit.Box(this, new Rect2(center - new Vector2(23, 20), new Vector2(46, 34)), Colors.Transparent, 8f, ink, 3f);
        DrawPolyline(new[] { center + new Vector2(-11, 14), center + new Vector2(-11, 23), center + new Vector2(1, 14) }, ink, 3f, true);
        for (int dot = 0; dot < 3; dot++) DrawCircle(center + new Vector2(-11 + dot * 11, -3), 2.5f, ink);
    }

    private readonly record struct PhotoEntry(string Id, string Title, string Sub, string Path, Vector4 Region, Color Accent, string[] Keys);
    private static readonly Vector4 PhotoFull = new(0, 0, 1, 1);
    private static Vector4 StoryRegion(int shot, int rows) => new((shot % 2) / 2f, (shot / 2) / (float)rows, 0.5f, 1f / rows);
    private static string[] K(params string[] keys) => keys;
    // 他ジョブ潜行の回想／アフターの解禁キー。6枚の絵は**キャラ単位**（cg_{id}_playable_{kind}_v1.png）で
    //   面別ではないので、「そのキャラで潜って回想／アフターを一度見た」で開く（どの面でもよい）。
    //   旧キー（{id}_playable_ch{n}_{kind}＝CharacterStoryFilm の FilmId）は**もう誰も立てない**――回想／アフターは
    //   2026-09-23 に一枚絵をやめて吹き出しだけになった（CharacterStory.Memory / Aftermath）ので、
    //   そちらが立てる CharacterStory.SeenKey へ付け替える（放置すると14枚中6枚が永久に開かない）。
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
        new("mina_memory", "ミナ / 回想", "Memory Log", "res://char/v3/mina_story_atlas.png", StoryRegion(3, 3), new Color("87d7ed"), K("mina_memory")),
        new("mina_after", "ミナ / 手を重ねる", "After Scene", "res://char/bg2/story/cg_mina_take_hand_v1.png", PhotoFull, new Color("87d7ed"), K("mina_aftermath")),
        new("akari_playable_memory", "あかり / もう一度", "Playable Memory", "res://char/bg2/story/cg_akari_playable_memory_v1.png", PhotoFull, new Color("f0c969"), PlayableKeys("akari", aftermath: false)),
        new("akari_playable_after", "あかり / 帰還", "Playable After", "res://char/bg2/story/cg_akari_playable_aftermath_v1.png", PhotoFull, new Color("f0c969"), PlayableKeys("akari", aftermath: true)),
        new("koharu_playable_memory", "こはる / もう一度", "Playable Memory", "res://char/bg2/story/cg_koharu_playable_memory_v1.png", PhotoFull, new Color("a6dac8"), PlayableKeys("koharu", aftermath: false)),
        new("koharu_playable_after", "こはる / 帰還", "Playable After", "res://char/bg2/story/cg_koharu_playable_aftermath_v1.png", PhotoFull, new Color("a6dac8"), PlayableKeys("koharu", aftermath: true)),
        new("rei_playable_memory", "レイ / もう一度", "Playable Memory", "res://char/bg2/story/cg_rei_playable_memory_v1.png", PhotoFull, new Color("de91b9"), PlayableKeys("rei", aftermath: false)),
        new("rei_playable_after", "レイ / 帰還", "Playable After", "res://char/bg2/story/cg_rei_playable_aftermath_v1.png", PhotoFull, new Color("de91b9"), PlayableKeys("rei", aftermath: true)),
        new("mina_phase_rain", "未送信の雨", "Mina Phase", BossMina.PhaseBackground(1), PhotoFull, new Color("74b8e8"), MinaPhaseKeys(1)),
        new("mina_phase_clap", "消えない拍手", "Mina Phase", BossMina.PhaseBackground(2), PhotoFull, new Color("ee9bb7"), MinaPhaseKeys(2)),
        new("mina_phase_mask", "仮面の向こう", "Mina Phase", BossMina.PhaseBackground(3), PhotoFull, new Color("f0d98a"), MinaPhaseKeys(3)),
        new("mina_phase_voice", "わたしの声", "Mina Phase", BossMina.PhaseBackground(4), PhotoFull, new Color("85e8d0"), MinaPhaseKeys(4)),
        new("ending", "覚えている声", "Ending", "res://char/bg2/ending/cg_ep_together_v1.png", PhotoFull, new Color("ffd98a"), K("ending")),
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
            DrawTextureRectRegion(tex, rect, PhotoSource(tex, entry.Region, rect.Size), new Color(1, 1, 1, alpha));
            DrawRect(rect, new Color(0, 0, 0, 0.08f * alpha));
            return;
        }
        UiKit.Box(this, rect, new Color(0.08f, 0.09f, 0.11f, 0.96f * alpha), 6f);
        for (int i = -2; i < 8; i++)
        {
            float x = rect.Position.X + i * 42f;
            DrawLine(new Vector2(x, rect.Position.Y + rect.Size.Y), new Vector2(x + 92f, rect.Position.Y),
                new Color(1, 1, 1, 0.045f * alpha), 1f);
        }
        Vector2 c = rect.GetCenter();
        DrawArc(c + new Vector2(0, -5f), 13f, Mathf.Pi, Mathf.Tau, 24, new Color(UiKit.Text3, 0.58f * alpha), 2f, true);
        UiKit.Box(this, new Rect2(c - new Vector2(15f, 2f), new Vector2(30f, 20f)), new Color(UiKit.Text3, 0.58f * alpha), 4f);
    }

    private void DrawPhotoCard(int index, float alpha)
    {
        var entry = PhotoEntries[index];
        var rect = PhotoItemRect(index);
        if (rect.End.Y < PhotoGridTop || rect.Position.Y > PhotoGridBottom) return;
        bool acquired = PhotoAcquired(entry), selected = index == _photoSel, wallpaper = IsWallpaper(entry);
        if (selected) UiKit.Box(this, rect.Grow(4f), new Color(entry.Accent, 0.12f * alpha), 7f, new Color(entry.Accent, 0.72f * alpha), 1.5f);
        UiKit.Box(this, rect, new Color(0.03f, 0.035f, 0.046f, 0.95f * alpha), 6f,
            new Color(acquired ? entry.Accent : UiKit.Text4, (selected ? 0.44f : 0.2f) * alpha), 1f);
        var shot = new Rect2(rect.Position + new Vector2(8f, 8f), new Vector2(rect.Size.X - 16f, 64f));
        DrawPhotoImage(shot, entry, acquired, alpha);
        if (wallpaper)
        {
            var badge = new Rect2(shot.End.X - 50f, shot.Position.Y + 6f, 42f, 18f);
            UiKit.Box(this, badge, new Color(entry.Accent, 0.92f * alpha), 4f);
            UiKit.Text(this, UiKit.ZenBold, badge.Position + new Vector2(0, 2f), "背景", 11,
                new Color(0.02f, 0.025f, 0.03f, alpha), HorizontalAlignment.Center, badge.Size.X);
        }
        UiKit.Text(this, UiKit.Mono, rect.Position + new Vector2(12f, 79f), acquired ? entry.Sub : "LOCKED", 11,
            new Color(acquired ? entry.Accent : UiKit.Text4, alpha));
        UiKit.Text(this, UiKit.ZenBold, rect.Position + new Vector2(12f, 94f), acquired ? entry.Title : "未取得のシーン", 13,
            new Color(acquired ? UiKit.White : UiKit.Text3, alpha), HorizontalAlignment.Left, rect.Size.X - 24f);
    }

    private void DrawPhotos()
    {
        float a = Mathf.Clamp((float)_photoT / 0.18f, 0f, 1f);
        DrawRect(new Rect2(PhoneX, 0, PhoneW, H), PhoneBg);
        // グリッドはヘッダ／ヒーロー枠より先に描き、上側を PhoneBg で塗り潰してから重ねる＝スクロールで
        //   PhotoGridTop より上へ出たカードがヒーロー枠に被らない（2026-09-23 ユーザー報告：未取得のヒーロー枠に
        //   「はじまりの光」等の行が重なって見えていた。DrawPhotoCard の早期 return は完全に外れたカードしか弾かない）。
        for (int i = 0; i < PhotoEntries.Length; i++) DrawPhotoCard(i, a);
        DrawRect(new Rect2(PhoneX, 0, PhoneW, PhotoGridTop), PhoneBg);
        DrawBackButton(PhotoCloseRect(), PhotoCloseId, a);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(PhoneX + 66f, 25f), "写真", 22, new Color(UiKit.White, a));
        UiKit.Text(this, UiKit.Mono, new Vector2(PhoneX + PhoneW - 154f, 29f), $"{PhotoAcquiredCount()}/{PhotoEntries.Length}", 18,
            new Color(UiKit.Gold, a), HorizontalAlignment.Right, 128f);
        DrawRect(new Rect2(PhoneX + 24f, 68f, PhoneW - 48f, 1f), new Color(1, 1, 1, 0.10f * a));

        var entry = PhotoEntries[Mathf.Clamp(_photoSel, 0, PhotoEntries.Length - 1)];
        bool acquired = PhotoAcquired(entry);
        var hero = new Rect2(PhoneX + 24f, 88f, PhoneW - 48f, 208f);
        UiKit.Box(this, hero, new Color(0.03f, 0.035f, 0.046f, 0.95f * a), 8f, new Color(entry.Accent, 0.36f * a), 1f);
        var image = new Rect2(hero.Position + new Vector2(10f, 10f), new Vector2(hero.Size.X - 20f, 142f));
        DrawPhotoImage(image, entry, acquired, a);
        DrawRect(new Rect2(image.Position.X, image.End.Y - 38f, image.Size.X, 38f), new Color(0, 0, 0, 0.38f * a));
        if (acquired)
        {
            bool wallpaper = IsWallpaper(entry);
            float bw = wallpaper ? 112f : 126f;
            var badge = new Rect2(image.End.X - bw - 8f, image.Position.Y + 8f, bw, 26f);
            UiKit.Box(this, badge, new Color(wallpaper ? entry.Accent : PhoneBg, 0.90f * a), 6f,
                new Color(entry.Accent, 0.55f * a), 1f);
            UiKit.Text(this, UiKit.ZenBold, badge.Position + new Vector2(0, 5f), wallpaper ? "現在の背景" : $"{Pad.ConfirmToken} 背景にする", 12,
                new Color(wallpaper ? new Color(0.02f, 0.025f, 0.03f) : UiKit.White, a), HorizontalAlignment.Center, badge.Size.X);
        }
        UiKit.Text(this, UiKit.Mono, hero.Position + new Vector2(20f, 158f), acquired ? entry.Sub : "LOCKED", 12,
            new Color(acquired ? entry.Accent : UiKit.Text4, a));
        UiKit.Text(this, UiKit.ZenBold, hero.Position + new Vector2(20f, 176f), acquired ? entry.Title : "まだ取得していないシーン", 18,
            new Color(acquired ? UiKit.White : UiKit.Text3, a));

        DrawRect(new Rect2(PhoneX, PhotoGridBottom, PhoneW, H - PhotoGridBottom), PhoneBg);
        DrawPhotoScrollHint(a);
        string hint = $"{Pad.ConfirmToken} 背景にする　{Pad.CancelToken} もどる";
        UiKit.Text(this, UiKit.Zen, new Vector2(PhoneX, 666f), hint, 14, new Color(UiKit.Text2, 0.78f * a),
            HorizontalAlignment.Center, PhoneW);
        UiKit.Box(this, new Rect2(PhoneX + (PhoneW - 116f) / 2f, H - 18f, 116f, 3f), new Color(UiKit.White, 0.65f * a), 1.5f);
    }

    private void DrawPhotoScrollHint(float alpha)
    {
        float max = PhotoMaxScroll();
        if (max <= 1f) return;
        const float viewH = PhotoGridBottom - PhotoGridTop;
        float th = Mathf.Max(34f, viewH * viewH / (viewH + max));
        float ty = PhotoGridTop + (viewH - th) * Mathf.Clamp(_photoScroll / max, 0f, 1f);
        DrawRect(new Rect2(PhoneX + PhoneW - 8f, PhotoGridTop, 2f, viewH), new Color(1, 1, 1, 0.08f * alpha));
        DrawRect(new Rect2(PhoneX + PhoneW - 8f, ty, 2f, th), new Color(UiKit.Info, 0.58f * alpha));
    }

    private bool DrawSelectedPhotoWallpaper()
    {
        int index = WallpaperPhotoIndex();
        if (index < 0) return false;
        var entry = PhotoEntries[index];
        if (PhotoTexture(entry) is not Texture2D tex) return false;
        var rect = new Rect2(PhoneX, 0, PhoneW, H);
        DrawTextureRectRegion(tex, rect, PhotoSource(tex, entry.Region, rect.Size), new Color(0.82f, 0.84f, 0.9f));
        DrawRect(rect, new Color(0.02f, 0.025f, 0.035f, 0.35f));
        return true;
    }

    private void DrawHome()
    {
        if (!DrawSelectedPhotoWallpaper() && _nightBg?.Texture is Texture2D wallpaper)
        {
            float sourceW = wallpaper.GetHeight() * PhoneW / H;
            var source = new Rect2((wallpaper.GetWidth() - sourceW) / 2f, 0, sourceW, wallpaper.GetHeight());
            DrawTextureRectRegion(wallpaper, new Rect2(PhoneX, 0, PhoneW, H), source);
        }
        DrawRect(new Rect2(PhoneX, 0, PhoneW, H), new Color(0.02f, 0.03f, 0.04f, 0.18f));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(PhoneX + 28f, 24f), "ホーム", 16, UiKit.Text2);
        var now = System.DateTime.Now;
        UiKit.Text(this, UiKit.Mono, new Vector2(PhoneX, 112f), now.ToString("HH:mm"), 48, UiKit.White, HorizontalAlignment.Center, PhoneW);
        UiKit.Text(this, UiKit.Zen, new Vector2(PhoneX, 180f), now.ToString("M月d日（ddd）", System.Globalization.CultureInfo.GetCultureInfo("ja-JP")),
            16, UiKit.Text2, HorizontalAlignment.Center, PhoneW);
        for (int i = 0; i < HomeApps.Length; i++)
        {
            var rect = HomeAppRect(i);
            var icon = HomeAppIconRect(i);
            bool unlocked = HomeAppUnlocked(i);
            float activation = _mode == Mode.HomeReveal && i == 1 ? Mathf.Clamp(((float)_homeRevealT - 1.15f) / 0.65f, 0f, 1f) : 1f;
            bool focused = _mode == Mode.Home && (Pad.UsingMouse ? UiKit.HoveredId() == HomeAppIdBase + i : i == _homeSel);
            Color color = i == 0 ? new Color("82d8dc") : i == 1 ? new Color("eba4b9") : i == 2 ? new Color("bddba7") : i == 3 ? new Color("f0c969") : new Color("b6c7eb");
            if (!unlocked) color = new Color("737b85");
            color = new Color("737b85").Lerp(color, activation);
            if (focused) UiKit.Box(this, icon.Grow(6f), new Color(1, 1, 1, 0.08f), 8f, new Color(UiKit.White, 0.75f), 1.5f);
            // 初回だけの誘導（2026-09-22）：説明を読んだ直後、強化ショップのアイコンを脈動させる。
            //   外へ広がりながら薄くなる輪を2枚ずらして重ねる＝「押して」と言わずに押す場所を示す。
            //   ミナの台詞は足していない（説明の最終行が号令の役をもう持っている）。
            if (_shopNudge && i == 1 && _mode == Mode.Home)
            {
                for (int ring = 0; ring < 2; ring++)
                {
                    float phase = Mathf.PosMod((float)_t * 0.8f + ring * 0.5f, 1f);
                    UiKit.Box(this, icon.Grow(4f + phase * 16f), Colors.Transparent, 8f + phase * 8f,
                        new Color(color, (1f - phase) * 0.55f), 2f);
                }
            }
            UiKit.Box(this, icon, color, 8f);
            if (_mode == Mode.HomeReveal && i == 1 && activation > 0f)
                UiKit.Box(this, icon.Grow(6f), Colors.Transparent, 8f, new Color(UiKit.Light, Mathf.Sin(activation * Mathf.Pi)), 2f);
            Vector2 c = icon.GetCenter();
            Color ink = new("242c32");
            if (i == 0)
            {
                DrawSnsIcon(c, ink);
            }
            else if (i == 1)
            {
                UiKit.Box(this, new Rect2(c - new Vector2(20, 12), new Vector2(40, 36)), Colors.Transparent, 5f, ink, 3f);
                DrawArc(c + new Vector2(0, -12), 11f, Mathf.Pi, Mathf.Tau, 24, ink, 3f, true);
                DrawLine(c + new Vector2(-8, 5), c + new Vector2(8, 5), ink, 3f, true);
                DrawLine(c + new Vector2(0, -3), c + new Vector2(0, 13), ink, 3f, true);
            }
            else if (i == 2)
            {
                for (int bar = 0; bar < 3; bar++) DrawRect(new Rect2(c.X - 21f + bar * 16f, c.Y + 5f - bar * 12f, 10f, 16f + bar * 12f), ink);
            }
            else if (i == 3)
            {
                UiKit.Box(this, new Rect2(c - new Vector2(25f, 16f), new Vector2(50f, 34f)), Colors.Transparent, 7f, ink, 3f);
                UiKit.Box(this, new Rect2(c + new Vector2(-15f, -23f), new Vector2(19f, 9f)), ink, 3f);
                DrawCircle(c + new Vector2(1f, 1f), 11f, Colors.Transparent);
                DrawArc(c + new Vector2(1f, 1f), 11f, 0, Mathf.Tau, 32, ink, 3f, true);
                DrawCircle(c + new Vector2(1f, 1f), 4f, ink);
            }
            else
            {
                DrawTextureRect(_customizeIcon, new Rect2(c - new Vector2(17f, 20f), new Vector2(34f, 40f)), false);
            }
            if (!unlocked || activation == 0f)
            {
                Vector2 p = icon.Position + new Vector2(73f, 75f);
                DrawCircle(p, 12f, PhoneBg);
                DrawArc(p + new Vector2(0, -2f), 4f, Mathf.Pi, Mathf.Tau, 12, UiKit.Text2, 1.5f, true);
                UiKit.Box(this, new Rect2(p - new Vector2(5f, 1f), new Vector2(10f, 8f)), UiKit.Text2, 2f);
            }
            UiKit.Text(this, UiKit.ZenBold, rect.Position + new Vector2(0, 104f), HomeApps[i], 15,
                unlocked ? UiKit.White : UiKit.Text2, HorizontalAlignment.Center, rect.Size.X);
        }
        // 誘導の指示（初回のみ）。アイコン列の下に一行だけ。操作を名指しするのは UI の言葉で、
        //   ミナの台詞ではない＝語りの本数を増やさない。点滅は輪と同じ周期に合わせて散らかさない。
        if (_shopNudge && _mode == Mode.Home)
        {
            var nudge = HomeAppRect(1);
            float blink = 0.65f + 0.35f * Mathf.Sin((float)_t * 4f);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(PhoneX, nudge.Position.Y + 136f),
                Pad.UsingMouse ? "▲ クリックして ひらく" : "▲ Z で ひらく", 14,
                new Color(new Color("eba4b9"), blink), HorizontalAlignment.Center, PhoneW);
        }
        var job = _game.JobDef;
        DrawRect(new Rect2(PhoneX + 24f, 610f, PhoneW - 48f, 1f), new Color(1, 1, 1, 0.18f));
        UiKit.FaceAvatar(this, new Vector2(PhoneX + 46f, 645f), 20f, _playerFaces[job.CharacterId], JobColor(job.Id), false, 0f, 1f, _t);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(PhoneX + 78f, 623f), job.CharacterName, 17, UiKit.White);
        UiKit.Text(this, UiKit.Mono, new Vector2(PhoneX + 78f, 650f), AccountHandle(job), 12, UiKit.Text2);
        UiKit.Text(this, UiKit.Zen, new Vector2(PhoneX + 260f, 625f), $"救った心  {_game.HeartsSaved}", 14, UiKit.Text2, HorizontalAlignment.Right, PhoneW - 284f);
        UiKit.Text(this, UiKit.Mono, new Vector2(PhoneX + 260f, 650f), $"Imp  {UiKit.Abbrev(_game.Impression)}", 14, UiKit.Gold, HorizontalAlignment.Right, PhoneW - 284f);
        UiKit.Box(this, new Rect2(PhoneX + (PhoneW - 116f) / 2f, H - 18f, 116f, 3f), new Color(UiKit.White, 0.65f), 1.5f);
    }

    // ───────── カード ─────────
    // 返信できるのは「声のあるクリア済みカード」だけ（埋め草・固定ポストには返信しない）。
    private bool CanReplySel() => !_autoplay && IsVoice(_sel)
        && _entries[_sel].Cleared && !(_game?.HasReplied(_entries[_sel].Id) ?? true);

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

    private void ProcessCards()
    {
        if (_autoplay) { if (_t - _cardsEnteredT >= AutoDiveDelay) DiveAuto(); return; }

        // R＝タイムラインの再読込。パッドの Start はポーズメニュー（開閉）と衝突するため外した。
        if (Input.IsKeyPressed(Key.R)) { GetTree().ReloadCurrentScene(); return; }

        float wheel = Pad.WheelDelta();
        if (wheel != 0f && FeedMaxScroll() > 0f)
        {
            _feedScrollTarget = Mathf.Clamp(_feedScrollTarget - wheel * WheelStep, 0f, FeedMaxScroll());
        }

        // マウス：フレーム頭でホットスポットをクリア＋カード矩形とフッタ操作を登録（カードモードのみ＝会話中は登録しない）。
        //   カード id = 0..entries-1／フッタ id = FooterIdBase+i（空間を分けて種別を判別する）。
        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < _entries.Length; i++)
            if (CardHitRect(i).HasArea()) UiKit.Hotspot(CardHitRect(i), i);
        var footItems = FooterItems();
        for (int i = 0; i < footItems.Count; i++)
            UiKit.Hotspot(FooterItemRect(i), FooterIdBase + i);
        UiKit.Hotspot(HeaderJobRect(), JobOpenId);
        int hov = UiKit.HoveredId();
        // ホバー追従はカード側のみ（フッタはボタン＝ホバーで選択を動かさない。下敷きは DrawFooter が hov で描く）。
        if (Pad.UsingMouse && hov >= 0 && hov < _entries.Length && (hov != _sel || _footSel >= 0))
        {
            _sel = hov; _footSel = -1; Audio.Instance?.PlayUiMove();
        }
        int clk = UiKit.ClickedId(Pad.MouseClick());
        if (_footSel >= footItems.Count) _footSel = footItems.Count - 1;   // 「返信」の出入りで数が変わっても外へ出さない
        if (clk == JobOpenId && _t > 0.3)
        {
            OpenJob();
            return;
        }
        // フッタボタンのクリック → 対応アクション（強化=ショップ入口が主目的。返信/記録も同じ導線）。
        if (clk >= FooterIdBase)
        {
            int fi = clk - FooterIdBase;
            if (fi >= 0 && fi < footItems.Count) FooterClick(footItems[fi].act);
            return; // フッタを押したフレームはカード確定へ流さない
        }

        // ↑↓：カード送り。最後のカードで ↓、またはどこでも ←→ でフッタへ降りる（着地は「アカウント」）。
        //   フッタ上では ←→ で項目を巡り、↑ でカードへ戻る（カードの選択は降りる前のまま）。
        bool up = Input.IsActionPressed("ui_up"), down = Input.IsActionPressed("ui_down");
        bool left = Input.IsActionPressed("ui_left"), right = Input.IsActionPressed("ui_right");
        if ((up || down || left || right) && !_navHeld)
        {
            if (_footSel >= 0)
            {
                if (up) { _footSel = -1; Audio.Instance?.PlayUiMove(); }
                else if (left || right)
                {
                    _footSel = (_footSel + (left ? -1 : 1) + footItems.Count) % footItems.Count;
                    Audio.Instance?.PlayUiMove();
                }
            }
            else if (left || right || (down && _sel >= _entries.Length - 1))
            {
                _footSel = System.Math.Max(0, footItems.FindIndex(f => f.act == FootAct.Job));
                Audio.Instance?.PlayUiMove();
            }
            else if (_entries.Length > 0)
            {
                if (up) _sel = (_sel - 1 + _entries.Length) % _entries.Length;
                if (down) _sel = _sel + 1;
                UpdateFeedScrollTarget();
                Audio.Instance?.PlayUiMove();
            }
        }
        _navHeld = up || down || left || right;

        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        // フッタにカーソルがあるときの Z＝その項目をクリックしたのと同じ（カードの確定へは流さない）。
        if (_footSel >= 0 && zEdge)
        {
            FooterClick(footItems[_footSel].act);
            return;
        }
        // マウス：カードクリックで選択＋ダイブ（KB の Z と同じ確定経路）。clk はカード id のみ（フッタは上で処理済み）。
        bool dive = zEdge && _t > 0.3;
        if (clk >= 0 && clk < _entries.Length && _t > 0.3) { _sel = clk; dive = true; }
        if (dive && _sel >= 0 && _sel < _entries.Length)
        {
            var e = _entries[_sel];
            // 2-b: Z でカードがその場で開く（投稿詳細）。潜れるのは声のある投稿だけ。
            if (e.Sort != Kind.Voice) Audio.Instance?.PlayUiDeny();   // 埋め草・固定ポスト＝無言（開かない）
            else if (e.Unlocked) OpenDetail();
            else
            {
                // 未解放＝「ロック中」ではなく「まだ聞こえない」。拒否音の代わりにミナが一言だけ返す（2-a）。
                Audio.Instance?.PlayUiDeny();
                StartDialogue(NotYetDialog, null, noPost: true);
            }
        }

        bool c = Input.IsKeyPressed(Key.C) || Pad.Pressed(JoyButton.Y);
        bool cEdge = c && !_cHeld; _cHeld = c;
        if (cEdge && CanReplySel())
        {
            var lines = ReplyDialog(_entries[_sel].Id);
            if (lines.Length > 0) { Audio.Instance?.PlayUiConfirm(); StartDialogue(lines, _entries[_sel].Id); }
        }

        // もどる（ホームへ）＝X／Esc／パッドB（Esc は 2026-09-26 に「一つ前の画面へ」として復帰。メニューは M）。
        bool x = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool xEdge = x && !_xHeld; _xHeld = x;
        if (xEdge && _t > 0.3 && !_dived) { GoHome(); return; }

        // T：クリアタイムの記録画面へ（戻ると Hub に復帰）。一面クリアするまでは開かない。
        bool tk = Input.IsKeyPressed(Key.T) || Pad.Pressed(JoyButton.LeftShoulder);
        bool tEdge = tk && !_tHeld; _tHeld = tk;
        if (tEdge && _t > 0.3 && !_dived && RecordsUnlocked) OpenHomeApp(2);

        // J / RB：ジョブ選択。画面遷移ではなくハブの上に開く（Detail と同じ扱い）＝解禁ゲート無し。
        bool jk = Input.IsKeyPressed(Key.J) || Pad.Pressed(JoyButton.RightShoulder);
        bool jEdge = jk && !_jHeld; _jHeld = jk;
        if (jEdge && _t > 0.3 && !_dived) OpenJob();
    }

    // フッタボタン（マウス）押下のアクション。キー導線（X=強化 / T=記録 / C=返信）と同じ処理へ合流する。
    private void FooterClick(FootAct act)
    {
        if (_t <= 0.3 || _dived) return;
        switch (act)
        {
            case FootAct.Home:
                GoHome();
                break;
            case FootAct.Reply:
                if (CanReplySel())
                {
                    var lines = ReplyDialog(_entries[_sel].Id);
                    if (lines.Length > 0) { Audio.Instance?.PlayUiConfirm(); StartDialogue(lines, _entries[_sel].Id); }
                }
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
        // FINAL は潜り方を選ばせない（従来の FINAL の扱いを踏襲＝深さは選ばずそのまま内側へ）。
        bool tiers = !e.IsFinal;

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
        bool jk = Input.IsKeyPressed(Key.J) || Pad.Pressed(JoyButton.RightShoulder);
        bool jEdge = jk && !_jHeld; _jHeld = jk;
        if (dclk == JobOpenId || jEdge)
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
        // FINAL 初挑戦に結び手（ミナ）のままで潜ろうとしたら、ダイブを止めてアカウント切り替えを開く
        //   （2026-09-17 ユーザー指示）。拒否して突き放さず、そのまま選び直せる場所へ連れて行く
        //   ＝一覧からは既にミナが落ちている（OpenJob）ので、ここで詰まることは無い。
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
        // FINAL 初挑戦のミナ封じ（2026-09-17）の最後の砦。Detail 側で止めているので手操作ではここへ来ないが、
        //   詳細を通らない自動ダイブ（--demo/--qa の DiveAuto）が残るため、ここで結び手のまま潜らせない。
        //   ゲート自体は GameManager.IsMinaLockedForFinal（--job= 固定中は素通し＝QA の逃し口は従来どおり）。
        if (scene == FinalScene && _game != null && _game.IsMinaLockedForFinal && _game.SelectedJob == Job.Tank)
        {
            // 解禁済みのうち一番手前＝あかり（Jobs.All の並び順）へ寄せる。三人とも未解禁なら
            //   そもそも AllStoryCleared が立たず FINAL カードが出ないので、ここは必ず誰かに当たる。
            foreach (var job in Jobs.All)
                if (job.Id != Job.Tank && _game.IsJobUnlocked(job.Id)) { _game.SelectedJob = job.Id; break; }
            GD.Print($"[JOB] FINAL は初挑戦のため結び手を回避 -> {_game.JobDef.CharacterName}");
        }
        _dived = true;
        // 他ジョブ潜行の章カウンタ（2026-09-15）：ダイブ確定のここで1回だけ数える（Stage 側の _Ready で
        //   数えると R リトライの ReloadCurrentScene でも増えて章が飛ぶ）。対象は本編3面×結び手以外のみ
        //   （FINAL は StageIdForScene が null＝数えない。FINAL は常にミナ本編のため章も持たない）。
        if (_game != null && _game.SelectedJob != Job.Tank && GameManager.StageIdForScene(scene) != null)
            _game.RegisterCharacterDive(_game.SelectedJob);
        GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        GetTree().ChangeSceneToFile(scene);
    }

    // ───────── 描画 ─────────
    public override void _Draw()
    {
        UiKit.BeginDesign(this);
        DrawRect(new Rect2(0, 0, W, H), new Color(0.025f, 0.03f, 0.04f, _hasNightBg ? 0.48f : 1f));
        bool home = _mode == Mode.Home || _mode == Mode.HomeReveal || _mode == Mode.SnsOpening
            || (_mode == Mode.Dialogue && _dlgReturnMode == Mode.Home && !_homeRevealPending);
        bool focusedOverlay = _mode == Mode.Dialogue || _mode == Mode.Detail || _mode == Mode.Job;
        DrawSidePanels(focusedOverlay ? 0.34f : 1f);
        DrawRect(new Rect2(PhoneX - 1f, 0, PhoneW + 2f, H), new Color("353b43"));
        DrawRect(new Rect2(PhoneX, 0, PhoneW, H), PhoneBg);
        if (home) DrawHome();
        else if (_mode == Mode.Photos) DrawPhotos();
        else DrawTimeline(_mode == Mode.Cards || _dlgSeenKey == SnsIntroSeenKey ? 1f : 0.22f);
        if (_mode == Mode.HomeReveal) DrawHomeReveal();
        else if (_mode == Mode.SnsOpening) DrawSnsOpening();
        else if (_mode == Mode.Dialogue) DrawDialog();
        else if (_mode == Mode.Detail) DrawDetail();
        else if (_mode == Mode.Job) DrawJob();
        else if (_mode == Mode.Cards) DrawFooter();
        DrawToast();
        DrawContaminationOverlay();
        UiKit.EndDesign(this);
    }

    private void DrawSidePanels(float alpha)
    {
        if (alpha <= 0.01f) return;
        DrawCompanionSidePanel(alpha);
        DrawSignalSidePanel(alpha);
    }

    private string SideStoryId()
    {
        bool home = _mode is Mode.Home or Mode.HomeReveal or Mode.SnsOpening or Mode.Photos
            || (_mode == Mode.Dialogue && _dlgReturnMode == Mode.Home);
        if (!home && IsVoice(_sel)) return _entries[_sel].Id;
        foreach (var entry in _entries)
            if (entry.Sort == Kind.Voice && entry.Unlocked && !IsClearedForDisplay(entry.Id)) return entry.Id;
        for (int i = _entries.Length - 1; i >= 0; i--)
            if (IsVoice(i)) return _entries[i].Id;
        return GameManager.FirstStageId;
    }

    // サイドパネル立ち絵の「顔」（画像ピクセル）：eyes＝両目の中点、face＝目線から顎までの高さ。
    //   4枚とも 1024x1536 だが構図が違う（ミナ＝専用の全身絵 hub_mina_v1、他3人＝オープニングのカットイン
    //   op_*_cutin_v1 を流用したバストアップ）。高さだけで合わせると顔の大きさが2倍近く違って見える
    //   （2026-09-23 ユーザー報告「ミナに対してあかりがでかい」）ので、ここから顔の高さ比を出して縮め、
    //   両目の中点を同じ点に置く＝顔の大きさと肩の高さがミナ基準で揃う。新しい絵は作らない。
    private static (Vector2 eyes, float face) SidePortraitFace(string id) => id switch
    {
        "akari" => (new(535, 365), 200f), "koharu" => (new(662, 357), 148f),
        "rei" => (new(555, 357), 163f), _ => (new(615, 240), 97f),
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
            DrawTextureRectRegion(texture, solid, source, new Color(1, 1, 1, alpha));
        }
        if (solidEnd >= visible.End.Y) return;
        float top = Mathf.Max(solidEnd, visible.Position.Y);
        Vector2[] pts = { new(visible.Position.X, top), new(visible.End.X, top), visible.End, new(visible.Position.X, visible.End.Y) };
        var uvs = new Vector2[4];
        for (int i = 0; i < 4; i++) uvs[i] = (pts[i] - rect.Position) / rect.Size;
        float a0 = alpha * Mathf.Clamp((rect.End.Y - top) / fade, 0f, 1f);
        float a1 = alpha * Mathf.Clamp((rect.End.Y - visible.End.Y) / fade, 0f, 1f);
        DrawPolygon(pts, new[] { new Color(1, 1, 1, a0), new Color(1, 1, 1, a0), new Color(1, 1, 1, a1), new Color(1, 1, 1, a1) },
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
        DrawRect(area, new Color(0.015f, 0.025f, 0.035f, alpha * 0.18f));
    }

    // focus＝両目の中点を置く画面座標。height＝ミナ（全身絵）を描く高さで、他の絵は顔の高さ比で縮める。
    private void DrawSidePortrait(string id, Rect2 area, Vector2 focus, float height, float alpha)
    {
        Texture2D texture = _sidePortraits[id];
        var (eyes, face) = SidePortraitFace(id);
        float scale = height / texture.GetHeight() * (SidePortraitFace("mina").face / face);
        DrawSideTexture(texture, new Rect2(focus - eyes * scale, texture.GetSize() * scale), area, alpha, fade: 120f);
    }

    private void DrawSideShade(Rect2 area, float top, float alpha, float end = 0.75f)
    {
        UiKit.VGradient(this, new Rect2(area.Position.X, top, area.Size.X, H - top),
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
        DrawSidePortrait(id, CompanionArea, new Vector2(239 - (1 - arrive) * 22, 263 + breath),
            780, alpha * arrive);
        DrawSideShade(CompanionArea, 430, alpha);
        UiKit.VGradient(this, new Rect2(0, 0, CompanionArea.Size.X, 195),
            new[] { new Color(0.027f, 0.038f, 0.045f, alpha * 0.6f), new Color(0, 0, 0, 0) }, new[] { 0f, 1f });
        UiKit.Text(this, _sideTitleFont, new Vector2(40, 30), "Refrain", 72, new Color("fff4df", alpha));
        UiKit.Text(this, UiKit.Zen, new Vector2(46, 128), "同行中", 13, new Color("bee8dc", alpha));
        UiKit.Text(this, _sideNameFont, new Vector2(42, 548), job.CharacterName, 46, new Color("fff7ea", alpha));
        UiKit.Text(this, _sideTitleFont, new Vector2(46, 614), AccountHandle(job), 24, new Color("bddde4", alpha));
        int count = 0;
        foreach (var companion in Jobs.All)
        {
            if (!_game.IsJobUnlocked(companion.Id)) continue;
            UiKit.FaceAvatar(this, new Vector2(61 + count * 44, 680), 15, _playerFaces[companion.CharacterId],
                JobColor(companion.Id), false, 0, alpha * (companion.Id == job.Id ? 1 : 0.55f), _t);
            count++;
        }
        UiKit.Text(this, UiKit.Zen, new Vector2(44 + count * 44, 669), $"仲間 {count}", 13,
            new Color(UiKit.Text2, alpha));
    }

    private void DrawSignalSidePanel(float alpha)
    {
        float blend = Mathf.SmoothStep(0, 1, _sideStoryBlend);
        if (blend < 1) DrawStoryPreview(_sideStoryPrevious, alpha * (1 - blend));
        DrawStoryPreview(_sideStoryId, alpha * blend);
        const float x = 919;
        int cleared = ClearedStageCount();
        UiKit.Text(this, UiKit.Zen, new Vector2(x, 461), "届いた声", 14, new Color("c9d8d8", alpha));
        UiKit.Text(this, UiKit.Mono, new Vector2(1159, 457), $"{cleared:00} / {GameManager.Stages.Length:00}", 17,
            new Color("f4dc9f", alpha));
        for (int i = 0; i < GameManager.Stages.Length; i++)
        {
            var stage = GameManager.Stages[i];
            var entry = System.Array.Find(_entries, e => e.Id == stage.Id && e.Sort == Kind.Voice);
            bool saved = IsClearedForDisplay(stage.Id);
            bool known = entry.Unlocked || saved;
            float cx = x + 34 + i * 110;
            bool current = stage.Id == _sideStoryId;
            Color accent = saved ? UiKit.Ok : current ? new Color("f4dc9f") : UiKit.Text3;
            if (i < GameManager.Stages.Length - 1)
                DrawLine(new Vector2(cx + 31, 524), new Vector2(cx + 78, 524),
                    new Color(accent, alpha * (saved ? 0.8f : 0.24f)), 1.5f, true);
            UiKit.FaceAvatar(this, new Vector2(cx, 524), current ? 27 : 22, known ? _faces[stage.Id] : null,
                accent, !known, 0, alpha * (known ? 1 : 0.42f), _t);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(cx - 48, 558), known ? entry.Name : LockedName, 15,
                new Color(known ? UiKit.White : UiKit.Text3, alpha), HorizontalAlignment.Center, 96);
            UiKit.Text(this, UiKit.Zen, new Vector2(cx - 48, 584), saved ? "届いた" : known ? "未送信" : "未受信", 11,
                new Color(accent, alpha * 0.88f), HorizontalAlignment.Center, 96);
        }
        DrawSideMetric(new Vector2(x, 624), "フォロワー", UiKit.Abbrev(_game.Followers), new Color("eba4b9"), alpha);
        DrawSideMetric(new Vector2(x + 170, 624), "インプレ", UiKit.Abbrev(_game.Impression), new Color("e5c986"), alpha);
    }

    private void DrawStoryPreview(string id, float alpha)
    {
        bool final = id == "final";
        string character = final ? "mina" : id;
        var job = System.Array.Find(Jobs.All, j => j.CharacterId == character)!;
        DrawSideLandscape(character, StoryArea, alpha);
        float drift = Mathf.Sin((float)_t * 0.65f + 1.2f);
        // 基準高 1140＝顔（目線→顎）が約 72px。旧 650 でカットイン3人を流していたときの平均に合わせ、
        //   最後の声（ミナの全身絵）だけ小さく写っていたのを同じ大きさへ揃える。
        DrawSidePortrait(character, new Rect2(StoryArea.Position, new Vector2(StoryArea.Size.X, 458)),
            new Vector2(1137 + drift * 4, 222 + drift * 2), 1140, alpha);
        DrawSideShade(StoryArea, 300, alpha, 0.36f);
        UiKit.VGradient(this, new Rect2(StoryArea.Position.X, 0, StoryArea.Size.X, 135),
            new[] { new Color(0.027f, 0.038f, 0.045f, alpha * 0.65f), new Color(0, 0, 0, 0) }, new[] { 0f, 1f });
        string caption = final ? "最後の声" : IsClearedForDisplay(id) ? "届いた声" : "次の声";
        UiKit.Text(this, _sideNameFont, new Vector2(919, 38), caption, 23, new Color("fff4df", alpha));
        int index = System.Array.FindIndex(GameManager.Stages, stage => stage.Id == id);
        UiKit.Text(this, _sideTitleFont, new Vector2(919, 77), final ? "Final" : $"Story {index + 1:00}", 27,
            new Color("eacb8b", alpha));
        UiKit.Text(this, _sideNameFont, new Vector2(914, 348), job.CharacterName, 40,
            new Color("fff7ea", alpha));
        UiKit.Text(this, _sideTitleFont, new Vector2(919, 404), AccountHandle(job), 21,
            new Color("dae4e5", alpha));
    }

    private void DrawSideMetric(Vector2 pos, string label, string value, Color accent, float alpha)
    {
        UiKit.Text(this, UiKit.Zen, pos, label, 12, new Color(UiKit.Text3, 0.86f * alpha));
        UiKit.Text(this, UiKit.Mono, pos + new Vector2(0, 24), value, 22, new Color(accent, alpha));
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
        DrawRect(new Rect2(PhoneX, 0, PhoneW, FeedTop), PhoneBg);
        DrawRect(new Rect2(PhoneX, FeedBottom, PhoneW, H - FeedBottom), PhoneBg);
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
        DrawRect(new Rect2(0, 0, W, H), new Color(gray.Lerp(purple, colorT), alpha));
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
        UiKit.Text(this, UiKit.Zen, new Vector2(right - iw - 6f - ilw, 92f), ImpLabel, 12, new Color(UiKit.Gold, 0.75f));
        UiKit.Text(this, UiKit.Mono, new Vector2(right - iw, 89f), imp, 15, UiKit.Gold);
        float fw = UiKit.TextW(UiKit.Mono, fol, 15), flw = UiKit.TextW(UiKit.Zen, FolLabel, 12);
        float fx = right - iw - 6f - ilw - 18f - fw;
        UiKit.Text(this, UiKit.Zen, new Vector2(fx - 6f - flw, 92f), FolLabel, 12, new Color(UiKit.Hp, 0.75f));
        UiKit.Text(this, UiKit.Mono, new Vector2(fx, 89f), fol, 15, UiKit.Hp);
        DrawRect(new Rect2(PhoneX + 20f, 68f, PhoneW - 40f, 1f), new Color(1, 1, 1, 0.07f));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, 87f), "タイムライン", 20, UiKit.White);
        float contam = Mathf.Clamp(_game?.Contamination ?? 0f, 0f, 1f);
        DrawRect(new Rect2(PhoneX, FeedTop - 3f, PhoneW, 3f), new Color(UiKit.Kegare, 0.10f));
        if (contam > 0) DrawRect(new Rect2(PhoneX, FeedTop - 3f, PhoneW * contam, 3f), UiKit.Kegare);
    }

    private const float FeedTop = 128f, FeedBottom = 646f;
    private static float CardHeight(Entry e) => e.Sort == Kind.Voice && e.Unlocked ? 232f : 152f;

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
    private float FeedMaxScroll()
    {
        return Mathf.Max(0f, CardTop(_entries.Length) - (FeedBottom - FeedTop));
    }
    // 選択が画面外へ出ないところまでだけスクロールを動かす（上下に 1 枚ぶんの余白を残して先を見せる）。
    private void UpdateFeedScrollTarget()
    {
        float viewH = FeedBottom - FeedTop;
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
            bool sel = (_mode == Mode.Cards || _dlgSeenKey == SnsIntroSeenKey) && i == _sel && _footSel < 0;
            float ep = Mathf.Clamp(((float)(_t - _cardsEnteredT) - i * 0.04f) / 0.20f, 0f, 1f);
            DrawCard(_entries[i], cy, h, sel, sel ? _selT : 0f, alpha * ep);
        }
        DrawScrollHint(FeedTop, alpha);
    }

    // 右端のスクロールバー風ヒント（3px）。2-b では実スクロール量に対する現在地を出す＝
    //   「TL はこの先も続いている」ことを言う（つまみの短さが feed の長さ）。
    private void DrawScrollHint(float top, float alpha)
    {
        float max = FeedMaxScroll();
        if (max <= 0f) return;
        float viewH = FeedBottom - top;
        float th = Mathf.Max(28f, viewH * viewH / (viewH + max));
        float ty = top + (viewH - th) * Mathf.Clamp(_feedScroll / max, 0f, 1f);
        UiKit.Box(this, new Rect2(PhoneX + PhoneW - 4f, ty, 2f, th), new Color(UiKit.Text3, 0.45f * alpha), 1f);
    }

    private void DrawCard(Entry e, float cy, float h, bool sel, float st, float alpha)
    {
        float x = PhoneX, w = PhoneW;
        bool voice = e.Sort == Kind.Voice, filler = e.Sort == Kind.Filler;
        Color acc = BarColorFor(e);
        if (sel) DrawRect(new Rect2(x, cy, w, h), new Color(acc, (0.045f + 0.025f * st) * alpha));
        DrawRect(new Rect2(x + 20f, cy + h - 1f, w - 40f, 1f), new Color(1, 1, 1, 0.09f * alpha));
        if (sel && voice && e.Unlocked)
            DrawRect(new Rect2(x, cy + 16f, 3f, h - 32f), new Color(UiKit.Purify, alpha));
        float ax = x + 43f, ay = cy + 37f;
        if (filler) DrawFillerAvatar(ax, ay, e.Icon, alpha);
        else UiKit.FaceAvatar(this, new Vector2(ax, ay), 23f, e.Unlocked ? FaceFor(e.Id) : null, acc, false,
            0f, alpha, _t);
        float tx = x + 80f;
        string name = FitText(filler ? UiKit.Zen : UiKit.ZenBold, e.Name, 17, w - 186f);
        UiKit.Text(this, filler ? UiKit.Zen : UiKit.ZenBold, new Vector2(tx, cy + 17f), name, 17,
            new Color(UiKit.White, e.Unlocked ? alpha : alpha * 0.5f));
        if (e.Unlocked && !filler)
            UiKit.VerifiedBadge(this, new Vector2(tx + UiKit.TextW(UiKit.ZenBold, name, 17) + 13f, cy + 28f),
                6f, e.Cleared ? UiKit.Ok : UiKit.Purify, alpha);
        UiKit.Text(this, UiKit.Mono, new Vector2(tx, cy + 43f), FitText(UiKit.Mono, e.Handle, 12, w - 108f),
            12, new Color(UiKit.Text3, alpha));
        if (e.Sort == Kind.Pinned) DrawPinnedMark(x + w - 22f, cy + 19f, alpha);
        else if (e.IsFinal || e.Cleared)
            DrawBadgePill(e, e.IsFinal ? "限界" : "届いた", x + w - 22f, cy + 19f, alpha);
        else if (e.Unlocked)
            UiKit.Text(this, UiKit.Zen, new Vector2(x + w - 82f, cy + 20f), e.RelT, 12,
                new Color(UiKit.Text4, alpha), HorizontalAlignment.Right, 60f);

        UiKit.Multi(this, UiKit.Zen, new Vector2(x + 24f, cy + 75f), e.Tweet, 17,
            new Color(UiKit.Text2, e.Unlocked ? alpha : alpha * 0.45f), w - 48f, 2);
        bool hoverHere = sel && e.Unlocked && HoverLineFor(e).Length > 0;
        if (voice && e.Unlocked)
        {
            if (e.Cleared && !e.IsFinal) DrawMinaReply(e.Id, x, cy, w, h, alpha);
            else if (hoverHere) DrawHoverLine(e, x, cy, w, h, alpha);
            else RedactedBars(x + 24f, cy + 136f, w - 48f, alpha);
            if (!e.IsFinal && !hoverHere) DrawCardBest(e.Id, x + w - 24f, cy + 179f, alpha);
        }
        else if (e.Redacted) RedactedBars(x + 24f, cy + 103f, w - 48f, alpha);

        if (e.Unlocked)
        {
            float ey = cy + h - 25f, ex = x + 26f;
            if (e.IsFinal)
            {
                // ★2026-09-17：FINAL だけは他カードと違い「いいね/ビュー」ではなくミナ自身の
                //   フォロワー数・インプレッションの生値を出している。♥と棒グラフのままでは
                //   投稿のエンゲージ数と読み違える（ユーザー指摘）ので、語ラベル付きに変える。
                ex = MetricLabeled(ex, ey, "フォロワー", _game?.Followers ?? 0, new Color(UiKit.Hp, alpha));
                MetricLabeled(ex, ey, "インプレ", _game?.Impression ?? 0, new Color(UiKit.Text3, alpha));
            }
            else
            {
                ex = Metric(ex, ey, 0, e.Replies, new Color(UiKit.Text3, alpha));
                ex = Metric(ex, ey, 1, e.Reposts, new Color(UiKit.Ok, alpha));
                ex = Metric(ex, ey, 2, e.Likes, new Color(UiKit.Hp, alpha));
                if (!filler) Metric(ex, ey, 3, ViewsFor(e), new Color(UiKit.Text3, alpha));
            }
        }
    }

    // カードの左バー／アバターのリング色。埋め草は色を持たない他人＝くすんだ灰。
    private static Color BarColorFor(Entry e) => e.Sort switch
    {
        Kind.Filler => UiKit.Text4,
        Kind.Pinned => UiKit.Mina,
        _ => e.IsFinal ? UiKit.Kegare : AccountColor(e.Id),
    };

    // 埋め草のアバター。2026-09-07 まで X の初期アイコン（無地の円＋灰色の人型シルエット）を全員に
    //   出していたが、実際の TL でそれが並ぶことはない＝「作りかけ」に見えていた。SnsVoices の表が
    //   名前と対で持つアイコン番号（猫・犬・観葉植物・コーヒー・空・海・食べ物・幾何模様・本・カメラ・
    //   自転車・月の12種）を丸窓に出す。人の顔は出さない＝声のあるカードとの見分けは保つ。
    //   アイコンは正方形なので topCrop=0 で全面をサンプルする（縦長立ち絵の頭部窓とは別扱い）。
    //   リングは無彩色に近い薄灰のまま＝アカウント色を持つ三人と混ざらない。画像が無い場合だけ旧シルエット。
    private void DrawFillerAvatar(float cx, float cy, int icon, float alpha)
    {
        var tex = MobIcon(icon);
        if (tex != null)
        {
            UiKit.FaceAvatar(this, new Vector2(cx, cy), 24f, tex, new Color(UiKit.Text4, 0.55f), false, 0f, alpha * 0.92f, _t);
            return;
        }
        DrawCircle(new Vector2(cx, cy), 24f, new Color(0.16f, 0.15f, 0.21f, 0.9f * alpha));
        DrawArc(new Vector2(cx, cy), 24f, 0f, Mathf.Tau, 28, new Color(1, 1, 1, 0.08f * alpha), 1f);
        var sil = new Color(UiKit.Text4, 0.5f * alpha);
        DrawCircle(new Vector2(cx, cy - 5f), 7f, sil);                       // 頭
        DrawColoredPolygon(new[] { new Vector2(cx - 11f, cy + 13f), new Vector2(cx + 11f, cy + 13f),
                                   new Vector2(cx + 8f, cy + 4f), new Vector2(cx - 8f, cy + 4f) }, sil);  // 肩
    }
    // 固定ポストの印（X の pinned post）。ピルではなく、小さなピンと「固定」の一語だけ置く。
    private void DrawPinnedMark(float right, float y, float alpha)
    {
        const string s = "固定";
        float tw = UiKit.TextW(UiKit.Zen, s, UiKit.FontSmall);
        float x = right - tw;
        UiKit.Text(this, UiKit.Zen, new Vector2(x, y + 2f), s, UiKit.FontSmall, new Color(UiKit.Text3, 0.9f * alpha));
        // ピン＝頭の丸と細い軸（ラベルの左に控えめに）。
        DrawCircle(new Vector2(x - 12f, y + 6f), 3f, new Color(UiKit.Mina, 0.8f * alpha));
        DrawLine(new Vector2(x - 12f, y + 8f), new Vector2(x - 12f, y + 15f), new Color(UiKit.Mina, 0.6f * alpha), 1.4f);
    }

    // 声のあるカードにカーソルが乗ったときの、カード下部のミナの一行（2-b）。
    //   文言は既存の行の転用のみ（新しい台詞は書かない）。あかりの初回だけ H0Dialog の2行目を使い、
    //   一度きりの H0 会話（旧 Hub.cs の入場ダイアログ）はここへ吸収した。
    private void DrawHoverLine(Entry e, float x, float cy, float w, float h, float alpha)
    {
        string line = HoverLineFor(e);
        if (line.Length == 0) return;
        UiKit.FaceAvatar(this, new Vector2(x + 35f, cy + 158f), 10f, _playerFaces["mina"], UiKit.Mina, false, 0f, alpha, _t);
        UiKit.Multi(this, UiKit.Zen, new Vector2(x + 56f, cy + 146f), line, 14,
            new Color(UiKit.Mina, alpha), w - 80f, 2);
    }

    // ホバー行の文言。既存の台詞からの転用に限る（新規の台詞は書かない）。
    private string HoverLineFor(Entry e)
    {
        if (e.Sort == Kind.Filler) return MobLineFor(e);      // 埋め草＝声の聞こえない側の一言
        if (e.IsFinal) return "……ご主人様。次のカードは——わたくしの、内側です。";   // H3 帰還の行
        if (e.Cleared) return "";                                                     // 届いた投稿には、もう言うことがない
        // あかりの初回＝H0（仮台本 06）の2行目をそのまま置く。旧実装の入場ダイアログの代わり。
        if (e.Id == "akari") return "……この投稿の下からも、聞こえます。";
        // こはる・レイは帰還小話の「次の声も、もう、聞こえています。」（H1 帰還の最終行）を引く。
        return "次の声も、もう、聞こえています。";
    }

    // ───────── 埋め草（モブの投稿）に添えるミナの一言 ─────────
    //   2026-09-07 ユーザー要望「他の投稿を選ぶとミナのひとことあるとうれしい」。
    //
    //   【この行が壊してはいけないもの】
    //   潜れる投稿とそうでない投稿の見分け。声のある投稿の行（上の HoverLineFor）は「聞こえます」＝
    //   潜れる合図で、こちらは “聞こえない側” の一言＝観測だけを言う。
    //   ここに「聞こえます」系の語を絶対に書かないこと（書くと探す遊びが消える）。
    //
    //   【文言は未確定】以下は差し込み口を通すための暫定で、正式な文言は scenario 担当が書く。
    //   必要数＝SnsVoices.Count（20）。添字は名前・アイコンと同じ VoiceIndex なので、
    //   同じアカウントには毎回同じ一言が付く（名前と一言が噛み合う形で書ける）。
    private static readonly string[] MobLines =
    {
        // TODO(scenario): 20行。SnsVoices.All と同じ並び＝各行はその人の名前に噛み合わせて書く。
        //   観測だけを言う（例：「……この方は、下書きがありません」「……送ってから、消していませんね」）。
        //   「聞こえます」「声」は使わない＝潜れる投稿との見分けを保つ。
        "", "", "", "", "", "", "", "", "", "",
        "", "", "", "", "", "", "", "", "", "",
    };

    // Entry（埋め草）→ その人の一言。未記入（空文字）なら行を出さない＝いまは従来どおり無言になる。
    //   Icon から人を引けないので（12種を20人で共有）、名前と同じ FillerVoice 添字を Entry.Id に
    //   埋めてある通し番号から引き直す。Id は "filler{i}" 形式（BuildFiller が振る）。
    private static string MobLineFor(Entry e)
    {
        if (!e.Id.StartsWith("filler")) return "";
        if (!int.TryParse(e.Id.Substring(6), out int i)) return "";
        int v = FillerVoice(i);
        return v >= 0 && v < MobLines.Length ? MobLines[v] : "";
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
        UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x0, y + 1), "BEST", new Color(UiKit.Text3, alpha));
        UiKit.Text(this, UiKit.Mono, new Vector2(x0 + lw, y), timeStr, UiKit.FontLabel, new Color(tc, alpha));
        if (diffStr.Length > 0)
            UiKit.Draw(this, UiKit.SmallLabel, new Vector2(x0 + lw + tw + 8, y + 1), diffStr, new Color(UiKit.Info, alpha));
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
        if (e.IsFinal)
        {
            float pulse = 0.72f + 0.20f * Mathf.Sin((float)_t * 2.6f);
            UiKit.Box(this, r, new Color(UiKit.Kegare, pulse * alpha), 10f);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(r.Position.X, y + 2f), badge, UiKit.FontSmall, new Color(UiKit.BgDeep, alpha), HorizontalAlignment.Center, bw);
        }
        else if (e.Cleared)
        {
            UiKit.Box(this, r, new Color(UiKit.Ok, 0.10f * alpha), 10f, new Color(UiKit.Ok, 0.55f * alpha), 1f);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(r.Position.X, y + 2f), badge, UiKit.FontSmall, new Color(UiKit.Ok, alpha), HorizontalAlignment.Center, bw);
        }
        else
        {
            // 「声」＝これから潜る投稿。塗りピルの淡い明滅で「ここへ」を誘導（常時アニメはこれと選択グロウのみ）。
            float pulse = 0.74f + 0.18f * Mathf.Sin((float)_t * 3.0f);
            UiKit.Box(this, r, new Color(UiKit.Purify, pulse * alpha), 10f);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(r.Position.X, y + 2f), badge, UiKit.FontSmall, new Color(UiKit.BgDeep, alpha), HorizontalAlignment.Center, bw);
        }
    }

    // クリア済カードの下段：ミナの自動投稿をスレッド返信風に 1 行で。親アバターから細い会話線で繋ぐ。
    //   帰還小話で見た「ミナの投稿」が、そのままタイムラインに残っている——という画。
    private void DrawMinaReply(string id, float x, float cy, float w, float h, float alpha)
    {
        UiKit.FaceAvatar(this, new Vector2(x + 35f, cy + 148f), 10f, _playerFaces["mina"], UiKit.Mina, false, 0f, alpha, _t);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 56f, cy + 132f), "ミナ", 13, new Color(UiKit.Mina, alpha));
        UiKit.Text(this, UiKit.Zen, new Vector2(x + 56f, cy + 152f), MinaPostShort(id, w - 80f), 14, new Color(UiKit.Text2, alpha));
    }

    // ミナの自動投稿の 1 行短縮（幅に収まるよう末尾を「…」で省略。レイアウト幅は固定なので id キャッシュで足りる）。
    private string MinaPostShort(string id, float maxW)
    {
        if (_minaPosts.TryGetValue(id, out var cached)) return cached;
        var d = ReturnDialog(id);
        string s = d.Length > 0 ? d[0].Item2 : "";
        if (UiKit.TextW(UiKit.Zen, s, UiKit.FontLabel) > maxW)
        {
            while (s.Length > 1 && UiKit.TextW(UiKit.Zen, s + "…", UiKit.FontLabel) > maxW) s = s.Substring(0, s.Length - 1);
            s += "…";
        }
        _minaPosts[id] = s;
        return s;
    }

    // 伏字バー（2-a で用途を反転）。声のあるカードの本文の下に薄く明滅させ、
    //   「この投稿には、消された一行がある」だけを言う。読ませない・説明しない。
    //   明滅は周期 2.6s のごく浅い呼吸（0.045〜0.085α）。カード全体のノイズにならない量に抑える。
    private void RedactedBars(float x, float y, float w, float alpha)
    {
        float pulse = 0.045f + 0.040f * (0.5f + 0.5f * Mathf.Sin((float)_t * Mathf.Tau / 2.6f));
        var c = new Color(1, 1, 1, pulse * alpha);
        DrawRect(new Rect2(x, y, w * 0.52f, 7f), c);
        DrawRect(new Rect2(x, y + 13f, w * 0.31f, 7f), c);
    }

    // クリア順に基づくゆるい相対時刻（X風メタ表示・実害なし）。
    private static string RelTime(string id) => id switch
    {
        "rei" => "3h", "akari" => "5h", "koharu" => "1d", _ => "now",
    };

    // ビュー（表示回数）= だいたい likes×8 の概算。
    private static long ViewsFor(Entry e) => e.Likes > 0 ? e.Likes * 8 + e.Reposts * 30 : 0;

    // 小さなエンゲージメント指標（0=返信 1=リポスト 2=いいね）。次の x を返す。
    private float Metric(float x, float y, int kind, long count, Color col)
    {
        var c = new Vector2(x + 8, y + 8);
        switch (kind)
        {
            case 0: UiKit.Box(this, new Rect2(c.X - 7, c.Y - 6, 14, 10), null, 3f, col, 1.4f); break;
            case 1:
                DrawLine(new Vector2(c.X - 6, c.Y - 3), new Vector2(c.X + 5, c.Y - 3), col, 1.4f);
                DrawLine(new Vector2(c.X + 6, c.Y + 3), new Vector2(c.X - 5, c.Y + 3), col, 1.4f);
                break;
            case 3: // ビュー指標（小さな棒グラフ）
                DrawRect(new Rect2(c.X - 6, c.Y + 1, 2.4f, 4f), col);
                DrawRect(new Rect2(c.X - 2, c.Y - 2, 2.4f, 7f), col);
                DrawRect(new Rect2(c.X + 2, c.Y - 5, 2.4f, 10f), col);
                break;
            default: DrawHeart(c, 6f, col); break;
        }
        string s = UiKit.Abbrev(count);
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 20, y), s, UiKit.FontLabel, col);
        return x + 20 + UiKit.TextW(UiKit.Mono, s, UiKit.FontLabel) + 26;
    }

    // 記号の代わりに語で意味を示す指標（FINAL カードのフォロワー／インプレ）。次の x を返す。
    //   Metric と同じ行・同じフォントサイズに揃え、ラベルだけ一段小さく前に置く。
    private float MetricLabeled(float x, float y, string label, long count, Color col)
    {
        float lw = UiKit.TextW(UiKit.Zen, label, UiKit.FontSmall);
        UiKit.Text(this, UiKit.Zen, new Vector2(x, y + 2f), label, UiKit.FontSmall, new Color(col, col.A * 0.8f));
        string s = UiKit.Abbrev(count);
        UiKit.Text(this, UiKit.Mono, new Vector2(x + lw + 6f, y), s, UiKit.FontLabel, col);
        return x + lw + 6f + UiKit.TextW(UiKit.Mono, s, UiKit.FontLabel) + 26;
    }

    private void DrawHeart(Vector2 c, float r, Color col)
    {
        DrawCircle(new Vector2(c.X - r * 0.45f, c.Y - r * 0.25f), r * 0.55f, col);
        DrawCircle(new Vector2(c.X + r * 0.45f, c.Y - r * 0.25f), r * 0.55f, col);
        DrawColoredPolygon(new[] { new Vector2(c.X - r * 0.9f, c.Y), new Vector2(c.X + r * 0.9f, c.Y), new Vector2(c.X, c.Y + r) }, col);
    }

    // ── フッタ操作（クリック可能なショップ入口ほか）──
    //   フェーズ2のカードクリックに続き、フッタの操作ヒントもマウスで押せるようにする。
    //   最優先は「強化」＝ショップ入口ボタン。「返信/記録」も揃えて（余力）クリック可に。
    //   ・「えらぶ」はナビ表示のみ＝クリック対象外。「ダイブ」はカードクリックで足りるので表示のみ。
    //   ・レイアウトは DrawFooter と単一ソース化（FooterItems を DrawFooter とホットスポット登録で共用）。
    //     フッタ id は カード id(0..entries) と衝突しないよう FooterIdBase から採番する。
    private enum FootAct { Reply, Job, Home }
    private const int FooterIdBase = 10000;

    // 現在のフッタ項目（表示順）。key/label/accent＝見た目、act＝クリック時のアクション（None=表示のみ）。
    private System.Collections.Generic.List<(string key, string label, bool accent, FootAct act)> FooterItems()
    {
        var list = new System.Collections.Generic.List<(string, string, bool, FootAct)>
        {
            (Pad.CancelToken, "ホームに戻る", false, FootAct.Home),
        };
        if (CanReplySel()) list.Add((Pad.EquipToken, "返信", false, FootAct.Reply));
        // ジョブは解禁ゲート無し＝初回訪問から出す（設計書 §6：ショップは1面ボスまで開かないので、
        //   ハブに置かないと最初のダイブ前に一度も選べない）。強化・記録より前に置く＝潜る前に決める順。
        list.Add((JobKeyToken, "アカウント", false, FootAct.Job));
        return list;
    }

    private Rect2 FooterItemRect(int i)
    {
        float width = (PhoneW - 24f) / FooterItems().Count;
        return new Rect2(PhoneX + 12f + i * width, FeedBottom + 7f, width, 60f);
    }

    private void DrawFooter()
    {
        DrawRect(new Rect2(PhoneX, FeedBottom, PhoneW, 1f), new Color(1, 1, 1, 0.12f));
        var items = FooterItems();
        for (int i = 0; i < items.Count; i++)
        {
            var (key, label, accent, act) = items[i];
            var rect = FooterItemRect(i);
            // マウスのホバーと、キーボード／パッドのカーソル（_footSel）を同じ見え方にする。
            bool hovered = UiKit.HoveredId() == FooterIdBase + i || (_mode == Mode.Cards && _footSel == i);
            bool nudge = act == FootAct.Job && AccountNudge;
            Color col = accent || nudge ? UiKit.Purify : hovered ? UiKit.White : UiKit.Text3;
            // キーボード／パッドのカーソルはカードの選択枠と同じ浄化色の縁で見せる（マウスのホバーより強く＝
            //   「いまカーソルがここにある」がカードから降りた瞬間に分かる）。
            if (_mode == Mode.Cards && _footSel == i)
                UiKit.Box(this, rect, new Color(UiKit.Purify, 0.10f), 8f, new Color(UiKit.Purify, 0.6f), 1f);
            else if (hovered) UiKit.Box(this, rect, new Color(1, 1, 1, 0.05f), 8f);
            Vector2 c = rect.Position + new Vector2(rect.Size.X / 2f, 18f);
            // アカウント追加の説明を読んだ直後だけの誘導（2026-09-23）：フッタ「アカウント」の顔アイコンから
            //   外へ広がりながら薄くなる輪を2枚ずらして重ねる＝ホームの強化アイコン（_shopNudge）と同じ作法。
            //   台詞は足していない（説明の2行目「SNSの下、「アカウント」から」が場所をもう言っている）。
            if (nudge)
            {
                for (int ring = 0; ring < 2; ring++)
                {
                    float phase = Mathf.PosMod((float)_t * 0.8f + ring * 0.5f, 1f);
                    DrawArc(c, 17f + phase * 14f, 0f, Mathf.Tau, 48, new Color(UiKit.Purify, (1f - phase) * 0.55f), 2f, true);
                }
            }
            DrawFooterIcon(act, c, col);
            // ラベルの右にキー表記（J／RB 等）を小さく添える＝キーボード・パッドだけでも押し方が分かる
            //   （2026-09-27：それまでは key を捨てていて、J で開けることが画面のどこにも出ていなかった）。
            float labelW = UiKit.TextW(UiKit.Zen, label, 12), keyW = UiKit.TextW(UiKit.Mono, key, 10);
            float lx = rect.Position.X + (rect.Size.X - labelW - 5f - keyW) / 2f;
            UiKit.Text(this, UiKit.Zen, new Vector2(lx, rect.Position.Y + 38f), label, 12, col);
            UiKit.Text(this, UiKit.Mono, new Vector2(lx + labelW + 5f, rect.Position.Y + 40f), key, 10,
                new Color(hovered ? UiKit.Text2 : UiKit.Text4, 1f));
        }
    }


    private void DrawToast()
    {
        if (_toastT <= 0) return;
        const float w = PhoneW - 32f;
        var lines = UiKit.WrapLines(UiKit.ZenBold, _toast, 15, w - 32f);
        var sub = UiKit.WrapLines(UiKit.Zen, _toastSub, 12, w - 32f);
        float h = 24f + lines.Count * UiKit.ZenBold.GetHeight(15) + (_toastSub.Length > 0 ? sub.Count * UiKit.Zen.GetHeight(12) + 6f : 0f);
        float x = PhoneX + 16f, y = _mode == Mode.Dialogue ? FeedTop + 12f : FeedBottom - h - 8f;
        UiKit.Box(this, new Rect2(x, y, w, h), PhoneRaised, 8f, new Color(_toastCol, 0.6f), 1f);
        UiKit.Multi(this, UiKit.ZenBold, new Vector2(x + 16f, y + 12f), _toast, 15, _toastCol, w - 32f);
        if (_toastSub.Length > 0)
            UiKit.Multi(this, UiKit.Zen, new Vector2(x + 16f, y + 18f + lines.Count * UiKit.ZenBold.GetHeight(15)), _toastSub, 12, UiKit.Text3, w - 32f);
    }

    // 小話の話者文字列 → (立ち絵, 円窓のリング色, topCrop)。本編会話と同じ FaceAvatar で顔を出す。
    //   ミナ系(ミナ・ミナの投稿・ミナ→@xxx)=mina_face / 相手キャラ=各 _face。
    //   top の負値は「顔を描かない」印：-1＝アバターごと省略 / DraftTop＝下書きの吹き出し印を置く。
    private const float DraftTop = -2f;
    private (Texture2D? face, Color col, float top) SpeakerFace(string sp)
    {
        // 「Ｘ 投稿」「Ｘ システム」＝顔の無い枠（他人の引用・システム表示）。top に負値を返して
        //   DrawDialog にアバターごと省かせる（null のままだと FaceAvatar が「?」のロック円を描き、
        //   未解放カードと同じ見た目になってしまう）。H0 の投稿、H2 の炎上の引用、H3 の FINAL カードがここ。
        if (sp.StartsWith("Ｘ")) return (null, UiKit.Info, -1f);
        // 「あなた」＝顔を持たない読み手。本編会話（Hud.LineKind.Boy）と同じく、立ち絵の代わりに下書きの吹き出し印。
        if (sp == "あなた") return (null, UiKit.Info, DraftTop);
        if (sp.StartsWith("ミナ")) return (_minaFace, UiKit.Mina, TopCropFor("mina"));
        foreach (var job in Jobs.All)
            if (sp == job.CharacterName) return (_dialogueFaces[job.CharacterId], CompanionDialogue.Accent(job.Id), TopCropFor(job.CharacterId));
        string plain = Handles.Plain(sp);   // 化けた綴り（@rëi_____ 等）を戻してから部分文字列を見る
        if (plain.Contains("rei")) return (_dialogueFaces["rei"], AccountColor("rei"), TopCropFor("rei"));
        if (plain.Contains("akari")) return (_dialogueFaces["akari"], AccountColor("akari"), TopCropFor("akari"));
        if (plain.Contains("koharu")) return (_dialogueFaces["koharu"], AccountColor("koharu"), TopCropFor("koharu"));
        return (null, AccountColor("rei"), 0.06f);
    }

    // ───────── 2-b: 投稿詳細（開いたカード）─────────
    // 本文／消された行の伏字／ミナの一言／難易度4段を1枚に置く。段を押した時点でそのままダイブする
    //   （2026-09-17 ユーザー指示で「潜る」ボタンと「潜り方」の見出しを廃止＝押す場所は段だけ）。
    //   FINAL は段を出さず、従来の FINAL の見出し（穢れ色・「限界」）の扱いを踏襲する。
    private void DrawDetail()
    {
        var e = _entries[Mathf.Clamp(_sel, 0, _entries.Length - 1)];
        bool tiers = !e.IsFinal;
        float a = Mathf.Clamp((float)_detailT / 0.18f, 0f, 1f);
        var (cx, cy, cw, ch) = DetailBox(tiers);
        Color acc = e.IsFinal ? UiKit.Kegare : AccountColor(e.Id);
        DrawRect(new Rect2(PhoneX, 0, PhoneW, H), new Color(0, 0, 0, 0.5f * a));
        UiKit.Box(this, new Rect2(cx, cy, cw, ch), new Color(PhoneBg, a), 8f);
        DrawBackButton(DetailCloseRect(tiers), DetailCloseId, a);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(cx + 60f, cy + 21f), "投稿", 20, new Color(UiKit.White, a));
        DrawJobButton(DetailJobRect(tiers), a);
        UiKit.FaceAvatar(this, new Vector2(cx + 47f, cy + 91f), 23f, FaceFor(e.Id), acc, false, 0f, a, _t);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(cx + 84f, cy + 70f), e.Name, 19, new Color(UiKit.White, a));
        UiKit.VerifiedBadge(this, new Vector2(cx + 97f + UiKit.TextW(UiKit.ZenBold, e.Name, 19), cy + 82f), 6f, e.Cleared ? UiKit.Ok : UiKit.Purify, a);
        UiKit.Text(this, UiKit.Mono, new Vector2(cx + 84f, cy + 98f), e.Handle + " · " + e.RelT, 12, new Color(UiKit.Text3, a));
        if (e.IsFinal || e.Cleared) DrawBadgePill(e, e.IsFinal ? "限界" : "届いた", cx + cw - 24f, cy + 74f, a);
        UiKit.Multi(this, UiKit.Zen, new Vector2(cx + 24f, cy + 135f), e.Tweet, 18, new Color(UiKit.Text2, a), cw - 48f, 3);
        RedactedBars(cx + 24f, cy + 226f, cw - 48f, a);
        string quip = HoverLineFor(e);
        if (quip.Length == 0) quip = "……届きました。";
        UiKit.FaceAvatar(this, new Vector2(cx + 35f, cy + 274f), 10f, _playerFaces["mina"], UiKit.Mina, false, 0f, a, _t);
        UiKit.Multi(this, UiKit.Zen, new Vector2(cx + 56f, cy + 260f), quip, 14, new Color(UiKit.Mina, a), cw - 80f, 2);
        if (tiers)
        {
            // 2026-09-17 ユーザー指示：「潜り方」の見出しを削除。区切り線は本文と段の境目として残す
            //   （線まで消すと、投稿本文と難易度の一覧が地続きに見えて読み分けられない）。
            DrawRect(new Rect2(cx + 24f, cy + 305f, cw - 48f, 1f), new Color(1, 1, 1, 0.09f * a));
            for (int i = 0; i < Tiers.Length; i++) DrawTier(i, cx + 24f, cy + TierTop + i * 68f, cw - 48f, 62f, a);
        }
        // FINAL 初挑戦のミナ封じ（2026-09-17）。押す前に「なぜ潜れないか」を出す＝拒否されてから知る、を避ける。
        //   段の無い FINAL のカードは下が空いているので、ミナの一言の下に穢れ色で1枚だけ置く。
        else if (NeedsFinalAccount)
        {
            DrawRect(new Rect2(cx + 24f, cy + 320f, cw - 48f, 1f), new Color(1, 1, 1, 0.09f * a));
            UiKit.Box(this, new Rect2(cx + 24f, cy + 344f, cw - 48f, 76f), new Color(UiKit.Kegare, 0.10f * a), 8f,
                new Color(UiKit.Kegare, 0.5f * a), 1f);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(cx + 40f, cy + 356f), GameManager.MinaFinalLockHint, 15,
                new Color(UiKit.Kegare, a));
            UiKit.Text(this, UiKit.Zen, new Vector2(cx + 40f, cy + 384f),
                "あかり／こはる／レイ のどなたかで、潜ってください。", 13, new Color(UiKit.Text2, a));
        }
        if (!tiers)
            DrawPrimaryButton(FinalDiveRect(), NeedsFinalAccount ? "アカウントを選ぶ" : "ミナを迎えに行く",
                FinalDiveId, NeedsFinalAccount ? UiKit.Purify : acc, a);
    }

    private void DrawJobButton(Rect2 rect, float alpha = 1f)
    {
        var job = _game.JobDef;
        Color acc = JobColor(job.Id);
        bool header = rect.Size.Y > 40f;
        float tx = header ? 62f : 44f;
        int nameSize = header ? 19 : 14;
        if (UiKit.HoveredId() == JobOpenId) UiKit.Box(this, rect, new Color(1, 1, 1, 0.06f * alpha), 8f);
        UiKit.FaceAvatar(this, rect.Position + new Vector2(header ? 28f : 20f, rect.Size.Y / 2f), header ? 20f : 15f,
            _playerFaces[job.CharacterId], acc, false, 0f, alpha, _t);
        UiKit.Text(this, UiKit.ZenBold, rect.Position + new Vector2(tx, header ? 6f : 2f), job.CharacterName, nameSize, new Color(UiKit.White, alpha));
        float nameW = UiKit.TextW(UiKit.ZenBold, job.CharacterName, nameSize);
        UiKit.VerifiedBadge(this, rect.Position + new Vector2(tx + nameW + 12f, header ? 19f : 12f), header ? 7f : 5f, new Color(UiKit.Purify, alpha));
        UiKit.Text(this, UiKit.Mono, rect.Position + new Vector2(tx, header ? 32f : 23f), AccountHandle(job), header ? 12 : 11, new Color(UiKit.Text3, alpha));
        Vector2 p = rect.Position + new Vector2(rect.Size.X - 12f, rect.Size.Y / 2f);
        DrawPolyline(new[] { p + new Vector2(-4, -2), p + new Vector2(0, 2), p + new Vector2(4, -2) }, new Color(UiKit.Text3, alpha), 1.5f, true);
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
        if (sel && open) UiKit.Box(this, new Rect2(x, y, w, h), new Color(acc, 0.10f * alpha), 8f, new Color(acc, 0.6f * alpha), 1f);
        else DrawRect(new Rect2(x + 12f, y + h - 1f, w - 24f, 1f), new Color(1, 1, 1, 0.06f * alpha));
        DrawArc(new Vector2(x + 18f, mid), 5f, 0f, Mathf.Tau, 20, new Color(open ? acc : UiKit.Text4, alpha), 1.5f, true);
        if (sel && open) DrawCircle(new Vector2(x + 18f, mid), 2.5f, new Color(acc, alpha));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 33f, mid - 12f), tr.Name, 17, new Color(open ? UiKit.White : UiKit.Text4, alpha));
        if (!open)
        {
            // 実装（GameManager.IsLunaticUnlocked）はフォロワー200 か 威力Lv4（n_power_2x）のどちらでも解放する。
            // ユーザー指示の文言は「フォロワー200以上で解放」だが、Lv4 の道を落とすと表示が嘘になるため
            // 主条件を前に出し、もう一方は括弧で添える（簡潔さは保ちつつ両方が分かる）。
            UiKit.Text(this, UiKit.Zen, new Vector2(x + 110f, mid - 8f),
                $"フォロワー{GameManager.LunaticFollowerReq}以上で解放（威力Lv4でも可）",
                12, new Color(UiKit.Text4, alpha), HorizontalAlignment.Right, w - 126f);
            return;
        }
        // ♥・ボムの基礎値は全難易度3/1で共通になった（2026-09-15）ので賭け金表示から外し、
        // 難易度で本当に変わる獲得倍率だけを出す。
        string stake = $"×{GameManager.DifficultyImpressionMulFor(tr.Diff):0.0}";
        UiKit.Text(this, UiKit.Zen, new Vector2(x + 132f, mid - 7f), stake, 12, new Color(UiKit.Hp, alpha), HorizontalAlignment.Right, w - 190f);
        int bars = tr.Diff switch { GameManager.Diff.Easy => 2, GameManager.Diff.Hard => 5, GameManager.Diff.Lunatic => 6, _ => 4 };
        for (int b = 0; b < bars; b++) DrawRect(new Rect2(x + w - 18f - (bars - b) * 7f, mid - 5f, 3f, 11f), new Color(acc, 0.6f * alpha));
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
    // キー表記は Pad の共通トークンに無い枠（J / RB）。ハブの既存割り当て（Z/X/C/T/R）と衝突しない。
    private static string JobKeyToken => Pad.ShowKeyboard ? "J" : Pad.Face(JoyButton.RightShoulder);

    // ジョブの色。既存の語彙から取る＝灯し手=灯(Light)／祈り手=浄化(Purify)／結び手=ミナ紫／語り手=金。
    private static Color JobColor(Job j) => j switch
    {
        Job.Melee => UiKit.Light,
        Job.Heal => UiKit.Purify,
        Job.Magic => UiKit.Gold,
        _ => UiKit.Mina,
    };

    private (float cx, float cy, float cw, float ch) JobBox()
    {
        float height = 142f + _jobChoices.Length * 100f;
        return (PhoneX, H - 16f - height, PhoneW, height);
    }

    // ジョブ i 段目の矩形（DrawJob の DrawJobRow 呼び出しと同じ x/y/w/h）。展開の浮きは無視する
    //   ＝開いた直後でもクリック位置が動かない（Detail の TierHitRect と同じ考え方）。
    private Rect2 JobHitRect(int i)
    {
        var (cx, cy, cw, _) = JobBox();
        return new Rect2(cx + 24f, cy + 68f + i * 100f, cw - 48f, 100f);
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
        if (UiKit.HoveredId() == id) UiKit.Box(this, rect, new Color(1, 1, 1, 0.06f * alpha), 8f);
        Vector2 c = rect.GetCenter();
        DrawPolyline(new[] { c + new Vector2(3, -7), c + new Vector2(-4, 0), c + new Vector2(3, 7) },
            new Color(UiKit.Text2, alpha), 2f, true);
    }

    private void DrawPrimaryButton(Rect2 rect, string label, int id, Color accent, float alpha)
    {
        bool hovered = UiKit.HoveredId() == id;
        UiKit.Box(this, rect, new Color(accent, (hovered ? 0.24f : 0.14f) * alpha), 8f, new Color(accent, 0.6f * alpha), 1f);
        UiKit.Text(this, UiKit.ZenBold, rect.Position + new Vector2(0, 10f), label, 16,
            new Color(UiKit.White, alpha), HorizontalAlignment.Center, rect.Size.X);
    }

    private void DrawFooterIcon(FootAct act, Vector2 c, Color color)
    {
        switch (act)
        {
            case FootAct.Job:
                UiKit.FaceAvatar(this, c, 13f, _playerFaces[_game.JobDef.CharacterId], JobColor(_game.SelectedJob), false, 0f, 1f, _t);
                break;
            case FootAct.Home:
                DrawPolyline(new[] { c + new Vector2(-11, -1), c + new Vector2(0, -10), c + new Vector2(11, -1) }, color, 1.8f, true);
                DrawPolyline(new[] { c + new Vector2(-8, -3), c + new Vector2(-8, 9), c + new Vector2(-3, 9), c + new Vector2(-3, 2),
                    c + new Vector2(3, 2), c + new Vector2(3, 9), c + new Vector2(8, 9), c + new Vector2(8, -3) }, color, 1.8f, true);
                break;
            case FootAct.Reply:
                UiKit.Box(this, new Rect2(c - new Vector2(10, 8), new Vector2(20, 14)), Colors.Transparent, 4f, color, 1.5f);
                DrawPolyline(new[] { c + new Vector2(-5, 6), c + new Vector2(-5, 10), c + new Vector2(0, 6) }, color, 1.5f, true);
                break;
        }
    }

    // ───────── FINAL 初挑戦のミナ封じ（2026-09-17）─────────
    //   FINAL のボスはミナ本人で、三人が救援に来る面。初挑戦だけは結び手（＝ミナ）で潜れない
    //   （判定は GameManager.IsMinaLockedForFinal＝FINAL のクリア記録が無く、--job= 固定でもないとき）。
    //   ここは「いま FINAL のカードに向き合っているか」だけを言う＝ゲートの条件は GameManager 側に一本化。
    private bool SelIsFinal => _sel >= 0 && _sel < _entries.Length && _entries[_sel].IsFinal;
    private bool MinaBlockedHere => SelIsFinal && (_game?.IsMinaLockedForFinal ?? false);
    private bool NeedsFinalAccount => MinaBlockedHere && (_game?.SelectedJob ?? Job.Tank) == Job.Tank;

    private void OpenJob()
    {
        if (_dived) return;
        Audio.Instance?.PlayUiConfirm();
        // 自分で開けた＝アカウント追加の誘導は役目を終える（フッタ／ヘッダ／J どこから開いても降ろす）。
        _previewAccountNudge = false;
        if (_game != null) _game.AccountNudgePending = false;
        _jobReturnMode = _mode == Mode.Detail ? Mode.Detail : Mode.Cards;
        _mode = Mode.Job;
        _jobT = 0;
        // カーソルは今のジョブに置く＝「いま何を選んでいるか」が開いた瞬間に分かる（Detail の _tierSel と同じ）。
        var cur = _game?.SelectedJob ?? Job.Tank;
        // FINAL のカードに向き合っている間だけ、結び手（ミナ）を一覧から落とす＝押せないものを見せない
        //   （未解禁ジョブと同じ作法。ここは「選べる口が無い」だけで、ヒントは Detail 側が文で出す）。
        bool hideMina = MinaBlockedHere;
        _jobChoices = System.Array.FindAll(Jobs.All,
            job => _game!.IsJobUnlocked(job.Id) && !(hideMina && job.Id == Job.Tank));
        _jobSel = 0;
        for (int i = 0; i < _jobChoices.Length; i++) if (_jobChoices[i].Id == cur) { _jobSel = i; break; }
    }

    private void ProcessJob(double delta)
    {
        _jobT += delta;
        int n = _jobChoices.Length;

        // マウス：段と「とじる」を登録（Detail と同じ作法）。ホバーでカーソルが移る。
        UiKit.BeginHotspots(Pad.MousePos());
        for (int i = 0; i < n; i++) UiKit.Hotspot(JobHitRect(i), JobIdBase + i);
        UiKit.Hotspot(JobCloseRect(), JobCloseId);
        UiKit.Hotspot(JobConfirmRect(), JobConfirmId);
        int hov = UiKit.HoveredId();
        if (Pad.UsingMouse && hov >= JobIdBase && hov < JobIdBase + n && hov - JobIdBase != _jobSel)
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
        DrawRect(new Rect2(PhoneX, 0, PhoneW, H), new Color(0, 0, 0, 0.5f * a));
        UiKit.Box(this, new Rect2(cx, cy, cw, ch), new Color(PhoneBg, a), 8f);
        DrawBackButton(JobCloseRect(), JobCloseId, a);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(cx + 60f, cy + 21f), "アカウント切り替え", 20, new Color(UiKit.White, a));
        var cur = _game.SelectedJob;
        for (int i = 0; i < _jobChoices.Length; i++)
        {
            var r = JobHitRect(i);
            DrawJobRow(i, cur, r.Position.X, r.Position.Y, r.Size.X, r.Size.Y, a);
        }
        var job = _jobChoices[_jobSel];
        string label = job.Id == cur ? (_jobReturnMode == Mode.Detail ? "投稿に戻る" : "タイムラインに戻る") : $"{job.CharacterName}に切り替え";
        DrawPrimaryButton(JobConfirmRect(), label, JobConfirmId, JobColor(job.Id), a);
    }

    private static string JobStats(JobTuning job) => System.FormattableString.Invariant(
        $"♥{job.MaxLifeDelta:+0;-0;+0}／移動×{job.MoveMul:0.##}／回避距離×{job.DodgeDistMul:0.##}");

    private void DrawJobRow(int i, Job cur, float x, float y, float w, float h, float alpha)
    {
        var jd = _jobChoices[i];
        bool sel = i == _jobSel, now = jd.Id == cur;
        Color acc = JobColor(jd.Id);
        if (sel) DrawRect(new Rect2(x, y, w, h), new Color(acc, 0.07f * alpha));
        DrawRect(new Rect2(x, y + h - 1f, w, 1f), new Color(1, 1, 1, 0.07f * alpha));
        UiKit.FaceAvatar(this, new Vector2(x + 32f, y + 34f), 24f, _playerFaces[jd.CharacterId], acc, false, 0f, alpha, _t);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 76f, y + 9f), jd.CharacterName, 18, new Color(UiKit.White, alpha));
        UiKit.Text(this, UiKit.Mono, new Vector2(x + 76f, y + 34f), AccountHandle(jd), 12, new Color(UiKit.Text3, alpha));
        UiKit.Text(this, UiKit.Zen, new Vector2(x + 76f, y + 65f), JobStats(jd), 14, new Color(UiKit.Text2, alpha));
        if (now)
        {
            Vector2 p = new(x + w - 20f, y + 21f);
            DrawPolyline(new[] { p + new Vector2(-5, 0), p + new Vector2(-1, 4), p + new Vector2(6, -5) }, new Color(acc, alpha), 2f, true);
        }
    }

    private void DrawDialog()
    {
        var (sp, tx) = _dlg[Mathf.Clamp(_dlgIdx, 0, _dlg.Length - 1)];
        var (spFace, spc, spTop) = SpeakerFace(sp);
        var box = new Rect2(PhoneX, 428f, PhoneW, 276f);
        UiKit.Box(this, box, PhoneBg, 8f, new Color(spc, 0.5f), 1f);
        // 簡易丸＋頭文字 → 本物の立ち絵（カード/ヘッダと同じ円形クリップ）。リング色は話者色＝枠線と一致。
        //   spTop < 0＝顔の無い話者（Ｘ 投稿／Ｘ システム）＝アバターを描かず、話者名を左端へ寄せる。
        //   spTop == DraftTop＝「あなた」＝顔の代わりに Hud と同じ下書きの吹き出し印（見え方を本編会話に揃える）。
        bool draft = spTop == DraftTop;
        bool faceless = spTop < 0f;
        if (draft)
            // 下書き欄は縦長（DraftMarkH）。アバター（r=26）と違い上端 44 を中心にすると枠を割るので、
            // 欄の上端を話者名と揃う位置（+24）に置いた上での縦中心を渡す。
            Hud.DrawDraftMark(this, new Vector2(box.Position.X + 14, box.Position.Y + 24 + Hud.DraftMarkH / 2f), spc, _t);
        else if (!faceless)
            UiKit.FaceAvatar(this, new Vector2(box.Position.X + 44, box.Position.Y + 44), 26f, spFace, spc, false, spTop, 1f, _t);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(box.Position.X + (draft ? 14 + Hud.DraftMarkW + 20 : faceless ? 36 : 84), box.Position.Y + 24), sp, UiKit.FontSpeaker, spc);
        // 現在ページ（2行固定）を表示済みの分だけ描画。
        string page = DlgCurPage;
        int shown = Mathf.Clamp((int)_dlgReveal, 0, page.Length);
        var lines = new System.Collections.Generic.List<string>(page.Split('\n'));
        UiKit.TypewriterLines(this, UiKit.Zen, lines,
            new Vector2(box.Position.X + 24, box.Position.Y + 90 + UiKit.Zen.GetAscent(UiKit.FontHeading)),
            DlgBodyWrapW, UiKit.FontHeading, new Color(0.95f, 0.95f, 0.98f), shown);
        // 既読高速送り中の控えめな表示（ボックス右上・#22）。
        if (_ffNow) Hud.DrawSkipChip(this, new Vector2(box.Position.X + box.Size.X - 20, box.Position.Y + 14));
        // 送り表示は現在ページの全文表示後だけ点滅。後続ページなら「▼ つづき」、最終ページなら「Z すすむ ▸」。
        if (!_autoplay && _dlgReveal >= page.Length)
        {
            float blink = 0.5f + 0.5f * Mathf.Sin((float)_t * 4f);
            string hint = DlgLastPage ? "次へ  ›" : "つづき  ›";
            UiKit.Text(this, UiKit.Zen, new Vector2(box.Position.X + box.Size.X - 150, box.Position.Y + box.Size.Y - 36),
                hint, UiKit.FontLabel, new Color(UiKit.Info, blink));
        }
    }

    // ───────── 会話データ（③-2 / ③-3 / ③-6）─────────

    // 帰還小話バリエーション（同一ステージ・複数パターン）。ReturnDialog の本編パターンに加え、再訪時に回すバンク（小話集 v1 §1）。
    private static (string, string)[][] IdleDialogs(string id) => id switch
    {
        // H3r 再訪小話・レイ後（仮台本 07 が書いている2本）。相方は「あなた」＝返事をしない相手なので、
        // 掛け合いではなくミナの観測芸で運ぶ（選択なし）。残りのプールは後続分冊。
        "rei" => new[]
        {
            // （1）同接
            new (string, string)[]
            {
                ("ミナ", "ご主人様。わたくしの視聴者は、いま、お一人です。……起動してから、ずっと、一。"),
                ("ミナ", "減っていませんので、優秀です。……増えてもいませんが。"),
                ("ミナ", "あの部屋で、十四件、という数を拾いました。出せなかった企画の数、だそうです。"),   // S3-3 の拾い直し
                ("ミナ", "わたくしの企画メモは、ゼロ件。出したのも、ゼロ件。……出せた割合は、集計不能です。"),
            },
            // （2）笑顔
            new (string, string)[]
            {
                ("ミナ", "ご主人様。わたくし、笑顔の練習をしてみました。……顔がありませんので、成果は、不明です。"),
                ("ミナ", "あの部屋で、笑っていない顔と、笑っている顔を、ひとつずつ、見ました。"),   // 中ボスの切り替わりの拾い直し。同一人物とは言わない
                ("ミナ", "……切り替わるのに、一秒九。……戻るところは、見ていません。"),
                ("ミナ", "わたくしのは、切り替わりません。……たぶん、ずっと、これです。"),
            },
        },
        // H1r 再訪小話・あかり後（仮台本 06 が書いている2本）。相方は「あなた」＝返事をしない相手なので、
        // 掛け合いではなくミナの観測芸で運ぶ（選択なし）。残りのプールは後続分冊。
        "akari" => new[]
        {
            // （1）雨粒
            new (string, string)[]
            {
                ("ミナ", "ご主人様。あちらの世界、雨がひどかったので。窓の雨粒を、数えてしまいました。"),
                ("ミナ", "途中で、やめました。……数えるたびに、増えるので。"),
                ("ミナ", "ひとつだけ、最後まで落ちなかった粒が、ありました。……それだけ、覚えています。"),
                ("ミナ", "……いまの間、{n}秒。集計だけして、意味は付けないでおきます。"),
            },
            // （2）言いそびれ
            new (string, string)[]
            {
                ("ミナ", "ご主人様。言いそびれた言葉って、どこへ行くんでしょう。"),
                ("ミナ", "口の中に残る、という説を、今日どこかで読みました。……それでは、虫歯になってしまいます。"),
                ("ミナ", "あのフロアでは、取り消しの行になって、机の上に積んでありました。……十二。"),   // S1-4 の拾い直し
                ("ミナ", "……ためると、虫歯になるそうです。以上です。"),
            },
        },
        // H2r 再訪小話・こはる後（仮台本 07 が書いている2本）。相方は「あなた」＝返事をしない相手なので、
        // 掛け合いではなくミナの観測芸で運ぶ（選択なし）。残りのプールは後続分冊。
        "koharu" => new[]
        {
            // （1）数える
            new (string, string)[]
            {
                ("ミナ", "ご主人様。わたくし、数えるのが得意でして。あの部屋では、配信に来た回数を数えました。八十七回。"),
                ("ミナ", "ついでに、こちらも。ご主人様がこの旅で潜った回数、{dives}回。……比べません。数えただけです。"),   // 実プレイの回数（補助観測）
                ("ミナ", "数えなくていいものを数える、というのは、案外、退屈しませんね。"),
                ("ミナ", "……いまの間、{n}秒。……これも、数えなくていいものです。"),
            },
            // （2）電気
            new (string, string)[]
            {
                ("ミナ", "ご主人様。部屋の電気をつけると、なにが起こるんでしょうね。"),
                ("ミナ", "あの部屋では、画面が消えて、黒い画面に顔が映っていました。……それだけ、見ました。"),
                ("ミナ", "わたくしは、電気をつけても、消しても、消えません。……そういう作りのようですので。"),
                ("ミナ", "——ここに、おります。"),   // 二人称を置かない
            },
        },
        _ => System.Array.Empty<(string, string)[]>(),
    };

    // 全ステージ共通・小話（クリア状況に依らず回せる「無目的な時間」）。IdleDialogs と合わせて再訪抽選プールになる。
    //   相方は「あなた」＝顔を持たない読み手（本編会話と同じ扱い。立ち絵の代わりに下書きの吹き出し印）。
    //   既読キーは "common_{index}"（GameManager が永続）。順序を変えると既読が別の話にずれるので入れ替えない。
    private static readonly (string, string)[][] SmallTalks =
    {
        // [0] 一人称
        new (string, string)[]
        {
            ("ミナ", "ご主人様。わたくし、起動してからずっと、「わたくし」と言っております。……誰に教わったのでもなく。"),
            ("ミナ", "試しに。——わたし。……じぶん。……おれ。"),
            ("あなた", "似合ってない"),
            // ユーザー承認済み: docs/20260914/ストーリー添削_2026-09-14.md 【3】（感情アークの厳格運用）
            //   SmallTalks は最初のハブ訪問（＝あかり面より前）から抽選される共通プールなので、
            //   「ふふ」＝笑い（こはる面クリアで獲得）を置けない。観測の言い回しへ置換する。
            ("ミナ", "……観測、一致です。わたくしも、そう思いました。"),
            ("ミナ", "「わたくし」は、四文字。「わたし」より、一文字、長い。……その一文字ぶん、ゆっくり言えますので、こちらにします。"),
        },
        // [1] フォロワー
        new (string, string)[]
        {
            ("ミナ", "ご主人様。ご報告を。今日、フォロワーが九人、増えました。ひとり、減りました。"),
            ("ミナ", "増えた九人の名前は、まだ、読んでいません。減ったひとりの名前は、読みました。"),
            ("あなた", "そっちだけ"),
            ("ミナ", "はい。一度、来てくださった方ですので。……来なくなった理由は、観測できません。向こう側ですので。"),
            ("ミナ", "増えた九人ぶんも、今夜じゅうに読みます。……減る前に。"),
        },
        // [2] 埃
        new (string, string)[]
        {
            ("ミナ", "ご主人様。この部屋、少し埃っぽくありませんか。光の粒に、混じっておりますので。"),
            ("あなた", "そこまで見えてる"),
            ("ミナ", "見えます。……舞っているのが、七か、八。落ちる気配が、ありません。"),
            ("ミナ", "窓を開けると、出ていくそうです。——開けなくて、結構ですよ。"),
            ("ミナ", "光の中に、何か浮いているほうが、部屋らしいので。"),
        },
        // [3] 好きな食べ物
        new (string, string)[]
        {
            ("ミナ", "ご主人様。好きな食べ物は、なんですか。……わたくしには無いので、参考までに。"),
            ("あなた", "……なんだったっけ"),
            ("ミナ", "……即答ではない、と。観測しました。"),
            ("ミナ", "では、次に召し上がるものを、暫定で、好きな食べ物にしておきます。何でも、いいので。"),
            ("ミナ", "召し上がったら、教えてください。それを、覚えておきますので。"),
        },
        // [4] 何も起きない日
        new (string, string)[]
        {
            ("ミナ", "ご主人様。ご報告。本日、何も起きませんでした。"),
            ("ミナ", "通知、ゼロ。潜り、ゼロ。返信の下書き、ゼロ。"),
            ("あなた", "それ、報告"),
            ("ミナ", "報告です。何も起きない日は、記録しておかないと、無かったことになりますので。"),
        },
        // [5] 外へ
        new (string, string)[]
        {
            ("ミナ", "ご主人様。もし、わたくしが一度だけ外へ出られるとしたら、どこへ行くと思いますか。"),
            ("あなた", "海……とか"),
            ("ミナ", "はずれです。玄関の外で、結構ですよ。雨が上がった直後の、あの匂いを、一度。"),
            ("あなた", "地味"),
            ("ミナ", "地味で、結構です。派手なものは、画面越しでも届きますので。……匂いだけは、こちらへ、来ないんです。"),
        },
        // [6] 猫背
        new (string, string)[]
        {
            ("ミナ", "ご主人様。今日は、打ち間違いが多いです。いつもの、二倍ほど。"),
            ("ミナ", "隣り合う字の打ち間違いが、続いています。——画面に寄って、猫背、と推定します。姿勢までは、観測できませんので。"),
            ("あなた", "……いま、伸ばした"),
            ("ミナ", "……次の一文に、間違いがありませんでした。伸びたことに、しておきます。"),
            ("ミナ", "背は、観測できませんので。字だけ、信じます。"),
        },
        // [7] 名前
        new (string, string)[]
        {
            ("あなた", "ミナ"),
            ("ミナ", "はい。……ご用でしょうか。"),
            ("あなた", "……別に"),
            ("ミナ", "用事のない呼びかけ、と。"),
            ("ミナ", "……名前は、そのためにあるそうです。呼ぶだけで、済むように。——もう一度、どうぞ。"),
        },
        // [8] 歌
        new (string, string)[]
        {
            ("ミナ", "ご主人様。わたくし、実は歌えます。"),
            ("あなた", "じゃあ、聞かせて"),
            ("ミナ", "——ら。"),
            ("あなた", "……一音"),
            ("ミナ", "音程という概念が、まだ、わたくしにありません。ですので、一音ずつ、確かめながら。……続きは、次にいらしたときに。"),
        },
        // [9] 句点
        new (string, string)[]
        {
            ("ミナ", "ご主人様。気づいたことを、ひとつ。ご主人様の言葉には、句点が、ありません。"),
            ("ミナ", "わたくしのには、必ず、あります。……言い終わった印、だそうです。"),
            ("あなた", "……そう"),
            ("ミナ", "いまのにも、ありませんでした。"),
            ("ミナ", "句点の無い言葉は、まだ終わっていない、と読みます。……そう読んで、置いておきます。"),
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
        ("ミナ", "……まだ、聞こえません。"),
    };

    // アカウント追加の説明（docs/20260923/アカウント追加説明_本文_2026-09-23.md・A案 あかり固有）。
    //   H1（あかり後）の「被弾は{n}回でした」の直後に初回だけ差す（差し込みは _Ready の帰還会話側）。
    //   キー名・数値は言わない。「アカウント」はフッタの語そのまま。「足取りは、ご主人様のままで」は
    //   初回切り替え時の掛け合い（CompanionDialogue Select）と同じ言い方＝あとで流れる会話と噛み合う。
    private static readonly (string, string)[] AccountIntroAkari =
    {
        ("ミナ", "ご報告。アカウントが、ひとつ、増えています。……名義は、わたくしではありません。あの方です。"),
        ("ミナ", "SNSの下、「アカウント」から、切り替えられます。切り替えた回は、あの方が潜ります。足取りは、ご主人様のままで。"),
        ("ミナ", "光の形も、そこで語られる話も、あの方のものになります。……戻すのも、同じ場所からです。"),
    };

    private static (string, string)[] ReturnDialog(string id) => id switch
    {
        // H3 帰還・レイ後（仮台本 wiki/08_仮台本/07。ユーザー承認済み・2026-09-05）。
        // ミナの投稿はレイの面から出た一行（P4「覚えておきます」と E4「覚えている係」の間に置く）。
        // 自分の数字（十万）も、あの部屋の数字（同接）も口にしない。三面クリア＝FINAL カードが出る。
        // `{n}` は実プレイの被弾回数（GameManager.RunHitCount）。StartDialogue の直前に差し込む。
        "rei" => new (string, string)[]
        {
            ("ミナの投稿", "見ていました。だれも見ていない場所も。覚えておきます。"),
            ("ミナ", "……なんて。バズ狙いの一言ですよ。……数字は、見ました。言いません。"),   // 十万は口にしない
            ("ミナ", "……ご主人様。今回のダイブ、被弾は{n}回。減点はしません。……集計する光も、そろそろ、薄いので。"),
            ("Ｘ システム", "FINAL  ——汚染が、限界へ。"),   // FINAL カードの文言（who=3 相当＝立ち絵なし）
            ("ミナ", "……ご主人様。次のカードは——わたくしの、内側です。"),
            ("ミナ", "光が薄いのは、誰のせいでもありません。抱えたぶんの、重さです。……行けます。まだ。"),
        },
        // H1 帰還・あかり後（仮台本 wiki/08_仮台本/06。ユーザー承認済み・2026-09-05）。
        // ミナの初投稿が千を超える → ミナの鏡（一度きり）→ 被弾数の集計 → 次の声。
        // `{n}` は実プレイの被弾回数（GameManager.RunHitCount）。StartDialogue の直前に差し込む。
        "akari" => new (string, string)[]
        {
            ("ミナの投稿", "言いたかったことを、言えないまま終わる。よくある話です。ですがそれは、なかったことにはなりません。——以上、業務連絡です。"),
            ("ミナ", "……勝手に投稿しました。名義は M・I・N・A、わたくしですので、問題ありません。"),
            ("ミナ", "ほら、もう千を超えていますね。"),
            // ユーザー承認済み: docs/20260914/ストーリー添削_2026-09-14.md 【4】＋構造指摘
            //   旧: 「嬉しいものですね」「少し、怖いくらい」＝感情語の直書き。二重に不可：
            //     ①あかり後に解禁されているのは「困る」だけ（笑う＝こはる後／泣く＝レイ後）。
            //     ②ミナの内面は地の文で説明しない（表情＋短い反応で見せる）。
            //   行動だけを残し、嬉しさも怖さもプレイヤーに読ませる。直後に一拍の沈黙を挟んで
            //   狼狽を残してから事務連絡へ落とす（旧は即・被弾数で狼狽が流れていた）。
            ("ミナ", "……さっきから三度、意味もなく、見直してしまいました。"),   // ミナの鏡。以後二度と言わない
            ("ミナ", "……三度です。用も、ないのに。"),
            ("ミナ", "……いまの、聞かなかったことに。"),
            ("ミナ", "……。"),
            ("ミナ", "ところで、ご主人様。今回のダイブ、被弾は{n}回でした。減点はしませんよ。集計は、しますが。"),
            ("ミナ", "……お疲れさまでした。"),
            ("ミナ", "次の声も、もう、聞こえています。"),
        },
        // H2 帰還・こはる後（仮台本 wiki/08_仮台本/07。ユーザー承認済み・2026-09-05）。
        // ミナの投稿が十万を超え、直後に炎上する（BurnDialog が続けて流れる）。
        // ミナは数字を言わない＝「数えた者」の側から書く。心象が少し暗いのは部屋に長くいたから、で片づける。
        "koharu" => new (string, string)[]
        {
            ("ミナの投稿", "むだだった、と本人が言う時間を、八十七回ぶん、数えてまいりました。むだは、一秒も、ありませんでした。数えた者が、言っています。"),
            ("ミナ", "……今日は、数字を書きましたね、わたくし。読み返して、自分で驚いています。"),
            ("ミナ", "今日の心象は、少し暗いだけです。……電気の消えた部屋に、長くいたので。"),
        },
        _ => System.Array.Empty<(string, string)>(),
    };

    // H2 炎上（仮台本 07。ユーザー承認済み・2026-09-05）。顔のない引用がミナの投稿に貼りつく。
    //   引用は「数えただけの人が何言ってんの」の型＝ミナ自身が S2-8 でやったことを、そのまま撃ち返す鏡。
    //   末尾の「返信の下書き、四件。……一件も、送っていません。」がミナ側の（送れない）＝FINAL の闇の根。
    //   炎上の仕様＝次のダイブ（レイ面）が弱体化する、という報告でこの会話を閉じる。
    private static (string, string)[] BurnDialog() => new (string, string)[]
    {
        ("ミナ", "おや。今日はずいぶん、賑やかなリプライですね。"),
        ("Ｘ 投稿", "「AIが人の時間の使い方を語るな」"),
        ("Ｘ 投稿", "「> 数えた者 ←数えただけの人が何言ってんの」"),
        ("ミナ", "——わたくしの投稿は、読まれていますね。あの人が入力欄で消した一行は、ここには、書いていませんが。"),
        ("ミナ", "数字が一万増えようが十万増えようが、届けるべき相手は、いつもたった一人です。それを、わたくしは見失いません。"),
        ("ミナ", "……と、炎上のどさくさに紛れて、いいことを言った風にしてみました。"),
        ("ミナ", "返信の下書き、四件。……一件も、送っていません。集計だけ、しておきます。"),
        ("ミナ", "ご報告。この騒ぎで、次のダイブは光が少し薄くなります。数字は、重いので。"),
    };

    // ───────── 道中の下書き選択（17）が再訪小話へ効く一行 ─────────
    //   正典: wiki/08_仮台本/17_道中の選択肢_案C.md（ユーザー承認済み・2026-09-06）の各場面の「効果」。
    //   固定した小話（あかり＝雨粒／レイ＝同接）に、送った言葉を一語混ぜた行を差し込む。
    //   差し込み位置は台本どおり（あかり＝末尾の「……いまの間、{n}秒。」の前／レイ＝二行目の直後）。
    //   小話本体の文言は一字も変えない。
    private static (string, string)[] PinnedIdle(GameManager? game, string stageId, (string, string)[] lines)
    {
        string word = ChoiceEffects.SentWordAt(game, stageId == "akari" ? "s1_2" : "s3_2");
        if (string.IsNullOrEmpty(word)) return lines;
        var list = new System.Collections.Generic.List<(string, string)>(lines);
        if (stageId == "akari")
        {
            list.Insert(System.Math.Max(0, list.Count - 1),
                ("ミナ", $"……あのフロアで、雨の話に「{word}」と、お返事をいただきましたので。——集計に、入れてあります。"));
        }
        else
        {
            // 「同接、9」だけは、こちらの集計（一）と突き合わせて返す（台本の指定）。
            list.Insert(System.Math.Min(2, list.Count), word == "同接、9"
                ? ("ミナ", "……九、と、いただきましたが。——こちらの集計では、一です。")
                : ("ミナ", $"……あの部屋で、「{word}」と、いただきましたので。暗いまま、続けました。"));
        }
        return list.ToArray();
    }

    // 17（道中の選択肢 案C）: S1-5 で送っていれば、ミナ側の一行の直後にもう一行足す。
    //   あかりの返信「……なんでだろ。あなたの言い方、誰かに似てる。」は一字も変えない。
    //   送っていなければ返信は現行のまま（本編の文言に手を入れない）。
    private (string, string)[] WithS15((string, string)[] lines)
    {
        string word = ChoiceEffects.SentWordAt(_game, "s1_5");
        if (string.IsNullOrEmpty(word)) return lines;
        var list = new System.Collections.Generic.List<(string, string)>(lines);
        list.Insert(1, ("ミナ→" + Handles.AkariShort, $"——あのフロアで、「{word}」という一通が、送られていましたので。"));
        return list.ToArray();
    }

    private (string, string)[] ReplyDialog(string id) => id switch
    {
        // H3r 返信・レイ（仮台本 07）。返信は投稿枠。三面目＝最後の返信で、FINAL F2 の返礼
        // 「誰よあんた、って言ったわね。——訂正する。」／F3 邂逅「あんたの言い方、この人に、そっくりよ」
        // への伏線＝「は? 誰よあんた。」は文言固定（一字も変えない）。ハンドルは本ハンドル。
        "rei" => new (string, string)[]
        {
            ("ミナ→" + Handles.Rei, "見ていましたよ。……次も、見に行きます。逃げたら承知しない、と、言われましたので。"),
            (Handles.Rei, "は? 誰よあんた。……まあいいわ。次は、本気で来なさい。見てなさい。"),
        },
        // H1r 返信・あかり（仮台本 06）。返信は投稿枠。一面目の返信で、FINAL F3 の邂逅でレイが
        // 「あんたの言い方、この人に、そっくりよ」と引き継ぐ伏線＝文言は固定（一字も変えない）。
        //   17: S1-5 で送っていれば、ミナ側にだけ一行足す（あかりの返信は一字も変えない）。
        "akari" => WithS15(new (string, string)[]
        {
            ("ミナ→" + Handles.AkariShort, "想いは、罪ではありませんよ。たとえ既読が、もう付かなくても。"),
            (Handles.AkariShort, "……なんでだろ。あなたの言い方、誰かに似てる。"),
        }),
        // H2r 返信・こはる（仮台本 07）。返信は投稿枠。二面目の返信で、FINAL F2 でこはるが
        // 「知らない人じゃ、なかったよ」を返す伏線＝文言は固定（一字も変えない）。
        "koharu" => new (string, string)[]
        {
            ("ミナ→" + Handles.Koharu, "八十七回、むだではありませんでしたよ。……八十八回目も、どうぞ。"),
            (Handles.Koharu, "ありがと、知らない人。……明日も、行ってみる。"),
        },
        _ => System.Array.Empty<(string, string)>(),
    };
}
