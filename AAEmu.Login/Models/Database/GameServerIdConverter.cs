using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AAEmu.Login.Models.Database;

public class GameServerIdConverter() : ValueConverter<GameServerId, byte>(
    v => v.Value,
    v => new GameServerId(v));
