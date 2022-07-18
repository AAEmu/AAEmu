using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units
{
    public interface IBaseUnit : IGameObject
    {
        uint Id { get; set; }
        string Name { get; set; }
        Buffs Buffs { get; set; }
    }
}
