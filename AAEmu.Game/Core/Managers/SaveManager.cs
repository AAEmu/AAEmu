using System.Diagnostics;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
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
    IWorldManager worldManager) : Singleton<SaveManager>, ISaveManager
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
    public bool DoSave()
    {
        if (_isSaving)
            return false;
        if (PersistenceGate.IsOperationHeld)
        {
            // Inside a money operation on this very thread. Taking the gate exclusively here
            // would deadlock; hand the request to the operation's own end-of-scope flush.
            mailManager.PersistNow();
            return false;
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
        }
        finally
        {
            PersistenceGate.ExitSave();
        }

        return saved;
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
                    foreach (var c in worldManager.GetAllCharacters())
                    {
                        if (c.Save(connection, transaction))
                            savedCharacters++;
                        else
                            Logger.Error($"Failed to get save data for character {c.Id} - {c.Name}");
                    }

                    // Slaves
                    var savedSlaves = 0;
                    foreach (var worldInstance in worldManager.GetWorlds())
                    {
                        foreach (var slave in worldInstance.GetAllSlaves())
                        {
                            if (slave.Save(connection, transaction))
                                savedSlaves++;
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

                    if (totalCommits <= 0)
                    {
                        Logger.Debug("No data to update ...");
                        saved = true;
                    }
                    else
                    {
                        try
                        {
                            transaction.Commit();

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
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "DoSave Exception\n");
        }
        stopWatch.Stop();
        Logger.Debug("Saving data took {0}", stopWatch.Elapsed);

        return saved;
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
}
