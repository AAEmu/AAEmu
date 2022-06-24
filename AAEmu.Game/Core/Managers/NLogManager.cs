using NLog;

namespace AAEmu.Game.Core.Managers
{
    internal class NLogManager : ILogManager
    {
        public ILogger GetCurrentLogger()
        {
            return LogManager.GetCurrentClassLogger();
        }
    }
}
