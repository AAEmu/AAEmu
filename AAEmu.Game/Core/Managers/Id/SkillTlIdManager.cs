using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Core.Managers.Id;

public static class SkillTlIdManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private const ushort FirstId = 0x000A;
    private const ushort LastId = 0xFFFE; // Must be less than ushort.MaxValue
    private const ushort MaxEntries = LastId - FirstId;
    private static bool[] AssignedTl { get; } = new bool[ushort.MaxValue];
    private static BaseUnit[] AssignedUnit { get; } = new BaseUnit[ushort.MaxValue];
    private static ushort NextIdToUse { get; set; }= FirstId;
    private static ulong TotalGets { get; set; }
    private static ulong TotalReleases { get; set; }
    private static readonly object s_lock = new ();

    public static ushort GetNextId(BaseUnit referenceUnit = null)
    {
        ushort res = 0;
        lock (s_lock)
        {
            TotalGets++;
            for (var i = 0; i <= MaxEntries; i++)
            {
                if (!AssignedTl[NextIdToUse])
                {
                    AssignedTl[NextIdToUse] = true;
                    AssignedUnit[NextIdToUse] = referenceUnit;

                    // Print some debugging info every 500 completed requests
                    // This can be removed later if confirmed it works fine on very long periods of uptime
                    //if (TotalGets % 500 == 0)
                    //    Logger.Debug(ReportStatus());

                    res = NextIdToUse;
                }

                NextIdToUse++;
                if (NextIdToUse > LastId)
                    NextIdToUse = FirstId;
                if (res != 0)
                    break;
            }
        }

        if (res == 0)
            Logger.Fatal($"No more Ids left to use with a total Gets of {TotalGets} and Release of {TotalReleases} !!!");
        return res;
    }

    public static void ReleaseId(ushort tlId)
    {
        if (tlId == 0)
            return;
        lock (s_lock)
        {
            TotalReleases++;
            if (!AssignedTl[tlId])
            {
                var refUnit = AssignedUnit[tlId];
                if (refUnit is Npc npc)
                {
                    Logger.Warn($"Tried to release unused Id {tlId} by NPC {npc.ObjId}, Template {npc.TemplateId} - {npc.Transform}");
                }
                else if (refUnit is Character player)
                {
                    Logger.Warn($"Tried to release unused Id {tlId} by Character {player.Name}");
                }
                else if (refUnit != null)
                {
                    Logger.Warn($"Tried to release unused Id {tlId} by {refUnit.ObjId} {refUnit.Name} - {refUnit.Transform}");
                }
                else
                {
                    Logger.Warn($"Tried to release unused Id {tlId} that was never referenced before");
                }
            }

            AssignedTl[tlId] = false;
            // AssignedUnit[tlId] = null;
        }
    }

    private static ushort CountFreeIds()
    {
        // Note, don't lock() here as this is only ever called within a lock already
        ushort count = 0;
        for(var i = FirstId; i <= LastId;i++)
            if (!AssignedTl[i])
                count++;
        return count;
    }

    public static string ReportStatus()
    {
        return $"{CountFreeIds()}/{MaxEntries} free TlId slots remaining with a total of: Gets {TotalGets}, Releases {TotalReleases}";
    }
}
