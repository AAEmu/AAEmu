using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// The interaction skills an NPC offers on <c>SCNpcInteractionSkillList</c>. The client puts each
/// entry on its dynamic action bar, so an NPC may offer several actions at once; the first entry is
/// the one its own service naming implies.
/// </summary>
public static class NpcInteractionRules
{
    /// <summary>Service skill a template's flags name, or zero when the NPC is not a service NPC.</summary>
    public static uint PrimarySkill(NpcTemplate template, bool questTalk = false)
    {
        if (template == null)
            return 0;
        if (questTalk)
            return SkillsEnum.NpcTalk;
        if (template.Banker)
            return SkillsEnum.UseWarehouse;
        if (template.AbilityChanger)
            return SkillsEnum.ChangeSkillsets;
        if (template.Auctioneer)
            return SkillsEnum.UseAuctioneer;
        if (template.Priest)
            return SkillsEnum.Blessing;
        if (template.Repairman)
            return SkillsEnum.Repair;
        if (template.Merchant)
            return SkillsEnum.UseStore;
        if (template.Stabler)
            return SkillsEnum.HealPetSWounds;
        if (template.Expedition)
            return SkillsEnum.FormGuild;
        if (template.RecrutingBattlefieldId > 0)
            return SkillsEnum.WarSupport;
        if (template.Blacksmith)
            return SkillsEnum.ItemFusion;
        return 0;
    }

    /// <summary>
    /// The interaction list for one NPC: its service skill (zero when it has none) followed by the
    /// skills its authored interaction set adds, without duplicates and in authored order.
    /// <para>
    /// An empty list is a valid answer - the client hides the interaction bar for it - and is what an
    /// NPC without a set and without a service must send. A zero entry is not: the client resolves an
    /// icon per entry and reports skill type 0 as a missing UI asset.
    /// </para>
    /// </summary>
    public static IReadOnlyList<uint> ComposeSkills(
        NpcTemplate template,
        bool questTalk,
        IReadOnlyList<uint> authoredSkills)
    {
        var skills = new List<uint>();
        var service = PrimarySkill(template, questTalk);
        if (service > 0)
            skills.Add(service);

        if (authoredSkills != null)
        {
            foreach (var skillId in authoredSkills)
            {
                if (skillId > 0 && !skills.Contains(skillId))
                    skills.Add(skillId);
            }
        }

        return skills;
    }
}
