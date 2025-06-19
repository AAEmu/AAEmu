using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.C2L;

public class CAEnterWorldPacket() : LoginPacket(CLOffsets.CAEnterWorldPacket)
{
    public override void Read(PacketStream stream)
    {
        var flag = stream.ReadUInt64();
        var gsId = new GameServerId(stream.ReadByte());

        GameController.Instance.RequestEnterWorld(Connection, gsId);
    }
}
