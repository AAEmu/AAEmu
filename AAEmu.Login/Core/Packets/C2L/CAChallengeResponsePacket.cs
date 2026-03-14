using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Core.Packets.L2C;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// Sent by the client in response to a V1 Korea authentication challenge (<see cref="ACChallengePacket"/>).
/// </summary>
/// <remarks>
/// V1 is not enabled in production — see <see cref="ACChallengePacket"/> for details.
/// </remarks>
public class CAChallengeResponsePacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CAChallengeResponsePacket;

    /// <summary>4 AES-encrypted challenge uint32 values.</summary>
    public uint[] Ch { get; } = new uint[4];

    /// <summary>32 raw bytes containing the AES-encrypted plaintext password (V1 only).</summary>
    public byte[] Pw { get; private set; } = [];

    public override void Read(PacketStream stream)
    {
        for (var i = 0; i < 4; i++)
            Ch[i] = stream.ReadUInt32();
        Pw = stream.ReadBytes(32);
    }
}
