
﻿using AAEmu.Game.Core.Managers.UnitManagers;

using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

using System;


namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRatioChange : DoodadFuncTemplate
    {
        public int Ratio { get; set; }
        public uint NextPhase { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncRatioChange : Ratio {0}, NextPhase {1}, SkillId {2}", Ratio, NextPhase, skillId);

            Random ratioChange = new Random();
            var roll = ratioChange.Next(0, 10000); //Basing this off of Rumbling Archeum Trees (10% for a Thunderstruck)

            if (roll <= Ratio)
            {
                //TODO conversion for Thunderstruck or next doodad
            }
            DoodadManager.Instance.TriggerPhaseFunc(GetType().Name, NextPhase, caster, owner, skillId);
        }
    }
}
