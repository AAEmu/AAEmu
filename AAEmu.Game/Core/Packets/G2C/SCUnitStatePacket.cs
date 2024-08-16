﻿using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitStatePacket : GamePacket
{
    private readonly Unit _unit;
    private readonly BaseUnitType _baseUnitType;
    private ModelPostureType _modelPostureType;

    public SCUnitStatePacket(Unit unit) : base(SCOffsets.SCUnitStatePacket, 5)
    {
        _unit = unit;
        _modelPostureType = unit.ModelPostureType;
        switch (_unit)
        {
            case Character:
                _baseUnitType = BaseUnitType.Character;
                _modelPostureType = ModelPostureType.None;
                break;
            case Npc npc:
                _baseUnitType = BaseUnitType.Npc;
                _modelPostureType = npc.AnimActionId > 0 ? ModelPostureType.ActorModelState : ModelPostureType.None;
                break;
            case Slave:
                _baseUnitType = BaseUnitType.Slave;
                _modelPostureType = ModelPostureType.TurretState; // was TurretState = 8
                break;
            case House:
                _baseUnitType = BaseUnitType.Housing;
                _modelPostureType = ModelPostureType.HouseState;
                break;
            case Transfer:
                _baseUnitType = BaseUnitType.Transfer;
                _modelPostureType = ModelPostureType.TurretState;
                break;
            case Mate:
                _baseUnitType = BaseUnitType.Mate;
                _modelPostureType = ModelPostureType.None;
                break;
            case Shipyard:
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

        // Cache character & npc
        var character = _unit as Character;
        var npc = _unit as Npc;

        #region BaseUnitType
        stream.Write((byte)_baseUnitType);
        switch (_baseUnitType)
        {
            case BaseUnitType.Character:
                stream.Write(character?.Id ?? 0u); // type(id)
                stream.Write(0L);                  // v
                break;
            case BaseUnitType.Npc:
                stream.WriteBc(npc.ObjId);    // objId
                stream.Write(npc.TemplateId); // npc templateId
                stream.Write(0);              // type(id) (ownerId?)
                stream.Write((byte)0);        // clientDriven
                break;
            case BaseUnitType.Slave:
                var slave = (Slave)_unit;
                stream.Write(slave.Id);             // Id ? slave.Id
                stream.Write(slave.TlId);           // tl
                stream.Write(slave.TemplateId);     // templateId
                stream.Write(slave.Summoner?.Id ?? 0); // ownerId
                break;
            case BaseUnitType.Housing:
                var house = (House)_unit;
                var buildStep = house.CurrentStep == -1
                    ? 0
                    : -house.Template.BuildSteps.Count + house.CurrentStep;

                stream.Write(house.TlId); // tl
                stream.Write(house.TemplateId); // templateId
                stream.Write((short)buildStep); // buildstep
                break;
            case BaseUnitType.Transfer:
                var transfer = (Transfer)_unit;
                stream.Write(transfer.TlId); // tl
                stream.Write(transfer.TemplateId); // templateId
                break;
            case BaseUnitType.Mate:
                var mount = (Mate)_unit;
                stream.Write(mount.TlId);       // tl
                stream.Write(mount.TemplateId); // teplateId
                stream.Write(mount.OwnerId);    // characterId (masterId)
                break;
            case BaseUnitType.Shipyard:
                var shipyard = (Shipyard)_unit;
                stream.Write(shipyard.ShipyardData.Id);         // type(id)
                stream.Write(shipyard.ShipyardData.TemplateId); // type(id)
                break;
        }
        #endregion BaseUnitType

        if (_unit.OwnerId > 0) // master
        {
            var name = NameManager.Instance.GetCharacterName(_unit.OwnerId);
            stream.Write(name ?? "");
        }
        else
            stream.Write("");

        stream.WritePosition(_unit.Transform.Local.Position);
        stream.Write(_unit.Scale); // scale
        stream.Write(_unit.Level); // level
        stream.Write(_unit.HierLevel); // hierarchy level for 3.0.3.0
        for (var i = 0; i < 4; i++)
            stream.Write((sbyte)-1); // slot for 3.0.3.0

        stream.Write(_unit.ModelId); // modelRef

        #region CharacterInfo_3EB0

        //Inventory_Equip1(stream, _unit); // Equip character
        //Inventory_Equip2(stream, _unit, _baseUnitType); // Equip character
        //Inventory_Equip0(stream, _unit); // Equip character
        Inventory_Equip3(stream, _unit); // Equip character

        #endregion CharacterInfo_3EB0

        stream.Write(_unit.ModelParams); // CustomModel_3570

        stream.WriteBc(0);
        stream.Write(_unit.Hp * 100); // preciseHealth
        stream.Write(_unit.Mp * 100); // preciseMana

        #region AttachPoint1
        switch (_unit)
        {
            case Gimmick:
            case Portal:
            case Character:
            case Npc:
            case House:
            case Mate:
            case Shipyard:
                stream.Write((byte)AttachPointKind.System);   // point
                break;
            case Slave unit:
                stream.Write(unit.AttachPointId);
                if (unit.AttachPointId > -1)
                    stream.WriteBc(unit.OwnerObjId);
                break;
            case Transfer unit:
                if (unit.BondingObjId != 0)
                {
                    stream.Write((byte)unit.AttachPointId);  // point
                    stream.WriteBc(unit.BondingObjId); // point to the owner where to attach
                }
                else
                    stream.Write((byte)AttachPointKind.System);   // point
                break;
        }
        #endregion AttachPoint1

        #region AttachPoint2
        switch (_unit)
        {
            case Character:
                switch (character.Bonding)
                {
                    case null:
                        stream.Write((byte)AttachPointKind.System);   // point
                        break;
                    default:
                        stream.Write(character.Bonding);
                        break;
                }
                break;
            case Npc:
            case House:
            case Mate:
            case Shipyard:
            case Transfer:
                stream.Write((byte)AttachPointKind.System);   // point
                break;
            case Slave unit:
                if (unit.BondingObjId > 0)
                {
                    stream.WriteBc(unit.BondingObjId);
                    stream.Write(0);  // space
                    stream.Write(0);  // spot
                    stream.Write(0);  // type
                }
                else
                    stream.Write((byte)AttachPointKind.System);   // point
                break;
        }
        #endregion AttachPoint2

        #region UnitModelPosture

        Unit.ModelPosture(stream, _unit, (_unit as Npc)?.AnimActionId ?? 0, true);

        #endregion

        stream.Write(_unit.ActiveWeapon);

        switch (_unit)
        {
            case Character:
                {
                    var skillList = character.Skills.Skills.Values.ToList();

                    stream.Write((byte)skillList.Count);       // learnedSkillCount
                    if (skillList.Count > 0)
                        Logger.Trace($"Warning! character.learnedSkillCount = {character.Skills.Skills.Count}");

                    stream.Write((byte)character.Skills.PassiveBuffs.Count); // passiveBuffCount
                    if (character.Skills.PassiveBuffs.Count > 0)
                        Logger.Trace($"Warning! character.passiveBuffCount = {character.Skills.PassiveBuffs.Count}");

                    stream.Write(character.HighAbilityRsc);                  // highAbilityRsc

                    var hcount = skillList.Count;
                    if (hcount > 0)
                    {
                        var index = 0;
                        do
                        {
                            var pcount = 4;
                            if (hcount <= 4)
                                pcount = hcount;
                            switch (pcount)
                            {
                                case 1:
                                    {
                                        stream.WritePisc(skillList[index].Id);
                                        index += 1;
                                        break;
                                    }
                                case 2:
                                    {
                                        stream.WritePisc(skillList[index].Id, skillList[index + 1].Id);
                                        index += 2;
                                        break;
                                    }
                                case 3:
                                    {
                                        stream.WritePisc(skillList[index].Id, skillList[index + 1].Id,
                                            skillList[index + 2].Id);
                                        index += 3;
                                        break;
                                    }
                                case 4:
                                    {
                                        stream.WritePisc(skillList[index].Id, skillList[index + 1].Id,
                                            skillList[index + 2].Id, skillList[index + 3].Id);
                                        index += 4;
                                        break;
                                    }
                            }
                            hcount -= pcount;
                        } while (hcount > 0);
                    }

                    var buffList = character.Skills.PassiveBuffs.Values.ToList();
                    if (buffList.Count > 0)
                    {
                        hcount = buffList.Count;
                        var index = 0;
                        do
                        {
                            var pcount = 4;
                            if (hcount <= 4)
                                pcount = hcount;
                            switch (pcount)
                            {
                                case 1:
                                    {
                                        stream.WritePisc(buffList[index].Template.BuffId);
                                        index += 1;
                                        break;
                                    }
                                case 2:
                                    {
                                        stream.WritePisc(buffList[index].Template.BuffId,
                                            buffList[index + 1].Template.BuffId);
                                        index += 2;
                                        break;
                                    }
                                case 3:
                                    {
                                        stream.WritePisc(buffList[index].Template.BuffId,
                                            buffList[index + 1].Template.BuffId,
                                            buffList[index + 2].Template.BuffId);
                                        index += 3;
                                        break;
                                    }
                                case 4:
                                    {
                                        stream.WritePisc(buffList[index].Template.BuffId,
                                            buffList[index + 1].Template.BuffId,
                                            buffList[index + 2].Template.BuffId,
                                            buffList[index + 3].Template.BuffId);
                                        index += 4;
                                        break;
                                    }
                            }
                            hcount -= pcount;
                        } while (hcount > 0);
                    }
                    break;
                }
            case Npc:
                {
                    var skills = new List<NpcSkill>();

                    if (npc.Template.BaseSkillId > 0)
                    {
                        var baseSkill = new NpcSkill
                        {
                            Id = 0,
                            OwnerId = npc.TemplateId,
                            OwnerType = "Npc",
                            SkillId = (uint)npc.Template.BaseSkillId,
                            SkillUseCondition = SkillUseConditionKind.InCombat,
                            SkillUseParam1 = 0,
                            SkillUseParam2 = 0
                        };
                        skills.Add(baseSkill);
                    }

                    foreach (var sl in npc.Template.Skills.Values)
                        skills.AddRange(sl);

                    stream.Write((byte)skills.Count);    // learnedSkillCount
                    if (skills.Count > 0)
                        Logger.Trace($"Warning! npc.Template.Skills.Count = {skills.Count}");

                    stream.Write((byte)npc.Template.PassiveBuffs.Count); // passiveBuffCount

                    stream.Write(npc.HighAbilityRsc);                    // highAbilityRsc

                    var hcount = skills.Count;
                    if (hcount > 0)
                    {
                        var index = 0;
                        do
                        {
                            var pcount = 4;
                            if (hcount <= 4)
                                pcount = hcount;
                            switch (pcount)
                            {
                                case 1:
                                    {
                                        stream.WritePisc(skills[index].SkillId);
                                        index += 1;
                                        break;
                                    }
                                case 2:
                                    {
                                        stream.WritePisc(skills[index].SkillId, skills[index + 1].SkillId);
                                        index += 2;
                                        break;
                                    }
                                case 3:
                                    {
                                        stream.WritePisc(skills[index].SkillId, skills[index + 1].SkillId,
                                            skills[index + 2].SkillId);
                                        index += 3;
                                        break;
                                    }
                                case 4:
                                    {
                                        stream.WritePisc(skills[index].SkillId, skills[index + 1].SkillId,
                                            skills[index + 2].SkillId, skills[index + 3].SkillId);
                                        index += 4;
                                        break;
                                    }
                            }
                            hcount -= pcount;
                        } while (hcount > 0);
                    }

                    var buffs = npc.Template.PassiveBuffs;
                    if (buffs.Count > 0)
                    {
                        hcount = buffs.Count;
                        var index = 0;
                        do
                        {
                            var pcount = 4;
                            if (hcount <= 4)
                                pcount = hcount;
                            switch (pcount)
                            {
                                case 1:
                                    {
                                        stream.WritePisc(buffs[index].PassiveBuffId);
                                        index += 1;
                                        break;
                                    }
                                case 2:
                                    {
                                        stream.WritePisc(buffs[index].PassiveBuffId, buffs[index + 1].PassiveBuffId);
                                        index += 2;
                                        break;
                                    }
                                case 3:
                                    {
                                        stream.WritePisc(buffs[index].PassiveBuffId, buffs[index + 1].PassiveBuffId, buffs[index + 2].PassiveBuffId);
                                        index += 3;
                                        break;
                                    }
                                case 4:
                                    {
                                        stream.WritePisc(buffs[index].PassiveBuffId, buffs[index + 1].PassiveBuffId, buffs[index + 2].PassiveBuffId, buffs[index + 3].PassiveBuffId);
                                        index += 4;
                                        break;
                                    }
                            }
                            hcount -= pcount;
                        } while (hcount > 0);
                    }
                    break;
                }
            default:
                {
                    stream.Write((byte)0); // learnedSkillCount
                    stream.Write((byte)0); // passiveBuffCount
                    stream.Write(0);       // highAbilityRsc
                    break;
                }
        }

        // Rotation
        if (_baseUnitType == BaseUnitType.Housing)
            stream.Write(_unit.Transform.Local.Rotation.Z); // должно быть float
        else
        {
            var (roll, pitch, yaw) = _unit.Transform.Local.ToRollPitchYawSBytes();
            stream.Write(roll);
            stream.Write(pitch);
            stream.Write(yaw);
        }

        switch (_unit)
        {
            case Character:
                stream.Write(character.RaceGender);
                break;
            case Npc:
                stream.Write(npc.RaceGender);
                break;
            default:
                stream.Write(_unit.RaceGender);
                break;
        }

        if (_unit is Character)
        {
            // ???, ??? and Appellation (Title)
            stream.WritePisc(0, 0, character.Appellations.ActiveAppellation, 0);      // pisc
                                                                                      // Faction and Guild
            stream.WritePisc((uint)(character.Faction?.Id ?? 0), (uint)(character.Expedition?.Id ?? 0), 0, 0); // pisc
                                                                                               // PvP Honor gained and PvP Kills
            stream.WritePisc(character.HonorGainedInCombat, character.HostileFactionKills, 0, 0); // pisc
        }
        else
        {
            stream.WritePisc(0, 0, 0, 0); // TODO второе число больше нуля, что это за число?
            stream.WritePisc((uint)(_unit.Faction?.Id ?? 0), (uint)(_unit.Expedition?.Id ?? 0), 0, 0); // pisc
            stream.WritePisc(0, 0, 0, 0); // pisc
        }

        switch (_unit)
        {
            case Character:
                {
                    var flags = new BitSet(16); // short
                    if (character.Invisible)
                        flags.Set(5);
                    if (character.IdleStatus)
                        flags.Set(13);
                    stream.Write(flags.ToByteArray()); // flags(ushort)

                    /*
                    * 0x0001 - 8bit - режим боя
                    * 0x0002 - 7bit - 
                    * 0x0004 - 6bit - невидимость?
                    * 0x0008 - 5bit - дуэль
                    * 0x0010 - 4bit - 
                    * 0x0040 - 2bit - gmmode, дополнительно 7 байт
                    * 0x0080 - 1bit - дополнительно tl(ushort), tl(ushort), tl(ushort), tl(ushort)
                    * 0x0020
                    * 0x0200
                    * 0x0100 - 16bit - дополнительно 3 байт (bc), firstHitterTeamId(uint)
                    * 0x0400 - 14bit - надпись "Отсутсвует" под именем
                    * 0x1000
                    * 0x0800
                    */
                    break;
                }
            case Npc:
                //var flags = new BitSet(16); // short
                //if (npc.IsInBattle)
                //    flags.Set(1);
                //if (npc.Invisible)
                //    flags.Set(5);
                //stream.Write(flags.ToByteArray()); // flags(ushort)
                //if (flags.Get(1)) // если Npc в бою, то шлем дополнительные байты
                //{
                //    stream.WriteBc(npc.CurrentAggroTarget);  // objId бойца
                //    stream.WriteBc(0u); // TeamId команды, кто первая нанесла удар
                //}
                stream.Write((ushort)0); // flags
                break;
            default:
                stream.Write((ushort)0); // flags
                break;
        }

        if (_unit is Character)
        {
            #region read_Abilities_6300
            var activeAbilities = character.Abilities.GetActiveAbilities();
            foreach (var ability in character.Abilities.Values)
            {
                stream.Write(ability.Exp);
                stream.Write(ability.Order);
            }

            stream.Write((byte)activeAbilities.Count); // nActive
            foreach (var ability in activeAbilities)
            {
                stream.Write((byte)ability); // active
            }
            #endregion read_Abilities_6300

            #region read_Exp_Order_6460
            foreach (var ability in character.Abilities.Values)
            {
                stream.Write(ability.Exp);
                stream.Write(ability.Order);  // ability.Order
                stream.Write(false);          // canNotLevelUp
            }

            byte nHighActive = 0;
            byte nActive = 0;
            stream.Write(nHighActive); // nHighActive
            stream.Write(nActive);     // nActive
            while (nHighActive > 0)
            {
                while (nActive > 0)
                {
                    stream.Write(0); // active
                    nActive--;
                }
                nHighActive--;
            }
            #endregion read_Exp_Order_6460

            stream.WriteBc(0);     // objId
            stream.Write((byte)0); // camp

            #region Stp
            stream.Write((byte)30);  // stp
            stream.Write((byte)60);  // stp
            stream.Write((byte)50);  // stp
            stream.Write((byte)0);   // stp
            stream.Write((byte)40);  // stp
            stream.Write((byte)100); // stp

            stream.Write((byte)7); // flags
            stream.Write((byte)0); // cosplay_visual

            //character6.VisualOptions.Write(stream, 0x20); // cosplay_visual
            //character6.VisualOptions.WriteOptions(stream);

            #endregion Stp

            stream.Write(1); // premium

            #region Stats
            for (var i = 0; i < 5; i++)
            {
                stream.Write(0); // stats
            }
            stream.Write(0); // extendMaxStats
            stream.Write(0); // applyExtendCount
            stream.Write(0); // applyNormalCount
            stream.Write(0); // applySpecialCount
            #endregion Stats

            stream.WritePisc(0, 0, 0, 0);
            stream.WritePisc(0, 0);
            stream.Write((byte)0); // accountPrivilege
        }
        #endregion NetUnit

        #region NetBuff

        var goodBuffs = new List<Buff>();
        var badBuffs = new List<Buff>();
        var hiddenBuffs = new List<Buff>();

        _unit.Buffs.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs, false);

        stream.Write((byte)goodBuffs.Count); // TODO max 32
        foreach (var buff in goodBuffs)
        {
            WriteBuff(stream, buff);
        }

        stream.Write((byte)badBuffs.Count); // TODO max 24 for 1.2, 20 for 3.0.3.0
        foreach (var buff in badBuffs)
        {
            WriteBuff(stream, buff);
        }

        stream.Write((byte)hiddenBuffs.Count); // TODO max 24 for 1.2, 28 for 3.0.3.0
        foreach (var buff in hiddenBuffs)
        {
            WriteBuff(stream, buff);
        }
        #endregion NetBuff

        return stream;
    }

    private void WriteBuff(PacketStream stream, Buff buff)
    {
        stream.Write(buff.Index);        // Id
        stream.Write(buff.SkillCaster);  // skillCaster
        stream.Write(0);                 // type(id)
        stream.Write(buff.Caster.Level); // sourceLevel
        stream.Write(buff.AbLevel);      // sourceAbLevel ushort
        stream.WritePisc(0, buff.GetTimeElapsed(), 0, 0u); // add in 3.0.3.0
        stream.WritePisc(buff.Template.BuffId, 1, 0, 0u);  // add in 3.0.3.0
    }

    #region CharacterInfo_3EB0

    private void Inventory_Equip0(PacketStream stream, Unit unit)
    {
        var index = 0;
        var validFlags = 0;
        if (unit is Character character1)
        {
            // calculate validFlags
            var items = character1.Inventory.Equipment.GetSlottedItemsList();
            validFlags = CalculateValidFlags(items);
            stream.Write((uint)validFlags); // validFlags for 3.0.3.0
            var itemSlot = EquipmentItemSlot.Head;
            foreach (var item in items)
            {
                if (item == null)
                {
                    itemSlot++;
                    continue;
                }
                switch (itemSlot)
                {
                    case EquipmentItemSlot.Head:
                    case EquipmentItemSlot.Neck:
                    case EquipmentItemSlot.Chest:
                    case EquipmentItemSlot.Waist:
                    case EquipmentItemSlot.Legs:
                    case EquipmentItemSlot.Hands:
                    case EquipmentItemSlot.Feet:
                    case EquipmentItemSlot.Arms:
                    case EquipmentItemSlot.Back:
                    case EquipmentItemSlot.Undershirt:
                    case EquipmentItemSlot.Underpants:
                    case EquipmentItemSlot.Mainhand:
                    case EquipmentItemSlot.Offhand:
                    case EquipmentItemSlot.Ranged:
                    case EquipmentItemSlot.Musical:
                    case EquipmentItemSlot.Stabilizer:
                    case EquipmentItemSlot.Cosplay:
                        {
                            stream.Write(item);
                            break;
                        }
                    case EquipmentItemSlot.Face:
                    case EquipmentItemSlot.Hair:
                    case EquipmentItemSlot.Glasses:
                    case EquipmentItemSlot.Horns:
                    case EquipmentItemSlot.Tail:
                    case EquipmentItemSlot.Body:
                    case EquipmentItemSlot.Beard:
                        {
                            stream.Write(item.TemplateId);
                            break;
                        }
                    case EquipmentItemSlot.Ear1:
                    case EquipmentItemSlot.Ear2:
                    case EquipmentItemSlot.Finger1:
                    case EquipmentItemSlot.Finger2:
                    case EquipmentItemSlot.Backpack:
                        {
                            break;
                        }
                }
                itemSlot++;
            }
        }
        else if (unit is Npc npc)
        {
            // calculate validFlags for 3.0.3.0
            for (var i = 0; i < npc.Equipment.GetSlottedItemsList().Count; i++)
            {
                var item = npc.Equipment.GetItemBySlot(i);
                if (item != null)
                {
                    validFlags |= 1 << index;
                }

                index++;
            }
            stream.Write((uint)validFlags); // validFlags for 3.0.3.0
            if (validFlags <= 0)
            {
                unit.ModelParams.SetType(UnitCustomModelType.Skin); // дополнительная проверка, что у NPC нет тела и лица
                return;
            }
            var itemSlot = EquipmentItemSlot.Head;
            var items = npc.Equipment.GetSlottedItemsList();
            foreach (var item in items)
            {
                if (item == null)
                {
                    itemSlot++;
                    continue;
                }
                switch (itemSlot)
                {
                    case EquipmentItemSlot.Head:
                    case EquipmentItemSlot.Neck:
                    case EquipmentItemSlot.Chest:
                    case EquipmentItemSlot.Waist:
                    case EquipmentItemSlot.Legs:
                    case EquipmentItemSlot.Hands:
                    case EquipmentItemSlot.Feet:
                    case EquipmentItemSlot.Arms:
                    case EquipmentItemSlot.Back:
                    case EquipmentItemSlot.Undershirt:
                    case EquipmentItemSlot.Underpants:
                    case EquipmentItemSlot.Mainhand:
                    case EquipmentItemSlot.Offhand:
                    case EquipmentItemSlot.Ranged:
                    case EquipmentItemSlot.Musical:
                        {
                            stream.Write(item.TemplateId);
                            stream.Write(0L);
                            stream.Write((byte)0);
                            break;
                        }
                    case EquipmentItemSlot.Cosplay:
                        {
                            stream.Write(item);
                            break;
                        }
                    case EquipmentItemSlot.Face:
                    case EquipmentItemSlot.Hair:
                    case EquipmentItemSlot.Glasses:
                    case EquipmentItemSlot.Horns:
                    case EquipmentItemSlot.Tail:
                    case EquipmentItemSlot.Body:
                    case EquipmentItemSlot.Beard:
                        {
                            stream.Write(item.TemplateId);
                            break;
                        }
                    case EquipmentItemSlot.Ear1:
                    case EquipmentItemSlot.Ear2:
                    case EquipmentItemSlot.Finger1:
                    case EquipmentItemSlot.Finger2:
                    case EquipmentItemSlot.Backpack:
                    case EquipmentItemSlot.Stabilizer:
                        {
                            break;
                        }
                }
                itemSlot++;
            }
        }
        else // for transfer and Shipyard
        {
            stream.Write(0u); // validFlags for 3.0.3.0
        }

        if (_unit is Character chrUnit)
        {
            index = 0;
            var ItemFlags = 0;
            var items = chrUnit.Inventory.Equipment.GetSlottedItemsList();
            foreach (var item in items)
            {
                if (item != null)
                {
                    var v15 = (int)item.ItemFlags << index;
                    ++index;
                    ItemFlags |= v15;
                }
            }
            stream.Write(ItemFlags); //  ItemFlags flags for 3.0.3.0
        }
    }
    private void Inventory_Equip1(PacketStream stream, Unit unit0, BaseUnitType baseUnitType)
    {
        var unit = new Unit();
        switch (baseUnitType)
        {
            case BaseUnitType.Character:
                {
                    unit = (Character)unit0;
                    break;
                }
            case BaseUnitType.Npc:
                {
                    unit = (Npc)unit0;
                    break;
                }
            case BaseUnitType.Slave:
                {
                    unit = (Slave)_unit;
                    break;
                }
            case BaseUnitType.Housing:
                {
                    unit = (House)_unit;
                    break;
                }
            case BaseUnitType.Transfer:
                {
                    unit = (Transfer)_unit;
                    break;
                }
            case BaseUnitType.Mate:
                {
                    unit = (Mate)_unit;
                    break;
                }
            case BaseUnitType.Shipyard:
                {
                    unit = (Shipyard)_unit;
                    break;
                }
            default:
                {
                    break;
                }
        }

        var items = unit.Equipment.GetSlottedItemsList();
        var validFlags = CalculateValidFlags(items);
        stream.Write((uint)validFlags); // validFlags for 3.0.3.0

        if (validFlags <= 0)
        {
            unit.ModelParams.SetType(UnitCustomModelType.Skin); // дополнительная проверка, что у NPC нет тела и лица
            return;
        }

        var index = 0;
        do
        {
            if (((validFlags >> index) & 1) != 0)
            {
                Item item;
                //if ((index - 19 >= 0 && index - 19 <= 6) || baseUnitType == BaseUnitType.Slave) // Slave
                if (index - 19 < 0 || index - 19 > 6)
                {
                    //if (index != 27 || baseUnitType != BaseUnitType.Npc)  // not CosPlay || not Npc
                    if (index != 27) // not CosPlay
                    {
                        switch (baseUnitType)
                        {
                            case BaseUnitType.Character: // Character
                            case BaseUnitType.Housing: // Housing
                            case BaseUnitType.Mate: // Mate
                            case BaseUnitType.Slave: // Slave
                                {
                                    item = unit.Equipment.GetItemBySlot(index);
                                    stream.Write(item);
                                    break;
                                }
                            case BaseUnitType.Npc: // Npc
                                {
                                    item = unit.Equipment.GetItemBySlot(index);
                                    stream.Write(item.TemplateId);
                                    stream.Write(item.Id);
                                    stream.Write(item.Grade);
                                    break;
                                }
                            case BaseUnitType.Transfer:
                            case BaseUnitType.Shipyard:
                                {
                                    break;
                                }
                            default:
                                {
                                    break;
                                }
                        }
                    }
                    else
                    {
                        item = unit.Equipment.GetItemBySlot(index);
                        stream.Write(item); // Cosplay [27]
                    }
                }
                else
                {
                    item = unit.Equipment.GetItemBySlot(index);
                    stream.Write(item.TemplateId); // somehow_special [19..26]
                }
            }

            ++index;
        } while (index < 29);

        if (baseUnitType != BaseUnitType.Character) { return; }

        var itemFlags = CalculateItemFlags(items);
        stream.Write(itemFlags); // ItemFlags flags for 3.0.3.0
    }
    private void Inventory_Equip2(PacketStream stream, Unit unit0, BaseUnitType baseUnitType)
    {
        var unit = new Unit();
        switch (baseUnitType)
        {
            case BaseUnitType.Character:
                {
                    unit = (Character)unit0;
                    break;
                }
            case BaseUnitType.Npc:
                {
                    unit = (Npc)unit0;
                    break;
                }
            case BaseUnitType.Slave:
                {
                    unit = (Slave)_unit;
                    break;
                }
            case BaseUnitType.Housing:
                {
                    unit = (House)_unit;
                    break;
                }
            case BaseUnitType.Transfer:
                {
                    unit = (Transfer)_unit;
                    break;
                }
            case BaseUnitType.Mate:
                {
                    unit = (Mate)_unit;
                    break;
                }
            case BaseUnitType.Shipyard:
                {
                    unit = (Shipyard)_unit;
                    break;
                }
            default:
                {
                    break;
                }
        }

        // calculate validFlags
        var items = unit.Equipment.GetSlottedItemsList();
        var validFlags = CalculateValidFlags(items);
        stream.Write((uint)validFlags); // validFlags for 3.0.3.0

        if (validFlags <= 0)
        {
            unit.ModelParams.SetType(UnitCustomModelType.Skin); // дополнительная проверка, что у NPC нет тела и лица
            return;
        }

        var index = 0;
        do
        {
            if (((validFlags >> index) & 1) != 0)
            {
                Item item;
                switch (index)
                {
                    case 0: // Head
                    case 1: // Neck
                    case 2: // Chest
                    case 3: // Waist
                    case 4: // Legs
                    case 5: // Hands
                    case 6: // Feet
                    case 7: // Arms
                    case 8: // Back
                    case 9: // Ear1
                    case 10: // Ear2
                    case 11: // Finger1
                    case 12: // Finger2
                    case 13: // Undershirt
                    case 14: // Underpants
                    case 15: // Mainhand
                    case 16: // Offhand
                    case 17: // Ranged
                    case 18: // Musical
                    case 26: // Backpack
                    case 28: // Stabilizer
                        {
                            switch (baseUnitType)
                            {
                                case BaseUnitType.Character: // Character
                                case BaseUnitType.Housing:   // Housing
                                case BaseUnitType.Mate:      // Mate
                                case BaseUnitType.Slave:     // Slave
                                    {
                                        item = unit.Equipment.GetItemBySlot(index);
                                        stream.Write(item);
                                        break;
                                    }
                                case BaseUnitType.Npc:       // Npc
                                    {
                                        item = unit.Equipment.GetItemBySlot(index);
                                        stream.Write(item.TemplateId);
                                        stream.Write(item.Id);
                                        stream.Write(item.Grade);
                                        break;
                                    }
                                case BaseUnitType.Transfer:
                                case BaseUnitType.Shipyard:
                                default:
                                    {
                                        break;
                                    }
                            }
                            break;
                        }
                    case 19: // Face
                    case 20: // Hair
                    case 21: // Glasses
                    case 22: // Horns
                    case 23: // Tail
                    case 24: // Body
                    case 25: // Beard
                        {
                            item = unit.Equipment.GetItemBySlot(index);
                            stream.Write(item.TemplateId); // somehow_special [19..25]
                            break;
                        }
                    case 27: // Cosplay
                        {
                            item = unit.Equipment.GetItemBySlot(index);
                            stream.Write(item); // Cosplay [27]
                            break;
                        }
                }
            }

            ++index;
        } while (index < 29);

        if (baseUnitType != BaseUnitType.Character) { return; }

        var itemFlags = CalculateItemFlags(items);
        stream.Write(itemFlags); // ItemFlags flags for 3.0.3.0
    }

    private void Inventory_Equip3(PacketStream stream, Unit unit)
    {
        var items = new List<Item>();

        switch (unit)
        {
            case Character character:
                {
                    items = character.Inventory.Equipment.GetSlottedItemsList();
                    WriteEquip(stream, items);
                    var itemFlags = CalculateItemFlags(items);
                    stream.Write(itemFlags); // ItemFlags flags for 3.0.3.0
                    break;
                }
            case House house:
                {
                    items = house.Equipment.GetSlottedItemsList();
                    WriteEquip(stream, items);
                    break;
                }
            case Mate mate:
                {
                    items = mate.Equipment.GetSlottedItemsList();
                    WriteEquip(stream, items);
                    break;
                }
            case Slave slave:
                {
                    items = slave.Equipment.GetSlottedItemsList();
                    WriteEquip(stream, items);
                    break;
                }
            case Npc npc:
                {
                    items = npc.Equipment.GetSlottedItemsList();
                    var validFlags = CalculateValidFlags(items);
                    stream.Write((uint)validFlags);

                    if (validFlags <= 0)
                    {
                        unit.ModelParams.SetType(UnitCustomModelType.Skin); // дополнительная проверка, что у NPC нет тела и лица
                        return;
                    }

                    for (var i = 0; i < items.Count; i++)
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
                    }
                    break;
                }
            // for transfer and Shipyard
            default:
                {
                    stream.Write(0u); // validFlags for 3.0.3.0
                    break;
                }
        }
    }

    private static void WriteEquip(PacketStream stream, List<Item> items)
    {
        var validFlags = CalculateValidFlags(items);
        stream.Write((uint)validFlags); // validFlags for 3.0.3.0
        WriteItems(stream, items);
    }

    private static void WriteItems(PacketStream stream, List<Item> items)
    {
        foreach (var item in items)
        {
            if (item != null)
            {
                stream.Write(item);
            }
        }
    }

    private static int CalculateValidFlags(List<Item> items)
    {
        var validFlags = 0;
        var index = 0;
        foreach (var item in items)
        {
            if (item != null)
            {
                validFlags |= 1 << index;
            }

            index++;
        }

        return validFlags;
    }

    private static int CalculateItemFlags(List<Item> items)
    {
        var itemFlags = 0;
        var index = 0;

        foreach (var tmp in items
                     .Where(item => item != null)
                     .Select(item => (int)item.ItemFlags << index))
        {
            ++index;
            itemFlags |= tmp;
        }

        return itemFlags;
    }

    #endregion CharacterInfo_3EB0

    public override string Verbose()
    {
        return " - " + _baseUnitType + " - " + _unit?.DebugName();
    }
}
