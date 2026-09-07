using Godot;

// CheckpointFlow : チェックポイント入口と「初回ショップ説明」導線の共通処理。
//   3ステージ（レイ/あかり/こはる）の中ボス(cameo)撃破フックから呼ぶ。
//   ・中ボス撃破を GameManager に記録（「中ボスから」入口の解放ゲート＝永続）。
//   ・強化ショップの説明パート（ShopTutorial）は「最初の面のボスを倒した後」に一度きり出す。
//     2026-09-07 まで初回の中ボス撃破時に道中を抜けて出していたが、強化そのものが
//     最初の面のボスクリアまで解禁されなくなった（Hub.ShopUnlocked）ため、説明だけ先に出ると
//     「案内されたのに入れない」ことになる。説明はショップが開くのと同じ瞬間へ移した。
public static class CheckpointFlow
{
    // オートプレイ（--demo/--qa）中か。進行を乱さないため、説明パートへは離脱しない。
    private static bool Autoplay()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") return true;
        return false;
    }

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

    // ステージのボス撃破直後（CompleteStage の直前）に Stage から呼ぶ。
    //   最初の面＝強化ショップが解禁される面のボスを、全ゲーム通して初めて倒したときだけ、
    //   ハブへ帰らずショップ説明パートへ離脱する（ShopTutorialSeen を立てて以降は出ない）。
    //   説明を読み切ると ShopTutorial が Shop へ、Shop は X でハブへ戻る
    //   ＝「ボス撃破 → 説明 → ショップ → ハブ」。クリア記録はここで先に確定させる。
    // 戻り値 true ＝離脱した（呼び元はハブへの遷移を行わず return する）。
    public static bool OnBossCleared(Node stage, string stageId)
    {
        var game = stage.GetNodeOrNull<GameManager>("/root/Game");
        if (game == null) return false;
        if (stageId != GameManager.FirstStageId) return false;
        if (game.ShopTutorialSeen || Autoplay()) return false;

        game.ShopTutorialSeen = true;   // 一度きり（説明完了後にセーブで永続化）
        // クリアを先に確定＝説明→ショップ→ハブ と回っても、ハブが「あかりクリア済み」の状態で開く
        //   （強化が解禁され、帰還会話 JustClearedStageId もハブで通常どおり再生される）。
        game.CompleteStage(stageId);
        game.AutoSave();
        stage.GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
        stage.GetTree().ChangeSceneToFile("res://ShopTutorial.tscn");
        return true;
    }
}
