using System;

using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterEvents : UnitEvents
{
    public EventHandler<OnTeamJoinArgs> OnTeamJoin = delegate { };
    public EventHandler<OnTeamLeaveArgs> OnTeamLeave = delegate { };
    public EventHandler<OnDungeonLeaveArgs> OnDungeonLeave = delegate { };
    public EventHandler<OnTeamLeaveArgs> OnTeamKick = delegate { };
    public EventHandler<OnDisconnectArgs> OnDisconnect = delegate { };
}

public class OnTeamJoinArgs : EventArgs
{
    public Character Player { get; set; }
    public Team.Team Team { get; set; }
}

public class OnTeamLeaveArgs : EventArgs
{
    public uint Id { get; set; }
    public Character Player { get; set; }
    public Team.Team Team { get; set; }
}

public class OnDungeonLeaveArgs : EventArgs
{
    public Character Player { get; set; }
}

public class OnDisconnectArgs : EventArgs
{
    public Character Player { get; set; }
}
