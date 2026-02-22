using AAEmu.Game.Models.Tasks;

namespace AAEmu.Game.Core.Managers;

public interface ISaveManager : IInitializable
{
    ShutdownTask ShutdownTask { get; set; }
    System.Threading.Tasks.Task StopAsync();
    void SaveTickStart();
    bool DoSave();
}
