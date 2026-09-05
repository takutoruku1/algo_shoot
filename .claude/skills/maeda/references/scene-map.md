# シーン ↔ 実装ファイル 対応表

シーンチェーン（正準順）: `Prologue → Rei → Akari → Koharu → Final → Epilogue`

| シーン | ファイル | セリフの在り処 | 形式 |
|---|---|---|---|
| PROLOGUE 起動 | `src/Prologue.cs` | `_talk` の `T(...)`、ブートログ `boot[]`、`Acrostic[]` | 独自レンダラ（"地"/"ミナ"/"少年"/"UI"） |
| STAGE3 レイ（道中） | `src/StageRei.cs` | `Intro`/`BossIntro`/`Clear` の配列 | `(who,text,face)[]` |
| STAGE3 レイ（改心） | `src/BossRei.cs` | `Lines[]` | `(who,text,face)[]` |
| STAGE1 あかり（道中） | `src/StageAkari.cs` | 同上 | `(who,text,face)[]` |
| STAGE1 あかり（改心） | `src/BossAkari.cs` | `Lines[]`（ナレ=記憶フラッシュ） | `(who,text,face)[]` |
| STAGE2 こはる（道中） | `src/StageKoharu.cs` | 同上 | `(who,text,face)[]` |
| STAGE2 こはる（改心） | `src/BossKoharu.cs` | `Lines[]` | `(who,text,face)[]` |
| ミナ戦（道中） | `src/StageMina.cs` + `src/BossMina.cs` | 配列 | `(who,text,face)[]` |
| FINAL 汚染 | `src/Final.cs` | `_talk` の `T(...)`、`Screams[]` | 独自レンダラ |
| EPILOGUE 名前 | `src/Epilogue.cs` | `I(...)`/`O(...)`、`PwChoices[]`、`Acrostic[]` | 独自レンダラ |

`who` = `Hud.LineKind`（`src/Hud.cs:119`）: 0=少年 / 1=ミナ / 2=相手ボス / 3=ナレ / 4=投稿 / 5=中継。
