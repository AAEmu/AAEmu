namespace AAEmu.Login.Core.PacketHandlers.C2L;

public enum LoginDeniedReason : byte
{
    /// <summary>
    /// "Authentication Failed". Generic auth failure.
    /// </summary>
    LoginUnknown = 0,

    /// <summary>
    /// Username or password wrong.
    /// </summary>
    BadAccount = 1,

    /// <summary>
    /// Username or password wrong.
    /// </summary>
    BadResponse = 2,

    /// <summary>
    /// Account already logged in
    /// </summary>
    DuplicateLogin = 3,

    /// <summary>
    /// The game is unavailable right now (e.g. under service/maintenance).
    /// </summary>
    ServiceTime = 4,

    /// <summary>
    /// Account is suspended for $$ days due to suspicious transactions.
    /// </summary>
    TryTradeCashTemporal = 5,


    // 6 = "try_trade_cash_forever";
    // 7 = "traded_cash_temporal";
    // 8 = "traded_cash_forever";
    // 9 = "try_trade_item_servers";
    // 10 = "traded_item_servers";
    // 11 = "traded_account";
    // 12 = "try_cheat_temporal";
    // 13 = "try_cheat_forever";
    // 14 = "cheated";
    // 15 = "gamble_temporal";
    // 16 = "gamble_forever";
    // 17 = "abuse_bug_forever";
    // 18 = "abuse_bug_temporal";
    // 19 = "use_bot_forever";
    // 20 = "use_bot_temporal";
    // 21 = "use_bad_sw_temporal";
    // 22 = "use_bad_sw_forever";
    // 23 = "bad_user_workplace";
    // 24 = "bad_user_proxy_ip";
    // 25 = "steal_info";
    // 26 = "foul_lang_temporal";
    // 27 = "foul_lang_forever";
    // 28 = "bad_game_name";
    // 29 = "disturb_play";
    // 30 = "abnormal_play";
    // 31 = "disturb_gm";
    // 32 = "fraudful_report";
    // 33 = "fake_gm";
    // 34 = "wait_cert";
    // 35 = "steal_account_temporal";
    // 36 = "steal_account_forever";
    // 37 = "fraudful_steal_report";
    // 38 = "steal_person";
    // 39 = "request_by_self";
    // 40 = "request_by_parent";
    // 41 = "ads";
    // 42 = "request_by_authority";
    // 43 = "defraud_pay";
    // 44 = "unpaid_account";
    // 45 = "bulk_blocked_account";
    // 46 = "unpaid_pcbang";
    // 47 = "congested_server";
    // 48 = "invalid_mac";
}
