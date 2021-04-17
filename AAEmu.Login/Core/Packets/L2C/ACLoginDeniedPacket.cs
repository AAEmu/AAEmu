using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACLoginDeniedPacket : LoginPacket
    {
        private byte _reason;

        public ACLoginDeniedPacket(byte reason) : base(LCOffsets.ACLoginDeniedPacket)
        {
            _reason = reason;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_reason);
            stream.Write(""); // vp
            stream.Write(""); // msg

            return stream;
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
}
