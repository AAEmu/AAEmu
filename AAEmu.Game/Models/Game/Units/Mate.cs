﻿using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Game.Units
{
    public class MatePassengerInfo
    {
        public uint _objId;
        public byte _reason;
    }
    
    public sealed class Mate : Unit
    {
        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Mate;
        //public ushort TlId { get; set; }
        public uint TemplateId { get; set; }
        public NpcTemplate Template { get; set; }

        public uint OwnerObjId { get; set; }
        public Dictionary<byte,MatePassengerInfo> Passengers { get; }

        public override float Scale => Template.Scale;

        // SpawnMate
        public uint Id { get; set; }
        public ulong ItemId { get; set; }
        public byte UserState { get; set; }
        public int Exp { get; set; }
        public int Mileage { get; set; }
        public uint SpawnDelayTime { get; set; }
        public List<uint> Skills { get; set; }
        public MateDb DbInfo { get; set; }

        #region Attributes

        [UnitAttribute(UnitAttribute.Str)]
        public int Str
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Str);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var result = formula.Evaluate(parameters);
                var res = (int)result;
                //foreach (var item in Inventory.Equip)
                //    if (item is EquipItem equip)
                //        res += equip.Str;
                foreach (var bonus in GetBonuses(UnitAttribute.Str))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        [UnitAttribute(UnitAttribute.Dex)]
        public int Dex
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Dex);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                //foreach (var item in Inventory.Equip)
                //    if (item is EquipItem equip)
                //        res += equip.Dex;
                foreach (var bonus in GetBonuses(UnitAttribute.Dex))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        [UnitAttribute(UnitAttribute.Sta)]
        public int Sta
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Sta);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                //foreach (var item in Inventory.Equip)
                //    if (item is EquipItem equip)
                //        res += equip.Sta;
                foreach (var bonus in GetBonuses(UnitAttribute.Sta))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        [UnitAttribute(UnitAttribute.Int)]
        public int Int
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Int);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                //foreach (var item in Inventory.Equip)
                //    if (item is EquipItem equip)
                //        res += equip.Int;
                foreach (var bonus in GetBonuses(UnitAttribute.Int))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        [UnitAttribute(UnitAttribute.Spi)]
        public int Spi
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Spi);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                //foreach (var item in Inventory.Equip)
                //    if (item is EquipItem equip)
                //        res += equip.Spi;
                foreach (var bonus in GetBonuses(UnitAttribute.Spi))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        [UnitAttribute(UnitAttribute.Fai)]
        public int Fai
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Fai);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Fai))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        [UnitAttribute(UnitAttribute.MaxHealth)]
        public override int MaxHp
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MaxHealth);
                var mateKindVariable = FormulaManager.Instance.GetUnitVariable(formula.Id,
                    UnitFormulaVariableType.MateKind, (uint)Template.MateKindId);

                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai,
                    ["mate_kind"] = mateKindVariable
                };
                var res = (int)formula.Evaluate(parameters);

                res = (int)CalculateWithBonuses(res, UnitAttribute.MaxHealth);

                return res;
            }
        }

        [UnitAttribute(UnitAttribute.HealthRegen)]
        public override int HpRegen
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.HealthRegen);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai,
                    ["mate_kind"] = Template.MateKindId
                };
                var res = (int)formula.Evaluate(parameters);
                res += Spi / 10;
                foreach (var bonus in GetBonuses(UnitAttribute.HealthRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        [UnitAttribute(UnitAttribute.PersistentHealthRegen)]
        public override int PersistentHpRegen
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.PersistentHealthRegen);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai,
                    ["mate_kind"] = Template.MateKindId
                };
                var res = (int)formula.Evaluate(parameters);
                res /= 5; // TODO ...
                foreach (var bonus in GetBonuses(UnitAttribute.PersistentHealthRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        [UnitAttribute(UnitAttribute.MaxMana)]
        public override int MaxMp
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MaxMana);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai,
                    ["mate_kind"] = Template.MateKindId
                };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MaxMana))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        [UnitAttribute(UnitAttribute.ManaRegen)]
        public override int MpRegen
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.ManaRegen);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai,
                    ["mate_kind"] = Template.MateKindId
                };
                var res = (int)formula.Evaluate(parameters);
                res += Spi / 10;
                foreach (var bonus in GetBonuses(UnitAttribute.ManaRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        [UnitAttribute(UnitAttribute.PersistentManaRegen)]
        public override int PersistentMpRegen
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.PersistentManaRegen);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai,
                    ["mate_kind"] = Template.MateKindId
                };
                var res = (int)formula.Evaluate(parameters);
                res /= 5; // TODO ...
                foreach (var bonus in GetBonuses(UnitAttribute.PersistentManaRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

        // [UnitAttribute(UnitAttribute.Dps)]
        public override float LevelDps
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.LevelDps);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai,
                    ["mate_kind"] = Template.MateKindId
                };

                var res = formula.Evaluate(parameters);
                return (float)res;
            }
        }

        #endregion

        public Mate()
        {
            ModelParams = new UnitCustomModelParams();
            Skills = new List<uint>();
            Passengers = new Dictionary<byte, MatePassengerInfo>();

            // TODO: Spawn this with the correct amount of seats depending on the template
            // 2 seats by default
            Passengers.Add(1,new MatePassengerInfo() { _objId = 0 , _reason = 0 });
            Passengers.Add(2,new MatePassengerInfo() { _objId = 0 , _reason = 0 });
        }

        public void AddExp(int exp)
        {
            var expMultiplier = 1d;
            if (exp == 0)
                return;
            if (float.TryParse(ConfigurationManager.Instance.GetConfiguration("ExperienceMultiplierInPercent"), out var xpm))
                expMultiplier = xpm / 100f;
            var totalExp = Math.Round(expMultiplier * exp);
            exp = (int)totalExp;
            Exp += exp;
            SendPacket(new SCExpChangedPacket(ObjId, exp, false));
            CheckLevelUp();
        }

        public void CheckLevelUp()
        {
            var needExp = ExpirienceManager.Instance.GetExpForLevel((byte)(Level + 1));
            var change = false;
            while (Exp >= needExp)
            {
                change = true;
                Level++;
                needExp = ExpirienceManager.Instance.GetExpForLevel((byte)(Level + 1));
            }

            if (change)
            {
                BroadcastPacket(new SCLevelChangedPacket(ObjId, Level), true);
                StartRegen();
            }

            DbInfo.Xp = Exp;
            DbInfo.Level = Level;
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCMateStatePacket(ObjId));
            character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
            foreach (var ati in Passengers)
            {
                if (ati.Value._objId > 0)
                {
                    var player = WorldManager.Instance.GetCharacterByObjId(ati.Value._objId);
                    if (player != null)
                        character.SendPacket(new SCUnitAttachedPacket(player.ObjId, ati.Key, ati.Value._reason, ObjId));
                }
            }
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }

            character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            foreach (var character in WorldManager.Instance.GetAround<Character>(this))
            {
                if (OwnerObjId == character.ObjId && self)
                    character.SendPacket(packet);
                else if (OwnerObjId != character.ObjId)
                    character.SendPacket(packet);
            }
        }

        public override int DoFallDamage(ushort fallVel)
        {
            var fallDmg = base.DoFallDamage(fallVel);
            if (Hp <= 0)
            {
                var riders = Passengers.ToList();
                // When fall damage kills a mount, also kill all of it's riders
                for (var i = riders.Count - 1; i >= 0; i--)
                {
                    var pos = (byte)riders[i].Key;
                    var rider = WorldManager.Instance.GetCharacterByObjId(riders[i].Value._objId);
                    if (rider != null)
                    {
                        rider.DoFallDamage(fallVel);
                        if (rider.Hp <= 0)
                            MateManager.Instance.UnMountMate(rider, TlId, pos, UnitDetachReason.Death);
                    }
                }
            }

            return fallDmg;
        }
    }
}
