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

    public bool FailNext { get; set; }

    public Action OnSave { get; set; }

    public ShutdownTask ShutdownTask { get; set; }

    public void Initialize()
    {
    }

    public System.Threading.Tasks.Task StopAsync() => System.Threading.Tasks.Task.CompletedTask;

    public T ExecuteOperation<T>(Func<MySql.Data.MySqlClient.MySqlConnection,
        MySql.Data.MySqlClient.MySqlTransaction, T> operation) =>
        throw new NotSupportedException("RecordingSaveManager does not provide database transactions.");

    public void SaveTickStart()
    {
    }

    public bool DoSave() => TrySave() == WorldSaveStatus.Saved;

    public WorldSaveStatus TrySave() => TrySave(null);

    public WorldSaveStatus TrySave(Action onFailed)
    {
        if (_isSaving)
        {
            BusySkips++;
            return WorldSaveStatus.Busy;
        }

        if (FailNext)
        {
            FailNext = false;
            PersistenceGate.EnterSave();
            try
            {
                onFailed?.Invoke();
                return WorldSaveStatus.Failed;
            }
            finally
            {
                PersistenceGate.ExitSave();
            }
        }

        PersistenceGate.EnterSave();
        try
        {
            _isSaving = true;
            SaveCount++;
            OnSave?.Invoke();
            return WorldSaveStatus.Saved;
        }
        finally
        {
            _isSaving = false;
            PersistenceGate.ExitSave();
        }
    }
}
