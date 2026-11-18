using Godot;

// Hud.BacklogFeed : Hud を使わず自前で会話を描く画面から会話ログ（Hud.Backlog）へ行を積む入口。
//   対象＝Prologue / Final / Epilogue（各カットシーン）、OpeningFilm / EndingFilm（字幕）、Hub の会話
//   （帰還会話・小話・強化ショップの説明）。どれも Hud.SetDialog を通らないため、これまで会話ログに残らなかった
//   （ユーザー指示 2026-09-26「オープニングから全シーンでログが開けるように」）。
public partial class Hud
{
    internal string? StoryLogSection { get; set; }

    private string DialogLogSection()
    {
        if (StoryLogSection != null) return StoryLogSection;
        string scene = GetParent()?.SceneFilePath ?? "";
        foreach (var stage in GameManager.Stages)
            if (stage.Scene == scene)
                return stage.Title + (_game.BossReached ? " / ボス戦" : " / 道中");
        return scene switch
        {
            "res://MinaBattle.tscn" => _game.BossReached ? "FINAL — ミナ / ボス戦" : "FINAL / 道中",
            "res://Stage0.tscn" => "チュートリアル",
            BossRushRoot.ScenePath => "ボスラッシュ",
            BossRushRoot.SingleScenePath => "ボス単体戦",
            "res://Main.tscn" => "ステージ W0",
            _ when GetTree().CurrentScene is BossPracticeRoot => "ボス練習",
            _ => "ステージ",
        };
    }

    // 1行を会話ログへ積む。speaker が空なら種別から補う（ミナ＝MinaLabel／投稿＝「Ｙ 投稿」／ナレ＝「ナレーション」）。
    //   color が未指定（default）なら種別の既定色（KindColor）。各画面の表示直後に、表示した行と同じ本文で呼ぶこと。
    public static void PushLog(string section, LineKind kind, string speaker, string text, Color color = default)
    {
        if (string.IsNullOrEmpty(text)) return;
        speaker = BacklogSpeaker(kind, speaker);
        if (_backlog.Count > 0)
        {
            var last = _backlog[^1];
            if (last.Section == section && last.Speaker == speaker && last.Text == text) return;
        }
        _backlog.Add(new LogLine(section, speaker, text, color.A <= 0f ? KindColor(kind) : color, kind));
        if (_backlog.Count > BacklogMax) _backlog.RemoveAt(0);
    }
}
