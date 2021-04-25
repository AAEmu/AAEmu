using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Login;

namespace AAEmu.Game.Core.Packets.L2G
{
    public class LGPlayerEnterPacket : LoginPacket
    {
        public LGPlayerEnterPacket() : base(LGOffsets.LGPlayerEnterPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var accountId = stream.ReadUInt32();
            var connectionId = stream.ReadUInt32();
            EnterWorldManager.Instance.AddAccount(accountId, connectionId);
        }
    }
}
