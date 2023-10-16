using System;
using NLog;

namespace AAEmu.Game.Models.Game.AI.Framework;

public class State
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public AbstractAI AI;
    public virtual void Enter() { }
    public virtual void Exit() { }
    public virtual void Tick(TimeSpan delta) { }
}
