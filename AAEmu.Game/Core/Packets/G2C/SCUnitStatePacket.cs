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
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitStatePacket : GamePacket
    {
        private readonly Unit _unit;
        private readonly BaseUnitType _baseUnitType;
        private UnitModelPostureType _unitModelPostureType;

        public SCUnitStatePacket(Unit unit) : base(SCOffsets.SCUnitStatePacket, 1)
        {
            _unit = unit;
            switch (_unit)
            {
                case Character _:
                    _baseUnitType = BaseUnitType.Character;
                    _unitModelPostureType = UnitModelPostureType.None;
                    break;
                case Npc npc:
                    {
                        _baseUnitType = BaseUnitType.Npc;
                        _unitModelPostureType = npc.Template.AnimActionId > 0 ? UnitModelPostureType.ActorModelState : UnitModelPostureType.None;
                        break;
                    }
                case Slave _:
                    _baseUnitType = BaseUnitType.Slave;
                    _unitModelPostureType = UnitModelPostureType.None; // was TurretState = 8
                    break;
                case House _:
                    _baseUnitType = BaseUnitType.Housing;
                    _unitModelPostureType = UnitModelPostureType.HouseState;
                    break;
                case Transfer _:
                    _baseUnitType = BaseUnitType.Transfer;
                    _unitModelPostureType = UnitModelPostureType.TurretState;
                    break;
                case Mount _:
                    _baseUnitType = BaseUnitType.Mate;
                    _unitModelPostureType = UnitModelPostureType.None;
                    break;
                case Shipyard _:
                    _baseUnitType = BaseUnitType.Shipyard;
                    _unitModelPostureType = UnitModelPostureType.None;
                    break;
            }
        }

        public override PacketStream Write(PacketStream stream)
        {
            #region NetUnit
            stream.WriteBc(_unit.ObjId);
            stream.Write(_unit.Name);

            #region BaseUnitType
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
                    stream.Write(mount.OwnerId);    // characterId (masterId)
                    break;
                case BaseUnitType.Shipyard:
                    var shipyard = (Shipyard)_unit;
                    stream.Write(shipyard.Template.Id); // type(id)
                    stream.Write(shipyard.Template.TemplateId); // type(id)
                    break;
            }
            #endregion BaseUnitType

            if (_unit.OwnerId > 0) // master
            {
                var name = NameManager.Instance.GetCharacterName(_unit.OwnerId);
                stream.Write(name ?? "");
            }
            else
            {
                stream.Write("");
            }

            stream.WritePositionBc(_unit.Position.X, _unit.Position.Y, _unit.Position.Z); // posXYZ
            stream.Write(_unit.Scale);   // scale
            stream.Write(_unit.Level);   // level
            stream.Write(_unit.ModelId); // modelRef

            #region CharacterInfo
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
            #endregion CharacterInfo

            stream.Write(_unit.ModelParams);
            stream.WriteBc(0);
            stream.Write(_unit.Hp * 100); // preciseHealth
            stream.Write(_unit.Mp * 100); // preciseMana

            #region AttachPoint1
            if (_unit is Transfer)
            {
                var transfer = (Transfer)_unit;
                if (transfer.BondingObjId != 0)
                {
                    stream.Write(transfer.AttachPointId);  // point
                    stream.WriteBc(transfer.BondingObjId); // point to the owner where to attach
                }
                else
                {
                    stream.Write((sbyte)-1);   // point
                }
            }
            else
            {
                stream.Write((sbyte)-1);   // point
            }
            #endregion AttachPoint1

            #region AttachPoint2
            switch (_unit)
            {
                case Character character2 when character2.Bonding == null:
                    stream.Write((sbyte)-1); // point
                    break;
                case Character character2:
                    stream.Write(character2.Bonding);
                    // character2.Bonding write to stream
                    // (byte) attachPoint
                    // (bc)   objId
                    // (byte) kind
                    // (int)  space
                    // (int)  spot
                    break;
                case Slave slave when slave.BondingObjId > 0:
                    stream.WriteBc(slave.BondingObjId);
                    break;
                case Slave _:
                case Transfer _:
                    stream.Write((sbyte)-1); // attachPoint
                    break;
                default:
                    stream.Write((sbyte)-1); // point
                    break;
            }
            #endregion AttachPoint2

            #region ModelPosture
            // TODO added that NPCs can be hunted to move their legs while moving, but if they sit or do anything they will just stand there
            if (_baseUnitType == BaseUnitType.Npc) // NPC
            {
                if (_unit is Npc npc)
                {
                    // TODO UnitModelPosture
                    if (npc.Faction.Id != 115 || npc.Faction.Id != 3) // npc.Faction.GuardHelp не аггрессивные мобы
                    {
                        stream.Write((byte)_unitModelPostureType); // type // оставим это для того, чтобы NPC могли заниматься своими делами
                    }
                    else
                    {
                        stream.Write((byte)UnitModelPostureType.None); // type //для NPC на которых можно напасть и чтобы они шевелили ногами (для людей особенно)
                    }
                }
            }
            else // other
            {
                stream.Write((byte)_unitModelPostureType);
            }

            stream.Write(false); // isLooted

            switch (_unitModelPostureType)
            {
                case UnitModelPostureType.HouseState: // build
                    for (var i = 0; i < 2; i++)
                    {
                        stream.Write(true); // door
                    }

                    for (var i = 0; i < 6; i++)
                    {
                        stream.Write(true); // window
                    }

                    break;
                case UnitModelPostureType.ActorModelState: // npc
                    var npc = (Npc)_unit;
                    stream.Write(npc.Template.AnimActionId);
                    stream.Write(true); // activate
                    break;
                case UnitModelPostureType.FarmfieldState:
                    stream.Write(0u);    // type(id)
                    stream.Write(0f);    // growRate
                    stream.Write(0);     // randomSeed
                    stream.Write(false); // isWithered
                    stream.Write(false); // isHarvested
                    break;
                case UnitModelPostureType.TurretState: // slave
                    stream.Write(0f); // pitch
                    stream.Write(0f); // yaw
                    break;
                case UnitModelPostureType.None:
                    break;
                default:
                    break;
            }
            #endregion ModelPosture

            stream.Write(_unit.ActiveWeapon);

            if (_unit is Character)
            {
                var character = (Character)_unit;
                stream.Write((byte)character.Skills.Skills.Count); // learnedSkillCount
                foreach (var skill in character.Skills.Skills.Values)
                {
                    stream.Write(skill.Id);
                    stream.Write(skill.Level);
                }

                stream.Write(character.Skills.PassiveBuffs.Count); // learnedBuffCount
                foreach (var buff in character.Skills.PassiveBuffs.Values)
                {
                    stream.Write(buff.Id);
                }
            }
            else if (_unit is Npc npc)
            {
                stream.Write((byte)1); // learnedSkillCount
                stream.Write(npc.Template.BaseSkillId);
                stream.Write(npc.Template.Level); // возможно, что это  левел не скилла, а самого NPC

                stream.Write(0);       // learnedBuffCount
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

                stream.WritePisc(0, 0); // PvP honor points, number of murders in PvP
                stream.Write(flags.ToByteArray()); // flags(ushort)
                /*
                 * 0x01 - 8bit - combat mode
                 * 0x04 - 6bit - invisibility?
                 * 0x08 - 5bit - duel
                 * 0x40 - 2bit - gmmode, 7 additional bytes
                 * 0x80 - 1bit - additional tl(ushort), tl(ushort), tl(ushort), tl(ushort)
                 * 0x0100 - 16bit - additional 3 байт (bc), firstHitterTeamId(uint)
                 * 0x0400 - 14bit - надпись "Отсутсвует" под именем
                 */
            }
            else if (_unit is Npc)
            {
                stream.WritePisc(0, 0); // pisc
                stream.Write((ushort)8192); // flags
            }
            else
            {
                stream.WritePisc(0, 0); // pisc
                stream.Write((ushort)8704); // flags
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

                stream.Write((byte)activeAbilities.Count); // nActive

                foreach (var ability in activeAbilities)
                {
                    stream.Write((byte)ability);          // active
                }

                stream.WriteBc(0);                        // unk

                character.VisualOptions.Write(stream, 31); // stp

                stream.Write(1); // premium

                for (var i = 0; i < 6; i++)
                {
                    stream.Write(0); // pStat
                }
            }
            #endregion NetUnit

            #region NetBuff
            var goodBuffs = new List<Effect>();
            var badBuffs = new List<Effect>();
            var hiddenBuffs = new List<Effect>();

            // TODO: Fix the patron and auction house license buff issue
            if (_unit is Character)
            {
                if (!_unit.Effects.CheckBuff(8000011)) //TODO Wrong place
                {
                    _unit.Effects.AddEffect(new Effect(_unit, _unit, SkillCaster.GetByType(EffectOriginType.Skill), SkillManager.Instance.GetBuffTemplate(8000011), null, System.DateTime.Now));
                }

                if (!_unit.Effects.CheckBuff(8000012)) //TODO Wrong place
                {
                    _unit.Effects.AddEffect(new Effect(_unit, _unit, SkillCaster.GetByType(EffectOriginType.Skill), SkillManager.Instance.GetBuffTemplate(8000012), null, System.DateTime.Now));
                }
            }

            _unit.Effects.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs);

            stream.Write((byte)goodBuffs.Count); // TODO max 32
            foreach (var effect in goodBuffs)
            {
                stream.Write(effect.Index);
                stream.Write(effect.Template.BuffId);
                stream.Write(effect.SkillCaster);
                stream.Write(0u);                      // type(id)
                stream.Write(effect.Caster.Level);     // sourceLevel
                stream.Write(effect.AbLevel);          // sourceAbLevel
                stream.Write(effect.Duration);         // totalTime
                stream.Write(effect.GetTimeElapsed()); // elapsedTime
                stream.Write((uint)effect.Tick);       // tickTime
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
                stream.Write(0u);                      // type(id)
                stream.Write(effect.Caster.Level);     // sourceLevel
                stream.Write(effect.AbLevel);          // sourceAbLevel
                stream.Write(effect.Duration);         // totalTime
                stream.Write(effect.GetTimeElapsed()); // elapsedTime
                stream.Write((uint)effect.Tick);       // tickTime
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
                stream.Write(0u);                      // type(id)
                stream.Write(effect.Caster.Level);     // sourceLevel
                stream.Write(effect.AbLevel);          // sourceAbLevel
                stream.Write(effect.Duration);         // totalTime
                stream.Write(effect.GetTimeElapsed()); // elapsedTime
                stream.Write((uint)effect.Tick);       // tickTime
                stream.Write(0); // tickIndex
                stream.Write(1); // stack
                stream.Write(0); // charged
                stream.Write(0u); // type(id) -> cooldownSkill
            }
            #endregion NetBuff
            return stream;
        }
    }
}
