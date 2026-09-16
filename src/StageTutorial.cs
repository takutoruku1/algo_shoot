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
public static class StageTutorial
{
    public const string RouteSeenKey = "once_tutorial_route";
    public const string BossSeenKey = "once_tutorial_boss";

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
        (1, "操作を、お伝えします。移動——キーボードは、矢印かWASD。コントローラーは、Lスティックか十字キー。", MFace),
        (1, "マウスは、カーソルの位置へ、わたくしが寄っていきます。……光は、自動で放ちます。撃つボタンは、ありません。", MFace),
        (1, "攻撃をよけながら、アンチャーを浄化してください。——数えるのは、わたくしがやります。", MFace),
        (1, "アンチャーの周りを、板が回っています。あれが「アンフォールダー（苦しめる者）」。砕けば、アンチャーごと浄化できます。", MFace),
        (1, "アンチャーは、背後からも来ます。狙い撃ちには、ロックオンを。キーボードはF、コントローラーはRB、マウスは左クリックを短く。", MFace),
        (1, "押すたびに、いちばん近い敵から、次に近い敵へ——狙いが移ります。そのあいだ、少し、足が重くなります。", MFace),
        (1, "解除は、マウスの右クリック。キーボードとコントローラーに、解除ボタンはありません。相手が浄化されるか、離れれば、外れます。", MFace),
        (1, "よけきれないとき、まとめて祓いたいときは——BOMB。キーボードはX、コントローラーもX、マウスは中クリック。", MWorried),
        (1, "画面の弾ごと、アンチャーを一掃します。……残数の、あるかぎり、ですが。", MFace),
        (1, "浄化したアンチャーからは、「心の欠片」がこぼれます。拾っておいてください。のちほど、必ず、役に立ちます。", MFace),
        (1, "——起動記録には、operator と、ありました。", MFace),
        (1, "では。オペレータのお仕事を、よろしくお願いいたします。……ご主人様。", MFace),
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

    // 道中開始時に一度だけ返す（返した瞬間に once を消費）。出さない条件では None（消費もしない）。
    public static (int who, string text, string face)[] TakeRoute(GameManager? game) => Take(game, RouteSeenKey, Route);

    // 本ボス出現時に一度だけ返す。同上。
    public static (int who, string text, string face)[] TakeBoss(GameManager? game) => Take(game, BossSeenKey, Boss);

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
