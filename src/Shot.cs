using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Shot : スクリーンショット用オートロード (/root/Shot)。
//
// 起動コマンドのユーザ引数（`--` の後ろ）に "--shot" が含まれているときだけ有効化し、
// 指定時刻にルートビューポートを PNG 保存して、全部撮り終えたら Quit() する。
// --shot 無効時は即停止。
//
//   有効化:        res://...tscn -- --shot
//   撮影時刻(秒):  -- --shot --shot-at 1.0,6.0,12.0   （省略時 1.0）
//   出力先ディレクトリ: -- --shot --shot-out d:/dev/algo_shoot/build/shots   （絶対パス）
//   ファイル接頭辞:     -- --shot --shot-name hub      （hub_00.png, hub_01.png ...）
//
// 静的UI画面（Hub/Shop/DiffSelect）は --demo/--qa だと即遷移してしまうので、
// それらは --shot 単体で（autoplay 無しで）撮ること。ゲームプレイ/カットシーンは
// --demo と併用すると自動操縦で進んだ状態を撮れる。
public partial class Shot : Node
{
    private bool _active;
    private string _outDir = "user://shots";
    private string _name = "shot";
    private readonly List<double> _times = new();
    private int _next;
    private double _t;

    public override void _Ready()
    {
        var user = OS.GetCmdlineUserArgs();
        for (int i = 0; i < user.Length; i++)
        {
            switch (user[i])
            {
                case "--shot": _active = true; break;
                case "--shot-out":
                    if (i + 1 < user.Length) _outDir = user[i + 1];
                    break;
                case "--shot-name":
                    if (i + 1 < user.Length) _name = user[i + 1];
                    break;
                case "--shot-at":
                    if (i + 1 < user.Length)
                        foreach (var part in user[i + 1].Split(','))
                            if (double.TryParse(part, out var v)) _times.Add(v);
                    break;
            }
        }

        if (!_active)
        {
            SetProcess(false);
            return;
        }

        if (_times.Count == 0) _times.Add(1.0);
        _times.Sort();

        // 出力ディレクトリを用意（絶対パスでも user:// でも可）
        DirAccess.MakeDirRecursiveAbsolute(_outDir);

        // 最後の撮影は描画完了後に拾いたいので、プロセスの末尾で動かす
        ProcessPriority = 1000;
        ProcessMode = ProcessModeEnum.Always;

        GD.Print($"[Shot] active. name={_name} out={_outDir} at=[{string.Join(",", _times)}]");
    }

    public override void _Process(double delta)
    {
        _t += delta;
        while (_next < _times.Count && _t >= _times[_next])
        {
            Capture(_next);
            _next++;
        }
        if (_next >= _times.Count)
        {
            GD.Print("[Shot] done.");
            SetProcess(false);
            Finish();
        }
    }

    private async void Finish()
    {
        GetNode<Node>("/root/DemoPilot").ProcessMode = ProcessModeEnum.Disabled;
        GetNode<Node>("/root/QaPilot").ProcessMode = ProcessModeEnum.Disabled;
        GetTree().Paused = false;
        Hud.BubblePaused = false;
        GetNode<BulletPool>("/root/Pool").DespawnAll();
        GetTree().CurrentScene?.QueueFree();
        for (int frame = 0; frame < 5; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Audio.Instance.StopMusicOnce(0);
        foreach (var child in Audio.Instance.GetChildren())
            if (child is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
        // 再生スレッドが音源参照を解放してから終了する。
        await Task.Delay(250);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (int frame = 0; frame < 5; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Quit();
    }

    private void Capture(int idx)
    {
        var vp = GetViewport();
        var tex = vp?.GetTexture();
        using var img = tex?.GetImage();
        if (img == null)
        {
            GD.PushWarning("[Shot] viewport image unavailable");
            return;
        }
        string path = $"{_outDir.TrimEnd('/')}/{_name}_{idx:00}.png";
        var err = img.SavePng(path);
        GD.Print($"[Shot] saved {path} ({img.GetWidth()}x{img.GetHeight()}) t={_t:0.0} err={err}");
    }
}
