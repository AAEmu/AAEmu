using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class AggroManager : Singleton<AggroManager>
    {
        ConcurrentDictionary<uint, ConcurrentBag<Aggro>> _abusedAggro;
        ConcurrentDictionary<uint, ConcurrentBag<Aggro>> _abuserAggro;

        public Aggro GetUnitAggro(Unit attacker, Unit abused)
        {
            if (_abuserAggro.TryGetValue(attacker.ObjId, out var value))
            {
                var aggro = value.Where(o => o.Npc.ObjId == abused.ObjId).FirstOrDefault();

                return aggro;
            }
            return null;
        }

        public void ClearUnitAggro
    }
}
