using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Opens the reopen-box window for one box the character just used.
/// </summary>
/// <remarks>
/// The record is the one the client later echoes on the claim: four type fields (the first is the
/// pack id), free and charge counts, life time, the box item id, and the open and refresh times.
/// </remarks>
public class SCReopenRandomBoxInfoPacket(ReopenBoxState state, int lifeTimeMinutes)
    : GamePacket(SCOffsets.SCReopenRandomBoxInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)state.PackId);
        stream.Write(state.GoodId);
        stream.Write(state.RewardItemId);
        stream.Write((uint)state.RewardGrade);
        stream.Write(state.FreeUsed);
        stream.Write(state.ChargeUsed);
        stream.Write(lifeTimeMinutes);
        stream.Write(state.ItemId);
        stream.Write(ToUnix(state.OpenedAt ?? state.RolledAt));
        stream.Write(ToUnix(state.RolledAt));
        return stream;
    }

    private static long ToUnix(DateTime value) =>
        new DateTimeOffset(ServerCalendar.AsUtc(value)).ToUnixTimeSeconds();
}
