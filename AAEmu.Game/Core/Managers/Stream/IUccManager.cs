using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Managers.Stream;

public interface IUccManager
{
    void Load();
    Ucc GetUccFromItem(Item item);
}
