namespace AAEmu.Game.Models.Game.Indun;

public enum InstancePermissionTagKind : byte
{
    Item = 0,
    Skill = 1,
    Buff = 2
}

public readonly record struct InstancePermissionTag(InstancePermissionTagKind Kind, uint TagId);

/// <summary>
/// One weekly entrance window. Content uses Sunday=0 and permits hours beyond 23 to express a
/// window that continues into the following civil day. Equal start/end times mean the full day.
/// </summary>
public readonly record struct InstanceEntranceTime(
    int DayOfWeek,
    int StartHour,
    int StartMinute,
    int EndHour,
    int EndMinute);

public enum InstanceAdmissionFailure
{
    None,
    ProhibitedTag
}

public static class InstanceAdmissionRules
{
    public static bool IsOpen(IReadOnlyList<InstanceEntranceTime> windows, DateTime civilNow)
    {
        if (windows == null || windows.Count == 0)
            return true;

        var now = DateTime.SpecifyKind(civilNow, DateTimeKind.Unspecified);
        var sunday = now.Date.AddDays(-(int)now.DayOfWeek);
        foreach (var window in windows)
        {
            if (window.DayOfWeek is < 0 or > 6)
                continue;

            var startMinutes = checked(window.StartHour * 60 + window.StartMinute);
            var endMinutes = checked(window.EndHour * 60 + window.EndMinute);
            var start = sunday.AddDays(window.DayOfWeek).AddMinutes(startMinutes);
            var end = startMinutes == endMinutes
                ? start.AddDays(1)
                : sunday.AddDays(window.DayOfWeek).AddMinutes(endMinutes);
            if (end <= start)
                end = end.AddDays(1);

            if (now >= start && now < end)
                return true;

            // A window starting late Saturday can extend into the next week.
            if (window.DayOfWeek == 6 && now < sunday.AddDays(1))
            {
                start = start.AddDays(-7);
                end = end.AddDays(-7);
                if (now >= start && now < end)
                    return true;
            }
        }

        return false;
    }

    public static InstanceAdmissionFailure CheckTags(
        IReadOnlyList<InstancePermissionTag> permissions,
        uint whiteListBits,
        Func<InstancePermissionTagKind, uint, bool> hasTag)
    {
        if (permissions == null || permissions.Count == 0)
            return InstanceAdmissionFailure.None;

        foreach (var permission in permissions)
        {
            // The only IndunZone blacklist in the 10.0.2 corpus is buff tag 5947 (Daru transformation), on
            // instances 50, 51, 55 and 80. The title (3889), guild buff (2445), hero blessing (2532) and
            // equipment (3428) rows belong to BattleField instances, whose target_type the IndunZone loader
            // never reads, so whitelist item/skill semantics here would be guessing. Refusing the blacklisted
            // buff is the intended behaviour for this path.
            var whiteListed = (whiteListBits & (1u << (int)permission.Kind)) != 0;
            if (!whiteListed && hasTag(permission.Kind, permission.TagId))
                return InstanceAdmissionFailure.ProhibitedTag;
        }

        return InstanceAdmissionFailure.None;
    }
}
