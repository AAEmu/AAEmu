using AAEmu.Game.Models.Game.Formulas;

namespace AAEmu.Game.Models.Game.Items;

public class Holdable
{
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
    /// Get the next attack animation ID for this weapon, cycling through available anims.
    /// </summary>
    public uint GetAttackAnimId(int attackIndex, bool leftHand = false)
    {
        uint[] anims;
        if (leftHand)
            anims = [AnimL1Id, AnimL2Id, AnimL3Id];
        else
            anims = [AnimR1Id, AnimR2Id, AnimR3Id];

        var validAnims = new List<uint>();
        foreach (var a in anims)
        {
            if (a > 0)
                validAnims.Add(a);
        }

        if (validAnims.Count == 0)
            return leftHand ? AnimR1Id : 2u; // Fallback: right-hand or fist

        return validAnims[attackIndex % validAnims.Count];
    }
}
