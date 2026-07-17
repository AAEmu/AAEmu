using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client containing the certificate number.
/// </summary>
public class CAPcCertNumberPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CAPcCertNumberPacket;

    public string? CertNumber { get; private set; }

    public override void Read(PacketStream stream)
    {
        // Nexon Simple Authentication Number? https://easyprotect.nexon.com/
        CertNumber = stream.ReadString(); // num
    }
}
