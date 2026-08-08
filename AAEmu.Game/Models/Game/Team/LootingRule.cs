using AAEmu.Commons.Network;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Team;

public class LootingRule : PacketMarshaler
{
    public LootingRuleMethod LootMethod { get; set; }
    public sbyte MinimumGrade { get; set; }
    public ulong LootMaster { get; set; }
    public bool RollForBindOnPickup { get; set; }

    public LootingRule()
    {
        var config = AppConfiguration.Instance.World;
        LootMethod = config.DefaultTeamLootMethod;
        MinimumGrade = config.DefaultTeamLootMinimumGrade;
        RollForBindOnPickup = config.DefaultTeamRollForBindOnPickup;
    }

    public static void ValidateDefaults(WorldConfig config)
    {
        if (config.DefaultTeamLootMethod is not (LootingRuleMethod.FreeForAll or LootingRuleMethod.RotateWinner))
            throw new InvalidOperationException("World.DefaultTeamLootMethod must be FreeForAll (0) or RotateWinner (1).");
        if (config.DefaultTeamLootMinimumGrade < (sbyte)ItemGrade.Crude ||
            config.DefaultTeamLootMinimumGrade > (sbyte)ItemGrade.Eternal)
            throw new InvalidOperationException("World.DefaultTeamLootMinimumGrade must be between Crude (0) and Eternal (12).");
    }

    public override void Read(PacketStream stream)
    {
        // i8 lootMethod, i8 minimumGrade, u64 lootMaster, bool rollForBop.
        LootMethod = (LootingRuleMethod)stream.ReadSByte();
        MinimumGrade = stream.ReadSByte();
        LootMaster = stream.ReadUInt64();
        RollForBindOnPickup = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((sbyte)LootMethod);
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
