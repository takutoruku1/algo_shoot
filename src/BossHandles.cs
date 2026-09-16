// BossHandles : 戦闘表示（ボスバー ShowBossBar／スペル宣告 AnnounceSpell）専用のアカウント表記（2026-09-16）。
//   実在アカウントとの偶然一致を避けるため、ハンドル末尾にキャラ固定のID風数字を付ける。
//   ・数字は無意味な4桁で、キャラごとに固定（ランダムにしない＝セーブ・周回・場面で変わらない）。
//   ・変更は戦闘表示のみ。ハブの投稿カード／プロローグ／エピローグ等の GameManager.Stages.Handle
//     （"@akari." "@koharu" "@rei_____"）とサイドパネルの自機アカウント（"@mina_ai_"）は据え置き
//     ＝戦闘中のボス名とハブの表示は現状 意図的に別物（不整合の扱いはユーザー判断待ち）。
//   ・W0 のヒカゲ（"@hikage_"）は非正典＝追加投資しない方針に従い対象外。
public static class BossHandles
{
    // キャラ固定のID風サフィックス：あかり=4137 ／ こはる=8025 ／ レイ=6390 ／ ミナ=2741
    public const string AkariBar = "@akari._4137";            // 本ボスバー＋中ボス（カメオ）バー
    public const string AkariSpell = "@akari_ame_4137";       // スペル宣告
    public const string KoharuCameo = "@koharu_8025";         // 中ボス（カメオ）バー
    public const string KoharuMain = "@koharu_light_8025";    // 本ボスバー＋スペル宣告
    public const string ReiCameo = "@rei_____6390";           // 中ボス（カメオ）バー（末尾 _ に直結）
    public const string ReiMain = "@hoshiai_rei_live_6390";   // 本ボスバー＋スペル宣告
    public const string MinaBattle = "@mina_ai_2741";         // FINAL ボスバー＋スペル宣告（末尾 _ に直結）
}
