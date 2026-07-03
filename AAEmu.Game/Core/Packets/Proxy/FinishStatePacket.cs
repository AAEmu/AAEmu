using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.Proxy;

public class FinishStatePacket() : GamePacket(PPOffsets.FinishStatePacket, 2)
{
    private readonly bool[] _scAccountInitPacket = [false, true];
    private readonly byte[] _scLevelRestrictionInitPacket = [0, 15, 15, 15, 0, 0, 15, 0, 0, 0, 0, 0, 0, 0, 15];

    public override void Read(PacketStream stream)
    {
        var state = stream.ReadInt32();

        switch (state)
        {
            case 0:
                Connection.SendPacket(new ChangeStatePacket(1));
                // Connection.SendPacket(new SCHackGuardRetAddrsRequestPacket(false, false)); // HG_REQ? // TODO - config files
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

                // Lobby config-burst order verified against a live 10.0.2.13 capture:
                // SCInitialConfig is followed by
                // SCServerInfo then SCWorldContent, then SCAccountInfo. SCWorldContent carries the content-filter
                // table (sent empty here = no content blocked).
                Connection.SendPacket(new SCServerInfoPacket());
                Connection.SendPacket(new SCWorldContentPacket());

                // SCTrionConfig does not exist in the 10.0.2.13 client (its opcode 0x07 now belongs to
                // SCInitialConfig) — do not send it.
                Connection.SendPacket(new SCAccountInfoPacket(
                        (int)Connection.Payment.Method,
                        Connection.Payment.Location,
                        Connection.Payment.StartTime,
                        Connection.Payment.EndTime)
                );
                Connection.SendPacket(new SCChatSpamDelayPacket());
                Connection.SendPacket(new SCAccountAttributeConfigPacket(_scAccountInitPacket)); // TODO
                Connection.SendPacket(new SCLevelRestrictionConfigPacket(10, 10, 10, 10, 10, _scLevelRestrictionInitPacket)); // TODO - config files

                // Closes the lobby config burst (capture frame 16, after the establishment config block).
                Connection.SendPacket(new SCServerFileTimeSyncPacket());

                // The 10.0.2.13 context-establishment config block (SCWorldRestrictOwnerChange/SCTaxItemConfig/
                // SCInGameShopConfig/SCGameRuleConfig/SCHousingAreaConfig, opcodes 0x2A2..0x2BD) is implemented but
                // NOT sent here: delivering it during the connect-stage establishment makes the client's
                // gameRatingWnd.OnUpdate write to the not-yet-spawned player object → access violation before
                // character select. The
                // packets stay available for the in-world phase once the player entity exists.
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
