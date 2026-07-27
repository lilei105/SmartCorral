using System.Threading;

namespace SmartCorral.Services;

/// <summary>
/// Ensures only one Smart Corral process runs at a time, via a named global Mutex.
/// </summary>
public static class SingleInstanceGuard
{
    private const string MutexName = @"Global\SmartCorral_SingleInstance_v1";
    private static Mutex? _mutex;

    /// <returns>True if this is the first instance (caller should proceed); false otherwise.</returns>
    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        return true;
    }
}
