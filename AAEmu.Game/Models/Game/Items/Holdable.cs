using AAEmu.Game.Models.Game.Formulas;

namespace AAEmu.Game.Models.Game.Items;

public class Holdable
{
    /// <summary>Fallback animation ID when the weapon has no configured anims — generic fist swing.</summary>
    private const uint DefaultFistAnimId = 2;

    public uint Id { get; set; }
    public uint KindId { get; set; }
    public int Speed { get; set; }
    public int ExtraDamagePierceFactor { get; set; }
    public int ExtraDamageSlashFactor { get; set; }
    public int ExtraDamageBluntFactor { get; set; }
    public int MaxRange { get; set; }
    public int Angle { get; set; }
    public int EnchantedDps1000 { get; set; }
    public uint SlotTypeId { get; set; }
    public int DamageScale { get; set; }
    public Formula FormulaDps { get; set; }
    public Formula FormulaMDps { get; set; }
    public Formula FormulaArmor { get; set; }
    public Formula FormulaHDps { get; set; }
    public int MinRange { get; set; }
    public int SheathePriority { get; set; }
    public float DurabilityRatio { get; set; }
    public int RenewCategory { get; set; }
    public int ItemProcId { get; set; }
    public int StatMultiplier { get; set; }

    // Attack animation IDs per weapon — right hand (R) and left hand (L)
    public uint AnimR1Id { get; set; }
    public uint AnimL1Id { get; set; }
    public uint AnimR2Id { get; set; }
    public uint AnimL2Id { get; set; }
    public uint AnimR3Id { get; set; }
    public uint AnimL3Id { get; set; }

    /// <summary>
    /// Get the next attack animation ID for this weapon, cycling through the up-to-3
    /// configured anims. Skips slots with id 0. Falls back to right-hand or fist when none set.
    /// </summary>
    public uint GetAttackAnimId(int attackIndex, bool leftHand = false)
    {
        var a1 = leftHand ? AnimL1Id : AnimR1Id;
        var a2 = leftHand ? AnimL2Id : AnimR2Id;
        var a3 = leftHand ? AnimL3Id : AnimR3Id;

        Span<uint> valid = stackalloc uint[3];
        var count = 0;
        if (a1 > 0) valid[count++] = a1;
        if (a2 > 0) valid[count++] = a2;
        if (a3 > 0) valid[count++] = a3;

        if (count == 0)
            return leftHand ? AnimR1Id : DefaultFistAnimId;

        return valid[Math.Abs(attackIndex) % count];
    }
}
