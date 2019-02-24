using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class FinishStatePacket : GamePacket
    {
        public FinishStatePacket() : base(0x001, 2)
        {
        }

        public override void Read(PacketStream stream)
        {
            var state = stream.ReadInt32();

            switch (state)
            {
                case 0:
                    Connection.SendPacket(new ChangeStatePacket(1));
                    Connection.SendPacket(new SetGameTypePacket("w_hanuimaru_1", 0, 1)); // TODO arche_mall
                    
                    // TODO ...
                    // TODO SCTrionConfigPacket
                    Connection.SendPacket(new SCInitialConfigPacket());
                    Connection.SendPacket(
                        new SCAccountInfoPacket(
                            (int)Connection.Payment.Method,
                            Connection.Payment.Location,
                            Connection.Payment.StartTime,
                            Connection.Payment.EndTime
                        )
                    );
                    Connection.SendPacket(new SCChatSpamDelayPacket());
                    break;
                case 1:
                    Connection.SendPacket(new ChangeStatePacket(2));
                    break;
                case 2:
                    Connection.SendPacket(new ChangeStatePacket(3));
                    break;
                case 3:
                case 4:
                case 5:
                case 6:
                    Connection.SendPacket(new ChangeStatePacket(state + 1));
                    break;
                case 7:
                    break;
                default:
                    _log.Info("Unknown state: {0}", state);
                    break;
            }
        }
    }
}
