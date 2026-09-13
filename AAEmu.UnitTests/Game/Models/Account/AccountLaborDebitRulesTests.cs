using AAEmu.Game.Models.Account;

namespace AAEmu.UnitTests.Game.Models.Account;

public class AccountLaborDebitRulesTests
{
    [Test]
    public async Task Debit_ConsumesAccountLaborBeforeServerLocalLabor()
    {
        var before = new AccountLaborBalance(3, 7);

        var accepted = AccountLaborDebitRules.TryCreate(1, before, 8, out var debit);

        await Assert.That(accepted).IsTrue();
        await Assert.That(debit.Before).IsEqualTo(before);
        await Assert.That(debit.After).IsEqualTo(new AccountLaborBalance(0, 2));
        await Assert.That(debit.LaborDelta).IsEqualTo(-3);
        await Assert.That(debit.LocalLaborDelta).IsEqualTo(-5);
    }

    [Test]
    public async Task Debit_RejectsInsufficientCombinedLaborWithoutChangingTheInputBalance()
    {
        var before = new AccountLaborBalance(3, 7);

        var accepted = AccountLaborDebitRules.TryCreate(1, before, 11, out var debit);

        await Assert.That(accepted).IsFalse();
        await Assert.That(debit).IsEqualTo(default(AccountLaborDebit));
        await Assert.That(before).IsEqualTo(new AccountLaborBalance(3, 7));
    }

    [Test]
    public async Task Debit_ExpectedStateDoesNotMatchAStalePersistedBalance()
    {
        var accepted = AccountLaborDebitRules.TryCreate(1, new AccountLaborBalance(10, 5), 4, out var debit);

        await Assert.That(accepted).IsTrue();
        await Assert.That(debit.Matches(new AccountLaborBalance(9, 5))).IsFalse();
        await Assert.That(debit.Matches(debit.Before)).IsTrue();
    }
}
