using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Tasks;

namespace AAEmu.UnitTests.Utils.Mocks;

/// <summary>
/// Stands in for <see cref="ISaveManager"/> so a test can see what a forced save
/// would have committed. <see cref="OnSave"/> runs at every <see cref="DoSave"/>
/// and is where the test snapshots balances, bids and mail.
/// </summary>
public sealed class RecordingSaveManager : ISaveManager
{
    public int SaveCount { get; private set; }

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
        SaveCount++;
        OnSave?.Invoke();
        return true;
    }
}
