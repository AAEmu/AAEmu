using System;
using System.Diagnostics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Tasks.SaveTask;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class SaveManager : Singleton<SaveManager>
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        private double Delay = 1; // TODO: 1 minute for debugging, should likely be more like 5 or 10 on production, maybe add to configuration ?
        private bool _enabled;
        private bool _isSaving;
        private object _lock = new object();
        SaveTickStartTask saveTask ;

        public SaveManager()
        {
            _enabled = false;
            _isSaving = false;
        }

        public void Initialize()
        {
            _log.Info("Initialising Save Manager...");
            _enabled = true;
            if (double.TryParse(ConfigurationManager.Instance.GetConfiguration("AutoSaveInterval"),out var d))
            {
                Delay = d;
            }
            else
            {
                Delay = 5; // Default to 5 minutes
            }
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
            // _log.Warn("SaveTickStart: Started");
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
                    _log.Debug("Saving DB ...");
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
                                    _log.Error("Failed to get save data for character {0} - {1}", c.Id, c.Name);
                            }

                            var totalCommits = 0;
                            totalCommits += savedHouses.Item1 + savedHouses.Item2;
                            totalCommits += savedMails.Item1 + savedMails.Item2;
                            totalCommits += saveItems.Item1 + saveItems.Item2;
                            totalCommits += savedAuctionHouse.Item1 + savedAuctionHouse.Item2;
                            totalCommits += savedCharacters;

                            if (totalCommits <= 0)
                            {
                                _log.Debug("No data to update ...");
                                saved = true;
                            }
                            else
                            {
                                try
                                {
                                    transaction.Commit();

                                    if ((savedHouses.Item1 + savedHouses.Item2) > 0)
                                        _log.Debug("Updated {0} and deleted {1} houses ...", savedHouses.Item1, savedHouses.Item2);
                                    if ((savedMails.Item1 + savedMails.Item2) > 0)
                                        _log.Debug("Updated {0} and deleted {1} mails ...", savedMails.Item1, savedMails.Item2);
                                    if ((saveItems.Item1 + saveItems.Item2) > 0)
                                        _log.Debug("Updated {0} and deleted {1} items ...", saveItems.Item1, saveItems.Item2);
                                    if ((savedAuctionHouse.Item1 + savedAuctionHouse.Item2) > 0)
                                        _log.Debug("Updated {0} and deleted {1} auction items ...", savedAuctionHouse.Item1, savedAuctionHouse.Item2);
                                    if (savedCharacters > 0)
                                        _log.Debug("Updated {0} characters ...", savedCharacters);

                                    saved = true;
                                }
                                catch (Exception e)
                                {
                                    _log.Error(e);
                                    try
                                    {
                                        transaction.Rollback();
                                    }
                                    catch (Exception eRollback)
                                    {
                                        _log.Error(eRollback);
                                    }
                                }
                            }

                        }
                    }

                }
                catch (Exception e)
                {
                    _log.Error(string.Format("DoSave Exception: {0}", e.Message));
                }
                stopWatch.Stop();
                _log.Debug("Saving data took {0}", stopWatch.Elapsed);
            }
            _isSaving = false;
            return saved;
        }


        public void SaveTick()
        {
            if (!_enabled)
            {
                _log.Warn("Auto-Saving disabled, skipping ...");
                return;
            }
            DoSave();
        }

    }
}
