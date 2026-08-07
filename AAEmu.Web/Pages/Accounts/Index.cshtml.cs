using AAEmu.Web.Data;
using AAEmu.Web.Models;
using AAEmu.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AAEmu.Web.Pages.Accounts;

public class IndexModel(
    IUserRepository userRepository,
    IClientLauncher clientLauncher,
    ILogger<IndexModel> logger) : PageModel
{
    private const int PageSize = 25;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<UserSummary> Users { get; private set; } = [];
    public int TotalCount { get; private set; }
    public bool DatabaseUnavailable { get; private set; }

    public bool CanLaunchClient => clientLauncher.Enabled;

    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    public int FirstRowOnPage => TotalCount == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;
    public int LastRowOnPage => Math.Min(PageNumber * PageSize, TotalCount);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (PageNumber < 1)
            PageNumber = 1;

        try
        {
            (Users, TotalCount) = await userRepository.GetUsersAsync(Search, PageNumber, PageSize, cancellationToken);

            // A page number past the end returns nothing; fall back to the last real page.
            if (Users.Count == 0 && TotalCount > 0 && PageNumber > TotalPages)
            {
                PageNumber = TotalPages;
                (Users, TotalCount) = await userRepository.GetUsersAsync(Search, PageNumber, PageSize, cancellationToken);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not load accounts from the login database.");
            DatabaseUnavailable = true;
        }
    }

    public async Task<IActionResult> OnPostLaunchAsync(uint id, CancellationToken cancellationToken)
    {
        if (!ClientLaunchRequest.IsFromLocalMachine(HttpContext))
        {
            logger.LogWarning("Refused a client launch for account {AccountId} from {RemoteIp}.",
                id, HttpContext.Connection.RemoteIpAddress);
            TempData["Error"] = ClientLaunchRequest.NonLocalMessage;
            return RedirectToPage(new { search = Search, p = PageNumber });
        }

        // Take the account name from the database rather than the form, so the value handed to the
        // client is always one that actually exists.
        UserSummary? user;
        try
        {
            user = await userRepository.GetUserAsync(id, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not read account {AccountId} from the login database.", id);
            TempData["Error"] = "The login database is not reachable, so the account name is unknown.";
            return RedirectToPage(new { search = Search, p = PageNumber });
        }

        if (user is null)
            return NotFound();

        var result = clientLauncher.Launch(user.Username);
        TempData[result.Success ? "Success" : "Error"] = result.Message;

        return RedirectToPage(new { search = Search, p = PageNumber });
    }
}
