using System.Buffers.Binary;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Core.PacketHandlers.C2L;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to inform the client that the login attempt has been denied.
/// </summary>
/// <param name="reason">The reason the login was denied.</param>
/// <param name="vp">
/// Variable parameters that replace <c>$1</c>, <c>$2</c>, ... <c>$9</c> placeholders in the client's
/// localized message for the given reason. Each value is formatted as a decimal integer.
/// For example, reason <c>try_trade_cash_temporal</c> uses <c>$1</c> for the
/// number of suspension days, so pass the day count as the first element.
/// </param>
public class ACLoginDeniedPacket(LoginDeniedReason reason, params int[] vp) : LoginPacket(LCOffsets.ACLoginDeniedPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)reason);
        stream.Write(BuildVpData(), appendSize: true); // vp - binary: [0x00 + int32_le] per entry
        stream.Write(""); // msg - completely overrides the displayed message
        stream.Write((byte)0); // quitClient - 0 = keep client running on the login screen

        return stream;
    }

    /// <summary>
    /// Builds the binary VP data. Each entry is a 0x00 separator byte followed by a 4-byte little-endian int32.
    /// The client formats each int32 with <c>%d</c> and substitutes them into <c>$1</c>..<c>$9</c> placeholders.
    /// </summary>
    private byte[] BuildVpData()
    {
        if (vp.Length == 0)
            return [];

        var data = new byte[vp.Length * 5]; // 1 null byte + 4 bytes per entry
        for (var i = 0; i < vp.Length; i++)
        {
            var offset = i * 5;
            data[offset] = 0x00;
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + 1, 4), vp[i]);
        }

        return data;
    }

    /*
    0 = "login_unknown";
    1 = "bad_account";
    2 = "bad_response";
    3 = "duplicate_login";
    4 = "service_time";
    5 = "try_trade_cash_temporal";
    6 = "try_trade_cash_forever";
    7 = "traded_cash_temporal";
    8 = "traded_cash_forever";
    9 = "try_trade_item_servers";
    10 = "traded_item_servers";
    11 = "traded_account";
    12 = "try_cheat_temporal";
    13 = "try_cheat_forever";
    14 = "cheated";
    15 = "gamble_temporal";
    16 = "gamble_forever";
    17 = "abuse_bug_forever";
    18 = "abuse_bug_temporal";
    19 = "use_bot_forever";
    20 = "use_bot_temporal";
    21 = "use_bad_sw_temporal";
    22 = "use_bad_sw_forever";
    23 = "bad_user_workplace";
    24 = "bad_user_proxy_ip";
    25 = "steal_info";
    26 = "foul_lang_temporal";
    27 = "foul_lang_forever";
    28 = "bad_game_name";
    29 = "disturb_play";
    30 = "abnormal_play";
    31 = "disturb_gm";
    32 = "fraudful_report";
    33 = "fake_gm";
    34 = "wait_cert";
    35 = "steal_account_temporal";
    36 = "steal_account_forever";
    37 = "fraudful_steal_report";
    38 = "steal_person";
    39 = "request_by_self";
    40 = "request_by_parent";
    41 = "ads";
    42 = "request_by_authority";
    43 = "defraud_pay";
    44 = "unpaid_account";
    45 = "bulk_blocked_account";
    46 = "unpaid_pcbang";
    47 = "congested_server";
    48 = "invalid_mac";
    */
}
