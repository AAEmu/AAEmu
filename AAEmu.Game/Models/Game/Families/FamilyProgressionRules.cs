using System.Text;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Models.Game.Families;

/// <summary>
/// Server policy for family progression and administration rules.
/// Timestamps use the server's shared Unix-seconds convention; percentage loss truncates the lost
/// amount and does not lower the explicitly purchased family level.
/// </summary>
public static class FamilyProgressionRules
{
    // The client reads family names into a 256-byte buffer and notices into an 800-byte buffer.
    // These are transport/UI limits, not shipped gameplay values; the family name rune policy is
    // still enforced by FamilyManager.IsValidFamilyName.
    public const int MaximumFamilyNameUtf8Bytes = 256;
    public const int MaximumNoticeUtf8Bytes = 800;

    public static bool IsNewUtcDay(long previousUnixSeconds, long currentUnixSeconds)
    {
        if (previousUnixSeconds <= 0)
            return true;

        var previous = DateTimeOffset.FromUnixTimeSeconds(previousUnixSeconds).UtcDateTime;
        var current = DateTimeOffset.FromUnixTimeSeconds(currentUnixSeconds).UtcDateTime;
        return ServerCalendar.IsNewDailyPeriod(previous, current);
    }

    public static bool CanChangeRole(long lastUpdateTime, long now, long cooldownSeconds) =>
        lastUpdateTime <= 0 || cooldownSeconds <= 0 || now - lastUpdateTime >= cooldownSeconds;

    public static bool IsWithinFamilyNameByteLimit(string value) =>
        value != null && Encoding.UTF8.GetByteCount(value) <= MaximumFamilyNameUtf8Bytes;

    public static bool IsValidNotice(string value) =>
        value != null && Encoding.UTF8.GetByteCount(value) <= MaximumNoticeUtf8Bytes;

    public static bool TryGetAssignableRole(Family family, FamilyMember member, uint roleId,
        FamilyGameData gameData, out FamilyRole role)
    {
        role = null;
        if (family == null || member == null || gameData == null || roleId == 0 || roleId > byte.MaxValue)
            return false;
        if (!family.Members.Contains(member) || member.Role == 1 || roleId == 1)
            return false;

        role = gameData.GetRole(roleId);
        return role != null && family.Members.Count(x => x.Role == roleId) < role.RoleCount;
    }

    public static bool TryGetNextMemberLimit(Family family, FamilyGameData gameData, out FamilyMemberLimit next)
    {
        next = null;
        if (family == null || gameData == null)
            return false;

        next = gameData.GetNextMemberLimit(family.MemberLimit);
        return next != null && next.Count == family.MemberLimit + 1 && next.ItemId != 0 && next.ItemCount > 0;
    }

    public static uint ApplyDepartureExperienceLoss(uint experience, int percent)
    {
        if (percent <= 0) return experience;
        if (percent >= 100) return 0;
        return experience - (uint)((ulong)experience * (uint)percent / 100);
    }
}
