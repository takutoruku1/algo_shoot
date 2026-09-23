using System.Text;
using System.Text.RegularExpressions;

// Handles : ゲーム内 SNS(X) に出るアカウント名（@ハンドル）の単一ソース（2026-09-23）。
//
// 背景: 表示していたハンドルのうち 10 件が実在アカウントと一致していた（@mina_ai_ @koharu_light @akari_ame
//   @koharu @akari @hikage_ と、引用の嵐で誹謗中傷風の発言者に使っていた @tori398 @rom_only @nichijo_x
//   @no_name_77）。ユーザー指示＝「文字化けで一部の文字をごまかし、絶対に存在しないアカウント名にする」。
//
// 規則（「文字化け」）: ハンドル内の英字 1 文字を、見た目の似た Latin-1 の文字に置き換える。
//   X のハンドルは A-Z a-z 0-9 _ の 4〜15 文字しか許さないので、Latin-1 が 1 文字でも混ざれば
//   構造的に存在し得ない。読みは保つ（@mïna_ai_ は「ミナ」と読める）。
//   ・対応表: a→ã i→ï u→ü e→ë o→ø n→ñ c→ç（大文字も同様）。JetBrains Mono・Zen Kaku Gothic の
//     両方にグリフがある文字だけ＝どの描画箇所でも豆腐にならない（tools/HandlesQa.cs が HasChar で確認）。
//     U+FFFD（�）や全角・ブロック文字は同梱フォントに無い／等幅で崩れるので使わない。
//   ・置換位置は決定論: @ の直後の 1 文字は残し（頭文字で読ませる）、2 文字目以降で最初に対応表に
//     ある文字を置き換える（@tori398→@tøri398、@nichijo_x→@nïchijo_x、@k_tanaka→@k_tãnaka）。
//   ・主要 4 名（akari / koharu / rei / mina）は「同じキャラは同じ位置・同じ文字で化ける」を
//     機械規則より優先する: ハンドルにその綴りが含まれていれば、その綴りの中で化かす
//     （@rei_____ も @hoshiai_rei_live も「rëi」。機械規則だけだと後者は「hø」になって別人に見える）。
//     綴りが先頭にある場合は機械規則と同じ結果（akãri / køharu / rëi / mïna）なので、規則は実質ひとつ。
//   ・対応表の文字が無い場合（2 文字目以降の英字が b d f g h j k l m p q r s t v w x y z だけ）は
//     最後の英字を ß に。英字が無ければ最初の 0 を ø に、それも無ければ末尾の文字を ß に
//     （現在のデータには無い保険。ß と 0→ø は Plain() で元に戻らない）。
//   ・既に非 ASCII を含む文字列はそのまま返す（二重適用しても化けが増えない）。
//
// 使い方:
//   ・固定名は定数（Mina / Akari / Koharu / Rei / AkariShort）。戦闘表示（ID風4桁つき）は src/BossHandles.cs。
//   ・道中カード／埋め草／プロローグの通知の「他人」は SnsVoices の ASCII 部品を Mob() で化かす。
//   ・その他のリテラルは Garble("@tori398") で包む＝ASCII のハンドルをそのまま画面に出す経路を作らない。
//   ・顔アイコンの判定（Hud.FaceIdFor / Hub.SpeakerFace）は Plain() で戻してから部分文字列を見る。
//   ・tools/HandlesQa.cs（tools/qa_handles.tscn）が全出所について IsImpossibleOnX を確認する。
public static class Handles
{
    public const string Mina = "@mïna_ai_";       // 自機・FINAL・サイドパネル・ハブのピン留め
    public const string Akari = "@akãri.";        // GameManager.Stages[akari]
    public const string Koharu = "@køharu";       // GameManager.Stages[koharu]
    public const string Rei = "@rëi_____";        // GameManager.Stages[rei]
    public const string AkariShort = "@akãri";    // ハブ小話（H1r 返信）の話者名。旧 "@akari"

    // 主要 4 名の綴り→化けた綴り。綴りの何文字目が変わるかは garbled 側との差分で決まる。
    private static readonly (string plain, string garbled)[] Cast =
    {
        ("akari", "akãri"), ("koharu", "køharu"), ("rei", "rëi"), ("mina", "mïna"),
    };
    // 対応表（同じ添字が対）。
    private const string PlainChars = "aiueoncAIUEONC";
    private const string GarbledChars = "ãïüëøñçÃÏÜËØÑÇ";
    private static readonly Regex XHandle = new("^@[A-Za-z0-9_]{4,15}$", RegexOptions.Compiled);

    // ASCII のハンドル（@ 付き／無しどちらでも）を化かす。
    public static string Garble(string ascii)
    {
        if (string.IsNullOrEmpty(ascii)) return ascii;
        bool at = ascii[0] == '@';
        string body = at ? ascii.Substring(1) : ascii;
        if (body.Length == 0) return ascii;
        foreach (char c in body) if (c > 0x7F) return ascii;   // 既に化けている

        int pos = -1; char rep = '\0';
        // 1) 主要 4 名の綴りが含まれていれば、その綴りの中で化かす（キャラ単位の一貫性）。
        foreach (var (plain, garbled) in Cast)
        {
            int i = body.IndexOf(plain, System.StringComparison.Ordinal);
            if (i < 0) continue;
            for (int k = 0; k < plain.Length; k++)
                if (plain[k] != garbled[k]) { pos = i + k; rep = garbled[k]; break; }
            break;
        }
        // 2) 機械規則: 頭文字は残し、2 文字目以降で最初に対応表にある文字。
        for (int i = 1; pos < 0 && i < body.Length; i++)
        {
            int m = PlainChars.IndexOf(body[i]);
            if (m >= 0) { pos = i; rep = GarbledChars[m]; }
        }
        // 3) 保険: 最後の英字を ß ／ 最初の 0 を ø ／ 末尾を ß。
        for (int i = body.Length - 1; pos < 0 && i >= 0; i--)
            if (char.IsAsciiLetter(body[i])) { pos = i; rep = 'ß'; }
        if (pos < 0) { int z = body.IndexOf('0'); if (z >= 0) { pos = z; rep = 'ø'; } }
        if (pos < 0) { pos = body.Length - 1; rep = 'ß'; }

        var sb = new StringBuilder(body);
        sb[pos] = rep;
        return at ? "@" + sb : sb.ToString();
    }

    // 逆変換（ã→a 等）。顔アイコンの判定など、化けた綴りに "akari" 等の部分文字列を探す前に通す。
    //   ß / 0→ø の保険は戻せない（主要 4 名と現在の全データは対応表だけで化けるので影響なし）。
    public static string Plain(string garbled)
    {
        if (string.IsNullOrEmpty(garbled)) return garbled;
        var sb = new StringBuilder(garbled.Length);
        foreach (char c in garbled)
        {
            int m = GarbledChars.IndexOf(c);
            sb.Append(m >= 0 ? PlainChars[m] : c);
        }
        return sb.ToString();
    }

    // 「他人」のハンドル: SnsVoices の ASCII 部品を化かして、末尾に決定論の数字を付ける
    //   （ハブの埋め草カード／道中の背景カード）。数字無し版はプロローグの通知カード用。
    public static string Mob(string part, int num) => "@" + Garble(part) + "_" + num;
    public static string Mob(string part) => "@" + Garble(part);

    // X のハンドル文法（@ + A-Z a-z 0-9 _ の 4〜15 文字）に合わない＝どのアカウントとも一致し得ない。QA 用。
    public static bool IsImpossibleOnX(string handle) => !XHandle.IsMatch(handle);
}
