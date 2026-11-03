using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;

namespace Project.Pages.Staff
{
    public record ActiveLoanRow(int RentalId, string? ItemName, string Category, string Borrower,
                                DateTime StartDate, DateTime? DueDate, int DaysOverdue, decimal EstimatedFine);

       public class ReturnsModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public ReturnsModel(LibraryDbContext db) => _db = db;

        public static readonly string[] Conditions = { "Good", "Damaged", "Lost" };

        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public bool OverdueOnly { get; set; }

        [TempData] public string? Message { get; set; }
        [TempData] public string? Error { get; set; }

        public List<ActiveLoanRow> Loans { get; set; } = new();

        public async Task OnGetAsync()
        {
            var now = DateTime.Now;
            var policies = await _db.FinePolicies.AsNoTracking().ToDictionaryAsync(p => p.ItemType);
            var names = await _db.LibraryUsers.AsNoTracking().ToDictionaryAsync(u => u.LibraryUserId, u => u.FullName);
            var items = await _db.Inventory.AsNoTracking()
                .Select(i => new { i.Id, i.Name, i.Type })
                .ToDictionaryAsync(i => i.Id);

            var active = await _db.Rentals.AsNoTracking()
                .Where(r => !r.IsReturned)
                .Select(r => new { r.RentalId, r.ItemId, r.LibraryUserId, r.StartDate, r.EndDate })
                .ToListAsync();

            Loans = active
                .Where(r => items.ContainsKey(r.ItemId))
                .Select(r =>
                {
                    var it = items[r.ItemId];
                    int days = r.EndDate != null && r.EndDate.Value.Date < now.Date ? (now.Date - r.EndDate.Value.Date).Days : 0;
                    decimal fine = days > 0 && policies.TryGetValue(it.Type, out var p)
                        ? Math.Min(days * p.DailyOverdueRate, p.OverdueCap) : 0;
                    return new ActiveLoanRow(r.RentalId, it.Name, LabelOf(it.Type),
                        names.GetValueOrDefault(r.LibraryUserId) ?? $"Member #{r.LibraryUserId}",
                        r.StartDate, r.EndDate, days, fine);
                })
                .Where(l => string.IsNullOrWhiteSpace(Search)
                         || (l.ItemName ?? "").Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)
                         || l.Borrower.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(l => !OverdueOnly || l.DaysOverdue > 0)
                .OrderByDescending(l => l.DaysOverdue).ThenBy(l => l.DueDate)
                .ToList();
        }

        public async Task<IActionResult> OnPostReturnAsync(int rentalId, string condition)
        {
            if (!Conditions.Contains(condition))
            {
                Error = "Choose the item's condition: Good, Damaged or Lost.";
                return RedirectToPage(new { Search, OverdueOnly });
            }

            var rental = await _db.Rentals.AsNoTracking().FirstOrDefaultAsync(r => r.RentalId == rentalId);
            if (rental == null || rental.IsReturned)
            {
                Error = "That loan was not found or has already been returned.";
                return RedirectToPage(new { Search, OverdueOnly });
            }

            // record the return, triggers do the rest.
            var returnedAt = DateTime.Now;
            int updated = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Rentals SET ReturnedDate = {returnedAt}, IsReturned = b'1', ReturnCondition = {condition} WHERE RentalId = {rentalId} AND IsReturned = b'0'");

            if (updated == 0)
            {
                Error = "That loan has already been returned.";
                return RedirectToPage(new { Search, OverdueOnly });
            }

            // Read back what the triggers did
            var item = await _db.Inventory.AsNoTracking()
                .Where(i => i.Id == rental.ItemId)
                .Select(i => new { i.Name, i.Status })
                .FirstOrDefaultAsync();
            var fines = await _db.Fines.AsNoTracking().Where(f => f.RentalId == rentalId).ToListAsync();
            var requests = await _db.MaintenanceRequests.AsNoTracking()
                .Where(m => m.RentalId == rentalId || (m.ItemId == rental.ItemId && m.CreatedAt >= returnedAt.AddMinutes(-1)))
                .Select(m => m.RequestType)
                .Distinct()
                .ToListAsync();

            var parts = new List<string> { $"\"{item?.Name}\" returned ({condition.ToLower()})." };
            parts.Add(fines.Count == 0
                ? "No fine."
                : "Fine assessed: " + string.Join(", ", fines.Select(f => $"{f.FineType} {f.Amount:C}")) + ".");
            if (requests.Count > 0)
                parts.Add("Maintenance request opened: " + string.Join(", ", requests) + ".");
            parts.Add($"Item status is now: {item?.Status}.");

            Message = string.Join(" ", parts);
            return RedirectToPage(new { Search, OverdueOnly });
        }

        private static string LabelOf(string type)
        {
            foreach (var t in LibraryOptions.Types)
                if (t.Value == type) return t.Label;
            return type;
        }
    }
}