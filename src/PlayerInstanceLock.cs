using System;
using System.Threading;

public sealed class PlayerInstanceLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _ownsLock;

    public PlayerInstanceLock(string name)
    {
        _mutex = new Mutex(false, name);
        try { _ownsLock = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsLock = true; }
    }

    public bool Acquired => _ownsLock;

    public void Dispose()
    {
        if (_ownsLock)
        {
            _mutex.ReleaseMutex();
            _ownsLock = false;
        }
        _mutex.Dispose();
    }

    public static bool AllowsMultipleInstances(bool editorBuild, string[] arguments)
    {
        if (editorBuild) return true;
        foreach (string argument in arguments)
            if (argument is "--allow-multiple-instances" or "--qa" or "--shot" or "--demo") return true;
        return false;
    }
}
