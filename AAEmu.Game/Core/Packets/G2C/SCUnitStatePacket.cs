using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Butler;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
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

        public SCUnitStatePacket(Unit unit) : base(SCOffsets.SCUnitStatePacket, 5)
        {
            _unit = unit;

            switch (_unit)
            {
                case Character _:
                    _baseUnitType = BaseUnitType.Character;
                    _modelPostureType = ModelPostureType.None;
                    break;
                case Npc npc:
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
                case Butler _: // добавился еще один ТИП = 7 add in 7.0.3.9
                    _baseUnitType = BaseUnitType.Butler;
                    _modelPostureType = ModelPostureType.None;
                    break;
            }
        }

        public override PacketStream Write(PacketStream stream)
        {
            #region read_NetUnit

            stream.WriteBc(_unit.ObjId); // objId
            stream.Write(_unit.Name);    // name
            if (_unit is Character chw)
            {
                stream.Write((byte)chw.Transform.WorldId); // worldId
            }
            else
            {
                stream.Write((byte)255); // for Npc
            }

            #region UnitOwnerInfo
            stream.Write((byte)_baseUnitType);
            switch (_baseUnitType) // UnitOwnerInfo?
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
                    stream.Write((byte)0);          // masterWorldId add in 7.0.3.9
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
                case BaseUnitType.Butler: // added in 7.0.3.9
                    var butler = (Butler)_unit;
                    stream.Write(butler.Template.Id); // type(id)
                    stream.Write((ushort)0); // tl added in 7.0.3.9
                    break;
            }
            #endregion

            if (_unit.OwnerId > 0) // master
            {
                var name = NameManager.Instance.GetCharacterName(_unit.OwnerId);
                stream.Write(name ?? "");
            }
            else
            {
                stream.Write("");
            }

            #region Position
            stream.WritePosition(_unit.Transform.Local.Position);
            #endregion

            stream.Write(_unit.Scale);     // scale
            stream.Write(_unit.Level);     // level
            stream.Write(_unit.HierLevel); // hierLevel add in 3.5.0.3 NA

            #region Level_hierLevel
            stream.Write((byte)0); // level
            stream.Write((byte)0); // hierLevel
            #endregion

            #region Slot
            for (var i = 0; i < 4; i++)
            {
                stream.Write((sbyte)-1); // slot
            }
            #endregion

            stream.Write(_unit.ModelId); // modelRef

            #region CharacterInfo
            Inventory_Equip(stream, _unit, _baseUnitType); // Equip character
            #endregion

            #region CustomModel
            stream.Write(_unit.ModelParams);
            #endregion

            stream.WriteBc(0);
            stream.Write((long)_unit.Hp * 100); // preciseHealth int64 in 7+
            stream.Write((long)_unit.Mp * 100); // preciseMana int64 in 7+

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

            #region ModelPosture

            // TODO UnitModelPosture
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

            stream.Write(false);             // isLooted

            switch (_modelPostureType)
            {
                case ModelPostureType.HouseState: // build
                    stream.Write(false); // flags Byte
                    break;
                case ModelPostureType.ActorModelState: // npc
                    var npc = _unit as Npc;
                    stream.Write(npc.Template.AnimActionId); // animId
                    stream.Write(true);                     // activate
                    break;
                case ModelPostureType.FarmfieldState:
                    stream.Write(0u);    // type(id)
                    stream.Write(0f);    // growRate
                    stream.Write(0);     // randomSeed
                    stream.Write(false); // flags Byte
                    break;
                case ModelPostureType.TurretState: // slave
                    stream.Write(0f);    // pitch
                    stream.Write(0f);    // yaw
                    break;
                default:
                    break;
            }
            #endregion

            stream.Write(_unit.ActiveWeapon);

            switch (_unit)
            {
                case Character character:
                    {
                        stream.Write((byte)character.Skills.Skills.Count);       // learnedSkillCount
                        if (character.Skills.Skills.Count >= 0)
                        {
                            _log.Trace("Warning! character.learnedSkillCount = {0}", character.Skills.Skills.Count);
                        }
                        stream.Write((byte)character.Skills.PassiveBuffs.Count); // passiveBuffCount
                        if (character.Skills.Skills.Count >= 0)
                        {
                            _log.Trace("Warning! character.passiveBuffCount = {0}", character.Skills.PassiveBuffs.Count);
                        }
                        //stream.Write(character.HighAbilityRsc); // highAbilityRsc нет в 7.0.3.9
                        stream.Write(0u);        // type
                        stream.Write(0);         // appellationStampId
                        stream.Write(0u);        // vechicleDyeing
                        stream.Write(false);     // isTempFaction added in 7.0.3.9

                        foreach (var skill in character.Skills.Skills.Values) // learnedSkillCount
                        {
                            stream.WritePisc(skill.Id);    // skillId
                        }
                        foreach (var buff in character.Skills.PassiveBuffs.Values) // passiveBuffCount
                        {
                            stream.WritePisc(buff.Id);
                        }
                        break;
                    }
                case Npc npc:
                    var learnedSkillCount = npc.Template.Skills.Values.Sum(skills => skills.Count);
                    learnedSkillCount++; // add BaseSkillId
                    stream.Write((byte)learnedSkillCount); // learnedSkillCount
                    if (learnedSkillCount >= 0)
                    {
                        _log.Warn("Warning! npc.learnedSkillCount = {0}", learnedSkillCount);
                    }

                    var passiveBuffCount = npc.Template.PassiveBuffs.Count;
                    stream.Write((byte)npc.Template.PassiveBuffs.Count); // passiveBuffCount
                    if (passiveBuffCount >= 0)
                    {
                        _log.Warn("Warning! npc.passiveBuffCount = {0}", passiveBuffCount);
                    }
                    //stream.Write(npc.HighAbilityRsc); // highAbilityRsc нет в 7.0.3.9
                    stream.Write(0u);        // type
                    stream.Write(0);         // appellationStampId
                    stream.Write(0u);        // vechicleDyeing
                    stream.Write(false);     // isTempFaction added in 7.0.3.9
                    var meleeAttackWithOtherSkill = new List<NpcSkill>(); // BaseSkillId for all NPC
                    meleeAttackWithOtherSkill.Add(new NpcSkill { SkillId = (uint)npc.Template.BaseSkillId });
                    if (learnedSkillCount == 1)
                    {
                        stream.WritePisc(npc.Template.BaseSkillId); // BaseSkillId for all NPC
                    }
                    else
                    {
                        var skills = new List<NpcSkill>();
                        skills.AddRange(meleeAttackWithOtherSkill);
                        foreach (var lns in npc.Template.Skills.Values)
                        {
                            skills.AddRange(lns);
                        }
                        var hcount = skills.Count;
                        var indx = 0;
                        var pcount = 4;
                        do
                        {
                            if (hcount < 4)
                                pcount = hcount;

                            switch (pcount)
                            {
                                case 1:
                                    stream.WritePisc(skills[indx].SkillId);
                                    indx += 1;
                                    break;
                                case 2:
                                    stream.WritePisc(skills[indx].SkillId, skills[indx + 1].SkillId);
                                    indx += 2;
                                    break;
                                case 3:
                                    stream.WritePisc(skills[indx].SkillId, skills[indx + 1].SkillId,
                                        skills[indx + 2].SkillId);
                                    indx += 3;
                                    break;
                                case 4:
                                    stream.WritePisc(skills[indx].SkillId, skills[indx + 1].SkillId,
                                        skills[indx + 2].SkillId, skills[indx + 3].SkillId);
                                    indx += 4;
                                    break;
                            }

                            hcount -= pcount;
                        } while (hcount > 0);
                        //}
                    }
                    var hcount2 = passiveBuffCount;
                    var index2 = 0;
                    var pcount2 = 4;
                    do
                    {
                        if (hcount2 < 4)
                            pcount2 = hcount2;

                        switch (pcount2)
                        {
                            case 1:
                                stream.WritePisc(npc.Template.PassiveBuffs[index2].PassiveBuffId);
                                index2 += 1;
                                break;
                            case 2:
                                stream.WritePisc(npc.Template.PassiveBuffs[index2].PassiveBuffId, npc.Template.PassiveBuffs[index2 + 1].PassiveBuffId);
                                index2 += 2;
                                break;
                            case 3:
                                stream.WritePisc(npc.Template.PassiveBuffs[index2].PassiveBuffId, npc.Template.PassiveBuffs[index2 + 1].PassiveBuffId, npc.Template.PassiveBuffs[index2 + 2].PassiveBuffId);
                                index2 += 3;
                                break;
                            case 4:
                                stream.WritePisc(npc.Template.PassiveBuffs[index2].PassiveBuffId, npc.Template.PassiveBuffs[index2 + 1].PassiveBuffId, npc.Template.PassiveBuffs[index2 + 2].PassiveBuffId, npc.Template.PassiveBuffs[index2 + 3].PassiveBuffId);
                                index2 += 4;
                                break;
                        }
                        hcount2 -= pcount2;
                    } while (hcount2 > 0);
                    break;
                default:
                    stream.Write((byte)0); // learnedSkillCount
                    stream.Write((byte)0); // passiveBuffCount
                    //stream.Write(0);     // highAbilityRsc нет в 7+
                    stream.Write(0u);      // type
                    stream.Write(0);       // appellationStampId
                    stream.Write(0u);      // vechicleDyeing
                    stream.Write(false);   // isTempFaction added in 7.0.3.9
                    break;
            }

            if (_baseUnitType == BaseUnitType.Housing)
            {
                stream.Write(_unit.Transform.World.Rotation.Z); // должно быть float
            }
            else
            {
                var (roll, pitch, yaw) = _unit.Transform.Local.ToRollPitchYawSBytes();
                stream.Write(roll);
                stream.Write(pitch);
                stream.Write(yaw);
            }

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

            if (_unit is Character character5)
            {
                stream.WritePisc(0, 0, character5.Appellations.ActiveAppellation, 0);      // pisc 4
                //stream.WritePisc(_unit.Faction?.Id ?? 0, _unit.Expedition?.Id ?? 0, 0, 0); // pisc 4
                stream.WritePisc(_unit.Faction?.Id ?? 0, _unit.Expedition?.Id ?? 0, 0); // pisc 3 in 7+
                stream.WritePisc(0, 0, 0, 0); // pisc 4
            }
            else
            {
                stream.WritePisc(0, 0, 0, 0); // pisc 4
                //stream.WritePisc(_unit.Faction?.Id ?? 0, _unit.Expedition?.Id ?? 0, 0, 0); // pisc 4
                stream.WritePisc(_unit.Faction?.Id ?? 0, _unit.Expedition?.Id ?? 0, 0); // pisc 3 in 7+
                stream.WritePisc(0, 0, 0, 0); // pisc 4
            }

            switch (_unit)
            {
                case Character character6:
                    {
                        var flags = new BitSet(16); // short

                        if (character6.Invisible)
                        {
                            flags.Set(5);
                        }

                        if (character6.IdleStatus)
                        {
                            flags.Set(13);
                        }

                        //stream.WritePisc(0, 0); // очки чести полученные в PvP, кол-во убийств в PvP
                        stream.Write(flags.ToByteArray()); // flags(ushort)
                        stream.Write((byte)0); // attckFactionFlags
                        /*
                          * 0x01 - 8bit - режим боя
                          * 0x04 - 6bit - невидимость?
                          * 0x08 - 5bit - дуэль
                          * 0x40 - 2bit - gmmode, дополнительно 7 байт, 8 байт в 7+
                          * 0x80 - 1bit - дополнительно tl(ushort), tl(ushort), tl(ushort), tl(ushort)
                          * 0x0100 - 16bit - дополнительно 3 байт (bc), firstHitterTeamId(uint)
                          * 0x0400 - 14bit - надпись "Отсутсвует" под именем
                         */
                        break;
                    }

                case Npc _:
                    stream.Write((ushort)8192); // flags
                    stream.Write((byte)0);      // attckFactionFlags
                    break;
                default:
                    stream.Write((ushort)0); // flags
                    stream.Write((byte)0);   // attckFactionFlags
                    break;
            }
            if (_unit is Character character7)
            {
                #region read_Exp_Order
                var activeAbilities = character7.Abilities.GetActiveAbilities();
                foreach (var ability in character7.Abilities.Values)
                {
                    stream.Write(ability.Exp);
                    stream.Write(ability.Order);
                }

                stream.Write((byte)activeAbilities.Count); // nActive
                foreach (var ability in activeAbilities)
                {
                    stream.Write((byte)ability);
                }

                #endregion read_Exp_Order

                stream.WriteBc(0);     // objId
                stream.Write((byte)255); // duelTeamType added in 7+
                stream.Write((byte)0); // camp

                #region read_Stp
                //for (var i = 0; i < 6; i++)
                //{
                //    stream.Write(0); // stp
                //}
                stream.Write((byte)30);  // stp
                stream.Write((byte)60);  // stp
                stream.Write((byte)50);  // stp
                stream.Write((byte)0);   // stp
                stream.Write((byte)40);  // stp
                stream.Write((byte)100); // stp

                stream.Write((byte)7); // flags
                character7.VisualOptions.Write(stream, 0x20); // cosplay_visual

                stream.Write(1); // premium
                #endregion read_Stp

                #region read_Stats_0
                var size = 1u;
                stream.Write(size); // size
                for (var i = 0; i < size; i++)
                {
                    for (var j = 0; j < 5; j++)
                    {
                        stream.Write(0); // stats
                    }
                    stream.Write(0); // applyNormalCount
                    stream.Write(0); // applySpecialCount
                }
                #endregion read_Stats_0

                stream.Write(0); // selectPageIndex
                stream.Write(0); // extendMaxStats
                stream.Write(0); // applyExtendCount
                stream.Write(0); // equipSlotReinforces
                stream.Write(0); // slotInfoList
                stream.Write(0); // levelEffectList

                stream.WritePisc(0, 0, 0, 0);
                stream.WritePisc(0, 0);
                stream.Write((byte)0); // accountPrivilege byte
            }
            #endregion

            #region NetBuff

            var goodBuffs = new List<Buff>();
            var badBuffs = new List<Buff>();
            var hiddenBuffs = new List<Buff>();
            stream.Write((byte)0); // goodBuffs
            stream.Write((byte)0); // badBuffs
            stream.Write((byte)0); // hiddenBuffs

            //// TODO: Fix the patron and auction house license buff issue
            //if (_unit is Character)
            //{
            //    if (!_unit.Buffs.CheckBuff(8000011)) //TODO Wrong place
            //    {
            //        _unit.Buffs.AddBuff(new Buff(_unit, _unit, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(8000011), null, System.DateTime.UtcNow));
            //    }

            //    if (!_unit.Buffs.CheckBuff(8000012)) //TODO Wrong place
            //    {
            //        _unit.Buffs.AddBuff(new Buff(_unit, _unit, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(8000012), null, System.DateTime.UtcNow));
            //    }
            //}
            /*
            _unit.Buffs.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs);

            stream.Write((byte)goodBuffs.Count); // TODO max 32
            foreach (var effect in goodBuffs)
            {
                WriteBuff(stream, effect);
            }

            stream.Write((byte)badBuffs.Count); // TODO max 24 for 1.2, 20 for 3.0.3.0
            foreach (var effect in badBuffs)
            {
                WriteBuff(stream, effect);
            }

            stream.Write((byte)hiddenBuffs.Count); // TODO max 24 for 1.2, 28 for 3.0.3.0
            foreach (var effect in hiddenBuffs)
            {
                WriteBuff(stream, effect);
            }
            */
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
                case BaseUnitType.Butler:
                    unit = (Butler)_unit;
                    break;
                default:
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

            stream.Write((uint)validFlags); // validFlags for 3.0.3.0

            if (validFlags <= 0)
            {
                unit.ModelParams.SetType(UnitCustomModelType.Skin); // дополнительная проверка, что у NPC нет тела и лица
                return;
            }

            index = 0;
            var v19 = 0;
            do
            {
                if (((validFlags >> index) & 1) != 0)
                {
                    var item = unit.Equipment.GetItemBySlot(index);
                    if (index - 19 > 6 || baseUnitType == BaseUnitType.Slave) // [19...26, 28] OR Slave
                    {
                        if (index != 27 && index != 31 || baseUnitType != BaseUnitType.Npc)  // not CosPlay && not CosplayLooks || not Npc
                        {
                            switch (baseUnitType)
                            {
                                case BaseUnitType.Character:
                                case BaseUnitType.Housing:
                                case BaseUnitType.Mate:
                                case BaseUnitType.Slave:
                                case BaseUnitType.Butler:
                                    stream.Write(item);
                                    break;
                                case BaseUnitType.Npc: // Npc
                                    stream.Write(item.TemplateId);
                                    stream.Write(item.Id);
                                    stream.Write(item.Grade);
                                    break;
                                case BaseUnitType.Transfer:
                                case BaseUnitType.Shipyard:
                                    break;
                            }
                        }
                        else
                        {
                            stream.Write(item); // Cosplay [27]
                        }
                    }
                    else
                    {
                        if (index >= 19 && index <= 26) // || index >= 28 && index <= 30)
                        {
                            stream.Write(item.TemplateId); // somehow_special [19..26]
                        }
                        else
                        {
                            switch (baseUnitType)
                            {
                                case BaseUnitType.Character:
                                case BaseUnitType.Housing:
                                case BaseUnitType.Mate:
                                case BaseUnitType.Slave:
                                case BaseUnitType.Butler:
                                    stream.Write(item);
                                    break;
                                case BaseUnitType.Npc: // Npc
                                    stream.Write(item.TemplateId);
                                    stream.Write(item.Id);
                                    stream.Write(item.Grade);
                                    break;
                                case BaseUnitType.Transfer:
                                case BaseUnitType.Shipyard:
                                    break;
                            }
                        }
                        index = v19;
                    }
                }

                ++index;
                v19 = index;
            } while (index < 32);

            if (baseUnitType != BaseUnitType.Character) { return; }

            index = 0;
            validFlags = 0;

            foreach (var item in items)
            {
                if (item == null) { continue; }

                var tmp = (int)item.ItemFlags << index;
                ++index;
                validFlags |= tmp;
            }
            stream.Write(validFlags); //  ItemFlags flags for 3.0.3.0
        }

        #endregion Inventory_Equip

        #region NetBuff
        private void WriteBuff(PacketStream stream, Buff effect)
        {
            stream.Write(effect.Index);        // Id
            stream.Write(effect.SkillCaster);  // skillCaster
            stream.Write(0);                   // type(id)
            stream.Write(effect.Caster.Level); // sourceLevel
            stream.Write(effect.AbLevel);      // sourceAbLevel
            stream.WritePisc(0, effect.GetTimeElapsed(), 0, 0u); // add in 3.0.3.0
            stream.WritePisc(effect.Template.BuffId, 1, 0, 0u);  // add in 3.0.3.0
        }
        #endregion NetBuff

        public override string Verbose()
        {
            return " - " + _baseUnitType.ToString() + " - " + _unit?.DebugName();
        }
    }
}
