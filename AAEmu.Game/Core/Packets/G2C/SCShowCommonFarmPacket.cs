using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CommonFarm;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The farm show-area response (SC 0x220): the client is told which farm tab this area hosts and
/// where its planting positions are.
/// </summary>
/// <remarks>
/// <para>
/// The body is <c>u32 type</c>, then a <b>signed</b> <c>s32 count</c>, then — only when
/// <c>count &gt; 0</c> — a loop of <c>count</c> quantized world positions, 11 bytes each. The count
/// is signed, so zero and negative values carry no positions at all.
/// </para>
/// <para>
/// The position is the standard quantized world-position block shared by every packet that reports
/// a unit location, which is why this writer reuses <see cref="PacketStream.WritePosition(float, float, float)"/>
/// and produces exactly the same 11 bytes as the sibling farm-list response. There is no
/// second, farm-specific position record.
/// </para>
/// </remarks>
public class SCShowCommonFarmPacket(uint farmType, int count, IReadOnlyList<Vector3> positions)
    : GamePacket(SCOffsets.SCShowCommonFarmPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Refuse a negative count instead of looping on it: the wire field is signed, and a
        // negative value is a malformed request rather than an empty one.
        if (!CommonFarmShowAreaRules.TryResolveCount(count, positions.Count, out var writeCount))
            throw new ArgumentOutOfRangeException(nameof(count), count,
                "ShowCommonFarm: the position count is signed and cannot be negative.");

        stream.Write(farmType);
        stream.Write(writeCount);

        for (var index = 0; index < writeCount; index++)
        {
            var position = positions[index];
            stream.WritePosition(position.X, position.Y, position.Z);
        }

        return stream;
    }
}
