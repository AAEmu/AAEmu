using System;

namespace AAEmu.Game.IO
{
    public interface IDateTimeManager
    {
        DateTime UtcNow { get; }
    }
}
