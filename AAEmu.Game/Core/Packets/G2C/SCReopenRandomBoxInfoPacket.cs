using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Opens the reopen-box window for one box the character just used or refreshed.
/// </summary>
/// <remarks>
/// The client reads a header, then the claim record, then the pack's group list.
/// Header: bool isSuccess, bool isBlock, i8 infoState, u32 group id, i64 expire unix seconds.
/// Record: pack id, good id, reward item, grade, free and charge counts, life time, box item id,
/// open unix, refresh unix. Groups: i32 count, then per group i32 id, u32 rank, u32 base weight,
/// u32 current weight, u32 distribution weight. Those weights are the content columns.
/// </remarks>
public class SCReopenRandomBoxInfoPacket(ReopenBoxState state, MerchantReopenPack pack)
    : GamePacket(SCOffsets.SCReopenRandomBoxInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(true);
        stream.Write(false);
        stream.Write((sbyte)0);
        stream.Write(state.GroupId);
        stream.Write(ExpireUnix(state));

        var lifeTimeMinutes = pack?.LifeTime ?? 0;
        stream.Write(state.PackId);
        stream.Write(state.GoodId);
        stream.Write(state.RewardItemId);
        stream.Write((uint)state.RewardGrade);
        stream.Write(state.FreeUsed);
        stream.Write(state.ChargeUsed);
        stream.Write(lifeTimeMinutes);
        stream.Write(state.ItemId);
        stream.Write(ToUnix(state.OpenedAt ?? state.RolledAt));
        stream.Write(ToUnix(state.RolledAt));

        var groups = pack?.Groups;
        stream.Write(groups?.Count ?? 0);
        if (groups != null)
        {
            foreach (var group in groups)
            {
                stream.Write((int)group.Id);
                stream.Write(NonNegative(group.Rank));
                stream.Write(NonNegative(group.Weight));
                stream.Write(NonNegative(group.Weight));
                stream.Write(NonNegative(group.DistributionWeight));
            }
        }

        return stream;
    }

    private static long ExpireUnix(ReopenBoxState state)
    {
        if (state.RefreshAvailableAt == DateTime.MaxValue)
            return 0;
        return ToUnix(state.RefreshAvailableAt);
    }

    private static uint NonNegative(int value) => value < 0 ? 0u : (uint)value;

    private static long ToUnix(DateTime value) =>
        new DateTimeOffset(ServerCalendar.AsUtc(value)).ToUnixTimeSeconds();
}
