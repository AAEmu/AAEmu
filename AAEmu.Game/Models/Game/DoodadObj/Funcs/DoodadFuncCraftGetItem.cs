﻿using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncCraftGetItem : DoodadFuncTemplate
    {
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncCraftGetItem");
            owner.NeedChangePhase = false;
        }
    }
}
