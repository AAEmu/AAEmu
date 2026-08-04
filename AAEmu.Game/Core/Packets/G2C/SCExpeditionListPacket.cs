using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// </summary>
public sealed class SCExpeditionListPacket(IReadOnlyCollection<Expedition> expeditions)
    : GamePacket(SCOffsets.SCExpeditionListPacket, 1)
{
    private const int MaxExpeditionsPerPacket = byte.MaxValue;

    public override PacketStream Write(PacketStream stream)
    {
        if (expeditions.Count > MaxExpeditionsPerPacket)
            throw new ArgumentOutOfRangeException(
                nameof(expeditions), expeditions.Count,
                "Native expedition list packets contain at most 255 descriptors.");

        stream.Write((byte)expeditions.Count);
        foreach (var expedition in expeditions)
            stream.Write(expedition);
        return stream;
    }
}
