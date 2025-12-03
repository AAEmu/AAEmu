using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCancelInstantGamePacket : GamePacket
    {
        public CSCancelInstantGamePacket() : base(CSOffsets.CSCancelInstantGamePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            Logger.Warn("CancelInstantGame");

            InstantGameManager.Instance.WithdrawFromBattlefield(Connection.ActiveChar);
        }
    }
}
