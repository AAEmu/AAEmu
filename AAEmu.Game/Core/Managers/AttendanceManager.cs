using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class AttendanceManager : Singleton<AttendanceManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public void Add(uint characterId)
    {
        var character = WorldManager.Instance.GetCharacterById(characterId);
        character?.Attendances.Add(character);
    }

}
