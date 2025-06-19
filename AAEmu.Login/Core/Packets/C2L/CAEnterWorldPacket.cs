using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.C2L;

public class CAEnterWorldPacket() : LoginPacket(CLOffsets.CAEnterWorldPacket)
{
    private GameServerId _gsId;
    
    public override void Read(PacketStream stream)
    {
        var flag = stream.ReadUInt64();
        _gsId = new GameServerId(stream.ReadByte());
    }

    public override void Execute()
    {
        GameController.Instance.RequestEnterWorld(Connection, _gsId);
    }
}
