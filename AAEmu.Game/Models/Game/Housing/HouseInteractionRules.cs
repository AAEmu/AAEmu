using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// The interaction list a house answers <c>CSStartInteraction</c> with (<c>SCNpcInteractionSkillList</c>).
/// </summary>
/// <remarks>
/// <para>
/// For a house the client acts on the first entry and nothing else (x2game <c>FUN_39884e60</c> hands
/// <c>skills[0]</c> to <c>FUN_39884a70</c>, which passes a housing unit to <c>FUN_39886420</c>):
/// </para>
/// <list type="bullet">
/// <item><see cref="SkillsEnum.ConstructionInfo"/> or <see cref="SkillsEnum.HousingInteraction"/> opens the
/// house window. The client asks for the tax info and fires HOUSE_INTERACTION_START, and the UI shows the
/// materials window for an unfinished house and the maintenance window for a finished one.</item>
/// <item>Any other skill is cast on the house. When the house belongs to someone else the client asks
/// first (<c>BuildInteractionDlgTask</c>), which is the "help build" case.</item>
/// </list>
/// <para>
/// The materials window has no build button, so right-clicking is the only way to cast the build step,
/// and the answer is the only way to show an unfinished house's window. The step is offered when the
/// character carries what the step skill consumes. Otherwise the window opens and shows what is
/// missing.
/// </para>
/// </remarks>
public static class HouseInteractionRules
{
    /// <summary>
    /// The skills to offer for <paramref name="house"/>. An empty list is a valid answer: the client
    /// then does nothing.
    /// </summary>
    /// <param name="house">The house the player interacted with.</param>
    /// <param name="getSkillTemplate">Looks up the build step's skill.</param>
    /// <param name="getItemCount">How many of an item the interacting character carries.</param>
    public static uint[] ComposeSkills(
        House house,
        Func<uint, SkillTemplate> getSkillTemplate,
        Func<uint, int> getItemCount)
    {
        // A wrecked house has nothing to offer. The original client would loot it on nil_loot, and
        // there is no wreck loot here.
        if (house?.Template == null || house.IsDead)
            return [];

        if (house.CurrentStep < 0)
            return [SkillsEnum.HousingInteraction];

        if (!house.Template.BuildSteps.TryGetValue(house.CurrentStep, out var step) || step.SkillId == 0)
            return [SkillsEnum.ConstructionInfo];

        return CanPayForStep(getSkillTemplate?.Invoke(step.SkillId), getItemCount)
            ? [step.SkillId]
            : [SkillsEnum.ConstructionInfo];
    }

    /// <summary>
    /// Whether the character carries every item the step skill's effects consume, summed per item the
    /// way the cast charges them. A step skill that consumes nothing can always be cast; an unknown
    /// one cannot.
    /// </summary>
    internal static bool CanPayForStep(SkillTemplate stepSkill, Func<uint, int> getItemCount)
    {
        if (stepSkill == null)
            return false;

        var required = new Dictionary<uint, int>();
        foreach (var effect in stepSkill.Effects)
        {
            if (effect.ConsumeItemId == 0 || effect.ConsumeItemCount <= 0)
                continue;

            required[effect.ConsumeItemId] = required.GetValueOrDefault(effect.ConsumeItemId) + effect.ConsumeItemCount;
        }

        foreach (var (itemId, amount) in required)
        {
            if (getItemCount == null || getItemCount(itemId) < amount)
                return false;
        }

        return true;
    }
}
