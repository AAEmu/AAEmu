using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Tasks.Telescopes;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class TransferTelescopeManager : Singleton<TransferTelescopeManager>
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        private Task transferTelescopeTickStartTask { get; set; }
        private const double Delay = 250;
        private Character owner { get; set; }

        public void TransferTelescopeStart(Character character)
        {
            owner = character;
            _log.Warn("TransferTelescopeTickStart: Started");

            transferTelescopeTickStartTask = new TransferTelescopeTickStartTask();
            //TaskManager.Instance.Schedule(transferTelescopeTickStartTask, TimeSpan.FromMilliseconds(Delay), TimeSpan.FromMilliseconds(Delay));
            TaskManager.Instance.Schedule(transferTelescopeTickStartTask, TimeSpan.FromMilliseconds(Delay));
        }

        internal void TransferTelescopeTick()
        {
            const int MaxCount = 10;
            // Copy a list of all transfers that have something attached. Ignore Carriage Boardings (46)
            var transfers = TransferManager.Instance.GetTransfers().Where(x => x.TemplateId != 46).ToArray();
            // не ограничивать дальность видимости для GM & Admins
            if (owner?.AccessLevel == 0)
            {
                var transfers2 = new List<Transfer>();
                foreach (var t in transfers)
                {
                    if (!(MathF.Abs(MathUtil.CalculateDistance(owner, t)) < 1000f)) { continue; }

                    transfers2.Add(t);
                }
                transfers = transfers2.ToArray();
            }
            if (transfers.Length > 0)
            {
                for (var i = 0; i < transfers.Length; i += MaxCount)
                {
                    var last = transfers.Length - i <= MaxCount;
                    var temp = new Transfer[last ? transfers.Length - i : MaxCount];
                    Array.Copy(transfers, i, temp, 0, temp.Length);
                    owner?.BroadcastPacket(new SCTransferTelescopeUnitsPacket(last, temp), true);
                }
            }
            TaskManager.Instance.Schedule(transferTelescopeTickStartTask, TimeSpan.FromMilliseconds(Delay));
        }

        public async System.Threading.Tasks.Task StopTransferTelescopeTickAsync()
        {
            if (transferTelescopeTickStartTask == null) 
                return;

            await transferTelescopeTickStartTask.CancelAsync();
            transferTelescopeTickStartTask = null;
            owner?.BroadcastPacket(new SCTransferTelescopeToggledPacket(false, 0), true);
            owner = null;
        }
    }
}
