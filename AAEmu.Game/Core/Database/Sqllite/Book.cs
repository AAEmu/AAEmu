using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Book
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? CategoryId { get; set; }
}
