using Godot;

// StageTutorial : 初見チュートリアル会話（ミナ）の once 管理と本文の一元置き場（2026-09-16）。
//   ①道中 … セーブで最初にステージの道中へ入ったとき（各ステージ _Ready の _step==1 確定後、
//            イントロ会話の末尾へ Concat ＝道中ザコ戦が始まる直前に流れる）。
//   ②本ボス … 言葉の板（パネル）が周回する本ボス戦の初回開始時（各ステージ Step_BossSpawn が
//            ボス口上（_playerBoss）の末尾へ Concat ＝消費もこの瞬間なので、道中でゲームオーバー
//            しても初ボス到達まで温存される）。
//   once キーはセーブ単位（GameManager._idleDialogSeen）。"once_" 接頭辞は小話の既読リセット
//   （ResetIdleDialogSeen）でも消えない＝once_phone_home と同じ流儀。
//   ・結び手（ミナ潜行）のときだけ表示する。他ジョブのキャラ別ストーリー中は出さず、
//     once も消費しない＝結び手の初回で必ず見られる。
//   ・本文の正典: docs/20260916/フェイクレルム_チュートリアル本文_2026-09-16.md（ユーザー支給草稿準拠・
//     操作表記は HowToPlay/Player の実装が正典）。差し替えはこの配列だけ＝フック側は無改造。
//   ・2026-09-17: 操作表記（キー名の羅列）をミナのセリフから抜き、盤面中央の操作カード（ControlCard）へ
//     分離した。セリフは「何ができるか・なぜするか」、カードは「どのボタンか」を担う。行ごとの
//     出し分けは下の RouteCues（Route と同じ並び・同じ長さ）が持つ。
public static class StageTutorial
{
    public const string RouteSeenKey = "once_tutorial_route";
    public const string BossSeenKey = "once_tutorial_boss";
    public const string AnkerAkariSeenKey = "once_ankers_akari";
    public const string AnkerKoharuSeenKey = "once_ankers_koharu";
    public const string AnkerReiSeenKey = "once_ankers_rei";

    private const string MFace = "res://char/mina_face.png";
    private const string MWorried = "res://char/mina_worried.png";
    private static readonly (int who, string text, string face)[] None = System.Array.Empty<(int, string, string)>();

    // ① 道中チュートリアル（ブロック1・16行）。who=1（ミナ）のみ。
    private static readonly (int who, string text, string face)[] Route =
    {
        (1, "偽りの世界——「フェイクレルム」。ここは、本音を隠した世界です。……観測を、はじめます。", MFace),
        (1, "悪い者たち——通称「アンチャー」が、ここを汚染して、本音を隠しています。ひとつずつ、浄化していきます。", MFace),
        (1, "左のパネルの「浄化」。あれが100%になれば——この偽りの世界を作った方の、本音を、引き出せます。", MFace),
        (1, "ご注意を。アンチャーの攻撃や接触を、わたくしの中心の「コア」に受けると、LIFEが減ります。", MWorried),
        (1, "操作を、お伝えします。まず、移動を。……この盤面のどこへでも、お連れします。", MFace),
        (1, "光は、自動で放ちます。撃つボタンは、ありません。……狙うことだけを、考えてください。", MFace),
        (1, "攻撃をよけながら、アンチャーを浄化してください。——数えるのは、わたくしがやります。", MFace),
        (1, "アンチャーの周りを、板が回っています。あれが「アンフォールダー（苦しめる者）」。砕けば、アンチャーごと浄化できます。", MFace),
        (1, "アンチャーは、背後からも来ます。狙い撃ちには、ロックオンを。", MFace),
        (1, "押すたびに、いちばん近い敵から、次に近い敵へ——狙いが移ります。そのあいだ、少し、足が重くなります。", MFace),
        (1, "狙いを捨てたいときは、解除を。その場ですぐ外せます。……相手が浄化されるか、離れれば、ひとりでに外れますので、急がずとも結構です。", MFace),
        (1, "よけきれないとき、まとめて祓いたいときは——BOMB を。", MWorried),
        (1, "画面の弾ごと、アンチャーを一掃します。……残数の、あるかぎり、ですが。", MFace),
        (1, "浄化したアンチャーからは、「心の欠片」がこぼれます。拾っておいてください。のちほど、必ず、役に立ちます。", MFace),
        (1, "——起動記録には、operator と、ありました。", MFace),
        (1, "では。オペレータのお仕事を、よろしくお願いいたします。……ご主人様。", MFace),
    };

    // 道中16行それぞれで盤面中央に出す操作カードの話題（Route と同じ並び・同じ長さ）。
    //   None の区間はカードを畳む＝世界観の語り（フェイクレルム／アンチャー／欠片／締め）に集中させる。
    //   同じ話題が続く行ではカードを出しっぱなしにする＝1行ごとに点滅しない。
    //   5行目=移動 / 6〜7行目=撃つ（自動射撃の念押しまで） / 9〜10行目=ロックオン送り（宣言・送りと減速）/
    //   11行目=ロックオン解除（別ボタン・2026-09-17 追加）/ 12〜13行目=ボム（宣言と効果）。
    private static readonly ControlCard.Topic[] RouteCues =
    {
        ControlCard.Topic.None,   //  1 フェイクレルムの説明
        ControlCard.Topic.None,   //  2 アンチャー
        ControlCard.Topic.None,   //  3 浄化ゲージ
        ControlCard.Topic.None,   //  4 コアとLIFE
        ControlCard.Topic.Move,   //  5 移動
        ControlCard.Topic.Shot,   //  6 自動射撃
        ControlCard.Topic.Shot,   //  7 よけながら浄化（撃つ話の続き＝カードは据え置き）
        ControlCard.Topic.None,   //  8 アンフォールダー
        ControlCard.Topic.Lock,   //  9 ロックオン
        ControlCard.Topic.Lock,   // 10 送りと減速
        ControlCard.Topic.LockClear, // 11 解除（送りとは別ボタン）
        ControlCard.Topic.Bomb,   // 12 BOMB
        ControlCard.Topic.Bomb,   // 13 一掃と残数
        ControlCard.Topic.None,   // 14 心の欠片
        ControlCard.Topic.None,   // 15 operator
        ControlCard.Topic.None,   // 16 締め
    };

    // ② 本ボス戦チュートリアル（ブロック2・5行）。ボスの口上（who=2）の直後に続く。
    private static readonly (int who, string text, string face)[] Boss =
    {
        (1, "——あの方が、このフェイクレルムを作った、主です。", MWorried),
        (1, "心の周りを、アンフォールダーが回っています。すべて砕けば——ご本人へ、直接の浄化が、届きます。", MFace),
        (1, "ただし。主のアンフォールダーは、アンチャーのものとは、比べものになりません。時間が経てば、よみがえります。", MWorried),
        (1, "よみがえるたび、砕いて。——届くまで、何度でも。", MFace),
        (1, "はじめましょう、ご主人様。……あの方の本音を、引き出します。", MFace),
    };

    // ③ アンチャー紹介（2026-09-17）。道中へ入る直前に、その面のアンチャー「全体の性格」だけを掴ませる。
    //   個別の12種は解説しない（画面に敵名が出ないので名指ししても照合できない＝覚える作業になるだけ）。
    //   道中チュートリアル（①）の後ろへ繋ぐ＝「一般（操作と世界のルール）→ 個別（この面の敵の傾向）」の順。
    //   once はステージごと（面ごとに一度きり）＝①の once_tutorial_route（セーブ全体で一度）とは別キー。
    //   本文の正典: docs/20260917/アンチャー紹介_本文_2026-09-17.md（ユーザー確認済み・一字も変えない）。
    private static readonly (int who, string text, string face)[] AnkerAkari =
    {
        (1, "この階のアンチャーは、間合いを詰めてまいります。落ちてくるもの、途中から速くなるもの。", MWorried),
        (1, "立っていられる場所を、少しずつ削られます。……あの方が、そうされてきたように。", MWorried),
        (1, "同じ場所に留まらないでください。それだけで、だいぶ違います。", MFace),
    };

    private static readonly (int who, string text, string face)[] AnkerKoharu =
    {
        (1, "ここのアンチャーは、一度に、たくさん寄越してきます。構えたと思った次の瞬間には、もう来ています。", MWorried),
        (1, "そのくせ、置いていくだけのものも混じっています。……速いものと、遅いものが、同じ部屋に。", MFace),
        (1, "来る合図は必ず出ます。合図を見てから、一歩。欲張らないでいただければ。", MFace),
    };

    private static readonly (int who, string text, string face)[] AnkerRei =
    {
        (1, "この枠のアンチャーは、正面から来ません。上から、まわりから、背中から。", MWorried),
        (1, "見られている方向が、多すぎるのです。逃げ場が、読みにくい。", MWorried),
        (1, "光る線が走ったら、そこは通れません。……ただ、線を引いた者を先に浄化すれば、線ごと消えます。", MFace),
        (1, "落ち着いて、正面を空けてください。ご主人様。", MFace),
    };

    // ───────── 操作カード（盤面中央のウィンドウ）の同期 ─────────
    // 各ステージの ShowLine が「いま出した配列と行番号」を渡してくるだけ＝ステージ側はカードの存在を知らない。
    //   lines が道中チュートリアル（Route）そのものでない限り何もしない（＝通常の会話では一切出ない・
    //   他ジョブのキャラ別ストーリーにも混ざらない）。ただし Route はイントロ末尾へ Concat されて渡るため、
    //   「いま出している行が Route の何行目か」を実体参照（ReferenceEquals）で引き当てて話題を選ぶ。
    //   ※2026-09-17: 以前は「配列の末尾16行が Route」と決め打ちして添字をずらしていたが、Route の
    //     さらに後ろへアンチャー紹介（③）を Concat した途端に末尾が Route でなくなり、カードが一切
    //     出なくなる作りだった。Route の各行は他所に無い固有の文字列リテラル＝参照一致で一意に引ける。
    //   カードは Hud（CanvasLayer）の子として遅延生成し、以後は使い回す。
    public static void SyncCard(Hud? hud, (int who, string text, string face)[] lines, int index)
    {
        if (hud == null) return;
        var card = hud.GetNodeOrNull<ControlCard>("ControlCard");
        int cue = -1;
        if (index >= 0 && index < lines.Length)
            for (int i = 0; i < Route.Length && i < RouteCues.Length; i++)
                if (ReferenceEquals(lines[index].text, Route[i].text)) { cue = i; break; }
        if (cue < 0)
        {
            card?.Dismiss();   // チュートリアル以外の行に移った＝畳む（生成前なら何もしない）
            return;
        }
        card ??= ControlCard.Attach(hud);
        card.Show(RouteCues[cue]);
    }

    // 道中開始時に一度だけ返す（返した瞬間に once を消費）。出さない条件では None（消費もしない）。
    public static (int who, string text, string face)[] TakeRoute(GameManager? game) => Take(game, RouteSeenKey, Route);

    // 本ボス出現時に一度だけ返す。同上。
    public static (int who, string text, string face)[] TakeBoss(GameManager? game) => Take(game, BossSeenKey, Boss);

    // アンチャー紹介（③）。道中開始時に面ごと一度だけ返す。①と同じく結び手潜行のときだけ・消費も同条件。
    public static (int who, string text, string face)[] TakeAnkerAkari(GameManager? game) => Take(game, AnkerAkariSeenKey, AnkerAkari);
    public static (int who, string text, string face)[] TakeAnkerKoharu(GameManager? game) => Take(game, AnkerKoharuSeenKey, AnkerKoharu);
    public static (int who, string text, string face)[] TakeAnkerRei(GameManager? game) => Take(game, AnkerReiSeenKey, AnkerRei);

    private static (int who, string text, string face)[] Take(
        GameManager? game, string key, (int who, string text, string face)[] lines)
    {
        if (game == null) return None;
        if (CharacterStory.DiveActive(game)) return None;   // 他ジョブ潜行＝出さない・once も消費しない
        if (game.IsIdleDialogSeen(key)) return None;        // セーブ単位で一度きり
        game.MarkIdleDialogSeen(key);                       // 表示が確定した瞬間に消費（次回セーブで永続）
        GD.Print($"[tutorial] {key} fired");                // ヘッドレスQAの発火確認用
        return lines;
    }
}
