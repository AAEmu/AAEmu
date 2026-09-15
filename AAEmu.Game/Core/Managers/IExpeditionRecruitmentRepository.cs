using AAEmu.Game.Models.Game.Expeditions.Recruitment;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IExpeditionRecruitmentRepository
{
    IReadOnlyList<ExpeditionRecruitment> GetActive(DateTime now);
    IReadOnlyList<ExpeditionRecruitmentApplication> GetApplicationsForExpedition(uint expeditionId, DateTime now);
    IReadOnlyList<ExpeditionRecruitmentApplication> GetApplicationsForCharacter(uint characterId, DateTime now);
    ExpeditionRecruitment GetForUpdate(uint expeditionId, MySqlConnection connection, MySqlTransaction transaction);
    ExpeditionJoinCandidate GetCandidateForUpdate(uint characterId, MySqlConnection connection,
        MySqlTransaction transaction);
    ExpeditionJoinCandidate GetCandidate(uint characterId);
    IReadOnlyDictionary<uint, ExpeditionJoinCandidate> GetCandidates(IReadOnlyCollection<uint> characterIds);
    int CountApplicationsForUpdate(uint characterId, DateTime now, MySqlConnection connection, MySqlTransaction transaction);
    bool ApplicationExistsForUpdate(uint expeditionId, uint characterId, MySqlConnection connection,
        MySqlTransaction transaction);
    void Upsert(ExpeditionRecruitment recruitment, MySqlConnection connection, MySqlTransaction transaction);
    bool TryDebitMoney(uint characterId, long amount, MySqlConnection connection, MySqlTransaction transaction);
    bool DeleteRecruitment(uint expeditionId, MySqlConnection connection, MySqlTransaction transaction);
    void AddApplication(ExpeditionRecruitmentApplication application, MySqlConnection connection,
        MySqlTransaction transaction);
    bool DeleteApplication(uint expeditionId, uint characterId, MySqlConnection connection,
        MySqlTransaction transaction);
    int DeleteApplicationsForCharacter(uint characterId, MySqlConnection connection, MySqlTransaction transaction);
}
