using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Achievement;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Collections;
using AAEmu.Game.Models.Game.Items;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>What one discovery did with a character's collection state.</summary>
public enum CollectionDiscoveryResult
{
    /// <summary>The discovery itself was malformed (no character, no collection state, no entry id).</summary>
    Rejected,

    /// <summary>
    /// The character's records and achievements are not loaded yet, as while the character list restores
    /// items. Nothing was recorded; <see cref="CollectionsManager.BackfillHeldItems"/> replays held items at
    /// world entry.
    /// </summary>
    Deferred,

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
    /// <param name="itemGrade">The item's grade; records that ask for a grade only move when it meets theirs.</param>
    public CollectionDiscoveryResult Discover(Character character, uint itemTypeId, byte itemGrade,
        CollectionDiscoverySource source) =>
        Discover(character, itemTypeId, itemGrade, source, character?.WorldEntryCompleted ?? false);

    /// <summary>
    /// Replays discovery over every item the character holds. Items restored for the character list arrive
    /// before the character's records and achievements exist, so they are deferred, and world entry calls this
    /// once those have loaded.
    /// </summary>
    /// <remarks>
    /// Each item counts as the event its container implies (<see cref="SourceForContainer"/>). A worn item
    /// also counts as obtained: a grade reached while it stays equipped never enters a container, and an
    /// obtain watch has no equip record to move. Nothing is sent: the replay after world entry delivers the
    /// rows, and a report keeps the high-water mark, so running this again on a later world entry moves nothing.
    /// </remarks>
    /// <returns>How many entries were discovered for the first time.</returns>
    public int BackfillHeldItems(Character character)
    {
        if (character?.Inventory?._itemContainers == null)
            return 0;

        var held = new List<(Item Item, SlotType ContainerType)>();
        foreach (var container in character.Inventory._itemContainers.Values)
        {
            if (container == null || container.ContainerType is SlotType.None or SlotType.Mail or SlotType.Trade)
                continue;

            foreach (var item in container.Items.ToList())
                held.Add((item, container.ContainerType));
        }

        return BackfillItems(character, held);
    }

    /// <summary>
    /// The replay behind <see cref="BackfillHeldItems"/>: discovers each held item as the event its container
    /// implies, and a worn item as obtained as well, without sending anything.
    /// </summary>
    /// <returns>How many entries were discovered for the first time.</returns>
    public int BackfillItems(Character character, IEnumerable<(Item Item, SlotType ContainerType)> heldItems)
    {
        if (character == null || heldItems == null)
            return 0;

        var discovered = 0;
        foreach (var (item, containerType) in heldItems)
        {
            if (item == null)
                continue;

            // A worn piece is still an item the character obtained. Reporting only the equip event
            // leaves an obtain watch (there is no equip record for it) stuck at the grade it was put on.
            if (containerType == SlotType.Equipment &&
                Discover(character, item.TemplateId, item.Grade, CollectionDiscoverySource.Acquired, sendPackets: false) ==
                CollectionDiscoveryResult.Discovered)
                discovered++;

            if (Discover(character, item.TemplateId, item.Grade, SourceForContainer(containerType), sendPackets: false) ==
                CollectionDiscoveryResult.Discovered)
                discovered++;
        }

        return discovered;
    }

    /// <summary>The discovery event an item arriving in a container of this type counts as.</summary>
    public static CollectionDiscoverySource SourceForContainer(SlotType containerType) =>
        containerType == SlotType.Equipment ? CollectionDiscoverySource.Equipped : CollectionDiscoverySource.Acquired;

    /// <summary>
    /// Reports a grade or template change on an item the character already holds. The item did not
    /// enter a container, so the obtain watch is reported at the new template and grade, and a worn
    /// piece also reports the equip watch.
    /// </summary>
    public void DiscoverInPlaceChange(Character character, uint itemTypeId, byte itemGrade, bool equipped)
    {
        Discover(character, itemTypeId, itemGrade, CollectionDiscoverySource.Acquired);
        if (equipped)
            Discover(character, itemTypeId, itemGrade, CollectionDiscoverySource.Equipped);
    }

    private CollectionDiscoveryResult Discover(Character character, uint itemTypeId, byte itemGrade,
        CollectionDiscoverySource source, bool sendPackets)
    {
        if (character?.Collections == null || itemTypeId == 0)
        {
            Logger.Warn("Collections: rejected a malformed discovery (character {0}, item type {1})",
                character?.Name ?? "<null>", itemTypeId);
            return CollectionDiscoveryResult.Rejected;
        }

        if (character.Records == null || character.Achievements == null)
        {
            Logger.Trace("Collections: deferred item type {0} for {1} until its progress is loaded",
                itemTypeId, character.Name);
            return CollectionDiscoveryResult.Deferred;
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
        foreach (var recordId in content.GetRecordsToReport(itemTypeId, itemGrade, source))
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
