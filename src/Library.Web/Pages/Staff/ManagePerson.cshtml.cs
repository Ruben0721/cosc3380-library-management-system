using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
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
    public class ManagePersonModel : PageModel
    {
        private readonly LibraryDbContext _db;
        private readonly ILogger<ManagePersonModel> _logger;

        private StaffMember? _staff;
        private FacultyMember? _faculty;
        private LibraryUser? _borrower;
        private UserAccount? _account;

        public ManagePersonModel(
            LibraryDbContext db,
            ILogger<ManagePersonModel> logger)
        {
            _db = db;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        public string Kind { get; set; } = "Staff";

        [BindProperty(SupportsGet = true)]
        public string? Id { get; set; }

        [BindProperty]
        public PersonInput Input { get; set; } = new();

        [BindProperty]
        public int? ExpectedAccountId { get; set; }

        [TempData]
        public string? SavedMessage { get; set; }

        public bool IsNew => string.IsNullOrEmpty(Id);
        public bool IsStaff => Kind == "Staff";
        public bool NeedsLogin { get; private set; }

        public class PersonInput
        {
            [Required, StringLength(30)]
            [Display(Name = "First name")]
            public string FirstName { get; set; } = "";

            [Required, StringLength(30)]
            [Display(Name = "Last name")]
            public string LastName { get; set; } = "";

            [Required, EmailAddress, StringLength(255)]
            [Display(Name = "Login email")]
            public string Email { get; set; } = "";

            [StringLength(7)]
            [Display(Name = "Faculty ID")]
            public string? FacultyId { get; set; }

            [StringLength(50)]
            [Display(Name = "Job title / position")]
            public string? Position { get; set; }

            [DataType(DataType.Date)]
            [Display(Name = "Employment start date")]
            public DateTime? StartDate { get; set; }

            [Display(Name = "Website role")]
            public string WebsiteRole { get; set; } = "Staff";

            [Range(0, int.MaxValue)]
            [Display(Name = "Rental limit")]
            public int? RentalLimit { get; set; }

            [Range(0, int.MaxValue)]
            [Display(Name = "Waitlist limit")]
            public int? WaitlistLimit { get; set; }

            [DataType(DataType.Password)]
            [StringLength(128, MinimumLength = 12)]
            public string? Password { get; set; }

            [DataType(DataType.Password)]
            [Compare(nameof(Password),
                ErrorMessage = "The passwords do not match.")]
            [Display(Name = "Confirm password")]
            public string? ConfirmPassword { get; set; }
        }

        private async Task<string?> LoadPersonAsync()
        {
            if (Kind != "Staff" && Kind != "Faculty")
            {
                return "Choose Staff or Faculty.";
            }

            if (IsNew)
            {
                NeedsLogin = true;
                return null;
            }

            if (IsStaff)
            {
                if (!int.TryParse(Id, out int staffId) || staffId <= 0)
                {
                    return "Invalid Staff ID.";
                }

                _staff = await _db.StaffMembers
                    .SingleOrDefaultAsync(s => s.StaffId == staffId);

                if (_staff == null)
                {
                    return "The Staff record was not found.";
                }

                _account = await _db.UserAccounts
                    .SingleOrDefaultAsync(a => a.StaffId == staffId);

                if (_account != null &&
                    _account.Role != "Staff" &&
                    _account.Role != "Admin")
                {
                    return "This employee has a different account type. " +
                           "Resolve that account link before editing here.";
                }
            }
            else
            {
                _faculty = await _db.FacultyMembers
                    .SingleOrDefaultAsync(f => f.FacultyId == Id);

                if (_faculty == null)
                {
                    return "The Faculty record was not found.";
                }

                _borrower = await _db.LibraryUsers
                    .SingleOrDefaultAsync(b =>
                        b.LibraryUserId == _faculty.LibraryUserId);

                if (_borrower == null)
                {
                    return "The Faculty record has no valid borrower.";
                }

                _account = await _db.UserAccounts
                    .SingleOrDefaultAsync(a =>
                        a.LibraryUserId == _faculty.LibraryUserId);

                if (_account != null &&
                    (_account.Role != "Faculty" ||
                     _account.StaffId != null))
                {
                    return "This borrower already has a different account " +
                           "type. This form will not convert that account.";
                }

                bool alsoStudent = await _db.Students.AnyAsync(s =>
                    s.LibraryUserId == _faculty.LibraryUserId);

                if (alsoStudent)
                {
                    return "This borrower is linked to both Student and " +
                           "Faculty records. Review that shared profile " +
                           "before editing here.";
                }
            }

            NeedsLogin = _account == null;
            return null;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            string? problem = await LoadPersonAsync();

            if (problem != null)
            {
                return BadRequest(problem);
            }

            ExpectedAccountId = _account?.AccountId;

            if (IsNew)
            {
                Input.StartDate = DateTime.Today;
                return Page();
            }

            Input.FirstName = IsStaff
                ? _staff!.FirstName
                : _faculty!.FirstName;

            Input.LastName = IsStaff
                ? _staff!.LastName
                : _faculty!.LastName;

            Input.Email = _account?.Email ?? _staff?.Email ?? "";

            Input.Position = IsStaff
                ? _staff!.Role
                : _faculty!.Position;

            Input.StartDate = _staff?.StartDate;
            Input.WebsiteRole = _account?.Role ?? "Staff";
            Input.FacultyId = _faculty?.FacultyId;
            Input.RentalLimit = _faculty?.RentalLimit;
            Input.WaitlistLimit = _faculty?.WaitlistLimit;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                await using var transaction =
                    await _db.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable);

                string? problem = await LoadPersonAsync();

                if (problem != null)
                {
                    return BadRequest(problem);
                }

                // Detect a login added or replaced after the form was opened.
                if (ExpectedAccountId != _account?.AccountId)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "The linked login changed. Reopen this person " +
                        "from the directory before saving.");

                    return Page();
                }

                Input.FirstName = Input.FirstName.Trim();
                Input.LastName = Input.LastName.Trim();
                Input.Email = Input.Email.Trim();
                Input.Position = Input.Position?.Trim();
                Input.FacultyId = Input.FacultyId?.Trim();

                if (string.IsNullOrWhiteSpace(Input.FirstName) ||
                    string.IsNullOrWhiteSpace(Input.LastName))
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "First and last name are required.");
                }

                if (IsStaff)
                {
                    if (string.IsNullOrWhiteSpace(Input.Position) ||
                        Input.Position.Length > 30)
                    {
                        ModelState.AddModelError(
                            "Input.Position",
                            "Enter a job title between 1 and 30 characters.");
                    }

                    if (!Input.StartDate.HasValue)
                    {
                        ModelState.AddModelError(
                            "Input.StartDate",
                            "Enter an employment start date.");
                    }

                    if (Input.WebsiteRole != "Staff" &&
                        Input.WebsiteRole != "Admin")
                    {
                        ModelState.AddModelError(
                            "Input.WebsiteRole",
                            "Choose Staff or Admin.");
                    }
                }
                else if (IsNew &&
                         (Input.FacultyId?.Length != 7))
                {
                    ModelState.AddModelError(
                        "Input.FacultyId",
                        "Enter a seven-character Faculty ID.");
                }

                if (NeedsLogin)
                {
                    if (string.IsNullOrWhiteSpace(Input.Password))
                    {
                        ModelState.AddModelError(
                            "Input.Password",
                            "A password is required for the new login.");
                    }

                    if (string.IsNullOrEmpty(Input.ConfirmPassword))
                    {
                        ModelState.AddModelError(
                            "Input.ConfirmPassword",
                            "Confirm the password.");
                    }
                }
                else if (!string.IsNullOrEmpty(Input.Password) ||
                         !string.IsNullOrEmpty(Input.ConfirmPassword))
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "This form does not reset existing passwords.");
                }

                if (!ModelState.IsValid)
                {
                    return Page();
                }

                string normalizedEmail = Input.Email.ToUpperInvariant();
                int currentAccountId = _account?.AccountId ?? 0;

                bool emailTaken = await _db.UserAccounts.AnyAsync(a =>
                    a.NormalizedEmail == normalizedEmail &&
                    a.AccountId != currentAccountId);

                if (emailTaken)
                {
                    ModelState.AddModelError(
                        "Input.Email",
                        "That email is already used by another login.");

                    return Page();
                }

                if (IsStaff)
                {
                    int currentStaffId = _staff?.StaffId ?? 0;

                    bool staffEmailTaken = await _db.StaffMembers.AnyAsync(s =>
                        s.Email == Input.Email &&
                        s.StaffId != currentStaffId);

                    if (staffEmailTaken)
                    {
                        ModelState.AddModelError(
                            "Input.Email",
                            "That email belongs to another Staff record. " +
                            "Select the existing person from the directory.");

                        return Page();
                    }
                }
                else if (IsNew &&
                         await _db.FacultyMembers.AnyAsync(f =>
                             f.FacultyId == Input.FacultyId))
                {
                    ModelState.AddModelError(
                        "Input.FacultyId",
                        "That Faculty ID already exists. Select that " +
                        "person from the directory.");

                    return Page();
                }

                if (IsStaff)
                {
                    if (IsNew)
                    {
                        _staff = new StaffMember
                        {
                            IsActive = true
                        };

                        _db.StaffMembers.Add(_staff);
                    }

                    _staff!.FirstName = Input.FirstName;
                    _staff.LastName = Input.LastName;
                    _staff.Role = Input.Position!;
                    _staff.StartDate = Input.StartDate!.Value.Date;
                    _staff.Email = Input.Email;
                }
                else
                {
                    if (IsNew)
                    {
                        _borrower = new LibraryUser
                        {
                            FullName =
                                $"{Input.FirstName} {Input.LastName}",
                            IsActive = true
                        };

                        _db.LibraryUsers.Add(_borrower);

                        // Obtain the generated borrower ID.
                        await _db.SaveChangesAsync();

                        _faculty = new FacultyMember
                        {
                            FacultyId = Input.FacultyId!,
                            LibraryUserId = _borrower.LibraryUserId
                        };

                        _db.FacultyMembers.Add(_faculty);
                    }

                    _faculty!.FirstName = Input.FirstName;
                    _faculty.LastName = Input.LastName;
                    _faculty.Position = Input.Position;
                    _faculty.RentalLimit = Input.RentalLimit;
                    _faculty.WaitlistLimit = Input.WaitlistLimit;

                    _borrower!.FullName =
                        $"{Input.FirstName} {Input.LastName}";
                }

                // Save profile changes and obtain a new Staff ID if needed.
                await _db.SaveChangesAsync();

                if (NeedsLogin)
                {
                    _account = new UserAccount
                    {
                        IsEnabled = true,
                        StaffId = IsStaff ? _staff!.StaffId : null,
                        LibraryUserId = IsStaff
                            ? null
                            : _faculty!.LibraryUserId,
                        FailedLoginAttempts = 0,
                        LockoutUntilUtc = null
                    };

                    var hasher = new PasswordHasher<UserAccount>();

                    _account.PasswordHash =
                        hasher.HashPassword(_account, Input.Password!);

                    _db.UserAccounts.Add(_account);
                }

                _account!.Email = Input.Email;
                _account.NormalizedEmail = normalizedEmail;
                _account.Role = IsStaff
                    ? Input.WebsiteRole
                    : "Faculty";

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                SavedMessage = NeedsLogin
                    ? "Person saved and login account created."
                    : "Changes saved. The password was not changed.";

                string savedId = IsStaff
                    ? _staff!.StaffId.ToString()
                    : _faculty!.FacultyId;

                return RedirectToPage(new { Kind, Id = savedId });
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is MySqlException mysql &&
                      mysql.Number == 1062)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "A matching ID, email, or linked account already " +
                    "exists. Check the directory before trying again.");
            }
            catch (Exception ex)
                when (ex is DbException || ex is DbUpdateException)
            {
                _logger.LogError(ex, "Unable to save Staff/Faculty account.");

                ModelState.AddModelError(
                    string.Empty,
                    "The save could not be confirmed. Check the directory " +
                    "before trying again.");
            }

            return Page();
        }
    }
}