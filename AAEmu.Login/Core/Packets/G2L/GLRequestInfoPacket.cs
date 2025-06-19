using AAEmu.Commons.Models;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLRequestInfoPacket() : InternalPacket(GLOffsets.GLRequestInfoPacket)
{
    public override void Read(PacketStream stream)
    {
        var connectionId = new ConnectionId(stream.ReadUInt32());
        var connection = LoginConnectionTable.Instance.GetConnection(connectionId);
        var requestId = stream.ReadUInt32();
        var characters = stream.ReadCollection<LoginCharacterInfo>();

        if (characters.Count > 0)
            connection!.AddCharacters(Connection.GameServer!.Id, characters);
        RequestController.Instance.ReleaseId(requestId);
    }
}
