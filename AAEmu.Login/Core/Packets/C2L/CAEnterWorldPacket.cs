using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.C2L;

public class CAEnterWorldPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CAEnterWorldPacket;
    
    public ulong Flag { get; private set; }
    public GameServerId GsId { get; private set; }

    public override void Read(PacketStream stream)
    {
        Flag = stream.ReadUInt64();
        GsId = new GameServerId(stream.ReadByte());
    }
}
