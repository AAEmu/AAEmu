using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Shipyard;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitStatePacket : GamePacket
    {
        private readonly Unit _unit;
        private readonly byte _type;
        private readonly byte _modelPostureType;

        public SCUnitStatePacket(Unit unit) : base(SCOffsets.SCUnitStatePacket, 1)
        {
            _unit = unit;
            if (_unit is Character)
            {
                _type = 0;
                _modelPostureType = 0;
            }
            else if (_unit is Npc)
            {
                _type = 1;
                if (((Npc)_unit).Template.AnimActionId > 0)
                    _modelPostureType = 4;
                else
                    _modelPostureType = 0;
            }
            else if (_unit is Slave)
            {
                _type = 2;
                _modelPostureType = 8;
            }
            else if (_unit is House)
            {
                _type = 3;
                _modelPostureType = 1;
            }
            else if (_unit is Transfer)
            {
                _type = 4;
                _modelPostureType = 0;
            }
            else if (_unit is Mount)
            {
                _type = 5;
                _modelPostureType = 0;
            }
            else if (_unit is Shipyard)
            {
                _type = 6;
                _modelPostureType = 0;
            }
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unit.ObjId);
            stream.Write(_unit.Name);
            stream.Write(_type);
            switch (_type) // UnitOwnerInfo?
            {
                case 0:
                    var character = (Character)_unit;
                    stream.Write(character.Id); // type(id)
                    stream.Write(0L); // v?
                    break;
                case 1:
                    var npc = (Npc)_unit;
                    stream.WriteBc(npc.ObjId);
                    stream.Write(npc.TemplateId); // npc templateId
                    stream.Write(0u); // type(id)
                    stream.Write((byte)0); // clientDriven
                    break;
                case 2:
                    var slave = (Slave)_unit;
                    stream.Write(0u); // type(id)
                    stream.Write(slave.TlId); // tl
                    stream.Write(slave.TemplateId); // type(id)
                    stream.Write(0u); // type(id)
                    break;
                case 3:
                    var house = (House)_unit;
                    var buildStep = house.CurrentStep == -1
                        ? 0
                        : -house.Template.BuildSteps.Count + house.CurrentStep;

                    stream.Write(house.TlId); // tl
                    stream.Write(house.TemplateId); // house templateId
                    stream.Write((short)buildStep); // buildstep
                    break;
                case 4:
                    var transfer = (Transfer)_unit;
                    stream.Write(transfer.TlId); // tl
                    stream.Write(transfer.TemplateId); // transfer templateId
                    break;
                case 5:
                    var mount = (Mount)_unit;
                    stream.Write(mount.TlId); // tl
                    stream.Write(mount.TemplateId); // npc teplateId
                    stream.Write(mount.OwnerId); // characterId (masterId)
                    break;
                case 6:
                    var shipyard = (Shipyard)_unit;
                    stream.Write(shipyard.Template.Id); // type(id)
                    stream.Write(shipyard.Template.TemplateId); // type(id)
                    break;
            }

            if (_unit.OwnerId > 0) // master
            {
                var name = NameManager.Instance.GetCharacterName(_unit.OwnerId);
                stream.Write(name ?? "");
            }
            else
                stream.Write("");

            stream.WritePosition(_unit.Position.X, _unit.Position.Y, _unit.Position.Z);
            stream.Write(_unit.Scale);
            stream.Write(_unit.Level);
            stream.Write(_unit.ModelId); // modelRef

            if (_unit is Character)
            {
                var character = (Character)_unit;
                foreach (var item in character.Inventory.Equip)
                {
                    if (item == null)
                        stream.Write(0);
                    else
                        stream.Write(item);
                }
            }
            else if (_unit is Npc)
            {
                var npc = (Npc)_unit;
                for (var i = 0; i < npc.Equip.Length; i++)
                {
                    var item = npc.Equip[i];

                    if (item is BodyPart)
                        stream.Write(item.TemplateId);
                    else if (item != null)
                    {
                        if (i == 27) // Cosplay
                            stream.Write(item);
                        else
                        {
                            stream.Write(item.TemplateId);
                            stream.Write(0L);
                            stream.Write((byte)0);
                        }
                    }
                    else
                        stream.Write(0);
                }
            }
            else
                for (var i = 0; i < 28; i++)
                    stream.Write(0);

            stream.Write(_unit.ModelParams);
            stream.WriteBc(0);
            stream.Write(_unit.Hp * 100); // preciseHealth
            stream.Write(_unit.Mp * 100); // preciseMana
            stream.Write((byte)255); // point // TODO UnitAttached
            //if (point != 255) // -1
            //{
            //    stream.WriteBc(0);
            //}

            if (_unit is Character)
            {
                var character = (Character)_unit;
                if (character.Bonding == null)
                    stream.Write((sbyte)-1); // point
                else
                    stream.Write(character.Bonding);
            }
            else if (_unit is Slave)
            {
                var slave = (Slave)_unit;
                if (slave.BondingObjId > 0)
                    stream.WriteBc(slave.BondingObjId);
                else
                    stream.Write((sbyte)-1);
            }
            else
                stream.Write((sbyte)-1); // point

            // TODO UnitModelPosture
            stream.Write(_modelPostureType); // type
            stream.Write(false); // isLooted

            switch (_modelPostureType)
            {
                case 1: // build
                    for (var i = 0; i < 2; i++)
                        stream.Write(true); // door
                    for (var i = 0; i < 6; i++)
                        stream.Write(true); // window
                    break;
                case 4: // npc
                    var npc = (Npc)_unit;
                    stream.Write(npc.Template.AnimActionId);
                    stream.Write(true); // active
                    break;
                case 7:
                    stream.Write(0u); // type(id)
                    stream.Write(0f); // growRate
                    stream.Write(0); // randomSeed
                    stream.Write(false); // isWithered
                    stream.Write(false); // isHarvested
                    break;
                case 8: // slave
                    stream.Write(0f); // pitch
                    stream.Write(0f); // yaw
                    break;
            }

            stream.Write(_unit.ActiveWeapon);

            if (_unit is Character)
            {
                var character = (Character)_unit;
                stream.Write((byte)character.Skills.Skills.Count);
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
                stream.Write((byte)0); // learnedSkillCount
                stream.Write(0); // learnedBuffCount
            }

            stream.Write(_unit.Position.RotationX);
            stream.Write(_unit.Position.RotationY);
            stream.Write(_unit.Position.RotationZ);

            if (_unit is Character unit)
                stream.Write(unit.RaceGender);
            else if (_unit is Npc npc)
                stream.Write(npc.RaceGender);
            else
                stream.Write(_unit.RaceGender);

            if (_unit is Character)
                stream.WritePisc(0, 0, ((Character)_unit).Appellations.ActiveAppellation, 0); // pisc
            else
                stream.WritePisc(0, 0, 0, 0); // pisc

            stream.WritePisc(_unit.Faction?.Id ?? 0, _unit.Expedition?.Id ?? 0, 0, 0); // pisc

            if (_unit is Character)
            {
                var character = (Character)_unit;
                var flags = new BitSet(16);

                if (character.Invisible)
                    flags.Set(5);

                if (character.IdleStatus)
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
                stream.Write((ushort)0); // flags
            }

            if (_unit is Character)
            {
                var character = (Character)_unit;

                var activeAbilities = character.Abilities.GetActiveAbilities();
                foreach (var ability in character.Abilities.Values)
                {
                    stream.Write(ability.Exp);
                    stream.Write(ability.Order);
                }

                stream.Write((byte)activeAbilities.Count);
                foreach (var ability in activeAbilities)
                    stream.Write((byte)ability);

                stream.WriteBc(0);

                character.VisualOptions.Write(stream, 31);

                stream.Write(1); // premium

                for (var i = 0; i < 6; i++)
                    stream.Write(0); // pStat
            }

            var goodBuffs = new List<Effect>();
            var badBuffs = new List<Effect>();
            var hiddenBuffs = new List<Effect>();
            _unit.Effects.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs);

            stream.Write((byte)goodBuffs.Count); // TODO max 32
            foreach (var effect in goodBuffs)
            {
                stream.Write(effect.Index);
                stream.Write(effect.Template.BuffId);
                stream.Write(effect.SkillCaster);
                stream.Write(0u); // type(id)
                stream.Write(effect.Caster.Level); // sourceLevel
                stream.Write((short)1); // sourceAbLevel
                stream.Write(effect.Duration); // totalTime
                stream.Write(effect.GetTimeElapsed()); // elapsedTime
                stream.Write((uint)effect.Tick); // tickTime
                stream.Write(0); // tickIndex
                stream.Write(1); // stack
                stream.Write(0); // charged
                stream.Write(0u); // type(id) -> cooldownSkill
            }

            stream.Write((byte)badBuffs.Count); // TODO max 24
            foreach (var effect in badBuffs)
            {
                stream.Write(effect.Index);
                stream.Write(effect.Template.BuffId);
                stream.Write(effect.SkillCaster);
                stream.Write(0u); // type(id)
                stream.Write(effect.Caster.Level); // sourceLevel
                stream.Write((short)1); // sourceAbLevel
                stream.Write(effect.Duration); // totalTime
                stream.Write(effect.GetTimeElapsed()); // elapsedTime
                stream.Write((uint)effect.Tick); // tickTime
                stream.Write(0); // tickIndex
                stream.Write(1); // stack
                stream.Write(0); // charged
                stream.Write(0u); // type(id) -> cooldownSkill
            }

            stream.Write((byte)hiddenBuffs.Count); // TODO max 24
            foreach (var effect in hiddenBuffs)
            {
                stream.Write(effect.Index);
                stream.Write(effect.Template.BuffId);
                stream.Write(effect.SkillCaster);
                stream.Write(0u); // type(id)
                stream.Write(effect.Caster.Level); // sourceLevel
                stream.Write((short)1); // sourceAbLevel
                stream.Write(effect.Duration); // totalTime
                stream.Write(effect.GetTimeElapsed()); // elapsedTime
                stream.Write((uint)effect.Tick); // tickTime
                stream.Write(0); // tickIndex
                stream.Write(1); // stack
                stream.Write(0); // charged
                stream.Write(0u); // type(id) -> cooldownSkill
            }

            //            for (var i = 0; i < 255; i++)
            //                stream.Write(0);

            return stream;
        }
    }
}
