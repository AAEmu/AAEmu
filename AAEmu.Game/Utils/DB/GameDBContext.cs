using AAEmu.DB.Game;
using AAEmu.Game.Models;

namespace AAEmu.Game.Utils.DB
{
    public class GameDBContext : GameContext
    {
        public GameDBContext() : base(AppConfiguration.Instance.Database.ConnectionString) { }
    }
}
