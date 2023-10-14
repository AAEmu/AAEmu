using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.StaticValues;
using Jitter.Dynamics;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Units;

public class Slave : Unit
{
    public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Slave;
    //public uint Id { get; set; } // moved to BaseUnit
    //public uint TemplateId { get; set; } // moved to BaseUnit
    public uint BondingObjId { get; set; } = 0;

    public SlaveTemplate Template { get; set; }
    // public Character Driver { get; set; }
    public Character Summoner { get; set; }
    public Item SummoningItem { get; set; }
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

            // Expected values;
            // NOTE: Cannons have 34666 Hp
            //
            //    ??? Hp -> Misc. Slaves (older formats, mostly unused) - slave_kind_id = 1
            // 104796 Hp -> Luxury Liner (item 19435) - slave_kind_id = 1
            //
            //  42216 Hp -> Schooner - slave_kind_id = 2
            //  52216 Hp -> Small Warship (Cutter/Junk) - slave_kind_id = 2, NOTE: +10000 HP
            //  57216 Hp -> Small Warship (Yawl) - slave_kind_id = 2, NOTE: +15000 HP
            //
            //   9216 Hp -> Harpoon Clipper - slave_kind_id = 3, NOTE: The Harpoon has 9666 Hp
            //   6216 Hp -> Rowboats - slave_kind_id = 4
            //  17216 Hp -> Tanks/Cars - slave_kind_id = 5
            //   7296 Hp -> Farm Wagon Types - slave_kind_id = 6
            //  42216 Hp -> Siege Catapult/Tower (item 150/155) - slave_kind_id = 7
            //   4796 Hp -> War Drum (item 26295) - slave_kind_id = 8 (type 8 seems various canons, war tools and instruments)
            //  32216 Hp -> Fishing Boats - slave_kind_id = 9

            // return 250000;

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
        base.AddVisibleObject(character);

        character.SendPacket(new SCUnitStatePacket(this));
        character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
        character.SendPacket(new SCSlaveStatePacket(ObjId, TlId, Summoner.Name, Summoner.ObjId, Id));

        foreach (var ati in AttachedCharacters)
        {
            if (ati.Value.ObjId > 0)
            {
                var player = WorldManager.Instance.GetCharacterByObjId(ati.Value.ObjId);
                if (player != null)
                    character.SendPacket(new SCUnitAttachedPacket(player.ObjId, ati.Key, AttachUnitReason.None, ObjId));
            }
        }
    }

    public override void RemoveVisibleObject(Character character)
    {
        base.RemoveVisibleObject(character);

        character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));
    }

    public void DoDamage(int damage, bool isPercent = true, KillReason killReason = KillReason.Damage)
    {
        // If % based, calculate it's damage
        if (isPercent)
        {
            damage = MaxHp * damage / 100;
        }

        ReduceCurrentHp(this, damage, killReason);
    }

    public override void ReduceCurrentHp(BaseUnit attacker, int value, KillReason killReason = KillReason.Damage)
    {
        if (Hp <= 0)
            return;

        var absorptionEffects = Buffs.GetAbsorptionEffects().ToList();
        if (absorptionEffects.Count > 0)
        {
            // Handle damage absorb
            foreach (var absorptionEffect in absorptionEffects)
            {
                value = absorptionEffect.ConsumeCharge(value);
            }
        }

        Hp = value < 0 ? Math.Max(Hp + value, 0) : Math.Max(Hp - value, 0);

        BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Hp > 0 ? Mp : 0), true);

        if (Hp > 0) { return; }
        ((Unit)attacker).Events.OnKill(attacker, new OnKillArgs { target = (Unit)attacker });
        DoDie(attacker, killReason);
    }

    public override void DoDie(BaseUnit killer, KillReason killReason)
    {
        InterruptSkills();
        Events.OnDeath(this, new OnDeathArgs { Killer = (Unit)killer, Victim = this });
        Buffs.RemoveEffectsOnDeath();
        killer.BroadcastPacket(new SCUnitDeathPacket(ObjId, killReason, (Unit)killer), true);

        DestroyAttachedItems();
        DistributeSlaveDropDoodads();

        Summoner?.SendPacket(new SCMySlavePacket(ObjId, TlId, Name, TemplateId, Hp, MaxHp, Transform.World.Position.X,Transform.World.Position.Y,Transform.World.Position.Z));
        Summoner?.SendPacket(new SCSlaveRemovedPacket(ObjId, TlId));
    }

    private void DestroyAttachedItems()
    {
        // Destroy Doodads
        foreach (var doodad in AttachedDoodads)
        {
            // Check if the doodad held a item
            if (doodad.ItemId > 0)
            {
                var droppedItem = ItemManager.Instance.GetItemByItemId(doodad.ItemId);
                // If the held item is a backpack, drop it to the floor
                if (droppedItem is Backpack backpackItem)
                {
                    // Drop Backpack to the floor (spawn doodad)
                    var putDownSkill = SkillManager.Instance.GetSkillTemplate(backpackItem.Template.UseSkillId);
                    foreach (var skillEffect in putDownSkill.Effects)
                    {
                        if (skillEffect.Template is not PutDownBackpackEffect putDownBackpackEffectTemplate)
                            continue;

                        var newDoodadId = putDownBackpackEffectTemplate.BackpackDoodadId;

                        // Create the Doodad at location on the floor if it's close to it
                        var newDoodad = DoodadManager.Instance.Create(0, newDoodadId, null, true);
                        if (newDoodad == null)
                        {
                            Logger.Warn($"Dropped Doodad {newDoodadId}, from BackpackDoodadId could not be created");
                            return;
                        }
                        newDoodad.IsPersistent = true;
                        newDoodad.Transform = doodad.Transform.CloneDetached();
                        // Add a bit of randomness to the dropped doodad
                        newDoodad.Transform.Local.Translate((Random.Shared.NextSingle() * 2f) - 1f,
                            (Random.Shared.NextSingle() * 2f) - 1f, 0);
                        newDoodad.AttachPoint = AttachPointKind.None;
                        newDoodad.ItemId = droppedItem.Id;
                        newDoodad.ItemTemplateId = droppedItem.TemplateId;
                        newDoodad.UccId = droppedItem.UccId; // Not sure if it's needed, but let's copy the Ucc for completeness' sake
                        newDoodad.SetScale(1f);
                        newDoodad.PlantTime = DateTime.UtcNow;
                        newDoodad.Faction = FactionManager.Instance.GetFaction(FactionsEnum.Friendly);

                        var floor = WorldManager.Instance.GetHeight(newDoodad.Transform);
                        var surface = WorldManager.Instance.GetWorld(doodad.Transform.WorldId)?.Water?.GetWaterSurface(newDoodad.Transform.World.Position) ?? 0f;
                        var depth = surface - floor;

                        // It seems that when the water is deep, drops to the water surface, otherwise, it sinks to the floor
                        // Requires more testing, possibly a server setting?
                        newDoodad.Transform.Local.SetHeight(depth < 30f ? floor : Math.Max(floor, surface));

                        // Save new doodad
                        newDoodad.InitDoodad();
                        newDoodad.Spawn();
                        newDoodad.Save();

                        // Remove data from trade pack slot
                        doodad.ItemTemplateId = 0;
                        doodad.ItemId = 0;

                        // Hacky way to force move to next phase to reset doodad to default before saving
                        var funcs = DoodadManager.Instance.GetDoodadFuncs(doodad.FuncGroupId);
                        foreach (var phaseFunc in funcs)
                        {
                            if (phaseFunc.FuncType == "DoodadFuncRecoverItem")
                            {
                                doodad.DoChangePhase(null, phaseFunc.NextPhase);
                                break;
                            }
                        }

                        // Save new empty data
                        doodad.Save();
                    }
                }
            }
            ObjectIdManager.Instance.ReleaseId(doodad.ObjId);
            doodad.IsPersistent = false;
            doodad.Delete();
        }

        // Destroy Slaves
        foreach (var slave in AttachedSlaves)
        {
            ObjectIdManager.Instance.ReleaseId(slave.ObjId);
            // slave.IsPersistent = false;
            slave.Delete();
        }
    }

    private void DistributeSlaveDropDoodads()
    {
        foreach (var dropDoodad in Template.SlaveDropDoodads)
        {
            for (var counter = 0; counter < dropDoodad.Count; counter++)
            {
                var doodad = DoodadManager.Instance.Create(0, dropDoodad.DoodadId, null, true);
                var pos = Transform.World.Position;
                var rng = new Vector3((Random.Shared.NextSingle() * 2f) - 1f, (Random.Shared.NextSingle() * 2f) - 1f, 0);
                rng = Vector3.Normalize(rng);
                rng *= Random.Shared.NextSingle() * dropDoodad.Radius;
                pos += rng;
                doodad.Transform.Local.SetPosition(pos);
                if (dropDoodad.OnWater == false)
                {
                    doodad.Transform.Local.SetHeight(WorldManager.Instance.GetHeight(doodad.Transform.ZoneId, pos.X, pos.Y));
                }
                else
                {
                    doodad.Transform.Local.SetHeight(WorldManager.Instance.GetWorld(doodad.Transform.WorldId).Water.GetWaterSurface(pos));
                }
                doodad.Transform.Local.Rotate(0,0,(float)(Random.Shared.NextDouble() * Math.PI * 2f));
                doodad.InitDoodad();
                doodad.Spawn();
            }
        }
    }

    public bool Save()
    {
        if (Id <= 0)
            return false;

        using var connection = MySQL.CreateConnection();
        return Save(connection, null);
    }

    public bool Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (Id <= 0)
            return false;

        var result = false;
        try
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            if (transaction != null)
                command.Transaction = transaction;

            command.CommandText =
                "REPLACE INTO slaves(`id`,`item_id`,`name`,`owner`,`updated_at`,`hp`,`mp`,`x`,`y`,`z`) " +
                "VALUES (@id, @item_id, @name, @owner, @updated_at, @hp, @mp, @x, @y, @z)";
            command.Parameters.AddWithValue("@id", Id);
            command.Parameters.AddWithValue("@item_id", SummoningItem?.Id ?? 0);
            command.Parameters.AddWithValue("@owner", Summoner?.Id ?? OwnerId);
            command.Parameters.AddWithValue("@name", Name);
            command.Parameters.AddWithValue("@hp", Hp);
            command.Parameters.AddWithValue("@mp", Mp);
            command.Parameters.AddWithValue("@updated_at", DateTime.UtcNow);
            command.Parameters.AddWithValue("@x", Transform.World.Position.X);
            command.Parameters.AddWithValue("@y", Transform.World.Position.Y);
            command.Parameters.AddWithValue("@z", Transform.World.Position.Z);
            command.ExecuteNonQuery();
            result = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            result = false;
        }

        return result;
    }
}
