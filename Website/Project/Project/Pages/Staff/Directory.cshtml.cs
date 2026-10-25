using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Pages.Shared;

namespace Project.Pages.Staff
{
    public class DirectoryModel : PageModel
    {
        private readonly LibraryDbContext _db;

        public DirectoryModel(LibraryDbContext db)
        {
            _db = db;
        }

        [BindProperty(SupportsGet = true)]
        [StringLength(100,
            ErrorMessage = "Please use 100 characters or fewer.")]
        public string? Search { get; set; }

        public List<PersonRow> StaffRows { get; set; } = new();
        public List<PersonRow> FacultyRows { get; set; } = new();

        public bool MoreStaff { get; set; }
        public bool MoreFaculty { get; set; }

        public class PersonRow
        {
            public int? StaffId { get; set; }
            public string? FacultyId { get; set; }

            public string FullName { get; set; } = "";
            public string? Position { get; set; }

            public bool? StaffIsActive { get; set; }

            public int? AccountId { get; set; }
            public string? LoginEmail { get; set; }
            public string? AccountRole { get; set; }
            public bool? LoginIsEnabled { get; set; }

            public string RecordId =>
                StaffId?.ToString() ?? FacultyId ?? "";
        }

        public async Task OnGetAsync()
        {
            if (!ModelState.IsValid)
            {
                return;
            }

            string search = Search?.Trim() ?? "";

            var staffQuery =
                from staff in _db.StaffMembers.AsNoTracking()
                join account in _db.UserAccounts.AsNoTracking()
                    on staff.StaffId equals account.StaffId
                    into accounts
                from account in accounts.DefaultIfEmpty()
                select new PersonRow
                {
                    StaffId = staff.StaffId,
                    FullName = staff.FirstName + " " + staff.LastName,
                    Position = staff.Role,
                    StaffIsActive = staff.IsActive,
                    AccountId = account == null
                        ? null
                        : (int?)account.AccountId,
                    LoginEmail = account == null
                        ? null
                        : account.Email,
                    AccountRole = account == null
                        ? null
                        : account.Role,
                    LoginIsEnabled = account == null
                        ? null
                        : (bool?)account.IsEnabled
                };

            var facultyQuery =
                from faculty in _db.FacultyMembers.AsNoTracking()
                join account in _db.UserAccounts.AsNoTracking()
                    on faculty.LibraryUserId equals account.LibraryUserId
                    into accounts
                from account in accounts.DefaultIfEmpty()
                select new PersonRow
                {
                    FacultyId = faculty.FacultyId,
                    FullName = faculty.FirstName + " " + faculty.LastName,
                    Position = faculty.Position,
                    AccountId = account == null
                        ? null
                        : (int?)account.AccountId,
                    LoginEmail = account == null
                        ? null
                        : account.Email,
                    AccountRole = account == null
                        ? null
                        : account.Role,
                    LoginIsEnabled = account == null
                        ? null
                        : (bool?)account.IsEnabled
                };

            if (!string.IsNullOrEmpty(search))
            {
                bool isStaffId = int.TryParse(search, out int staffId);

                staffQuery = staffQuery.Where(row =>
                    row.FullName.Contains(search) ||
                    (row.LoginEmail != null &&
                     row.LoginEmail.Contains(search)) ||
                    (isStaffId && row.StaffId == staffId));

                facultyQuery = facultyQuery.Where(row =>
                    row.FullName.Contains(search) ||
                    (row.LoginEmail != null &&
                     row.LoginEmail.Contains(search)) ||
                    row.FacultyId == search);
            }

            var staffResults = await staffQuery
                .OrderBy(row => row.FullName)
                .ThenBy(row => row.StaffId)
                .Take(101)
                .ToListAsync();

            var facultyResults = await facultyQuery
                .OrderBy(row => row.FullName)
                .ThenBy(row => row.FacultyId)
                .Take(101)
                .ToListAsync();

            MoreStaff = staffResults.Count > 100;
            MoreFaculty = facultyResults.Count > 100;

            StaffRows = staffResults.Take(100).ToList();
            FacultyRows = facultyResults.Take(100).ToList();
        }
    }
}