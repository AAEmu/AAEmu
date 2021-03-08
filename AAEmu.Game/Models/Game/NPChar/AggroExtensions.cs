using System.Collections.Concurrent;
using System.Linq;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.NPChar
{
    public static class AggroExtensions
    {
        public static uint GetTopTotalAggroAbuserObjId(this ConcurrentDictionary<uint, Aggro> table)
        {
            return table.Aggregate((i1, i2) => i1.Value.TotalAggro > i2.Value.TotalAggro ? i1 : i2).Key;
        }
        
        public static uint GetTopDamageAggroAbuserObjId(this ConcurrentDictionary<uint, Aggro> table)
        {
            return table.Aggregate((i1, i2) => i1.Value.DamageAggro > i2.Value.DamageAggro ? i1 : i2).Key;
        }

        public static uint GetTopHealAggroAbuserObjId(this ConcurrentDictionary<uint, Aggro> table)
        {
            return table.Aggregate((i1, i2) => i1.Value.HealAggro > i2.Value.HealAggro ? i1 : i2).Key;
        }

        public static void AddUnitAggro(this ConcurrentDictionary<uint, Aggro> table, AggroKind kind, Unit unit, int amount)
        {

        }
    }
}
