using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;

namespace Project.Pages.Staff
{
    public record DashReturnRow(DateTime ReturnedAt, string? Item, string Borrower, string Condition);
    public record DashPaymentRow(DateTime PaidAt, string Member, decimal Amount, string Method);

    // Staff dashboard
    public class IndexModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public IndexModel(LibraryDbContext db) => _db = db;

        private static readonly string[] OpenStatuses = { "Open", "In Progress" };

   
        public int CheckedOut { get; set; }
        public int OverdueNow { get; set; }
        public decimal OverdueAccruing { get; set; }
        public int DueToday { get; set; }

      
        public decimal UnpaidTotal { get; set; }
        public int UnpaidMembers { get; set; }

       
        public int OpenRepairs { get; set; }
        public int OldestRepairDays { get; set; }
        public int OutOfService { get; set; }

        
        public int HoldsWaiting { get; set; }
        public int HoldsReady { get; set; }

        
        public int ActiveMembers { get; set; }
        public int MembersWithoutLogin { get; set; }

        public List<DashReturnRow> RecentReturns { get; set; } = new();
        public List<DashPaymentRow> RecentPayments { get; set; } = new();

        public async Task OnGetAsync()
        {
            var now = DateTime.Now;
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

           
            var active = await _db.Rentals.AsNoTracking()
                .Where(r => !r.IsReturned)
                .Select(r => new { r.ItemId, r.EndDate })
                .ToListAsync();

            CheckedOut = active.Count;
            DueToday = active.Count(r => r.EndDate >= today && r.EndDate < tomorrow);

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

            
            var unpaid = await _db.Fines.AsNoTracking()
                .Where(f => !f.IsPaid)
                .Select(f => new { f.LibraryUserId, f.Amount })
                .ToListAsync();
            UnpaidTotal = unpaid.Sum(f => f.Amount);
            UnpaidMembers = unpaid.Select(f => f.LibraryUserId).Distinct().Count();

           
            var openRequests = await _db.MaintenanceRequests.AsNoTracking()
                .Where(m => OpenStatuses.Contains(m.Status))
                .Select(m => m.CreatedAt)
                .ToListAsync();
            OpenRepairs = openRequests.Count;
            OldestRepairDays = openRequests.Count == 0 ? 0 : openRequests.Max(c => (now - c).Days);
            OutOfService = await _db.Inventory.CountAsync(i => i.Status == "Under Repair" || i.Status == "Lost");

            
            HoldsWaiting = await _db.Holds.CountAsync(h => h.Status == "Pending");
            HoldsReady = await _db.Holds.CountAsync(h => h.Status == "Ready");

           
            ActiveMembers = await _db.LibraryUsers.CountAsync(u => u.IsActive);
            MembersWithoutLogin = await _db.LibraryUsers
                .CountAsync(u => !_db.UserAccounts.Any(a => a.LibraryUserId == u.LibraryUserId));

           
            var names = await _db.LibraryUsers.AsNoTracking()
                .ToDictionaryAsync(u => u.LibraryUserId, u => u.FullName);
            var itemNames = await _db.Inventory.AsNoTracking()
                .ToDictionaryAsync(i => i.Id, i => i.Name);

            
            var returns = await _db.Rentals.AsNoTracking()
                .Where(r => r.IsReturned && r.ReturnedDate != null)
                .OrderByDescending(r => r.ReturnedDate)
                .Take(5)
                .Select(r => new { r.RentalId, r.ItemId, r.LibraryUserId, r.ReturnedDate })
                .ToListAsync();

            var damagedOrLost = await _db.Fines.AsNoTracking()
                .Where(f => f.FineType == "Damaged" || f.FineType == "Lost")
                .Select(f => new { f.RentalId, f.FineType })
                .ToListAsync();
            var conditionByRental = damagedOrLost
                .GroupBy(f => f.RentalId)
                .ToDictionary(g => g.Key, g => g.First().FineType);

            RecentReturns = returns
                .Select(r => new DashReturnRow(
                    r.ReturnedDate!.Value,
                    itemNames.GetValueOrDefault(r.ItemId),
                    names.GetValueOrDefault(r.LibraryUserId) ?? $"Member #{r.LibraryUserId}",
                    conditionByRental.GetValueOrDefault(r.RentalId) ?? "Good"))
                .ToList();

            RecentPayments = (await _db.Payments.AsNoTracking()
                    .OrderByDescending(p => p.PaymentDate)
                    .Take(5)
                    .ToListAsync())
                .Select(p => new DashPaymentRow(
                    p.PaymentDate,
                    names.GetValueOrDefault(p.LibraryUserId) ?? $"Member #{p.LibraryUserId}",
                    p.Amount,
                    p.PaymentMethod))
                .ToList();
        }
    }
}