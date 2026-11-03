using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;

namespace Project.Pages.Staff
{
    public record MaintRequestRow(int RequestId, int ItemId, string? ItemName, string Category, string RequestType,
                                  string? Reason, string Status, DateTime CreatedAt, DateTime? ResolvedAt,
                                  int DaysOpen, string ItemStatus, string? Notes);

    // Work through maintenance requests.
    // Setting a request to Resolved fires trg_maintenance_resolved, which stamps
    // ResolvedAt and puts the item back to Available (if nothing else is open on it).
    public class MaintenanceModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public MaintenanceModel(LibraryDbContext db) => _db = db;

        public static readonly string[] NewStatuses = { "In Progress", "Resolved", "Replaced" };
        private static readonly string[] OpenStatuses = { "Open", "In Progress" };

        [BindProperty(SupportsGet = true)] public string? Show { get; set; }   // "" = open, "closed", "all"

        [TempData] public string? Message { get; set; }
        [TempData] public string? Error { get; set; }

        public List<MaintRequestRow> Requests { get; set; } = new();

        public async Task OnGetAsync()
        {
            var now = DateTime.Now;
            var items = await _db.Inventory.AsNoTracking()
                .Select(i => new { i.Id, i.Name, i.Type, i.Status })
                .ToDictionaryAsync(i => i.Id);

            var query = _db.MaintenanceRequests.AsNoTracking();
            query = Show switch
            {
                "closed" => query.Where(m => !OpenStatuses.Contains(m.Status)),
                "all" => query,
                _ => query.Where(m => OpenStatuses.Contains(m.Status))
            };

            Requests = (await query.ToListAsync())
                .Where(m => items.ContainsKey(m.ItemId))
                .Select(m =>
                {
                    var it = items[m.ItemId];
                    return new MaintRequestRow(m.RequestId, m.ItemId, it.Name, LabelOf(it.Type), m.RequestType, m.Reason,
                        m.Status, m.CreatedAt, m.ResolvedAt, ((m.ResolvedAt ?? now) - m.CreatedAt).Days,
                        it.Status ?? "", m.Notes);
                })
                .OrderBy(r => OpenStatuses.Contains(r.Status) ? 0 : 1)
                .ThenBy(r => r.RequestType == "Replacement Review" ? 0 : r.RequestType == "Damage Repair" ? 1 : 2)
                .ThenBy(r => r.CreatedAt)
                .ToList();
        }

        public async Task<IActionResult> OnPostUpdateAsync(int requestId, string status, string? notes)
        {
            if (!NewStatuses.Contains(status))
            {
                Error = "Choose In Progress, Resolved or Replaced.";
                return RedirectToPage(new { Show });
            }

            var request = await _db.MaintenanceRequests.FirstOrDefaultAsync(m => m.RequestId == requestId);
            if (request == null || !OpenStatuses.Contains(request.Status))
            {
                Error = "That request was not found or is already closed.";
                return RedirectToPage(new { Show });
            }

            request.Status = status;
            if (!string.IsNullOrWhiteSpace(notes))
            {
                var stamped = $"{DateTime.Now:M/d} {User.Identity?.Name}: {notes.Trim()}";
                request.Notes = string.IsNullOrEmpty(request.Notes) ? stamped : $"{request.Notes} | {stamped}";
                if (request.Notes.Length > 200) request.Notes = request.Notes[^200..];
            }
            await _db.SaveChangesAsync();   // trigger stamps ResolvedAt / frees the item

            string extra = "";
            if (status == "Replaced")
            {
                // A new unit replaces the old one: close the item's other open requests
                // and put the item back in service.
                var others = await _db.MaintenanceRequests
                    .Where(m => m.ItemId == request.ItemId && m.RequestId != request.RequestId
                             && OpenStatuses.Contains(m.Status))
                    .ToListAsync();
                foreach (var o in others)
                {
                    o.Status = "Replaced";
                    o.Notes = string.IsNullOrEmpty(o.Notes) ? "Closed: item replaced" : $"{o.Notes} | Closed: item replaced";
                    if (o.Notes.Length > 200) o.Notes = o.Notes[^200..];
                }
                await _db.SaveChangesAsync();

                bool onLoan = await _db.Rentals.AnyAsync(r => r.ItemId == request.ItemId && !r.IsReturned);
                if (!onLoan)
                    await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE Inventory SET Status = 'Available' WHERE Id = {request.ItemId}");

                if (others.Count > 0) extra = $" {others.Count} other open request(s) on this item were closed.";
            }

            var item = await _db.Inventory.AsNoTracking()
                .Where(i => i.Id == request.ItemId)
                .Select(i => new { i.Name, i.Status })
                .FirstOrDefaultAsync();

            int stillOpen = await _db.MaintenanceRequests
                .CountAsync(m => m.ItemId == request.ItemId && OpenStatuses.Contains(m.Status));

            Message = $"Request #{request.RequestId} for \"{item?.Name}\" set to {status}.{extra} " +
                      $"Item status is now: {item?.Status}." +
                      (stillOpen > 0 && status != "In Progress" ? $" ({stillOpen} request(s) still open on this item.)" : "");
            return RedirectToPage(new { Show });
        }

        private static string LabelOf(string type)
        {
            foreach (var t in LibraryOptions.Types)
                if (t.Value == type) return t.Label;
            return type;
        }
    }
}