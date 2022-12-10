using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
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
                case Mate _:
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
            #region NetUnit
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
                    var mount = (Mate)_unit;
                    stream.Write(mount.TlId); // tl
                    stream.Write(mount.TemplateId); // npc templateId
                    stream.Write(mount.OwnerId); // characterId (masterId)
                    break;
                case BaseUnitType.Shipyard:
                    var shipyard = (Shipyard)_unit;
                    stream.Write(shipyard.ShipyardData.Id); // type(id)
                    stream.Write(shipyard.ShipyardData.TemplateId); // type(id)
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

            stream.WritePosition(_unit.Transform.Local.Position);
            stream.Write(_unit.Scale);
            stream.Write(_unit.Level);
            stream.Write(_unit.ModelId); // modelRef

            #region Inventory_Equip
            Inventory_Equip(stream, _unit, _baseUnitType); // Equip character
            //Inventory_Equip(stream, _unit); // Equip character
            #endregion Inventory_Equip

            stream.Write(_unit.ModelParams);
            stream.WriteBc(0);
            stream.Write(_unit.Hp * 100); // preciseHealth
            stream.Write(_unit.Mp * 100); // preciseMana

            #region AttachPoint1
            switch (_unit)
            {
                case Character _:
                case Npc _:
                    stream.Write((byte)AttachPointKind.System); // point
                    break;
                case Slave unit:
                    stream.Write(unit.AttachPointId);
                    if (unit.AttachPointId > -1)
                        stream.WriteBc(unit.OwnerObjId);
                    break;
                case House _:
                case Mate _:
                case Shipyard _:
                    stream.Write((byte)AttachPointKind.System);   // point
                    break;
                case Transfer unit:
                    stream.Write((byte)unit.AttachPointId);  // point
                    if (unit.AttachPointId != AttachPointKind.System)
                        stream.WriteBc(unit.BondingObjId); // point to the owner where to attach
                    break;
            }
            #endregion AttachPoint1

            #region AttachPoint2
            switch (_unit)
            {
                case Character unit:
                    if (unit.Bonding == null)
                    {
                        stream.Write((sbyte)-1); // point
                    }
                    else
                    {
                        stream.Write(unit.Bonding);
                    }
                    break;
                case Npc _:
                    stream.Write((sbyte)-1); // point
                    break;
                case Slave unit:
                    if (unit.BondingObjId > 0)
                    {
                        stream.WriteBc(unit.BondingObjId);
                    }
                    else
                    {
                        stream.Write((sbyte)-1);
                    }
                    break;
                case House _:
                case Mate _:
                case Shipyard _:
                case Transfer _:
                    stream.Write((sbyte)-1); // point
                    break;
            }
            #endregion AttachPoint2

            #region UnitModelPosture

            _unit.ModelPosture(stream, _unit, _baseUnitType, _modelPostureType);

            #endregion

            stream.Write(_unit.ActiveWeapon);

            // Skills and Passive Buffs
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

            // Rotation
            var (roll, pitch, yaw) = _unit.Transform.Local.ToRollPitchYawSBytes();
            stream.Write(roll);
            stream.Write(pitch);
            stream.Write(yaw);

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

                stream.WritePisc(character.HonorGainedInCombat, character.HostileFactionKills); // очки чести полученные в PvP, кол-во убийств в PvP
                stream.Write(flags.ToByteArray()); // flags(ushort)
                /*
                 * 0x01 - 8bit - режим боя - combat mode
                 * 0x04 - 6bit - невидимость? - invisibility?
                 * 0x08 - 5bit - дуэль - duel
                 * 0x40 - 2bit - gmmode, дополнительно 7 байт - gmmode, extra 7 bytes
                 * 0x80 - 1bit - дополнительно - additionally - tl(ushort), tl(ushort), tl(ushort), tl(ushort)
                 * 0x0100 - 16bit - дополнительно 3 байт - additional 3 bytes (bc), firstHitterTeamId(uint)
                 * 0x0400 - 14bit - надпись "Отсутсвует" под именем - the inscription "Missing" under the name
                 */
            }
            else
            {
                stream.WritePisc(0, 0); // pisc
                stream.Write((ushort)8192); // flags
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

                //character.VisualOptions.Write(stream, 31);
                character.VisualOptions.WriteOptions(stream); // cosplay_visual

                stream.Write(1); // premium

                for (var i = 0; i < 6; i++)
                {
                    stream.Write(0); // pStat
                }
            }
            #endregion NetUnit

            #region NetBuff
            var goodBuffs = new List<Buff>();
            var badBuffs = new List<Buff>();
            var hiddenBuffs = new List<Buff>();

            // TODO: Fix the patron and auction house license buff issue
            if (_unit is Character)
            {
                if (!_unit.Buffs.CheckBuff(8000011)) //TODO Wrong place
                {
                    _unit.Buffs.AddBuff(new Buff(_unit, _unit, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(8000011), null, System.DateTime.UtcNow));
                }

                if (!_unit.Buffs.CheckBuff(8000012)) //TODO Wrong place
                {
                    _unit.Buffs.AddBuff(new Buff(_unit, _unit, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(8000012), null, System.DateTime.UtcNow));
                }
            }

            _unit.Buffs.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs);

            stream.Write((byte)goodBuffs.Count); // TODO max 32
            foreach (var effect in goodBuffs)
            {
                WriteBuff(stream, effect);
            }

            stream.Write((byte)badBuffs.Count); // TODO max 24
            foreach (var effect in badBuffs)
            {
                WriteBuff(stream, effect);
            }

            stream.Write((byte)hiddenBuffs.Count); // TODO max 24
            foreach (var effect in hiddenBuffs)
            {
                WriteBuff(stream, effect);
            }
            #endregion NetBuff

            return stream;
        }

        #region Inventory_Equip
        private void Inventory_Equip(PacketStream stream, Unit unit0, BaseUnitType baseUnitType)
        {
            var unit = new Unit();
            switch (baseUnitType)
            {
                case BaseUnitType.Character:
                    unit = (Character)unit0;
                    break;
                case BaseUnitType.Npc:
                    unit = (Npc)unit0;
                    break;
                case BaseUnitType.Slave:
                    unit = (Slave)_unit;
                    break;
                case BaseUnitType.Housing:
                    unit = (House)_unit;
                    break;
                case BaseUnitType.Transfer:
                    unit = (Transfer)_unit;
                    break;
                case BaseUnitType.Mate:
                    unit = (Mate)_unit;
                    break;
                case BaseUnitType.Shipyard:
                    unit = (Shipyard)_unit;
                    break;
            }

            // calculate validFlags
            var index = 0;
            var validFlags = 0;
            var items = unit.Equipment.GetSlottedItemsList();
            foreach (var item in items)
            {
                if (item != null)
                {
                    validFlags |= 1 << index;
                }

                index++;
            }
            if (validFlags <= 0 && baseUnitType == BaseUnitType.Npc)
            {
                unit.ModelParams.SetType(UnitCustomModelType.Skin); // additional check that the NPC has no body and no face
            }
            index = 0;
            do
            {
                var item = unit.Equipment.GetItemBySlot(index);
                if (index - 19 < 0 || index - 19 > 6)
                {
                    if (index != 27) // not CosPlay
                    {
                        switch (baseUnitType)
                        {
                            case BaseUnitType.Character: // Character
                            case BaseUnitType.Housing:   // Housing
                            case BaseUnitType.Mate:      // Mate
                            case BaseUnitType.Slave:     // Slave
                                if (item != null)
                                {
                                    stream.Write(item);
                                }
                                else
                                {
                                    stream.Write(0);
                                }
                                break;
                            case BaseUnitType.Npc:       // Npc
                                if (item != null)
                                {
                                    stream.Write(item.TemplateId);
                                    stream.Write(item.Id);
                                    stream.Write(item.Grade);
                                }
                                else
                                {
                                    stream.Write(0);
                                }
                                break;
                            case BaseUnitType.Transfer:
                            case BaseUnitType.Shipyard:
                                break;
                        }
                    }
                    else
                    {
                        if (baseUnitType == BaseUnitType.Transfer || baseUnitType == BaseUnitType.Shipyard)
                        {
                            return;
                        }
                        if (item != null)
                        {
                            stream.Write(item); // Cosplay [27]
                        }
                        else
                        {
                            stream.Write(0);
                        }
                    }
                }
                else
                {
                    if (item != null)
                    {
                        stream.Write(item.TemplateId); // BodyPart | somehow_special [19..26]
                    }
                    else
                    {
                        stream.Write(0);
                    }
                }
                ++index;
            } while (index < 28);
        }

        private void Inventory_Equip(PacketStream stream, Unit unit0)
        {
            switch (unit0)
            {
                case Character unit:
                    {
                        var items = unit.Inventory.Equipment.GetSlottedItemsList();
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
                        break;
                    }
                case Npc unit:
                    {
                        for (var i = 0; i < unit.Equipment.GetSlottedItemsList().Count; i++)
                        {
                            var item = unit.Equipment.GetItemBySlot(i);

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
                        break;
                    }
                case Slave unit:
                    {
                        var items = unit.Equipment.GetSlottedItemsList();
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
                        break;
                    }
                case House unit:
                    {
                        var items = unit.Equipment.GetSlottedItemsList();
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
                        break;
                    }
                case Mate unit:
                    {
                        var items = unit.Equipment.GetSlottedItemsList();
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
                        break;
                    }
                case Shipyard _:
                case Transfer _:
                    for (var i = 0; i < 7; i++)
                    {
                        stream.Write(0); // somehow_special [19..26]
                    }
                    break;
            }
        }
        #endregion Inventory_Equip

        #region NetBuff
        private void WriteBuff(PacketStream stream, Buff effect)
        {
            stream.Write(effect.Index);
            stream.Write(effect.Template.BuffId);
            stream.Write(effect.SkillCaster);
            stream.Write(0u);                      // type(id)
            stream.Write(effect.Caster.Level);     // sourceLevel
            stream.Write((short)effect.AbLevel);   // sourceAbLevel
            stream.Write(effect.Duration);         // totalTime
            stream.Write(effect.GetTimeElapsed()); // elapsedTime
            stream.Write((uint)effect.Tick);       // tickTime
            stream.Write(0);                       // tickIndex
            stream.Write(1);                       // stack
            stream.Write(0);                       // charged
            stream.Write(0u);                      // type(id) -> cooldownSkill
        }
        #endregion NetBuff

        public override string Verbose()
        {
            return " - " + _baseUnitType.ToString() + " - " + _unit?.DebugName();
        }
    }
}
