using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;

namespace Project.Pages.Staff
{
    public record DashRoomSetupRow(string RoomId, string ReservationId, DateTime StartTime,
        DateTime EndTime, int NumberOfPeople);

    
    public class IndexModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public IndexModel(LibraryDbContext db) => _db = db;

        private static readonly string[] OpenStatuses = { "Open", "In Progress" };

        // Loans
        public int OverdueNow { get; set; }
        public decimal OverdueAccruing { get; set; }
        public int DueToday { get; set; }

        // Fines
        public decimal UnpaidTotal { get; set; }
        public int UnpaidMembers { get; set; }

        // Repairs
        public int OpenRepairs { get; set; }
        public int OldestRepairDays { get; set; }

        // Pickups and holds
        public int PickupsWaiting { get; set; }     
        public int HoldsWaiting { get; set; }       
        public int BookDropWaiting { get; set; }   

        // Room setup
        public int OpenRoomSetupTasks { get; set; }
        public List<DashRoomSetupRow> RoomSetupTasks { get; set; } = new();

        public async Task OnGetAsync()
        {
            var now = DateTime.Now;
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

            // Overdue loans and the fines building up on them
            var active = await _db.Rentals.AsNoTracking()
                .Where(r => !r.IsReturned)
                .Select(r => new { r.ItemId, r.EndDate })
                .ToListAsync();

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

            // Items set aside at the desk, and members waiting in line
            PickupsWaiting = await _db.Holds.CountAsync(h => h.Status == "Ready");
            HoldsWaiting = await _db.Holds.CountAsync(h => h.Status == "Pending");
            BookDropWaiting = await _db.Rentals.CountAsync(r => !r.IsReturned && r.DroppedOffAt != null);

            // Rooms to set up before confirmed reservations
            RoomSetupTasks = await (from task in _db.RoomSetupTasks.AsNoTracking()
                                    join reservation in _db.RoomReservations.AsNoTracking()
                                        on task.ReservationId equals reservation.ReservationId
                                    where task.Status == "Open"
                                       && reservation.Status == "Confirmed"
                                    orderby reservation.StartTime
                                    select new DashRoomSetupRow(
                                        task.RoomId,
                                        task.ReservationId,
                                        reservation.StartTime,
                                        reservation.EndTime,
                                        reservation.NumberOfPeople))
                .Take(5)
                .ToListAsync();
            OpenRoomSetupTasks = await _db.RoomSetupTasks
                .CountAsync(t => t.Status == "Open");
        }
    }
}
