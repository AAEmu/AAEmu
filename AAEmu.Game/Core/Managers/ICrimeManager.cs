using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crime;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface ICrimeManager: ILoadable
{
    (int,int) Save(MySqlConnection connection, MySqlTransaction transaction);
    CrimeEvent ReportCrime(Character reporter, Doodad evidence, uint arg1, uint arg2, uint arg3, string message);
    List<CrimeEvent> GetCrimesOfPlayer(uint playerId);
    Doodad GenerateEvidenceFromDamage(BaseUnit criminal, Unit victim);
    Doodad GenerateEvidenceFromKill(BaseUnit criminal, Unit victim);
}
