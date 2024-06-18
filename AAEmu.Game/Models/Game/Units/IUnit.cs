using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.Units;

public interface IUnit : IBaseUnit
{
    byte Level { get; set; }
    BaseUnit CurrentTarget { get; set; }
    BaseUnit CurrentInteractionObject { get; set; }
    ItemContainer Equipment { get; set; }
    void SendPacket(GamePacket packet);
    SkillResult UseSkill(uint skillId, IUnit target);
}
