using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SystemFaction
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string OwnerName { get; set; }

    public long? OwnerTypeId { get; set; }

    public long? OwnerId { get; set; }

    public long? PoliticalSystemId { get; set; }

    public long? MotherId { get; set; }

    public byte[] AggroLink { get; set; }

    public byte[] GuardHelp { get; set; }

    public byte[] IsDiplomacyTgt { get; set; }

    public long? DiplomacyLinkId { get; set; }
}
