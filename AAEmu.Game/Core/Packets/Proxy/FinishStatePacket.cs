using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class FinishStatePacket : GamePacket
    {
        public FinishStatePacket() : base(PPOffsets.FinishStatePacket, 2)
        {
        }

        public override void Read(PacketStream stream)
        {
            var state = stream.ReadInt32();

            switch (state)
            {
                case 0:
                    _log.Info("FinishStatePacket : BEGIN");
                    Connection.SendPacket(new ChangeStatePacket(1));
//                    Connection.SendPacket(new SCHackGuardRetAddrsRequestPacket(true, false, false)); // HG_REQ? // TODO - config files
                    Connection.SendPacket(new SetGameTypePacket("s_boiling_sea_4", 0, 1)); // TODO - level
                    Connection.SendPacket(new SCInitialConfigPacket());
                    Connection.SendPacket(new SCTrionConfigPacket(true, "https://archeage.dev", "https://shop.kakaogames.com", "https://store.steampowered.com")); // TODO - config files
                    Connection.SendPacket(new SCAccountInfoPacket((int)Connection.Payment.Method, Connection.Payment.Location, Connection.Payment.StartTime, Connection.Payment.EndTime));
                    Connection.SendPacket(new SCChatSpamConfigPacket());
                    Connection.SendPacket(new SCAccountAttributeConfigPacket(new[] { false, true, false, false })); // TODO
                    Connection.SendPacket(new SCLevelRestrictionConfigPacket(0, 10, 0, 0, 0, new byte[] { 0, 15, 15, 15, 0, 0, 15, 0, 0, 0, 0, 0, 0, 0, 15, 0, 0, 0 })); // TODO - config files 7533
                    Connection.SendPacket(new SCTaxItemConfigPacket(0));
                    Connection.SendPacket(new SCInGameShopConfigPacket(1, 2, 0));
                    Connection.SendPacket(new SCGameRuleConfigPacket(0, 0));
                    Connection.SendPacket(new SCTaxItemConfig2Packet(0));
                    _log.Info("FinishStatePacket : END");
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
                    Connection.SendPacket(new SCUpdatePremiumPointPacket(1, 1, 1));
                    break;
                default:
                    _log.Info("Unknown state: {0}", state);
                    break;
            }
        }
    }
}
