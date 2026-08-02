using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;

namespace AAEmu.Game.Core.Packets.G2C;

// confirmed field-by-field; no need to re-derive.
public class SCInitialConfigPacket() : GamePacket(SCOffsets.SCInitialConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var config = AppConfiguration.Instance.InitialConfig;

        stream.Write(config.Host);   // host (zstring, cap 259)
        FeaturesManager.Fsets.Write(stream); // fset (31-byte bitmap; catalog in Features/Feature.cs)

        // Characters per page of the character list, read through X2:GetCandidateOnceRetrieveCount().
        // Only consulted while the useCharacterListPage feature is enabled.
        stream.Write(config.CandidateRetrieveCount); // count (u32)

        // Labor shown for a fresh character - the pool AccountManager seeds a new account with.
        stream.Write(AppConfiguration.Instance.Labor.Default); // initLp (u32)

        stream.Write(config.CanPlaceHouse);
        stream.Write(config.CanPayTax);
        stream.Write(config.CanUseAuction);
        stream.Write(config.CanTrade);
        stream.Write(config.CanSendMail);
        stream.Write(config.CanUseBank);
        stream.Write(config.CanUseCopper);

        stream.Write(config.SecondPasswordMaxFailCount); // secondPasswordMaxFailCount (i8)
        stream.Write(config.IdleKickTime);               // idleKickTime (i32)

        // enable/pcbang/premium/maxCh. The server runs no premium subscription window, so all three
        // flags go out false. Publishing enable and premium true puts the lobby into its character
        // reservation gate: the client stops sending CSSelectCharacter for an existing character and
        // routes to character create instead.
        stream.Write(false);                           // enable
        stream.Write(false);                           // pcbang
        stream.Write(false);                           // premium
        stream.Write(config.PremiumMaxCharacterSlots); // maxCh (i8)

        // Honor percentage the client shows for War-zone kills, from the multiplier CharacterCombat
        // applies to each one.
        var warHonorPercent = AppConfiguration.Instance.World.PvpHonorRate * 100.0;
        stream.Write((short)Math.Clamp(warHonorPercent, 0.0, short.MaxValue)); // honorPointDuringWarPercent (i16)

        // when the wire value is zero. Publish that effective revision explicitly.
        stream.Write(AppConfiguration.Instance.Ucc.CacheVersion); // uccVer (i8)

        // account payment method. The native ClientPlayer constructor defaults it to 1.
        stream.Write(config.MemberType); // memberType (i8)

        // UnitDistance over_distance threshold, copied to ClientPlayer+0x3DFC; target.lua shows "???"
        // beyond it, so 0 blanks every target.
        stream.Write(config.BigModelDistance);  // bigModel (f32)
        stream.Write(config.MaxCharacterSlots); // tmpMaxCharSlot (i8)

        stream.Write(config.CashHost);     // cashHost (zstring, cap 259)
        stream.Write(config.SecurityHost); // securityHost (zstring, cap 259)
        stream.Write(config.IsDev);        // isDev

        // Client consumers use this as the premium_configs descriptor key. Source the exact content
        // row already loaded by PremiumGameData rather than coupling it to PaymentMethodType.
        stream.Write(checked((int)(PremiumGameData.Instance.Config?.Id ?? 0))); // premiumConfigType (i32)

        // The server publishes no world divisions, so the list is genuinely empty.
        stream.Write(0); // specificWorldDivisionIds: Size (i32)

        return stream;
    }
}
