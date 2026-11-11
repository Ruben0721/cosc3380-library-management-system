using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Project.Data;

namespace Project.Pages
{
    // Shared rules for borrowing, used by the member Borrow page and the staff Pickups page.
    // The database triggers (ValidateRentalLimit, ValidateRentalBeforeInsert) still have the
    // final say on rental limits, availability and inactive members.
    public static class Circulation
    {
        // Every item is requested online and handed out at the circulation desk.
        // To let members check out some types themselves instead, list them here (e.g. "book").
        public static readonly string[] SelfCheckoutTypes = { };

        public const decimal FineBlockLimit = 10.00m;      // unpaid fines above this block new loans
        public const int MaxPickupReservations = 3;        // checkout requests waiting per member
        public const int PickupDays = 2;                   // days to collect a request
        public const int DefaultWaitlistLimit = 3;         // holds per member if no limit is set on their record

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

        // Mark requests/holds whose pickup deadline has passed as Expired,
        // then offer each freed copy to the next member waiting for that title.
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

        // The hold queue: if this copy is on the shelf and not set aside for anyone, give it to the
        // member who has waited longest for the same title (same rule as trg_rental_return_hold).
        // Returns the name of the member it was set aside for, or null.
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

        // How many holds this member may have waiting (from their Student or Faculty record).
        public static async Task<int> WaitlistLimitAsync(LibraryDbContext db, int memberId)
        {
            var values = await db.Database.SqlQuery<int>(
                $"SELECT COALESCE((SELECT WaitlistLimit FROM Student WHERE LibraryUserId = {memberId} LIMIT 1), (SELECT waitlist_limit FROM Faculty WHERE LibraryUserId = {memberId} LIMIT 1), {DefaultWaitlistLimit}) AS Value")
                .ToListAsync();
            return values.FirstOrDefault(DefaultWaitlistLimit);
        }

        // Create the loan and mark the copy Checked Out, in one transaction.
        // Returns null on success, or the reason it was refused.
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

        // Next Hold_ID in the H00001 format
        public static async Task<string> NextHoldIdAsync(LibraryDbContext db)
        {
            var ids = await db.Holds.Select(h => h.HoldId).ToListAsync();
            int max = ids.Select(id => id.Length > 1 && int.TryParse(id[1..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
            return $"H{max + 1:D5}";
        }
    }
}
