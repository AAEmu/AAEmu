using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncPuzzleOut
{
    public long? Id { get; set; }

    public long? GroupId { get; set; }

    public double? Ratio { get; set; }

    public string Anim { get; set; }

    public long? ProjectileId { get; set; }

    public long? LootPackId { get; set; }

    public long? Delay { get; set; }

    public long? SoundId { get; set; }

    public long? NextPhase { get; set; }

    public long? ProjectileDelay { get; set; }
}
