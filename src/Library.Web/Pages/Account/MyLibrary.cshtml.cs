using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;

namespace Project.Pages.Account;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MyLibraryModel(LibraryDbContext db) : PageModel
{
    public string ActiveTab { get; private set; } = "overview";

    public string Email { get; private set; } = "";
    public string Role { get; private set; } = "";
    public string? FullName { get; private set; }
 
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

    public async Task<IActionResult> OnGetAsync(string? tab)
    {
       
        ActiveTab = tab switch
        {
            "checkouts" or "requests" or "readinglist" or "fines" or "personal" => tab,
            _ => "overview"
        };

        var borrowerId = await LoadAccountAsync();
        if (borrowerId == -1) return Challenge();

        FullName = await db.LibraryUsers.Where(u => u.LibraryUserId == borrowerId)
            .Select(u => u.FullName).SingleOrDefaultAsync();
        if (FullName == null) return Page();

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
            .Select(a => new { a.Email, a.Role, a.StaffId, a.LibraryUserId })
            .SingleOrDefaultAsync();
        if (account == null) return -1;

        Email = account.Email;
        Role = account.Role;
        // Staff/admins have their borrower link on the Staff record
        return account.StaffId.HasValue
            ? await db.StaffMembers.Where(s => s.StaffId == account.StaffId)
                .Select(s => s.LibraryUserId).SingleOrDefaultAsync()
            : account.LibraryUserId;
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
