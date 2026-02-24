using System.Numerics;

using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers;

public interface ISubZoneManager : ILoadable
{
    List<uint> GetSubZoneByPosition(WorldTemplate worldTemplate, Vector3 pos);
    List<uint> GetSubZoneByPosition(WorldTemplate worldTemplate, float x, float y);
}
