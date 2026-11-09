using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Auth;
using Project.Data;
using Project.Models;

namespace Project.Pages.Account;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ChangePasswordModel(LibraryDbContext db, ILogger<ChangePasswordModel> logger) : PageModel
{
    [BindProperty]
    public ChangeInput Input { get; set; } = new();

    public class ChangeInput : IValidatableObject
    {
        [Required, StringLength(128), DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = "";

        [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = "";

        [Required, StringLength(128), DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
        [Display(Name = "Confirm new password")]
        public string ConfirmPassword { get; set; } = "";

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (CurrentPassword == NewPassword)
                yield return new ValidationResult("Choose a different password.",
                    new[] { nameof(NewPassword) });
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            if (!ModelState.IsValid) return Page();
            // Get the account from the login cookie, so users can only change their own password.
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
                return Challenge();

            await using var transaction = await db.Database.BeginTransactionAsync();
            // Lock the row so another reset or change cannot overwrite this one.
            var accounts = await db.UserAccounts.FromSqlInterpolated(
                $"SELECT * FROM UserAccount WHERE AccountId = {accountId} FOR UPDATE").ToListAsync();
            var account = accounts.SingleOrDefault();
            if (account == null ||
                !AccountAccessRules.HasCurrentSessionVersion(User, account) ||
                !await AccountAccessRules.CanSignInAsync(db, account))
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return RedirectToPage("/Account/Login");
            }

            var hasher = new PasswordHasher<UserAccount>();
            if (hasher.VerifyHashedPassword(account, account.PasswordHash, Input.CurrentPassword)
                == PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError("Input.CurrentPassword", "The current password is incorrect.");
                return Page();
            }

            account.PasswordHash = hasher.HashPassword(account, Input.NewPassword);
            // This ends other sessions when they next contact the website.
            account.SessionVersion = checked(account.SessionVersion + 1);
            account.FailedLoginAttempts = 0;
            account.LockoutUntilUtc = null;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["PasswordChangedMessage"] = "Your password was changed. Log in with your new password.";
            return RedirectToPage("/Account/Login");
        }
        catch (Exception ex) when (ex is DbException || ex is DbUpdateException)
        {
            logger.LogError(ex, "Unable to change the signed-in account's password.");
            ModelState.AddModelError(string.Empty,
                "The password change could not be confirmed. Log in again before retrying.");
            return Page();
        }
        finally
        {
            // Clear all password values when showing validation errors.
            Input = new ChangeInput();
            foreach (var field in new[] { "Input.CurrentPassword", "Input.NewPassword", "Input.ConfirmPassword" })
            {
                if (ModelState.TryGetValue(field, out var entry))
                    entry.RawValue = entry.AttemptedValue = null;
            }
        }
    }
}
