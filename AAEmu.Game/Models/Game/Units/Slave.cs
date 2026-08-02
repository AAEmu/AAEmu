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
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Static;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.StaticValues;

using MySql.Data.MySqlClient;

using static AAEmu.Game.Models.Game.Units.Buffs;

namespace AAEmu.Game.Models.Game.Units;

public class Slave : Unit
{
    public override UnitTypeFlag TypeFlag { get => UnitTypeFlag.Slave; }
    public override BaseUnitType BaseUnitType => BaseUnitType.Slave;
    public override ModelPostureType ModelPostureType { get => ModelPostureType.TurretState; }
    //public uint Id { get; set; } // moved to BaseUnit
    //public uint TemplateId { get; set; } // moved to BaseUnit
    public uint BondingObjId { get; set; } = 0;

    public SlaveTemplate Template { get; set; }
    // public Character Driver { get; set; }
    public Character Summoner { get; set; }
    public BaseUnitType OwnerType { get; init; }

    /// <summary>
    /// When true, SCUnitState writes flags bit 11 (0x0800). Client maps that bit to unit+0x6E5C
    /// Set for the initial Spawn broadcast when CS/skill did not request hideSpawnEffect; clear
    /// immediately after so AOI re-streams do not replay the portal.
    /// </summary>
    public bool PendingSpawnPortal { get; set; }

    public Item SummoningItem { get; init; }
    public List<Doodad> AttachedDoodads { get; set; }
    public List<Slave> AttachedSlaves { get; set; }
    public Dictionary<AttachPointKind, Character> AttachedCharacters { get; set; }
    public DateTime SpawnTime { get; init; }
    public sbyte ThrottleRequest { get; set; }
    public sbyte Throttle { get; set; }
    public float Speed { get; set; }
    public sbyte SteeringRequest { get; set; }
    public sbyte Steering { get; set; }
    public float RotSpeed { get; set; }
    public short RotationZ { get; set; }
    public float RotationDegrees { get; set; }
    public sbyte AttachPointId { get; init; } = -1;
    public uint OwnerObjId { get; init; }

    /// <summary>
    /// Signed server-world selector for the slave's master. Slaves are local to their summoner by default.
    /// </summary>
    public sbyte MasterWorldId { get; set; } = -1;
    public SlaveSpawner Spawner { get; set; }
    public Task LeaveTask { get; set; }
    public CancellationTokenSource CancelTokenSource { get; set; }
    /// <summary>Ship harpoon rope / skill-controller sync (only meaningful for harpoon cannon slaves; default struct = disengaged, no heap alloc).</summary>
    public ShipHarpoonRopeState HarpoonRope;

    /// <summary>
    /// ZoneId of the dedicate this hull was announced to with WZUnitState, or 0 when no dedicate
    /// holds it. The hull must live in exactly one zone: a second dedicate that still simulates it
    /// keeps streaming its own ShipMoveType, so the World mirror (and every client) flip-flops
    /// between two headings and skill impulses land in the wrong process.
    /// </summary>
    public uint ZoneAnnouncedTo { get; set; }

    public Slave()
    {
        // Unit ctor builds SlotType.Equipment; retail ship customize uses 0xF2.
        // Slots go to at least 31 (slave_equip_slots); character gear enum tops out at 27.
        Equipment = new EquipmentContainer(0, SlotType.EquipmentSlave, false, this)
        {
            ContainerSize = 32
        };
        AttachedDoodads = [];
        AttachedSlaves = [];
        AttachedCharacters = [];
        HpTriggerPointsPercent.Add(0);
        HpTriggerPointsPercent.Add(25);
        HpTriggerPointsPercent.Add(50);
        HpTriggerPointsPercent.Add(75);
        HpTriggerPointsPercent.Add(100);
    }

    #region Attributes
    [UnitAttribute(UnitAttribute.Str)]
    public int Str
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.Str);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Str))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Dex))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Sta))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Int))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Spi))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
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
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.Fai))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
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
                    res += (int)bonus.Value;
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
                    res += (int)bonus.Value;
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
            foreach (var bonus in GetBonuses(UnitAttribute.PersistentHealthRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
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
                    res += (int)bonus.Value;
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
                    res += (int)bonus.Value;
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
            foreach (var bonus in GetBonuses(UnitAttribute.PersistentManaRegen))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    public override float LevelDps
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Slave, UnitFormulaKind.LevelDps);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["ab_level"] = 0
            };
            var res = formula.Evaluate(parameters);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MainhandDps)]
    public override int Dps
    {
        get
        {
            var weapon = Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand) as Weapon;
            var res = weapon?.Dps ?? 0;
            res += Str / 10f;
            foreach (var bonus in GetBonuses(UnitAttribute.MainhandDps))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
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
            var res = formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.MeleeDpsInc))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += res * bonus.Value / 100f;
                else
                    res += (int)bonus.Value;
            }

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.OffhandDps)]
    public override int OffhandDps
    {
        get
        {
            var weapon = Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand) as Weapon;
            var res = weapon?.Dps ?? 0;
            res += Str / 10f;
            foreach (var bonus in GetBonuses(UnitAttribute.OffhandDps))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }

            return (int)(res * 1000);
        }
    }

    [UnitAttribute(UnitAttribute.RangedDps)]
    public override int RangedDps
    {
        get
        {
            var weapon = Equipment.GetItemBySlot((int)EquipmentItemSlot.Ranged) as Weapon;
            var res = weapon?.Dps ?? 0;
            res += Dex / 10f;
            foreach (var bonus in GetBonuses(UnitAttribute.RangedDps))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
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
            var res = formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.RangedDpsInc))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += res * bonus.Value / 100f;
                else
                    res += (int)bonus.Value;
            }

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellDps)]
    public override int MDps
    {
        get
        {
            var weapon = Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand) as Weapon;
            var res = weapon?.MDps ?? 0;
            res += Int / 10f;
            foreach (var bonus in GetBonuses(UnitAttribute.SpellDps))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
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
            var res = formula.Evaluate(parameters);
            foreach (var bonus in GetBonuses(UnitAttribute.SpellDpsInc))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += res * bonus.Value / 100f;
                else
                    res += (int)bonus.Value;
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
            foreach (var bonus in GetBonuses(UnitAttribute.Armor))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
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
            foreach (var bonus in GetBonuses(UnitAttribute.MagicResist))
            {
                if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    res += (int)(res * bonus.Value / 100f);
                else
                    res += (int)bonus.Value;
            }
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.TurnSpeed)]
    public virtual float TurnSpeed { get => (float)CalculateWithBonuses(100f, UnitAttribute.TurnSpeed) / 100f; }

    #endregion

    /// <summary>
    /// Ship parts have no effects of their own; item_grade_buffs maps the equipped (itemId, grade) pair to a
    /// buff that belongs on the hull (all of them are flagged slave_applicable). That buff is where a sail's
    /// move-speed / turn-speed unit_modifiers live, and it is what a figurehead's mount skills name in their
    /// unit_reqs (kind 15) before they may be used at the helm.
    /// Called with both arguments null to (re)build the whole set, e.g. after a summon restores saved gear.
    /// </summary>
    public void UpdateEquipmentBuffs(Item itemAdded, Item itemRemoved)
    {
        if (itemRemoved != null)
        {
            // A hull can carry several copies of the same part (paired cannons), and they all resolve to the
            // same buff, so it only goes away with the last one.
            var removedBuff = GetEquipmentBuff(itemRemoved);
            if (removedBuff != null && Buffs.CheckBuff(removedBuff.Id) &&
                !Equipment.Items.Any(i => i != itemRemoved && GetEquipmentBuff(i)?.Id == removedBuff.Id))
                Buffs.RemoveBuff(removedBuff.Id);
        }

        if (itemAdded != null)
        {
            ApplyEquipmentBuff(itemAdded);
            return;
        }

        if (itemRemoved != null)
            return;

        foreach (var item in Equipment.Items)
            ApplyEquipmentBuff(item);
    }

    private static BuffTemplate GetEquipmentBuff(Item item)
    {
        return ItemGameData.Instance.GetItemBuff(item.TemplateId, item.Grade) ??
               SkillManager.Instance.GetBuffTemplate(item.Template?.BuffId ?? 0);
    }

    private void ApplyEquipmentBuff(Item item)
    {
        var buffTemplate = GetEquipmentBuff(item);
        if (buffTemplate == null || Buffs.CheckBuff(buffTemplate.Id))
            return;

        Buffs.AddBuff(new Buff(this, this, new SkillCasterUnit(ObjId), buffTemplate, null, DateTime.UtcNow)
        {
            AbLevel = (uint)(item.Template?.Level ?? 1)
        });
    }

    public override void AddVisibleObject(Character character)
    {
        character.SendPacket(new SCUnitStatePacket(this));
        character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
        character.SendPacket(new SCSlaveStatePacket(ObjId, TlId, Summoner?.Name ?? string.Empty, Summoner?.ObjId ?? 0, Id));

        // Same gate as Npc: SCUnitState does not carry faction for non-characters, and the
        // client only applies 0x02E when oldId matches current (fresh units are 0). Without
        // old=Invalid → new=real, summoned vehicles stay yellow/neutral.
        if (Faction != null)
            character.SendPacket(new SCUnitFactionChangedPacket(
                ObjId, Name ?? "", FactionsEnum.Invalid, Faction.Id, false));

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

        character.SendPacket(new SCUnitsRemovedPacket([ObjId]));
    }

    public override void PostUpdateCurrentHp(BaseUnit attacker, int oldHpValue, int newHpValue, KillReason killReason = KillReason.Damage)
    {
        base.PostUpdateCurrentHp(attacker, oldHpValue, newHpValue, killReason);
    }

    protected override void DoHpChangeTrigger(int triggerValue, bool tookDamage, int oldHpValue, int newHpValue)
    {
        Logger.Debug($"{Name} from {Summoner?.Name ?? "unknown"}'s HP is now at {triggerValue}%");
        ParentWorld.SlaveManager.UpdateSlaveRepairPoints(this);
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
        SlaveManager.SendUpdatedSlaveSourceItem(Summoner, this);
        Summoner?.BroadcastPacket(new SCSlaveRemovedPacket(Summoner.ObjId, TlId), true);
        ClearAllAggro();

        // Unbind all passengers
        foreach (var character in AttachedCharacters.Values.ToList())
            ParentWorld.SlaveManager.UnbindSlave(character, TlId, AttachUnitReason.None);

        // Schedule full cleanup via slave.Delete() → Hide() + DetachAll() + RemoveObject()
        // This keeps the slave visible and selectable during the death animation
        Despawn = DateTime.UtcNow.AddSeconds(Spawner?.DespawnTime ?? 20);
        ParentWorld.SpawnManager.AddDespawn(this);
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
                        var newDoodad = DoodadManager.Instance.Create(ParentWorld, 0, newDoodadId, null, true);
                        if (newDoodad == null)
                        {
                            Logger.Warn($"Dropped Doodad {newDoodadId}, from BackpackDoodadId could not be created");
                            break;
                        }
                        newDoodad.IsPersistent = true;
                        newDoodad.Transform = doodad.Transform.CloneDetached();
                        // Add a bit of randomness to the dropped doodad
                        newDoodad.Transform.Local.Translate(
                            Random.Shared.NextSingle() * 2f - 1f,
                            Random.Shared.NextSingle() * 2f - 1f,
                            0f);
                        newDoodad.AttachPoint = AttachPointKind.None;
                        newDoodad.ItemId = droppedItem.Id;
                        newDoodad.ItemTemplateId = droppedItem.TemplateId;
                        newDoodad.UccId = droppedItem.UccId; // Not sure if it's needed, but let's copy the Ucc for completeness' sake
                        newDoodad.SetScale(1f);
                        newDoodad.PlantTime = DateTime.UtcNow;
                        newDoodad.Faction = FactionManager.Instance.GetFaction(FactionsEnum.Friendly);

                        var floor = ParentWorld.Template.GeoData.GetHeight(newDoodad.Transform.World.Position); // WorldManager.Instance.GetHeight(newDoodad.Transform);
                        var surface = WorldManager.Instance.GetWorld(doodad.Transform.InstanceId)?.Water?.GetWaterSurface(newDoodad.Transform.World.Position, out _) ?? 0f;
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
            NonUnitObjectIdManager.Instance.ReleaseId(doodad.ObjId);
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
                var doodad = DoodadManager.Instance.Create(ParentWorld, 0, dropDoodad.DoodadId, null, true);
                var pos = Transform.World.Position;
                var rng = new Vector3(Random.Shared.NextSingle() * 2f - 1f, Random.Shared.NextSingle() * 2f - 1f, 0);
                rng = Vector3.Normalize(rng);
                rng *= Random.Shared.NextSingle() * dropDoodad.Radius;
                pos += rng;
                doodad.Transform.Local.SetPosition(pos);
                if (dropDoodad.OnWater == false)
                {
                    doodad.Transform.Local.SetHeight(doodad.ParentWorld.Template.GeoData.GetHeight(doodad.Transform.World.Position)); //WorldManager.Instance.GetHeight(doodad.Transform.ZoneId, pos.X, pos.Y, pos.Z));
                }
                else
                {
                    doodad.Transform.Local.SetHeight(WorldManager.Instance.GetWorld(doodad.Transform.InstanceId).Water.GetWaterSurface(pos, out _));
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
        Summoner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.MateDeath, new ItemUpdate(item), []));
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

    /// <summary>
    /// Moored / Slave Customizing Area (sphere_buffs detail → buffs.id 13817). Flat HealthRegen +200.
    /// Distinct from Ezi's Divine Protection (13816), which is collision/speed — not the dock heal.
    /// </summary>
    public const uint MooredBuffId = 13817;

    /// <summary>
    /// Registers the <c>unit_modifiers</c> of the hull's equipped parts as gear bonuses.
    /// </summary>
    /// <remarks>
    /// Ship parts carry real stats — each Bubbling mast (item 35426) is MaxHealth +5000 — and the
    /// client sums them into the hull bar. Ignoring them server-side left the Growling sailing ship
    /// with MaxHp 85000 against the 95000 the client computed, so a "full" hull drew as 89% and
    /// dock repair had nothing left to heal (both numbers then scale by Ezi's +10%: 93500/104500).
    /// </remarks>
    public void UpdateSlaveGearBonuses()
    {
        Bonuses[GearBonusesIndex] = [];
        if (Equipment == null)
            return;

        foreach (var item in Equipment.Items)
        {
            if (item == null)
                continue;

            foreach (var template in ItemManager.Instance.GetUnitModifiers(item.TemplateId))
                AddBonus(GearBonusesIndex, new Bonus { Template = template, Value = template.Value });

            if (item is EquipItem equipItem)
            {
                foreach (var gem in equipItem.GemIds)
                    foreach (var template in ItemManager.Instance.GetUnitModifiers(gem))
                        AddBonus(GearBonusesIndex, new Bonus { Template = template, Value = template.Value });
            }
        }
    }

    protected override void RegenTick(TimeSpan delta)
    {
        if (!NeedsRegen)
        {
            return;
        }
        if (IsDead)
        {
            foreach (var (_, character) in AttachedCharacters)
                character.ParentWorld.SlaveManager.UnbindSlave(character, TlId, AttachUnitReason.None);
            return;
        }

        var oldHp = Hp;

        // Dock Moored heal is HealthRegen (+200). Ship turn skills (Impulse) must not mute it via
        // IsInBattle — retail keeps repairing while you maneuver under the customize-area buff.
        var moored = Buffs.CheckBuff(MooredBuffId);
        if (IsInBattle && !moored)
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
        var points = new SCUnitPointsPacket(ObjId, Hp, Mp);
        BroadcastPacket(points, false);
        // Parent/driver can miss GetAround after hull Zone sync moves the ship; always push to riders.
        foreach (var rider in AttachedCharacters.Values)
            rider?.SendPacket(points);
        if (Summoner != null && !AttachedCharacters.ContainsValue(Summoner))
            Summoner.SendPacket(points);
        PostUpdateCurrentHp(this, oldHp, Hp, KillReason.Unknown);
        if (Hp != oldHp)
            SlaveManager.SendUpdatedSlaveSourceItem(Summoner, this);
    }

    public override void OnZoneChange(uint lastZoneKey, uint newZoneKey)
    {
        base.OnZoneChange(lastZoneKey, newZoneKey); // Unit

        // Sailing out of the zone the ship was summoned in has to move the hull to the new dedicate:
        // WZ traffic (impulse turns, control changes) is routed by the hull's current zone key, and a
        // dedicate that was never told to drop it keeps streaming a competing ShipMoveType.
        // ZoneAnnouncedTo 0 means the hull has not been handed to any dedicate yet — that first
        // announce belongs to the summon path, which sends the fully built state body.
        if (Template?.IsABoat() == true && ZoneAnnouncedTo != 0 && ZoneAnnouncedTo != newZoneKey)
            SlaveManager.AnnounceBoatToZone(this);

        foreach (var passenger in AttachedCharacters)
        {
            passenger.Value?.OnZoneChange(lastZoneKey, newZoneKey);
        }
    }

    public override Character GetOwnerCharacter()
    {
        var ownerObject = Summoner ?? (OwnerObjId > 0 ? ParentWorld.GetGameObject(OwnerObjId) as BaseUnit : null);
        return ownerObject?.GetOwnerCharacter();
    }
}
