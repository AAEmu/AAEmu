using System.Diagnostics;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Tasks.SaveTask;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class SaveManager(
    ITaskManager taskManager,
    IHousingManager housingManager,
    IMailManager mailManager,
    IItemManager itemManager,
    IAuctionManager auctionManager,
    ICrimeManager crimeManager,
    IAccountAttributeManager accountAttributeManager,
    IWorldManager worldManager,
    RankScoreManager rankScoreManager) : Singleton<SaveManager>, ISaveManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private double Delay = 1;
    private bool _enabled = false;
    private bool _isSaving = false;
    private readonly object _lock = new();
    private SaveTickStartTask saveTask;
    public ShutdownTask ShutdownTask { get; set; } = null;

    public void Initialize()
    {
        Logger.Info("Initialising Save Manager...");
        _enabled = true;
        Delay = AppConfiguration.Instance.World.AutoSaveInterval;
        SaveTickStart();
    }

    public async System.Threading.Tasks.Task StopAsync()
    {
        _enabled = false;
        if (saveTask == null)
        {
            return;
        }
        var result = await saveTask.CancelAsync();
        if (result)
        {
            saveTask = null;
        }
        // Do one final save here
        DoSave();
    }

    public void SaveTickStart()
    {
        // Logger.Warn("SaveTickStart: Started");
        saveTask = new SaveTickStartTask();
        taskManager.Schedule(saveTask, TimeSpan.FromMinutes(Delay), TimeSpan.FromMinutes(Delay));
    }

    /// <summary>
    /// Writes the World snapshot. Returns false without saving when another save is already
    /// running; that save took the <see cref="PersistenceGate"/> after every in-flight money
    /// operation finished, so it already carries the caller's state.
    /// </summary>
    public bool DoSave() => TrySave() == WorldSaveStatus.Saved;

    public WorldSaveStatus TrySave() => TrySave(null);

    public WorldSaveStatus TrySave(Action onFailed)
    {
        if (_isSaving)
            return WorldSaveStatus.Busy;
        if (PersistenceGate.IsOperationHeld)
        {
            // Inside a money operation on this very thread. Taking the gate exclusively here
            // would deadlock; hand the request to the operation's own end-of-scope flush.
            mailManager.PersistNow();
            return WorldSaveStatus.Busy;
        }

        var saved = false;
        PersistenceGate.EnterSave();
        try
        {
            lock (_lock)
            {
                _isSaving = true;
                try
                {
                    saved = SaveLocked();
                }
                finally
                {
                    _isSaving = false;
                }
            }

            if (!saved)
                onFailed?.Invoke();
        }
        finally
        {
            PersistenceGate.ExitSave();
        }

        return saved ? WorldSaveStatus.Saved : WorldSaveStatus.Failed;
    }

    private bool SaveLocked()
    {
        var saved = false;
        var stopWatch = new Stopwatch();
        stopWatch.Start();
        try
        {
            // Save stuff
            Logger.Debug("Saving DB ...");
            using (var connection = MySQL.CreateConnection())
            {
                using (var transaction = connection.BeginTransaction())
                {
                    // Houses
                    var savedHouses = housingManager.Save(connection, transaction);
                    // Mail
                    var savedMails = mailManager.Save(connection, transaction);
                    // Items
                    var saveItems = itemManager.Save(connection, transaction);
                    // Auction House
                    var savedAuctionHouse = auctionManager.Save(connection, transaction);
                    // Crimes
                    var savedCrimes = crimeManager.Save(connection, transaction);
                    // Account attributes
                    var savedAccountAttributes = accountAttributeManager.Save(connection, transaction);

                    // Characters
                    var savedCharacters = 0;
                    var characterSaveFailed = false;
                    foreach (var c in worldManager.GetAllCharacters())
                    {
                        if (c.Save(connection, transaction))
                        {
                            savedCharacters++;
                            // The ranking boards are kept with the character they score, so a board shows
                            // holders who are offline as well as the ones in world.
                            rankScoreManager.SaveCharacter(connection, transaction, c);
                            continue;
                        }

                        Logger.Error($"Failed to get save data for character {c.Id} - {c.Name}");
                        characterSaveFailed = true;
                        break;
                    }

                    // Slaves
                    var savedSlaves = 0;
                    if (!characterSaveFailed)
                    {
                        foreach (var worldInstance in worldManager.GetWorlds())
                        {
                            foreach (var slave in worldInstance.GetAllSlaves())
                            {
                                if (slave.Save(connection, transaction))
                                    savedSlaves++;
                            }
                        }
                    }

                    var totalCommits = 0;
                    totalCommits += savedHouses.Item1 + savedHouses.Item2;
                    totalCommits += savedMails.Item1 + savedMails.Item2;
                    totalCommits += saveItems.Item1 + saveItems.Item2 + saveItems.Item3;
                    totalCommits += savedAuctionHouse.Item1 + savedAuctionHouse.Item2;
                    totalCommits += savedCrimes.Item1 + savedCrimes.Item2;
                    totalCommits += savedCharacters;
                    totalCommits += savedSlaves;

                    if (WorldSaveCommitRules.MustRollback(characterSaveFailed))
                    {
                        try
                        {
                            transaction.Rollback();
                        }
                        catch (Exception eRollback)
                        {
                            Logger.Error(eRollback);
                        }

                        DiscardAccountLiveClears();
                    }
                    else if (!WorldSaveCommitRules.CanCommit(totalCommits, characterSaveFailed))
                    {
                        Logger.Debug("No data to update ...");
                        DiscardAccountLiveClears();
                        saved = true;
                    }
                    else
                    {
                        try
                        {
                            transaction.Commit();
                            ConfirmAccountLiveSaved();

                            if (savedHouses.Item1 + savedHouses.Item2 > 0)
                                Logger.Debug($"Updated {savedHouses.Item1} and deleted {savedHouses.Item2} houses ...");
                            if (savedMails.Item1 + savedMails.Item2 > 0)
                                Logger.Debug($"Updated {savedMails.Item1} and deleted {savedMails.Item2} mails ...");
                            if (saveItems.Item1 + saveItems.Item2 > 0)
                                Logger.Debug($"Updated {saveItems.Item1} and deleted {saveItems.Item2} items in {saveItems.Item3} containers ...");
                            if (saveItems.Item3 > 0)
                                Logger.Debug($"Updated {saveItems.Item3} item containers ...");
                            if (savedAuctionHouse.Item1 + savedAuctionHouse.Item2 > 0)
                                Logger.Debug($"Updated {savedAuctionHouse.Item1} and deleted {savedAuctionHouse.Item2} auction items ...");
                            if (savedCrimes.Item1 + savedCrimes.Item2 > 0)
                                Logger.Debug($"Updated {savedCrimes.Item1} and deleted {savedCrimes.Item2} crime events ...");
                            if (savedCharacters > 0)
                                Logger.Debug($"Updated {savedCharacters} characters ...");
                            if (savedSlaves > 0)
                                Logger.Debug($"Updated {savedSlaves} slaves ...");

                            saved = true;
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e);
                            try
                            {
                                transaction.Rollback();
                            }
                            catch (Exception eRollback)
                            {
                                Logger.Error(eRollback);
                            }
                            DiscardAccountLiveClears();
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "DoSave Exception\n");
            DiscardAccountLiveClears();
        }
        stopWatch.Stop();
        Logger.Debug("Saving data took {0}", stopWatch.Elapsed);

        return saved;
    }

    public T ExecuteOperation<T>(Func<MySql.Data.MySqlClient.MySqlConnection, MySql.Data.MySqlClient.MySqlTransaction, T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_lock)
        {
            using var connection = MySQL.CreateConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                var result = operation(connection, transaction);
                transaction.Commit();
                return result;
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception rollbackException)
                {
                    Logger.Error(rollbackException, "Failed to roll back database operation");
                }

                throw;
            }
        }
    }

    public void SaveTick()
    {
        if (!_enabled)
        {
            Logger.Warn("Auto-Saving disabled, skipping ...");
            return;
        }
        DoSave();
    }

    public void SetAutoSaveInterval()
    {
        Delay = AppConfiguration.Instance.World.AutoSaveInterval;
    }

    private static void ConfirmAccountLiveSaved()
    {
        AccountAttendanceManager.Instance.ConfirmSaved();
        ScheduleItemManager.Instance.ConfirmSaved();
        AccountLiveWallet.ConfirmSaved();
        ItemManager.Instance?.ConfirmSaved();
        MailManager.Instance?.ConfirmSaved();
    }

    private static void DiscardAccountLiveClears()
    {
        AccountAttendanceManager.Instance.DiscardPendingClears();
        ScheduleItemManager.Instance.DiscardPendingClears();
        AccountLiveWallet.DiscardPendingClears();
        ItemManager.Instance?.DiscardPendingClears();
        MailManager.Instance?.DiscardPendingClears();
    }
}
