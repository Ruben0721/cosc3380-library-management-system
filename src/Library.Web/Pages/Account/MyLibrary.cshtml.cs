using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MySqlConnector;
using Project.Auth;
using Project.Models;

namespace Project.Pages.Account;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MyLibraryModel(LibraryDbContext db) : PageModel
{
    public string ActiveTab { get; private set; } = "overview";
    public bool IsEditing { get; private set; }

    [BindProperty] public ProfileInput Profile { get; set; } = new();

    public class ProfileInput
    {
        [Required, StringLength(30), Display(Name = "First name")]
        public string FirstName { get; set; } = "";
        [Required, StringLength(30), Display(Name = "Last name")]
        public string LastName { get; set; } = "";
        [Required, EmailAddress, StringLength(255)]
        public string Email { get; set; } = "";
        public ContactInput Contact { get; set; } = new();
    }

    public string Email { get; private set; } = "";
    public string Role { get; private set; } = "";
    public string? FullName { get; private set; }
    public int? MemberId { get; private set; }
    public int RentalLimit { get; private set; }
    public int WaitlistLimit { get; private set; }
public string? StudentNumber { get; private set; }
public string? FacultyNumber { get; private set; }
 
    public record Loan(string? Item, DateTime? Due, int DaysLate = 0, decimal Accruing = 0,
                       int RentalId = 0, bool CanDropOff = false, DateTime? DroppedOffAt = null);

    //
    public static readonly string[] DropBoxTypes = { "book" };

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }
    public record HoldRow(string? Item, string Status, DateTime? Deadline);
    public record FineRow(string? Item, string Reason, decimal Amount);
    public record RoomReservationRow(string ReservationId, string RoomId,
        DateTime StartTime, DateTime EndTime, int NumberOfPeople,
        string Purpose, string Status);
    public List<Loan> Loans { get; private set; } = new();
    public List<HoldRow> Holds { get; private set; } = new();
    public List<FineRow> Fines { get; private set; } = new();
    public List<RoomReservationRow> RoomReservations { get; private set; } = new();
    public record SavedRow(int ItemId, string? Title, string? Type, DateTime Added, string Status, bool Highlight);
    public List<SavedRow> ReadingList { get; private set; } = new();
    public decimal TotalDue => Fines.Sum(f => f.Amount);
    public List<Loan> OverdueLoans => Loans.Where(l => l.DaysLate > 0).ToList();
    public decimal AccruingTotal => Loans.Sum(l => l.Accruing);

    public async Task<IActionResult> OnGetAsync(string? tab, bool edit = false)
    {
       
        ActiveTab = tab switch
        {
            "checkouts" or "requests" or "readinglist" or "fines" or "personal" => tab,
            _ => "overview"
        };
        IsEditing = ActiveTab == "personal" && edit;

        var borrowerId = await LoadAccountAsync();
        if (borrowerId == -1) return Challenge();

        FullName = await db.LibraryUsers.Where(u => u.LibraryUserId == borrowerId)
            .Select(u => u.FullName).SingleOrDefaultAsync();
        if (FullName == null) return Page();
        MemberId = borrowerId;
        RentalLimit = await Circulation.RentalLimitAsync(db, borrowerId!.Value);
        WaitlistLimit = await Circulation.WaitlistLimitAsync(db, borrowerId.Value);
        StudentNumber = await db.Database
            .SqlQuery<string>($"SELECT CAST(Student_ID AS CHAR) AS Value FROM Student WHERE LibraryUserId = {borrowerId}")
            .FirstOrDefaultAsync();
        FacultyNumber = await db.Database
            .SqlQuery<string>($"SELECT CAST(faculty_id AS CHAR) AS Value FROM Faculty WHERE LibraryUserId = {borrowerId}")
            .FirstOrDefaultAsync();

        if (ActiveTab == "personal")
        {
            if (HttpMethods.IsGet(Request.Method))
            {
                var person = await db.LibraryUsers.AsNoTracking().SingleAsync(u => u.LibraryUserId == borrowerId);
                Profile = new ProfileInput
                {
                    FirstName = person.FirstName, LastName = person.LastName, Email = Email,
                    Contact = ContactInput.FromPerson(person)
                };
            }
            return Page();
        }

        var openLoans = await (from r in db.Rentals
                               where r.LibraryUserId == borrowerId && !r.IsReturned && r.ReturnedDate == null
                               orderby r.EndDate == null, r.EndDate, r.RentalId
                               select new { r.RentalId, r.ItemId, r.EndDate, r.DroppedOffAt }).ToListAsync();
        var loanItemIds = openLoans.Select(l => l.ItemId).ToList();
        var loanItems = await db.Inventory.AsNoTracking()
            .Where(i => loanItemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Name, i.Type })
            .ToDictionaryAsync(i => i.Id);
       
        var policies = await db.FinePolicies.AsNoTracking().ToDictionaryAsync(p => p.ItemType);
        var today = DateTime.Today;

        Loans = openLoans.Select(l =>
        {
            var item = loanItems.GetValueOrDefault(l.ItemId);
            // A dropped-off item stops building up a late fee on the day it was dropped
            var asOf = (l.DroppedOffAt ?? DateTime.Now).Date;
            int daysLate = l.EndDate != null && l.EndDate.Value.Date < asOf ? (asOf - l.EndDate.Value.Date).Days : 0;
            decimal accruing = 0;
            if (daysLate > 0 && item?.Type != null && policies.TryGetValue(item.Type, out var p))
                accruing = Math.Min(daysLate * p.DailyOverdueRate, p.OverdueCap);
            bool canDrop = l.DroppedOffAt == null && item?.Type != null && DropBoxTypes.Contains(item.Type);
            return new Loan(item?.Name, l.EndDate, daysLate, accruing, l.RentalId, canDrop, l.DroppedOffAt);
        }).ToList();

        Holds = await (from h in db.Holds
                       where h.LibraryUserId == borrowerId && (h.Status == "Pending" || h.Status == "Ready")
                       orderby h.Status == "Ready" descending, h.RequestDatetime
                       select new HoldRow(db.Inventory.Where(i => i.Id == h.ItemId)
                           .Select(i => i.Name).FirstOrDefault(), h.Status, h.PickupDeadline)).ToListAsync();

        Fines = await (from f in db.Fines
                       where f.LibraryUserId == borrowerId && !f.IsPaid
                       orderby f.DateAssessed descending, f.FineId
                       select new FineRow((from r in db.Rentals
                                           join i in db.Inventory on r.ItemId equals i.Id
                                           where r.RentalId == f.RentalId && r.LibraryUserId == borrowerId
                                           select i.Name).FirstOrDefault(), f.FineType, f.Amount)).ToListAsync();

        await LoadReadingListAsync(borrowerId!.Value);

        RoomReservations = await db.RoomReservations.AsNoTracking()
            .Where(r => r.LibraryUserId == borrowerId)
            .OrderBy(r => r.Status == "Cancelled")
            .ThenBy(r => r.StartTime)
            .Select(r => new RoomReservationRow(
                r.ReservationId,
                r.RoomId,
                r.StartTime,
                r.EndTime,
                r.NumberOfPeople,
                r.Purpose,
                r.Status))
            .ToListAsync();
        return Page();
    }

   
    public async Task<IActionResult> OnPostSavePersonalAsync()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            return Challenge();
        if (!ModelState.IsValid) return await OnGetAsync("personal", edit: true);

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var accounts = await db.UserAccounts.FromSqlInterpolated(
                $"SELECT * FROM UserAccount WHERE AccountId = {accountId} FOR UPDATE").ToListAsync();
            var account = accounts.SingleOrDefault();
            if (account == null || !AccountAccessRules.HasCurrentSessionVersion(User, account) ||
                !await AccountAccessRules.CanSignInAsync(db, account))
                return Challenge();

            var email = Profile.Email.Trim();
            var normalizedEmail = email.ToUpperInvariant();
            if (await db.UserAccounts.AnyAsync(a => a.AccountId != accountId && a.NormalizedEmail == normalizedEmail))
            {
                ModelState.AddModelError("Profile.Email", "That email address is already in use.");
                return await OnGetAsync("personal", edit: true);
            }

            var person = await db.LibraryUsers.SingleAsync(u => u.LibraryUserId == account.LibraryUserId);
            person.FirstName = Profile.FirstName.Trim();
            person.LastName = Profile.LastName.Trim();
            Profile.Contact.ApplyTo(person);
            bool emailChanged = account.Email != email;
            account.Email = email;
            account.NormalizedEmail = normalizedEmail;
            if (emailChanged) account.SessionVersion = checked(account.SessionVersion + 1);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            if (emailChanged)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                TempData["ProfileChangedMessage"] = "Your details were saved. Log in using your updated email address and existing password.";
                return RedirectToPage("/Account/Login");
            }
            Message = "Your personal information was saved.";
            return RedirectToPage(new { tab = "personal" });
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: 1062 })
        {
            ModelState.AddModelError("Profile.Email", "That email address is already in use.");
            db.ChangeTracker.Clear();
            return await OnGetAsync("personal", edit: true);
        }
    }

    public async Task<IActionResult> OnPostDropOffAsync(int rentalId)
    {
        var borrowerId = await LoadAccountAsync();
        if (borrowerId == -1) return Challenge();

        var loan = await (from r in db.Rentals
                          join i in db.Inventory on r.ItemId equals i.Id
                          where r.RentalId == rentalId && r.LibraryUserId == borrowerId && !r.IsReturned
                          select new { i.Name, i.Type, r.DroppedOffAt }).FirstOrDefaultAsync();

        if (loan == null)
            Error = "That loan wasn't found on your account.";
        else if (!DropBoxTypes.Contains(loan.Type))
            Error = "Only books can go in the book drop. Please return this item at the circulation desk.";
        else if (loan.DroppedOffAt != null)
            Error = $"\"{loan.Name}\" is already marked as dropped off.";
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Rentals SET DroppedOffAt = {DateTime.Now} WHERE RentalId = {rentalId} AND IsReturned = b'0' AND DroppedOffAt IS NULL");
            Message = $"Thanks. \"{loan.Name}\" is marked as dropped off. Late fees stop as of now; staff will check it in shortly.";
        }
        return RedirectToPage(new { tab = "checkouts" });
    }

   
    private async Task<int?> LoadAccountAsync()
    {
        // Use the account ID from the login cookie so people only see their own records
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            return -1;

        var account = await db.UserAccounts.Where(a => a.AccountId == accountId && a.IsEnabled)
            .Select(a => new { a.Email, a.Role, a.LibraryUserId })
            .SingleOrDefaultAsync();
        if (account == null) return -1;

        Email = account.Email;
        Role = account.Role;
        return account.LibraryUserId;
    }

   
    private async Task LoadReadingListAsync(int borrowerId)
    {
        var saved = await db.ReadingList.AsNoTracking()
            .Where(r => r.LibraryUserId == borrowerId)
            .OrderByDescending(r => r.AddedAt)
            .ToListAsync();
        if (saved.Count == 0) return;

        var items = await db.Inventory.AsNoTracking()
            .Select(i => new { i.Id, i.Name, i.Type, i.Status })
            .ToListAsync();
        var activeHolds = await db.Holds.AsNoTracking()
            .Where(h => h.ItemId != null && (h.Status == "Ready" || h.Status == "Pending"))
            .Select(h => new { ItemId = h.ItemId!.Value, h.LibraryUserId, h.Status })
            .ToListAsync();
        var myLoanItems = await db.Rentals.AsNoTracking()
            .Where(r => r.LibraryUserId == borrowerId && !r.IsReturned)
            .Select(r => r.ItemId)
            .ToListAsync();

        foreach (var s in saved)
        {
            var it = items.FirstOrDefault(i => i.Id == s.ItemId);
            if (it == null) continue;
            var copies = items.Where(i => i.Name == it.Name && i.Type == it.Type).Select(i => i.Id).ToHashSet();
            var holds = activeHolds.Where(h => copies.Contains(h.ItemId)).ToList();
            var setAside = holds.Where(h => h.Status == "Ready").Select(h => h.ItemId).ToHashSet();
            int free = items.Count(i => copies.Contains(i.Id) && i.Status == "Available" && !setAside.Contains(i.Id));

            (string status, bool highlight) =
                myLoanItems.Any(copies.Contains) ? ("Checked out to you", false)
                : holds.Any(h => h.LibraryUserId == borrowerId && h.Status == "Ready") ? ("Ready for pickup", true)
                : holds.Any(h => h.LibraryUserId == borrowerId && h.Status == "Pending") ? ("On hold, waiting in line", false)
                : free > 0 ? ($"Available ({free} on the shelf)", true)
                : ("All copies checked out", false);

            ReadingList.Add(new SavedRow(s.ItemId, it.Name, it.Type, s.AddedAt, status, highlight));
        }
    }

    public async Task<IActionResult> OnPostRemoveSavedAsync(int itemId)
    {
        var borrowerId = await LoadAccountAsync();
        if (borrowerId == -1) return Challenge();
        var row = await db.ReadingList.FirstOrDefaultAsync(r => r.LibraryUserId == borrowerId && r.ItemId == itemId);
        if (row != null)
        {
            db.ReadingList.Remove(row);
            await db.SaveChangesAsync();
            Message = "Removed from your reading list.";
        }
        return RedirectToPage(new { tab = "readinglist" });
    }
}
