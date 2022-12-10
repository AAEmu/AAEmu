using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class PlayUserMusic : SpecialEffectAction
    {
        protected override SpecialType SpecialEffectActionType => SpecialType.PlayUserMusic;
        
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
            // TODO ...
            if (caster is Character) { _log.Debug("Special effects: PlayUserMusic value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

            // TODO: make sure the proper instrument buff gets applied
            // The related tags seems to be "Play Song" (1155) and "Music Play Animation" (1202)

            if (target is Character player)
            {
                // Send Midi data
                player.BroadcastPacket(new SCSendUserMusicPacket(player.ObjId, player.Name, MusicManager.Instance.GetMidiCache(player.Id)), true);
                
                
                var instrument = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Musical);
                if (instrument != null)
                {
                    // I'm sure we can get this relation info from the tables somewhere, but can't find it
                    // TODO: It might be that instrument doodads require special handling (not tested)
                    switch ((ItemCategory)instrument.Template.Category_Id)
                    {
                        case ItemCategory.Lute:
                            target.Buffs.AddBuff((uint)BuffConstants.LutePlay, caster);
                            break;
                        case ItemCategory.Flute:
                            target.Buffs.AddBuff((uint)BuffConstants.FlutePlay, caster);
                            break;
                        //case ItemCategory.Drum:
                        //    // NOTE: You can't actually use drum weapons as a instrument, client will throw a "requirements not met" error
                        //    target.Buffs.AddBuff((uint)BuffConstants.DrumPlay, caster);
                        //    break;
                        default:
                            _log.Trace("SpecialEffectAction - PlayUserMusic - Equipped instrument slot is not a known instrument !");
                            break;
                    }
                }
                else
                {
                    _log.Trace("SpecialEffectAction - PlayUserMusic - No instrument equipped !");
                }

            }
            
        }
    }
}
