using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Core.Packets.L2C;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// Sent by the client in response to a V2 Korea authentication challenge (<see cref="ACChallenge2Packet"/>).
/// </summary>
public class CAChallengeResponse2Packet() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CAChallengeResponse2Packet;

    /// <summary>8 AES-encrypted challenge uint32 values.</summary>
    public uint[] Ch { get; } = new uint[8];

    public override void Read(PacketStream stream)
    {
        for (var i = 0; i < 8; i++)
            Ch[i] = stream.ReadUInt32();
    }
}
