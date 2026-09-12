namespace AAEmu.Game.Models.Account;

/// <summary>Both account-scoped labor pools from <c>accounts.labor</c> and <c>accounts.local_labor</c>.</summary>
public readonly record struct AccountLaborBalance(short Labor, int LocalLabor);

/// <summary>One account-first labor debit, ready for an expected-state database write.</summary>
public readonly record struct AccountLaborDebit(
    uint AccountId,
    AccountLaborBalance Before,
    AccountLaborBalance After)
{
    public int LaborDelta => After.Labor - Before.Labor;
    public int LocalLaborDelta => After.LocalLabor - Before.LocalLabor;
    public bool Matches(AccountLaborBalance persisted) => Before == persisted;
}

/// <summary>Pure account-first labor spending shared with <c>Character.ChangeLabor</c> semantics.</summary>
public static class AccountLaborDebitRules
{
    public static bool TryCreate(uint accountId, AccountLaborBalance balance, int amount, out AccountLaborDebit debit)
    {
        debit = default;
        if (accountId == 0 || amount <= 0 || balance.Labor < 0 || balance.LocalLabor < 0)
            return false;

        var fromLabor = Math.Min(amount, balance.Labor);
        var remaining = amount - fromLabor;
        if (remaining > balance.LocalLabor)
            return false;

        var after = new AccountLaborBalance(
            checked((short)(balance.Labor - fromLabor)),
            balance.LocalLabor - remaining);
        debit = new AccountLaborDebit(accountId, balance, after);
        return true;
    }
}
