using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Families;

/// <summary>Authoritative family rules from content_configs kind 33.</summary>
public static class FamilyContentConfig
{
    public const string JoinLeaveItemKey = "family_join_leave_item";
    public const string LeaveExpPercentKey = "family_leave_dec_exp";
    public const string LoginExpKey = "family_login_inc_exp";
    public const string MaximumCountKey = "family_max_count";
    public const string MaximumLevelKey = "family_max_level";
    public const string NameChangeDelayDaysKey = "family_name_change_delay";
    public const string NameChangeItemKey = "family_name_change_item";
    public const string NameChangeItemCountKey = "family_name_change_item_count";
    public const string RejoinDelayHoursKey = "family_rejoin_delay_time";

    private static ContentConfigGameData Data => ContentConfigGameData.Instance;
    public static uint JoinLeaveItem => Data.RequireUInt(JoinLeaveItemKey);
    public static int LeaveExpPercent => Data.RequireInt(LeaveExpPercentKey);
    public static uint LoginExp => Data.RequireUInt(LoginExpKey);
    public static int MaximumCount => Data.RequireInt(MaximumCountKey);
    public static int MaximumLevel => Data.RequireInt(MaximumLevelKey);
    public static long NameChangeDelaySeconds => checked((long)Data.RequireInt(NameChangeDelayDaysKey) * 86400);
    public static uint NameChangeItem => Data.RequireUInt(NameChangeItemKey);
    public static int NameChangeItemCount => Data.RequireInt(NameChangeItemCountKey);
    public static long RejoinDelaySeconds => checked((long)Data.RequireInt(RejoinDelayHoursKey) * 3600);
}
