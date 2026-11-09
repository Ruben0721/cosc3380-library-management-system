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
    public string Email { get; private set; } = "";
    public string Role { get; private set; } = "";
    public string? FullName { get; private set; }
    public record Loan(string? Item, DateTime? Due);
    public record HoldRow(string? Item, string Status, DateTime? Deadline);
    public record FineRow(string? Item, string Reason, decimal Amount);
    public record RoomReservationRow(string ReservationId, string RoomId,
        DateTime StartTime, DateTime EndTime, int NumberOfPeople,
        string Purpose, string Status);
    public List<Loan> Loans { get; private set; } = new();
    public List<HoldRow> Holds { get; private set; } = new();
    public List<FineRow> Fines { get; private set; } = new();
    public List<RoomReservationRow> RoomReservations { get; private set; } = new();
    public decimal TotalDue => Fines.Sum(f => f.Amount);

    public async Task<IActionResult> OnGetAsync()
    {
        // Use the account ID from the login cookie so people only see their own records
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            return Challenge();

        var account = await db.UserAccounts.Where(a => a.AccountId == accountId && a.IsEnabled)
            .Select(a => new { a.Email, a.Role, a.StaffId, a.LibraryUserId })
            .SingleOrDefaultAsync();
        if (account == null) return Challenge();

        Email = account.Email;
        Role = account.Role;
        // Staff/admins have their borrower link on the Staff record
        var borrowerId = account.StaffId.HasValue
            ? await db.StaffMembers.Where(s => s.StaffId == account.StaffId)
                .Select(s => s.LibraryUserId).SingleOrDefaultAsync()
            : account.LibraryUserId;

        FullName = await db.LibraryUsers.Where(u => u.LibraryUserId == borrowerId)
            .Select(u => u.FullName).SingleOrDefaultAsync();
        if (FullName == null) return Page();

        Loans = await (from r in db.Rentals
                       where r.LibraryUserId == borrowerId && !r.IsReturned && r.ReturnedDate == null
                       orderby r.EndDate == null, r.EndDate, r.RentalId
                       select new Loan(db.Inventory.Where(i => i.Id == r.ItemId)
                           .Select(i => i.Name).FirstOrDefault(), r.EndDate)).ToListAsync();

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
}
