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
    //   cameo   : 呼び元 Stage が保持する CameoBoss（改心退場アニメが自然に QueueFree するまで
    //             シーン遷移を待つために使う。null なら待たずに即遷移＝旧挙動）。
    // 戻り値 true ＝ステージを離脱する（呼び元は以降の進行＝Advance を行わず return する）。
    //   ※ firstEver 分岐の実際のシーン遷移は cameo の退場演出が終わるまで非同期で遅延される。
    public static bool OnMidBossCleared(Node stage, string stageId, bool tutorial, Node? cameo = null)
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
            // 強化ショップのあと、このランの“中ボスの続き”（道中後半）から再開する＝中ボスを再戦させない。
            // ショップ退出が PendingResumeScene を消費してこのステージへ戻り、_Ready が AfterMidBoss を読んで Step_MidwaveB から始める。
            game.PendingResumeScene = stage.GetTree().CurrentScene?.SceneFilePath;
            game.SelectedEntry = GameManager.StageEntry.AfterMidBoss;
            game.AutoSave();               // ランで貯めたインプレ＋既読＋中ボス撃破フラグを確定保存
            stage.GetNodeOrNull<BulletPool>("/root/Pool")?.DespawnAll();
            // 通常（2回目以降）の撃破は Advance で次フェーズへ進むだけ＝シーンは変わらないので、
            // cameo は生かしたまま背後で改心退場アニメ（PurifiedExitHold/Fade）を自然に流し切れる。
            // だがここは ChangeSceneToFile でシーンごと畳む＝待たずに呼ぶと cameo が退場を1コマも
            // 見せないまま消える（中の人がフル不透明で見える演出が飛ぶ）。なので退場完了（自然な
            // QueueFree）を待ってからシーン遷移する。
            LeaveToShopTutorialAfterCameoExit(stage, cameo);
            return true;
        }
        return false;
    }

    // firstEver 分岐専用：cameo が退場アニメを終えて自然に QueueFree されるまで待ってからショップ説明へ遷移する。
    // cameo が null／既に無効なら待たずに即遷移（旧挙動と同じ）。
    private static async void LeaveToShopTutorialAfterCameoExit(Node stage, Node? cameo)
    {
        while (cameo != null && GodotObject.IsInstanceValid(cameo)
            && GodotObject.IsInstanceValid(stage) && stage.IsInsideTree())
        {
            var tree = stage.GetTree();
            if (tree == null) break;
            await stage.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
        if (GodotObject.IsInstanceValid(stage))
            stage.GetTree()?.ChangeSceneToFile("res://ShopTutorial.tscn");
    }
}
