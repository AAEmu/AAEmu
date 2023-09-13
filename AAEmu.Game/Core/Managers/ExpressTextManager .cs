using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Emotion;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class ExpressTextManager : Singleton<ExpressTextManager>, IExpressTextManager
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, uint> _expressTexts;

        public uint GetExpressAnimId(uint emotionId)
        {
            return _expressTexts.ContainsKey(emotionId) ? _expressTexts[emotionId] : 0;
        }

        public void Load()
        {
            _expressTexts = new Dictionary<uint, uint>();

            _log.Info("Loading express text...");

            using var connection = SQLite.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM express_texts";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var template = new ExpressText();
                template.Id = reader.GetUInt32("id");
                template.AnimId = reader.GetUInt32("anim_id");

                if (!_expressTexts.ContainsKey(template.Id))
                {
                    _expressTexts.Add(template.Id, template.AnimId);
                }
            }
        }
    }
}
