using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.SaveTask
{
    public class SaveTickStartTask : Task
    {
        public SaveTickStartTask()
        {
        }

        public override void Execute()
        {
            SaveManager.Instance.SaveTick();
        }
    }
}
