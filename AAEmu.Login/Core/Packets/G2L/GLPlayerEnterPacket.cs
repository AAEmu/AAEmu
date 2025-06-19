using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLPlayerEnterPacket() : InternalPacket(GLOffsets.GLPlayerEnterPacket)
{
    private ConnectionId _connectionId;
    private GameServerId _gsId;
    private byte _result;
    
    public override void Read(PacketStream stream)
    {
        _connectionId = new ConnectionId(stream.ReadUInt32());
        _gsId = new GameServerId(stream.ReadByte());
        _result = stream.ReadByte();
    }

    public override void Execute()
    {
        var connection = LoginConnectionTable.Instance.GetConnection(_connectionId);
        GameController.Instance.EnterWorld(connection!, _gsId, _result);
    }
}
