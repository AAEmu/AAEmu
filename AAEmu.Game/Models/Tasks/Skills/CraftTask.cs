using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public class CraftTask : Task
    {
        private uint _craftId;
        private uint _objId;
        private int _count;
        private Character _character;

        public CraftTask(Character character, uint craftId, uint objId, int count)
        {
            _character = character;
            _craftId = craftId;
            _objId = objId;
            _count = count;
        }

        public override void Execute()
        {
            if (_count > 0)
            {
                // _character.SendMessage($"CraftTask: {_craftId}");
                var craft = CraftManager.Instance.GetCraftById(_craftId);
                _character?.Craft.Craft(craft, _count, _objId);
            }
        }
    }
}
