using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Gimmicks
{
    public class GimmickTickStartTask : Task
    {
        public override void Execute()
        {
            GimmickManager.Instance.GimmickTick();
        }
    }
}
