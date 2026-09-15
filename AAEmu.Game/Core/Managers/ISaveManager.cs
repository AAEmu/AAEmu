using AAEmu.Game.Models.Tasks;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface ISaveManager : IInitializable
{
    ShutdownTask ShutdownTask { get; set; }
    System.Threading.Tasks.Task StopAsync();
    void SaveTickStart();
    bool DoSave();

    /// <summary>
    /// Same snapshot as <see cref="DoSave"/>, but <see cref="WorldSaveStatus.Busy"/> is not a
    /// failure — another save already holds the caller's state.
    /// </summary>
    WorldSaveStatus TrySave();

    /// <summary>
    /// Same as <see cref="TrySave()"/>, and <paramref name="onFailed"/> runs before the save
    /// lock is released when the snapshot does not commit.
    /// </summary>
    WorldSaveStatus TrySave(Action onFailed);

    T ExecuteOperation<T>(Func<MySqlConnection, MySqlTransaction, T> operation);
}

public enum WorldSaveStatus
{
    Saved = 0,
    Busy,
    Failed
}
