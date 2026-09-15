using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Tasks.Skills;

public class CraftTask(Character character, uint craftId, uint objId, int count)
    : Task
{
    public override void Execute()
    {
        if (CharacterCraft.IsValidBatchCount(count))
        {
            // _character.SendMessage($"CraftTask: {_craftId}");
            if (character != null &&
                CraftManager.Instance.TryGetCraft(craftId, out var craft))
                character.Craft.Craft(craft, count, objId);
        }
    }
}
