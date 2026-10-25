using Microsoft.EntityFrameworkCore;
using Project.Pages.Shared;

namespace Project.Auth
{
    public static class AccountAccessRules
    {
        public static async Task<bool> CanSignInAsync(
            LibraryDbContext db,
            UserAccount account)
        {
            if (!account.IsEnabled)
            {
                return false;
            }

            bool knownRole =
                account.Role == "Student" ||
                account.Role == "Faculty" ||
                account.Role == "Staff" ||
                account.Role == "Admin";

            if (!knownRole)
            {
                return false;
            }

            bool staffRole =
                account.Role == "Staff" ||
                account.Role == "Admin";

            if (staffRole && !account.StaffId.HasValue)
            {
                return false;
            }

            if (account.StaffId.HasValue)
            {
                return await db.StaffMembers
                    .AsNoTracking()
                    .AnyAsync(s =>
                        s.StaffId == account.StaffId.Value &&
                        s.IsActive);
            }

            return true;
        }
    }
}