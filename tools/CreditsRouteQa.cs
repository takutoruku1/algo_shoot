using Godot;
using System.Reflection;
using System.Threading.Tasks;

// CreditsRouteQa : タイトル →「クレジット」→ 戻る の導線と、BGM・必須表記の自動検証（2026-09-17）。
//   BGM 素材の利用許諾条件（MusMus の「BGM:MusMus」／PeriTune の CC BY 4.0）は、ゲーム内から
//   到達できて初めて満たされる。到達不能への逆戻りをここで検出する。
//   実行: Godot --headless --path . res://tools/qa_credits_route.tscn
public partial class CreditsRouteQa : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private int _fail;

    public override async void _Ready()
    {
        await Frames(2);
        // ChangeSceneToFile は「現在のシーン」を解放する＝自分がそれだと途中で消える。
        //   自分を CurrentScene から外し、root 直下の常駐ノードとして生き残らせてから始める。
        var self = GetTree().CurrentScene;
        if (self == this) GetTree().CurrentScene = null;

        // ① タイトルメニューに「クレジット」項目がある（設定の後ろ）
        var title = GD.Load<PackedScene>("res://TitleMenu.tscn").Instantiate();
        GetTree().Root.AddChild(title);
        GetTree().CurrentScene = title;
        await Frames(20);
        var items = (System.Array)typeof(TitleMenu)
            .GetField("Items", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        int creditsAt = -1, settingsAt = -1;
        for (int i = 0; i < items.Length; i++)
        {
            string jp = (string)items.GetValue(i)!.GetType().GetField("Item2")!.GetValue(items.GetValue(i))!;
            GD.Print($"[creditsqa] menu[{i}] = {jp}");
            if (jp == "クレジット") creditsAt = i;
            if (jp == "設定") settingsAt = i;
        }
        Check("タイトルに「クレジット」がある", creditsAt >= 0, "項目なし");
        Check("並びは「設定」の後ろ", settingsAt >= 0 && creditsAt == settingsAt + 1, $"設定={settingsAt} クレジット={creditsAt}");

        // ② その項目を決定すると Credits.tscn へ行く（Confirm を直に叩く＝入力合成に依らない）
        typeof(TitleMenu).GetField("_sel", Private)!.SetValue(title, creditsAt);
        typeof(TitleMenu).GetMethod("Confirm", Private)!.Invoke(title, null);
        await Frames(30);
        string scene = GetTree().CurrentScene?.SceneFilePath ?? "";
        Check("クレジット画面へ遷移", scene == "res://Credits.tscn", $"遷移先={scene}");

        // ③ 必須表記が実際に画面の項目として載っている
        var credits = GetTree().CurrentScene!;
        var list = (System.Collections.IEnumerable)typeof(Credits)
            .GetField("_items", Private)!.GetValue(credits)!;
        bool musmus = false, peritune = false, ccby = false;
        int lines = 0;
        foreach (var it in list)
        {
            string text = (string)it.GetType().GetField("Item2")!.GetValue(it)!;
            lines++;
            if (text.Contains("BGM:MusMus")) musmus = true;
            if (text.Contains("PeriTune")) peritune = true;
            if (text.Contains("creativecommons.org/licenses/by/4.0")) ccby = true;
        }
        Check("必須表記: BGM:MusMus", musmus, "見当たらない");
        Check("必須表記: PeriTune", peritune, "見当たらない");
        Check("必須表記: CC BY 4.0 のURL", ccby, "見当たらない");
        GD.Print($"[creditsqa] credits items = {lines}");

        // ④ 専用BGM（bgm_credits＝「帰り道」）に切り替わっている。
        //   Audio 側へ公開APIを足さず、いま鳴らしている曲を持つ _currentMusic を直に見る。
        var audio = GetNodeOrNull<Audio>("/root/Audio");
        var now = audio == null ? null
            : typeof(Audio).GetField("_currentMusic", Private)!.GetValue(audio) as AudioStream;
        Check("BGM が bgm_credits", audio != null && now != null && now == audio.BgmCredits,
            $"再生中={(now == null ? "(無音)" : now.ResourcePath)}");

        // ⑤ タイトルへ戻れる（X/Esc/右クリック＝Credits._Process の戻り分岐）。
        //   ヘッドレスでは物理キーを合成できないので、_t を進めたうえで Esc を押し下げ、
        //   通常の _Process が回る中で遷移するかを見る（分岐そのものを踏ませる）。
        typeof(Credits).GetField("_t", Private)!.SetValue(credits, 1.0);
        typeof(Credits).GetField("_backHeld", Private)!.SetValue(credits, false);
        var ev = new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true };
        Input.ParseInputEvent(ev);
        await Frames(10);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
        await Frames(10);
        string back = GetTree().CurrentScene?.SceneFilePath ?? "";
        Check("X/Esc でタイトルへ戻る", back == "res://TitleMenu.tscn", $"戻り先={back}");

        GD.Print(_fail == 0 ? "[creditsqa] ALL OK" : $"[creditsqa] FAILED: {_fail}");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    private void Check(string label, bool ok, string detail)
    {
        if (!ok) _fail++;
        GD.Print($"[creditsqa] {(ok ? "OK  " : "NG  ")}{label}{(ok ? "" : $" — {detail}")}");
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
