using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Managers.Stream;

public interface IUccManager : ILoadable
{
    Ucc GetUccFromItem(Item item);
}
