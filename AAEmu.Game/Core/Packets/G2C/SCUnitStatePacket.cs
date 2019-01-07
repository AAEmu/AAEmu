using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitStatePacket : GamePacket
    {
        private readonly Unit _unit;
        private readonly byte _type;

        public SCUnitStatePacket(Unit unit) : base(0x064, 1)
        {
            _unit = unit;
            if (_unit is Character)
                _type = 0;
            if (_unit is Npc)
                _type = 1;
            if (_unit is Slave)
                _type = 2;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unit.BcId);
            stream.Write(_unit.Name);
            stream.Write(_type);
            switch (_type) // UnitOwnerInfo?
            {
                case 0: // character
                    var character = (Character) _unit;
                    stream.Write(character.Id); // type(id)
                    stream.Write(0L); // v?
                    break;
                case 1: // npc
                    var npc = (Npc) _unit;
                    stream.WriteBc(npc.BcId);
                    stream.Write(npc.TemplateId); // type(id)
                    stream.Write(0u); // type(id)
                    break;
                case 2: // slave
                    var slave = (Slave) _unit;
                    stream.Write(0u); // type(id)
                    stream.Write((ushort) 0); // tl
                    stream.Write(0u); // type(id)
                    stream.Write(0u); // type(id)
                    break;
                case 3:
                    stream.Write((ushort) 0); // tl
                    stream.Write(0u); // type(id)
                    stream.Write((short) 0); // buildstep
                    break;
                case 4:
                    stream.Write((ushort) 0); // tl
                    stream.Write(0u); // type(id)
                    break;
                case 5:
                    stream.Write((ushort) 0); // tl
                    stream.Write(0u); // type(id)
                    stream.Write(0u); // type(id)
                    break;
                case 6:
                    stream.Write(0L); // type(id)
                    stream.Write(0u); // type(id)
                    break;
            }

            stream.Write(""); // master
            stream.Write(Helpers.ConvertX(_unit.Position.X));
            stream.Write(Helpers.ConvertY(_unit.Position.Y));
            stream.Write(Helpers.ConvertZ(_unit.Position.Z));
            stream.Write(_unit.Scale);
            stream.Write(_unit.Level);
            stream.Write(_unit.ModelId); // modelRef

            if (_unit is Character)
                foreach (var item in ((Character) _unit).Inventory.Equip)
                {
                    if (item == null)
                        stream.Write(0);
                    else
                        stream.Write(item);
                }
            else if (_unit is Npc)
                foreach (var item in ((Npc) _unit).Equip)
                {
                    if (item is BodyPart)
                        stream.Write(item.TemplateId);
                    else if (item != null)
                    {
                        stream.Write(item.TemplateId);
                        stream.Write(0L);
                        stream.Write((byte) 0);
                    }
                    else
                        stream.Write(0);
                }

            stream.Write(_unit.ModelParams);
            stream.WriteBc(0);
            stream.Write(_unit.Hp * 100); // preciseHealth
            stream.Write(_unit.Mp * 100); // preciseMana
            stream.Write((byte) 255); // point
//            if (point != 255) // -1
//            {
//                stream.WriteBc(0);
//            }
            stream.Write((byte) 255); // point
//            if (point != 255) // -1
//            {
//                stream.WriteBc(0);
//                stream.Write((byte) 0); // kind
//                stream.Write(0); // space
//                stream.Write(0); // spot
//            }

//            type = 1 // build
//            {
//                for (var i = 0; i < 2; i++)
//                    stream.Write(false); // door
//                for (var i = 0; i < 6; i++)
//                    stream.Write(false); // window
//            }
//            type = 4 // npc or mobs?
//            {
//                stream.Write(0u); // type(id) -> animActionId
//                stream.Write(true); // activate
//            }
//            type = 7 // grow
//            {
//                stream.Write(0u); // type(id)
//                stream.Write(0f); // growRate
//                stream.Write(0); // randomSeed
//                stream.Write(false); // isWithered
//                stream.Write(false); // isHarvested
//            }
//            type = 8 // object?
//            {
//                stream.Write(0f); // pitch
//                stream.Write(0f); // yaw
//            }

            switch (_type)
            {
                case 0: // character
                    stream.Write((byte) 0); // type
                    stream.Write(false); // isLooted
                    break;
                case 1: // npc
                    var npc = (Npc) _unit;
                    stream.Write(npc.Template.AnimActionId > 0 ? (byte) 4 : (byte) 0); // type
                    stream.Write(false); //isLooted
                    if (npc.Template.AnimActionId > 0)
                    {
                        stream.Write(npc.Template.AnimActionId);
                        stream.Write(true); //active
                    }

                    break;
            }

            stream.Write((byte) 0); // activeWeapon

            if (_unit is Character)
            {
                var character = (Character) _unit;
                stream.Write((byte) character.Skills.Skills.Count);
                foreach (var skill in character.Skills.Skills.Values)
                {
                    stream.Write(skill.Id);
                    stream.Write(skill.Level);
                }

                stream.Write(character.Skills.PassiveBuffs.Count);
                foreach (var buff in character.Skills.PassiveBuffs.Values)
                    stream.Write(buff.Id);
            }
            else
            {
                stream.Write((byte) 0); // learnedSkillCount
                stream.Write(0); // learnedBuffCount
            }

            stream.Write(_unit.Position.RotationX);
            stream.Write(_unit.Position.RotationY);
            stream.Write(_unit.Position.RotationZ);
            stream.Write(_unit.RaceGender);
            
            if (_unit is Character)
                stream.WritePisc(0, 0, ((Character)_unit).Appellations.ActiveAppellation, 0); // pisc
            else
                stream.WritePisc(0, 0, 0, 0); // pisc
            
            stream.WritePisc(_unit.Faction.Id, 0, 0, 0); // pisc
            
            if (_unit is Character)
            {
                var character = (Character) _unit;
                var flags = new BitSet(16);

                if (character.IsOffline)
                    flags.Set(13);

                stream.WritePisc(0, 0); // очки чести полученные в PvP, кол-во убийств в PvP
                stream.Write(flags.ToByteArray()); // flags(ushort)
                /*
                 * 0x01 - 8bit - режим боя
                 * 0x04 - 6bit - невидимость?
                 * 0x08 - 5bit - дуэль
                 * 0x40 - 2bit - gmmode, дополнительно 7 байт
                 * 0x80 - 1bit - дополнительно tl(ushort), tl(ushort), tl(ushort), tl(ushort)
                 * 0x0100 - 16bit - дополнительно 3 байт (bc), firstHitterTeamId(uint)
                 * 0x0400 - 14bit - надпись "Отсутсвует" под именем
                 */
            }
            else
            {
                stream.WritePisc(0, 0); // pisc
                stream.Write((ushort) 0); // flags
            }

            if (_unit is Character)
            {
                var character = (Character) _unit;

                var activeAbilities = character.Abilities.GetActiveAbilities();
                foreach (var ability in character.Abilities.Values)
                {
                    stream.Write(ability.Exp);
                    stream.Write(ability.Order);
                }

                stream.Write((byte) activeAbilities.Count);
                foreach (var ability in activeAbilities)
                    stream.Write((byte) ability);

                stream.WriteBc(0);

                character.VisualOptions.Write(stream, 15);

                stream.Write(1); //premium
            }

            stream.Write((byte) 0); // goodBuffCount
            // TODO ...
            stream.Write((byte) 0); // badBuffCount
            // TODO ...
            stream.Write((byte) 0); // hiddenBuffCount
            // TODO ...

            return stream;
        }
    }
}