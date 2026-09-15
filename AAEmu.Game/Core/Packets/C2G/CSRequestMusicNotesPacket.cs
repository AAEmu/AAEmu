using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Music;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks for the notes of a score item: what the score window shows when it is opened, and what the
/// item tooltip shows when a written score is hovered.
/// </summary>
/// <remarks>
/// The id in the request is the score item while the composition window is open and the song the
/// written score carries once there is one to read, so both are resolved here — and only for a
/// score the player actually holds. <c>isTooltip</c> tells the two requests apart (a hovered
/// tooltip stays silent about the memorized-score state), and <c>invenType</c> is the container the
/// client is showing the item from and is echoed back so the client can refresh the right window.
/// </remarks>
public class CSRequestMusicNotesPacket() : GamePacket(CSOffsets.CSRequestMusicNotesPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var noteId = stream.ReadUInt64(); // the score item, or the song a written score carries
        var isTooltip = stream.ReadBoolean(); // requested for a tooltip, not for the score window
        var showBuffOnly = stream.ReadBoolean(); // a refresh of notes that are already being played
        var invenType = stream.ReadByte(); // container the client is showing the item from

        var player = Connection.ActiveChar;
        var scoreItem = ItemManager.Instance.GetItemByItemId(noteId);
        var song = MusicNoteRules.TryGetNoteSong(scoreItem, player.Id, out var songId)
            ? MusicManager.Instance.GetSongById(songId)
            : null;

        if (song == null && MusicNoteRules.HoldsNote(HeldItems(player), player.Id, (uint)noteId))
            song = MusicManager.Instance.GetSongById((uint)noteId);

        if (song == null)
        {
            // Answer for an item of the player's that simply carries no notes (a blank score, or one
            // whose score is gone). A request for an item they do not own is not answered at all.
            if (scoreItem != null && scoreItem.OwnerId == player.Id)
            {
                player.SendPacket(new SCUserNoteLoadedPacket((uint)noteId, isTooltip, (sbyte)invenType,
                    string.Empty, string.Empty));
            }
            else
            {
                Logger.Warn("Player {0} ({1}) requested music notes of {2}, which they do not hold",
                    player.Name, player.Id, noteId);
            }

            return;
        }

        player.SendPacket(new SCUserNoteLoadedPacket((uint)noteId, isTooltip, (sbyte)invenType,
            song.Title, song.Song));

        if (showBuffOnly)
            player.Buffs.AddBuff((uint)BuffConstants.ScoreMemorized, player); // Score Memorized
    }

    /// <summary>
    /// A snapshot of the items the player is known to hold, bag, warehouse and equipment alike.
    /// Taken under the inventory's mutation monitor: items move while a request is handled, and
    /// walking the live lists would either throw or answer from a half-moved inventory.
    /// </summary>
    private static List<Item> HeldItems(Character player)
    {
        var inventory = player.Inventory;
        if (inventory == null)
            return [];

        lock (inventory.MutationSyncRoot)
        {
            var held = new List<Item>();
            foreach (var container in new[] { inventory.Bag, inventory.Warehouse, inventory.Equipment })
            {
                if (container?.Items != null)
                    held.AddRange(container.Items);
            }

            return held;
        }
    }
}
