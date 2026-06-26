using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A developer/test packet sent by the client (via the <c>test_ars</c> console command) carrying an ARS test number.
/// </summary>
public class CATestArsPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CATestArsPacket;

    /// <summary>
    /// Gets the ARS test number entered by the user.
    /// </summary>
    public string? Number { get; private set; }

    public override void Read(PacketStream stream)
    {
        Number = stream.ReadString();
    }
}