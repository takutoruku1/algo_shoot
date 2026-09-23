using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// HandlesQa : ゲーム画面に出る全 @ハンドルが「X に存在し得ない」形か（src/Handles.cs の規則）を確認する。
//   ヘッドレスで完結（Shot なし）。
//   (a) 全出所（Stages / BossHandles / Handles 定数 / QuoteStorm / SnsVoices×Mob / Prologue の3人 / ヒカゲ /
//       Hud の自動生成 / BossPostStory）が IsImpossibleOnX かつ Latin-1 を 1 文字以上含む
//   (b) Plain(Garble(x)) == x、Garble は冪等、化けるのは 1 文字だけ（長さ不変・頭文字は残る）
//   (c) Hud.FaceIdFor が化けた綴りから顔IDを引ける／Hub.SpeakerFace 相当の Plain 判定
//   (d) 主要 4 名はキャラ単位で同じ位置・同じ文字で化ける（akã / kø / rëi / mï）
//   (e) 同梱フォント（Mono / Zen / ZenBold / ZenBlack）に置換文字のグリフがある
//   (f) BossHandles / Handles の定数が Garble(旧ASCII) と一致（定数を手で直したときのズレを検出）
public partial class HandlesQa : Node
{
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private int _pass;
    private void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        _pass++;
        GD.Print($"[HandlesQA] PASS {message}");
    }
    private static bool HasLatin1(string s) => s.Any(c => c > 0x7F);
    private static string Const(Type t, string name) => (string)t.GetField(name, AnyStatic)!.GetRawConstantValue()!;

    // 旧 ASCII → 期待する化け方（規則の出力を固定。規則を変えたら意図して直す）。
    private static readonly (string ascii, string garbled)[] Expected =
    {
        ("@mina_ai_", "@mïna_ai_"), ("@akari.", "@akãri."), ("@koharu", "@køharu"), ("@rei_____", "@rëi_____"),
        ("@akari", "@akãri"), ("@hikage_", "@hïkage_"), ("@boss", "@bøss"),
        ("@akari._4137", "@akãri._4137"), ("@akari_ame_4137", "@akãri_ame_4137"),
        ("@koharu_8025", "@køharu_8025"), ("@koharu_light_8025", "@køharu_light_8025"),
        ("@rei_____6390", "@rëi_____6390"), ("@hoshiai_rei_live_6390", "@hoshiai_rëi_live_6390"),
        ("@mina_ai_2741", "@mïna_ai_2741"),
        ("@tori398", "@tøri398"), ("@gaiya_8", "@gãiya_8"), ("@anon_5502", "@añon_5502"), ("@rom_only", "@røm_only"),
        ("@kansoku_01", "@kãnsoku_01"), ("@no_name_77", "@nø_name_77"), ("@mob_4410", "@møb_4410"),
        ("@sotogawa_2", "@søtogawa_2"), ("@nichijo_x", "@nïchijo_x"), ("@teifujo__", "@tëifujo__"),
        ("@nanashi_3942", "@nãnashi_3942"),
        ("yuki_design", "yüki_design"), ("k_tanaka", "k_tãnaka"), ("natsume_3rd", "nãtsume_3rd"), ("ao_00", "aø_00"),
        ("harada_cmt", "hãrada_cmt"), ("nao_x2", "não_x2"), ("komatsu_soum", "kømatsu_soum"), ("nozomi_re", "nøzomi_re"),
        ("ren_0921", "rën_0921"), ("minami_ddl", "mïnami_ddl"), ("shiori_live", "shïori_live"), ("moco2000", "møco2000"),
        ("mikami_draft", "mïkami_draft"), ("aoink", "aøink"), ("hikaru_diet", "hïkaru_diet"), ("satomi_low", "sãtomi_low"),
        ("nemui_zzz", "nëmui_zzz"), ("mikan_711", "mïkan_711"), ("inu_sanpo", "iñu_sanpo"), ("shio_umi", "shïo_umi"),
    };

    public override void _Ready()
    {
        try
        {
            GD.Print($"[HandlesQA] user dir = {OS.GetUserDataDir()}");
            // ── (a) 全出所を集める ──
            var all = new List<(string src, string handle)>();
            foreach (var s in GameManager.Stages) all.Add(($"Stages[{s.Id}].Handle", s.Handle));
            foreach (var f in typeof(BossHandles).GetFields(BindingFlags.Public | BindingFlags.Static))
                all.Add(($"BossHandles.{f.Name}", (string)f.GetRawConstantValue()!));
            foreach (var f in typeof(Handles).GetFields(BindingFlags.Public | BindingFlags.Static))
                all.Add(($"Handles.{f.Name}", (string)f.GetRawConstantValue()!));
            foreach (string stage in new[] { "Stage1", "Stage2", "Stage3" })
            {
                var quotes = (Array)typeof(QuoteStorm).GetField(stage, AnyStatic)!.GetValue(null)!;
                foreach (object q in quotes)
                    all.Add(($"QuoteStorm.{stage}", (string)q.GetType().GetField("Handle")!.GetValue(q)!));
            }
            all.Add(("QuoteStorm.PinHandle", Const(typeof(QuoteStorm), "PinHandle").Split(' ').Last()));
            foreach (var v in SnsVoices.All)
            {
                all.Add(($"Mob({v.Handle}, 4321)", Handles.Mob(v.Handle, 4321)));
                all.Add(($"Mob({v.Handle})", Handles.Mob(v.Handle)));
            }
            foreach (string name in new[] { "V1", "V2", "V3" })
            {
                int idx = (int)typeof(Prologue).GetField(name, AnyStatic)!.GetRawConstantValue()!;
                all.Add(($"Prologue.{name}", Handles.Mob(SnsVoices.At(idx).Handle)));
            }
            all.Add(("BossHikage", Handles.Garble("@hikage_")));
            all.Add(("Hud auto fallback", Handles.Garble("@boss")));
            foreach (string id in new[] { "akari", "koharu", "rei", "mina" })
                all.Add(($"BossPostStory[{id}].Handle", BossPostStory.Get(id).Handle));
            Check(all.Count >= 3 + 7 + 5 + 17 + 1 + 40 + 3 + 2 + 4, $"collected {all.Count} handle sources");
            foreach (var (src, h) in all)
                Check(Handles.IsImpossibleOnX(h) && HasLatin1(h), $"{src} = {h} cannot exist on X");
            // 規則の外形: 対応表の文字だけ（ß / 0→ø の保険が実データで発動していない）。
            foreach (var (src, h) in all)
                Check(!h.Contains('ß'), $"{src} = {h} uses the lookalike table, not the ß fallback");

            // ── (b) 規則の性質 ──
            foreach (var (ascii, garbled) in Expected)
            {
                string g = Handles.Garble(ascii);
                Check(g == garbled, $"Garble({ascii}) == {garbled} (got {g})");
                Check(Handles.Plain(g) == ascii, $"Plain(Garble({ascii})) round-trips");
                Check(Handles.Garble(g) == g, $"Garble({g}) is idempotent");
                Check(g.Length == ascii.Length && g.Zip(ascii).Count(p => p.First != p.Second) == 1,
                    $"{ascii}: exactly one character changes");
                int head = ascii[0] == '@' ? 1 : 0;
                Check(g[head] == ascii[head], $"{ascii}: first letter is kept");
            }
            Check(Handles.Garble("akari") == "akãri" && Handles.Garble("") == "" && Handles.Garble("@") == "@",
                "Garble works without @ and tolerates empty input");
            Check(Handles.Garble("k_tkr") == "k_tkß" && Handles.Garble("@2024_") == "@2ø24_" && Handles.Garble("@9999") == "@999ß",
                "fallbacks (ß / 0→ø / last char) keep even letterless handles impossible on X");
            Check(Handles.IsImpossibleOnX("@akãri.") && Handles.IsImpossibleOnX("@akari.") && Handles.IsImpossibleOnX("@abc")
                && !Handles.IsImpossibleOnX("@akari") && !Handles.IsImpossibleOnX("@rei_____6390"),
                "IsImpossibleOnX follows X's handle grammar");
            Check(Handles.Mob("tori398", 42) == "@tøri398_42" && Handles.Mob("tori398") == "@tøri398", "Mob garbles the SnsVoices part");

            // ── (c) 顔アイコンの判定 ──
            var faceIdFor = typeof(Hud).GetMethod("FaceIdFor", AnyStatic)!;
            string Face(string handle) => (string)faceIdFor.Invoke(null, new object[] { handle, "" })!;
            Check(Face(BossHandles.AkariBar) == "akari" && Face(BossHandles.AkariSpell) == "akari", "FaceIdFor(AkariBar/AkariSpell) == akari");
            Check(Face(BossHandles.KoharuCameo) == "koharu" && Face(BossHandles.KoharuMain) == "koharu", "FaceIdFor(KoharuCameo/KoharuMain) == koharu");
            Check(Face(BossHandles.ReiCameo) == "rei" && Face(BossHandles.ReiMain) == "rei", "FaceIdFor(ReiCameo/ReiMain) == rei");
            Check(Face(BossHandles.MinaBattle) == "mina" && Face(Handles.Mina) == "mina", "FaceIdFor(MinaBattle/Mina) == mina");
            Check(Face(Handles.Garble("@hikage_")) == "", "FaceIdFor(hikage) stays blank (no dedicated face)");
            Check(Handles.Plain("ミナ→" + Handles.AkariShort).Contains("akari") && Handles.Plain(Handles.Rei).Contains("rei")
                && Handles.Plain(Handles.Koharu).Contains("koharu"),
                "Hub.SpeakerFace can recover the character from the garbled speaker string");

            // ── (d) 主要 4 名のキャラ単位の一貫性 ──
            string[] akari = { GameManager.Stages[0].Handle, Handles.Akari, Handles.AkariShort, BossHandles.AkariBar, BossHandles.AkariSpell };
            string[] koharu = { GameManager.Stages[1].Handle, Handles.Koharu, BossHandles.KoharuCameo, BossHandles.KoharuMain };
            string[] rei = { GameManager.Stages[2].Handle, Handles.Rei, BossHandles.ReiCameo, BossHandles.ReiMain };
            string[] mina = { Handles.Mina, BossHandles.MinaBattle, BossPostStory.Get("mina").Handle };
            Check(akari.All(h => h.Contains("akã") && HasLatin1(h)), "every Akari handle garbles the same letter (akã)");
            Check(koharu.All(h => h.Contains("kø") && HasLatin1(h)), "every Koharu handle garbles the same letter (kø)");
            Check(rei.All(h => h.Contains("rëi") && HasLatin1(h)), "every Rei handle garbles the same letter (rëi), including hoshiai_rëi_live");
            Check(mina.All(h => h.Contains("mï") && HasLatin1(h)), "every Mina handle garbles the same letter (mï)");

            // ── (e) フォントのグリフ ──
            var fonts = new (string name, Font font)[] { ("Mono", UiKit.Mono), ("Zen", UiKit.Zen), ("ZenBold", UiKit.ZenBold), ("ZenBlack", UiKit.ZenBlack) };
            foreach (var (name, font) in fonts)
            {
                Check(font.HasChar('a') && !font.HasChar(0x1F600), $"{name}: HasChar is meaningful in this run");
                foreach (char c in "ãïüëøñçÃÏÜËØÑÇß")
                    Check(font.HasChar(c), $"{name} has glyph U+{(int)c:X4} '{c}'");
            }

            // ── (f) 定数が規則の出力と一致 ──
            var boss = new (string name, string ascii)[]
            {
                ("AkariBar", "@akari._4137"), ("AkariSpell", "@akari_ame_4137"), ("KoharuCameo", "@koharu_8025"),
                ("KoharuMain", "@koharu_light_8025"), ("ReiCameo", "@rei_____6390"), ("ReiMain", "@hoshiai_rei_live_6390"),
                ("MinaBattle", "@mina_ai_2741"),
            };
            foreach (var (name, ascii) in boss)
                Check(Const(typeof(BossHandles), name) == Handles.Garble(ascii), $"BossHandles.{name} == Garble({ascii})");
            Check(Handles.Mina == Handles.Garble("@mina_ai_") && Handles.Akari == Handles.Garble("@akari.")
                && Handles.Koharu == Handles.Garble("@koharu") && Handles.Rei == Handles.Garble("@rei_____")
                && Handles.AkariShort == Handles.Garble("@akari"), "Handles constants == Garble(old ASCII)");

            GD.Print($"[HandlesQA] ALL PASS ({_pass} checks)");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[HandlesQA] FAIL {e.Message}");
            GetTree().Quit(1);
            return;
        }
        GetTree().Quit(0);
    }
}
