using Godot;

// Hud.BacklogFeed : Hud を使わず自前で会話を描く画面から会話ログ（Hud.Backlog）へ行を積む入口。
//   対象＝Prologue / Final / Epilogue（各カットシーン）、OpeningFilm / EndingFilm（字幕）、Hub の会話、
//   ShopTutorial。どれも Hud.SetDialog を通らないため、これまで会話ログに残らなかった
//   （ユーザー指示 2026-09-26「オープニングから全シーンでログが開けるように」）。
//   本体（PushBacklog / BacklogSpeaker / KindColor）は Hud.cs 側。ここは partial で同じクラスに
//   公開の呼び口を1つ足すだけ＝積み方（空行は捨てる／直前と同一の行は弾く／上限 200 行）は Hud と同じ。
public partial class Hud
{
    // 1行を会話ログへ積む。speaker が空なら種別から補う（ミナ＝MinaLabel／投稿＝「Ｘ 投稿」／ナレ＝「ナレーション」）。
    //   color が未指定（default）なら種別の既定色（KindColor）。各画面の表示直後に、表示した行と同じ本文で呼ぶこと。
    public static void PushLog(LineKind kind, string speaker, string text, Color color = default)
    {
        if (string.IsNullOrEmpty(text)) return;
        Color col = color.A <= 0f ? KindColor(kind) : color;
        PushBacklog(BacklogSpeaker(kind, speaker), text, col, kind);
    }
}
