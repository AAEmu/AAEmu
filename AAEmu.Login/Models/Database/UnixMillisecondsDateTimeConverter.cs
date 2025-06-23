using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AAEmu.Login.Models.Database;

public class UnixMillisecondsDateTimeConverter() : ValueConverter<DateTime, ulong>(
    v => (ulong)(DateTime.SpecifyKind(v, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalMilliseconds,
    v => DateTime.SpecifyKind(DateTime.UnixEpoch.AddMilliseconds(v), DateTimeKind.Utc));
