using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Models;
using Project.Data;
using Project.Configuration;
using Project.Auth;

namespace Project.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly LibraryDbContext _db;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            LibraryDbContext db,
            ILogger<LoginModel> logger)
        {
            _db = db;
            _logger = logger;
        }

        [BindProperty]
        public LoginInput Input { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        public class LoginInput
        {
            [Required]
            [EmailAddress]
            [StringLength(255)]
            public string Email { get; set; } = "";

            [Required]
            [StringLength(128)]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";
        }

        public IActionResult OnGet()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToPage("/Search");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToPage("/Search");
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            string normalizedEmail =
                Input.Email.Trim().ToUpperInvariant();

            const string loginError =
                "Unable to sign in. Check your credentials. " +
                "If you have made five incorrect attempts, wait 15 minutes.";

            try
            {
                await using var transaction =
                    await _db.Database.BeginTransactionAsync();

                // Lock this account row while checking/updating its counter
                var matches = await _db.UserAccounts
                    .FromSqlInterpolated(
                        $@"SELECT *
                   FROM UserAccount
                   WHERE NormalizedEmail = {normalizedEmail}
                   FOR UPDATE")
                    .ToListAsync();

                var account = matches.SingleOrDefault();

                if (account is null ||
                    !await AccountAccessRules.CanSignInAsync(_db, account))
                {
                    ModelState.AddModelError(string.Empty, loginError);
                    return Page();
                }

                DateTime now = DateTime.UtcNow;

                // active lockout rejects correct password aswell
                if (account.LockoutUntilUtc.HasValue &&
                    account.LockoutUntilUtc.Value > now)
                {
                    ModelState.AddModelError(string.Empty, loginError);
                    return Page();
                }

                // expired lockout starts a fresh set of attempts
                if (account.LockoutUntilUtc.HasValue)
                {
                    account.FailedLoginAttempts = 0;
                    account.LockoutUntilUtc = null;
                }

                var passwordHasher = new PasswordHasher<UserAccount>();

                var result = passwordHasher.VerifyHashedPassword(
                    account,
                    account.PasswordHash,
                    Input.Password);

                if (result == PasswordVerificationResult.Failed)
                {
                    account.FailedLoginAttempts++;

                    if (account.FailedLoginAttempts >= 5)
                    {
                        account.LockoutUntilUtc =
                            DateTime.UtcNow.AddMinutes(15);
                    }

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    ModelState.AddModelError(string.Empty, loginError);
                    return Page();
                }

                // a successful login clears previous failures
                account.FailedLoginAttempts = 0;
                account.LockoutUntilUtc = null;

                if (result ==
                    PasswordVerificationResult.SuccessRehashNeeded)
                {
                    account.PasswordHash =
                        passwordHasher.HashPassword(
                            account,
                            Input.Password);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                var claims = new List<Claim>
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                account.AccountId.ToString()),

            new Claim(
                ClaimTypes.Name,
                account.Email),

            new Claim(
                ClaimTypes.Role,
                account.Role),

            //save the version from this login so we can check it on later requests
            new Claim(
                "session_version",
                account.SessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

                if (account.LibraryUserId.HasValue)
                {
                    claims.Add(new Claim(
                        "library_user_id",
                        account.LibraryUserId.Value.ToString()));
                }

                if (account.StaffId.HasValue)
                {
                    claims.Add(new Claim(
                        "staff_id",
                        account.StaffId.Value.ToString()));
                }

                var identity = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme);

                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = false
                    });

                if (!string.IsNullOrEmpty(ReturnUrl) &&
                    Url.IsLocalUrl(ReturnUrl))
                {
                    return LocalRedirect(ReturnUrl);
                }

                return RedirectToPage("/Search");
            }
            catch (DbException ex)
            {
                _logger.LogError(
                    ex,
                    "Database error while signing in.");

                ModelState.AddModelError(
                    string.Empty,
                    "Sign-in is temporarily unavailable. " +
                    "Please try again later.");

                return Page();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Unable to update login information.");

                ModelState.AddModelError(
                    string.Empty,
                    "Sign-in is temporarily unavailable. " +
                    "Please try again later.");

                return Page();
            }
        }
    }
}
