namespace AAEmu.Game.Models.Game.Team;

/// <summary>
/// Wire values for <c>SCTeamJointInfoPacket.mode</c> and <c>CSTeamJointInfoPacket.mode</c>.
///
/// This is deliberately NOT the client's UI event table. That table lists names such as
/// TEAM_JOINT_RESPONSE and TEAM_JOINT_BROKEN next to the *length of the name string*
/// ("TEAM_JOINT_RESPONSE" is 19 characters), so 19 and 17 are string lengths and must never be
/// sent as a mode.
/// </summary>
public static class TeamJointModes
{
    /// <summary>Client → server: the request came from the chat menu.</summary>
    public const sbyte MenuChatRequest = 1;

    /// <summary>Client → server: the request came from the target context menu.</summary>
    public const sbyte MenuTargetRequest = 2;

    /// <summary>
    /// TEAM_JOINT_REQUEST. The raid popup's "invite raid joint" entry sends this as the request
    /// (x2ui/components/popup_menu_proc.lua:236), so it is a mode a client may originate with;
    /// the server also uses it to open the request frame. One value, both directions.
    /// </summary>
    public const sbyte ContextRequest = 3;

    /// <summary>Server → client: open the joint *response* frame for the other raid's owner.</summary>
    public const sbyte ResponsePrompt = 4;

    /// <summary>Lowest value the client handler understands.</summary>
    public const sbyte KnownMin = MenuChatRequest;

    /// <summary>Highest value the client handler understands.</summary>
    public const sbyte KnownMax = ResponsePrompt;

    public static bool IsKnownWireMode(sbyte mode) => mode >= KnownMin && mode <= KnownMax;

    /// <summary>True for the modes a client may originate a request with: the two menu queries
    /// and the raid popup's own invite entry.</summary>
    public static bool IsRequestMode(sbyte mode) => mode is MenuChatRequest or MenuTargetRequest or ContextRequest;
}
