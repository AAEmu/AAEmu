using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Music;

namespace AAEmu.UnitTests.Game.Models.Game.Music;

public class MusicNoteRulesTests
{
    [Test]
    public async Task IsUploadable_AcceptsANormalNote()
    {
        await Assert.That(MusicNoteRules.IsUploadable("My Song", "c1 d2 e3", 5000, out var reason)).IsTrue();
        await Assert.That(reason).IsNull();
    }

    [Test]
    public async Task IsUploadable_RejectsAnEmptyTitleOrScore()
    {
        await Assert.That(MusicNoteRules.IsUploadable("", "c1", 5000, out _)).IsFalse();
        await Assert.That(MusicNoteRules.IsUploadable("My Song", "", 5000, out _)).IsFalse();
    }

    [Test]
    public async Task IsUploadable_MeasuresTheTitleInBytes()
    {
        var fits = new string('♪', 32); // 96 bytes exactly
        var tooLong = new string('♪', 33); // 99 bytes

        await Assert.That(MusicNoteRules.IsUploadable(fits, "c1", 5000, out _)).IsTrue();
        await Assert.That(MusicNoteRules.IsUploadable(tooLong, "c1", 5000, out var reason)).IsFalse();
        await Assert.That(reason).Contains("96");
    }

    [Test]
    public async Task IsUploadable_RejectsAScoreLongerThanTheCompositionLimit()
    {
        var score = new string('a', 5001);

        await Assert.That(MusicNoteRules.IsUploadable("My Song", score, 5000, out var reason)).IsFalse();
        await Assert.That(reason).Contains("5000");
        await Assert.That(MusicNoteRules.IsUploadable("My Song", new string('a', 5000), 5000, out _)).IsTrue();
    }

    [Test]
    public async Task IsUploadable_BoundsTheLimitByTheClientBuffer()
    {
        // A data change may claim a much larger score than the composition window can hold.
        await Assert.That(MusicNoteRules.IsUploadable("My Song", new string('a', 20001), 999999, out var reason))
            .IsFalse();
        await Assert.That(reason).Contains("20000");
    }

    [Test]
    public async Task IsUploadable_FallsBackToTheShippedLimitForUnknownData()
    {
        await Assert.That(MusicNoteRules.IsUploadable("My Song", new string('a', 5000), 0, out _)).IsTrue();
        await Assert.That(MusicNoteRules.IsUploadable("My Song", new string('a', 5001), 0, out _)).IsFalse();
    }

    [Test]
    public async Task TryGetNoteSong_ResolvesAWrittenScoreOfTheRequester()
    {
        var sheet = new MusicSheetItem(1, new ItemTemplate { Id = 28051 }, 1) { OwnerId = 42, SongId = 777 };

        await Assert.That(MusicNoteRules.TryGetNoteSong(sheet, 42, out var songId)).IsTrue();
        await Assert.That(songId).IsEqualTo(777u);
    }

    [Test]
    public async Task TryGetNoteSong_RefusesSomebodyElsesScore()
    {
        var sheet = new MusicSheetItem(1, new ItemTemplate { Id = 28051 }, 1) { OwnerId = 42, SongId = 777 };

        await Assert.That(MusicNoteRules.TryGetNoteSong(sheet, 43, out var songId)).IsFalse();
        await Assert.That(songId).IsEqualTo(0u);
    }

    [Test]
    public async Task TryGetNoteSong_RefusesABlankScoreAndAPlainItem()
    {
        var blank = new MusicSheetItem(1, new ItemTemplate { Id = 28051 }, 1) { OwnerId = 42 };
        var plain = new Item(2, new ItemTemplate { Id = 29656 }, 1) { OwnerId = 42 };

        await Assert.That(MusicNoteRules.TryGetNoteSong(blank, 42, out _)).IsFalse();
        await Assert.That(MusicNoteRules.TryGetNoteSong(plain, 42, out _)).IsFalse();
        await Assert.That(MusicNoteRules.TryGetNoteSong(null, 42, out _)).IsFalse();
    }

    [Test]
    public async Task HoldsNote_MatchesAWrittenScoreOfTheRequester()
    {
        var sheet = new MusicSheetItem(1, new ItemTemplate { Id = 28051 }, 1) { OwnerId = 42, SongId = 777 };
        var blank = new MusicSheetItem(2, new ItemTemplate { Id = 28051 }, 1) { OwnerId = 42 };
        var other = new MusicSheetItem(3, new ItemTemplate { Id = 28051 }, 1) { OwnerId = 43, SongId = 778 };
        var held = new List<Item> { blank, other, sheet };

        await Assert.That(MusicNoteRules.HoldsNote(held, 42, 777)).IsTrue();
        await Assert.That(MusicNoteRules.HoldsNote(held, 42, 778)).IsFalse(); // somebody else's score
        await Assert.That(MusicNoteRules.HoldsNote(held, 43, 778)).IsTrue();
        await Assert.That(MusicNoteRules.HoldsNote(held, 42, 0)).IsFalse();
        await Assert.That(MusicNoteRules.HoldsNote(null, 42, 777)).IsFalse();
    }

    [Test]
    public async Task ClampToBytes_KeepsWholeCharacters()
    {
        await Assert.That(MusicNoteRules.ClampToBytes(new string('♪', 40), 96)).IsEqualTo(new string('♪', 32));
        await Assert.That(MusicNoteRules.ClampToBytes("short", 96)).IsEqualTo("short");
        await Assert.That(MusicNoteRules.ClampToBytes("abc", 0)).IsEqualTo(string.Empty);
        await Assert.That(MusicNoteRules.ClampToBytes(null, 96)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ClampToBytes_NeverSplitsASurrogatePair()
    {
        // 93 bytes of ASCII leave room for three of the emoji's four bytes: the whole emoji has to
        // go, and a lone half pair must never reach the wire as a replacement character.
        var value = new string('a', 93) + char.ConvertFromUtf32(0x1F642);

        var clamped = MusicNoteRules.ClampToBytes(value, 96);

        await Assert.That(clamped).IsEqualTo(new string('a', 93));
        await Assert.That(clamped.Length).IsEqualTo(93);
    }

    [Test]
    public async Task ClampToBytes_KeepsAScalarThatFitsExactly()
    {
        var value = new string('a', 92) + char.ConvertFromUtf32(0x1F642); // 96 bytes exactly

        var clamped = MusicNoteRules.ClampToBytes(value, 96);

        await Assert.That(clamped).IsEqualTo(value);
        await Assert.That(System.Text.Encoding.UTF8.GetByteCount(clamped)).IsEqualTo(96);
    }
}
