using AAEmu.Game.Models.Tasks;

namespace AAEmu.Game.Core.Managers;

public interface ISaveManager
{
    ShutdownTask ShutdownTask { get; set; }
    void Initialize();
    System.Threading.Tasks.Task StopAsync();
    void SaveTickStart();
    bool DoSave();
}
