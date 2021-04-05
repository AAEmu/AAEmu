using System.Linq;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.States
{
    public class AAttackState : State
    {
        public override void Enter()
        {
            base.Enter();
            AI.Owner.BroadcastPacket(new SCCombatEngagedPacket(AI.Owner.ObjId), true);
        }

        public override void Exit()
        {
            base.Exit();
            AI.Owner.BroadcastPacket(new SCCombatClearedPacket(AI.Owner.ObjId), true);

            if (!(AI.Owner is Npc npc))
                return;
            npc.ClearAllAggro();
        }

        public bool HasAnyAggro()
        {
            if (!(AI.Owner is Npc npc))
                return false;
            if (npc.AggroTable == null)
                return false;

            return npc.AggroTable.Values.Count > 0;
        }

        public Unit GetTopDamageAggro()
        {
            if (!(AI.Owner is Npc npc))
                return null;
            if (npc.AggroTable == null)
                return null;

            var ret = npc.AggroTable.Aggregate((i1,i2) => i1.Value.DamageAggro > i2.Value.DamageAggro ? i1 : i2);
            var unit = WorldManager.Instance.GetUnit(ret.Key);

            return unit;
        }
        
        public Unit GetTopHealAggro()
        {
            if (!(AI.Owner is Npc npc))
                return null;
            if (npc.AggroTable == null)
                return null;

            var ret = npc.AggroTable.Aggregate((i1,i2) => i1.Value.HealAggro > i2.Value.HealAggro ? i1 : i2);
            var unit = WorldManager.Instance.GetUnit(ret.Key);

            return unit;
        }
    }
}
