using AAEmu.Commons.Utils;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData.Framework
{
    public abstract class GameDataLoader : Singleton<GameDataLoader>
    {
        public abstract void Load(SqliteConnection connection);
        public abstract void PostLoad();
    }
}
