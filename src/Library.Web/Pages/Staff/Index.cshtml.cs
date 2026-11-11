using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;

namespace Project.Pages.Staff
{
    // Staff dashboard: only what needs attention today.
    public class IndexModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public IndexModel(LibraryDbContext db) => _db = db;

        private static readonly string[] OpenStatuses = { "Open", "In Progress" };

        public int OverdueNow { get; set; }
        public decimal OverdueAccruing { get; set; }
        public int DueToday { get; set; }
        public decimal UnpaidTotal { get; set; }
        public int UnpaidMembers { get; set; }
        public int OpenRepairs { get; set; }
        public int OldestRepairDays { get; set; }
        public int PickupsWaiting { get; set; }

        public async Task OnGetAsync()
        {
            var now = DateTime.Now;
            var today = DateTime.Today;

            // Overdue loans and the fines building up on them
            var active = await _db.Rentals.AsNoTracking()
                .Where(r => !r.IsReturned)
                .Select(r => new { r.ItemId, r.EndDate })
                .ToListAsync();

            DueToday = active.Count(r => r.EndDate != null && r.EndDate.Value.Date == today);

            var itemTypes = await _db.Inventory.AsNoTracking()
                .Select(i => new { i.Id, i.Type })
                .ToDictionaryAsync(i => i.Id, i => i.Type);
            var policies = await _db.FinePolicies.AsNoTracking().ToDictionaryAsync(p => p.ItemType);

            foreach (var r in active.Where(r => r.EndDate != null && r.EndDate.Value.Date < today))
            {
                OverdueNow++;
                int days = (today - r.EndDate!.Value.Date).Days;
                if (itemTypes.TryGetValue(r.ItemId, out var type) && type != null && policies.TryGetValue(type, out var p))
                    OverdueAccruing += Math.Min(days * p.DailyOverdueRate, p.OverdueCap);
            }

            // Unpaid fines
            var unpaid = await _db.Fines.AsNoTracking()
                .Where(f => !f.IsPaid)
                .Select(f => new { f.LibraryUserId, f.Amount })
                .ToListAsync();
            UnpaidTotal = unpaid.Sum(f => f.Amount);
            UnpaidMembers = unpaid.Select(f => f.LibraryUserId).Distinct().Count();

            // Open repair work
            var openRequests = await _db.MaintenanceRequests.AsNoTracking()
                .Where(m => OpenStatuses.Contains(m.Status))
                .Select(m => m.CreatedAt)
                .ToListAsync();
            OpenRepairs = openRequests.Count;
            OldestRepairDays = openRequests.Count == 0 ? 0 : openRequests.Max(c => (now - c).Days);

            // Checkout requests placed online, waiting to be handed out at the desk
            PickupsWaiting = await _db.Holds.CountAsync(h => h.Status == "Ready");
        }
    }
}
