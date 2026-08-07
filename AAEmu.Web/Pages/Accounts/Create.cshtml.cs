using System.ComponentModel.DataAnnotations;
using AAEmu.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AAEmu.Web.Pages.Accounts;

public class CreateModel(IUserRepository userRepository, ILogger<CreateModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        // The users.username column is varchar(32).
        [Required]
        [StringLength(32, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 32 characters.")]
        [RegularExpression("^[A-Za-z0-9_-]+$",
            ErrorMessage = "Username may only contain letters, digits, underscores and hyphens.")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        // The users.email column is varchar(128) NOT NULL.
        [Required]
        [EmailAddress]
        [StringLength(128)]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(64, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            // The users table has no unique index on username, so this check is the only thing
            // preventing duplicates — two simultaneous registrations of the same name can still
            // both succeed. Adding a UNIQUE KEY in SQL/updates would close that race for good.
            if (await userRepository.UsernameExistsAsync(Input.Username, cancellationToken))
            {
                ModelState.AddModelError("Input.Username", "That username is already taken.");
                return Page();
            }

            var passwordHash = LegacyPasswordHasher.HashForStorage(Input.Password);
            var registrationIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

            var id = await userRepository.CreateUserAsync(
                Input.Username, Input.Email, passwordHash, registrationIp, cancellationToken);

            logger.LogInformation("Created account {AccountId} ({Username}).",
                id, Input.Username.ReplaceLineEndings(" "));

            TempData["Success"] = $"Account \"{Input.Username}\" created. You can now log in with the game client.";
            return RedirectToPage("/Accounts/Index");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not create account {Username}.", Input.Username.ReplaceLineEndings(" "));
            ModelState.AddModelError(string.Empty,
                "The account could not be created because the login database is not reachable.");
            return Page();
        }
    }
}
