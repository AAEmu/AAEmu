using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Music;

using Moq;

using MySql.Data.MySqlClient;

using Xunit;

namespace AAEmu.IntegrationTests;

/// <summary>
/// The score a player writes has to survive the server they wrote it on: save, then a fresh
/// manager as a relog builds one, then the request resolution the score window runs against the
/// sheet item the player holds.
/// </summary>
public sealed class MusicPersistenceIntegrationTests(MusicMySqlFixture fixture) : IClassFixture<MusicMySqlFixture>
{
    [Fact]
    public async Task Save_ThenRelog_ThenRequest_ServesThePersistedScore()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_MUSIC_TEST_MYSQL to run the isolated MySQL fixture.");

        const uint songId = 501;
        const uint authorId = 7;
        // Ids come out in order, as the shipped MusicIdManager hands them out.
        var nextId = songId - 1;
        var ids = new Mock<IMusicIdManager>();
        ids.Setup(manager => manager.GetNextId()).Returns(() => ++nextId);
        var items = new Mock<IItemManager>();

        // Save: the composition window's upload, written to the music table.
        var beforeRelog = NewManager(fixture.ConnectionString, ids, items);
        var written = new SongData { AuthorId = authorId, Title = "Round Trip", Song = "c1 d2" };
        Assert.True(beforeRelog.Save(written));
        Assert.Equal(songId, written.Id);

        // Relog: a fresh manager reads the same table back, as a server start does.
        var afterRelog = NewManager(fixture.ConnectionString, ids, items);
        afterRelog.Load();
        var loaded = afterRelog.GetSongById(songId);
        Assert.NotNull(loaded);
        Assert.Equal("Round Trip", loaded.Title);
        Assert.Equal("c1 d2", loaded.Song);
        Assert.Equal(authorId, loaded.AuthorId);

        // Request: the resolution CSRequestMusicNotesPacket runs — a written score the requester
        // holds answers with the stored song, and the same sheet answers nobody else.
        var sheet = new MusicSheetItem(9001, new ItemTemplate { Id = 27902 }, 1)
        {
            OwnerId = authorId,
            SongId = songId,
        };
        Assert.True(MusicNoteRules.TryGetNoteSong(sheet, authorId, out var requestedId));
        Assert.Equal(songId, requestedId);
        Assert.Same(loaded, afterRelog.GetSongById(requestedId));
        Assert.False(MusicNoteRules.TryGetNoteSong(sheet, authorId + 1, out _));
        Assert.False(MusicNoteRules.HoldsNote([sheet], authorId + 1, songId));

        // A second save takes a new id, so the sheet the first one wrote keeps its song.
        var second = new SongData { AuthorId = authorId, Title = "Second", Song = "d3" };
        Assert.True(afterRelog.Save(second));
        Assert.NotEqual(songId, second.Id);
        Assert.Equal("Round Trip", afterRelog.GetSongById(songId).Title);
    }

    /// <summary>Mirrors <c>MySQL.CreateConnection</c>: the factory hands over an open connection.</summary>
    private static MusicManager NewManager(string connectionString, Mock<IMusicIdManager> ids,
        Mock<IItemManager> items) =>
        new(ids.Object, items.Object)
        {
            ConnectionFactory = () =>
            {
                var connection = new MySqlConnection(connectionString);
                connection.Open();
                return connection;
            },
        };
}
