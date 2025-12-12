using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Tasks.Skills;

public class CraftTask(Character character, uint craftId, uint objId, int count)
    : Task
{
    public override void Execute()
    {
        if (count > 0)
        {
            // _character.SendMessage($"CraftTask: {_craftId}");
            var craft = CraftManager.Instance.GetCraftById(craftId);
            character?.Craft.Craft(craft, count, objId);
        }
    }
}
