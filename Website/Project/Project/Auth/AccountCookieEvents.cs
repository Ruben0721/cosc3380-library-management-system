using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Project.Pages.Shared;

namespace Project.Auth
{
    public class AccountCookieEvents : CookieAuthenticationEvents
    {
        private readonly LibraryDbContext _db;
        private readonly ILogger<AccountCookieEvents> _logger;

        public AccountCookieEvents(
            LibraryDbContext db,
            ILogger<AccountCookieEvents> logger)
        {
            _db = db;
            _logger = logger;
        }

        public override async Task ValidatePrincipal(
            CookieValidatePrincipalContext context)
        {
            var principal = context.Principal;

            string? accountIdText =
                principal?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!int.TryParse(accountIdText, out int accountId))
            {
                await EndSessionAsync(context);
                return;
            }

            try
            {
                var account = await _db.UserAccounts
                    .AsNoTracking()
                    .SingleOrDefaultAsync(a =>
                        a.AccountId == accountId);

                if (account is null ||
                    !await AccountAccessRules.CanSignInAsync(_db, account))
                {
                    await EndSessionAsync(context);
                    return;
                }

                string cookieRole =
                    principal?.FindFirstValue(ClaimTypes.Role) ?? "";

                string cookieEmail =
                    principal?.FindFirstValue(ClaimTypes.Name) ?? "";

                string cookieBorrowerId =
                    principal?.FindFirstValue("library_user_id") ?? "";

                string cookieStaffId =
                    principal?.FindFirstValue("staff_id") ?? "";

                string currentBorrowerId =
                    account.LibraryUserId?.ToString() ?? "";

                string currentStaffId =
                    account.StaffId?.ToString() ?? "";

                bool accountChanged =
                    cookieRole != account.Role ||
                    cookieEmail != account.Email ||
                    cookieBorrowerId != currentBorrowerId ||
                    cookieStaffId != currentStaffId;

                if (accountChanged)
                {
                    await EndSessionAsync(context);
                }
            }
            catch (DbException ex)
            {
                _logger.LogError(
                    ex,
                    "Unable to revalidate the signed-in account.");

                // If current access cannot be verified,do not continue using the old permissions
                await EndSessionAsync(context);
            }
        }

        private static async Task EndSessionAsync(
            CookieValidatePrincipalContext context)
        {
            context.RejectPrincipal();

            await context.HttpContext.SignOutAsync(
                context.Scheme.Name);
        }
    }
}