using System.ComponentModel.DataAnnotations;
using AAEmu.Web.Data;
using AAEmu.Web.Models;
using AAEmu.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AAEmu.Web.Pages.Accounts;

public class DetailsModel(
    IUserRepository userRepository,
    IGameRepository gameRepository,
    IAccessLevelCatalog accessLevelCatalog,
    IClientLauncher clientLauncher,
    ILogger<DetailsModel> logger) : PageModel
{
    public bool CanLaunchClient => clientLauncher.Enabled;

    /// <summary>Null when the login database could not be read; the page then renders an alert only.</summary>
    public UserSummary? Account { get; private set; }

    /// <summary>Null when the account has never logged in, so the game server never created its row.</summary>
    public GameAccount? GameAccount { get; private set; }

    public IReadOnlyList<CharacterSummary> Characters { get; private set; } = [];

    public IReadOnlyList<AccessLevelTier> AccessLevelTiers => accessLevelCatalog.Tiers;
    public int TotalCommands => accessLevelCatalog.TotalCommands;

    public bool LoginDatabaseUnavailable { get; private set; }
    public bool GameDatabaseUnavailable { get; private set; }

    private enum LoadOutcome
    {
        Ok,
        NotFound,
        LoginDatabaseDown
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Range(0, 999)]
        [Display(Name = "Access level")]
        public int AccessLevel { get; set; }

        // Capped at short.MaxValue: the game server reads this column with GetInt16.
        [Range(0, Models.GameAccount.MaxLabor,
            ErrorMessage = "Labor must be between 0 and 32767 — the game server reads it as a 16-bit value.")]
        [Display(Name = "Labor")]
        public int Labor { get; set; }

        [Range(0, int.MaxValue)]
        [Display(Name = "Credits")]
        public int Credits { get; set; }

        [Range(0, int.MaxValue)]
        [Display(Name = "Loyalty")]
        public int Loyalty { get; set; }
    }

    public int CommandsAvailableAt(int level) => accessLevelCatalog.CommandsAvailableAt(level);

    public string DescribeLevel(int level) => accessLevelCatalog.DescribeLevel(level);

    public async Task<IActionResult> OnGetAsync(uint id, CancellationToken cancellationToken)
    {
        switch (await LoadAsync(id, cancellationToken))
        {
            case LoadOutcome.NotFound:
                return NotFound();
            case LoadOutcome.LoginDatabaseDown:
                return Page();
        }

        Input = new InputModel
        {
            AccessLevel = GameAccount?.AccessLevel ?? 0,
            Labor = GameAccount?.Labor ?? 0,
            Credits = GameAccount?.Credits ?? 0,
            Loyalty = GameAccount?.Loyalty ?? 0
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(uint id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await ReloadOrFailAsync(id, cancellationToken);
        }

        try
        {
            await gameRepository.UpdateAccountAsync(
                id, Input.AccessLevel, Input.Labor, Input.Credits, Input.Loyalty, cancellationToken);

            logger.LogInformation(
                "Updated game account {AccountId}: access_level={AccessLevel}, labor={Labor}, credits={Credits}, loyalty={Loyalty}.",
                id, Input.AccessLevel, Input.Labor, Input.Credits, Input.Loyalty);

            TempData["Success"] = "Account values saved.";
            return RedirectToPage(new { id });
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not update game account {AccountId}.", id);
            ModelState.AddModelError(string.Empty,
                "The changes could not be saved because the game database is not reachable.");
            return await ReloadOrFailAsync(id, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostLaunchAsync(uint id, CancellationToken cancellationToken)
    {
        if (!ClientLaunchRequest.IsFromLocalMachine(HttpContext))
        {
            logger.LogWarning("Refused a client launch for account {AccountId} from {RemoteIp}.",
                id, HttpContext.Connection.RemoteIpAddress);
            TempData["Error"] = ClientLaunchRequest.NonLocalMessage;
            return RedirectToPage(new { id });
        }

        UserSummary? user;
        try
        {
            user = await userRepository.GetUserAsync(id, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not read account {AccountId} from the login database.", id);
            TempData["Error"] = "The login database is not reachable, so the account name is unknown.";
            return RedirectToPage(new { id });
        }

        if (user is null)
            return NotFound();

        var result = clientLauncher.Launch(user.Username);
        TempData[result.Success ? "Success" : "Error"] = result.Message;

        return RedirectToPage(new { id });
    }

    private async Task<IActionResult> ReloadOrFailAsync(uint id, CancellationToken cancellationToken) =>
        await LoadAsync(id, cancellationToken) == LoadOutcome.NotFound ? NotFound() : Page();

    /// <summary>
    /// Loads the account from the login database and its gameplay data from the game database.
    /// Either database being down is reported to the page rather than thrown, so an outage renders
    /// an explanation instead of a 500.
    /// </summary>
    private async Task<LoadOutcome> LoadAsync(uint id, CancellationToken cancellationToken)
    {
        UserSummary? user;
        try
        {
            user = await userRepository.GetUserAsync(id, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not load account {AccountId} from the login database.", id);
            LoginDatabaseUnavailable = true;
            return LoadOutcome.LoginDatabaseDown;
        }

        if (user is null)
            return LoadOutcome.NotFound;

        Account = user;

        // The game database is a separate server as far as this app is concerned — if it is down,
        // still render the login-side details rather than failing the whole page.
        try
        {
            GameAccount = await gameRepository.GetAccountAsync(id, cancellationToken);
            Characters = await gameRepository.GetCharactersByAccountAsync(id, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not load game data for account {AccountId}.", id);
            GameDatabaseUnavailable = true;
        }

        return LoadOutcome.Ok;
    }
}
