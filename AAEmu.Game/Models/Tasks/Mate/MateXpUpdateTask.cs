using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Tasks.Mate;

public class MateXpUpdateTask : Task
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
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
        _mate.MateXpUpdateTask?.Cancel();
        _mate.MateXpUpdateTask = null;
        _mate?.AddExp(Exp);
        // TODO: Proper message?
        _owner.SendMessage($"pet received {Exp} experience points");
        Logger.Debug($"[MateXpUpdateTask] Id {_mate?.Id}, ObjId {_mate?.ObjId}, DbInfo.Xp {_mate?.DbInfo.Xp}, AddExp {Exp}");
    }
}
