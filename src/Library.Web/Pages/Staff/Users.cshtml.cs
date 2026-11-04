using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Project.Models;
using Project.Data;
using Project.Configuration;

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

        
        [TempData]
        public string? TempPassword { get; set; }

        [TempData]
        public string? TempPasswordFor { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        
        [BindProperty(SupportsGet = true)]
        public string? MemberType { get; set; }

      
        [BindProperty(SupportsGet = true)]
        public string? Login { get; set; }

        public List<BorrowerRow> Borrowers { get; set; } = new();

        public bool HasMoreResults { get; set; }

        public int CountTotal { get; set; }
        public int CountNoLogin { get; set; }
        public int CountInactive { get; set; }

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

            public bool IsStudent { get; set; }
            public bool IsFaculty { get; set; }
            public string? RecordEmail { get; set; }

            public string MemberType =>
                StaffId.HasValue ? "Staff"
                : IsStudent && IsFaculty ? "Student & Faculty"
                : IsStudent ? "Student"
                : IsFaculty ? "Faculty"
                : Role ?? "—";

            public bool CanManage =>
                StaffId == null && (AccountId == null || Role == "Student" || Role == "Faculty");

            
            public bool CanCreateLogin =>
                StaffId == null && AccountId == null && (IsStudent ^ IsFaculty);
        }

        public async Task OnGetAsync()
        {
            var query =
                from borrower in _db.LibraryUsers.AsNoTracking()
                join employee in _db.StaffMembers
                    on (int?)borrower.LibraryUserId equals employee.LibraryUserId into employees
                from employee in employees.DefaultIfEmpty()
                // Employee logins point to Staff. Other logins point straight to the borrower.
                from account in _db.UserAccounts.Where(a =>
                    (employee != null && a.StaffId == employee.StaffId) ||
                    (employee == null && a.LibraryUserId == borrower.LibraryUserId)).DefaultIfEmpty()
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
                    StaffId = employee == null ? (account == null ? null : account.StaffId) : employee.StaffId,
                    IsStudent = _db.Students.Any(s => s.LibraryUserId == borrower.LibraryUserId),
                    IsFaculty = _db.FacultyMembers.Any(f => f.LibraryUserId == borrower.LibraryUserId),
                    RecordEmail = _db.Students
                        .Where(s => s.LibraryUserId == borrower.LibraryUserId)
                        .Select(s => s.Email)
                        .FirstOrDefault()
                };

            
            CountTotal = await query.CountAsync();
            CountNoLogin = await query.CountAsync(r => r.AccountId == null);
            CountInactive = await query.CountAsync(r => !r.BorrowerIsActive);

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

            if (MemberType == "Student")
                query = query.Where(r => r.IsStudent);
            else if (MemberType == "Faculty")
                query = query.Where(r => r.IsFaculty);
            else if (MemberType == "Staff")
                query = query.Where(r => r.StaffId != null);

            if (Login == "none")
                query = query.Where(r => r.AccountId == null);
            else if (Login == "enabled")
                query = query.Where(r => r.AccountId != null && r.LoginIsEnabled == true);
            else if (Login == "disabled")
                query = query.Where(r => r.AccountId != null && r.LoginIsEnabled == false);

            var results = await query
                .OrderBy(row => row.FullName)
                .ThenBy(row => row.LibraryUserId)
                .Take(101)
                .ToListAsync();

            HasMoreResults = results.Count > 100;

            Borrowers = results.Take(100).ToList();
        }

       
        public async Task<IActionResult> OnPostCreateLoginAsync(
            int libraryUserId,
            string? email)
        {
            email = email?.Trim() ?? "";

            if (libraryUserId <= 0)
            {
                return BadRequest();
            }

            if (email.Length == 0 || email.Length > 255 ||
                !new EmailAddressAttribute().IsValid(email))
            {
                ErrorMessage = "Enter a valid email address for the new login.";
                return RedirectToPage(new { Search, MemberType, Login });
            }

            string normalizedEmail = email.ToUpperInvariant();

            try
            {
                await using var transaction =
                    await _db.Database.BeginTransactionAsync();

                var borrower = await _db.LibraryUsers
                    .SingleOrDefaultAsync(b => b.LibraryUserId == libraryUserId);

                if (borrower == null)
                {
                    ErrorMessage = "That borrower was not found.";
                    return RedirectToPage(new { Search, MemberType, Login });
                }

                if (await _db.StaffMembers.AnyAsync(s => s.LibraryUserId == libraryUserId))
                    return Forbid();

                if (await _db.UserAccounts.AnyAsync(a => a.LibraryUserId == libraryUserId))
                {
                    ErrorMessage = $"{borrower.FullName} already has a login.";
                    return RedirectToPage(new { Search, MemberType, Login });
                }

                bool isStudent = await _db.Students.AnyAsync(s => s.LibraryUserId == libraryUserId);
                bool isFaculty = await _db.FacultyMembers.AnyAsync(f => f.LibraryUserId == libraryUserId);

                if (isStudent == isFaculty)
                {
                    ErrorMessage = isStudent
                        ? $"{borrower.FullName} is linked to both Student and Faculty records. Review that profile first."
                        : $"{borrower.FullName} has no Student or Faculty record, so the login role is unknown.";
                    return RedirectToPage(new { Search, MemberType, Login });
                }

                if (await _db.UserAccounts.AnyAsync(a => a.NormalizedEmail == normalizedEmail))
                {
                    ErrorMessage = "That email is already used by another login.";
                    return RedirectToPage(new { Search, MemberType, Login });
                }

                string tempPassword = GenerateTemporaryPassword();

                var account = new UserAccount
                {
                    Email = email,
                    NormalizedEmail = normalizedEmail,
                    Role = isStudent ? "Student" : "Faculty",
                    IsEnabled = true,
                    LibraryUserId = libraryUserId,
                    StaffId = null,
                    FailedLoginAttempts = 0,
                    LockoutUntilUtc = null
                };

              
                var hasher = new PasswordHasher<UserAccount>();
                account.PasswordHash = hasher.HashPassword(account, tempPassword);

                _db.UserAccounts.Add(account);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                StatusMessage = $"Login created for {borrower.FullName} ({account.Role}) with email {email}.";
                TempPassword = tempPassword;
                TempPasswordFor = borrower.FullName;
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is MySqlException mysql &&
                      mysql.Number == 1062)
            {
                ErrorMessage = "That email or borrower already has a login.";
            }
            catch (Exception ex)
                when (ex is DbException || ex is DbUpdateException)
            {
                _logger.LogError(ex, "Unable to create login for borrower {LibraryUserId}.", libraryUserId);
                ErrorMessage = "The login could not be created. Refresh the page and check before trying again.";
            }

            return RedirectToPage(new { Search, MemberType, Login });
        }

        
        private static string GenerateTemporaryPassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
            return RandomNumberGenerator.GetString(chars, 14);
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
                    .Where(b => !_db.StaffMembers.Any(s => s.LibraryUserId == b.LibraryUserId))
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

            return RedirectToPage(new { Search, MemberType, Login });
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
                    .Where(a => !_db.StaffMembers.Any(s => s.LibraryUserId == libraryUserId))
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

            return RedirectToPage(new { Search, MemberType, Login });
        }
    }
}
