using System.Threading;

using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Tasks.Mates
{
    public class MateXpUpdateTask : Task
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private readonly Game.Units.Mate _mate;
        private readonly Character _owner;
        private const int Exp = 300;

        public MateXpUpdateTask(Character owner, Game.Units.Mate mate)
        {
            _mate = mate;
            _owner = owner;
        }

        public override void Execute()
        {
            _ = _mate.MateXpUpdateTask?.CancelAsync();
            _mate.MateXpUpdateTask = null;
            _mate?.AddExp(Exp);
            _owner.SendMessage($"pet received {Exp} experience points");
            _log.Debug($"[MateXpUpdateTask] Id {_mate?.Id}, ObjId {_mate?.ObjId}, DbInfo.Xp {_mate?.DbInfo.Xp}, AddExp {Exp}");
        }
    }
}
