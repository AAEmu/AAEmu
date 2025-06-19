using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

public class CAListWorldPacket() : LoginPacket(CLOffsets.CAListWorldPacket)
{
    public override void Read(PacketStream stream)
    {
        var flag = stream.ReadUInt64();
    }

    public override void Execute()
    {
        Task.Run(() => GameController.Instance.RequestWorldListAsync(Connection));
    }
}
