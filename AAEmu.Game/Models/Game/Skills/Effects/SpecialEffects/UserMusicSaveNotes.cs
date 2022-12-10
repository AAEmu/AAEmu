using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class UserMusicSaveNotes : SpecialEffectAction
    {
        public override void Execute(Unit caster,
            SkillCaster casterObj,
            BaseUnit target,
            SkillCastTarget targetObj,
            CastAction castObj,
            Skill skill,
            SkillObject skillObject,
            DateTime time,
            int value1,
            int value2,
            int value3,
            int value4)
        {
            // Not sure what to actually do with this other than validate the last uploaded song and create the item
            // There does not seem to be any reference to the source song, other than the used itemId to create it
            // TODO ...
            if (caster is Character) { _log.Debug("Special effects: UserMusicSaveNotes value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

            if ((caster is Character player) && (casterObj is SkillItem si))
            {
                var item = ItemManager.Instance.GetItemByItemId(si.ItemId);
                if (!MusicManager.Instance.CreateSheetMusic(player, item))
                    _log.Error("Special effects: UserMusicSaveNotes - Error saving music for {0} !", player.Name);
            }
            else
            {
                _log.Error("Special effects: UserMusicSaveNotes - Invalid Arguments");
            }

        }
    }
}
