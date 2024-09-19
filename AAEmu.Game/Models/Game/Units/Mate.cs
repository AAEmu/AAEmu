using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Tasks.Mate;

namespace AAEmu.Game.Models.Game.Units;

public class MatePassengerInfo
{
    public uint ObjId;
    public AttachUnitReason Reason;
}

public sealed class Mate : Unit
{
    public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Mate;
    public override BaseUnitType BaseUnitType => BaseUnitType.Mate;
    public NpcTemplate Template { get; set; }
    public uint OwnerObjId { get; set; }
    public Dictionary<AttachPointKind, MatePassengerInfo> Passengers { get; }
    public override float Scale => Template.Scale;
    /// <summary>
    /// The item that this summon is from
    /// </summary>
    public ulong ItemId { get; set; }
    public byte UserState { get; set; }
    public int Experience { get; set; }
    public int Mileage { get; set; }
    public uint SpawnDelayTime { get; set; }
    public List<uint> Skills { get; set; }
    public List<uint> Tags { get; set; }
    public List<uint> Charges { get; set; }
    public MateDb DbInfo { get; set; }
    public Task MateXpUpdateTask { get; set; }
    public MateType MateType { get; set; }  // added in 3+

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
                ["ab_level"] = Level
            };

            var res = formula.Evaluate(parameters);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeDpsInc)]
    public override int DpsInc
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MeleeDpsInc);
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

    [UnitAttribute(UnitAttribute.SpellDpsInc)]
    public override int MDpsInc
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.SpellDpsInc);
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.Armor);
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
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Mate, UnitFormulaKind.MagicResist);
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
    #endregion

    public Mate()
    {
        ModelParams = new UnitCustomModelParams();
        Skills = new List<uint>();
        Tags = new List<uint>();
        Charges = new List<uint>();
        Passengers = new Dictionary<AttachPointKind, MatePassengerInfo>();
        Equipment = new MateEquipmentContainer(0, SlotType.EquipmentMate, false, this);

        // TODO: Spawn this with the correct amount of seats depending on the template
        // 2 seats by default
        Passengers.Add(AttachPointKind.Driver, new MatePassengerInfo { ObjId = 0, Reason = 0 });
        Passengers.Add(AttachPointKind.Passenger0, new MatePassengerInfo { ObjId = 0, Reason = 0 });
    }

    /// <summary>
    /// Update the Item Data if it was summoned by an item
    /// </summary>
    private void UpdateMateItemData()
    {
        if (ItemId > 0)
        {
            var item = ItemManager.Instance.GetItemByItemId(ItemId);
            if (item is SummonMate mateItem)
            {
                mateItem.DetailMateExp = Experience;
                mateItem.DetailLevel = Level;
                mateItem.IsDirty = true;
            }
        }
    }

    /// <summary>
    /// Adds exp to this Mate and checks for level ups
    /// </summary>
    /// <param name="exp"></param>
    public void AddExp(int exp)
    {
        if (exp == 0)
            return;
        if (exp > 0)
        {
            var totalExp = (int)Math.Round(AppConfiguration.Instance.World.ExpRate * exp);
            Experience += totalExp;
        }

        var owner = WorldManager.Instance.GetCharacterByObjId(OwnerObjId);
        owner.SendPacket(new SCExpChangedPacket(ObjId, exp, false));
        CheckLevelUp();
    }

    private void CheckLevelUp()
    {
        var needExp = ExperienceManager.Instance.GetExpForLevel((byte)(Level + 1), true);
        var leveledUp = false;
        while (Experience >= needExp)
        {
            leveledUp = true;
            Level++;
            needExp = ExperienceManager.Instance.GetExpForLevel((byte)(Level + 1), true);
        }

        UpdateMateItemData();
        DbInfo.Xp = Experience;
        DbInfo.Level = Level;

        if (leveledUp)
        {
            BroadcastPacket(new SCLevelChangedPacket(ObjId, Level), true);
            if (OwnerId > 0)
            {
                // Notify owner of the level up event
                var owner = WorldManager.Instance.GetCharacterByObjId(OwnerObjId);
                if (owner != null)
                    owner.Events.OnMateLevelUp(this, new OnMateLevelUpArgs());
            }
            //StartRegen();
        }
    }

    public override void AddVisibleObject(Character character)
    {
        base.AddVisibleObject(character);

        character.SendPacket(new SCUnitStatePacket(this));
        character.SendPacket(new SCMateStatusPacket(this));
        character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp, HighAbilityRsc));
        // TODO: Maybe let base handle this ?
        foreach (var ati in Passengers)
        {
            if (ati.Value.ObjId > 0)
            {
                var player = WorldManager.Instance.GetCharacterByObjId(ati.Value.ObjId);
                if (player != null)
                    character.SendPacket(new SCUnitAttachedPacket(player.ObjId, ati.Key, ati.Value.Reason, ObjId));
            }
        }
    }

    public override void RemoveVisibleObject(Character character)
    {
        base.RemoveVisibleObject(character);

        character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));
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
                var pos = riders[i].Key;
                var rider = WorldManager.Instance.GetCharacterByObjId(riders[i].Value.ObjId);
                if (rider != null)
                {
                    rider.DoFallDamage(fallVel);
                    if (rider.Hp <= 0)
                        MateManager.Instance.UnMountMate(rider.Connection, TlId, pos, AttachUnitReason.SlaveBinding);
                }
            }
        }

        return fallDmg;
    }

    public override void Regenerate()
    {
        if (!NeedsRegen)
        {
            return;
        }
        if (IsDead)
        {
            var riders = Passengers.ToList();
            for (var i = riders.Count - 1; i >= 0; i--)
            {
                var pos = riders[i].Key;
                var rider = WorldManager.Instance.GetCharacterByObjId(riders[i].Value.ObjId);
                if (rider != null)
                {
                    MateManager.Instance.UnMountMate(rider.Connection, TlId, pos, AttachUnitReason.None);
                }
            }
            return;
        }

        var oldHp = Hp;

        if (IsInBattle)
        {
            Hp += PersistentHpRegen;
            Mp += PersistentMpRegen;
        }
        else
        {
            Hp += HpRegen;
            Mp += MpRegen;
        }

        Hp = Math.Min(Hp, MaxHp);
        Mp = Math.Min(Mp, MaxMp);
        BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Mp, HighAbilityRsc), false);
        PostUpdateCurrentHp(this, oldHp, Hp, KillReason.Unknown);
    }

    public void StartUpdateXp(Character Owner)
    {
        if (MateXpUpdateTask != null)
        {
            return;
        }
        MateXpUpdateTask = new MateXpUpdateTask(Owner, this);
        TaskManager.Instance.Schedule(MateXpUpdateTask, TimeSpan.FromSeconds(60));
        //Logger.Trace("[StartUpdateXp] The current timer has been started...");
    }

    public void StopUpdateXp()
    {
        MateXpUpdateTask?.Cancel();
        MateXpUpdateTask = null;
        //Logger.Trace("[StopUpdateXp] The current timer has been canceled...");
    }

    public override void OnZoneChange(uint lastZoneKey, uint newZoneKey)
    {
        base.OnZoneChange(lastZoneKey, newZoneKey); // Unit

        if (Passengers.Count <= 0)
        {
            return;
        }

        foreach (var (_, passengerInfo) in Passengers)
        {
            var passenger = WorldManager.Instance.GetCharacterByObjId(passengerInfo.ObjId);
            passenger?.OnZoneChange(lastZoneKey, newZoneKey);
        }
    }
}
