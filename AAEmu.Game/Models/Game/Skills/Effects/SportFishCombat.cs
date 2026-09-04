using System.Collections.Concurrent;

using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Sport-fish combat split under ZoneAuthority, plus hook / line-break rules.
/// </summary>
/// <remarks>
/// Plot 821 waits on tag 1090, applied by bite skill 21608 (입질). Zone owns the rest of the
/// InCombat kit (move / flee / basic attack). World still applies the bite so the running
/// plot sees the tag; movement skills must not run on World or the fish is simulated twice.
/// Tension (buff 5793) transforms to line-broken (5794) at 20 stacks; that pulse and the
/// fish-lifetime / struggle timeouts all SkillUse 21616, whose only effect is NpcDespawn.
/// </remarks>
public static class SportFishCombat
{
    public const uint BiteSkillId = 21608;
    public const uint FleeSkillId = 21616;
    public const uint TensionBuffId = 5793;
    public const uint LineBrokenBuffId = 5794;
    public const uint FishingSkillTagId = (uint)TagsEnum.FishingSkill;
    public const uint BaitFishingPlotId = 809;
    public const uint SportFishingPlotId = 821;

    private static readonly ConcurrentDictionary<uint, uint> HooksByPlayerId = new();

    public static bool ShouldWorldApplyInCombatSkill(bool zoneAuthority, bool isZoneMirror, uint skillId)
    {
        if (!zoneAuthority || !isZoneMirror)
            return true;
        return skillId == BiteSkillId;
    }

    public static bool HasFishingSkillTag(IReadOnlyList<uint> skillTags)
    {
        if (skillTags == null)
            return false;
        for (var i = 0; i < skillTags.Count; i++)
        {
            if (skillTags[i] == FishingSkillTagId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Hold / reel / slack. Rod casts (21571 / 21578) are Pos-targeted and are not holds.
    /// </summary>
    public static bool IsFishingHoldSkill(SkillTargetType targetType, IReadOnlyList<uint> skillTags) =>
        targetType == SkillTargetType.Hostile && HasFishingSkillTag(skillTags);

    /// <summary>
    /// Stand-firm left → right (and the rest of the kit) must start on the first click. The
    /// shared GCD and the 150 ms anti-spam belong to the rod cast, not to swapping holds.
    /// </summary>
    public static bool ShouldBypassSharedGcd(
        int castingTime,
        SkillTargetType targetType,
        IReadOnlyList<uint> skillTags) =>
        castingTime <= 0 && IsFishingHoldSkill(targetType, skillTags);

    public static bool IsRodPlot(uint plotId) =>
        plotId == BaitFishingPlotId || plotId == SportFishingPlotId;

    /// <summary>
    /// Rod casts 21571 / 21578 are not cancelable on the skill row. The client still
    /// sends CSStopCasting when the hull (or the attached player) reports movement,
    /// which tore the plot down during the 1.5 s cast and left the last throw pose.
    /// </summary>
    public static bool ShouldIgnoreClientStopCasting(
        uint plotId,
        bool castingCancelable,
        bool channelingCancelable) =>
        IsRodPlot(plotId) && !castingCancelable && !channelingCancelable;

    /// <summary>
    /// A new hold replaces the previous hold immediately. A hold must not cancel the rod
    /// channel (plots 809 / 821). Any other busy cast/channel is still replaced.
    /// </summary>
    public static bool ShouldCancelPreviousPlot(bool previousWasBusy, bool previousWasHold, bool incomingIsHold)
    {
        if (previousWasHold && incomingIsHold)
            return true;
        if (incomingIsHold)
            return false;
        return previousWasBusy;
    }

    /// <summary>
    /// Hold skills such as 21194 ship a plot but are not flagged plot_only. Running Cast()
    /// afterwards EndSkill's the TlId while the plot is still on the bar.
    /// </summary>
    public static bool ShouldRunPlotGraphOnly(
        bool casterIsCharacter,
        bool hasPlot,
        bool plotOnly,
        IReadOnlyList<uint> skillTags)
    {
        if (plotOnly)
            return true;
        if (!casterIsCharacter || !hasPlot)
            return false;
        return HasFishingSkillTag(skillTags);
    }

    public static void RegisterHook(uint playerId, uint fishObjId)
    {
        if (playerId == 0 || fishObjId == 0)
            return;
        HooksByPlayerId[playerId] = fishObjId;
    }

    public static bool HasActiveHook(uint playerId)
    {
        if (playerId == 0 || !HooksByPlayerId.TryGetValue(playerId, out var fishObjId))
            return false;

        if (WorldIntegration.FindUnitAcrossWorlds(fishObjId) is Npc { SportFishLineDropped: false })
            return true;

        HooksByPlayerId.TryRemove(playerId, out _);
        return false;
    }

    public static bool IsUnusableTarget(BaseUnit target) =>
        target is Npc { SportFishLineDropped: true };

    public static void OnLineDropped(Npc fish)
    {
        if (fish == null || fish.SportFishLineDropped)
            return;

        fish.SportFishLineDropped = true;
        if (fish.OwnerId != 0)
            HooksByPlayerId.TryRemove(fish.OwnerId, out _);

        fish.ClearAllAggro();
        fish.IsInBattle = false;
        fish.CurrentTarget = null;
        WorldIntegration.RelayAggroResetToZone?.Invoke(fish.ObjId, 0, 0, 0, 0);
        WorldIntegration.RelayTargetChangedToZone?.Invoke(fish.ObjId, 0, true);

        var owner = fish.OwnerId == 0 ? null : WorldManager.Instance.GetCharacterById(fish.OwnerId);
        if (owner == null)
            return;

        if (owner.CurrentTarget?.ObjId == fish.ObjId)
        {
            owner.CurrentTarget = null;
            owner.SendPacket(new SCTargetChangedPacket(owner.ObjId, 0));
            WorldIntegration.RelayTargetChangedToZone?.Invoke(owner.ObjId, 0, true);
        }

        var plotSkill = owner.ActivePlotState?.ActiveSkill;
        if (plotSkill?.Template == null)
            return;

        var tags = SkillManager.Instance.GetSkillTags(plotSkill.Id);
        if (ShouldRunPlotGraphOnly(true, plotSkill.Template.Plot != null, plotSkill.Template.PlotOnly, tags))
            owner.ActivePlotState.RequestCancellation();
    }
}
