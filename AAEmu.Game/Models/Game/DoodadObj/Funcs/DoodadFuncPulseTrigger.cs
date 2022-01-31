﻿using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncPulseTrigger : DoodadFuncTemplate
    {
        public bool Flag { get; set; }
        public uint NextPhase { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncPulseTrigger");
            if (Flag && nextPhase == 1)
                owner.GoToPhaseChanged(null, (int)NextPhase);
            owner.NeedChangePhase = false;
        }
    }
}
