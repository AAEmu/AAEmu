using System.Collections.Concurrent;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Music;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class MusicManager(IMusicIdManager musicIdManager, IItemManager itemManager) : Singleton<MusicManager>, IMusicManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Where the songs are persisted. Production runs against the configured MySQL; the persistence
    /// tests point it at an isolated fixture so a save/relog round-trip can be exercised without a
    /// developer's database. Internal to keep the DI constructor as it is.
    /// </summary>
    internal Func<MySqlConnection> ConnectionFactory { get; set; } = MySQL.CreateConnection;

    private Dictionary<uint, SongData> _uploadQueue = []; // playerId, song
    private Dictionary<uint, SongData> _allSongs = []; // songId, song
    private readonly ConcurrentDictionary<uint, byte[]> _midiCache = new(); // playerId, midi data

    /// <summary>
    /// Longest score a player may save, taken from the shipped composition steps and bounded by
    /// the composition window's own buffer.
    /// </summary>
    public int MaxNoteBytes { get; private set; } = MusicNoteRules.DefaultMaxNoteBytes;

    public void Load()
    {
        _uploadQueue = [];
        _allSongs = [];
        _midiCache.Clear();

        LoadNoteLimit();

        using (var connection = ConnectionFactory())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM music";
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var songData = new SongData
                        {
                            Id = reader.GetUInt32("id"),
                            AuthorId = reader.GetUInt32("author"),
                            Title = reader.GetString("title"),
                            Song = reader.GetString("song")
                        };
                        _allSongs.Add(songData.Id, songData);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Reads the top composition step from the client data, so the save limit follows the shipped
    /// table instead of a magic number. That top step is the grandmaster one, so it is a ceiling
    /// for every player rather than the per-step limit the table describes; the step a player may
    /// actually use is gated by their composition actability, which the client applies itself.
    /// Any failure keeps the built-in default rather than blocking startup.
    /// </summary>
    private void LoadNoteLimit()
    {
        try
        {
            using var connection = SQLite.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(note_length) FROM music_note_limits";
            command.Prepare();
            var result = command.ExecuteScalar();
            if (result is null || result == DBNull.Value)
                return;

            var limit = Convert.ToInt32(result);
            if (limit > 0)
                MaxNoteBytes = Math.Min(limit, MusicNoteRules.MaxBufferedNoteBytes);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Could not read music_note_limits, using {0} as the score length limit", MaxNoteBytes);
        }
    }

    public bool Save(SongData songData)
    {
        // Every stored composition gets its own row: the sheets already written from an earlier one
        // keep pointing at it, so a later save must never take its id over.
        songData.Id = musicIdManager.GetNextId();

        using (var connection = ConnectionFactory())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "REPLACE INTO music (" +
                                      "`id`,`author`,`title`,`song` ) VALUES ( " +
                                      "@id, @author, @title, @song" +
                                      " )";
                command.Parameters.AddWithValue("@id", songData.Id);
                command.Parameters.AddWithValue("@author", songData.AuthorId);
                command.Parameters.AddWithValue("@title", songData.Title);
                command.Parameters.AddWithValue("@song", songData.Song);
                command.Prepare();
                // REPLACE reports two rows when it had to replace an existing id, so anything
                // below one is a real failure.
                if (command.ExecuteNonQuery() < 1)
                {
                    Logger.Warn("Error saving song to DB for {0} ({1})", songData.Title, songData.Id);
                    return false;
                }
            }
        }
        _allSongs[songData.Id] = songData;

        return true;
    }

    /// <summary>
    /// Queues the notes the composition window just saved, so the score item that is used next can
    /// be written from them. The notes are kept until they are replaced: the composition window
    /// only sends them again when the player actually changed something, and the same score may be
    /// written onto several blank scores in a row.
    /// </summary>
    public bool UploadSong(uint charId, string title, string song, ulong itemId)
    {
        if (!MusicNoteRules.IsUploadable(title, song, MaxNoteBytes, out var reason))
        {
            Logger.Warn("Player {0} tried to save music notes that were rejected: {1}", charId, reason);
            return false;
        }

        // The notes are written onto the score item the player is composing on, so that item has
        // to exist and to be theirs; the item id alone must never be enough.
        var sourceItem = itemManager.GetItemByItemId(itemId);
        if (sourceItem == null || sourceItem.OwnerId != charId)
        {
            Logger.Warn("Player {0} tried to save music notes for item {1} which they do not own", charId, itemId);
            return false;
        }

        if (!_uploadQueue.TryGetValue(charId, out var q))
        {
            q = new SongData();
            _uploadQueue.Add(charId, q);
        }
        q.AuthorId = charId;
        q.Title = title;
        q.Song = song;
        q.SourceItemId = itemId;
        return true;
    }

    public bool CreateSheetMusic(Character player, Item sourceItem)
    {
        // Check if a valid owned item
        if (sourceItem?._holdingContainer?.OwnerId != player.Id)
        {
            Logger.Warn("Player {0} ({1}) does not own the used source item", player.Name, player.Id);
            return false;
        }

        // Grab the related queued song (if any)
        if (!_uploadQueue.TryGetValue(player.Id, out var sud))
        {
            Logger.Warn("Player {0} ({1}) did not upload any music yet.", player.Name, player.Id);
            return false;
        }

        // The composition window only re-sends notes that actually changed, so a player may write
        // the same score onto several blank scores; the queued notes are not tied to one item.
        if (sud.SourceItemId != sourceItem.Id)
        {
            Logger.Trace("Player {0} ({1}) writes notes composed on item {2} onto item {3}",
                player.Name, player.Id, sud.SourceItemId, sourceItem.Id);
        }

        if (player.Inventory.Bag.FreeSlotCount < 1)
        {
            player.SendErrorMessage(ErrorMessageType.BagFull);
            Logger.Warn("Player {0} ({1}) did not have enough space to aquire new music sheet {2} ({3})",
                player.Name, player.Id, sud.Title, sud.Id);
            return false;
        }

        // Save to DB. A failed save must not spend the blank score: the notes stay queued and the
        // player can try again with the same item.
        if (!Save(sud))
        {
            Logger.Warn("Player {0} ({1}) could not store the notes of {2} ({3})",
                player.Name, player.Id, sud.Title, sud.Id);
            return false;
        }

        var sheet = (MusicSheetItem)itemManager.Create(Item.SheetMusic, 1, 0, true);
        sheet.OwnerId = player.Id;
        sheet.MadeUnitId = player.Id;
        sheet.SongId = sud.Id;

        // Add Sheet Music to inventory
        if (!player.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.SaveMusicNotes, sheet))
        {
            Logger.Warn("Player {0} ({1}) had a unknown error when adding Sheet Music to inventory {2} ({3})",
                player.Name, player.Id, sud.Title, sud.Id);
            return false;
        }

        // Consume Music Paper
        if (player.Inventory.Bag.ConsumeItem(ItemTaskType.SaveMusicNotes, sourceItem.TemplateId, 1, sourceItem) <= 0)
        {
            Logger.Warn("Failed to consume source item while creating music for Player {0} ({1}) item {2} ({3})",
                player.Name, player.Id, sourceItem.Id, sourceItem.Template.Name);
        }

        return true;
    }

    public SongData GetSongById(uint songId)
    {
        if (_allSongs.TryGetValue(songId, out var song))
            return song;
        return null;
    }

    /// <summary>Replaces a player's current performance block, refusing an absent or empty body.</summary>
    public bool CacheMidi(uint playerId, byte[] midiData)
    {
        if (midiData is not { Length: > 0 })
        {
            Logger.Warn("Refusing to cache an empty MIDI block for player {0}", playerId);
            ClearMidiCache(playerId);
            return false;
        }

        _midiCache[playerId] = midiData;
        return true;
    }

    /// <summary>Returns only a non-empty performance block.</summary>
    public bool TryGetMidiCache(uint playerId, out byte[] midiData)
    {
        if (_midiCache.TryGetValue(playerId, out var data) && data is { Length: > 0 })
        {
            midiData = data;
            return true;
        }

        midiData = null;
        return false;
    }

    /// <summary>Drops a player's current performance block so it cannot be replayed later.</summary>
    public bool ClearMidiCache(uint playerId) => _midiCache.TryRemove(playerId, out _);

    /// <summary>Clears session-owned music state when a character leaves the world.</summary>
    public void OnCharacterLogout(BaseUnit player)
    {
        if (player != null)
            ClearMidiCache(player.Id);
    }

    private readonly Dictionary<uint, EnsembleSession> _ensembles = []; // maestro bc, session

    /// <summary>
    /// Asks one player to join the maestro's ensemble, opening it if they are not leading one yet. The
    /// suggestive skill runs this once per player it reached, so the session is kept rather than remade.
    /// </summary>
    public EnsembleSession SuggestEnsembleTo(Character maestro, Character target)
    {
        if (maestro == null || target == null)
            return null;

        if (!_ensembles.TryGetValue(maestro.ObjId, out var session) || !session.IsOpen)
        {
            session = new EnsembleSession(maestro.ObjId, maestro.Name);
            _ensembles[maestro.ObjId] = session;
        }

        if (!session.Invite(target.ObjId))
            return session;

        target.SendPacket(new SCEnsembleSuggestedPacket(maestro.ObjId));
        Logger.Info("Ensemble: {0} asked {1} to join ({2} asked so far)",
            maestro.Name, target.Name, session.Invited.Count);
        return session;
    }

    /// <summary>The ensemble a player is part of, whether they lead it, joined it or were only asked.</summary>
    public EnsembleSession FindEnsemble(uint bc)
    {
        PruneEnsembles();

        foreach (var session in _ensembles.Values)
        {
            if (session.Involves(bc))
                return session;
        }

        return null;
    }

    private EnsembleSession FindEnsemble(uint memberBc, uint maestroBc)
    {
        PruneEnsembles();
        return _ensembles.TryGetValue(maestroBc, out var session) && session.Involves(memberBc)
            ? session
            : null;
    }

    /// <summary>Takes an invitation up and tells the ensemble who is in it now.</summary>
    public EnsembleJoinResult AcceptEnsemble(Character member)
    {
        if (member == null)
            return EnsembleJoinResult.NotMember;

        var session = FindEnsemble(member.ObjId);
        if (session == null)
            return EnsembleJoinResult.NotMember;

        var result = session.Accept(member.ObjId);
        if (result != EnsembleJoinResult.Accepted)
            return result;

        Logger.Info("Ensemble: {0} joined {1}'s ensemble ({2} member(s))",
            member.Name, session.MaestroName, session.Members.Count);
        SendToParticipants(session,
            new SCEnsembleStartedPacket(session.MaestroBc, session.MaestroName, session.Members));
        return result;
    }

    /// <summary>Turns an invitation down and tells the maestro.</summary>
    public EnsembleJoinResult RejectEnsemble(Character member)
    {
        if (member == null)
            return EnsembleJoinResult.NotMember;

        var session = FindEnsemble(member.ObjId);
        if (session == null)
            return EnsembleJoinResult.NotMember;

        var result = session.Reject(member.ObjId);
        if (result != EnsembleJoinResult.Rejected)
            return result;

        Logger.Info("Ensemble: {0} turned down {1}'s ensemble", member.Name, session.MaestroName);
        WorldManager.Instance.GetCharacterByObjId(session.MaestroBc)
            ?.SendPacket(new SCEnsembleRejectPacket(member.ObjId));
        return result;
    }

    /// <summary>
    /// A member's part arrived: the maestro is handed it. The sender, maestro, and byte count are
    /// checked before the session is changed, so a malformed request cannot reserve a member's part.
    /// </summary>
    public bool EnsemblePartReady(Character member, uint claimedMemberBc, uint claimedMaestroBc,
        uint claimedSize, string data)
    {
        if (member == null || string.IsNullOrEmpty(data) || claimedMemberBc != member.ObjId)
            return false;

        var session = FindEnsemble(member.ObjId, claimedMaestroBc);
        if (session == null || !session.IsMember(member.ObjId))
            return false;

        var size = (uint)System.Text.Encoding.UTF8.GetByteCount(data);
        if (claimedSize != size || !session.PartReady(member.ObjId))
            return false;

        WorldManager.Instance.GetCharacterByObjId(session.MaestroBc)
            ?.SendPacket(new SCEnsembleMidiBinReadyPacket(member.ObjId, session.MaestroBc, size, data));

        if (session.AllPartsReady)
            Logger.Info("Ensemble: {0}'s ensemble has every part in and can play", session.MaestroName);

        return true;
    }

    /// <summary>
    /// Starts the performance the maestro is leading, once every member's part is in. Returns false when
    /// they lead nothing, are not the maestro, or somebody has not sent a part yet.
    /// </summary>
    public bool TryStartEnsemble(Character maestro)
    {
        if (maestro == null)
            return false;

        var session = FindEnsemble(maestro.ObjId);
        if (session == null || session.MaestroBc != maestro.ObjId)
            return false;

        if (!session.Start())
            return false;

        Logger.Info("Ensemble: {0}'s ensemble starts to perform with {1} member(s)",
            session.MaestroName, session.Members.Count);
        SendToParticipants(session, new SCStartToPerformAnEnsemblePacket(session.MaestroBc));
        return true;
    }

    /// <summary>Ends the ensemble a player is part of, whether they lead it or are leaving it.</summary>
    public void CancelEnsemble(Character who)
    {
        if (who == null)
            return;

        var session = FindEnsemble(who.ObjId);
        if (session == null)
            return;

        LeaveEnsemble(who);
    }

    /// <summary>
    /// Takes one player out of their ensemble. Losing the maestro ends it for everyone; losing a member
    /// only tells the rest to drop that member's part, including after the performance has started.
    /// </summary>
    public void LeaveEnsemble(Character who)
    {
        if (who == null)
            return;

        var session = FindEnsemble(who.ObjId);
        if (session == null)
            return;

        var participants = session.Participants();
        var wasMaestro = who.ObjId == session.MaestroBc;
        if (!session.Leave(who.ObjId))
            return;

        if (wasMaestro)
        {
            Logger.Info("Ensemble: {0}'s ensemble ended because {1} left", session.MaestroName, who.Name);
            _ensembles.Remove(session.MaestroBc);
            foreach (var participant in participants)
            {
                WorldManager.Instance.GetCharacterByObjId(participant)
                    ?.SendPacket(new SCEnsembleCanceledPacket());
            }

            return;
        }

        SendToParticipants(session, new SCDeleteEnsembleSoundPacket(who.ObjId));
        if (session.IsOpen)
        {
            SendToParticipants(session,
                new SCEnsembleStartedPacket(session.MaestroBc, session.MaestroName, session.Members));
        }
    }

    /// <summary>
    /// A disconnected performance has no resumable session left to own. Remove it immediately and
    /// close it for everyone still online; open sessions keep the ordinary member/maestro leave rules.
    /// </summary>
    public void OnCharacterLogout(Character character)
    {
        if (character == null)
            return;

        var session = FindEnsemble(character.ObjId);
        if (session == null)
            return;

        if (!session.IsStarted || !session.IsMember(character.ObjId))
        {
            LeaveEnsemble(character);
            return;
        }

        var participants = session.Participants();
        _ensembles.Remove(session.MaestroBc);
        session.Cancel();
        Logger.Info("Ensemble: {0}'s started ensemble was cleaned up because {1} disconnected",
            session.MaestroName, character.Name);

        foreach (var participant in participants)
        {
            if (participant == character.ObjId)
                continue;

            WorldManager.Instance.GetCharacterByObjId(participant)
                ?.SendPacket(new SCEnsembleCanceledPacket());
        }
    }

    private static void SendToParticipants(EnsembleSession session, GamePacket packet)
    {
        foreach (var participant in session.Participants())
            WorldManager.Instance.GetCharacterByObjId(participant)?.SendPacket(packet);
    }

    /// <summary>
    /// Drops ensembles whose maestro is no longer in the world — a session is only as long-lived as the
    /// player leading it, and nothing else tells us they logged out.
    /// </summary>
    private void PruneEnsembles()
    {
        if (_ensembles.Count == 0)
            return;

        List<uint> gone = null;
        foreach (var (maestroBc, session) in _ensembles)
        {
            if (session.IsOpen && WorldManager.Instance.GetCharacterByObjId(maestroBc) == null)
                (gone ??= []).Add(maestroBc);
        }

        if (gone == null)
            return;

        foreach (var maestroBc in gone)
            _ensembles.Remove(maestroBc);
    }

    /// <summary>
    /// Ends a performance: nearby clients stop the sound and the play buffs that hold the playing
    /// pose are dropped. Reached both from the client's own report that the performance is over and
    /// from the "Close the Score" skill, so it has to run twice without harm.
    /// </summary>
    public static void EndPerformance(BaseUnit player)
    {
        if (player == null)
            return;

        MusicManager.Instance.ClearMidiCache(player.Id);
        player.BroadcastPacket(new SCPauseUserMusicPacket(player.ObjId), true);

        var buffs = player.Buffs;
        if (buffs == null)
            return;

        // 1155 = Play Song: the instrument play buffs and the memorized score alike. An unseeded
        // skill table answers with null, which must still end the performance cleanly.
        foreach (var buff in SkillManager.Instance.GetBuffsByTagId((uint)TagsEnum.PlaySong) ?? [])
        {
            if (buffs.CheckBuff(buff))
                buffs.RemoveBuff(buff);
        }
    }
}
