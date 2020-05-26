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

        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Warn("DoodadFuncFakeUse : SkillId {0}, skillId {1}, FakeSkillId {2}, TargetParent {3}", SkillId, skillId, FakeSkillId, TargetParent);
            DoodadManager.Instance.TriggerPhaseFunc(GetType().Name, owner.FuncGroupId, caster, owner, skillId);

        }
    }
}
