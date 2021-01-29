using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitStatePacket : GamePacket
    {
        private readonly Unit _unit;
        private readonly BaseUnitType _baseUnitType;
        private ModelPostureType _modelPostureType;
        //private byte _attachPoint;

        public SCUnitStatePacket(Unit unit) : base(SCOffsets.SCUnitStatePacket, 1)
        {
            _unit = unit;
            switch (_unit)
            {
                case Character _:
                    _baseUnitType = BaseUnitType.Character;
                    _modelPostureType = ModelPostureType.None;
                    break;
                case Npc npc:
                    {
                        _baseUnitType = BaseUnitType.Npc;
                        if (npc.Template.AnimActionId > 0)
                        {
                            _modelPostureType = ModelPostureType.ActorModelState;
                        }
                        else
                        {
                            _modelPostureType = ModelPostureType.None;
                        }

                        break;
                    }
                case Slave _:
                    _baseUnitType = BaseUnitType.Slave;
                    _modelPostureType = ModelPostureType.None; // was TurretState = 8
                    break;
                case House _:
                    _baseUnitType = BaseUnitType.Housing;
                    _modelPostureType = ModelPostureType.HouseState;
                    break;
                case Transfer _:
                    _baseUnitType = BaseUnitType.Transfer;
                    _modelPostureType = ModelPostureType.TurretState;
                    break;
                case Mount _:
                    _baseUnitType = BaseUnitType.Mate;
                    _modelPostureType = ModelPostureType.None;
                    break;
                case Shipyard _:
                    _baseUnitType = BaseUnitType.Shipyard;
                    _modelPostureType = ModelPostureType.None;
                    break;
            }
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unit.ObjId);
            stream.Write(_unit.Name);
            stream.Write((byte)_baseUnitType);
            switch (_baseUnitType)
            {
                case BaseUnitType.Character:
                    var character = (Character)_unit;
                    stream.Write(character.Id); // type(id)
                    stream.Write(0L);           // v?
                    break;
                case BaseUnitType.Npc:
                    var npc = (Npc)_unit;
                    stream.WriteBc(npc.ObjId);    // objId
                    stream.Write(npc.TemplateId); // npc templateId
                    stream.Write(0u);             // type(id)
                    stream.Write((byte)0);        // clientDriven
                    break;
                case BaseUnitType.Slave:
                    var slave = (Slave)_unit;
                    stream.Write(slave.Id);         // Id ?
                    stream.Write(slave.TlId);       // tl
                    stream.Write(slave.TemplateId); // templateId
                    stream.Write(slave.Summoner.ObjId); // ownerId
                    break;
                case BaseUnitType.Housing:
                    var house = (House)_unit;
                    var buildStep = house.CurrentStep == -1
                        ? 0
                        : -house.Template.BuildSteps.Count + house.CurrentStep;

                    stream.Write(house.TlId);       // tl
                    stream.Write(house.TemplateId); // house templateId
                    stream.Write((short)buildStep); // buildstep
                    break;
                case BaseUnitType.Transfer:
                    var transfer = (Transfer)_unit;
                    stream.Write(transfer.TlId);       // tl
                    stream.Write(transfer.TemplateId); // transfer templateId
                    break;
                case BaseUnitType.Mate:
                    var mount = (Mount)_unit;
                    stream.Write(mount.TlId); // tl
                    stream.Write(mount.TemplateId); // npc teplateId
                    stream.Write(mount.OwnerId); // characterId (masterId)
                    break;
                case BaseUnitType.Shipyard:
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
            {
                stream.Write("");
            }

            stream.WritePosition(_unit.Position.X, _unit.Position.Y, _unit.Position.Z);
            stream.Write(_unit.Scale);
            stream.Write(_unit.Level);
            stream.Write(_unit.ModelId); // modelRef

            if (_unit is Character)
            {
                var character = (Character)_unit;
                var items = character.Inventory.Equipment.GetSlottedItemsList();
                foreach (var item in items)
                {
                    if (item == null)
                    {
                        stream.Write(0);
                    }
                    else
                    {
                        stream.Write(item);
                    }
                }
            }
            else if (_unit is Npc)
            {
                var npc = (Npc)_unit;
                for (var i = 0; i < npc.Equipment.GetSlottedItemsList().Count; i++)
                {
                    var item = npc.Equipment.GetItemBySlot(i);

                    if (item is BodyPart)
                    {
                        stream.Write(item.TemplateId);
                    }
                    else if (item != null)
                    {
                        if (i == 27) // Cosplay
                        {
                            stream.Write(item);
                        }
                        else
                        {
                            stream.Write(item.TemplateId);
                            stream.Write(0L);
                            stream.Write((byte)0);
                        }
                    }
                    else
                    {
                        stream.Write(0);
                    }
                }
            }
            else
            {
                for (var i = 0; i < 28; i++)
                {
                    stream.Write(0);
                }
            }

            stream.Write(_unit.ModelParams);
            stream.WriteBc(0);
            stream.Write(_unit.Hp * 100); // preciseHealth
            stream.Write(_unit.Mp * 100); // preciseMana
            //stream.Write(_attachPoint);   // point
            //_modelPostureType = ModelPostureType.None;
            if (_unit is Transfer)
            {
                var transfer = (Transfer)_unit;
                if (transfer.BondingObjId != 0)
                {
                    stream.Write(transfer.AttachPointId); // point
                    stream.WriteBc(transfer.BondingObjId);     // point to the owner where to attach
                }
                else
                {
                    stream.Write((sbyte)-1);   // point
                }
            }
            else if (_unit is Slave slave)
            {
                stream.Write(slave.AttachPointId);
                if (slave.AttachPointId > -1)
                {
                    stream.WriteBc(slave.OwnerObjId);
                }
            }
            else
            {
                stream.Write((sbyte)-1);   // point
            }

            //if (_attachPoint != 255)      // -1
            //{
            //    var transfer = (Transfer)_unit;
            //    stream.WriteBc(transfer.OwnerId); // point to the owner where to attach
            //}

            if (_unit is Character character2)
            {
                if (character2.Bonding == null)
                {
                    stream.Write((sbyte)-1); // point
                }
                else
                {
                    stream.Write(character2.Bonding);
                }
            }
            else if (_unit is Slave slave)
            {
                if (slave.BondingObjId > 0)
                {
                    stream.WriteBc(slave.BondingObjId);
                }
                else
                {
                    stream.Write((sbyte)-1);
                }
            }
            else if (_unit is Transfer transfer)
            {
                //if (transfer.BondingObjId > 0)
                //{
                //    stream.WriteBc(transfer.BondingObjId);
                //}
                //else
                //{
                stream.Write((sbyte)-1); // point
                //}
            }
            else
            {
                stream.Write((sbyte)-1); // point
            }

            // TODO added that NPCs can be hunted to move their legs while moving, but if they sit or do anything they will just stand there
            if (_baseUnitType == BaseUnitType.Npc) // NPC
            {
                if (_unit is Npc npc)
                {
                    // TODO UnitModelPosture
                    if (npc.Faction.GuardHelp)
                    {
                        stream.Write((byte)_modelPostureType); // type // оставим это для того, чтобы NPC могли заниматься своими делами
                    }
                    else
                    {
                        _modelPostureType = 0; // type //для NPC на которых можно напасть и чтобы они шевелили ногами (для людей особенно)
                        stream.Write((byte)_modelPostureType);
                    }
                }
            }
            else // other
            {
                stream.Write((byte)_modelPostureType);
            }

            stream.Write(false); // isLooted

            switch (_modelPostureType)
            {
                case ModelPostureType.HouseState: // build
                    for (var i = 0; i < 2; i++)
                    {
                        stream.Write(true); // door
                    }

                    for (var i = 0; i < 6; i++)
                    {
                        stream.Write(true); // window
                    }

                    break;
                case ModelPostureType.ActorModelState: // npc
                    var npc = (Npc)_unit;
                    stream.Write(npc.Template.AnimActionId);
                    stream.Write(true); // activate
                    break;
                case ModelPostureType.FarmfieldState:
                    stream.Write(0u);    // type(id)
                    stream.Write(0f);    // growRate
                    stream.Write(0);     // randomSeed
                    stream.Write(false); // isWithered
                    stream.Write(false); // isHarvested
                    break;
                case ModelPostureType.TurretState: // slave
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
                {
                    stream.Write(buff.Id);
                }
            }
            else
            {
                stream.Write((byte)0); // learnedSkillCount
                stream.Write(0);       // learnedBuffCount
            }

            stream.Write(_unit.Position.RotationX);
            stream.Write(_unit.Position.RotationY);
            stream.Write(_unit.Position.RotationZ);

            switch (_unit)
            {
                case Character unit:
                    stream.Write(unit.RaceGender);
                    break;
                case Npc npc:
                    stream.Write(npc.RaceGender);
                    break;
                default:
                    stream.Write(_unit.RaceGender);
                    break;
            }

            if (_unit is Character)
            {
                stream.WritePisc(0, 0, ((Character)_unit).Appellations.ActiveAppellation, 0); // pisc
            }
            else
            {
                stream.WritePisc(0, 0, 0, 0); // pisc
            }

            stream.WritePisc(_unit.Faction?.Id ?? 0, _unit.Expedition?.Id ?? 0, 0, 0); // pisc

            if (_unit is Character)
            {
                var character = (Character)_unit;
                var flags = new BitSet(16);

                if (character.Invisible)
                {
                    flags.Set(5);
                }

                if (character.IdleStatus)
                {
                    flags.Set(13);
                }

                stream.WritePisc(character.PvPHonor, character.PvPKills); // очки чести полученные в PvP, кол-во убийств в PvP
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
                {
                    stream.Write((byte)ability);
                }

                stream.WriteBc(0);

                character.VisualOptions.Write(stream, 31);

                stream.Write(1); // premium

                for (var i = 0; i < 6; i++)
                {
                    stream.Write(0); // pStat
                }
            }

            var goodBuffs = new List<Buff>();
            var badBuffs = new List<Buff>();
            var hiddenBuffs = new List<Buff>();

            // TODO: Fix the patron and auction house license buff issue
            if (_unit is Character)
            {
                if (!_unit.Buffs.CheckBuff(8000011)) //TODO Wrong place
                {
                    _unit.Buffs.AddBuff(new Buff(_unit, _unit, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(8000011), null, System.DateTime.Now));
                }

                if (!_unit.Buffs.CheckBuff(8000012)) //TODO Wrong place
                {
                    _unit.Buffs.AddBuff(new Buff(_unit, _unit, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(8000012), null, System.DateTime.Now));
                }
            }

            _unit.Buffs.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs);

            stream.Write((byte)goodBuffs.Count); // TODO max 32
            foreach (var effect in goodBuffs)
            {
                stream.Write(effect.Index);
                stream.Write(effect.Template.BuffId);
                stream.Write(effect.SkillCaster);
                stream.Write(0u); // type(id)
                stream.Write(effect.Caster.Level); // sourceLevel
                stream.Write((short)effect.AbLevel); // sourceAbLevel
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
                stream.Write((short)effect.AbLevel); // sourceAbLevel
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
                stream.Write((short)effect.AbLevel); // sourceAbLevel
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
