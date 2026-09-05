using AAEmu.Commons.Utils.DB;

namespace AAEmu.Game.Models.Game.Char;

public interface ICharacterOptionStore
{
    void Save(uint characterId, ushort key, string value);
}

public sealed class CharacterOptionStore : ICharacterOptionStore
{
    public void Save(uint characterId, ushort key, string value)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO `options` (`owner`, `key`, `value`) VALUES (@owner, @key, @value) " +
            "ON DUPLICATE KEY UPDATE `value` = @value";
        command.Parameters.AddWithValue("@owner", characterId);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        command.ExecuteNonQuery();
    }
}
