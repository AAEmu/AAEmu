using System.Threading.Tasks;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

public class CAListWorldPacket : LoginPacket
{
    public CAListWorldPacket() : base(CLOffsets.CAListWorldPacket)
    {
        // Nothing
    }

    public override void Read(PacketStream stream)
    {
        var flag = stream.ReadUInt64();

        Task.Run(() => GameController.Instance.RequestWorldListAsync(Connection));
    }
}
