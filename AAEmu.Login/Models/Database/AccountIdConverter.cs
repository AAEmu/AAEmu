using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AAEmu.Login.Models.Database;

public class AccountIdConverter() : ValueConverter<AccountId, uint>(
    v => v.Value,
    v => new AccountId(v));
