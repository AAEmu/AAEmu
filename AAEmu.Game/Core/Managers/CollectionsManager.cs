using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Achievement;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Collections;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>What one discovery did with a character's collection state.</summary>
public enum CollectionDiscoveryResult
{
    /// <summary>The discovery itself was malformed (no character, no collection state, no entry id).</summary>
    Rejected,

    /// <summary>The item type is not part of any shipped collection or encyclopedia content.</summary>
    UnknownEntry,

    /// <summary>The entry was already discovered; nothing was persisted and no packet is owed for it.</summary>
    AlreadyKnown,

    /// <summary>The entry was discovered for the first time and its records were reported.</summary>
    Discovered,
}

/// <summary>
/// Owns the collection/encyclopedia domain: which entries a character has discovered, which content
/// records a discovery reports into, and the post-world-entry replay of the client's collection view.
/// </summary>
/// <remarks>
/// <para>
/// The client draws its collection tab from the achievement list — the discovery of an entry reports
/// the content records that watch that item type, progress and completion ride the achievement packets
/// that are already pushed to the player, and the reward is paid once, on the completion transition.
/// This manager therefore never invents a wire shape: it drives the packets the client speaks and
/// re-sends only the collection rows of that list during the world-entry replay.
/// </para>
/// <para>
/// Discovery before world entry reports into state without sending anything — the load-time replay is
/// what delivers those rows, so nothing reaches the player while they are still on the loading screen.
/// </para>
/// </remarks>
public class CollectionsManager : Singleton<CollectionsManager>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Records a discovery event for a character and reports the content records it feeds.
    /// </summary>
    /// <remarks>
    /// Reporting happens even for an already-discovered entry: each source reports its own records
    /// (obtaining, unpacking and equipping are separate content watches over the same item), and a
    /// record report keeps the high-water mark, so repeating an event cannot move anything twice.
    /// </remarks>
    public CollectionDiscoveryResult Discover(Character character, uint itemTypeId,
        CollectionDiscoverySource source)
    {
        if (character?.Collections == null || itemTypeId == 0)
        {
            Logger.Warn("Collections: rejected a malformed discovery (character {0}, item type {1})",
                character?.Name ?? "<null>", itemTypeId);
            return CollectionDiscoveryResult.Rejected;
        }

        var content = CollectionGameData.Instance;
        if (!content.IsKnownEntry(itemTypeId))
        {
            // Most item types in the game are not collection entries; a discovery of one is normal traffic.
            Logger.Trace("Collections: {0} discovered item type {1}, which no collection content watches",
                character.Name, itemTypeId);
            return CollectionDiscoveryResult.UnknownEntry;
        }

        var firstDiscovery = character.Collections.TryDiscover(itemTypeId);

        // World-entry state is queued, not pushed: the replay after NotifyInGameCompleted delivers it.
        var sendPackets = character.WorldEntryCompleted;
        foreach (var recordId in content.GetRecordsToReport(itemTypeId, source))
            AchievementManager.Instance.Report(character, recordId, 1, sendPackets);

        if (firstDiscovery)
        {
            Logger.Info("Collections: {0} discovered entry {1} ({2})",
                character.Name, itemTypeId, source);
            return CollectionDiscoveryResult.Discovered;
        }

        return CollectionDiscoveryResult.AlreadyKnown;
    }

    /// <summary>
    /// The collection rows of the character's achievement list — what the world-entry replay sends.
    /// </summary>
    public List<AchievementInfo> BuildInitialSyncRows(Character character)
    {
        var rows = new List<AchievementInfo>();
        if (character?.Collections == null)
            return rows;

        var content = CollectionGameData.Instance;
        foreach (var row in AchievementManager.Instance.BuildList(character))
        {
            if (content.IsCollectionAchievement(row.Id))
                rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Flushes the queued collection sync once the character has finished entering the world.
    /// </summary>
    /// <returns>How many packets were sent; a second call sends nothing.</returns>
    public int FlushInitialSync(Character character)
    {
        if (character?.Collections == null || !character.Collections.InitialSyncPending)
            return 0;

        var rows = BuildInitialSyncRows(character);
        var packets = 0;
        for (var offset = 0; offset < rows.Count; offset += AchievementManager.MaxEntriesPerPacket)
        {
            var count = Math.Min(AchievementManager.MaxEntriesPerPacket, rows.Count - offset);
            character.SendPacket(new SCAchievementsPacket(rows.GetRange(offset, count)));
            packets++;
        }

        character.Collections.MarkSynced();
        return packets;
    }
}
