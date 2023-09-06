using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SlashCommand
{
    public long? Id { get; set; }

    public long? ActionId { get; set; }

    public string ActionType { get; set; }

    public long? SkillId { get; set; }

    public string CommandList { get; set; }
}
