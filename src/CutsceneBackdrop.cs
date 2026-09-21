using Godot;

// CutsceneBackdrop : カットシーン（回想フィルム／FINAL のフェーズ間）の**背後を確実に塞ぐ不透明の黒板**。
//
// なぜ要るか（2026-09-22 ユーザー実機指摘「BOSSのステージイラストが選択肢の直前やステージ切り替えで出る」）:
//   StoryFilm / MinaPhaseScene は**ノード全体の Modulate を α 0→1** でフェードインする作りだった。
//   このフェードはレターボックスの黒帯（_Draw の DrawRect 2本）にも等しく乗るため、入りと明けの
//   0.5〜0.65 秒のあいだ**画面全体が半透明**になり、背後がそのまま透ける。
//   さらに StoryFilm._Ready が止めるのは World と GameManager の ProcessMode だけで、
//   StageBackground / BgLayers は **World の子ではなく Root の兄弟**（ReiRoot.cs:70 ほか）なので
//   止まりも隠れもしない。しかも EnterBoss() 後は Mode.Boss が立ちっぱなし（StageBackground.cs:248）
//   ＝ボス撃破後のクリア会話・回想のあいだ中ずっとボス背景が出ている。
//   結果、aftermath 回想の入り／明けで**ボスのステージイラストが丸見え**になっていた。
//
// 方式（なぜ「板を敷く」を選んだか）:
//   ・フィルム側のフェードを畳んで TextureRect の α だけ動かす案もあるが、StoryFilm は
//     _Draw の帯・時制見出し・Hud の会話欄と**複数の描画経路が同じ α に乗っている**ので、
//     全部を個別に α 管理へ直すと回帰範囲が広い（時制見出しは独自の outA を持つ等）。
//   ・背景レイヤー側を隠す案（stagebg グループを Visible=false）も、EnterBoss/BeginRoute/
//     CrossfadeTo の内部状態を触らずに戻す保証が要り、**戦闘中の正規表示を壊す危険**がある。
//   → カットシーンと背景のあいだに**不透明の板を1枚差し込む**のが、既存の描画経路と
//     背景の状態機械の**どちらにも触らずに「確実に透けない」を満たす**最短手。
//     板はカットシーン本体**が描くどの要素よりも奥**に置き、最初から不透明で敷き、
//     本体が消えきった**後に**引く＝どの瞬間を切り取っても背後が出ない。
//
// 置き場所は Hud(CanvasLayer) の直下＝カットシーン本体と同じ親。ワールドのカメラや
// StageBackground の ZIndex(-90..-88) とは別レイヤーなので、確実に手前を覆える。
//
// ★z の取り方（2026-09-22 の回帰。板が回想の絵まで隠して画面が真っ黒になった）:
//   最初は「本体の ZIndex の1つ下」にしていたが、StoryFilm は絵の TextureRect を
//   **ZAsRelative（既定）の ZIndex=-1** で持つ＝実効 z は親(-10)+(-1)= -11。
//   板も絶対 -11 だったため**同じ z**になり、後から AddChild した板が勝って絵を覆っていた。
//   → 本体が内部で使う相対 z のぶんを見込んで、Depth だけ深い固定のマージンを取る。
//     単に -2 にすると本体がもう一段深い子を足した瞬間にまた衝突するので、
//     「カットシーンの内部構造がどう深くなっても届かない深さ」を1箇所の定数で宣言する。
public partial class CutsceneBackdrop : ColorRect
{
    // 板は**最初のフレームから不透明**にする。ここをフェードインにすると、立ち上がりきるまでの
    //   数フレームは板ごと半透明＝背後がそのまま透ける（実測：立ち上げ 0.28s だと fade-in 3 フレーム目で
    //   ボス背景・自機・ボスカードがはっきり見えた。build/qa_story/backdrop/shots/01_fadein_03.png）。
    //   「暗転してから絵が浮かぶ」のは回想の入りとして自然な運びなので、演出上も黒からで正しい。
    // 引きだけは時間をかける（本体が消えきってから呼ばれる＝ここが回想明けの暗転になる）。
    private const double FallTime = 0.3;
    private double _t;
    private bool _leaving;

    // 本体の ZIndex から何段深くに板を敷くか。本体が相対 z の子（StoryFilm の絵は -1）を
    //   持っていても確実に下回るだけの余裕を取る。StageBackground(-90..-88) よりは十分浅いので、
    //   広げても「背景より奥に沈んで意味を失う」側には倒れない。
    private const int Depth = 8;

    // カットシーン本体（film）の裏に黒板を差し込む。film は hud の子である前提。
    // baseZ は本体の ZIndex（StoryFilm / MinaPhaseScene とも -10）。板はそこから Depth 段奥へ置く。
    public static CutsceneBackdrop Attach(Hud hud, int baseZ)
    {
        var pad = new CutsceneBackdrop
        {
            Name = "CutsceneBackdrop",
            Color = Colors.Black,   // 生成した瞬間から不透明（FallTime のコメント参照）
            Size = new Vector2(384, 216),
            ZIndex = baseZ - Depth,
            ZAsRelative = false,
            MouseFilter = MouseFilterEnum.Ignore,
            // カットシーンは World/Game を止めるが、板自身は自分でフェードを進める必要がある。
            ProcessMode = ProcessModeEnum.Always,
        };
        hud.AddChild(pad);
        return pad;
    }

    // カットシーン本体が消え終わってから呼ぶ（Restore() 相当のタイミング）。板は自分で引いて消える。
    public void Dismiss()
    {
        if (_leaving) return;
        _leaving = true;
        _t = 0;
    }

    // 引いている間だけ働く（敷いている間は不透明のまま＝何もしない）。
    public override void _Process(double delta)
    {
        if (!_leaving) return;
        _t += delta;
        float k = Mathf.Clamp((float)(_t / FallTime), 0, 1);
        Color = new Color(0, 0, 0, 1 - k);
        if (k >= 1) QueueFree();
    }
}
