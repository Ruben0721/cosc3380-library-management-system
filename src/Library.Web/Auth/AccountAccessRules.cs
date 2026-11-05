using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Project.Models;
using Project.Data;
using Project.Configuration;

namespace Project.Auth
{
    public static class AccountAccessRules
    {
        // The login cookie must have the same version as the database
        public static bool HasCurrentSessionVersion(ClaimsPrincipal? principal, UserAccount account) =>
            int.TryParse(principal?.FindFirstValue("session_version"), out var version)
            && version == account.SessionVersion;

        // Staff can reset students and faculty. Admins can also reset staff.
        public static bool CanResetPassword(ClaimsPrincipal actor, string? targetRole) =>
            actor.Identity?.IsAuthenticated == true &&
            (((targetRole == "Student" || targetRole == "Faculty") &&
              (actor.IsInRole("Staff") || actor.IsInRole("Admin")))
             || (targetRole == "Staff" && actor.IsInRole("Admin")));

        // Staff cannot edit an Admin account or make someone an Admin
        public static bool CanManageAccountRole(ClaimsPrincipal actor, string? currentRole, string? requestedRole) =>
            actor.Identity?.IsAuthenticated == true &&
            (actor.IsInRole("Admin") ||
             (actor.IsInRole("Staff") && currentRole != "Admin" && requestedRole != "Admin"));

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
