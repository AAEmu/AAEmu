namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// How the socketing special effect (<c>value1</c>) splits install / destroy / salvage, and how
/// the extract tab names which sockets to pull.
/// </summary>
/// <remarks>
/// Shipped rows: install <c>value1 = 1</c>, remove (dawnstone wipe) <c>0</c>, extract/salvage
/// <c>2</c>. The extract tab sends SkillObject type 11: a zero-based socket index and an
/// <c>isAll</c> bool only when true (a single-socket cast is the index alone).
/// </remarks>
public static class ItemSocketingRules
{
    public const int ModeInstall = 1;
    public const int ModeRemove = 0;
    public const int ModeExtract = 2;

    public static int RequestedInstallAttempts(Skills.SkillObject skillObject)
    {
        if (skillObject is not Skills.SkillObjectExtraValues extras || extras.Values.Length == 0)
            return 1;
        // isAll u8, then an unaligned u32 count. Never truncate a bad count to a byte.
        var count = (uint)extras.Values[0] >> 8;
        if (extras.Values.Length > 1)
            count |= (uint)extras.Values[1] << 24;
        return count > int.MaxValue ? -1 : (int)count;
    }

    public static ErrorMessageType? PlanInstallCosts(int attempts, int availableGems, int usedSockets,
        int socketLimit, Func<uint, long> price, out long[] costs, out long total)
    {
        costs = [];
        total = 0;
        if (attempts < 1 || usedSockets < 0 || socketLimit > EquipItem.SocketSlots ||
            attempts > socketLimit - usedSockets)
            return ErrorMessageType.ItemCannotUse;
        if (availableGems < attempts)
            return ErrorMessageType.NotEnoughItem;
        try
        {
            costs = new long[attempts];
            for (var i = 0; i < attempts; i++)
            {
                var cost = price((uint)(usedSockets + i));
                if (cost < 0)
                    return ErrorMessageType.ItemCannotUse;
                costs[i] = cost;
                total = checked(total + cost);
            }
            return null;
        }
        catch (OverflowException)
        {
            return ErrorMessageType.ItemCannotUse;
        }
    }

    /// <summary>
    /// Extract (and other socketing) stones must grant the cast skill. A BindOnPickup item that
    /// starts a mismatched skill must not pay the stone cost.
    /// </summary>
    public static bool IsStoneForSkill(uint itemUseSkillId, uint castSkillId) =>
        itemUseSkillId != 0 && castSkillId != 0 && itemUseSkillId == castSkillId;

    /// <summary>
    /// Whether the bag can take every returned gem type together. Per-template
    /// <c>SpaceLeftForItem</c> double-counts the same empty slots, so extract-all could clear
    /// the sockets and then fail halfway through the returns.
    /// </summary>
    /// <param name="returns">Template id → count to add.</param>
    /// <param name="stackRoom">Units that already fit into existing stacks of that template.</param>
    /// <param name="maxStack">Max stack size for that template (at least 1).</param>
    /// <param name="freeSlots">Empty bag slots still available.</param>
    public static bool CanFitExtractReturns(
        IReadOnlyDictionary<uint, int> returns,
        Func<uint, int> stackRoom,
        Func<uint, int> maxStack,
        int freeSlots)
    {
        if (returns == null || returns.Count == 0)
            return true;
        if (stackRoom == null || maxStack == null)
            return false;

        var free = freeSlots;
        foreach (var (templateId, count) in returns)
        {
            if (count <= 0)
                continue;

            var roomInStacks = Math.Max(0, stackRoom(templateId));
            var remaining = count - Math.Min(count, roomInStacks);
            if (remaining <= 0)
                continue;

            var stack = Math.Max(1, maxStack(templateId));
            var slotsNeeded = (remaining + stack - 1) / stack;
            free -= slotsNeeded;
            if (free < 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Which filled sockets the extract cast asked for, in socket order. Empty when the cast
    /// names nothing that can be pulled.
    /// </summary>
    public static IReadOnlyList<int> ResolveExtractSlots(IReadOnlyList<uint> gemIds, int index,
        bool isAll)
    {
        if (gemIds == null || gemIds.Count == 0)
            return Array.Empty<int>();

        if (isAll)
        {
            var all = new List<int>();
            for (var i = 0; i < gemIds.Count; i++)
            {
                if (gemIds[i] != 0)
                    all.Add(i);
            }

            return all;
        }

        if (index < 0 || index >= gemIds.Count || gemIds[index] == 0)
            return Array.Empty<int>();

        return [index];
    }

    /// <summary>
    /// Clears the named sockets and packs the remaining gems into the front of the run so the
    /// next install still lands on <c>GemIds[filledCount]</c>.
    /// </summary>
    public static uint[] CompactAfterClear(IReadOnlyList<uint> gemIds, IReadOnlyList<int> clearIndexes)
    {
        var clear = new HashSet<int>(clearIndexes ?? Array.Empty<int>());
        var kept = new List<uint>(gemIds.Count);
        for (var i = 0; i < gemIds.Count; i++)
        {
            if (clear.Contains(i) || gemIds[i] == 0)
                continue;
            kept.Add(gemIds[i]);
        }

        var result = new uint[gemIds.Count];
        for (var i = 0; i < kept.Count; i++)
            result[i] = kept[i];
        return result;
    }
}
