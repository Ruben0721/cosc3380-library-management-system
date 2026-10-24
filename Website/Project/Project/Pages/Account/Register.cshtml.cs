using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Project.Pages.Shared;

namespace Project.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly LibraryDbContext _db;
        private readonly ILogger<RegisterModel> _logger;

        public RegisterModel(
            LibraryDbContext db,
            ILogger<RegisterModel> logger)
        {
            _db = db;
            _logger = logger;
        }

        [BindProperty]
        public RegistrationInput Input { get; set; } = new();

        [TempData]
        public string? ValidationMessage { get; set; }

        public class RegistrationInput
        {
            [Required]
            [StringLength(30)]
            [Display(Name = "First name")]
            public string FirstName { get; set; } = "";

            [Required]
            [StringLength(30)]
            [Display(Name = "Last name")]
            public string LastName { get; set; } = "";

            [Required]
            [RegularExpression(
                @"[0-9]{7}",
                ErrorMessage = "Enter your seven-digit student ID.")]
            [Display(Name = "Student ID")]
            public string StudentId { get; set; } = "";

            [Required]
            [EmailAddress]
            [StringLength(255)]
            public string Email { get; set; } = "";

            [Required]
            [StringLength(
                128,
                MinimumLength = 12,
                ErrorMessage = "Use a password between 12 and 128 characters.")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";

            [Required]
            [Compare(
                nameof(Password),
                ErrorMessage = "The passwords do not match.")]
            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            public string ConfirmPassword { get; set; } = "";
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            string email = Input.Email.Trim();
            string normalizedEmail = email.ToUpperInvariant();

            try
            {
                // An existing student must not be claimed through public signup.
                bool studentExists = await _db.Students
                    .AnyAsync(s => s.StudentId == Input.StudentId);

                if (studentExists)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Unable to register with those details. " +
                        "If you already have a library record, contact library staff.");

                    return Page();
                }

                bool emailExists = await _db.UserAccounts
                    .AnyAsync(a => a.NormalizedEmail == normalizedEmail);

                if (emailExists)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Unable to register with those details. " +
                        "You may already have an account.");

                    return Page();
                }

                // Everything below must succeed together.
                await using var transaction =
                    await _db.Database.BeginTransactionAsync();

                // 1. Create the borrower.
                var borrower = new LibraryUser
                {
                    FullName =
                        $"{Input.FirstName.Trim()} {Input.LastName.Trim()}",
                    IsActive = true,
                    UserStatus = null
                };

                _db.LibraryUsers.Add(borrower);

                // MySQL generates the borrower's ID during this save.
                await _db.SaveChangesAsync();

                // 2. Create the student using that generated ID.
                var student = new Student
                {
                    StudentId = Input.StudentId,
                    FirstName = Input.FirstName.Trim(),
                    LastName = Input.LastName.Trim(),
                    RegistrationDate = DateTime.UtcNow,
                    Email = email,
                    LibraryUserId = borrower.LibraryUserId
                };

                // 3. Create the login account using the same borrower ID.
                var account = new UserAccount
                {
                    Email = email,
                    NormalizedEmail = normalizedEmail,
                    Role = "Student",
                    IsEnabled = true,
                    LibraryUserId = borrower.LibraryUserId,
                    StaffId = null
                };

                var passwordHasher = new PasswordHasher<UserAccount>();

                account.PasswordHash =
                    passwordHasher.HashPassword(account, Input.Password);

                _db.Students.Add(student);
                _db.UserAccounts.Add(account);

                await _db.SaveChangesAsync();

                // Make all three records permanent together.
                await transaction.CommitAsync();

                ValidationMessage =
                    "Your student account was created successfully. " +
                    "You can now log in.";

                return RedirectToPage();
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is MySqlException mysql &&
                      mysql.Number == 1062)
            {
                // A duplicate can still occur if two submissions arrive together.
                ModelState.AddModelError(
                    string.Empty,
                    "Unable to register with those details. " +
                    "The email or student ID may already be registered.");

                return Page();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Unable to save student registration.");

                ModelState.AddModelError(
                    string.Empty,
                    "Registration could not be completed. Please try again later.");

                return Page();
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "Database error during student registration.");

                ModelState.AddModelError(
                    string.Empty,
                    "The database is currently unavailable. Please try again later.");

                return Page();
            }
        }
    }
}