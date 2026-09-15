namespace AAEmu.Game.Models.Game.Expeditions;

internal static class InactiveExpeditionOwnerRules
{
    public static ExpeditionMember SelectSuccessor(Expedition expedition, DateTime now,
        TimeSpan ownerInactivity, TimeSpan candidateRecency, uint minimumContribution)
    {
        var owner = expedition.GetMember(expedition.OwnerId);
        if (owner == null || owner.IsOnline || now - owner.LastWorldLeaveTime < ownerInactivity)
            return null;

        return expedition.Members
            .Where(member => member.CharacterId != owner.CharacterId &&
                             member.ContributionPoint >= minimumContribution &&
                             (member.IsOnline || now - member.LastWorldLeaveTime <= candidateRecency))
            .OrderByDescending(member => member.ContributionPoint)
            .ThenByDescending(member => member.IsOnline)
            .ThenByDescending(member => member.LastWorldLeaveTime)
            .ThenBy(member => member.CharacterId)
            .FirstOrDefault();
    }
}
