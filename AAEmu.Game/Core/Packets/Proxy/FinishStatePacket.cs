using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class FinishStatePacket() : GamePacket(PPOffsets.FinishStatePacket, 2)
{
    public override void Read(PacketStream stream)
    {
        var state = stream.ReadInt32();

        switch (state)
        {
            case 0:
                Connection.SendPacket(new ChangeStatePacket(1));
                // Do not issue SCHackGuardRetAddrsRequestPacket: this server has no corresponding client
                // response/attestation handler, so requesting addresses would advertise an unenforced check.
                var initialConfig = AppConfiguration.Instance.InitialConfig;
                Connection.SendPacket(new SetGameTypePacket(
                    initialConfig.LobbyLevel,
                    initialConfig.LobbyLevelChecksum,
                    initialConfig.LobbyImmersiveMode));
                Connection.SendPacket(new SCInitialConfigPacket());

                // Lobby config-burst order verified against a live 10.0.2.13 capture
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
                        Connection.Payment.EndTime,
                        Connection.Payment.RealPayTimeSeconds,
                        Connection.Payment.BuyPremiumCount)
                );
                Connection.SendPacket(new SCChatSpamDelayPacket());
                Connection.SendPacket(new SCAccountAttributeConfigPacket());

                AccountAttributePublisher.Send(Connection);

                Connection.SendPacket(new SCLevelRestrictionConfigPacket(AppConfiguration.Instance.LevelRestrictions));

                // Closes the lobby config burst (capture frame 16, after the establishment config block).
                Connection.SendPacket(new SCServerFileTimeSyncPacket());

                // The 10.0.2.13 context-establishment config block (SCW22orldRestrictOwnerChange/SCTaxItemConfig/
                // SCInGameShopConfig/SCGameRuleConfig/SCHousingAreaConfig, opcodes 0x2A2..0x2BD) is implemented but
                // NOT sent here: delivering it during the connect-stage establishment makes the client's
                // *(player+13720) with player == null) → EXCEPTION_ACCESS_VIOLATION before character select. The
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
                // This carried a hardcoded (1, 1, 1). Grade 1 is the free tier and the client's Patron
                // readout counts from zero - buff 7153 is named "grade 5" and hangs on grade_id 6 - so
                // character select showed "Patron 0" for every account regardless of characters.point.
                var (premiumPoint, premiumGrade) = ResolveAccountPremium(Connection);
                Logger.Debug(
                    "SCUpdatePremiumPoint point={0} grade={1} (forceMaxGrade={2}, maxGradeId={3}, chars={4})",
                    premiumPoint, premiumGrade,
                    AppConfiguration.Instance.Account?.ForceMaxPremiumGrade,
                    PremiumGameData.Instance.MaxGradeId,
                    Connection.Characters.Count);
                // Wire format verified against x2game-dev.dll: the serializer at rva 0xc61e50 reads
                // {int32 point, uint8 oldPg, uint8 pg} - point through the int32 vtable slot 0xA0, both
                // grades through the one-byte slot 0x90 - so this packet is shaped correctly. Nothing is
                // in flight at the handshake, so the old grade IS the current one; claiming a transition
                // here would be a fabrication, and testing one changed nothing in the client anyway.
                Connection.SendPacket(new SCUpdatePremiumPointPacket(
                    premiumPoint, (byte)premiumGrade, (byte)premiumGrade));
                break;
            default:
                Logger.Info("Unknown state: {0}", state);
                break;
        }
    }

    /// <summary>
    /// Premium points and grade for the whole account. Premium is account-wide and the lobby has no
    /// character selected yet, so the best grade any character on the account reached stands in for it.
    /// </summary>
    /// <remarks>
    /// The character list may not be loaded yet at this point in the handshake; that only costs the
    /// free tier, which is what the hardcoded value gave anyway. A forced max grade needs no characters.
    /// </remarks>
    private static (int Point, uint Grade) ResolveAccountPremium(GameConnection connection)
    {
        if (AppConfiguration.Instance.Account?.ForceMaxPremiumGrade == true)
        {
            var maxGrade = PremiumGameData.Instance.MaxGradeId;
            if (maxGrade > 0)
                return (Math.Max(0, PremiumGameData.Instance.GetGrade(maxGrade)?.Point ?? 0), maxGrade);
        }

        var point = 0;
        foreach (var character in connection.Characters.Values)
            point = Math.Max(point, character.Point);

        return (point, PremiumGameData.Instance.GetGradeForPoint(point));
    }
}
