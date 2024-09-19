using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

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
using AAEmu.Game.Models.Game.Items.Actions;
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
    public override BaseUnitType BaseUnitType => BaseUnitType.Slave;
    public override ModelPostureType ModelPostureType { get => ModelPostureType.TurretState; }
    //public uint Id { get; set; } // moved to BaseUnit
    //public uint TemplateId { get; set; } // moved to BaseUnit
    public uint BondingObjId { get; set; } = 0;

    public SlaveTemplate Template { get; set; }
    // public Character Driver { get; set; }
    public Character Summoner { get; set; }
    public BaseUnitType OwnerType { get; set; }

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
    public sbyte Rpm { get; set; }
    public float RotSpeed { get; set; }
    public short RotationZ { get; set; }
    public float RotationDegrees { get; set; }
    public sbyte AttachPointId { get; set; } = -1;
    public uint OwnerObjId { get; set; }
    public RigidBody RigidBody { get; set; }
    public SlaveSpawner Spawner { get; set; }
    public Task LeaveTask { get; set; }
    public CancellationTokenSource CancelTokenSource { get; set; }
    public List<uint> Skills { get; set; }
    public List<uint> Tags { get; set; }
    public List<uint> Charges { get; set; }
    public bool IsLoadedPlayerSlave { get; set; }

    public Slave()
    {
        AttachedDoodads = new List<Doodad>();
        AttachedSlaves = new List<Slave>();
        AttachedCharacters = new Dictionary<AttachPointKind, Character>();
        HpTriggerPointsPercent.Add(0);
        HpTriggerPointsPercent.Add(25);
        HpTriggerPointsPercent.Add(50);
        HpTriggerPointsPercent.Add(75);
        HpTriggerPointsPercent.Add(100);
        Skills = new List<uint>();
        Tags = new List<uint>();
        Charges = new List<uint>();
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
        character.SendPacket(new SCUnitStatePacket(this));
        character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp, HighAbilityRsc));
        character.SendPacket(new SCSlaveStatusPacket(this));

        base.AddVisibleObject(character);

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

    /// <summary>
    /// Damage handler used by BoatPhysics
    /// </summary>
    /// <param name="damage"></param>
    /// <param name="isPercent"></param>
    /// <param name="killReason"></param>
    public void DoFloorCollisionDamage(int damage, bool isPercent = true, KillReason killReason = KillReason.Damage)
    {
        // If % based, calculate its damage
        if (isPercent)
        {
            damage = MaxHp * damage / 100;
        }

        ReduceCurrentHp(this, damage, killReason);
    }

    public override void PostUpdateCurrentHp(BaseUnit attacker, int oldHpValue, int newHpValue, KillReason killReason = KillReason.Damage)
    {
        base.PostUpdateCurrentHp(attacker, oldHpValue, newHpValue, killReason);
    }

    protected override void DoHpChangeTrigger(int triggerValue, bool tookDamage, int oldHpValue, int newHpValue)
    {
        Logger.Debug($"{Name} from {Summoner?.Name ?? "unknown"}'s HP is now at {triggerValue}%");
        SlaveManager.Instance.UpdateSlaveRepairPoints(this);
    }

    public override void DoDie(BaseUnit killer, KillReason killReason)
    {
        InterruptSkills();
        Events.OnDeath(this, new OnDeathArgs { Killer = (Unit)killer, Victim = this });
        Buffs.RemoveEffectsOnDeath();
        killer.BroadcastPacket(new SCUnitDeathPacket(ObjId, killReason, (Unit)killer), true);

        DestroyAttachedItems();
        DistributeSlaveDropDoodads();
        MarkSummoningItemAsDestroyed();

        Summoner?.SendPacket(new SCMySlavePacket(ObjId, TlId, Name, TemplateId, Hp, MaxHp, Transform.World.Position.X, Transform.World.Position.Y, Transform.World.Position.Z));
        Summoner?.SendPacket(new SCSlaveRemovedPacket(ObjId, TlId));
    }

    /// <summary>
    /// Destroys (de-spawns) any child doodads and slaves and drops trade packs if present in a random 1m range to the center of the vehicle
    /// </summary>
    private void DestroyAttachedItems()
    {
        // Destroy Doodads
        foreach (var doodad in AttachedDoodads)
        {
            // Check if the doodad held an item
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

    /// <summary>
    /// Creates the random debris created by destroying some of the vehicles (mostly ships)
    /// </summary>
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
                doodad.Transform.Local.Rotate(0, 0, (float)(Random.Shared.NextDouble() * Math.PI * 2f));
                doodad.InitDoodad();
                doodad.Spawn();
            }
        }
    }

    /// <summary>
    /// Updates the summon item data as being destroyed
    /// </summary>
    private void MarkSummoningItemAsDestroyed()
    {
        if (SummoningItem is not SummonSlave item)
            return;
        item.IsDestroyed = 1;
        item.RepairStartTime = DateTime.MinValue;
        item.SummonLocation = Vector3.Zero;
        item.IsDirty = true;
        Summoner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.MateDeath, new ItemUpdate(item), new List<ulong>()));
    }

    /// <summary>
    /// Creates a new DB connection and calls the Save function
    /// </summary>
    /// <returns></returns>
    public bool Save()
    {
        if (Id <= 0 || SummoningItem == null)
            return false;

        using var connection = MySQL.CreateConnection();
        return Save(connection, null);
    }

    /// <summary>
    /// Saves vehicle data to DB
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="transaction"></param>
    /// <returns></returns>
    public bool Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (Id <= 0)
            return false;

        bool result;
        try
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            if (transaction != null)
                command.Transaction = transaction;

            command.CommandText =
                "REPLACE INTO slaves(`id`,`item_id`,`template_id`,`attach_point`,`name`,`owner_type`,`owner_id`,`summoner`,`updated_at`,`hp`,`mp`,`x`,`y`,`z`) " +
                "VALUES (@id, @item_id, @templateId, @attachPoint, @name, @ownerType, @ownerId, @owner, @updated_at, @hp, @mp, @x, @y, @z)";
            command.Parameters.AddWithValue("@id", Id);
            command.Parameters.AddWithValue("@item_id", SummoningItem?.Id ?? 0);
            command.Parameters.AddWithValue("@templateId", Template.Id);
            command.Parameters.AddWithValue("@attachPoint", AttachPointId);
            command.Parameters.AddWithValue("@ownerType", (byte)OwnerType);
            command.Parameters.AddWithValue("@ownerId", OwnerId);
            command.Parameters.AddWithValue("@owner", Summoner?.Id ?? 0);
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

        // Also save its children if needed
        foreach (var child in AttachedSlaves)
            if (child.Id > 0)
                child.Save(connection, transaction);

        return result;
    }

    public override void Regenerate()
    {
        if (!NeedsRegen)
        {
            return;
        }
        if (IsDead)
        {
            foreach (var (_, character) in AttachedCharacters)
                SlaveManager.Instance.UnbindSlave(character, TlId, AttachUnitReason.None);
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

    public override void OnZoneChange(uint lastZoneKey, uint newZoneKey)
    {
        base.OnZoneChange(lastZoneKey, newZoneKey); // Unit
        
        foreach (var passenger in AttachedCharacters)
        {
            passenger.Value?.OnZoneChange(lastZoneKey, newZoneKey);
        }
    }
}
