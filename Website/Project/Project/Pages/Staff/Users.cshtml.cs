using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Pages.Shared;
using System.Data.Common;

namespace Project.Pages.Staff
{
    public class UsersModel : PageModel
    {
        private readonly LibraryDbContext _db;
        private readonly ILogger<UsersModel> _logger;

        public UsersModel(
            LibraryDbContext db,
            ILogger<UsersModel> logger)
        {
            _db = db;
            _logger = logger;
        }

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        public List<BorrowerRow> Borrowers { get; set; } = new();

        public bool HasMoreResults { get; set; }

        public class BorrowerRow
        {
            public int LibraryUserId { get; set; }
            public string FullName { get; set; } = "";
            public bool BorrowerIsActive { get; set; }
            public int? AccountId { get; set; }
            public string? Email { get; set; }
            public string? Role { get; set; }
            public bool? LoginIsEnabled { get; set; }
            public int? StaffId { get; set; }
            public bool CanManage =>
                AccountId == null ||
                (StaffId == null &&
                 (Role == "Student" || Role == "Faculty"));
        }

        public async Task OnGetAsync()
        {
            var query =
                from borrower in _db.LibraryUsers.AsNoTracking()
                join account in _db.UserAccounts.AsNoTracking()
                    on borrower.LibraryUserId equals account.LibraryUserId
                    into accounts
                from account in accounts.DefaultIfEmpty()
                select new BorrowerRow
                {
                    LibraryUserId = borrower.LibraryUserId,
                    FullName = borrower.FullName,
                    BorrowerIsActive = borrower.IsActive,
                    AccountId = account == null
                        ? null
                        : (int?)account.AccountId,
                    Email = account == null ? null : account.Email,
                    Role = account == null ? null : account.Role,
                    LoginIsEnabled = account == null
                        ? null
                        : (bool?)account.IsEnabled,
                    StaffId = account == null ? null : account.StaffId,
                };

            string search = Search?.Trim() ?? "";

            if (search.Length > 100)
            {
                ModelState.AddModelError(
                    nameof(Search),
                    "Please use 100 characters or fewer");

                return;
            }

            if (!string.IsNullOrEmpty(search))
            {
                bool isBorrowerId = int.TryParse(search, out int borrowerId);

                query = query.Where(row =>
                    row.FullName.Contains(search) ||
                    (row.Email != null && row.Email.Contains(search)) ||
                    (isBorrowerId && row.LibraryUserId == borrowerId));
            }

            var results = await query
                .OrderBy(row => row.FullName)
                .ThenBy(row => row.LibraryUserId)
                .Take(101)
                .ToListAsync();

            HasMoreResults = results.Count > 100;

            Borrowers = results.Take(100).ToList();
        }
        public async Task<IActionResult> OnPostSetBorrowerStatusAsync(
    int libraryUserId,
    bool? isActive)
        {
            if (!ModelState.IsValid ||
                libraryUserId <= 0 ||
                !isActive.HasValue)
            {
                return BadRequest();
            }

            bool desiredStatus = isActive.Value;

            try
            {
                int changed = await _db.LibraryUsers
                    .Where(b => b.LibraryUserId == libraryUserId)
                    .Where(b => !_db.UserAccounts.Any(a =>
                        a.LibraryUserId == b.LibraryUserId &&
                        (a.StaffId != null ||
                         (a.Role != "Student" && a.Role != "Faculty"))))
                    .Where(b => b.IsActive != desiredStatus)
                    .ExecuteUpdateAsync(updates => updates
                        .SetProperty(b => b.IsActive, desiredStatus));

                if (changed == 1)
                {
                    StatusMessage = desiredStatus
                        ? "Borrower activated."
                        : "Borrower deactivated.";
                }
                else
                {
                    ErrorMessage =
                        "No change was made. The borrower may already have " +
                        "that status, no longer exist, or be protected.";
                }
            }
            catch (DbException ex)
            {
                _logger.LogError(
                    ex,
                    "Unable to change borrower status for {LibraryUserId}.",
                    libraryUserId);

                ErrorMessage =
                    "The change could not be confirmed. Refresh the page " +
                    "to check the current status before trying again.";
            }

            return RedirectToPage(new { Search });
        }

        public async Task<IActionResult> OnPostSetLoginStatusAsync(
            int libraryUserId,
            bool? isEnabled)
        {
            if (!ModelState.IsValid ||
                libraryUserId <= 0 ||
                !isEnabled.HasValue)
            {
                return BadRequest();
            }

            bool desiredStatus = isEnabled.Value;

            try
            {
                int changed = await _db.UserAccounts
                    .Where(a =>
                        a.LibraryUserId == libraryUserId &&
                        a.StaffId == null &&
                        (a.Role == "Student" || a.Role == "Faculty"))
                    .Where(a => a.IsEnabled != desiredStatus)
                    .ExecuteUpdateAsync(updates => updates
                        .SetProperty(a => a.IsEnabled, desiredStatus));

                if (changed == 1)
                {
                    StatusMessage = desiredStatus
                        ? "Website login enabled."
                        : "Website login disabled.";
                }
                else
                {
                    ErrorMessage =
                        "No change was made. The login may already have " +
                        "that status, no longer exist, or be protected.";
                }
            }
            catch (DbException ex)
            {
                _logger.LogError(
                    ex,
                    "Unable to change login status for borrower {LibraryUserId}.",
                    libraryUserId);

                ErrorMessage =
                    "The change could not be confirmed. Refresh the page " +
                    "to check the current status before trying again.";
            }

            return RedirectToPage(new { Search });
        }
    }
}