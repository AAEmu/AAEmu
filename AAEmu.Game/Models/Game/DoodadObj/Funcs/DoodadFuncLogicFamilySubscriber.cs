﻿using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncLogicFamilySubscriber : DoodadFuncTemplate
    {
        public uint FamilyId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncLogicFamilySubscriber");
            owner.cancelPhasing = true;
        }
    }
}
