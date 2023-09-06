using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncClout
{
    public long? Id { get; set; }

    public long? Duration { get; set; }

    public long? Tick { get; set; }

    public long? TargetRelationId { get; set; }

    public long? BuffId { get; set; }

    public long? ProjectileId { get; set; }

    public byte[] ShowToFriendlyOnly { get; set; }

    public long? FxGroupId { get; set; }

    public long? NextPhase { get; set; }

    public long? AoeShapeId { get; set; }

    public long? TargetBuffTagId { get; set; }

    public long? TargetNoBuffTagId { get; set; }

    public byte[] UseOriginSource { get; set; }
}
