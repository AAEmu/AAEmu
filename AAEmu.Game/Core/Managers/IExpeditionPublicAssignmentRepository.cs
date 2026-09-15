using AAEmu.Game.Models.Game.Expeditions.PublicAssignments;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IExpeditionPublicAssignmentRepository
{
    IReadOnlyList<ExpeditionPublicAssignmentState> LoadCurrent(DateTime periodStart);
    MySqlConnection Open();
    bool TrySave(MySqlConnection connection, MySqlTransaction transaction,
        ExpeditionPublicAssignmentState state, uint expectedVersion);
    void UpsertContributor(MySqlConnection connection, MySqlTransaction transaction,
        ExpeditionPublicAssignmentState state, uint characterId, string characterName, int delta);
}
