using System;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
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
        private const short LpChangePremium = 10; // TODO in config
        private const short LpChange = 5;
        private const short UpLimit = 5000;
        private const double Delay = 5; // min

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
            TaskManager.Instance.Schedule(lpTickStartTask, TimeSpan.FromMinutes(Delay), TimeSpan.FromMinutes(Delay));
        }
        public void LaborPowerTick()
        {
            var connections = GameConnectionTable.Instance.GetConnections();
            foreach (var connection in connections)
            {
                foreach (var character in connection.Characters.Where(character => character.Value.IsOnline))
                {
                    if (character.Value.LaborPower >= UpLimit)
                    {
                        // _log.Warn("No need to increase Labor Point, since they reached the limit {0} for Char: {1}", UpLimit, character.Value.Name);
                        continue;
                    }

                    if (character.Value.LaborPower < 0)
                    {
                        _log.Warn("Char: {1} has negative {0} labor points, reseting to 0.", character.Value.LaborPower, character.Value.Name);
                        character.Value.ChangeLabor((short)(character.Value.LaborPower * -1), 0);
                        continue;
                    }

                    // Offline Regeneration: 10 Labor Points every 5 minutes
                    if ((DateTime.UtcNow - character.Value.LaborPowerModified).TotalMinutes > Delay)
                    {
                        var needAddOfflineLp = (DateTime.UtcNow - character.Value.LaborPowerModified).TotalMinutes / Delay * LpChangePremium;
                        var calculatedOfflineLp = character.Value.LaborPower + needAddOfflineLp;
                        if (needAddOfflineLp > short.MaxValue)
                        {
                            needAddOfflineLp = short.MaxValue;
                        }
                        if (calculatedOfflineLp <= UpLimit)
                        {
                            character.Value.LaborPowerModified = DateTime.UtcNow;
                            character.Value.ChangeLabor((short)needAddOfflineLp, 0);
                            _log.Debug("Character {1} gained {0} offline Labor Point(s)", needAddOfflineLp, character.Value.Name);
                        }
                        else
                        {
                            var valueLp = (short)(UpLimit - character.Value.LaborPower);
                            _log.Debug("Character {1} gained {0} offline Labor Point(s)", valueLp, character.Value.Name);
                            character.Value.LaborPowerModified = DateTime.UtcNow;
                            character.Value.ChangeLabor(valueLp, 0);
                        }
                        continue;
                    }

                    // Online Regeneration: 10 Labor Points every 5 minutes
                    var laborAmountUntilUpLimit = (short)(UpLimit - character.Value.LaborPower);
                    if (laborAmountUntilUpLimit >= LpChangePremium)
                    {
                        _log.Debug("Character {1} gained {0} Labor Point(s)", LpChangePremium, character.Value.Name);
                        character.Value.LaborPowerModified = DateTime.UtcNow;
                        character.Value.ChangeLabor(LpChangePremium, 0);
                    }
                    else if (laborAmountUntilUpLimit > 0 && laborAmountUntilUpLimit < LpChangePremium)
                    {
                        _log.Debug("Character {1} gained {0} Labor Point(s)", laborAmountUntilUpLimit, character.Value.Name);
                        character.Value.LaborPowerModified = DateTime.UtcNow;
                        character.Value.ChangeLabor(laborAmountUntilUpLimit, 0);
                    }
                }
            }
        }

    }
}
