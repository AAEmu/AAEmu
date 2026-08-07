using System.ComponentModel.DataAnnotations;

namespace AAEmu.Web.Models;

/// <summary>
/// Settings for launching the game client from the account list.
/// </summary>
/// <remarks>
/// The client is started by the web server process, so it appears on the machine hosting this app
/// — not on the machine viewing the page. This is only meaningful when you are browsing the site
/// from the same machine that runs it, which is why <see cref="Enabled"/> defaults to false and
/// requests from non-loopback addresses are refused.
/// <para>
/// Mirrors the arguments in <c>launch_aaemu.bat</c>:
/// <c>archeage.exe -devmode {DevMode} -StrUserName={account} -strUserToken={UserToken}
/// -sIp={AuthIp} -sPort={AuthPort} -gameId={GameId} +locale {Locale}</c>
/// </para>
/// </remarks>
public class ClientLauncherOptions
{
    public const string ConfigurationSectionName = "ClientLauncher";

    /// <summary>
    /// Whether the Run button is shown and the launch handler will act. Off by default.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Full path to <c>archeage.exe</c>. Its own directory is used as the working directory,
    /// which the client requires in order to find its data.
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Value for <c>-devmode</c>, which boots the dev client and skips the patcher and launcher.
    /// </summary>
    public string DevMode { get; set; } = "1514255490";

    /// <summary>
    /// Value for <c>+locale</c>. The locale cvar defaults to zh_cn on some builds, so the command
    /// line is the only reliable override.
    /// </summary>
    public string Locale { get; set; } = "en_us";

    /// <summary>Login server address passed as <c>-sIp</c>.</summary>
    public string AuthIp { get; set; } = "127.0.0.1";

    /// <summary>Login server port passed as <c>-sPort</c>.</summary>
    [Range(1, 65535)]
    public int AuthPort { get; set; } = 1237;

    /// <summary>Auth context passed as <c>-gameId</c>.</summary>
    public int GameId { get; set; } = 1;

    /// <summary>
    /// Value for <c>-strUserToken</c>. AAEmu has no web-auth backend and
    /// <c>CARequestWebAuthPacketHandler</c> never reads this value, so any non-empty string works.
    /// </summary>
    public string UserToken { get; set; } = "testtoken";
}
