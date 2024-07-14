using System;
using System.Diagnostics;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Tasks.SaveTask;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class SaveManager : Singleton<SaveManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private double Delay = 1;
    private bool _enabled;
    private bool _isSaving;
    private object _lock = new();
    private SaveTickStartTask saveTask;
    public ShutdownTask ShutdownTask { get; set; } = null;

    public SaveManager()
    {
        _enabled = false;
        _isSaving = false;
    }

    public void Initialize()
    {
        Logger.Info("Initialising Save Manager...");
        _enabled = true;
        Delay = AppConfiguration.Instance.World.AutoSaveInterval;
        SaveTickStart();
    }

    public async void Stop()
    {
        _enabled = false;
        if (saveTask == null)
        {
            return;
        }
        var result = await saveTask.Cancel();
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
        TaskManager.Instance.Schedule(saveTask, TimeSpan.FromMinutes(Delay), TimeSpan.FromMinutes(Delay));
    }

    public bool DoSave()
    {
        if (_isSaving)
            return false;
        var saved = false;
        lock (_lock)
        {
            _isSaving = true;
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
                        var savedHouses = HousingManager.Instance.Save(connection, transaction);
                        // Mail
                        var savedMails = MailManager.Instance.Save(connection, transaction);
                        // Items
                        var saveItems = ItemManager.Instance.Save(connection, transaction);
                        //Auction House
                        var savedAuctionHouse = AuctionManager.Instance.Save(connection, transaction);

                        // Characters
                        var savedCharacters = 0;
                        foreach (var c in WorldManager.Instance.GetAllCharacters())
                        {
                            if (c.Save(connection, transaction))
                                savedCharacters++;
                            else
                                Logger.Error("Failed to get save data for character {0} - {1}", c.Id, c.Name);
                        }

                        // Slaves
                        var savedSlaves = 0;
                        foreach (var slave in WorldManager.Instance.GetAllSlaves())
                        {
                            if (slave.Save(connection, transaction))
                                savedSlaves++;
                        }

                        // Residents
                        ResidentManager.Instance.SaveDirectlyToDatabase();

                        //Expeditions
                        var savedExpeditions = 0;
                        foreach (var expedition in ExpeditionManager.Instance.GetExpeditions())
                        {
                            expedition.Save(connection, transaction);
                            savedExpeditions++;
                        }

                        var totalCommits = 0;
                        totalCommits += savedHouses.Item1 + savedHouses.Item2;
                        totalCommits += savedMails.Item1 + savedMails.Item2;
                        totalCommits += saveItems.Item1 + saveItems.Item2 + saveItems.Item3;
                        totalCommits += savedAuctionHouse.Item1 + savedAuctionHouse.Item2;
                        totalCommits += savedCharacters;
                        totalCommits += savedSlaves;
                        totalCommits += savedExpeditions;

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

                                if ((savedHouses.Item1 + savedHouses.Item2) > 0)
                                    Logger.Debug($"Updated {savedHouses.Item1} and deleted {savedHouses.Item2} houses ...");
                                if ((savedMails.Item1 + savedMails.Item2) > 0)
                                    Logger.Debug($"Updated {savedMails.Item1} and deleted {savedMails.Item2} mails ...");
                                if ((saveItems.Item1 + saveItems.Item2) > 0)
                                    Logger.Debug($"Updated {saveItems.Item1} and deleted {saveItems.Item2} items in {saveItems.Item3} containers ...");
                                if ((saveItems.Item3) > 0)
                                    Logger.Debug($"Updated {saveItems.Item3} item containers ...");
                                if ((savedAuctionHouse.Item1 + savedAuctionHouse.Item2) > 0)
                                    Logger.Debug($"Updated {savedAuctionHouse.Item1} and deleted {savedAuctionHouse.Item2} auction items ...");
                                if (savedCharacters > 0)
                                    Logger.Debug($"Updated {savedCharacters} characters ...");
                                if (savedSlaves > 0)
                                    Logger.Debug($"Updated {savedSlaves} slaves ...");
                                if (savedExpeditions > 0)
                                    Logger.Debug($"Updated {savedExpeditions} expeditions ...");

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
        }
        _isSaving = false;
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
