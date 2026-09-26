using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Relays one member's standard MIDI part to the ensemble maestro.
/// </summary>
/// <remarks>
/// The client builds the MIDI file in a fixed buffer and sends its raw bytes. The unsigned size field
/// describes the block, and the block itself goes through the serializer's string/blob slot, which
/// writes a u16 length in front of the bytes — the same shape <see cref="SCSendUserMusicPacket"/> uses.
/// Without that prefix the client reads the first two MIDI bytes as the length.
/// </remarks>
public class SCEnsembleMidiBinReadyPacket(uint bc, uint bc2, uint size, byte[] data) : GamePacket(SCOffsets.SCEnsembleMidiBinReadyPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        stream.WriteBc(bc2);
        // The unsigned size is written with the uint overload; the payload then goes out through the
        // length-prefixed blob slot, which is what the client reads it back with.
        stream.Write(size);
        stream.Write(data, true);
        return stream;
    }
}
