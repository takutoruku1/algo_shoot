using Godot;

// CheckpointFlow : チェックポイント入口と「初回ショップ説明」導線の共通処理。
//   3ステージ（レイ/あかり/こはる）の中ボス(cameo)撃破フックから呼ぶ。
//   ・中ボス撃破を GameManager に記録（「中ボスから」入口の解放ゲート＝永続）。
//   ・全ゲーム通して“初めて”中ボスを倒した瞬間だけ、そのステージを抜けて
//     強化ショップの説明パートへ離脱する（ShopTutorialSeen を立てて以降は離脱しない）。
public static class CheckpointFlow
{
    // 中ボス(cameo)撃破時に Stage から呼ぶ。
    //   stageId : "rei" / "akari" / "koharu"
    //   tutorial: そのランが操作チュートリアル中か（チュートリアル中はショップ説明へは飛ばさず通常進行）。
    // 戻り値 true ＝ステージを離脱した（呼び元は以降の進行＝Advance を行わず return する）。
    public static bool OnMidBossCleared(Node stage, string stageId, bool tutorial)
    {
        var game = stage.GetNodeOrNull<GameManager>("/root/Game");
        if (game == null) return false;

        bool firstEver = game.MarkMidBossCleared(stageId);

        // オートプレイ（--demo/--qa）は進行を乱さないため離脱しない（中ボス撃破記録だけ残す）。
        bool autoplay = false;
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a == "--demo" || a == "--qa") { autoplay = true; break; }

        // 「全ゲーム通して初めて中ボスを倒した」かつ未読・非チュートリアル・非オート のときだけショップ説明へ離脱。
        if (firstEver && !game.ShopTutorialSeen && !tutorial && !autoplay)
        {
            game.ShopTutorialSeen = true;  // 一度きり（説明完了後にセーブで永続化）
            game.AutoSave();               // ランで貯めたインプレ＋既読＋中ボス撃破フラグを確定保存
            stage.GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            stage.GetTree().ChangeSceneToFile("res://ShopTutorial.tscn");
            return true;
        }
        return false;
    }
}
