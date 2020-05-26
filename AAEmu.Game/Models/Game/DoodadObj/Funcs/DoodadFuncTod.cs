using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncTod : DoodadFuncTemplate
    {//This func is typically the end of the chain
        public int Tod { get; set; }
        public uint NextPhase { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            //_log.Debug("DoodadFuncTod : NextPhase {0}, SkillId {1}", NextPhase, skillId);
            if (NextPhase > 0)
            {
                owner.FuncGroupId = NextPhase;
                owner.BroadcastPacket(new SCDoodadPhaseChangedPacket(owner), true);
            }
        }
    }
}
