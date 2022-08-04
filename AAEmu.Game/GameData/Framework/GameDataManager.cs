using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.GameData.Framework
{
    public class GameDataManager : Singleton<GameDataManager>
    {
        private Logger _logger = LogManager.GetCurrentClassLogger();
        private List<IGameDataLoader> _loaders;
        private bool _loadedGameData = false;
        private bool _postLoadedGameData = false;
        public GameDataManager()
        {
            _loaders = new List<IGameDataLoader>();
        }
        
        public void LoadGameData()
        {
            if (_loadedGameData)
                return;
            
            _logger.Info("Loading game data");
            CreateLoaders();
            using (var connection = SQLite.CreateConnection())
            {
                foreach (var loader in _loaders)
                {
                    _logger.Info("Loading {0}", loader.GetType().Name);
                    loader.Load(connection);
                    _logger.Info("Loaded {0}", loader.GetType().Name);
                }
            }

            _logger.Info("Game data loaded");

            _loadedGameData = true;
        }

        public void PostLoadGameData()
        {
            if (_postLoadedGameData)
                return;
            
            _logger.Info("Post loading game data");
            foreach (var loader in _loaders)
            {
                _logger.Info("Post loading {0}", loader.GetType().Name);
                loader.PostLoad();
                _logger.Info("Post loaded {0}", loader.GetType().Name);
            }
            _logger.Info("Game data post loaded");

            _postLoadedGameData = true;
        }
        
        private void CreateLoaders()
        {
            foreach(var type in Assembly.GetAssembly(typeof(GameDataManager)).GetTypes())
            {
                if (type.GetCustomAttributes(typeof(GameDataAttribute), true).Length <= 0)
                    continue;

                if (!type.GetInterfaces().Contains(typeof(IGameDataLoader)))
                {
                    _logger.Error("[GameData] {0} does not inherit IGameDataLoader", type.Name);
                    continue;
                }

                var e = type.BaseType?.GetProperty("Instance")?.GetValue(null);
                Register((IGameDataLoader)e);
            }
        }
        
        private void Register(IGameDataLoader dataLoader)
        {
            _loaders.Add(dataLoader);
        }
    }
}
