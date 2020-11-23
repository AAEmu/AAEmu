using System;
using System.Collections.Generic;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.GameData.Framework
{
    public class GameDataManager : Singleton<GameDataManager>
    {
        private Logger _logger = LogManager.GetCurrentClassLogger();
        private List<GameDataLoader> _loaders;

        public GameDataManager()
        {
            _loaders = new List<GameDataLoader>();
        }
        
        public void LoadGameData()
        {
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
        }

        public void PostLoadGameData()
        {
            _logger.Info("Post loading game data");
            foreach (var loader in _loaders)
            {
                _logger.Info("Post loading {0}", loader.GetType().Name);
                loader.PostLoad();
                _logger.Info("Post loaded {0}", loader.GetType().Name);
            }
            _logger.Info("Game data post loaded");
        }
        
        private void CreateLoaders()
        {
            foreach(var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.GetCustomAttributes(typeof(GameDataAttribute), true).Length <= 0)
                    continue;

                var e = (GameDataLoader)Activator.CreateInstance(type);
                Register(e);
            }
        }
        
        private void Register(GameDataLoader dataLoader)
        {
            _loaders.Add(dataLoader);
        }
    }
}
