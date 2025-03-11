using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.Proxy;

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
                Connection.SendPacket(new ChangeStatePacket(1));
                Connection.SendPacket(new SCHackGuardRetAddrsRequestPacket(false, false)); // HG_REQ? // TODO - config files
                var levelname = string.Empty;
                if (Connection.ActiveChar != null)
                {
                    levelname = ZoneManager.Instance.GetZoneByKey(Connection.ActiveChar.Transform.ZoneId)?.Name ?? "w_hanuimaru_1";
                }
                else
                {
                    levelname = "w_hanuimaru_1";
                }
                Connection.SendPacket(new SetGameTypePacket(levelname, 0, 1)); // TODO - level
                Connection.SendPacket(new SCInitialConfigPacket());
                // added in 5.0.7.0
                //Connection.SendPacket(new SCTowerConfigPacket());

                // Test URLs                                          // Original Trion values
                // Client treats these as folders and will add a trailing slash (/) with whatever it needs
                // For example, opening the Wiki would send http://localhost/aaemu/platform/login
                //var authUrl = "http://localhost/aaemu/login";         // "https://session.draft.integration.triongames.priv";
                //var platformUrl = "http://localhost/aaemu/platform";  // "http://archeage.draft.integration.triongames.priv/commerce/pruchase/credits/purchase-credits-flow.action";
                //var commerceUrl = "http://localhost/aaemu/shop";      // "" ;

                // It seems this packet can be ignored if you don't use the wiki/shop
                //Connection.SendPacket(new SCTrionConfigPacket(
                //    true,
                //    authUrl,
                //    platformUrl,
                //    commerceUrl)
                //); // TODO - config files
                Connection.SendPacket(new SCAccountInfoPacket(
                        (int)Connection.Payment.Method,
                        Connection.Payment.Location,
                        Connection.Payment.StartTime,
                        Connection.Payment.EndTime)
                );
                Connection.SendPacket(new SCChatSpamConfigPacket());
                Connection.SendPacket(new SCAccountAttributeConfigPacket([false, false, false]));
                Connection.SendPacket(new SCLevelRestrictionConfigPacket(0, 10, 0, 10, 10, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])); // TODO - config files

                Connection.SendPacket(new SCTaxItemConfigPacket(0));
                Connection.SendPacket(new SCInGameShopConfigPacket(1, 2, 0));
                Connection.SendPacket(new SCGameRuleConfigPacket(0, 0));

                ExpeditionManager.Instance.SendExpeditionProtect(Connection);

                Connection.SendPacket(new SCTaxItemConfig2Packet(0));
                // added in 5.0.7.0
                Connection.SendPacket(new SCServerFileTimeSyncPacket());
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
                Logger.Info("Unknown state: {0}", state);
                break;
        }
    }
}
