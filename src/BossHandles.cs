// BossHandles : 戦闘表示（ボスバー ShowBossBar／スペル宣告 AnnounceSpell）専用のアカウント表記（2026-09-16）。
//   実在アカウントとの偶然一致を避けるため、ハンドル末尾にキャラ固定のID風数字を付ける。
//   ・数字は無意味な4桁で、キャラごとに固定（ランダムにしない＝セーブ・周回・場面で変わらない）。
//   ・2026-09-23: 4桁を付けても元の綴りが実在アカウントと一致していた（@koharu_light / @akari_ame）ので、
//     src/Handles.cs の規則で英字 1 文字を Latin-1 に化かした（akãri / køharu / rëi / mïna）。
//     X のハンドルは A-Z a-z 0-9 _ しか許さないので構造的に存在しない。値は Handles.Garble(旧ASCII) の
//     出力を定数に固定したもの（tools/HandlesQa.cs が一致を確認する。規則を変えたらここも追随）。
//   ・ハブの投稿カード／プロローグ／エピローグ等の GameManager.Stages.Handle（Handles.Akari 等）と
//     サイドパネルの自機アカウント（Handles.Mina）は 4 桁無し＝戦闘中のボス名とハブの表示は
//     現状 意図的に別物（不整合の扱いはユーザー判断待ち）。化け方（位置・文字）はキャラ単位で揃えてある。
//   ・W0 のヒカゲは非正典＝追加投資しない方針に従い対象外（BossHikage.cs が Handles.Garble で直接化かす）。
public static class BossHandles
{
    // キャラ固定のID風サフィックス：あかり=4137 ／ こはる=8025 ／ レイ=6390 ／ ミナ=2741
    public const string AkariBar = "@akãri._4137";            // 本ボスバー＋中ボス（カメオ）バー
    public const string AkariSpell = "@akãri_ame_4137";       // スペル宣告
    public const string KoharuCameo = "@køharu_8025";         // 中ボス（カメオ）バー
    public const string KoharuMain = "@køharu_light_8025";    // 本ボスバー＋スペル宣告
    public const string ReiCameo = "@rëi_____6390";           // 中ボス（カメオ）バー（末尾 _ に直結）
    public const string ReiMain = "@hoshiai_rëi_live_6390";   // 本ボスバー＋スペル宣告（rëi＝レイの他の表記と同じ化け方）
    public const string MinaBattle = "@mïna_ai_2741";         // FINAL ボスバー＋スペル宣告（末尾 _ に直結）
}
