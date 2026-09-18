using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The channel picker's list for a system instance. The client opens its window on the UI event this packet
/// raises — it never asks for the list — so it is pushed when the player reaches an entrance.
/// </summary>
/// <remarks>
/// Wire: u32 type, s32 count, then <c>count</c> rows of s32 channel, u32 instanceId, s32 restrict, s32 current.
/// The client reads a fixed loop over its clamped count, so a count above
/// <see cref="SysIndunChannelRules.MaxChannels"/> would have it read past the body; the count is clamped here
/// as well as by the row builder.
/// </remarks>
public class SCSysIndunStatPacket(uint type, IReadOnlyList<SysIndunChannel> channels)
    : GamePacket(SCOffsets.SCSysIndunStatPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var rows = channels ?? [];
        var count = Math.Min(rows.Count, SysIndunChannelRules.MaxChannels);

        stream.Write(type);
        stream.Write(count);
        for (var i = 0; i < count; i++)
        {
            stream.Write(rows[i].ChannelId);
            stream.Write(rows[i].InstanceId);
            stream.Write(rows[i].Restrict);
            stream.Write(rows[i].Current);
        }

        return stream;
    }
}
