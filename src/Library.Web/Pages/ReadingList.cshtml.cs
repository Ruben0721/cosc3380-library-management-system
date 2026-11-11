using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;

namespace Project.Pages
{
    public record ReadingListRow(int ItemId, string? Title, string Category, DateTime Added,
                                 string Availability, string Action, bool Highlight);

    // A member's saved titles, with where each one stands for them right now.
    public class ReadingListModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public ReadingListModel(LibraryDbContext db) => _db = db;

        [TempData] public string? Message { get; set; }

        public bool SignedIn { get; set; }
        public List<ReadingListRow> Rows { get; set; } = new();

        public async Task OnGetAsync()
        {
            int? memberId = int.TryParse(User.FindFirst("library_user_id")?.Value, out var id) ? id : null;
            SignedIn = memberId != null;
            if (memberId == null) return;

            await Circulation.ExpirePickupsAsync(_db);

            var saved = await _db.ReadingList.AsNoTracking()
                .Where(r => r.LibraryUserId == memberId)
                .OrderByDescending(r => r.AddedAt)
                .ToListAsync();
            var items = await _db.Inventory.AsNoTracking()
                .Select(i => new { i.Id, i.Name, i.Type, i.Status })
                .ToListAsync();
            var activeHolds = await _db.Holds.AsNoTracking()
                .Where(h => h.ItemId != null && (h.Status == "Ready" || h.Status == "Pending"))
                .ToListAsync();
            var openLoans = await _db.Rentals.AsNoTracking()
                .Where(r => !r.IsReturned)
                .Select(r => new { r.ItemId, r.LibraryUserId, r.EndDate })
                .ToListAsync();

            foreach (var s in saved)
            {
                var it = items.FirstOrDefault(i => i.Id == s.ItemId);
                if (it == null) continue;
                var copies = items.Where(i => i.Name == it.Name && i.Type == it.Type).Select(i => i.Id).ToHashSet();
                var holds = activeHolds.Where(h => copies.Contains(h.ItemId!.Value)).ToList();
                var setAside = holds.Where(h => h.Status == "Ready").Select(h => h.ItemId!.Value).ToHashSet();
                int free = items.Count(i => copies.Contains(i.Id) && i.Status == "Available" && !setAside.Contains(i.Id));
                var myLoan = openLoans.FirstOrDefault(l => copies.Contains(l.ItemId) && l.LibraryUserId == memberId);
                var myReady = holds.FirstOrDefault(h => h.LibraryUserId == memberId && h.Status == "Ready");
                var pending = holds.Where(h => h.Status == "Pending").OrderBy(h => h.RequestDatetime).ToList();
                int myPlace = pending.FindIndex(h => h.LibraryUserId == memberId);

                string availability, action;
                bool highlight = false;
                if (myLoan != null) { availability = $"Checked out to you, due {myLoan.EndDate:MMM d}"; action = "View"; }
                else if (myReady != null) { availability = $"Ready for pickup by {myReady.PickupDeadline:MMM d}"; action = "View"; highlight = true; }
                else if (myPlace >= 0) { availability = $"On hold: #{myPlace + 1} in line"; action = "View"; }
                else if (free > 0) { availability = $"Available ({free} on the shelf)"; action = "Request checkout"; highlight = true; }
                else
                {
                    var due = openLoans.Where(l => copies.Contains(l.ItemId) && l.EndDate != null).Select(l => l.EndDate).Min();
                    availability = due != null ? $"All copies out, next due {due:MMM d}" : "Not available";
                    action = "Place hold";
                }

                Rows.Add(new ReadingListRow(s.ItemId, it.Name, LabelOf(it.Type), s.AddedAt, availability, action, highlight));
            }
        }

        public async Task<IActionResult> OnPostRemoveAsync(int itemId)
        {
            int? memberId = int.TryParse(User.FindFirst("library_user_id")?.Value, out var id) ? id : null;
            var row = await _db.ReadingList.FirstOrDefaultAsync(r => r.LibraryUserId == memberId && r.ItemId == itemId);
            if (row != null)
            {
                _db.ReadingList.Remove(row);
                await _db.SaveChangesAsync();
                Message = "Removed from your reading list.";
            }
            return RedirectToPage();
        }

        private static string LabelOf(string? type)
        {
            foreach (var t in LibraryOptions.Types)
                if (t.Value == type) return t.Label;
            return type ?? "";
        }
    }
}
