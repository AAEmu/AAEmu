using AAEmu.Commons.Models;

namespace AAEmu.Login.Models;

public readonly record struct WorldListResult(
    List<GameServer> GameServers,
    List<LoginCharacterInfo> Characters);
