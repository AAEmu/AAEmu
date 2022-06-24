using NLog;

namespace AAEmu.Game.Core.Managers
{
    public interface ILogManager
    {
        ILogger GetCurrentLogger();
    }
}
