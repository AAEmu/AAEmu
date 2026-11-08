using AAEmu.Game.Models.Game.World;
using Jitter2.LinearMath;

namespace AAEmu.Game.Core.Managers;

public interface ISubZoneManager : ILoadable
{
    List<uint> GetSubZoneByPosition(WorldTemplate worldTemplate, JVector pos);
    List<uint> GetSubZoneByPosition(WorldTemplate worldTemplate, float x, float y);
}
