﻿using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncFakeUse : DoodadFuncTemplate
    {
        public uint SkillId { get; set; }
        public uint FakeSkillId { get; set; }
        public bool TargetParent { get; set; }
        public uint NextPhase { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncFakeUse with SkillId:{0}, FakeSkillId{1} and NextPhase:{2}", SkillId, FakeSkillId,NextPhase);
            if(NextPhase > 0)
            {
                DoodadManager.Instance.TriggerPhases(GetType().Name, caster, owner, FakeSkillId);
            } else
            {
                DoodadManager.Instance.TriggerFunc(GetType().Name, caster, owner, FakeSkillId);
            }
            
        }
    }
}
