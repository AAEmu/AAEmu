using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client to the login server to request authentication using a web/launcher session token.
/// </summary>
public class CARequestWebAuthPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CARequestWebAuthPacket;

    /// <summary>
    /// Gets the web session token provided by the launcher for authentication.
    /// </summary>
    public string? Auth { get; private set; }

    public override void Read(PacketStream stream)
    {
        var pFrom = stream.ReadUInt32();
        var pTo = stream.ReadUInt32();
        var svc = stream.ReadByte();
        var dev = stream.ReadBoolean();
        Auth = stream.ReadString();
        var mac = stream.ReadBytes();   // u16 length-prefixed (8 bytes on the wire)
        var mac2 = stream.ReadBytes();  // u16 length-prefixed (8 bytes on the wire)
        var cpu = stream.ReadUInt64();
        var is64Bit = stream.ReadBoolean();
        var isMultiClient = stream.ReadBoolean();
        var clientSerial = stream.ReadByte();
    }
}