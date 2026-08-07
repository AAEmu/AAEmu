using System.Net;

namespace AAEmu.Web.Services;

public static class ClientLaunchRequest
{
    /// <summary>
    /// Whether the request came from the machine running this app.
    /// </summary>
    /// <remarks>
    /// The client is started by the web server process, so it opens on the host's desktop. A
    /// request from anywhere else would spawn a window nobody is sitting in front of, and would
    /// let anyone who can reach the site start processes on the host. Neither is wanted, so
    /// launching is restricted to loopback.
    /// </remarks>
    public static bool IsFromLocalMachine(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        return remoteIp is not null && IPAddress.IsLoopback(remoteIp);
    }

    public const string NonLocalMessage =
        "The client can only be launched from the machine running this site — it would otherwise " +
        "open on the host's desktop.";
}
