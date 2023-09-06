using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class LocalizedText
{
    public long? Id { get; set; }

    public string TblName { get; set; }

    public string TblColumnName { get; set; }

    public long? Idx { get; set; }

    public string Ko { get; set; }

    public long? KoVer { get; set; }

    public string EnUs { get; set; }

    public long? EnUsVer { get; set; }

    public string ZhCn { get; set; }

    public long? ZhCnVer { get; set; }

    public string Ja { get; set; }

    public long? JaVer { get; set; }

    public string Ru { get; set; }

    public long? RuVer { get; set; }

    public string ZhTw { get; set; }

    public long? ZhTwVer { get; set; }

    public string De { get; set; }

    public long? DeVer { get; set; }

    public string Fr { get; set; }

    public long? FrVer { get; set; }
}
