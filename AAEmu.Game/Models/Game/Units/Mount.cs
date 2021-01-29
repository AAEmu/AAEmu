using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units
{
    public sealed class Mount : Unit
    {
        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Mate;
        //public ushort TlId { get; set; }
        public uint TemplateId { get; set; }
        public NpcTemplate Template { get; set; }

        public uint OwnerObjId { get; set; }
        public uint Att1 { get; set; }
        public byte Reason1 { get; set; }
        public uint Att2 { get; set; }
        public byte Reason2 { get; set; }

        public override float Scale => Template.Scale;

        // SpawnMate
        public uint Id { get; set; }
        public ulong ItemId { get; set; }
        public byte UserState { get; set; }
        public int Exp { get; set; }
        public int Mileage { get; set; }
        public uint SpawnDelayTime { get; set; }
        public List<uint> Skills { get; set; }

        #region Attributes

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

        public override int MaxHp
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MaxHealth);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MaxHealth))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                        res += (int)(res * bonus.Value / 100f);
                    else
                        res += bonus.Value;
                }

                return res;
            }
        }

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
                    ["fai"] = Fai
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
                    ["fai"] = Fai
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
                    ["fai"] = Fai
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
                    ["fai"] = Fai
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
                    ["fai"] = Fai
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

        #endregion

        public Mount()
        {
            ModelParams = new UnitCustomModelParams();
            Skills = new List<uint>();
            Att1 = 0u;
            Reason1 = 0;
            Att2 = 0u;
            Reason2 = 0;
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCMateStatePacket(ObjId));
            character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));

            if (Att1 > 0)
            {
                var owner = WorldManager.Instance.GetCharacterByObjId(Att1);
                if (owner != null)
                    character.SendPacket(new SCUnitAttachedPacket(owner.ObjId, 1, Reason1, ObjId));
            }
            if (Att2 > 0)
            {
                var passenger = WorldManager.Instance.GetCharacterByObjId(Att2);
                if (passenger != null)
                    character.SendPacket(new SCUnitAttachedPacket(passenger.ObjId, 2, Reason2, ObjId));
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

        public override void SetPosition(float x, float y, float z)
        {
            base.SetPosition(x, y, z);
            UpdatePassengers();
        }

        public override void SetPosition(float x, float y, float z, sbyte rotationX, sbyte rotationY, sbyte rotationZ)
        {
            base.SetPosition(x, y, z, rotationX, rotationY, rotationZ);
            UpdatePassengers();
        }

        public override void SetPosition(Point pos)
        {
            base.SetPosition(pos);
            UpdatePassengers();
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

        private void UpdatePassengers()
        {
            foreach (var passenger in GetPassengers())
            {
                passenger.SetPosition(Position);
            }
        }

        public List<Character> GetPassengers()
        {
            var passengers = new List<Character>();
            var owner = WorldManager.Instance.GetCharacterByObjId(Att1);
            var passenger = WorldManager.Instance.GetCharacterByObjId(Att2);

            if (owner != null) passengers.Add(owner);
            if (passenger != null) passengers.Add(passenger);

            return passengers;
        }
    }
}
