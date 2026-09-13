namespace AAEmu.Game.Models.Game.Expeditions;

internal static class GuildLevelChangeRules
{
    public static bool CanApply(uint ownerId, uint actorId, byte actorRole, uint currentLevel,
        uint targetLevel, long currentExp, ExpeditionLevel target) =>
        ownerId == actorId && actorRole == byte.MaxValue && target != null &&
        targetLevel == currentLevel + 1 && currentExp >= target.TotalExp;
}
