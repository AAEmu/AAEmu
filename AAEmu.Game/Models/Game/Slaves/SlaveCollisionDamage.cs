using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Slaves;

/// <summary>
/// Zone physics owns the contact, World owns HP and the floater.
/// Zone's collision manager aggregates contacts per unit pair and flushes them as ZWUnitCollision
/// (srcUnit/srcPart/srcMass, trgUnit/trgPart/trgMass, impact, world point); each side that is a hull
/// takes damage from formula <c>physics_collision_damage</c> (id 28):
/// <c>(0.000008 * (impact * (mass ^ 1.6))) * part_gain * equip_gain / armor_gain</c>.
/// <para>
/// part_gain is the struck face's <c>slave_collision_damages</c> gain, equip_gain the hull's
/// <see cref="UnitAttribute.PhysicsCollisionDamageMul"/> and armor_gain its
/// <see cref="UnitAttribute.PhysicsCollisionArmorMul"/>, both percent points over 100. Under a dock's
/// Ezi/Moored pair (-99 damage, +2900 armour) that lands on the 1 HP scratch retail shows, which the
/// same buff's health regen then repairs.
/// </para>
/// </summary>
public static class SlaveCollisionDamage
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Last resort when neither Zone nor ship_models has a mass for the hull.</summary>
    private const float DefaultMass = 1000f;

    public static void ApplyFromImpact(
        Slave slave, SlaveCollisionPart part, float impact, float mass,
        long collisionX, long collisionY, float collisionZ)
    {
        if (slave == null || slave.IsDead || slave.Template?.IsABoat() != true)
            return;

        impact = Math.Abs(impact);
        if (impact <= 0f)
            return;

        // The formula is very sensitive to mass (^1.6), so prefer Zone's physics figure and only fall
        // back to the model's authored mass — 45000 for the small sailing ship — when it is missing.
        if (mass < 1f || float.IsNaN(mass))
            mass = ModelManager.Instance.GetShipModel(slave.ModelId)?.Mass ?? DefaultMass;
        if (mass < 1f)
            mass = DefaultMass;

        var desc = SlaveGameData.Instance.GetCollisionDamageDesc(slave.Template.SlaveCollisionDamageId);
        var partGain = desc?.GainFor(part) ?? 1f;
        var equipGain = PercentGain(slave, UnitAttribute.PhysicsCollisionDamageMul);
        var armorGain = Math.Max(0.01d, PercentGain(slave, UnitAttribute.PhysicsCollisionArmorMul));

        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.PhysicsCollisionDamage);
        var raw = formula?.Evaluate(new Dictionary<string, double>
        {
            ["impact"] = impact,
            ["mass"] = mass,
            ["part_gain"] = partGain,
            ["equip_gain"] = equipGain,
            ["armor_gain"] = armorGain
        }) ?? 0.000008 * (impact * Math.Pow(mass, 1.6)) * partGain * equipGain / armorGain;

        // The client shows even a glancing dock nudge as -1, so a contact never resolves to nothing.
        var damage = (int)Math.Round(raw);
        if (damage < 1)
            damage = 1;
        var limit = desc?.LimitFor(part) ?? 0;
        if (limit > 0)
            damage = Math.Min(damage, limit);

        var oldHp = slave.Hp;
        slave.ReduceCurrentHp(slave, damage);
        slave.BroadcastPacket(
            new SCEnvDamagePacket(slave.ObjId, (uint)damage, collisionX, collisionY, collisionZ, impact, (byte)part),
            false);
        SlaveManager.SendUpdatedSlaveSourceItem(slave.Summoner, slave);

        Logger.Info(
            "SlaveCollision obj={0} dmg={1} hp={2}->{3}/{4} part={5} impact={6:F2} mass={7:F0} " +
            "partGain={8:F2} equipGain={9:F2} armorGain={10:F2} raw={11:F2}",
            slave.ObjId, damage, oldHp, slave.Hp, slave.MaxHp, part, impact, mass,
            partGain, equipGain, armorGain, raw);
    }

    /// <summary>
    /// Collision muls are stored as percent points where 100 is "unmodified", so the gain is the
    /// attribute evaluated over a base of 100. Feeding the base through
    /// <see cref="Unit.CalculateWithBonuses"/> keeps flat rows (+2900) and the rare percent-typed
    /// row (-100, full immunity) on the same footing.
    /// </summary>
    private static double PercentGain(Slave slave, UnitAttribute attribute)
    {
        return Math.Max(0d, slave.CalculateWithBonuses(100d, attribute) / 100d);
    }
}
