using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLPlayerReconnectPacket() : InternalPacket(GLOffsets.GLPlayerReconnectPacket)
{
    public GameServerId GsId { get; private set; }
    public AccountId AccountId { get; private set; }
    public uint Token { get; private set; }

    public override void Read(PacketStream stream)
    {
        GsId = new GameServerId(stream.ReadByte());
        AccountId = new AccountId(stream.ReadUInt32());
        Token = stream.ReadUInt32();
    }
}
