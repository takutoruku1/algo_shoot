using Godot;
using System;
using System.Threading.Tasks;

public static class QaSceneTransition
{
    public static async Task Wait(Node node, int settleFrames = 20)
    {
        for (int frame = 0; frame < 1800 && LoadingScreen.IsActive; frame++)
            await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (LoadingScreen.IsActive) throw new Exception("Scene loading timed out");
        for (int frame = 0; frame < settleFrames; frame++)
            await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
