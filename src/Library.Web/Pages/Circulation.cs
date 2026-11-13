using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Project.Data;

namespace Project.Pages
{
   
    public static class Circulation
    {
       
        public static readonly string[] SelfCheckoutTypes = { };

        public const decimal FineBlockLimit = 10.00m;      
        public const int MaxPickupReservations = 3;       
        public const int PickupDays = 2;                  
        public const int DefaultWaitlistLimit = 3;         

        public static bool NeedsPickup(string? type) => type == null || !SelfCheckoutTypes.Contains(type);

        public static async Task<int> LoanDaysAsync(LibraryDbContext db, string? type)
        {
            var days = await db.FinePolicies.AsNoTracking()
                .Where(p => p.ItemType == type)
                .Select(p => (int?)p.LoanDays)
                .FirstOrDefaultAsync();
            return days ?? 14;
        }

        public static async Task<decimal> UnpaidFinesAsync(LibraryDbContext db, int memberId) =>
            await db.Fines.Where(f => f.LibraryUserId == memberId && !f.IsPaid).SumAsync(f => (decimal?)f.Amount) ?? 0;

        public static async Task<decimal> AmountOwedAsync(LibraryDbContext db, int memberId)
        {
            decimal charged = await UnpaidFinesAsync(db, memberId);

            var now = DateTime.Now;
            var overdue = await (from r in db.Rentals
                                 join i in db.Inventory on r.ItemId equals i.Id
                                 where r.LibraryUserId == memberId && !r.IsReturned && r.EndDate != null
                                 select new { r.EndDate, r.DroppedOffAt, i.Type }).ToListAsync();
            if (overdue.Count == 0) return charged;

            var policies = await db.FinePolicies.AsNoTracking().ToDictionaryAsync(p => p.ItemType);
            decimal accruing = 0;
            foreach (var o in overdue)
            {
                var asOf = (o.DroppedOffAt ?? now).Date;              // fee stops at drop-off
                int days = (asOf - o.EndDate!.Value.Date).Days;          // same as MySQL DATEDIFF
                if (days > 0 && o.Type != null && policies.TryGetValue(o.Type, out var p))
                    accruing += Math.Min(days * p.DailyOverdueRate, p.OverdueCap);
            }
            return charged + accruing;
        }

    
        public static async Task ExpirePickupsAsync(LibraryDbContext db)
        {
            var now = DateTime.Now;
            var expired = await db.Holds
                .Where(h => h.Status == "Ready" && h.PickupDeadline != null && h.PickupDeadline < now)
                .ToListAsync();
            if (expired.Count == 0) return;

            foreach (var h in expired) h.Status = "Expired";
            await db.SaveChangesAsync();

            foreach (var itemId in expired.Where(h => h.ItemId != null).Select(h => h.ItemId!.Value).Distinct())
                await PromoteNextHoldAsync(db, itemId);
        }

      
        public static async Task<string?> PromoteNextHoldAsync(LibraryDbContext db, int itemId)
        {
            var item = await db.Inventory.AsNoTracking()
                .Where(i => i.Id == itemId)
                .Select(i => new { i.Id, i.Name, i.Type, i.Status })
                .FirstOrDefaultAsync();
            if (item == null || item.Status != "Available") return null;
            if (await db.Holds.AnyAsync(h => h.ItemId == itemId && h.Status == "Ready")) return null;

            var sameTitle = await db.Inventory.AsNoTracking()
                .Where(i => i.Name == item.Name && i.Type == item.Type)
                .Select(i => i.Id)
                .ToListAsync();
            var next = (await db.Holds.Where(h => h.Status == "Pending" && h.ItemId != null).ToListAsync())
                .Where(h => sameTitle.Contains(h.ItemId!.Value))
                .OrderBy(h => h.RequestDatetime)
                .FirstOrDefault();
            if (next == null) return null;

            next.Status = "Ready";
            next.ItemId = itemId;
            next.ReadyDate = DateTime.Now;
            next.PickupDeadline = DateTime.Today.AddDays(PickupDays).AddHours(23).AddMinutes(59);
            await db.SaveChangesAsync();

            return await db.LibraryUsers.Where(u => u.LibraryUserId == next.LibraryUserId)
                .Select(u => u.FullName).FirstOrDefaultAsync();
        }

      
        public static async Task<int> WaitlistLimitAsync(LibraryDbContext db, int memberId)
        {
            var values = await db.Database.SqlQuery<int>(
                $"SELECT COALESCE((SELECT WaitlistLimit FROM Student WHERE LibraryUserId = {memberId} LIMIT 1), (SELECT waitlist_limit FROM Faculty WHERE LibraryUserId = {memberId} LIMIT 1), {DefaultWaitlistLimit}) AS Value")
                .ToListAsync();
            return values.FirstOrDefault(DefaultWaitlistLimit);
        }

      
        public static async Task<string?> CreateLoanAsync(LibraryDbContext db, int itemId, int memberId, string? type)
        {
            int loanDays = await LoanDaysAsync(db, type);
            var start = DateTime.Now;
            var due = start.Date.AddDays(loanDays).AddHours(23).AddMinutes(59);

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO Rentals (ItemId, LibraryUserId, StartDate, EndDate, IsReturned) VALUES ({itemId}, {memberId}, {start}, {due}, b'0')");
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Inventory SET Status = 'Checked Out' WHERE Id = {itemId}");
                await tx.CommitAsync();
                return null;
            }
            catch (MySqlException ex) when (ex.SqlState == "45000")
            {
                // A trigger refused the loan; its message says why (limit reached, unavailable, inactive)
                await tx.RollbackAsync();
                return ex.Message.Replace("Rental rejected: ", "").Replace("Retnal rejected: ", "");
            }
        }

       
        public static async Task<string> NextHoldIdAsync(LibraryDbContext db)
        {
            var ids = await db.Holds.Select(h => h.HoldId).ToListAsync();
            int max = ids.Select(id => id.Length > 1 && int.TryParse(id[1..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
            return $"H{max + 1:D5}";
        }
    }
}
