using Godot;
using System.Collections.Generic;

// Shop : ミナ強化ショップ。二分木ディシジョンツリー型レイアウト（3幹並列スキルツリーから改装）。
//   上：ヘッダ＋ウォレット ／ ショットモード切替ストリップ（R0・装備チップ＋過熱トグル）
//   中：単一ルート「ミナの核」から 1→2→4→7→6 と二個ずつ広がる二分木。
//       攻めの系譜（連射速度→…）と支えの系譜（身のこなし→…）。親Lv≥1 でノードに入れる（続きLvは親不要）。
//       枝の末端は排他フォーク4対（⊗）＝どちらか一方だけ。片側を選ぶと相方は封印（振り直しで解除可）。
//       通常の二叉＝白灰の独立エッジ2本／排他フォーク＝金のY字束ね＋⊗メダル＋共通ブラケットで二重符号化。
//   右：詳細パネル「つぎの一手」＝射撃プレビュー・いま→買うと・コスト・前提/親/封印・振り直し差引き。
//   振り直し：封印側ノードで Z →「選び直す？」2段確認。返金100%−手数料20%（10単位切上げ・最低100）。
//   操作：↑↓←→ えらぶ（方向最近傍・エッジ接続先優先）／Z 購入/選び直し／C 装備／V 過熱／X もどる。
public partial class Shop : Node2D
{
    private GameManager _game = null!;
    private const float W = UiKit.DesignW, H = UiKit.DesignH;

    // ★上部の装備チップに並ぶのは 0..2（連射/拡散/ホーミング）のみ（DrawModeStrip は for i<3）。
    //   加速球(index 3)はチップを増やすと _sel 体系（3=root）が破綻するためチップにはしない。
    //   代わりに加速球は「起点ノード accel_1」で装備（C）＋購入時に自動装備＋通常プレイの V ローテで選べる
    //   （拡散/ホーミングと同じ流儀）。Modes/ModeEn/ModeDesc は index 3 まで持たせて EquipMode(3)・
    //   詳細プレビューの索引整合を取る（配列外参照を防ぐ）。
    private static readonly GameManager.ShotMode[] Modes =
        { GameManager.ShotMode.Rapid, GameManager.ShotMode.Spread, GameManager.ShotMode.Homing, GameManager.ShotMode.Accel };
    private static readonly string[] ModeEn = { "RAPID", "SPREAD", "HOMING", "ACCEL" };
    private static readonly string[] ModeDesc =
    {
        "右へ直線の高速ストリーム。正面の硬い敵に火力集中。",
        "右方向へ扇状に展開。雑魚処理・面制圧・道中向き。",
        "右側のマゼンタの穢れへ吸い寄せられて曲射。避けながら削る。",
        "その場でタメて撃つ→0.8秒後にロケット発進。硬い敵に一気に叩き込む。",
    };

    // ───── 単Lvノード配置（設計座標・セル左上）。W146×H44、深さ列 x: d1=168/d2=344/d3=520/d4=696/d5=872/d6=1048/d7=1224 ─────
    //   系統ごとに縦の「帯（レーン）」を割り当て、行ピッチ66px（セル44＋余白22）で重なりゼロに再配置（フェーズ3）。
    //   縦に長い＝スクロールで見える。MaxCam（TreeVirt*）はこの表の実端から算出＝座標を変えれば自動追従する。
    //   帯順：連射→拡散→ホーミング→バックファイア→生存/経済。x列は親→子が左→右に流れるよう深さで決める。
    private static readonly (string id, float x, float y)[] NodePos =
    {
        // ── 連射系（ATTACK） y120-352 ──
        ("shot_power_1",  344, 120),
        ("shot_power_2",  520, 120),
        ("shot_power_3",  696, 120),
        ("shot_power_4",  872, 120),
        ("fire_rate_1",   168, 186),
        ("fire_rate_2",   344, 186),
        ("fire_rate_3",   520, 186),
        ("fire_rate_4",   696, 186),
        ("rapid_power_1", 696, 252),
        ("rapid_power_2", 872, 252),
        ("pierce_1",      1048, 220),
        ("pierce_2",      1224, 220),
        ("focus_1",       1048, 286),
        ("focus_2",       1224, 286),
        ("rapid_rate_1",  696, 352),
        ("rapid_rate_2",  872, 352),
        // ── 拡散系（SPREAD） y452-782 ──
        ("spread_1",      344, 452),
        ("spread_2",      520, 452),
        ("spread_3",      696, 452),
        ("spread_power_1",520, 518),
        ("spread_power_2",696, 518),
        ("option_1",      696, 584),
        ("option_2",      872, 584),
        ("fol_gain_1",    520, 650),
        ("fol_gain_2",    696, 650),
        ("chain_1",       872, 650),
        ("chain_2",       1048, 650),
        ("spread_rate_1", 696, 782),
        // ── ホーミング系（HOMING） y882-1080 ──
        ("homing_1",      344, 882),
        ("homing_2",      520, 882),
        ("homing_3",      696, 882),
        ("homing_power_1",520, 948),
        ("homing_power_2",696, 948),
        ("counter_1",     696, 1014),
        ("counter_2",     872, 1014),
        ("homing_rate_1", 696, 1080),
        ("veil_1",        872, 1080),
        ("veil_2",        1048, 1080),
        // ── バックファイア系（BACKFIRE） y1180-1312 ──
        ("bf_power_1",    344, 1180),
        ("bf_power_2",    520, 1180),
        ("bf_power_3",    696, 1180),
        ("bf_rate_1",     520, 1246),
        ("bf_rate_2",     696, 1246),
        ("bf_track_1",    520, 1312),
        // ── 生存・経済系（SURVIVAL/ECON） y1412-1808 ──
        ("move_speed_1",  168, 1412),
        ("move_speed_2",  344, 1412),
        ("move_speed_3",  520, 1412),
        ("combo_hold_1",  168, 1478), // move_speed_1直下へ移設（旧: 拡散帯fol_gain_1直下から中立幹の傍へ）
        ("combo_hold_2",  168, 1544),
        ("contam_1",      344, 1478),
        ("contam_2",      520, 1478),
        ("hitbox_1",      520, 1544),
        ("hitbox_2",      696, 1544),
        ("hitbox_3",      872, 1544),
        ("imp_mult_1",    520, 1610),
        ("imp_mult_2",    696, 1610),
        ("imp_mult_3",    872, 1610),
        ("imp_mult_4",    1048, 1610),
        ("max_life_1",    520, 1676),
        ("max_life_2",    696, 1676),
        ("bomb_count_1",  696, 1742),
        ("bomb_count_2",  872, 1742),
        ("bomb_power_1",  696, 1808),
        ("bomb_power_2",  872, 1808),
        // ── 加速球系（ACCEL） y1908-2040 ──（拡散/ホーミングと同格の独立帯。入り口 accel_1 でモード解放）
        ("accel_1",       344, 1908),
        ("accel_power_1", 520, 1908),
        ("accel_power_2", 696, 1908),
        ("accel_charge_1",520, 1974),
        ("accel_charge_2",696, 1974),
        ("accel_speed_1", 696, 2040),
    };
    private const float NodeW = 146f, NodeH = 44f;
    private static readonly Vector2 RootC = new(72f, 389f); // ルート「ミナの核」＝円形メダリオン
    private const float RootR = 34f;
    // 解放パルスの対象（前提つきノード＝各系統の奥義の入り口）。前提が成立した瞬間に「解放!」を一度だけ。
    private static readonly string[] CapstoneIds =
        { "pierce_1", "option_1", "chain_1", "counter_1", "veil_1" };

    // おすすめ（迷ったらこれ）：進行連動の道しるべ。表示はフロンティア強調＝おすすめ∩いま買えるを金パルス。
    // 親未接続で買えないおすすめは、親チェーンを遡って最初に買える祖先を代わりに光らせる（RebuildFrontier）。
    //   クリア状況の4分岐（下記 Base）を土台に、①所持済みidを除外 ②使用中ショットモードの未所持入り口ノードを
    //   先頭に差し込み ③残りは「買える物」優先で安定ソート ④全所持済みなら前段の分岐へフォールバック、
    //   それでも空ならクラッシュしないよう固定デフォルトを返す（所持状況・所持金・装備モードを無視しない）。
    private string[] RecommendedNow()
    {
        if (_game == null) return new[] { "shot_power_1", "fire_rate_1", "max_life_1" };

        // 既存の4分岐（クリア進行の段階別ベース推薦）はそのまま土台として使う。
        string[] Base(int stage) => stage switch
        {
            3 => new[] { "imp_mult_1", "fol_gain_1" },
            2 => new[] { "hitbox_1", "bomb_power_1" },
            1 => new[] { "spread_1", "homing_1", "bomb_count_1" },
            _ => new[] { "shot_power_1", "fire_rate_1", "max_life_1" },
        };
        int stage = _game.IsStageCleared("koharu") ? 3
            : _game.IsStageCleared("akari") ? 2
            : _game.IsStageCleared("rei") ? 1
            : 0;

        // ①所持済みidを除外。段階のベースが全部所持済みなら前段（易しい方）へ遡ってフォールバック。
        var rest = new List<string>();
        for (int s = stage; s >= 0 && rest.Count == 0; s--)
            foreach (var id in Base(s))
                if (_game.GetUpgradeLevel(id) < 1 && !rest.Contains(id)) rest.Add(id);

        // ③残った候補は「買える物」を先に（List.Sort は安定ソート非保証のため、2パスの明示的な安定分割を使う）。
        var affordable = new List<string>();
        var notAffordable = new List<string>();
        foreach (var id in rest) (_game.CanPurchase(id) ? affordable : notAffordable).Add(id);
        rest = affordable;
        rest.AddRange(notAffordable);

        // ②使用中ショットモードに対応する系統の未所持入り口ノードを先頭に差し込む（所持済みなら差し込まない）。
        string modeEntry = _game.SelectedShotMode switch
        {
            GameManager.ShotMode.Spread => "spread_1",
            GameManager.ShotMode.Homing => "homing_1",
            GameManager.ShotMode.Accel => "accel_1",
            _ => "fire_rate_1", // Rapid＝連射系の入り口
        };
        if (_game.GetUpgradeLevel(modeEntry) < 1)
        {
            rest.Remove(modeEntry);
            rest.Insert(0, modeEntry);
        }

        // ④全て所持済みで rest が空になっても、呼び出し元（RebuildFrontier）が落ちないよう固定デフォルトで補う。
        return rest.Count > 0 ? rest.ToArray() : new[] { "shot_power_1", "fire_rate_1", "max_life_1" };
    }
    private string[] _recommended = System.Array.Empty<string>();
    private readonly HashSet<string> _frontier = new(); // _Draw 冒頭で毎フレーム更新

    // カテゴリ色（詳細パネルの色タグ）。0=攻撃 / 1=生存 / 2=応援。
    private static readonly Color[] CatCol = { new("9be0f5"), new("7ec880"), new("f0d98a") };
    private static int CatFor(string id) => id switch
    {
        // 1=生存（青緑→緑）：生存・回避・ボム・帳。バックファイアも守勢の系譜として生存色に寄せる。
        _ when id.StartsWith("max_life") || id.StartsWith("bomb_count") || id.StartsWith("bomb_power")
            || id.StartsWith("move_speed") || id.StartsWith("hitbox") || id.StartsWith("contam")
            || id.StartsWith("veil") || id.StartsWith("bf_") => 1,
        // 2=応援（口コミ・獲得心・コンボ）。
        _ when id.StartsWith("imp_mult") || id.StartsWith("fol_gain") || id.StartsWith("combo_hold") => 2,
        _ => 0, // 0=攻撃（連射・威力・拡散・ホーミング・貫通・集中・オプション・連鎖・返し・誘導）
    };

    // ───── 5系統アイデンティティ ─────
    //   連射／拡散／ホーミング／バックファイア／生存経済を一目で色分けする（帯・エッジ・ノード縁・ラベル）。
    //   所持済みエッジと「育てた道」の発光をこの色で塗る＝どの枝を伸ばしてきたかが色で読める。
    //   色相を広く散らして隣接系統でも判別可（シアン/アンバー/グリーン/バイオレット/ローズ）。色弱でも帯位置＋形状で二重符号化。
    private enum Stream { Rapid = 0, Spread = 1, Homing = 2, Backfire = 3, Survive = 4, Accel = 5 }
    private static readonly Color[] StreamCol =
    {
        new("7ad7f0"), // 連射＝シアン（既存 Info 系）
        new("f2b866"), // 拡散＝アンバー
        new("86dca0"), // ホーミング＝グリーン
        new("c39cf0"), // バックファイア＝バイオレット（ミナ紫の親戚）
        new("f0a0a8"), // 生存・経済＝ローズ
        new("f0925c"), // 加速球＝オレンジ（弾の琥珀色の親戚・拡散アンバーより赤寄りで判別可）
    };
    private static readonly string[] StreamName = { "連射", "拡散", "ホーミング", "後方の光", "生存・経済", "加速球" };
    private static Stream StreamOf(string id) =>
        id.StartsWith("spread") || id.StartsWith("fol_gain")
            || id.StartsWith("option") || id.StartsWith("chain") ? Stream.Spread
        : id.StartsWith("homing") || id.StartsWith("counter") || id.StartsWith("veil") ? Stream.Homing
        : id.StartsWith("bf_") ? Stream.Backfire
        : id.StartsWith("accel") ? Stream.Accel
        : id.StartsWith("move_speed") || id.StartsWith("contam") || id.StartsWith("hitbox")
            || id.StartsWith("imp_mult") || id.StartsWith("max_life") || id.StartsWith("bomb")
            || id.StartsWith("combo_hold") ? Stream.Survive // combo_holdはmove_speed_1直下(経済帯)へ移設済み
        : Stream.Rapid; // 連射・威力・貫通・集中・速射
    private static Color StreamColor(string id) => StreamCol[(int)StreamOf(id)];

    // 系統に属する全ノードが所持済みか（ご褒美演出のトリガ判定）。
    private bool IsStreamComplete(Stream s)
    {
        if (_game == null) return false;
        bool any = false;
        foreach (var (id, _, _) in NodePos)
        {
            if (StreamOf(id) != s) continue;
            any = true;
            if (_game.GetUpgradeLevel(id) < 1) return false;
        }
        return any;
    }

    private static readonly Color Light = new("9be0f5");   // 光のハイライト
    private static readonly Color Deny = new("ef9a9a");    // 買えない理由（赤）
    private static readonly Color ForkGold = new(0.91f, 0.77f, 0.35f); // 排他フォーク（金）

    // 小話3（ショップの一言）：入店・購入時・退店でミナがぽつりと零す台詞。既存の Toast() で表示するだけ＝
    //   買い物のテンポを邪魔しない短時間表示（1.8秒）。docs/小話集_v1.md §3 の文面をそのまま採用。
    private static readonly string[] ShopEnterTalk =
    {
        "いらっしゃいませ。……冗談です、ご主人様しかいらっしゃいませんもの。",
        "さあ、わたくしを研いでくださいまし。",
        "お財布の中身、ちゃんと確認なさいました?",
        "本日の心の残高、しかとご報告いたします。",
        "急がなくて結構ですよ。ここは、時間が減りませんので。",
        "眺めているだけでも構いません。……買い物は、選んでいる時間がいちばん楽しいので。",
        "ご主人様の趣味が出ますね、この枝の伸ばし方。",
        "ああ、これ。前もそこで迷っておられました。",
    };

    private static readonly string[] ShopBuyTalk =
    {
        "はい、たしかに。……染みますね、これは。",
        "またひとつ、ご主人様の色になりました。",
        "お買い上げ、ありがとうございます。領収書はご入用で?",
        "重くなった気がします。……気のせいですね。",
        "ご主人様、いい買い物です。わたくしが言うのですから間違いありません。",
        "これで、また一歩、遠くまで行けます。",
        "……ふふ。育てられるのは、悪くありませんね。",
        "無駄遣いだったら、あとで責任を取っていただきます。",
        "ありがとうございます。ちゃんと使いますので。",
    };

    private static readonly string[] ShopExitTalk =
    {
        "では、まいりましょう。……お忘れ物はありませんか。",
        "行ってまいります。Stay——でしたね。",
        "支度は済みました。ご主人様の号令をどうぞ。",
        "戻ってきたら、また買い物に付き合ってくださいね。",
        "閉店です。……なんて、看板もないのですけれど。",
        "ご主人様、背筋。",
        "次に来るときは、もう少し稼いでおきます。",
    };

    // 射撃プレビューのミナ立ち絵（右へ撃つポーズ）。毎フレームLoadしないよう_Readyで一度だけキャッシュ。
    private Texture2D? _minaShot;

    // フォーカス：0..2=R0チップ / 3=root（ミナの核） / 4..=NodePos 順のノード。
    private int _sel;

    // ───── 縦横スクロールカメラ（フェーズ2）─────
    //   ツリー領域だけをカメラオフセット _cam（設計座標）でスクロールする。ヘッダ/ストリップ/詳細/フッタは固定。
    //   ツリー描画は専用の子CanvasItem(_treeLayer)へ寄せ、RID クリップで表示窓に収める（固定UIへ漏れない）。
    //   FX（DrawLitEdge/DrawCellFx 等の系統色発光・呼吸・背景モーション）もツリー座標系なので同オフセットで動く。
    private Vector2 _cam;        // 現在のカメラオフセット（設計座標・Lerp で追従）
    private Vector2 _camTarget;  // 追従目標（フォーカスノード中心を表示窓中央へ）
    private Node2D _bgLayer = null!;    // 背景（グラデ/放射光/走査線）専用の子（ZIndex -2＝最背面・固定）。
    private Control _treeLayer = null!; // ツリー描画専用の子（Control.ClipContents で表示窓にクリップ・カメラ適用）。
    private CanvasItem _ci = null!;      // ツリー系ヘルパの描画先（通常は _treeLayer。それ以外は this）。

    // 入力エッジ
    private bool _navHeld, _zHeld, _equipHeld, _backHeld, _trainHeld;
    private double _t, _toastT;
    private string _toast = "";
    private Color _toastCol = UiKit.Info;
    private bool _autoplay;

    // 小話3・退店演出：ExitShop() は即遷移せず「一言トースト→短い遅延」を挟む。二重発火は _exitPending が防ぐ
    //   （ExitShop() が何度呼ばれても最初の1回だけ有効）。
    private bool _exitPending;
    private double _exitDelayT;
    private string _pendingExitDest = "";

    // ───── マウス操作（フェーズ3・キーボード/パッドへ純粋に追加）─────
    //   ノード/チップ/フッタのクリック領域は _Draw で UiKit.Hotspot に登録し、_Process 冒頭で
    //   ホバー/クリックを突合する。ツリーはカメラでスクロールするので、ツリー内ノードは設計座標(1280系)の
    //   マウス位置を「ツリー設計座標」へ逆変換してから NodeRect と突合する（MouseToTree）。
    //   ホットスポット id の割り当て（_sel と同じ番号体系を使い、負の予約帯で固定UIを表す）：
    //     0..2       = R0 チップ（=_sel の 0..2）
    //     3          = root「ミナの核」（=_sel の 3）
    //     4..        = ノード（=_sel の 4.. と同じ＝NodePos 順 +4）
    //     HsBack     = フッタ「もどる/つづける」ボタン
    //     HsBuy      = 詳細パネルの購入/選び直しボタン
    //   後勝ち仕様：ツリーノード（背面寄り）を先に登録し、固定UIボタン（前面）を後に登録する。
    private const int HsBack = -100; // フッタ もどる/つづける
    private const int HsBuy  = -101; // 詳細パネル 購入/選び直し ボタン
    private const int HsTrain = -102; // ヘッダ トレーニング（試し打ち）ボタン
    private Rect2 _trainBtnRect;      // トレーニングボタン矩形（設計座標・_Draw で更新）
    private int _hovId = -1;         // このフレームのホバー id（_Draw で確定 → 次フレーム頭で読む）
    private Rect2 _backBtnRect;      // フッタ もどる ボタン矩形（設計座標・_Draw で更新）
    private Rect2 _buyBtnRect;       // 詳細パネル 購入ボタン矩形（設計座標・_Draw で更新・無効時は空）
    private bool _buyBtnActive;      // 購入ボタンが押せる状態か（表示＆当たり判定の有効フラグ）
    // ホイール手動スクロール中はカメラのフォーカス追従を一時停止する調停（追従がホイールを毎フレーム打ち消すため）。
    //   ホイール入力があったら _wheelHoldT 秒だけ追従を止め、その間は _cam を直接動かす。カーソル移動(Nav)で即再開。
    private double _wheelHoldT;
    // フォーカス追従は「フォーカスが変わった瞬間」だけ ON にし、目標到達で自動的に OFF になる（毎フレーム追従しない）。
    //   これが false の間は _cam を一切動かさない＝手動スクロール（ホイール/バー）や放置でも位置が保持される。
    //   （以前は毎フレーム _sel 目標へ Lerp していたため、_wheelHoldT 失効後に一定時間で先頭へ戻るバグがあった）
    private bool _camFollow;
    private const double WheelHoldSecs = 0.9;   // ホイール後この秒数は追従を抑止
    private const float WheelStep = 90f;        // ホイール1ノッチあたりのスクロール量（設計座標）

    // ───── スクロールバーのマウスドラッグ（横スクロールをマウスだけで全域到達させる主手段）─────
    //   横バー(下端)・縦バー(右端)のサムをドラッグ、またはトラックをクリックで _cam を動かす。
    //   細い見た目のバーは掴みづらいので、当たり判定は太い帯(HitPad)に広げる（見た目は細いまま）。
    //   ドラッグ中はカメラのフォーカス追従を抑止（_wheelHoldT を立て続ける）＝手で持った位置が追従に戻されない。
    //   MouseToTree のノードクリック逆変換は _cam に依存＝バーで _cam.X を動かしても正しく追従する（横ズレしない）。
    private enum DragTarget { None, Horiz, Vert }
    private DragTarget _drag;         // いまドラッグ中のバー
    private bool _dragTookClick;      // このフレームのクリックをバードラッグ開始で消費したか（クリック処理の抑止）
    private float _dragGrab;          // サム内のつかみ位置オフセット（サム左端/上端からマウスまでの距離・設計座標）
    private Rect2 _hBarHit, _vBarHit; // バーの当たり帯（_Draw で更新・トラッククリック/サムドラッグ判定に使う）
    private Rect2 _hThumb, _vThumb;   // サム矩形（見た目＝細い。ドラッグ開始のヒット元）
    private const float BarHitPad = 12f; // 当たり判定を細い見た目の外へ広げる量（掴みやすさ）

    // 演出タイマー
    private double _buyFxT;       // 購入バースト
    private double _walletPopT;   // ウォレットpop
    private string _buyFxId = ""; // 購入したノード（セルの充填グロー）
    private Vector2 _buyFxAt;     // バースト発生源
    private double _sweepT;       // モードスウィープ
    private string _sweepName = "";

    // カプストーン解放パルス（前提が成立した瞬間に一度だけ「解放!」）。
    private readonly HashSet<string> _capSeen = new();
    private string _capPulseId = "";
    private double _capPulseT;

    // 系統コンプリート演出：ある系統の全ノードを買い切った瞬間に一度だけ「◯◯ 完成!」バナー＋
    //   その系統のエッジ束を一斉点灯。入店時に既に完成済みの系統は演出対象外（_streamDone に既知登録）。
    private readonly HashSet<Stream> _streamDone = new();
    private Stream _streamBannerId;
    private double _streamBannerT; // >0 の間バナー＋束点灯

    // 振り直し（排他フォーク単点）の2段確認。封印側ノードで Z → 確認表示 → Z 確定 / X・カーソル移動で取消。
    private bool _respecArmed;
    private string _respecId = ""; // 封印側（＝選び直したい側）のノードID

    public override void _Ready()
    {
        _game = GetNodeOrNull<GameManager>("/root/Game")!;
        if (Audio.Instance != null) Audio.Instance.Music(Audio.Instance.BgmMenu);
        // 起動時、装備中モードのチップ（R0）にカーソルを合わせる。
        //   加速球(index 3)はチップが無い＝チップ範囲(0..2)外なので、その時は連射チップ(0)に置く（root=3 との衝突回避）。
        _sel = System.Array.IndexOf(Modes, _game?.SelectedShotMode ?? GameManager.ShotMode.Rapid);
        if (_sel < 0 || _sel > 2) _sel = 0;

        // 背景専用の子（最背面 ZIndex -2・固定）。Shop 本体(ZIndex 0)は固定UIを描くので、
        // 背景をここに分離しないと ZIndex -1 のツリーが背景に覆われて見えなくなる（レイヤ順の要）。
        _bgLayer = new Node2D { Name = "BgLayer", ZIndex = -2 };
        _bgLayer.Draw += DrawBgLayer;
        AddChild(_bgLayer);

        // ツリー描画専用の子（背景の上 ZIndex -1・固定UIの下）。Control.ClipContents で表示窓に確実にクリップする
        //   （RID クリップは _Draw 描画に効かないため Control の矩形クリップを使う）。位置・サイズは実ピクセル。
        //   ・_Draw を Shop 側の DrawTreeLayer へ委譲（Draw シグナル直結）。カメラオフセット＋design スケールは _Draw 内で。
        float sc = UiKit.Scale;
        _treeLayer = new Control
        {
            Name = "TreeLayer",
            ZIndex = -1,
            ClipContents = true,
            Position = new Vector2(WinL * sc, TreeTop * sc),
            Size = new Vector2((WinR - WinL) * sc, (TreeBot - TreeTop) * sc),
        };
        _treeLayer.Draw += DrawTreeLayer;
        AddChild(_treeLayer);
        // 起動時のカメラは現フォーカス（起動時はチップ＝入口）に合わせて即座に収める＝最上段が見える左上端(0,0)。
        //   Lerp の立ち上がりで枠外から滑り込まないよう、UpdateCamera と同じ目標を初期値にする。
        _camTarget = _cam = _sel >= 4
            ? ClampCam(NodeRect(_sel - 4).GetCenter() - new Vector2((WinR - WinL) / 2f, WinH / 2f) - new Vector2(TreeVirtL, TreeVirtT))
            : Vector2.Zero;
        _minaShot = ResourceLoader.Load<Texture2D>("res://char/mina_shoot.png");
        // 既に前提成立済みのカプストーンは「解放!」パルスの対象外（入店時点の状態を既知とする）。
        foreach (var id in CapstoneIds)
            if (_game?.IsPrereqMet(id) ?? false) _capSeen.Add(id);
        // 入店時点で既に完成している系統は「完成!」バナーの対象外（既知として登録）。
        foreach (Stream s in System.Enum.GetValues<Stream>())
            if (IsStreamComplete(s)) _streamDone.Add(s);
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { _autoplay = true; break; }
        // 小話3：入店時、ミナがぽつりと一言（既存トーストで表示するだけ＝新規UIなし）。
        Toast(ShopEnterTalk[GD.RandRange(0, ShopEnterTalk.Length - 1)], UiKit.Mina);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_toastT > 0) _toastT -= delta;
        if (_buyFxT > 0) _buyFxT -= delta;
        if (_walletPopT > 0) _walletPopT -= delta;
        if (_sweepT > 0) _sweepT -= delta;
        if (_capPulseT > 0) _capPulseT -= delta;
        if (_streamBannerT > 0) _streamBannerT -= delta;
        if (_wheelHoldT > 0) _wheelHoldT -= delta;

        // 小話3・退店演出：ExitShop() が退店トーストを立てたら、実際のシーン遷移までここで待つ。
        //   _autoplay 分岐より前に置くことで、オートプレイでも ExitShop() の毎フレーム再呼び出しが
        //   _exitPending ガードで無害化されつつ、遅延タイマーはちゃんと進む（進行不能にしない）。
        if (_exitPending)
        {
            _exitDelayT -= delta;
            QueueRedraw(); // トーストのフェードだけは動かす
            if (_exitDelayT <= 0) GetTree().ChangeSceneToFile(_pendingExitDest);
            return;
        }
        if (_autoplay) { ExitShop(); return; }

        // 系統コンプリート：この画面内で系統の全ノードが揃った瞬間に一度だけ祝う。
        foreach (Stream s in System.Enum.GetValues<Stream>())
            if (IsStreamComplete(s) && _streamDone.Add(s))
            {
                _streamBannerId = s; _streamBannerT = 2.4;
                Audio.Instance?.PlayUiBuy();
            }

        // マウスホイールで手動スクロール（縦優先。Shift 併用で横）。ホイールがある間はフォーカス追従を抑止し、
        //   _cam を直接動かす（追従が毎フレーム _cam を目標へ引き戻して手動スクロールを打ち消すのを防ぐ）。
        //   MaxCam でクランプ＝端で止まる。UiBlocked（オーバーレイ直後）中はスクロールしない。
        float wheel = Pad.WheelDelta();
        if (wheel != 0f && !Pad.UiBlocked(this) && (MaxCam.X > 1f || MaxCam.Y > 1f))
        {
            bool horiz = Input.IsKeyPressed(Key.Shift) || MaxCam.Y <= 1f; // 縦スクロール不能なら横に振る
            // ホイール上(+)＝上/左へ（cam 減）、下(−)＝下/右へ（cam 増）。手触りは一般的なドキュメントスクロールに合わせる。
            Vector2 d = horiz ? new Vector2(-wheel * WheelStep, 0f) : new Vector2(0f, -wheel * WheelStep);
            _cam = ClampCam(_cam + d);
            _camTarget = _cam;         // 追従の目標も現在地に固定（次フレームの Lerp で戻らない）
            _camFollow = false;        // 手動スクロール＝以後フォーカス追従は次のフォーカス変更まで再開しない
            _wheelHoldT = WheelHoldSecs; // この間はフォーカス追従を止める（保険）
        }

        // スクロールバーのマウスドラッグ（横スクロールをマウスだけで全域到達させる主手段）。UiBlocked 中は不可。
        //   _cam / _wheelHoldT をここで更新するので UpdateCamera より前に呼ぶ。返り値＝このフレームのクリックを
        //   ドラッグ開始として消費したか＝後段の HandleMouseClicks を抑止するフラグ（ノード誤選択を防ぐ）。
        _dragTookClick = !Pad.UiBlocked(this) && HandleScrollbarDrag();

        // カメラ追従：フォーカスノード中心を表示窓中央（≈(430,250)オフセット）へ寄せ、Lerp で滑らかに。
        //   ホイール手動スクロール中・バードラッグ中（_wheelHoldT>0）は追従をスキップ＝手で動かした位置を保つ。
        if (_wheelHoldT <= 0) UpdateCamera((float)delta);

        // カプストーンの前提がこの画面内で成立した瞬間（例：光の出力 Lv2 を購入）に一度だけ解放パルス。
        foreach (var id in CapstoneIds)
            if ((_game?.IsPrereqMet(id) ?? false) && _capSeen.Add(id))
            { _capPulseId = id; _capPulseT = 1.4; }

        // ポーズメニュー（Esc で重なる）を閉じた Esc/Z の同じ押下がこのフレームに漏れて
        // 「もどる＝ショップごと閉じる」「購入」が誤発火しないよう、ゲート中は全キーを既押し扱いで食う。
        if (Pad.UiBlocked(this))
        {
            _navHeld = _zHeld = _equipHeld = _backHeld = true;
            QueueRedraw();
            return;
        }

        // ── マウス クリック（キーボード/パッドへ純粋に追加）──
        //   ライブに当たり判定（HitTest）してからクリック処理する＝描画レジストリの1フレーム遅延に依らず、
        //   マウスが今いる要素に確実に当てる。ツリーノードはカメラ逆変換込みで突合する（HitTest 内）。
        //   スクロールバーのドラッグ開始/継続でクリックを消費したフレームは、ノード/チップの誤選択を避けて抑止する。
        if (!_dragTookClick && _drag == DragTarget.None) HandleMouseClicks();

        // カーソル移動：十字で方向最近傍へ（エッジ接続先を第一候補）。移動で振り直し確認は取り消す。
        bool up = Input.IsActionPressed("ui_up");
        bool down = Input.IsActionPressed("ui_down");
        bool left = Input.IsActionPressed("ui_left");
        bool right = Input.IsActionPressed("ui_right");
        bool any = up || down || left || right;
        if (any && !_navHeld)
        {
            if (up) Nav(0, -1);
            else if (down) Nav(0, 1);
            else if (left) Nav(-1, 0);
            else Nav(1, 0);
            Audio.Instance?.PlayUiMove();
        }
        _navHeld = any;

        // Z：購入（解放/強化）。R0チップ＝装備、root＝説明、封印ノード＝選び直しフロー。
        bool z = Input.IsKeyPressed(Key.Z) || Input.IsActionPressed("ui_accept") || Pad.Pressed(JoyButton.A);
        bool zEdge = z && !_zHeld; _zHeld = z;
        if (zEdge && _t > 0.2) OnConfirm();

        // C：装備。チップ＝そのモード、ノード＝その枝のモード（全モード系は案内トースト）。
        bool c = Input.IsKeyPressed(Key.C) || Pad.Pressed(JoyButton.Y);
        bool cEdge = c && !_equipHeld; _equipHeld = c;
        if (cEdge && _t > 0.2) OnEquipKey();

        // X：振り直し確認中はその取消、通常はショップ退出。
        bool back = Input.IsKeyPressed(Key.X) || Input.IsKeyPressed(Key.Escape) || Pad.Pressed(JoyButton.B);
        bool backEdge = back && !_backHeld; _backHeld = back;
        if (backEdge && _t > 0.2)
        {
            if (_respecArmed) { CancelRespec(); Audio.Instance?.PlayUiCancel(); }
            else { Audio.Instance?.PlayUiCancel(); ExitShop(); }
        }

        // T：トレーニング（試し打ち場）へ。ツリー操作のカーソル移動とは別キー＝住み分け。
        bool train = Input.IsKeyPressed(Key.T);
        bool trainEdge = train && !_trainHeld; _trainHeld = train;
        if (trainEdge && _t > 0.2) EnterTraining();

        QueueRedraw();
    }

    // ───────────────────────── フォーカスとナビ ─────────────────────────
    private static Rect2 NodeRect(int i) => new(NodePos[i].x, NodePos[i].y, NodeW, NodeH);
    private static int SelOf(string id)
    {
        for (int i = 0; i < NodePos.Length; i++)
            if (NodePos[i].id == id) return i + 4;
        return -1;
    }
    private string? FocusNodeId => _sel >= 4 ? NodePos[_sel - 4].id : null;

    private Vector2 FocusCenter(int sel) => sel switch
    {
        <= 2 => ChipRect(sel).GetCenter(),
        3 => RootC,
        _ => NodeRect(sel - 4).GetCenter(),
    };

    // グラフ隣接（エッジ接続先）＝ナビの第一候補。チップは隣チップ、root は系譜の入り口2つ。
    private List<int> LinkedSels(int sel)
    {
        var l = new List<int>();
        if (sel <= 2)
        {
            if (sel > 0) l.Add(sel - 1);
            if (sel < 2) l.Add(sel + 1);
        }
        else if (sel == 3)
        {
            l.Add(SelOf("fire_rate_1"));
            l.Add(SelOf("move_speed_1"));
        }
        else
        {
            string id = NodePos[sel - 4].id;
            var d = GameManager.GetUpgradeDef(id);
            if (d != null)
            {
                l.Add(string.IsNullOrEmpty(d.ParentId) ? 3 : SelOf(d.ParentId));
                if (!string.IsNullOrEmpty(d.ExclusiveWith)) l.Add(SelOf(d.ExclusiveWith));
            }
            foreach (var nd in GameManager.Upgrades)
                if (nd.ParentId == id) l.Add(SelOf(nd.Id));
        }
        l.RemoveAll(v => v < 0);
        return l;
    }

    // 方向最近傍探索：押した方向の半平面から「前進距離＋直交ずれ×1.8」が最小の対象へ。
    // エッジ接続先（親/子/相方/チップ隣）はスコア×0.45 で優先＝木に沿って歩ける。
    private void Nav(int dx, int dy)
    {
        Vector2 from = FocusCenter(_sel);
        var linked = LinkedSels(_sel);
        int best = -1;
        float bestScore = float.MaxValue;
        int count = 4 + NodePos.Length;
        for (int s = 0; s < count; s++)
        {
            if (s == _sel) continue;
            Vector2 d = FocusCenter(s) - from;
            float forward = d.X * dx + d.Y * dy;
            if (forward < 4f) continue;
            float perp = Mathf.Abs(d.X * dy) + Mathf.Abs(d.Y * dx);
            float score = forward + perp * 1.8f;
            if (linked.Contains(s)) score *= 0.45f;
            if (score < bestScore) { bestScore = score; best = s; }
        }
        if (best >= 0) { _sel = best; CancelRespec(); _camFollow = true; } // フォーカス移動＝この時だけ追従を再開
    }

    // ───────────────────────── マウス操作（フェーズ3）─────────────────────────
    // 設計座標(1280系)のマウス位置 → ツリー設計座標へ逆変換。
    //   DrawTreeLayer は design 点(dx,dy)を Control ローカル (dx − cam − TreeVirtL/T)*Scale へ写し、
    //   Control 自体は画面 (WinL,TreeTop)*Scale に置かれる。よって設計座標のマウス mx は
    //     mx = WinL + (dx − cam.X − TreeVirtL)  ⇔  dx = mx − WinL + cam.X + TreeVirtL
    //   （y も同様に TreeTop / TreeVirtT で）。これがカメラ逆変換の肝＝スクロールしても正しいノードに当たる。
    private Vector2 MouseToTree(Vector2 mouseDesign) => new(
        mouseDesign.X - WinL + _cam.X + TreeVirtL,
        mouseDesign.Y - TreeTop + _cam.Y + TreeVirtT);

    // マウス設計座標が表示窓(ツリークリップ矩形)の中にあるか＝窓外(詳細パネル/フッタ側)のノードには当てない。
    private static bool InTreeWindow(Vector2 mouseDesign) =>
        mouseDesign.X >= WinL && mouseDesign.X <= WinR
        && mouseDesign.Y >= TreeTop && mouseDesign.Y <= TreeBot;

    // ライブ当たり判定：マウス設計座標が指す要素の id を返す（無ければ -1）。
    //   優先順は前面→背面：フッタ もどる → 詳細パネル 購入ボタン → チップ/root（固定UI・設計座標そのまま）
    //   → ツリーノード（カメラ逆変換して表示窓内のみ）。id 体系は _sel と同じ＋HsBack/HsBuy の予約帯。
    private int HitTest(Vector2 m)
    {
        // 固定UI（設計座標そのまま）
        if (_backBtnRect.HasPoint(m)) return HsBack;
        if (_buyBtnActive && _buyBtnRect.HasPoint(m)) return HsBuy;
        if (_trainBtnRect.HasPoint(m)) return HsTrain;
        for (int i = 0; i < 3; i++)
            if (ChipRect(i).HasPoint(m)) return i;                 // R0 チップ
        // ツリー（カメラ逆変換）。表示窓の外に出たマウスは当てない（クリップ整合）。
        if (InTreeWindow(m))
        {
            Vector2 tp = MouseToTree(m);
            var rootR = new Rect2(RootC - new Vector2(RootR, RootR), new Vector2(RootR * 2, RootR * 2));
            if (rootR.HasPoint(tp)) return 3;                       // root「ミナの核」
            for (int i = 0; i < NodePos.Length; i++)
                if (NodeRect(i).HasPoint(tp)) return i + 4;         // ノード
        }
        return -1;
    }

    // マウスクリック処理（キーボード/パッドへ純粋に追加）。ライブ HitTest で今指している要素へ確実に当てる。
    //   方式（手触り優先で決定）：
    //     ・チップ（0..2）  … 左クリックでフォーカス移動＋そのモードを装備（EquipMode）。
    //     ・root（3）        … 左クリックでフォーカス移動（説明を詳細パネルに出す）。
    //     ・ノード（4..）    … 1回目クリック＝フォーカス移動（詳細パネル更新）。同じノードをもう一度クリック＝購入。
    //                          既にフォーカス中のノードをクリックした場合は即購入（＝実質ダブルクリック不要の“2度押し購入”）。
    //     ・購入ボタン(HsBuy)… 詳細パネルの明示ボタン。フォーカス中ノードを1クリックで購入（迷いなく買える導線）。
    //     ・もどる(HsBack)  … フッタのボタン。振り直し確認中は取消、通常は退出。
    //   ホバーだけ（クリックなし）でも _hovId を持つのは _Draw 側。ここはクリックエッジ時のみ動く。
    private void HandleMouseClicks()
    {
        if (!Pad.MouseClick()) return;
        if (_t <= 0.2) return; // 入店直後の誤爆防止（キーボードと同じガード）
        int hit = HitTest(Pad.MousePos());
        if (hit == -1) return;

        if (hit == HsBack)
        {
            if (_respecArmed) { CancelRespec(); Audio.Instance?.PlayUiCancel(); }
            else { Audio.Instance?.PlayUiCancel(); ExitShop(); }
            return;
        }
        if (hit == HsBuy)
        {
            // 詳細パネルの購入ボタン＝フォーカス中ノードを確定（購入 or 選び直し）。
            if (_sel >= 4) OnConfirm();
            return;
        }
        if (hit == HsTrain) { EnterTraining(); return; } // ヘッダのトレーニングボタン
        if (hit <= 2) // R0 チップ＝フォーカス＋装備
        {
            _sel = hit; CancelRespec(); _camFollow = true;
            EquipMode(hit);
            return;
        }
        if (hit == 3) // root＝フォーカスのみ（説明）
        {
            if (_sel != 3) { _sel = 3; CancelRespec(); _camFollow = true; Audio.Instance?.PlayUiMove(); }
            else OnConfirm(); // 2度押しで説明トースト（キーボード Z 相当）
            return;
        }
        // ノード：未フォーカスなら選択、既フォーカスなら購入（＝同じノードの2度押しで買う）。
        if (_sel == hit) { OnConfirm(); }
        else { _sel = hit; CancelRespec(); _camFollow = true; Audio.Instance?.PlayUiMove(); }
    }

    // スクロールバーのマウスドラッグ処理（毎フレーム）。横スクロールをマウスだけで全域到達させる主手段。
    //   ・押下エッジ：サムの上なら「つかむ」（グラブ位置を保存）、トラック内サム外なら「そこへジャンプ」して即つかむ。
    //   ・押下中：マウス位置をトラックの割合へ写して _cam を更新（横=_cam.X／縦=_cam.Y）。
    //   ・離した：ドラッグ終了。ドラッグ中は _wheelHoldT を立て続けてカメラ追従を抑止（手で持った位置を保つ）。
    //   バー矩形(_hBarHit/_vBarHit/_hThumb/_vThumb)は直前フレームの DrawScrollBars が更新済み＝それと突合する。
    //   返り値：このフレームのクリックエッジをドラッグ開始として消費したか（true なら HandleMouseClicks を抑止）。
    private bool HandleScrollbarDrag()
    {
        Vector2 m = Pad.MousePos();
        bool consumedClick = false;

        // 押下エッジ：どちらのバーを掴むか決める（横バーを縦バーより優先＝重なり領域は下端の横を拾う）。
        if (Pad.MouseClick() && _t > 0.2 && _drag == DragTarget.None)
        {
            if (MaxCam.X > 1f && _hBarHit.HasPoint(m))
            {
                _drag = DragTarget.Horiz;
                // サム上でつかんだらその相対位置を保持。サム外(トラック)なら中央でつかんだ扱いにしてジャンプ。
                _dragGrab = _hThumb.HasPoint(m) ? m.X - _hThumb.Position.X : HThumbW() / 2f;
                consumedClick = true;
            }
            else if (MaxCam.Y > 1f && _vBarHit.HasPoint(m))
            {
                _drag = DragTarget.Vert;
                _dragGrab = _vThumb.HasPoint(m) ? m.Y - _vThumb.Position.Y : VThumbH() / 2f;
                consumedClick = true;
            }
        }

        if (_drag == DragTarget.None) return consumedClick;

        // 離したら終了。
        if (!Pad.MouseDown()) { _drag = DragTarget.None; return consumedClick; }

        // 押下中：マウス位置 → トラック割合 → _cam。サム長を差し引いた可動域で正規化する（両端で端に張り付く）。
        if (_drag == DragTarget.Horiz)
        {
            float winW = WinR - WinL, thumbW = HThumbW();
            float travel = winW - thumbW;                        // サムが動ける幅
            float thumbX = m.X - _dragGrab;                      // つかみ位置を保ったサム左端
            float t = travel > 0.5f ? Mathf.Clamp((thumbX - WinL) / travel, 0f, 1f) : 0f;
            _cam.X = t * MaxCam.X;
        }
        else // Vert
        {
            float winH = WinH, thumbH = VThumbH();
            float travel = winH - thumbH;
            float thumbY = m.Y - _dragGrab;
            float t = travel > 0.5f ? Mathf.Clamp((thumbY - TreeTop) / travel, 0f, 1f) : 0f;
            _cam.Y = t * MaxCam.Y;
        }
        _cam = ClampCam(_cam);
        _camTarget = _cam;             // 追従目標も現在地へ固定
        _camFollow = false;            // バードラッグ＝以後フォーカス追従は次のフォーカス変更まで再開しない
        _wheelHoldT = WheelHoldSecs;   // ドラッグ中は追従抑止を延長し続ける（離してもしばらく戻さない・保険）
        return consumedClick;
    }

    // ショップ退出先：初回ショップ導線で復帰先(PendingResumeScene)が立っていれば、ハブでなくそのステージへ戻り
    // “中ボスの続き”から再開する（消費して以降は通常どおりハブへ）。それ以外は従来どおりハブ。
    private void ExitShop()
    {
        if (_exitPending) return; // 二重発火ガード（連打・オートプレイの毎フレーム呼び出し対策）
        var game = GetNodeOrNull<GameManager>("/root/Game");
        string dest = "res://Hub.tscn";
        if (game != null && !string.IsNullOrEmpty(game.PendingResumeScene))
        {
            dest = game.PendingResumeScene!;
            game.PendingResumeScene = null; // 消費
        }
        // 小話3：退店の一言を見せてから、短い遅延の後に実際のシーン遷移（_Process 側で処理）。
        Toast(ShopExitTalk[GD.RandRange(0, ShopExitTalk.Length - 1)], UiKit.Mina);
        _pendingExitDest = dest;
        _exitDelayT = 0.8;
        _exitPending = true;
    }

    // トレーニング（試し打ち場）へ。スキルを無料で付け外しして撃ち味を数値で比べる。
    //   本番状態（通貨/所持強化/装備モード/フォロワー）は TrainingRoot が退避→復元＝ここでは何も汚さない。
    //   戻りは TrainingRoot が res://Shop.tscn へ直接戻す（PendingResumeScene は使わない＝もどるのループ回避）。
    private void EnterTraining()
    {
        Audio.Instance?.PlayUiConfirm();
        GetTree().ChangeSceneToFile("res://Training.tscn");
    }

    private void OnConfirm()
    {
        if (_sel <= 2) { EquipMode(_sel); return; } // R0 チップ＝装備
        if (_sel == 3) { Toast("ここからすべてが伸びます。線でつながった隣から買えますわ", UiKit.Info); return; }
        string id = NodePos[_sel - 4].id;
        // 封印中は購入の代わりに「選び直す？」フロー（2段確認）。
        if (_game?.IsSealed(id) ?? false) { RespecStep(id); return; }
        Buy(id, NodeRect(_sel - 4).GetCenter());
    }

    // 振り直しの2段確認。1回目のZ＝確認表示（詳細パネルに差引きプレビュー）、2回目のZ＝確定。
    private void RespecStep(string id)
    {
        var d = GameManager.GetUpgradeDef(id);
        if (d == null || _game == null || string.IsNullOrEmpty(d.ExclusiveWith)) return;
        if (!_respecArmed || _respecId != id)
        {
            _respecArmed = true;
            _respecId = id;
            Audio.Instance?.PlayUiMove();
            return;
        }
        // 確定：対の両ノードを Lv0 に戻し、返金−手数料をウォレットへ（RunImpression には足さない）。
        long refund = _game.RespecRefund(id, d.ExclusiveWith);
        long fee = _game.RespecFee(id, d.ExclusiveWith);
        if (_game.TryRespec(id, d.ExclusiveWith))
        {
            Audio.Instance?.PlayUiBuy();
            Toast($"選び直しました　＋♥{refund - fee:N0}（返金 ♥{refund:N0} − 手数料 ♥{fee:N0}）", UiKit.Info);
            _walletPopT = 0.5;
        }
        CancelRespec();
    }

    private void CancelRespec() { _respecArmed = false; _respecId = ""; }

    private void Buy(string id, Vector2 at)
    {
        var d = GameManager.GetUpgradeDef(id);
        if (d == null || _game == null) return;
        int lv = _game.GetUpgradeLevel(id);
        if (lv >= d.MaxLevel) { Audio.Instance?.PlayUiDeny(); Toast("すでに最大です", UiKit.Text4); return; }
        // 親未接続（ノードに入るときだけ）：どこから伸ばすかを明示して拒否。
        if (!_game.IsParentMet(id))
        {
            string pn = GameManager.GetUpgradeDef(d.ParentId)?.Name ?? "ミナの核";
            Audio.Instance?.PlayUiDeny(); Toast($"まず {pn} を Lv1 に（線でつながった親から）", Deny); return;
        }
        // 前提未達（奥義のみ）：理由を明示して拒否。所持済み Lv には触れない（グランドファーザー規則）。
        if (!_game.IsPrereqMet(id))
        {
            string pn = GameManager.GetUpgradeDef(d.PrereqId)?.Name ?? d.PrereqId;
            Audio.Instance?.PlayUiDeny(); Toast($"前提: {pn} Lv{d.PrereqLv} が必要です", Deny); return;
        }
        if (!_game.CanPurchase(id)) { Audio.Instance?.PlayUiDeny(); Toast("浄化した心が足りません", Deny); return; }
        if (_game.TryPurchase(id))
        {
            Audio.Instance?.PlayUiBuy(); // 購入成功＝達成音
            // 各系統の入り口ノード（spread_1/homing_1/accel_1）は「解放」、それ以外は「強化」。
            bool isUnlock = id == "spread_1" || id == "homing_1" || id == "accel_1";
            // 小話3：低頻度（約25%）で強化確認トーストの代わりにミナの一言を出す。買い物のテンポを崩さないよう
            //   毎回は出さない（頻発すると邪魔）。強化確認自体は毎回のフィードバックとして残す＝置き換えのみ。
            if (GD.Randf() < 0.25f) Toast(ShopBuyTalk[GD.RandRange(0, ShopBuyTalk.Length - 1)], UiKit.Mina);
            else Toast($"{d.Name} を{(isUnlock ? "解放" : "強化")}！", UiKit.Info);
            _buyFxT = 0.7; _walletPopT = 0.5; _buyFxId = id; _buyFxAt = at;
            // 拡散/ホーミング/加速球を解放したら自動で装備に切り替える（従来挙動を踏襲）。
            if (id == "spread_1") EquipMode(1, silent: true);
            if (id == "homing_1") EquipMode(2, silent: true);
            if (id == "accel_1") EquipMode(3, silent: true);
        }
    }

    // C装備（作り直し）：R0チップ＝そのモード。ノードは「モードの起点ノード」だけが装備入口。
    //   起点ノード（fire_rate_1=連射 / spread_1=拡散 / homing_1=ホーミング）以外では、
    //   どこで装備するかを具体案内する（従来の PreviewModeFor 逆引きの無反応バグを解消）。
    private void OnEquipKey()
    {
        if (_sel <= 2) { EquipMode(_sel); return; }
        int m = IsModeEquipNode(FocusNodeId);
        if (m >= 0) { EquipMode(m); return; }
        // 起点ノードでない：装備の入口（起点ノード or 上のチップ）へ誘導。
        Audio.Instance?.PlayUiDeny();
        Toast("装備はモードの起点ノード（連射速度I／拡散展開I／誘導の祈りI）か上のチップで", UiKit.Text4);
    }

    // モードの起点ノードか（そのノード上で C＝そのモード装備）。起点でなければ -1。
    private static int IsModeEquipNode(string? id) => id switch
    {
        "fire_rate_1" => 0, // 連射
        "spread_1" => 1,    // 拡散
        "homing_1" => 2,    // ホーミング
        "accel_1" => 3,     // 加速球
        _ => -1,
    };

    private void EquipMode(int idx, bool silent = false)
    {
        var m = Modes[idx];
        if (!(_game?.IsModeUnlocked(m) ?? false)) { if (!silent) { Audio.Instance?.PlayUiDeny(); Toast("まだ解放されていません（枝の入り口で解放）", UiKit.Text4); } return; }
        // 既に装備中のモードで C＝「装備中」を明示（従来の無音 return ＝“反応しない”の解消）。
        if (_game!.SelectedShotMode == m)
        {
            if (!silent) { Audio.Instance?.PlayUiMove(); Toast($"{_game.ShotModeName(m)} は装備中です", UiKit.Info); }
            return;
        }
        if (!silent) Audio.Instance?.PlayUiConfirm(); // 装備＝決定音
        _game.SelectedShotMode = m;
        _sweepName = _game.ShotModeName(m);
        _sweepT = 1.1;
    }

    private void Toast(string msg, Color col) { _toast = msg; _toastCol = col; _toastT = 1.8; }

    // ノード → その枝のショットモード（詳細プレビューと C 装備用。-1＝モード固有でない＝装備中を映す）。
    private static int PreviewModeFor(string? id) => id switch
    {
        _ when id == null => -1,
        // 0=連射：連射速度・光の出力・連射威力/速射・貫通・集中。
        _ when id.StartsWith("fire_rate") || id.StartsWith("shot_power") || id.StartsWith("rapid_")
            || id.StartsWith("pierce") || id.StartsWith("focus") => 0,
        // 1=拡散：拡散展開/威力/速射・拡散力・オプション・連鎖。
        _ when id.StartsWith("spread") || id.StartsWith("fol_gain")
            || id.StartsWith("option") || id.StartsWith("chain") => 1,
        // 2=ホーミング：誘導・誘導威力/速射・返し光・帳。
        _ when id.StartsWith("homing") || id.StartsWith("counter") || id.StartsWith("veil") => 2,
        // 3=加速球：加速球解放ノード。
        _ when id.StartsWith("accel") => 3,
        _ => -1, // 全モード共通（生存・経済・コンボ・バックファイア）は装備中を映す
    };

    // ───────────────────────── レイアウト座標 ─────────────────────────
    private const float PadX = 40f;
    private const float StripY = 96f, StripH = 42f;     // R0 モードストリップ
    private const float DetailX = 870f, DetailW = 370f; // 詳細パネル x870-1240
    private const float TreeTop = 146f, TreeBot = 652f; // ツリー領域（表示窓）y146-652

    // ───── スクロール表示窓・仮想ツリー範囲・カメラ上限（設計座標）─────
    //   表示窓：x[WinL,WinR]・y[TreeTop,TreeBot]（幅826×高506）。詳細パネル(x870)より左で切る＝固定UIへ漏れない。
    //   仮想ツリーの左上端(TreeVirtL,TreeVirtT)〜右下端(TreeVirtR,TreeVirtB)を NodePos＋ルートの実端＋余白から算出。
    //   カメラの原点(cam=0)は仮想ツリー左上端＝ノード最上段が表示窓の中に完全に収まる位置（ストリップ下に潜らない）。
    //   MaxCam：仮想ツリーの右下端が表示窓の右下に来る量＝(仮想右-WinR - 仮想左, 仮想下-窓高 - 仮想上)を（0以上に）クランプ。
    private const float WinL = 24f, WinR = 850f;                    // 表示窓の左右（設計座標）
    private const float WinH = TreeBot - TreeTop;                   // 表示窓の高さ（設計座標）
    // 仮想ツリー範囲＝NodePos の実端＋余白から算出（座標を変えればカメラ上限も自動追従する）。
    //   ルート「ミナの核」も左上端候補に含める。ハードコードだと座標変更でスクロール外に取り残されるので必ず実端依存に。
    private static readonly Vector4 TreeVirt = ComputeTreeVirt();   // (左, 上, 右, 下)
    private static float TreeVirtL => TreeVirt.X;
    private static float TreeVirtT => TreeVirt.Y;                   // 仮想ツリー上端（＝cam.Y=0 の基準）
    private static float TreeVirtR => TreeVirt.Z;                   // 仮想ツリー右端
    private static float TreeVirtB => TreeVirt.W;                   // 仮想ツリー下端
    private const float TreeMargin = 18f;                           // 仮想端に付ける余白（最上段のヘッドルーム含む）
    private static Vector4 ComputeTreeVirt()
    {
        float l = RootC.X - RootR, t = RootC.Y - RootR, r = RootC.X + RootR, b = RootC.Y + RootR;
        foreach (var (_, x, y) in NodePos)
        {
            l = Mathf.Min(l, x);
            t = Mathf.Min(t, y);
            r = Mathf.Max(r, x + NodeW);
            b = Mathf.Max(b, y + NodeH);
        }
        return new Vector4(l - TreeMargin, t - TreeMargin, r + TreeMargin, b + TreeMargin);
    }
    // カメラ可動幅：仮想ツリーが表示窓（WinR-WinL 幅 / WinH 高）に対してはみ出す量。0未満は0（スクロール不要）。
    private static readonly Vector2 MaxCam = new(
        Mathf.Max(0f, (TreeVirtR - TreeVirtL) - (WinR - WinL)),
        Mathf.Max(0f, (TreeVirtB - TreeVirtT) - WinH));

    // カメラを [0, MaxCam] に収める（負や仮想範囲超えのオフセットを禁止＝端で止まる）。
    private static Vector2 ClampCam(Vector2 c) =>
        new(Mathf.Clamp(c.X, 0f, MaxCam.X), Mathf.Clamp(c.Y, 0f, MaxCam.Y));

    // カメラ追従：フォーカスがチップ/root/ノードのどれでも、その中心を表示窓中央へ寄せる目標を作り Lerp。
    //   チップ(R0)や root は上部固定域なので、カメラは左上端(0,0)へ戻す＝ツリーの入口(d1)が見える。
    //   ★追従は _camFollow が true の間だけ（フォーカス変更で ON）。目標へ十分寄ったら自動 OFF＝以後は _cam を触らない。
    //     これにより、手動スクロール後や放置中に「一定時間で先頭へ戻る」不具合を根絶（追従が現在地を打ち消さない）。
    private void UpdateCamera(float delta)
    {
        if (!_camFollow) return; // フォーカス未変更なら位置を保持（毎フレーム目標へ引き戻さない）
        if (_sel >= 4)
        {
            Vector2 center = NodeRect(_sel - 4).GetCenter();
            // フォーカスノード中心を表示窓中央へ。cam = ノード中心 − 窓中央（design） − 仮想左上端。
            //   （transform が design→ローカルで cam＋仮想左上端を引くので、その分をここで足し戻す）。
            Vector2 winCenter = new((WinR - WinL) / 2f, WinH / 2f);
            _camTarget = ClampCam(center - winCenter - new Vector2(TreeVirtL, TreeVirtT));
        }
        else
        {
            _camTarget = Vector2.Zero; // チップ/root にいる間は入口（左上）を映す＝最上段が余白ぶん下がって全部見える
        }
        _cam = _cam.Lerp(_camTarget, Mathf.Clamp(12f * delta, 0f, 1f));
        if (_cam.DistanceTo(_camTarget) < 0.5f) { _cam = _camTarget; _camFollow = false; } // 到達したら追従終了
    }

    // R0 装備チップの矩形（幅は名前の実幅から。ストリップ描画とフォーカス枠が共有する）。
    private float ChipW(int i) => 34f + UiKit.TextW(UiKit.ZenBold, _game?.ShotModeName(Modes[i]) ?? "", 14);
    private Rect2 ChipRect(int i)
    {
        float cx = PadX + 132f;
        for (int k = 0; k < i; k++) cx += ChipW(k) + 8f;
        return new Rect2(cx, StripY + 7f, ChipW(i), StripH - 14f);
    }

    // ───────────────────────── 描画 ─────────────────────────
    public override void _Draw()
    {
        _ci = this; // 固定UI（ヘッダ/ストリップ/詳細/フッタ）は Shop 本体へ描く（カメラ非適用）。
        UiKit.BeginDesign(this);
        _recommended = RecommendedNow();
        RebuildFrontier(); // フロンティア強調（祖先フォールバック込み）を毎フレーム確定

        // ── マウス ホバー判定（フェーズ3）──
        //   ライブ HitTest（カメラ逆変換込み）で今マウスが指す要素の id をこのフレーム分だけ確定する。
        //   固定UI（チップ/root/購入/もどる）は UiKit のホットスポットレジストリにも登録し、突合を二重化しておく
        //   （ツリーノードはスクロールで座標が動く＝レジストリの平坦な設計座標に乗らないため HitTest 一本で判定する）。
        UiKit.BeginHotspots(Pad.MousePos());
        _hovId = HitTest(Pad.MousePos());

        // 背景（最背面 -2）とツリー本体（-1・クリップ）は専用の子が描く＝毎フレーム再描画を要求。
        //   レイヤ順：背景(-2) → ツリー(-1) → 固定UI(このShop本体・0)。
        _bgLayer.QueueRedraw();
        _treeLayer.QueueRedraw();

        DrawHeader();
        DrawModeStrip();
        DrawDetailPanel();
        DrawScrollBars(); // 横位置バー＋縦の「まだ下にある」矢印（固定UI）

        // フッタ操作ヒント（ボタン表記は Pad に集約＝KB/PS/Xbox 切替に追従）。過熱削除で4項目に整理。
        //   「もどる/つづける」はマウスでもクリック可＝そのヒント矩形を _backBtnRect に保存してホットスポット化する。
        float fy = H - 34f, fx = PadX;
        fx = Hint(fx, fy, Pad.MoveToken, "えらぶ", false);
        fx = Hint(fx, fy, Pad.ConfirmToken, "購入", true);
        fx = Hint(fx, fy, Pad.EquipToken, "装備", false);
        // 初回ショップ導線で復帰先がある間は、退出＝ステージの続きへ＝「つづける」表記にする。
        bool resuming = !string.IsNullOrEmpty(GetNodeOrNull<GameManager>("/root/Game")?.PendingResumeScene);
        string backLbl = resuming ? "つづける" : "もどる";
        float backEndX = Hint(fx, fy, Pad.CancelToken, backLbl, false);
        // もどるヒントの当たり矩形（キーキャップ左端〜ラベル右端、上下に余裕）。_backBtnRect＝クリック領域。
        _backBtnRect = new Rect2(fx - 6f, fy - 20f, (backEndX - 22f) - fx + 12f, 30f);
        bool backHov = _hovId == HsBack;
        if (backHov) UiKit.Box(this, _backBtnRect, new Color(UiKit.Info, 0.10f), 6f, new Color(UiKit.Info, 0.5f), 1f);
        UiKit.Hotspot(_backBtnRect, HsBack);

        DrawModeSweep();
        DrawStreamBanner();
        DrawToast();
        UiKit.EndDesign(this);
    }

    // 系統コンプリート・バナー（固定UI・中央上寄り）。系統色のリボン＋「◯◯系 コンプリート!」＋星の輪。
    //   立ち上がり→保持→抜けの3段でフェード。エッジ束の総点灯（DrawLitEdge 側）と同時に出す＝達成の総まとめ。
    private void DrawStreamBanner()
    {
        if (_streamBannerT <= 0) return;
        float k = 1f - (float)(_streamBannerT / 2.4);           // 0→1
        float a = k < 0.14f ? k / 0.14f : (k > 0.82f ? (1f - k) / 0.18f : 1f);
        Color sc = StreamCol[(int)_streamBannerId];
        string name = StreamName[(int)_streamBannerId];
        string t = name + "系 コンプリート！";
        float tw = UiKit.TextW(UiKit.ZenBlack, t, 30) + 96;
        float bx = W / 2f - tw / 2f, by = 250f - 8f * (1f - Mathf.Min(1f, k * 3f)); // わずかに落ちてくる
        UiKit.RadialGlow(this, new Vector2(W / 2f, by + 30f), tw * 0.7f, sc, 0.28f * a);
        UiKit.Box(this, new Rect2(bx, by, tw, 62f), new Color(0.06f, 0.05f, 0.10f, 0.94f * a), 18f, new Color(sc, 0.85f * a), 1.6f);
        UiKit.Text(this, UiKit.Mono, new Vector2(bx, by + 8f), "STREAM COMPLETE", 10, new Color(sc, 0.7f * a), HorizontalAlignment.Center, tw);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(bx, by + 24f), t, 30, new Color(sc.Lerp(UiKit.White, 0.35f), a), HorizontalAlignment.Center, tw);
        // 星の輪（回転しながら広がる）
        int n = 12;
        float rr = 44f + 34f * k;
        for (int i = 0; i < n; i++)
        {
            float ang = Mathf.Tau * i / n + (float)_t * 1.4f;
            Vector2 p = new(W / 2f + Mathf.Cos(ang) * (tw * 0.5f + rr), by + 30f + Mathf.Sin(ang) * (36f + rr * 0.4f));
            DrawStar(p, 3.4f * (1f - k * 0.4f), new Color(sc.Lerp(UiKit.White, 0.5f), a * (0.4f + 0.6f * (1f - k))));
        }
    }

    // 小さな4方向きらめき星（ダイヤ十字）。固定UI（this）に描く。凹多角形を避け菱形2枚で描く。
    private void DrawStar(Vector2 c, float s, Color col)
    {
        DrawColoredPolygon(new[] { c + new Vector2(0, -s), c + new Vector2(s * 0.32f, 0), c + new Vector2(0, s), c + new Vector2(-s * 0.32f, 0) }, col);
        DrawColoredPolygon(new[] { c + new Vector2(-s, 0), c + new Vector2(0, -s * 0.32f), c + new Vector2(s, 0), c + new Vector2(0, s * 0.32f) }, col);
    }

    // 背景専用の子（_bgLayer・最背面）の _Draw 委譲先。グラデ＋放射光＋走査線（固定・カメラ非適用）。
    private void DrawBgLayer()
    {
        _bgLayer.DrawSetTransform(Vector2.Zero, 0f, new Vector2(UiKit.Scale, UiKit.Scale));
        UiKit.VGradient(_bgLayer, new Rect2(0, 0, W, H),
            new[] { new Color("0d0b1c"), new Color("0a0916"), new Color("070611") }, new[] { 0f, 0.55f, 1f });
        float bgBreath = 0.10f + 0.03f * Mathf.Sin((float)_t * 0.6f);
        UiKit.RadialGlow(_bgLayer, new Vector2(W * 0.12f, H * 0.42f), 460f, UiKit.Info, bgBreath);
        for (float y = 0; y < H; y += 6f) _bgLayer.DrawRect(new Rect2(0, y, W, 1f), new Color(0, 0, 0, 0.05f));
        _bgLayer.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    // ツリー描画専用の子（_treeLayer）の _Draw 委譲先。design スケール＋カメラオフセットで座標系を作り、
    //   背景モーション（BgMotes）→ エッジ → ルート → ノードセルの順に描く。FX もこの座標系＝カメラで動く。
    //   クリップ矩形（_Ready で設定）が表示窓外への漏れを止める。窓外ノード/エッジは描画スキップで負荷も抑える。
    private void DrawTreeLayer()
    {
        _ci = _treeLayer;
        // Control ローカル原点＝表示窓の左上（画面 (WinL,TreeTop)*Scale）。design 点(dx,dy)を
        //   ローカル (dx - cam - 仮想左上)*Scale へ写す＝cam=0 で仮想ツリー左上端が窓左上に来る
        //   （＝最上段ノードが余白ぶん下がって表示窓に完全に収まり、ストリップ下に潜らない）。
        Vector2 off = -(_cam + new Vector2(TreeVirtL, TreeVirtT)) * UiKit.Scale;
        _treeLayer.DrawSetTransform(off, 0f, new Vector2(UiKit.Scale, UiKit.Scale));
        DrawBgMotes();
        DrawTree();
        DrawBuyFx(); // 購入バーストはノード位置＝ツリー座標で光る
        _treeLayer.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    // ノードが表示窓（＋余白）に触れているか。ローカル座標（design 換算・窓左上=0）で判定＝窓外は描画スキップ。
    //   local = design − cam − 仮想左上端。窓は [0, WinW]×[0, WinH]（±40px 余白）。
    private bool NodeVisible(Rect2 r)
    {
        float lx = r.Position.X - _cam.X - TreeVirtL, ly = r.Position.Y - _cam.Y - TreeVirtT;
        float winW = WinR - WinL;
        return lx + r.Size.X >= -40f && lx <= winW + 40f
            && ly + r.Size.Y >= -40f && ly <= WinH + 40f;
    }

    // エッジ（L字経路）が表示窓に触れているか。経路は from→to のバウンディングボックス内に収まる
    //（DrawElbow/DrawLitEdge は水平→垂直→水平の配線＝箱の外に出ない）ので、箱と窓の交差で判定する。
    //   帯をまたぐ長いエッジ（ミナの核→身のこなし等）は両端が窓外でも中間が窓を横切る＝この判定で正しく描かれる。
    private bool EdgeVisible(Vector2 from, Vector2 to)
    {
        var box = new Rect2(new Vector2(Mathf.Min(from.X, to.X), Mathf.Min(from.Y, to.Y)),
                            new Vector2(Mathf.Abs(to.X - from.X), Mathf.Abs(to.Y - from.Y)));
        return NodeVisible(box); // 同じローカル変換＋±40px余白の窓交差テストを流用
    }

    // ───── スクロール位置インジケータ（固定UI・カメラ非適用）─────
    //   横：表示窓の下端に細いトラック＋サム（_cam.X/MaxCam.X）＝「まだ右にある」を可視化。
    //   縦：表示窓の右端内側に縦トラック＋サム（_cam.Y/MaxCam.Y）＝「まだ下にある」を可視化。横バーと視覚を揃える。
    //   サムの見た目（太さ6・角丸3・Info半透明）とトラック（幅4・8%白）は縦横で共通。
    private const float BarThick = 4f, ThumbThick = 6f; // トラック太さ / サム太さ（縦横共通）
    // トラック(見た目の細い帯)の基準座標＝ドラッグ計算と描画で同じ式を使うため定数化。
    private const float HTrackY = TreeBot + 6f;   // 横バー トラックの y（見た目）
    private const float VTrackX = WinR - 6f;      // 縦バー トラックの x（見た目）
    private void DrawScrollBars()
    {
        float winW = WinR - WinL, winH = WinH;

        // 横位置バー（表示窓下端）。トラック＋サム。見えている横割合でサム長を決める。ドラッグ可（当たり帯を上下に拡張）。
        UiKit.Box(this, new Rect2(WinL, HTrackY, winW, BarThick), new Color(1, 1, 1, 0.08f), 2f);
        _hBarHit = _hThumb = new Rect2(); // 既定は空（スクロール不能なら掴めない）
        if (MaxCam.X > 1f)
        {
            float thumbW = HThumbW();
            float t = Mathf.Clamp(_cam.X / MaxCam.X, 0f, 1f);
            float thumbX = WinL + t * (winW - thumbW);
            _hThumb = new Rect2(thumbX, HTrackY - 1f, thumbW, ThumbThick);
            _hBarHit = new Rect2(WinL, HTrackY - BarHitPad, winW, ThumbThick + BarHitPad * 2f); // 当たり帯（トラック全長×太め）
            bool active = _drag == DragTarget.Horiz;
            bool hov = active || (_drag == DragTarget.None && _hBarHit.HasPoint(Pad.MousePos()));
            UiKit.Box(this, _hThumb, new Color(UiKit.Info, hov ? 0.85f : 0.55f), 3f);
            if (hov) UiKit.Box(this, _hThumb.Grow(1.5f), null, 4f, new Color(UiKit.PurifyHi, 0.6f), 1f);
        }

        // 縦位置バー（表示窓の右端内側 x≈844）。詳細パネル(x870)と被らない位置。トラック＋サム。ドラッグ可。
        UiKit.Box(this, new Rect2(VTrackX, TreeTop, BarThick, winH), new Color(1, 1, 1, 0.08f), 2f);
        _vBarHit = _vThumb = new Rect2();
        if (MaxCam.Y > 1f)
        {
            float thumbH = VThumbH();
            float t = Mathf.Clamp(_cam.Y / MaxCam.Y, 0f, 1f);
            float thumbY = TreeTop + t * (winH - thumbH);
            _vThumb = new Rect2(VTrackX - 1f, thumbY, ThumbThick, thumbH);
            _vBarHit = new Rect2(VTrackX - BarHitPad, TreeTop, ThumbThick + BarHitPad * 2f, winH);
            bool active = _drag == DragTarget.Vert;
            bool hov = active || (_drag == DragTarget.None && _vBarHit.HasPoint(Pad.MousePos()));
            UiKit.Box(this, _vThumb, new Color(UiKit.Info, hov ? 0.85f : 0.55f), 3f);
            if (hov) UiKit.Box(this, _vThumb.Grow(1.5f), null, 4f, new Color(UiKit.PurifyHi, 0.6f), 1f);
        }
    }

    // サム長（横/縦）＝見えている割合×トラック長。ドラッグ計算(位置→cam)と描画で同じ値を共有する。
    private static float HThumbW() => (WinR - WinL) * Mathf.Clamp((WinR - WinL) / (TreeVirtR - TreeVirtL), 0.12f, 1f);
    private static float VThumbH() => WinH * Mathf.Clamp(WinH / (TreeVirtB - TreeVirtT), 0.12f, 1f);

    private void DrawHeader()
    {
        UiKit.Text(this, UiKit.Mono, new Vector2(PadX, 22), "SHOT UPGRADE SYSTEM", 11, UiKit.Text3);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(PadX, 36), "弾・ショット強化システム", 28, UiKit.White);
        UiKit.Text(this, UiKit.Zen, new Vector2(PadX, 72), "ミナの核から枝を伸ばして育てる。連射・拡散・誘導、後方の光——好きな順で、いつかすべて。", 13, UiKit.Text2);

        // 長期目標（LUNATIC解放）＝「何のために稼ぐか」の遠い灯り。条件は GameManager.IsLunaticUnlocked
        //（フォロワー200 or 光の出力Lv4＝ツリー側にも王冠マークで重ねる）。解放済みなら出さない。
        if (_game != null && !_game.IsLunaticUnlocked)
        {
            string goal = $"LUNATIC解放まで: フォロワー {_game.Followers}/{GameManager.LunaticFollowerReq} ／ 光の出力 Lv{_game.ChainLevel("shot_power", 4)}/4";
            UiKit.Text(this, UiKit.Zen, new Vector2(W - PadX - UiKit.TextW(UiKit.Zen, goal, 11), 80), goal, 11, new Color("c9b6ef"));
        }

        // ウォレット（右）
        long imp = _game?.Impression ?? 0;
        string impS = imp.ToString("N0");
        float popA = _walletPopT > 0 ? (float)(_walletPopT / 0.5) : 0f;
        int impSize = 24 + Mathf.RoundToInt(3f * popA);
        float numW = UiKit.TextW(UiKit.Mono, impS, impSize);
        // ラベル実幅から pill 幅を算出＝「浄化した心」と数値が桁伸びでも衝突しないよう動的化。
        float lblW = UiKit.TextW(UiKit.Zen, "浄化した心", 12);
        float pillW = 16f + 16f + 6f + lblW + 16f + numW + 18f;
        float pillX = W - PadX - pillW, pillY = 30f;
        UiKit.Box(this, new Rect2(pillX, pillY, pillW, 44f), new Color(232 / 255f, 196 / 255f, 90 / 255f, 0.1f), 14f, new Color(UiKit.Gold, 0.4f), 1f);
        DrawCircle(new Vector2(pillX + 22, pillY + 22), 8f, UiKit.Gold);
        UiKit.Text(this, UiKit.Zen, new Vector2(pillX + 38, pillY + 14), "浄化した心", 12, new Color("f0d98a"));
        if (popA > 0) UiKit.RadialGlow(this, new Vector2(pillX + pillW - 24 - numW / 2f, pillY + 22), 50f, UiKit.Gold, 0.45f * popA);
        Color impCol = new Color("f0d98a").Lerp(UiKit.White, popA);
        UiKit.Text(this, UiKit.Mono, new Vector2(pillX + pillW - 18 - numW, pillY + 22 - impSize / 2f - popA * 1.5f), impS, impSize, impCol);

        // ── トレーニング（試し打ち）ボタン：ウォレットの左。装備チップとは意味が違う（強化ではなく“試す場”）ので
        //    色・位置・アイコンで明確に別物として置く（シアン枠＋的アイコン）。クリック or T キーで入る。
        const float tbW = 156f, tbH = 34f;
        float tbX = pillX - 14f - tbW, tbY = 35f;
        _trainBtnRect = new Rect2(tbX, tbY, tbW, tbH);
        bool trainHov = _hovId == HsTrain;
        UiKit.Box(this, _trainBtnRect, new Color(UiKit.Info, trainHov ? 0.22f : 0.10f), 10f, new Color(UiKit.Info, trainHov ? 0.85f : 0.5f), 1.4f);
        // 的アイコン（同心円）＝“試し打ち”の記号。
        Vector2 tic = new(tbX + 20f, tbY + tbH / 2f);
        DrawArc(tic, 8f, 0, Mathf.Tau, 20, new Color(UiKit.Info, 0.9f), 1.4f);
        DrawCircle(tic, 2.6f, new Color(1f, 0.3f, 0.4f));
        UiKit.Text(this, UiKit.ZenBold, new Vector2(tbX + 36f, tbY + tbH / 2f - 9), "トレーニング", 14, UiKit.White);
        UiKit.Text(this, UiKit.Mono, new Vector2(tbX + 36f, tbY + tbH / 2f + 4), "T / CLICK", 8, new Color(UiKit.Info, 0.7f));
        UiKit.Hotspot(_trainBtnRect, HsTrain);
    }

    // R0：装備チップ（フォーカス可能）。装備は Z/C の1押し。過熱トグルは撤去し、右側に「装備中モード」を表示。
    private void DrawModeStrip()
    {
        float x = PadX, y = StripY, w = W - PadX * 2, h = StripH;
        UiKit.Box(this, new Rect2(x, y, w, h), new Color(15 / 255f, 11 / 255f, 26 / 255f, 0.7f), 13f, new Color(1, 1, 1, 0.1f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x + 16, y + h / 2f - 8), "ショットモード", 13, UiKit.Text2);

        for (int i = 0; i < 3; i++)
        {
            var m = Modes[i];
            bool unlocked = _game?.IsModeUnlocked(m) ?? false;
            bool equipped = _game?.SelectedShotMode == m;
            bool focus = _sel == i;
            string name = _game?.ShotModeName(m) ?? "";
            var r = ChipRect(i);
            if (equipped) UiKit.Box(this, r, new Color(UiKit.Info, 0.22f), 999f, UiKit.Info, 1.2f);
            else UiKit.Box(this, r, new Color(1, 1, 1, 0.05f), 999f, new Color(1, 1, 1, unlocked ? 0.12f : 0.06f), 1f);
            // マウスホバー：フォーカスでない被ホバーのチップを淡く縁取り＝クリック先が分かる（フォーカスは下で強く描く）。
            if (!focus && _hovId == i) UiKit.Box(this, r.Grow(2f), null, 999f, new Color(UiKit.PurifyHi, 0.6f), 1.4f);
            UiKit.Hotspot(r, i);
            if (focus) UiKit.Box(this, r.Grow(3f), null, 999f, new Color(UiKit.Info, 0.85f), 1.6f);
            DrawModeIcon(new Vector2(r.Position.X + 15, y + h / 2f), i, unlocked ? (equipped ? UiKit.PurifyHi : UiKit.Info) : UiKit.Text4);
            UiKit.Text(this, UiKit.ZenBold, new Vector2(r.Position.X + 26, y + h / 2f - 8), name, 14, unlocked ? (equipped ? UiKit.White : UiKit.Text2) : UiKit.Text4);
        }

        // 右：装備中モードの明示＋装備操作子（過熱チップ跡地の整理）。
        string cur = "装備中: " + (_game?.ShotModeName(_game.SelectedShotMode) ?? "連射");
        float curW = UiKit.TextW(UiKit.ZenBold, cur, 12);
        float keyX = x + w - 148f;
        UiKit.Text(this, UiKit.ZenBold, new Vector2(keyX - curW - 16f, y + h / 2f - 8), cur, 12, new Color(UiKit.PurifyHi, 0.9f));
        UiKit.Key(this, new Vector2(keyX, y + h / 2f - 13), Pad.EquipToken, new Color(1, 1, 1, 0.07f), new Color(1, 1, 1, 0.16f), UiKit.Text2);
        UiKit.Text(this, UiKit.Zen, new Vector2(keyX + 28f, y + h / 2f - 8), "で装備", 12, UiKit.Text3);
    }

    private void DrawModeIcon(Vector2 c, int idx, Color col)
    {
        switch (idx)
        {
            case 0: // 連射＝三本の横線
                for (int k = -1; k <= 1; k++)
                    DrawLine(c + new Vector2(-6, k * 3.5f), c + new Vector2(6, k * 3.5f), col, 1.6f);
                break;
            case 1: // 拡散＝扇状の三本
                for (int k = -1; k <= 1; k++)
                {
                    float a = k * 0.5f;
                    DrawLine(c + new Vector2(-5, 0), c + new Vector2(-5 + Mathf.Cos(a) * 11, Mathf.Sin(a) * 11), col, 1.6f);
                }
                break;
            default: // ホーミング＝弧＋標的点
                DrawArc(c, 6f, Mathf.Pi * 0.2f, Mathf.Pi * 1.6f, 20, col, 1.6f, true);
                DrawCircle(c + new Vector2(5, -4), 2.2f, col);
                break;
        }
    }

    // ───────────────────────── 二分木ツリー描画（_treeLayer へ・カメラ座標系）─────────────────────────
    private void DrawTree()
    {
        // 0) 系統レーン帯（最背面）＝縦に長いツリーでも「いまどの系統を見ているか」が色と見出しで分かる。
        DrawStreamLanes();

        // 1) エッジ（通常＝白灰の独立エッジ／排他＝金のY字束ね＋⊗メダル）。フォーク対は片側だけが描く（重複回避）。
        //    カリングは「エッジの経路（from→to のバウンディングボックス）が表示窓に触れるか」で判定する。
        //    ★旧判定（親か子のどちらかのノードが窓内）だと、帯をまたぐ長いエッジ（ミナの核→身のこなし／
        //      身のこなし→誘導・加速球など）が“中間だけ表示中”のとき両端とも窓外＝スキップされ、
        //      線が途切れて見えるバグがあった（L字経路は from→to の箱内に収まる＝箱交差で正しく拾える）。
        var forkDrawn = new HashSet<string>();
        foreach (var nd in GameManager.Upgrades)
        {
            int childSel = SelOf(nd.Id);
            if (childSel < 0) continue;
            Rect2 childR = NodeRect(childSel - 4);
            int parentSel = string.IsNullOrEmpty(nd.ParentId) ? -1 : SelOf(nd.ParentId);
            Rect2 parentR = parentSel >= 4 ? NodeRect(parentSel - 4) : new Rect2(RootC, Vector2.Zero);
            Vector2 to = LeftCenter(childR);
            Vector2 from = string.IsNullOrEmpty(nd.ParentId)
                ? RootC + new Vector2(RootR, 0)
                : RightCenter(parentR);
            if (!EdgeVisible(from, to)) continue; // エッジ経路が窓外＝描かない（両端窓外でも中間が見えるなら描く）

            if (!string.IsNullOrEmpty(nd.ExclusiveWith))
            {
                // 排他フォーク：対でまとめて1回だけ描く（Y字＋メダル＋ブラケット）。※現状は撤廃で未使用。
                string key = string.CompareOrdinal(nd.Id, nd.ExclusiveWith) < 0 ? nd.Id : nd.ExclusiveWith;
                if (!forkDrawn.Add(key)) continue;
                DrawForkEdges(nd.Id, nd.ExclusiveWith, from);
            }
            else
            {
                // 通常エッジ：所持済みの枝は系統色で灯し、光が流れて「育てた道」が生きて見える。
                //   未所持エッジも系統色をごく淡く乗せて「これから伸ばす道」の色を予告する（一目の系統識別）。
                bool lit = (_game?.GetUpgradeLevel(nd.Id) ?? 0) >= 1;
                if (lit) DrawLitEdge(from, to, StreamColor(nd.Id), nd.Id);
                else DrawElbow(from, to, new Color(StreamColor(nd.Id), 0.18f), 1.6f);
            }
        }

        // 2) root メダリオン「ミナの核」（左上・常に窓内寄り）
        if (NodeVisible(new Rect2(RootC - new Vector2(RootR, RootR), new Vector2(RootR * 2, RootR * 2))))
            DrawRootMedallion();

        // 3) ノードセル（窓外はスキップ）
        for (int i = 0; i < NodePos.Length; i++)
        {
            Rect2 r = NodeRect(i);
            if (!NodeVisible(r)) continue;
            bool focus = _sel == i + 4;
            DrawNodeCell(NodePos[i].id, r, focus);
            // マウスホバー：フォーカスでない被ホバーのノードを淡く縁取り＝クリック先が分かる（フォーカス枠と衝突しない）。
            //   _hovId はカメラ逆変換込みの HitTest 結果＝スクロールしても実際にマウス直下のノードだけが光る。
            if (!focus && _hovId == i + 4)
                UiKit.Box(_ci, r.Grow(2f), null, 8f, new Color(UiKit.PurifyHi, 0.55f), 1.4f);
        }
    }

    // 系統レーン帯：各系統の y 範囲を NodePos から実測し、淡い系統色の帯＋左レール＋見出し（◯◯・n/N）を描く。
    //   縦スクロールの長いツリーで現在地の手掛かりになる。窓外の帯は描画スキップ。所持数で見出しの明るさが増す。
    private void DrawStreamLanes()
    {
        foreach (Stream s in System.Enum.GetValues<Stream>())
        {
            float top = float.MaxValue, bot = float.MinValue;
            int owned = 0, tot = 0;
            foreach (var (id, _, y) in NodePos)
            {
                if (StreamOf(id) != s) continue;
                top = Mathf.Min(top, y); bot = Mathf.Max(bot, y + NodeH); tot++;
                if ((_game?.GetUpgradeLevel(id) ?? 0) >= 1) owned++;
            }
            if (tot == 0) continue;
            top -= 16f; bot += 12f;
            if (top - _cam.Y > TreeBot || bot - _cam.Y < TreeTop) continue; // 帯が縦スクロール外なら描かない
            Color sc = StreamCol[(int)s];
            bool done = owned >= tot;
            // 帯の地（ごく淡い系統色）＝仮想幅いっぱい。
            UiKit.Box(_ci, new Rect2(WinL, top, TreeVirtR - WinL, bot - top), new Color(sc, 0.045f), 0f);
            // 見出し（系統名＋所持数）は左端に貼り付く＝横スクロールしても常に読める（cam.X を足して窓左に固定）。
            float hx = _cam.X + WinL + 6f;
            _ci.DrawRect(new Rect2(hx - 2f, top, 2.5f, bot - top), new Color(sc, done ? 0.75f : 0.3f)); // 左レール
            Color hc = done ? UiKit.Gold.Lerp(sc, 0.3f) : new Color(sc, owned > 0 ? 0.9f : 0.5f);
            UiKit.Box(_ci, new Rect2(hx + 4f, top + 2f, 96f, 38f), new Color(0.05f, 0.04f, 0.09f, 0.62f), 6f); // 見出し下地（帯や線と被っても読める）
            UiKit.Text(_ci, UiKit.ZenBlack, new Vector2(hx + 10f, top + 2f), StreamName[(int)s], 14, hc);
            UiKit.Text(_ci, UiKit.Mono, new Vector2(hx + 10f, top + 22f), $"{owned}/{tot}" + (done ? "  ✦" : ""), 11, new Color(hc, 0.9f));
        }
    }

    private static Vector2 LeftCenter(Rect2 r) => new(r.Position.X, r.Position.Y + r.Size.Y / 2f);
    private static Vector2 RightCenter(Rect2 r) => new(r.End.X, r.Position.Y + r.Size.Y / 2f);

    // L字エッジ（水平→垂直→水平）。二分木の枝分かれを配線図の語彙で読ませる。
    private void DrawElbow(Vector2 from, Vector2 to, Color col, float width)
    {
        float midX = (from.X + to.X) / 2f;
        _ci.DrawLine(from, new Vector2(midX, from.Y), col, width);
        _ci.DrawLine(new Vector2(midX, from.Y), new Vector2(midX, to.Y), col, width);
        _ci.DrawLine(new Vector2(midX, to.Y), to, col, width);
    }

    // 所持済みエッジ：系統色でL字を描き、その上を光の粒が流れる（根元→ノードへ「育てた道」が生きる）。
    //   ・下地に太く薄い同色グロウを重ねてコントラストを底上げ（地味さの主因＝発光不足への対処）。
    //   ・光の粒は id をシード化した位相で流し、全エッジが同時明滅しない（画面が呼吸して見える）。
    //   ・購入直後（_buyFxId==id）はその区間だけ強く伝播点灯＝解放が「線を伝って」広がる手触り。
    private void DrawLitEdge(Vector2 from, Vector2 to, Color col, string id)
    {
        float midX = (from.X + to.X) / 2f;
        Vector2 a = from, b = new(midX, from.Y), c = new(midX, to.Y), d = to;
        bool justBought = _buyFxT > 0 && _buyFxId == id;
        float boost = justBought ? (float)(_buyFxT / 0.7) : 0f;   // 1→0
        // 系統コンプリート中は、その系統のエッジ束を一斉に強く脈打たせる（ご褒美＝道が総点灯）。
        if (_streamBannerT > 0 && StreamOf(id) == _streamBannerId)
            boost = Mathf.Max(boost, 0.5f + 0.5f * Mathf.Sin((float)_t * 8f));
        // 下地グロウ（太い半透明）＋本線。
        _ci.DrawLine(a, b, new Color(col, 0.10f + 0.30f * boost), 5f);
        _ci.DrawLine(b, c, new Color(col, 0.10f + 0.30f * boost), 5f);
        _ci.DrawLine(c, d, new Color(col, 0.10f + 0.30f * boost), 5f);
        float baseA = 0.5f + 0.4f * boost;
        _ci.DrawLine(a, b, new Color(col, baseA), 1.8f);
        _ci.DrawLine(b, c, new Color(col, baseA), 1.8f);
        _ci.DrawLine(c, d, new Color(col, baseA), 1.8f);
        // 流れる光の粒（区間長で正規化した位相を1つ流す）。位相は id ハッシュで散らす。
        float lenAB = Mathf.Abs(b.X - a.X), lenBC = Mathf.Abs(c.Y - b.Y), lenCD = Mathf.Abs(d.X - c.X);
        float total = lenAB + lenBC + lenCD;
        if (total < 1f) return;
        float seed = (Mathf.Abs(id.GetHashCode()) % 1000) / 1000f;
        float ph = Mathf.PosMod((float)_t * 0.35f + seed, 1f) * total;   // 進んだ距離
        Vector2 p = ph < lenAB ? a.Lerp(b, ph / Mathf.Max(1f, lenAB))
            : ph < lenAB + lenBC ? b.Lerp(c, (ph - lenAB) / Mathf.Max(1f, lenBC))
            : c.Lerp(d, (ph - lenAB - lenBC) / Mathf.Max(1f, lenCD));
        float dotA = 0.7f + 0.3f * boost;
        UiKit.RadialGlow(_ci, p, 9f, col, 0.45f * dotA);
        _ci.DrawCircle(p, 2.2f, new Color(1, 1, 1, dotA));
    }

    // 排他フォーク：親からの1本をY字の根元で束ね、⊗メダル＋対セルの共通ブラケット＋「どちらか一方」。
    // エッジ色（金）と束ね形状の二重符号化＝色弱でも判別できる。
    private void DrawForkEdges(string idA, string idB, Vector2 from)
    {
        int sa = SelOf(idA), sb = SelOf(idB);
        var ra = NodeRect(sa - 4);
        var rb = NodeRect(sb - 4);
        Vector2 ta = LeftCenter(ra), tb = LeftCenter(rb);
        float forkX = Mathf.Min(ta.X, tb.X) - 26f;
        Vector2 fork = new(forkX, (ta.Y + tb.Y) / 2f);

        bool chosen = (_game?.GetUpgradeLevel(idA) ?? 0) >= 1 || (_game?.GetUpgradeLevel(idB) ?? 0) >= 1;
        Color gold = new(ForkGold, chosen ? 0.85f : 0.55f);

        // 親→根元は1本に束ねる（通常の独立エッジとの見分けの本体）。
        _ci.DrawLine(from, new Vector2(forkX, from.Y), gold, 2f);
        _ci.DrawLine(new Vector2(forkX, from.Y), fork, gold, 2f);
        // 根元→両翼
        _ci.DrawLine(fork, ta, gold, 2f);
        _ci.DrawLine(fork, tb, gold, 2f);

        // ⊗メダル（金の円＋クロス）
        _ci.DrawCircle(fork, 9f, new Color(0.12f, 0.10f, 0.05f, 0.95f));
        _ci.DrawArc(fork, 9f, 0, Mathf.Tau, 24, gold, 1.6f, true);
        _ci.DrawLine(fork + new Vector2(-4, -4), fork + new Vector2(4, 4), gold, 1.6f);
        _ci.DrawLine(fork + new Vector2(-4, 4), fork + new Vector2(4, -4), gold, 1.6f);

        // 対の2セルを囲う薄金ブラケット＋ラベル「どちらか一方」。
        float top = Mathf.Min(ra.Position.Y, rb.Position.Y) - 5f;
        float bot = Mathf.Max(ra.End.Y, rb.End.Y) + 5f;
        var br = new Rect2(ra.Position.X - 5f, top, NodeW + 10f, bot - top);
        UiKit.Box(_ci, br, null, 12f, new Color(ForkGold, 0.35f), 1f);
        UiKit.Text(_ci, UiKit.Zen, new Vector2(br.Position.X + 8f, top - 13f), "どちらか一方", 9, new Color(ForkGold, 0.8f));
    }

    // ルート「ミナの核」：無償付与・購入不可。ツリーの説明アンカー（フォーカス可能）。
    private void DrawRootMedallion()
    {
        bool focus = _sel == 3;
        bool hov = !focus && _hovId == 3; // マウスホバー（フォーカス時は下の Info リングが優先）
        UiKit.RadialGlow(_ci, RootC, RootR * 2.2f, UiKit.Mina, focus ? 0.30f : hov ? 0.24f : 0.18f);
        _ci.DrawCircle(RootC, RootR, new Color(0.10f, 0.08f, 0.16f, 0.95f));
        _ci.DrawArc(RootC, RootR, 0, Mathf.Tau, 40, focus ? UiKit.Info : hov ? new Color(UiKit.PurifyHi, 0.9f) : new Color(UiKit.Mina, 0.8f), focus ? 2.2f : hov ? 1.8f : 1.5f, true);
        _ci.DrawArc(RootC, RootR - 4f, 0, Mathf.Tau, 40, new Color(UiKit.Mina, 0.35f), 1f, true);
        // 核＝ハート（ミナの心）。
        UiKit.Heart(_ci, RootC + new Vector2(0, -6f), 10f, new Color(UiKit.Mina, 0.95f));
        UiKit.Text(_ci, UiKit.ZenBold, new Vector2(RootC.X - RootR, RootC.Y + 8f), "ミナの核", 11, UiKit.White, HorizontalAlignment.Center, RootR * 2f);
    }

    // ノードセル（146×44）：1行目=名前＋Lvピップ、2行目=コスト or 前提。封印は暗転＋斜線＋「封印」タグ。
    private void DrawNodeCell(string id, Rect2 r, bool focus)
    {
        var d = GameManager.GetUpgradeDef(id);
        if (d == null) return;
        int lv = _game?.GetUpgradeLevel(id) ?? 0;
        bool maxed = lv >= d.MaxLevel;
        long cost = maxed ? -1 : (_game?.GetUpgradeCost(id) ?? 0);
        bool sealed_ = _game?.IsSealed(id) ?? false;
        bool parentOk = _game?.IsParentMet(id) ?? true;
        bool prereqOk = _game?.IsPrereqMet(id) ?? true;
        long imp = _game?.Impression ?? 0;
        bool can = !maxed && !sealed_ && parentOk && prereqOk && cost >= 0 && imp >= cost;

        Color sCol = StreamColor(id); // 系統アイデンティティ色

        // 封印・親未接続は背景から沈めて「まだ触れない」を先に読ませる。
        float bgA = sealed_ ? 0.35f : (!parentOk && lv == 0) ? 0.38f : focus ? 0.8f : 0.55f;
        // ノード縁を系統色でうっすら着色（買えない状態は控えめ）＝どの系統のノードかを一目で。フォーカスは Info で最優先。
        Color border = focus ? UiKit.Info
            : lv >= 1 ? new Color(sCol, 0.55f)
            : can ? new Color(sCol, 0.42f)
            : new Color(sCol, 0.16f);
        UiKit.Box(_ci, r, new Color(22 / 255f, 18 / 255f, 34 / 255f, bgA), 8f, border, focus ? 1.8f : 1f);

        float x = r.Position.X, y = r.Position.Y;
        // 左端の系統カラーバー（帯＝系統アイデンティティの地の色）。所持で濃く、未所持は淡く。
        UiKit.Box(_ci, new Rect2(x, y + 4f, 3.5f, r.Size.Y - 8f), new Color(sCol, lv >= 1 ? 0.9f : can ? 0.5f : 0.25f), 2f);

        // 名前（買えるものは白＝“いま買える”が一覧で拾える。買えない/MAX/封印は沈める）
        Color nameCol = sealed_ ? UiKit.Text4 : maxed ? UiKit.Text4 : can ? UiKit.White : (!parentOk && lv == 0) ? UiKit.Text4 : UiKit.Text3;
        UiKit.Text(_ci, UiKit.ZenBold, new Vector2(x + 12, y + 5), d.Name, 12, nameCol);
        float nw = UiKit.TextW(UiKit.ZenBold, d.Name, 12);

        // 王冠（shot_power_4＝光の出力IV＝LUNATIC解放条件のひとつ）。所持で点灯。
        if (id == "shot_power_4")
            DrawCrown(new Vector2(x + 12 + nw + 12, y + 13f), 6f, lv >= 1 ? UiKit.Gold : new Color(UiKit.Gold, 0.5f));

        // Lvピップ（右上：MaxLevel 個、lv ぶん充填）。所持ピップは系統色。
        float px = r.End.X - 10 - d.MaxLevel * 9f, py = y + 12f;
        for (int p = 0; p < d.MaxLevel; p++)
            _ci.DrawCircle(new Vector2(px + p * 9f + 3f, py), 2.6f,
                p < lv ? new Color(sCol, 0.95f) : new Color(1, 1, 1, 0.14f));

        // 2行目：封印はタグで示しコストは出さない。前提未達は理由を常時表示。
        float ly = y + 24f;
        if (sealed_)
        {
            // 斜線＋封印タグ（暗転はセル背景のアルファで済ませ、名前は読めるまま残す）。※排他撤廃で現状は未到達。
            for (float sx = x + 8f; sx < r.End.X - 4f; sx += 22f)
                _ci.DrawLine(new Vector2(sx, r.End.Y - 4f), new Vector2(sx + 12f, y + 4f), new Color(ForkGold, 0.22f), 1f);
            const string tag = "封印";
            float tw = UiKit.TextW(UiKit.ZenBold, tag, 10) + 14f;
            var tr = new Rect2(r.End.X - tw - 8f, ly + 2f, tw, 16f);
            UiKit.Box(_ci, tr, new Color(0.12f, 0.10f, 0.05f, 0.9f), 4f, new Color(ForkGold, 0.6f), 1f);
            UiKit.Text(_ci, UiKit.ZenBold, new Vector2(tr.Position.X, ly + 3f), tag, 10, new Color(ForkGold, 0.9f), HorizontalAlignment.Center, tw);
            UiKit.Text(_ci, UiKit.Zen, new Vector2(x + 12, ly + 3f), Pad.ConfirmToken + " 選び直し", 9, UiKit.Text4);
        }
        else if (!prereqOk)
        {
            string pn = GameManager.GetUpgradeDef(d.PrereqId)?.Name ?? d.PrereqId;
            DrawLockIcon(new Vector2(x + 17, ly + 8f), 5f, new Color(Deny, 0.9f));
            UiKit.Text(_ci, UiKit.Zen, new Vector2(x + 27, ly + 2f), $"前提: {pn}", 9, Deny);
        }
        else if (maxed)
        {
            UiKit.Text(_ci, UiKit.Mono, new Vector2(x + 12, ly + 1f), "所持済み", 11, new Color("c9b6ef"));
        }
        else
        {
            string costS = "♥" + cost.ToString("N0");
            UiKit.Text(_ci, UiKit.Mono, new Vector2(x + 12, ly + 1f), costS, 11, can ? UiKit.Gold : UiKit.Text4);
        }

        DrawCellFx(id, r, can);
    }

    // フロンティア金パルス／購入可能ノードの呼吸／購入直後グロー／カプストーン解放パルス。
    private void DrawCellFx(string id, Rect2 r, bool can)
    {
        // 購入可能ノードの呼吸：フロンティア（金＝おすすめ）でない「いま買える」全ノードを
        // 系統色でそっと明滅させ、「触れるところ」が一覧で息づく（金の道しるべと衝突しないよう控えめに）。
        if (can && !_frontier.Contains(id))
        {
            float breath = 0.18f + 0.16f * Mathf.Sin((float)_t * 2.6f + r.Position.X * 0.03f);
            UiKit.Box(_ci, r, null, 8f, new Color(StreamColor(id), breath), 1.4f);
        }
        if (_frontier.Contains(id))
        {
            float pulse = 0.35f + 0.35f * Mathf.Sin((float)_t * 4f);
            UiKit.Box(_ci, r, null, 8f, new Color(UiKit.Gold, pulse), 1.6f);
            const string rec = "おすすめ";
            float rw = UiKit.TextW(UiKit.Zen, rec, 9) + 10f;
            var rr = new Rect2(r.End.X - rw - 6f, r.Position.Y - 7f, rw, 14f);
            UiKit.Box(_ci, rr, new Color(UiKit.Gold, 0.2f), 999f, new Color(UiKit.Gold, 0.7f), 1f);
            UiKit.Text(_ci, UiKit.Zen, new Vector2(rr.Position.X, rr.Position.Y), rec, 9, new Color("f0d98a"), HorizontalAlignment.Center, rw);
        }
        if (_buyFxT > 0 && _buyFxId == id)
        {
            float a = (float)(_buyFxT / 0.7);
            UiKit.Box(_ci, r, null, 8f, new Color(UiKit.Info, 0.8f * a), 2f);
        }
        if (_capPulseT > 0 && _capPulseId == id)
        {
            float k = 1f - (float)(_capPulseT / 1.4);
            float a = 1f - k;
            UiKit.Box(_ci, r.Grow(2f + 8f * k), null, 10f, new Color(UiKit.Gold, 0.8f * a), 2f);
            UiKit.Text(_ci, UiKit.ZenBlack, new Vector2(r.Position.X, r.Position.Y - 22f), "解放!", 14, new Color(UiKit.Gold, a), HorizontalAlignment.Center, r.Size.X);
        }
    }

    // フロンティア（金パルス対象）を確定：おすすめが未購入かつ買えるならそれを、
    // 親未接続で買えないなら親チェーンを遡って最初に買える祖先を代わりに光らせる。
    private void RebuildFrontier()
    {
        _frontier.Clear();
        if (_game == null) return;
        foreach (var start in _recommended)
        {
            string cur = start;
            for (int guard = 0; guard < 8 && !string.IsNullOrEmpty(cur); guard++)
            {
                var d = GameManager.GetUpgradeDef(cur);
                if (d == null) break;
                if (_game.GetUpgradeLevel(cur) > 0) break;           // 既に着手済み＝道しるべ不要
                if (_game.CanPurchase(cur)) { _frontier.Add(cur); break; }
                if (!_game.IsParentMet(cur)) { cur = d.ParentId; continue; } // 親未接続→祖先へ
                break;                                               // 資金/前提/封印が理由なら光らせない
            }
        }
    }

    // 小さな錠前（前提ロックの合図）。
    // 小さな錠前（前提ロックの合図）。呼び先の _ci（ツリー or 詳細パネル）へ描く。
    private void DrawLockIcon(Vector2 c, float s, Color col)
    {
        _ci.DrawArc(new Vector2(c.X, c.Y - s * 0.25f), s * 0.45f, Mathf.Pi, Mathf.Tau, 10, col, 1.4f, true);
        UiKit.Box(_ci, new Rect2(c.X - s * 0.6f, c.Y - s * 0.2f, s * 1.2f, s * 0.95f), col, 2f);
    }

    // 小さな王冠（shot_power_4 ＝ LUNATIC 解放条件、の印）。凹多角形は使わず矩形＋三角3枚で描く。
    // 呼び先の _ci（ツリーセル or 詳細パネル）へ描く＝どちらの座標系でも正しく乗る。
    private void DrawCrown(Vector2 c, float s, Color col)
    {
        _ci.DrawRect(new Rect2(c.X - s, c.Y, s * 2f, s * 0.55f), col);
        _ci.DrawColoredPolygon(new[] { new Vector2(c.X - s, c.Y), new Vector2(c.X - s, c.Y - s * 0.8f), new Vector2(c.X - s * 0.34f, c.Y) }, col);
        _ci.DrawColoredPolygon(new[] { new Vector2(c.X - s * 0.33f, c.Y), new Vector2(c.X, c.Y - s * 0.95f), new Vector2(c.X + s * 0.33f, c.Y) }, col);
        _ci.DrawColoredPolygon(new[] { new Vector2(c.X + s * 0.34f, c.Y), new Vector2(c.X + s, c.Y - s * 0.8f), new Vector2(c.X + s, c.Y) }, col);
    }

    // ───────────────────────── 詳細パネル「つぎの一手」（フォーカス連動） ─────────────────────────
    // 射撃プレビュー＋「いま何を選んでいて、買うと何がどう変わり、いくら残るか／なぜ買えないか」を集約。
    // 封印ノードでは振り直しの差引きを常時表示し、Z の2段確認もこのパネルで完結する。
    private void DrawDetailPanel()
    {
        _buyBtnActive = false; // 既定は無効。ノード詳細のときだけ下部にクリック購入ボタンを立てる。
        float x = DetailX, w = DetailW;
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, TreeTop), "つぎの一手", 18, UiKit.White);
        float by = TreeTop + 30f, bh = TreeBot - by;
        UiKit.Box(this, new Rect2(x, by, w, bh), new Color(20 / 255f, 16 / 255f, 30 / 255f, 0.5f), 12f, new Color(UiKit.Info, 0.25f), 1f);

        // 射撃プレビュー（フォーカスノードの枝のモード。全モード系・root は装備中を映す）。
        int pv = _sel <= 2 ? _sel : PreviewModeFor(FocusNodeId);
        if (pv < 0) pv = System.Array.IndexOf(Modes, _game?.SelectedShotMode ?? GameManager.ShotMode.Rapid);
        if (pv < 0) pv = 0;
        bool pvLocked = !(_game?.IsModeUnlocked(Modes[pv]) ?? (pv == 0));
        DrawModeField(x + 10, by + 10, w - 20, 90, pv, pvLocked);
        string pvLabel = _game?.ShotModeName(Modes[pv]) ?? "";
        // ラベルは右上（左はミナ立ち絵が立つので隠れる）。
        UiKit.Text(this, UiKit.Mono, new Vector2(x + w - 16 - UiKit.TextW(UiKit.Mono, pvLabel, 10), by + 14), pvLabel, 10, new Color(1, 1, 1, 0.55f));

        float ix = x + 16f, iw = w - 32f, iy = by + 112f;

        // root（ミナの核）：説明アンカー。
        if (_sel == 3)
        {
            DrawRect(new Rect2(ix, iy + 4, 4f, 18f), new Color(UiKit.Mina, 0.9f));
            UiKit.Text(this, UiKit.ZenBlack, new Vector2(ix + 12, iy), "ミナの核", 19, UiKit.White);
            UiKit.Multi(this, UiKit.Zen, new Vector2(ix, iy + 28),
                "ここからすべてが伸びる。線でつながった隣のノードから買える。上＝攻めの系譜、下＝支えの系譜。", 12, UiKit.Text2, iw, 3);
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, iy + 84), "⊗ の枝は「どちらか一方」。心を払えば選び直せる。", 11, new Color(ForkGold, 0.9f));
            return;
        }

        // R0 チップ：モードの案内。
        if (_sel <= 2)
        {
            bool unlocked = _game?.IsModeUnlocked(Modes[_sel]) ?? (_sel == 0);
            DrawRect(new Rect2(ix, iy + 4, 4f, 18f), new Color(CatCol[0], 0.9f));
            UiKit.Text(this, UiKit.ZenBlack, new Vector2(ix + 12, iy), _game?.ShotModeName(Modes[_sel]) ?? "", 19, UiKit.White);
            UiKit.Multi(this, UiKit.Zen, new Vector2(ix, iy + 28), ModeDesc[_sel], 12, UiKit.Text2, iw, 2);
            if (_sel == 0)
                UiKit.Text(this, UiKit.Zen, new Vector2(ix, iy + 74), "初期解放・常時使用可（" + Pad.EquipToken + " で装備）", 12, new Color("7ec880"));
            else if (unlocked)
                UiKit.Text(this, UiKit.Zen, new Vector2(ix, iy + 74), Pad.EquipToken + " で装備", 12, new Color("7ec880"));
            else
                UiKit.Text(this, UiKit.Zen, new Vector2(ix, iy + 74),
                    _sel == 1 ? "解放: 連射速度 → 拡散展開（枝の入り口）" : "解放: 身のこなし → 誘導の祈り（枝の入り口）", 12, UiKit.Text3);
            return;
        }

        // ノード詳細
        string id = FocusNodeId!;
        var d = GameManager.GetUpgradeDef(id)!;
        int lv = _game?.GetUpgradeLevel(id) ?? 0;
        bool maxed = lv >= d.MaxLevel;
        long cost = maxed ? -1 : (_game?.GetUpgradeCost(id) ?? 0);
        bool sealed_ = _game?.IsSealed(id) ?? false;
        bool parentOk = _game?.IsParentMet(id) ?? true;
        bool prereqOk = _game?.IsPrereqMet(id) ?? true;
        long imp = _game?.Impression ?? 0;

        DrawRect(new Rect2(ix, iy + 4, 4f, 18f), new Color(CatCol[CatFor(id)], 0.9f));
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(ix + 12, iy), d.Name, 19, UiKit.White);
        string lvS = $"Lv {lv}/{d.MaxLevel}";
        UiKit.Text(this, UiKit.Mono, new Vector2(ix + iw - UiKit.TextW(UiKit.Mono, lvS, 12), iy + 6), lvS, 12, UiKit.Text3);
        UiKit.Multi(this, UiKit.Zen, new Vector2(ix, iy + 26), d.Desc, 12, UiKit.Text2, iw, 2);

        // 現在 → 購入後（差分＝意思決定の中心）。長い効果文が入るので上下2段。
        float ey = iy + 66f;
        UiKit.Box(this, new Rect2(ix, ey, iw, 56f), new Color(0, 0, 0, 0.24f), 9f);
        UiKit.Text(this, UiKit.Mono, new Vector2(ix + 12, ey + 8), "いま", 10, UiKit.Text3);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 58, ey + 6), Eff(id, withThisNode: lv >= 1), 13, UiKit.Text2);
        if (!maxed)
        {
            UiKit.Text(this, UiKit.Mono, new Vector2(ix + 12, ey + 32), "買うと", 10, new Color(CatCol[CatFor(id)], 0.8f));
            UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 58, ey + 30), Eff(id, withThisNode: true), 13, UiKit.White);
        }
        else
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix + 12, ey + 32), "最大強化済み", 12, new Color("c9b6ef"));
        }

        // 封印中：振り直しの差引きプレビュー（常時）＋2段確認。
        if (sealed_)
        {
            DrawRespecPanel(ix, iw, ey + 68f, id, d);
            return;
        }

        // コスト行：値段・購入後の残り・買えない理由（押す前に全部わかる）。
        float cy = ey + 68f;
        if (cost >= 0)
        {
            string costS = "♥" + cost.ToString("N0");
            bool afford = imp >= cost;
            bool blocked = !parentOk || !prereqOk;
            UiKit.Text(this, UiKit.Mono, new Vector2(ix, cy), costS, 16, afford && !blocked ? UiKit.Gold : Deny);
            float cw2 = UiKit.TextW(UiKit.Mono, costS, 16);
            if (blocked)
                UiKit.Text(this, UiKit.Zen, new Vector2(ix + cw2 + 14, cy + 2), "条件が必要です（下記）", 12, Deny);
            else if (afford)
                UiKit.Text(this, UiKit.Zen, new Vector2(ix + cw2 + 14, cy + 2), $"買うと のこり ♥{(imp - cost):N0}", 12, UiKit.Text3);
            else
                UiKit.Text(this, UiKit.Zen, new Vector2(ix + cw2 + 14, cy + 2), $"あと ♥{(cost - imp):N0} たりない", 12, Deny);
        }
        else
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, cy + 2), "これ以上は強化できません", 12, UiKit.Text3);
        }

        // 条件行：親（未接続時）→前提（奥義）→排他の注意→LUNATIC 条件（shot_power）。
        float ny = cy + 26f;
        if (!parentOk)
        {
            string pn = GameManager.GetUpgradeDef(d.ParentId)?.Name ?? "ミナの核";
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, ny), $"親: {pn} を先に Lv1 へ（線でつながった隣）", 12, Deny);
            ny += 20f;
        }
        if (!string.IsNullOrEmpty(d.PrereqId))
        {
            string pn = GameManager.GetUpgradeDef(d.PrereqId)?.Name ?? d.PrereqId;
            int plv = _game?.GetUpgradeLevel(d.PrereqId) ?? 0;
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, ny), $"前提: {pn} Lv{d.PrereqLv}（いま Lv{plv}）", 12, prereqOk ? new Color("7ec880") : Deny);
            ny += 20f;
            if (!prereqOk && lv > 0)
            {
                UiKit.Text(this, UiKit.Zen, new Vector2(ix, ny), $"所持済みの Lv{lv} は有効のままです", 11, UiKit.Text3);
                ny += 20f;
            }
        }
        if (!string.IsNullOrEmpty(d.ExclusiveWith))
        {
            string en = GameManager.GetUpgradeDef(d.ExclusiveWith)?.Name ?? d.ExclusiveWith;
            int elv = _game?.GetUpgradeLevel(d.ExclusiveWith) ?? 0;
            // 両側所持（旧セーブの共存特例）はその旨を明示。未選択なら「⊗＝選ぶと相方は封印」を予告。
            string exText = elv >= 1 && lv >= 1 ? $"⊗ {en} と両立中（引き継ぎ特例）"
                          : lv >= 1 ? $"⊗ 相方 {en} は封印中（そちらで {Pad.ConfirmToken}＝選び直し）"
                          : $"⊗ どちらか一方: 買うと {en} は封印されます";
            UiKit.Text(this, UiKit.Zen, new Vector2(ix, ny), exText, 11, new Color(ForkGold, 0.9f));
            ny += 20f;
        }
        if (id == "shot_power_4")
        {
            DrawCrown(new Vector2(ix + 7, ny + 10), 6f, UiKit.Gold);
            UiKit.Text(this, UiKit.Zen, new Vector2(ix + 18, ny), "所持で LUNATIC 解放条件のひとつを満たします", 11, new Color("c9b6ef"));
        }

        // ── 詳細パネル下部の「購入」ボタン（マウスでクリック購入する明示導線）──
        //   キーボード/パッドの Z 購入はそのまま。マウス派はノードを1度クリックで選択→このボタンで確定できる
        //   （ツリー上で同じノードを2度クリックしても購入＝どちらの手触りでも買える）。買える状態(can)のみ有効化。
        bool can = !maxed && parentOk && prereqOk && cost >= 0 && imp >= cost;
        DrawBuyButton(x, w, "♥ 購入", can);
    }

    // 詳細パネル下部の購入/確定ボタンを描き、当たり矩形を _buyBtnRect / 有効フラグ _buyBtnActive に保存する。
    //   有効時：金の縁取り＋ホバーで発光。無効時：沈めて当たり判定も無効化（誤クリックで買えない旨のトーストを出さない）。
    private void DrawBuyButton(float panelX, float panelW, string label, bool enabled)
    {
        float bw = 132f, bh = 34f;
        var r = new Rect2(panelX + (panelW - bw) / 2f, TreeBot - bh - 12f, bw, bh);
        _buyBtnRect = r;
        _buyBtnActive = enabled;
        bool hov = enabled && _hovId == HsBuy;
        Color edge = enabled ? UiKit.Gold : new Color(1, 1, 1, 0.14f);
        Color bg = enabled ? new Color(UiKit.Gold, hov ? 0.24f : 0.14f) : new Color(1, 1, 1, 0.04f);
        if (hov) UiKit.RadialGlow(this, r.GetCenter(), bw * 0.7f, UiKit.Gold, 0.22f);
        UiKit.Box(this, r, bg, 10f, new Color(edge, enabled ? (hov ? 1f : 0.75f) : 0.3f), enabled && hov ? 1.8f : 1.2f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(r.Position.X, r.Position.Y + 8f), label,
            15, enabled ? (hov ? UiKit.White : new Color("f0d98a")) : UiKit.Text4, HorizontalAlignment.Center, bw);
        if (enabled) UiKit.Hotspot(r, HsBuy);
    }

    // 封印ノードの振り直しパネル：差引きの内訳（返金−手数料）を常時見せ、Z の2段確認をここで完結する。
    private void DrawRespecPanel(float ix, float iw, float ry, string id, GameManager.UpgradeDef d)
    {
        string partner = d.ExclusiveWith;
        var pd = GameManager.GetUpgradeDef(partner);
        int plv = _game?.GetUpgradeLevel(partner) ?? 0;
        long refund = _game?.RespecRefund(id, partner) ?? 0;
        long fee = _game?.RespecFee(id, partner) ?? 0;

        UiKit.Text(this, UiKit.Zen, new Vector2(ix, ry), $"封印中: ⊗ {pd?.Name ?? partner} を選んだ枝", 12, new Color(ForkGold, 0.9f));

        bool armed = _respecArmed && _respecId == id;
        var box = new Rect2(ix, ry + 22f, iw, armed ? 112f : 70f);
        UiKit.Box(this, box, new Color(0.12f, 0.10f, 0.05f, 0.5f), 9f, new Color(ForkGold, armed ? 0.9f : 0.45f), armed ? 1.6f : 1f);
        float yy = ry + 30f;
        UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 12, yy), $"手放す: {pd?.Name} Lv{plv} → 選べる: {d.Name}", 12, UiKit.Text2);
        yy += 22f;
        UiKit.Text(this, UiKit.Zen, new Vector2(ix + 12, yy), $"返金 ♥{refund:N0} − 手数料 ♥{fee:N0} = ♥{refund - fee:N0}", 12, UiKit.Gold);
        yy += 24f;
        if (armed)
        {
            UiKit.Text(this, UiKit.ZenBold, new Vector2(ix + 12, yy), "本当に選び直しますか？", 13, UiKit.White);
            yy += 22f;
            UiKit.Text(this, UiKit.Zen, new Vector2(ix + 12, yy), Pad.ConfirmToken + " 確定　／　" + Pad.CancelToken + " 取消", 12, new Color(ForkGold, 0.9f));
        }
        else
        {
            UiKit.Text(this, UiKit.Zen, new Vector2(ix + 12, yy), Pad.ConfirmToken + " で選び直し（2段確認）", 11, UiKit.Text3);
        }
    }

    // 単Lvノードの効果表示（チェーン実効値）。ゲーム側の実計算式（GameManager/Player/Enemy/Bullet）と必ず一致させる。
    //   withThisNode＝このノードを所持済みとみなすか（"いま"＝現状 / "買うと"＝このノードを足した後）。
    //   系統プレフィクスの連続所持段数に、focus 中ノードの分だけ +1 を反映した実効 Lv で従来式を評価する。
    private string Eff(string id, bool withThisNode)
    {
        (string prefix, int max) = ChainInfo(id);
        int lv = EffLevel(prefix, max, id, withThisNode);
        return prefix switch
        {
            "shot_power" => $"威力 +{lv}",
            "fire_rate" => $"発射間隔 ×{Mathf.Max(0.4f, 1f - 0.08f * lv):0.00}",
            "rapid_power" => $"連射弾の威力 +{lv}",
            "rapid_rate" => $"連射間隔 ×{Mathf.Max(0.7f, 1f - 0.06f * lv):0.00}",
            "spread" => lv == 0 ? "未解放" : $"{new[] { 0, 5, 7, 9 }[Mathf.Clamp(lv, 0, 3)]}way",
            "spread_power" => $"拡散弾威力 ×{new[] { 0.50f, 0.56f, 0.62f }[Mathf.Clamp(lv, 0, 2)]:0.00}",
            "spread_rate" => $"拡散間隔税 ×{Mathf.Max(1f, 1.45f - 0.10f * lv):0.00}",
            "homing" => lv == 0 ? "未解放" : $"{new[] { 0, 2, 3, 4 }[Mathf.Clamp(lv, 0, 3)]}体追尾",
            "homing_power" => $"ホーミング威力 ×{new[] { 0.85f, 0.95f, 1.05f }[Mathf.Clamp(lv, 0, 2)]:0.00}",
            "homing_rate" => lv == 0 ? "間隔税 ×1.55・旋回150" : "間隔税 ×1.40・旋回200",
            "pierce" => lv == 0 ? "貫通なし" : $"連射弾が敵 {lv} 体を貫通",
            "focus" => lv == 0 ? "集中なし" : $"同じ敵に当て続けて威力 最大+{lv}",
            "counter" => lv == 0 ? "変換なし" : lv == 1 ? "2発に1発を光弾化（上限6/回避）" : "全弾を光弾化（上限12/回避）",
            "veil" => lv == 0 ? "光輪なし" : $"回避後の光輪 r{(lv == 1 ? 20 : 28)}px・{(lv == 1 ? 0.5f : 0.7f):0.0}s",
            "option" => lv == 0 ? "オプションなし" : $"追従オプション {lv} 基（威力×0.5）",
            "chain" => lv == 0 ? "跳弾なし" : $"拡散弾が {lv} 回跳弾（威力×0.4）",
            "max_life" => $"ライフ上限 +{lv}",
            "bomb_count" => $"初期ボム +{lv}",
            "bomb_power" => $"ボム直撃 {Mathf.RoundToInt(Enemy.BombStrikeBase * (1f + 0.25f * lv))}ダメージ",
            "move_speed" => $"移動×{1f + 0.12f * lv:0.00}・回避CD{0.8f - 0.1f * lv:0.0}s・{64 + 4 * lv}px",
            "hitbox" => $"被弾判定 ×{Mathf.Max(0.4f, 1f - 0.12f * lv):0.00}",
            // 澄んだ心（contam）：3→2圧縮＝段2で旧Lv3相当の実効Lvで評価。現在の汚染度で正直に出す。
            "contam" => $"汚染上昇 ×{Mathf.Max(0f, 1f - 0.15f * (lv >= 2 ? 3 : lv)):0.00}・心の効率 ×{_game?.KindnessGainMulAt(lv >= 2 ? 3 : lv) ?? 1f:0.00}",
            "imp_mult" => $"獲得心 ×{1f + 0.12f * lv:0.00}",
            "fol_gain" => $"口コミ ×{1f + 0.15f * lv:0.00}", // “拡散 ×N”はショットモード「拡散」と紛らわしいため別名（実体＝フォロワー獲得倍率）
            "combo_hold" => $"コンボ猶予 {2.0 + 0.4 * lv:0.0}秒",
            // バックファイア：段数で威力/間隔/追尾。lv は bf_power の段数を使う（rate/track は別ノード）。
            "bf_power" => $"後方弾 威力 {1 + lv}",
            "bf_rate" => $"後方弾 間隔 {new[] { 0.9f, 0.7f, 0.55f }[Mathf.Clamp(lv, 0, 2)]:0.00}秒",
            "bf_track" => lv == 0 ? "旋回60・単発" : "旋回90・同時2発",
            _ => $"Lv{lv}",
        };
    }

    // ノードID → (系統プレフィクス, 段数上限)。末尾の "_N" を落として系統名を得る。
    //   例 "fire_rate_2"→("fire_rate",4)、"spread_power_1"→("spread_power",2)、"spread_1"→("spread",3)。
    private static (string prefix, int max) ChainInfo(string id)
    {
        int u = id.LastIndexOf('_');
        string prefix = (u > 0 && int.TryParse(id[(u + 1)..], out _)) ? id[..u] : id;
        int max = prefix switch
        {
            "fire_rate" or "shot_power" or "imp_mult" => 4,
            "spread" or "homing" or "move_speed" or "hitbox" => 3,
            "spread_rate" or "homing_rate" or "bf_track" => 1,
            "bf_power" => 3,
            _ => 2,
        };
        return (prefix, max);
    }

    // 系統の実効 Lv（連続所持段数）を、focus 中ノードの所持見立て（withThisNode）込みで算出。
    //   現状の所持段数 base を数え、focus ノードのステップ番号が base+1（＝次に買う段）なら withThisNode で +1 する。
    //   focus ノードが既に所持段以下なら base をそのまま（"いま"＝現状）／withThisNode でも既所持は変わらない。
    private int EffLevel(string prefix, int max, string focusId, bool withThisNode)
    {
        int baseLv = _game?.ChainLevel(prefix, max) ?? 0;
        // focus ノードのステップ番号を取り出す（"prefix_N"）。取れなければ base のまま。
        int u = focusId.LastIndexOf('_');
        if (u <= 0 || !int.TryParse(focusId[(u + 1)..], out int step)) return baseLv;
        if (withThisNode && step == baseLv + 1) return Mathf.Min(max, baseLv + 1);
        return baseLv;
    }

    // モード別の射撃プレビュー（ミナ＋流れる光弾。連射=直線/拡散=扇/ホーミング=曲射）。
    private void DrawModeField(float x, float y, float w, float h, int i, bool locked)
    {
        UiKit.Box(this, new Rect2(x, y, w, h), new Color("0a1020"), 12f, new Color(1, 1, 1, 0.08f), 1f);
        UiKit.RadialGlow(this, new Vector2(x + w * 0.08f, y + h / 2f), w * 0.4f, UiKit.Info, 0.14f);
        for (float yy = y; yy < y + h; yy += 3f) DrawRect(new Rect2(x, yy, w, 1f), new Color(0, 0, 0, 0.16f));

        float t = (float)_t;

        // 射撃リズム（モード別）に同期した微リコイル＋アンティシペーション。
        float cycle = i == 0 ? 0.7f : i == 1 ? 1.0f : 1.3f;       // DrawLightBullet の位相と同周期
        float fp = (t / cycle) % 1f;                               // 0..1 発射位相
        float recoil;                                             // +で後方(左)へ引く
        if (fp < 0.12f) recoil = -Mathf.Lerp(0f, 2f, fp / 0.12f); // タメ：わずか前傾(前進)
        else if (fp < 0.30f) recoil = Mathf.Lerp(-2f, 5f, (fp - 0.12f) / 0.18f); // 発射：後方へキック
        else recoil = Mathf.Lerp(5f, 0f, (fp - 0.30f) / 0.70f);   // 余韻：ゆっくり戻す
        float breath = Mathf.Sin(t * 2.6f) * 4f;
        Vector2 mina = new(x + 28 - recoil, y + h / 2f + breath);

        if (!locked)
        {
            // 光弾の発射口は突き出した右手のあたり（mina中心より右寄り・腕の高さで少し上）。
            float x0 = mina.X + 30, x1 = x + w - 8;
            Vector2 muzzle = new(x0, mina.Y - 3f);
            switch (i)
            {
                case 0: // 連射：直線レーン
                    int lines = Mathf.Clamp(2 + (_game?.ChainLevel("shot_power", 4) ?? 0) / 2, 2, 4);
                    float[] offs = lines <= 2 ? new[] { -5f, 5f } : lines == 3 ? new[] { -8f, 0f, 8f } : new[] { -10f, -4f, 4f, 10f };
                    foreach (float dy in offs)
                        for (int k = 0; k < 4; k++)
                        {
                            float ph = (t / 0.7f + k / 4f) % 1f;
                            DrawLightBullet(new Vector2(Mathf.Lerp(x0, x1, ph), mina.Y + dy), 4f, ph);
                        }
                    break;
                case 1: // 拡散：扇状
                    int n = Mathf.Max(5, _game?.SpreadWays ?? 5);
                    for (int b = 0; b < n; b++)
                    {
                        float tt = n == 1 ? 0f : (float)b / (n - 1) - 0.5f;
                        float ang = tt * Mathf.DegToRad(70f); // 実弾道（Player.FireSpread ±35°＝全幅70°）と一致させる
                        float ph = (t / 1.0f + b * 0.06f) % 1f;
                        Vector2 dir = new(Mathf.Cos(ang), Mathf.Sin(ang));
                        DrawLightBullet(muzzle + dir * (ph * (w * 0.72f)), 3.5f, ph);
                    }
                    break;
                case 3: // 加速球：muzzle 付近でタメ→後半で一気に右へ発進（琥珀）。
                    for (int k = 0; k < 3; k++)
                    {
                        float ph = (t / 1.3f + k / 3f) % 1f;
                        // 前半0.55はタメ（muzzle付近でほぼ静止＝わずかに前進）、後半で一気に x1 へ発進。
                        const float chargeK = 0.55f;
                        float px = ph < chargeK
                            ? Mathf.Lerp(x0, x0 + 12f, ph / chargeK)                 // タメ：ほぼ静止
                            : Mathf.Lerp(x0 + 12f, x1, (ph - chargeK) / (1f - chargeK)); // 発進：一気に
                        var ac = new Color(0.96f, 0.78f, 0.36f); // 琥珀（加速球色）
                        if (ph >= chargeK) // 発進中は尾を引く
                            DrawLine(new Vector2(px, mina.Y), new Vector2(px - 10f, mina.Y), new Color(ac.R, ac.G, ac.B, 0.5f), 3f, true);
                        DrawCircle(new Vector2(px, mina.Y), 4f, ac);
                    }
                    break;
                default: // ホーミング（i=2）：標的へ曲射
                    Vector2[] tg = { new(x + w - 26, y + 20), new(x + w - 20, y + h - 18) };
                    int shots = Mathf.Max(2, _game?.HomingShots ?? 2);
                    foreach (var tp in tg)
                    {
                        DrawCircle(tp, 7f, new Color(0.27f, 0.09f, 0.2f));
                        DrawArc(tp, 8f, 0, Mathf.Tau, 18, new Color(UiKit.Kegare, 0.6f), 1.2f, true);
                    }
                    for (int s = 0; s < shots; s++)
                    {
                        var tp = tg[s % tg.Length];
                        float ph = (t / 1.3f + s * 0.22f) % 1f;
                        float e = ph * ph * (3f - 2f * ph); // smoothstep
                        Vector2 mid = new(muzzle.X + (tp.X - muzzle.X) * 0.5f, muzzle.Y);
                        Vector2 p = QuadBezier(muzzle, mid, tp, e);
                        DrawLightBullet(p, 3.5f, ph);
                    }
                    break;
            }
        }

        // ミナ：右へ撃つ射撃ポーズの立ち絵（呼吸揺れ＋微リコイルは mina 座標に反映済み）。
        UiKit.RadialGlow(this, mina, 22f, UiKit.Mina, 0.6f);
        if (_minaShot != null)
        {
            float dh = Mathf.Min(h - 12f, 86f);
            float dw = dh * _minaShot.GetWidth() / _minaShot.GetHeight();
            // 体の中心を mina に合わせ、突き出した右手（テクスチャ右端寄り）が発射口側へ来るよう配置。
            var dst = new Rect2(mina.X - dw * 0.5f, mina.Y - dh * 0.5f, dw, dh);
            DrawTextureRect(_minaShot, dst, false);
        }
        else
        {
            // フォールバック（テクスチャ未ロード時）。
            DrawCircle(mina, 11f, UiKit.Mina);
            DrawCircle(mina - new Vector2(2, 3), 4f, new Color(1, 1, 1, 0.9f));
        }

        if (locked)
        {
            DrawRect(new Rect2(x, y, w, h), new Color(8 / 255f, 6 / 255f, 14 / 255f, 0.66f));
            UiKit.Text(this, UiKit.ZenBold, new Vector2(x, y + h / 2f - 8), "ツリーで解放", 13, UiKit.Text2, HorizontalAlignment.Center, w);
        }
    }

    private static Vector2 QuadBezier(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    private void DrawLightBullet(Vector2 p, float r, float ph)
    {
        float a = ph < 0.12f ? ph / 0.12f : (ph > 0.86f ? (1f - ph) / 0.14f : 1f);
        UiKit.RadialGlow(this, p, r * 2.4f, Light, 0.55f * a);
        DrawCircle(p, r, new Color(Light, a));
        DrawCircle(p - new Vector2(r * 0.3f, r * 0.3f), r * 0.4f, new Color(1, 1, 1, a));
    }

    // 背景の浮遊粒：ツリー領域をゆっくり昇る淡い光の粒。座標は index からの決定的擬似乱数
    //   （new/割り当てなし・時間で位相を進めるだけ）。動きの無さ＝地味さへの最小コストの回答。
    // 背景モーションはツリー（カメラ）座標系＝_ci へ描く。仮想ツリー全域に散らし、スクロールしても粒が続く。
    private void DrawBgMotes()
    {
        const int n = 30;
        float top = TreeTop, bot = TreeVirtB, span = bot - top;
        for (int i = 0; i < n; i++)
        {
            float sx = (i * 131 % 97) / 97f;                 // 0..1 横位置の散らし
            float speed = 0.06f + (i % 5) * 0.015f;          // 粒ごとに違う速度
            float phase = Mathf.PosMod((float)_t * speed + i * 0.137f, 1f);
            float x = 60f + sx * (TreeVirtR - 120f);         // 仮想ツリー横幅いっぱいに散らす
            float y = bot - phase * span;                    // 下→上へ
            float tw = 0.5f + 0.5f * Mathf.Sin((float)_t * 1.3f + i);   // またたき
            float fade = Mathf.Sin(phase * Mathf.Pi);        // 端で消える
            _ci.DrawCircle(new Vector2(x, y), 1.3f, new Color(UiKit.PurifyHi, 0.16f * tw * fade));
        }
    }

    // 購入バースト：光のフラッシュ＋スパーク環。ノード位置（ツリー座標）で光るので _ci＝ツリー層へ描く
    // （DrawTreeLayer から呼ばれる＝カメラでスクロールしても発生ノードに貼り付いたまま）。
    private void DrawBuyFx()
    {
        if (_buyFxT <= 0) return;
        float p = 1f - (float)(_buyFxT / 0.7);
        float a = 1f - p;
        UiKit.RadialGlow(_ci, _buyFxAt, 80f * (0.5f + p), Light, 0.5f * a);
        const int n = 10;
        for (int i = 0; i < n; i++)
        {
            float ang = Mathf.Tau * i / n;
            float rr = 14f + p * 84f;
            _ci.DrawCircle(_buyFxAt + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr, 2.8f * (1f - p), new Color(1, 1, 1, a));
        }
    }

    // モードスウィープ（装備切替時、中央に MODE ▸ 名称が横切る）。
    private void DrawModeSweep()
    {
        if (_sweepT <= 0) return;
        float k = 1f - (float)(_sweepT / 1.1);    // 0→1 進行
        float a = k < 0.18f ? k / 0.18f : (k > 0.82f ? (1f - k) / 0.18f : 1f);
        float slide = (k - 0.5f) * 220f;          // 横切り
        string t = "MODE ▸ " + _sweepName;
        float tw = UiKit.TextW(UiKit.ZenBlack, t, 32) + 90;
        float x = W / 2f - tw / 2f + slide, y = 330f;
        UiKit.Box(this, new Rect2(x, y, tw, 60f), new Color(0.06f, 0.11f, 0.16f, 0.92f * a), 16f, new Color(UiKit.Info, 0.6f * a), 1.4f);
        UiKit.Text(this, UiKit.ZenBlack, new Vector2(x, y + 14), t, 32, new Color(UiKit.PurifyHi, a), HorizontalAlignment.Center, tw);
    }

    private float Hint(float x, float y, string key, string label, bool accent)
    {
        Color kbg = accent ? new Color(UiKit.Info, 0.12f) : new Color(1, 1, 1, 0.07f);
        Color kbd = accent ? new Color(UiKit.Info, 0.5f) : new Color(1, 1, 1, 0.16f);
        UiKit.Key(this, new Vector2(x, y - 12), key, kbg, kbd, accent ? UiKit.PurifyHi : UiKit.Text2);
        float kw = Mathf.Max(24f, UiKit.TextW(UiKit.Mono, key, 12) + 12f);
        UiKit.Text(this, UiKit.Zen, new Vector2(x + kw + 8, y - 8), label, 14, accent ? UiKit.Info : UiKit.Text3);
        return x + kw + 8 + UiKit.TextW(UiKit.Zen, label, 14) + 22f;
    }

    private void DrawToast()
    {
        if (_toastT <= 0) return;
        float w = UiKit.TextW(UiKit.ZenBold, _toast, 16) + 48;
        float x = (W - w) / 2f;
        UiKit.Box(this, new Rect2(x, H - 96, w, 38f), new Color(0.06f, 0.05f, 0.10f, 0.96f), 12f, new Color(_toastCol, 0.7f), 1f);
        UiKit.Text(this, UiKit.ZenBold, new Vector2(x, H - 88), _toast, 16, _toastCol, HorizontalAlignment.Center, w);
    }
}
