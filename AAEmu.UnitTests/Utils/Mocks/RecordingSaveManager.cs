using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Tasks;

namespace AAEmu.UnitTests.Utils.Mocks;

/// <summary>
/// Stands in for <see cref="ISaveManager"/> so a test can see what a forced save would have
/// committed. Mirrors the real manager's coordination: it takes <see cref="PersistenceGate"/>
/// exclusively for the snapshot and answers false while another save is running.
/// <see cref="OnSave"/> runs inside the snapshot and is where the test records balances, bids
/// and mail.
/// </summary>
public sealed class RecordingSaveManager : ISaveManager
{
    private volatile bool _isSaving;

    public int SaveCount { get; private set; }

    /// <summary>How many callers were answered false because a save was already running.</summary>
    public int BusySkips { get; private set; }

    public Action OnSave { get; set; }

    public ShutdownTask ShutdownTask { get; set; }

    public void Initialize()
    {
    }

    public System.Threading.Tasks.Task StopAsync() => System.Threading.Tasks.Task.CompletedTask;

    public void SaveTickStart()
    {
    }

    public bool DoSave()
    {
        if (_isSaving)
        {
            BusySkips++;
            return false;
        }

        PersistenceGate.EnterSave();
        try
        {
            _isSaving = true;
            SaveCount++;
            OnSave?.Invoke();
            return true;
        }
        finally
        {
            _isSaving = false;
            PersistenceGate.ExitSave();
        }
    }
}
