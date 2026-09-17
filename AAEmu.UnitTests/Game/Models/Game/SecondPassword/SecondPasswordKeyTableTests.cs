using AAEmu.Game.Models.Game.SecondPassword;

namespace AAEmu.UnitTests.Game.Models.Game.SecondPassword;

public class SecondPasswordKeyTableTests
{
    [Test]
    public async Task Build_ReturnsFourFullPermutationsOfTheAlphabet()
    {
        var tables = SecondPasswordKeyTable.Build(new Random(20260916).Next);

        await Assert.That(tables.Length).IsEqualTo(SecondPasswordKeyTable.TableCount);
        foreach (var table in tables)
        {
            await Assert.That(table.Length).IsEqualTo(SecondPasswordKeyTable.Alphabet.Length);
            await Assert.That(table.Distinct().Count()).IsEqualTo(SecondPasswordKeyTable.Alphabet.Length);
            await Assert.That(string.Concat(table.OrderBy(c => c)))
                .IsEqualTo(string.Concat(SecondPasswordKeyTable.Alphabet.OrderBy(c => c)));
        }
    }

    [Test]
    public async Task Build_ShufflesTheAlphabetRatherThanHandingItBackInOrder()
    {
        var tables = SecondPasswordKeyTable.Build(new Random(7).Next);

        await Assert.That(tables.Any(t => t != SecondPasswordKeyTable.Alphabet)).IsTrue();
    }

    [Test]
    public async Task Build_IsReproducibleForASeedAndDifferentAcrossSeeds()
    {
        var first = SecondPasswordKeyTable.Build(new Random(42).Next);
        var second = SecondPasswordKeyTable.Build(new Random(42).Next);
        var other = SecondPasswordKeyTable.Build(new Random(43).Next);

        await Assert.That(first).IsEquivalentTo(second);
        await Assert.That(first[0] == other[0] && first[1] == other[1] && first[2] == other[2] && first[3] == other[3])
            .IsFalse();
    }

    [Test]
    public async Task Decode_ReadsBackWhatEncodeWouldHaveThePlayerClick()
    {
        var table = SecondPasswordKeyTable.Build(new Random(99).Next)[2];
        const string password = "1aZ9";

        var clicked = SecondPasswordKeyTable.Encode(table, password);
        await Assert.That(clicked).IsNotNull();
        await Assert.That(SecondPasswordKeyTable.Decode(table, clicked)).IsEqualTo(password);
    }

    [Test]
    public async Task Decode_IsNotThePasswordItself()
    {
        // The whole point of the tables: the clicked positions do not spell the password.
        var table = SecondPasswordKeyTable.Build(new Random(1234).Next)[0];

        var clicked = SecondPasswordKeyTable.Encode(table, "0000");
        await Assert.That(clicked).IsNotEqualTo("0000");
        await Assert.That(SecondPasswordKeyTable.Decode(table, "0000")).IsEqualTo(new string(table[0], 4));
    }

    [Test]
    public async Task Decode_RefusesClicksTheTableCannotName()
    {
        var table = SecondPasswordKeyTable.Build(new Random(5).Next)[1];

        await Assert.That(SecondPasswordKeyTable.Decode(table, "!!")).IsNull();   // not a position
        await Assert.That(SecondPasswordKeyTable.Decode(table, null)).IsNull();
        await Assert.That(SecondPasswordKeyTable.Decode(string.Empty, "0")).IsNull();
        await Assert.That(SecondPasswordKeyTable.Decode(table, string.Empty)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Encode_RefusesCharactersTheTableDoesNotCarry()
    {
        var table = SecondPasswordKeyTable.Build(new Random(6).Next)[3];

        await Assert.That(SecondPasswordKeyTable.Encode(table, "ok!")).IsNull();
        await Assert.That(SecondPasswordKeyTable.Encode(table, "ok")).IsNotNull();
    }

    [Test]
    public async Task EveryTableOfOneSetDecodesItsOwnClicks()
    {
        var tables = SecondPasswordKeyTable.Build(new Random(2026).Next);
        const string password = "Pin42";

        for (var i = 0; i < tables.Length; i++)
        {
            var clicked = SecondPasswordKeyTable.Encode(tables[i], password);
            await Assert.That(SecondPasswordKeyTable.Decode(tables[i], clicked)).IsEqualTo(password);

            // A click read against another table of the same set must not give the password back.
            var other = tables[(i + 1) % tables.Length];
            await Assert.That(SecondPasswordKeyTable.Decode(other, clicked)).IsNotEqualTo(password);
        }
    }
}
