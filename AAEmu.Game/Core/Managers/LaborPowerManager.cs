using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Tasks.LaborPower;

using NLog;

/*
 * Labor Points Generation
 * The generation of labor points is a continuous action while online for all players.
 * Patron subscribers benefit from labor point generation while offline, same amount as the online rate.
 * 
 * Patron Subscribers
 * Maximum Labor Pool: 5,000 Labor Points
 * Online Regeneration: 10 Labor Points every 5 minutes
 * Offline Regeneration: 10 Labor Points every 5 minutes
 * Free to Play
 * Maximum Labor Pool: 2,000 Labor Points
 * Online Regeneration: 5 Labor Points every 5 minutes
 * Offline Regeneration: None
 */
namespace AAEmu.Game.Core.Managers
{
    public class LaborPowerManager : Singleton<LaborPowerManager>
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        //private List<LaborPower> _onlineChar;
        //private List<LaborPower> _offlineChar;
        //private const short LpChangePremium = 10; // TODO in config
        //private const short LpChange = 5;
        //private const short UpLimit = 5000;
        //private const double Delay = 5; // min

        public LaborPowerManager()
        {
            //_onlineChar = new List<LaborPower>();
            //_offlineChar = new List<LaborPower>();
        }

        public void Initialize()
        {
            _log.Info("Initialising Labor Power Manager...");
            LaborPowerTickStart();
        }

        public void LaborPowerTickStart()
        {
            _log.Debug("LaborPowerTickStart: Started");

            var lpTickStartTask = new LaborPowerTickStartTask();
            TaskManager.Instance.Schedule(lpTickStartTask, TimeSpan.FromMinutes(AppConfiguration.Instance.LabowPower.Delay), TimeSpan.FromMinutes(AppConfiguration.Instance.LabowPower.Delay));
        }
        public void LaborPowerTick()
        {
            using (var connection = MySQL.CreateConnection())
            {
                var characterIds = new List<uint>();
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.CommandText = "SELECT id FROM characters WHERE `deleted`=0";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            characterIds.Add(reader.GetUInt32("id"));
                    }
                }

                foreach (var id in characterIds)
                {
                    var character = Character.Load(connection, id);

                    var onlineCharacter = WorldManager.Instance.GetCharacterById(character.Id);
                    if (onlineCharacter != null)
                        character = onlineCharacter;

                    if (character.LaborPower > AppConfiguration.Instance.LabowPower.UpLimit)
                    {
                        // _log.Warn("No need to increase Labor Point, since they reached the limit {0} for Char: {1}", UpLimit, character.Value.Name);
                        character.LaborPowerModified = DateTime.UtcNow;
                        character.ChangeLabor((short)(AppConfiguration.Instance.LabowPower.UpLimit - character.LaborPower), 0, false);
                    }

                    if (character.LaborPower < 0)
                    {
                        _log.Warn("Char: {1} has negative {0} labor points, reseting to 0.", character.LaborPower, character.Name);
                        character.LaborPowerModified = DateTime.UtcNow;
                        character.ChangeLabor((short)(character.LaborPower * -1), 0);
                    }

                    if (character.IsOnline)
                    {
                        // Online Regeneration: 10 Labor Points every 5 minutes
                        var laborAmountUntilUpLimit = (short)(AppConfiguration.Instance.LabowPower.UpLimit - character.LaborPower);
                        if (laborAmountUntilUpLimit >= AppConfiguration.Instance.LabowPower.LpChangePremium)
                        {
                            _log.Debug("Character {1} gained {0} Labor Point(s)", AppConfiguration.Instance.LabowPower.LpChangePremium, character.Name);
                            character.LaborPowerModified = DateTime.UtcNow;
                            character.ChangeLabor(AppConfiguration.Instance.LabowPower.LpChangePremium, 0);
                        }
                        else if (laborAmountUntilUpLimit > 0 && laborAmountUntilUpLimit < AppConfiguration.Instance.LabowPower.LpChangePremium)
                        {
                            _log.Debug("Character {1} gained {0} Labor Point(s)", laborAmountUntilUpLimit, character.Name);
                            character.LaborPowerModified = DateTime.UtcNow;
                            character.ChangeLabor(laborAmountUntilUpLimit, 0);
                        }
                    }
                    else
                    {
                        // Offline Regeneration: 10 Labor Points every 5 minutes
                        if ((DateTime.UtcNow - character.LaborPowerModified).TotalMinutes > AppConfiguration.Instance.LabowPower.Delay)
                        {
                            var needAddOfflineLp = (DateTime.UtcNow - character.LaborPowerModified).TotalMinutes / 
                                AppConfiguration.Instance.LabowPower.Delay * AppConfiguration.Instance.LabowPower.LpChangePremium;
                            var calculatedOfflineLp = character.LaborPower + needAddOfflineLp;
                            if (needAddOfflineLp > short.MaxValue)
                            {
                                needAddOfflineLp = short.MaxValue;
                            }
                            if (calculatedOfflineLp <= AppConfiguration.Instance.LabowPower.UpLimit)
                            {
                                character.LaborPowerModified = DateTime.UtcNow;
                                character.ChangeLabor((short)needAddOfflineLp, 0);
                                _log.Debug("Character {1} gained {0} offline Labor Point(s)", needAddOfflineLp, character.Name);
                            }
                            else
                            {
                                var valueLp = (short)(AppConfiguration.Instance.LabowPower.UpLimit - character.LaborPower);
                                _log.Debug("Character {1} gained {0} offline Labor Point(s)", valueLp, character.Name);
                                character.LaborPowerModified = DateTime.UtcNow;
                                character.ChangeLabor(valueLp, 0);
                            }
                        }
                        character.SaveDirectlyToDatabase();
                    }
                }
            }
        }
    }
}
