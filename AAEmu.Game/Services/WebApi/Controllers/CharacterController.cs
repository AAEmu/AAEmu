using System.Text;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Services.WebApi.Models;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

internal class CharacterController : BaseController
{
    [WebApiGet("/api/character/list")]
    public HttpResponse List(HttpRequest request)
    {
        var queryParams = ParseQueryString(request.Url);
        var list = new List<CharacterModel>();
        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                // Build SQL
                var sqlBuilder =
                    new StringBuilder(
                        "SELECT `id`, `name`, `level`,`created_at`, `account_id`, `family`, `expedition_id` FROM `characters` WHERE `deleted` = 0");

                var accountId = queryParams.Get("AccountId");
                if (accountId != null)
                {
                    sqlBuilder.Append(" AND `account_id` = @accountId");
                    command.Parameters.AddWithValue("@accountId", accountId);
                }

                var name = queryParams.Get("Name");
                if (name != null)
                {
                    sqlBuilder.Append(" AND `name` = @name");
                    command.Parameters.AddWithValue("@name", name);
                }

                command.CommandText = sqlBuilder.ToString();
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("id");
                        var charName = reader.GetString("name");
                        var createdAt = reader.GetDateTime("created_at");
                        var character = WorldManager.Instance.GetCharacterById(id);

                        var level = reader.GetUInt32("level");
                        var familyId = reader.GetUInt32("family");
                        var expeditionId = reader.GetUInt32("expedition_id");

                        if (character != null)
                        {
                            level = character.Level;
                            familyId = character.Family;
                            expeditionId = (uint)(character.Expedition?.Id ?? 0);
                        }

                        list.Add(new CharacterModel(id, charName, level, createdAt, character != null, familyId, expeditionId));
                    }
                }
            }
        }

        return OkJson(list);
    }
}
