using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Mails;

public class MailDeliveryTask : Task
{
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        MailManager.Instance.CheckAllMailTimings();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
