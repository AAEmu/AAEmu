using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crime;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface ICrimeManager: ILoadable
{
    (int,int) Save(MySqlConnection connection, MySqlTransaction transaction);
    CrimeEvent ReportCrime(Character reporter, Doodad evidence, uint usedSkillId, int doodadNextFuncGroup, uint doodadFuncId, string message);
    List<CrimeEvent> GetCrimesOfPlayer(uint playerId, bool includeOld);
    Doodad GenerateEvidenceFromDamage(BaseUnit criminal, Unit victim);
    Doodad GenerateEvidenceFromKill(BaseUnit criminal, Unit victim);
}
