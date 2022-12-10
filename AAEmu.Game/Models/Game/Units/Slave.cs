using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using Jitter.Dynamics;

namespace AAEmu.Game.Models.Game.Units
{
    public class Slave : Unit
    {
        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Slave;
        //public uint Id { get; set; } // moved to BaseUnit
        //public uint TemplateId { get; set; } // moved to BaseUnit
        public uint BondingObjId { get; set; } = 0;
        
        public SlaveTemplate Template { get; set; }
        // public Character Driver { get; set; }
        public Character Summoner { get; set; }
        public List<Doodad> AttachedDoodads { get; set; }
        public List<Slave> AttachedSlaves { get; set; }
        public Dictionary<AttachPointKind, Character> AttachedCharacters { get; set; }
        public DateTime SpawnTime { get; set; }
        public sbyte ThrottleRequest { get; set; }
        public sbyte Throttle { get; set; }
        public float Speed { get; set; }
        public sbyte SteeringRequest { get; set; }
        public sbyte Steering { get; set; }
        public float RotSpeed { get; set; }
        public short RotationZ { get; set; }
        public float RotationDegrees { get; set; }
        public sbyte AttachPointId { get; set; } = -1;
        public uint OwnerObjId { get; set; }
        public RigidBody RigidBody { get; set; }
        public SlaveSpawner Spawner { get; set; }

        public Slave()
        {
            AttachedDoodads = new List<Doodad>();
            AttachedSlaves = new List<Slave>();
            AttachedCharacters = new Dictionary<AttachPointKind, Character>();
        }

        #region Attributes
        [UnitAttribute(UnitAttribute.Str)]
        public int Str
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Str);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                var res = (int)formula.Evaluate(parameters);
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
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Dex);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                var res = (int)formula.Evaluate(parameters);
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
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Sta);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                var res = (int)formula.Evaluate(parameters);
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
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Int);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                var res = (int)formula.Evaluate(parameters);
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
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Spi);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                var res = (int)formula.Evaluate(parameters);
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
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Fai);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
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
                // These don't seem to match what the client expects, must be missing something
                // Example: (level * 70 + sta * 12)
                // Should be 9216 Hp, but we only have 4796 (at 108 base stamina for Lv50)
                // For example a clipper would be correct is we added another 368.33 (= +341%) stamina boost
                // TODO: for now just put a static 250k HP so spawned slaves don't show damaged
                return 250000;
                /*
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.MaxHealth);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MaxHealth))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
                */
            }
        }

        [UnitAttribute(UnitAttribute.HealthRegen)]
        public override int HpRegen
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.HealthRegen);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.PersistentHealthRegen);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                var res = (int)formula.Evaluate(parameters);
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
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.MaxMana);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.ManaRegen);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
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
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.PersistentManaRegen);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                var res = (int)formula.Evaluate(parameters);
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

        public override float LevelDps
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.LevelDps);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                parameters["ab_level"] = 0;
                var res = formula.Evaluate(parameters);
                return (float)res;
            }
        }

        [UnitAttribute(UnitAttribute.MainhandDps)]
        public override int Dps
        {
            get
            {
                // TODO: This probably needs to change
                var weapon = (Weapon)Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
                var res = weapon?.Dps ?? 0;
                res += Str / 10f;
                foreach (var bonus in GetBonuses(UnitAttribute.MainhandDps))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)(res * 1000);
            }
        }

        [UnitAttribute(UnitAttribute.MeleeDpsInc)]
        public override int DpsInc
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.MeleeDpsInc);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                var res = formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MeleeDpsInc))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)res;
            }
        }

        [UnitAttribute(UnitAttribute.OffhandDps)]
        public override int OffhandDps
        {
            get
            {
                var weapon = (Weapon)Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);
                var res = weapon?.Dps ?? 0;
                res += Str / 10f;
                foreach (var bonus in GetBonuses(UnitAttribute.OffhandDps))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)(res * 1000);
            }
        }

        [UnitAttribute(UnitAttribute.RangedDps)]
        public override int RangedDps
        {
            get
            {
                var weapon = (Weapon)Equipment.GetItemBySlot((int)EquipmentItemSlot.Ranged);
                var res = weapon?.Dps ?? 0;
                res += Dex / 10f;
                foreach (var bonus in GetBonuses(UnitAttribute.RangedDps))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)(res * 1000);
            }
        }

        [UnitAttribute(UnitAttribute.RangedDpsInc)]
        public override int RangedDpsInc
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.RangedDpsInc);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                var res = formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.RangedDpsInc))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)res;
            }
        }

        [UnitAttribute(UnitAttribute.SpellDps)]
        public override int MDps
        {
            get
            {
                var weapon = (Weapon)Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
                var res = weapon?.MDps ?? 0;
                res += Int / 10f;
                foreach (var bonus in GetBonuses(UnitAttribute.SpellDps))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)(res * 1000);
            }
        }

        [UnitAttribute(UnitAttribute.SpellDpsInc)]
        public override int MDpsInc
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.SpellDpsInc);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                var res = formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.SpellDpsInc))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return (int)res;
            }
        }

        [UnitAttribute(UnitAttribute.Armor)]
        public override int Armor
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Armor);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Armor))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }

        [UnitAttribute(UnitAttribute.MagicResist)]
        public override int MagicResistance
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.MagicResist);
                var parameters = new Dictionary<string, double>();
                parameters["level"] = Level;
                parameters["str"] = Str;
                parameters["dex"] = Dex;
                parameters["sta"] = Sta;
                parameters["int"] = Int;
                parameters["spi"] = Spi;
                parameters["fai"] = Fai;
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MagicResist))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }
                return res;
            }
        }
        
        [UnitAttribute(UnitAttribute.TurnSpeed)]
        public virtual float TurnSpeed { get => (float)CalculateWithBonuses(0, UnitAttribute.TurnSpeed); }

        #endregion
        
        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
            character.SendPacket(new SCSlaveStatePacket(ObjId, TlId, Summoner.Name, Summoner.ObjId, Template.Id));
            
            base.AddVisibleObject(character);
        }

        public override void RemoveVisibleObject(Character character)
        {
            base.RemoveVisibleObject(character);

            character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));
        }

    }
}
