using AAEmu.Commons.Models;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLRequestInfoPacket() : InternalPacket(GLOffsets.GLRequestInfoPacket)
{
    private ConnectionId _connectionId;
    private uint _requestId;
    private List<LoginCharacterInfo>? _characters;
    
    public override void Read(PacketStream stream)
    {
        _connectionId = new ConnectionId(stream.ReadUInt32());
        _requestId = stream.ReadUInt32();
        _characters = stream.ReadCollection<LoginCharacterInfo>();
    }

    public override void Execute()
    {
        var connection = LoginConnectionTable.Instance.GetConnection(_connectionId);
        if (_characters!.Count > 0)
            connection!.AddCharacters(Connection.GameServer!.Id, _characters);
        RequestController.Instance.ReleaseId(_requestId);
    }
}
