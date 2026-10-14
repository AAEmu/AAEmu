namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// How the socketing special effect (<c>value1</c>) splits install / destroy / salvage, and how
/// the extract tab names which sockets to pull.
/// </summary>
/// <remarks>
/// Shipped rows: install <c>value1 = 1</c>, remove (dawnstone wipe) <c>0</c>, extract/salvage
/// <c>2</c>. The extract tab sends SkillObject type 11: a zero-based socket index and an
/// <c>isAll</c> bool (extract-all still sends index 0 with <c>isAll</c> set).
/// </remarks>
public static class ItemSocketingRules
{
    public const int ModeInstall = 1;
    public const int ModeRemove = 0;
    public const int ModeExtract = 2;

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
