using Godot;

// CheckpointFlow : チェックポイント入口の共通処理。
//   3ステージ（レイ/あかり/こはる）の中ボス(cameo)撃破フックから呼ぶ。
//   ・中ボス撃破を GameManager に記録（「中ボスから」入口の解放ゲート＝永続）。
//
//   強化ショップの説明パート（ShopTutorial）の導線は 2026-09-22 にここから外した。
//     従来：最初の面のボス撃破時にハブへ帰らず説明へ離脱し、説明→ショップ画面と自動で開いていた。
//     それだと「ホームのアイコンを押してショップへ入る」操作をプレイヤーが一度も経験しないまま
//     ショップに放り込まれる。いまはボス撃破後かならずハブ（スマホのホーム）へ帰し、
//     帰還会話→ホーム解禁演出のあとにハブ側が説明を一度だけ挟む（Hub.ProcessHomeReveal）。
//     ＝「ボス撃破 → ハブ（帰還会話→解禁演出）→ 説明 → ホームでアイコン押下 → ショップ」。
public static class CheckpointFlow
{
    // 中ボス(cameo)撃破時に Stage から呼ぶ。
    //   stageId : "rei" / "akari" / "koharu"
    //   tutorial: そのランが操作チュートリアル中か（現在この分岐では未使用だが、呼び元の意味は残す）。
    // 戻り値 true ＝ステージを離脱した（呼び元は以降の進行＝Advance を行わず return する）。
    //   中ボス撃破では離脱しなくなったので常に false。記録（＝「中ボスから」入口の解放）だけを行う。
    public static bool OnMidBossCleared(Node stage, string stageId, bool tutorial)
    {
        var game = stage.GetNodeOrNull<GameManager>("/root/Game");
        if (game == null) return false;
        game.MarkMidBossCleared(stageId);
        return false;
    }
}
