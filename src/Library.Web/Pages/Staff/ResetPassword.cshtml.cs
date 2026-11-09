using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Auth;
using Project.Data;
using Project.Models;

namespace Project.Pages.Staff;

// The /Staff folder requires the LibraryStaff policy in Program.cs.
public class ResetPasswordModel : PageModel
{
    private readonly LibraryDbContext _db;
    private readonly ILogger<ResetPasswordModel> _logger;

    public ResetPasswordModel(LibraryDbContext db, ILogger<ResetPasswordModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public int AccountId { get; set; }

    [BindProperty]
    public int ExpectedSessionVersion { get; set; }

    [BindProperty]
    public ResetInput Input { get; set; } = new();

    public string AccountEmail { get; private set; } = "";
    public string AccountRole { get; private set; } = "";

    [TempData]
    public string? SuccessMessage { get; set; }

    public class ResetInput
    {
        [Required, StringLength(128, MinimumLength = 12)]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = "";

        [Required, StringLength(128)]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
        [Display(Name = "Confirm new password")]
        public string ConfirmPassword { get; set; } = "";
    }

    private void ShowAccount(UserAccount account)
    {
        AccountEmail = account.Email;
        AccountRole = account.Role;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var account = await _db.UserAccounts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.AccountId == AccountId);
        if (account == null) return NotFound();
        if (!AccountAccessRules.CanResetPassword(User, account.Role)) return Forbid();

        ShowAccount(account);
        // Remember which version was shown when the form opened.
        ExpectedSessionVersion = account.SessionVersion;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();

            // Lock this account row so a login or another reset cannot update it at the same time.
            // Read the account's role from the database, not from the form.
            var matches = await _db.UserAccounts.FromSqlInterpolated(
                $"SELECT * FROM UserAccount WHERE AccountId = {AccountId} FOR UPDATE")
                .ToListAsync();
            var account = matches.SingleOrDefault();
            if (account == null) return NotFound();
            // Check permission again when saving, even if the button was visible.
            if (!AccountAccessRules.CanResetPassword(User, account.Role)) return Forbid();
            ShowAccount(account);

            // An old form should not overwrite a password that was just reset.
            if (ExpectedSessionVersion != account.SessionVersion)
                ModelState.AddModelError(string.Empty,
                    "This account changed since you opened the form. Reopen it from the directory before resetting.");

            if (!ModelState.IsValid) return Page();

            // Store a hash instead of the actual password.
            account.PasswordHash = new PasswordHasher<UserAccount>()
                .HashPassword(account, Input.NewPassword);
            account.SessionVersion = checked(account.SessionVersion + 1);
            // Let the user try their new password without the old temporary lockout.
            account.FailedLoginAttempts = 0;
            account.LockoutUntilUtc = null;
            // Do not change Role, IsEnabled, or the linked person.
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            SuccessMessage = "Password reset. Previous sessions will be signed out on their next request. " +
                "Any disabled login remains disabled.";
            // Redirect after saving so refreshing does not submit the reset again.
            return RedirectToPage(new { AccountId });
        }
        catch (Exception ex) when (ex is DbException || ex is DbUpdateException)
        {
            _logger.LogError(ex, "Unable to reset password for account {AccountId}.", AccountId);
            ModelState.AddModelError(string.Empty,
                "The reset could not be confirmed. Reopen the account from the directory before trying again.");
            return Page();
        }
        finally
        {
            // Never repopulate password inputs after an error.
            Input.NewPassword = "";
            Input.ConfirmPassword = "";
            if (ModelState.TryGetValue("Input.NewPassword", out var password))
                password.RawValue = password.AttemptedValue = null;
            if (ModelState.TryGetValue("Input.ConfirmPassword", out var confirmation))
                confirmation.RawValue = confirmation.AttemptedValue = null;
        }
    }
}
