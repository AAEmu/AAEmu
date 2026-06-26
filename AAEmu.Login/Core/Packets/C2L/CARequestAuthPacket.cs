using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client to the login server to request authentication.
/// </summary>
public class CARequestAuthPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CARequestAuthPacket;

    /// <summary>
    /// Gets the account name provided by the client for authentication.
    /// </summary>
    public string? Account { get; private set; }

    public override void Read(PacketStream stream)
    {
        var pFrom = stream.ReadUInt32();
        var pTo = stream.ReadUInt32();
        var svc = stream.ReadByte();
        var dev = stream.ReadBoolean();
        Account = stream.ReadString();
        var mac = stream.ReadBytes(8);  // fixed 8 bytes (not length-prefixed)
        var mac2 = stream.ReadBytes(8); // fixed 8 bytes (not length-prefixed)
        var cpu = stream.ReadUInt64();
        var is64Bit = stream.ReadBoolean();
        var isMultiClient = stream.ReadBoolean();
        var clientSerial = stream.ReadByte();
    }
}
