using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SubZone
{
    public long? Id { get; set; }

    public long? Idx { get; set; }

    public string Name { get; set; }

    public double? X { get; set; }

    public double? Y { get; set; }

    public double? W { get; set; }

    public double? H { get; set; }

    public long? ImageMap { get; set; }

    public long? LinkedZoneGroupId { get; set; }

    public long? ParentSubZoneId { get; set; }

    public long? CategoryId { get; set; }

    public long? SoundId { get; set; }

    public long? SoundPackId { get; set; }
}
