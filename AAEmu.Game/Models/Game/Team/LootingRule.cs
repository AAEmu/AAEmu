using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Team;

public class LootingRule : PacketMarshaler
{
    // TODO: Make default party loot settings configurable or remember player's last settings?
    public LootingRuleMethod LootMethod { get; set; } = LootingRuleMethod.RotateWinner;
    public byte MinimumGrade { get; set; } = 2; // Grand+
    public uint LootMaster { get; set; }
    public bool RollForBindOnPickup { get; set; } = true;

    public override void Read(PacketStream stream)
    {
        LootMethod = (LootingRuleMethod)stream.ReadByte();
        MinimumGrade = stream.ReadByte();
        LootMaster = stream.ReadUInt32();
        RollForBindOnPickup = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)LootMethod);
        stream.Write(MinimumGrade);
        stream.Write(LootMaster);
        stream.Write(RollForBindOnPickup);
        return stream;
    }

    /// <summary>
    /// Returns a new instance of this LootingRule with exactly the same settings
    /// </summary>
    /// <returns></returns>
    public LootingRule Clone()
    {
        return new LootingRule
        {
            LootMethod = LootMethod,
            MinimumGrade = MinimumGrade,
            LootMaster = LootMaster,
            RollForBindOnPickup = RollForBindOnPickup
        };
    }
}
