using System.Numerics;

namespace AAEmu.Game.Models.Game.World;

/// <summary>Exit routing rules for quest area volumes and their quest-sign volumes.</summary>
public static class QuestAreaSphereExitRules
{
    public static IReadOnlyList<SphereQuest> MatchingSigns(
        IEnumerable<SphereQuest> questSigns,
        uint componentId)
    {
        if (questSigns == null)
            return [];

        return componentId == 0
            ? questSigns.ToArray()
            : questSigns.Where(sign => sign.ComponentId == componentId).ToArray();
    }

    /// <summary>
    /// An area sphere may be smaller than the sign sphere for the same objective. Its exit should
    /// not clear that objective while the player remains in any matching sign volume.
    /// </summary>
    public static bool PlayerRemainsInMatchingSign(
        IEnumerable<SphereQuest> matchingSigns,
        Vector3 position) => matchingSigns?.Any(sign => sign.Contains(position)) == true;
}
