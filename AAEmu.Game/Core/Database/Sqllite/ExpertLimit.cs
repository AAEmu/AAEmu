using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ExpertLimit
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? UpLimit { get; set; }

    public long? ExpertLimit1 { get; set; }

    public long? Advantage { get; set; }

    public long? ColorArgb { get; set; }

    public long? CastAdv { get; set; }

    public long? UpCurrencyId { get; set; }

    public long? UpPrice { get; set; }

    public long? DownCurrencyId { get; set; }

    public long? DownPrice { get; set; }

    public byte[] Show { get; set; }

    public long? GaugeColor { get; set; }

    public long? ExpMul { get; set; }
}
