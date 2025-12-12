using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Tasks.Mate;

public class MateXpUpdateTask(Character owner, Game.Units.Mate mate) : Task
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private const int Exp = 300;

    public override void Execute()
    {
        mate.MateXpUpdateTask?.Cancel();
        mate.MateXpUpdateTask = null;
        mate?.AddExp(Exp);
        // TODO: Proper message?
        owner.SendMessage($"pet received {Exp} experience points");
        Logger.Debug($"[MateXpUpdateTask] Id {mate?.Id}, ObjId {mate?.ObjId}, DbInfo.Xp {mate?.DbInfo.Xp}, AddExp {Exp}");
    }
}
