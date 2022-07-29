using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Core.Managers.World
{
    public interface IWorldManager
    {

        Npc GetNpcByTemplateId(uint templateId);
    }
}
