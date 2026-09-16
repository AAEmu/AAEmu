using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.SecondPassword;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class SecondPasswordManagerTests
{
    private static uint NextAccount() => (uint)Random.Shared.Next(1_000_000, 9_000_000);

    [Test]
    public async Task Secret_HashesAndVerifiesWithoutStoringThePassword()
    {
        var salt = SecondPasswordSecret.NewSalt();
        var hash = SecondPasswordSecret.Hash("1234", salt);

        await Assert.That(hash).IsNotEqualTo("1234");
        await Assert.That(hash).DoesNotContain("1234");
        await Assert.That(SecondPasswordSecret.Verify("1234", salt, hash)).IsTrue();
        await Assert.That(SecondPasswordSecret.Verify("1235", salt, hash)).IsFalse();
    }

    [Test]
    public async Task Secret_RefusesWhatItCannotRead()
    {
        var salt = SecondPasswordSecret.NewSalt();

        await Assert.That(SecondPasswordSecret.Verify("1234", salt, "not base64 !!")).IsFalse();
        await Assert.That(SecondPasswordSecret.Verify("1234", salt, string.Empty)).IsFalse();
        await Assert.That(SecondPasswordSecret.Verify(null, salt, "AAAA")).IsFalse();
        await Assert.That(SecondPasswordSecret.Verify("1234", null, "AAAA")).IsFalse();
    }

    [Test]
    public async Task Secret_UsesAFreshSaltEveryTime()
    {
        var first = SecondPasswordSecret.NewSalt();
        var second = SecondPasswordSecret.NewSalt();

        await Assert.That(first).IsNotEquivalentTo(second);
        // the same password under two salts must not produce the same hash
        await Assert.That(SecondPasswordSecret.Hash("same", first))
            .IsNotEqualTo(SecondPasswordSecret.Hash("same", second));
    }

    [Test]
    public async Task Create_SetsThePasswordOnce()
    {
        var id = NextAccount();
        var manager = SecondPasswordManager.Instance;

        await Assert.That(manager.HasPassword(id)).IsFalse();
        await Assert.That(manager.TryCreate(id, "4321")).IsTrue();
        await Assert.That(manager.HasPassword(id)).IsTrue();
        // a second create must not overwrite the first
        await Assert.That(manager.TryCreate(id, "9999")).IsFalse();
        await Assert.That(manager.Verify(id, "4321", out _)).IsTrue();
        manager.TryClear(id, "4321", out _);
    }

    [Test]
    public async Task Create_RefusesAnEmptyPassword()
    {
        var id = NextAccount();
        var manager = SecondPasswordManager.Instance;

        await Assert.That(manager.TryCreate(id, string.Empty)).IsFalse();
        await Assert.That(manager.TryCreate(id, null)).IsFalse();
        await Assert.That(manager.HasPassword(id)).IsFalse();
    }

    [Test]
    public async Task Verify_CountsWrongAnswersAndResetsOnSuccess()
    {
        var id = NextAccount();
        var manager = SecondPasswordManager.Instance;
        manager.TryCreate(id, "1111");

        await Assert.That(manager.Verify(id, "2222", out var first)).IsFalse();
        await Assert.That(first).IsEqualTo(1);
        await Assert.That(manager.Verify(id, "3333", out var second)).IsFalse();
        await Assert.That(second).IsEqualTo(2);

        await Assert.That(manager.Verify(id, "1111", out var afterSuccess)).IsTrue();
        await Assert.That(afterSuccess).IsEqualTo(0);
        // and the counter starts over after a success
        await Assert.That(manager.Verify(id, "4444", out var again)).IsFalse();
        await Assert.That(again).IsEqualTo(1);
        manager.TryClear(id, "1111", out _);
    }

    [Test]
    public async Task Verify_IsFalseForAnAccountWithoutAPassword()
    {
        var id = NextAccount();
        await Assert.That(SecondPasswordManager.Instance.Verify(id, "1234", out var failed)).IsFalse();
        await Assert.That(failed).IsEqualTo(0);
    }

    [Test]
    public async Task Change_ReplacesOnlyWithTheOldPassword()
    {
        var id = NextAccount();
        var manager = SecondPasswordManager.Instance;
        manager.TryCreate(id, "1111");

        await Assert.That(manager.TryChange(id, "9999", "2222", out var wrong)).IsFalse();
        await Assert.That(wrong).IsEqualTo(1);
        await Assert.That(manager.Verify(id, "1111", out _)).IsTrue(); // unchanged

        await Assert.That(manager.TryChange(id, "1111", "2222", out var ok)).IsTrue();
        await Assert.That(ok).IsEqualTo(0);
        await Assert.That(manager.Verify(id, "1111", out _)).IsFalse();
        await Assert.That(manager.Verify(id, "2222", out _)).IsTrue();
        manager.TryClear(id, "2222", out _);
    }

    [Test]
    public async Task Change_NeedsAPasswordToChangeAndANewOneToSet()
    {
        var id = NextAccount();
        var manager = SecondPasswordManager.Instance;

        await Assert.That(manager.TryChange(id, "1111", "2222", out _)).IsFalse(); // nothing set yet

        manager.TryCreate(id, "1111");
        await Assert.That(manager.TryChange(id, "1111", string.Empty, out _)).IsFalse();
        await Assert.That(manager.Verify(id, "1111", out _)).IsTrue();
        manager.TryClear(id, "1111", out _);
    }

    [Test]
    public async Task Clear_RemovesThePasswordOnlyForTheRightOne()
    {
        var id = NextAccount();
        var manager = SecondPasswordManager.Instance;
        manager.TryCreate(id, "1111");

        await Assert.That(manager.TryClear(id, "2222", out var wrong)).IsFalse();
        await Assert.That(wrong).IsEqualTo(1);
        await Assert.That(manager.HasPassword(id)).IsTrue();

        await Assert.That(manager.TryClear(id, "1111", out var ok)).IsTrue();
        await Assert.That(ok).IsEqualTo(0);
        await Assert.That(manager.HasPassword(id)).IsFalse();
    }

    [Test]
    public async Task IssuedTables_AreHandedOutAndDecodeTheirOwnClicks()
    {
        var id = NextAccount();
        var manager = SecondPasswordManager.Instance;

        var tables = manager.Issue(id, out var time);
        await Assert.That(tables.Length).IsEqualTo(SecondPasswordKeyTable.TableCount);
        await Assert.That(time).IsGreaterThan(0u);

        const string password = "42Z";
        var clicked = SecondPasswordKeyTable.Encode(tables[1], password);
        await Assert.That(manager.Decode(id, 1, clicked)).IsEqualTo(password);

        // a table index the account was never given, and an account with no tables, both read nothing
        await Assert.That(manager.Decode(id, 9, clicked)).IsNull();
        await Assert.That(manager.Decode(NextAccount(), 0, clicked)).IsNull();

        manager.Forget(id);
        await Assert.That(manager.Decode(id, 1, clicked)).IsNull();
    }
}
