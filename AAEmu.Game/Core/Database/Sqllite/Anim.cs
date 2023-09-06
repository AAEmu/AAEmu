using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Anim
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public byte[] Loop { get; set; }

    public long? CategoryId { get; set; }

    public string RideUb { get; set; }

    public string HangUb { get; set; }

    public string SwimUb { get; set; }

    public string MoveUb { get; set; }

    public string RelaxedUb { get; set; }

    public string SwimMoveUb { get; set; }
}
