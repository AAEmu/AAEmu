using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFunc
{
    public long? Id { get; set; }

    public long? DoodadFuncGroupId { get; set; }

    public long? ActualFuncId { get; set; }

    public string ActualFuncType { get; set; }

    public long? NextPhase { get; set; }

    public long? SoundId { get; set; }

    public long? FuncSkillId { get; set; }

    public long? PermId { get; set; }

    public long? ActCount { get; set; }

    public byte[] PopupWarn { get; set; }

    public byte[] ForbidOnClimb { get; set; }
}
