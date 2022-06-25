﻿using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class Demolish : IWorldInteraction
    {
        public void Execute(IUnit caster, SkillCaster casterType, IBaseUnit target, SkillCastTarget targetType,
            uint skillId, uint doodadId, DoodadFuncTemplate objectFunc)
        {
            if (target is House house && caster is Character character)
                HousingManager.Instance.Demolish(character.Connection, house, false, false);
        }
    }
}
