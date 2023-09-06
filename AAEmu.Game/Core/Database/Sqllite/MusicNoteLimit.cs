using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class MusicNoteLimit
{
    public long? Id { get; set; }

    public long? Step { get; set; }

    public long? NoteLength { get; set; }
}
