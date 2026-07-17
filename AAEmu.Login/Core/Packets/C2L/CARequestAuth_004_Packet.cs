using System.Xml.Linq;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client to request authentication via the 0x004 XML path.
/// </summary>
public class CARequestAuthPacket_0x004() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CARequestAuthPacket_0x004;

    /// <summary>
    /// Gets the username extracted from the XML payload.
    /// </summary>
    public string? Username { get; private set; }

    /// <summary>
    /// Gets the hex-encoded SHA-256 password extracted from the XML payload.
    /// </summary>
    public string? Password { get; private set; }

    public override void Read(PacketStream stream)
    {
        var pFrom = stream.ReadUInt32();
        var pTo = stream.ReadUInt32();
        var dev = stream.ReadBoolean();
        var mac = stream.ReadBytes();
        var param = stream.ReadString();
        var signature = stream.ReadString();

        var xmlDoc = XDocument.Parse(param);

        if (xmlDoc.Root == null)
        {
            Logger.Error("RequestAuth_004: failed to parse ticket XML");
            return;
        }

        var username = xmlDoc.Root.Element("username")?.Value;
        var password = xmlDoc.Root.Element("password")?.Value;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Logger.Error("RequestAuth_004: username or password is empty or whitespace");
            return;
        }

        Username = username;
        Password = password;
    }
}
