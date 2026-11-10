using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Pages.Staff
{
    public record UnpaidFineRow(int FineId, int LibraryUserId, string Member, string? ItemName, string FineType,
                                decimal Amount, int? DaysLate, DateTime DateAssessed, string? Notes);

    public record MemberBalanceRow(int LibraryUserId, string Member, int Fines, decimal Balance, DateTime Oldest);

    public record RecentPaymentRow(string PaymentId, string Member, string FineType, decimal Amount,
                                   string Method, DateTime PaymentDate);

    // Record fine payments.
    // Fines are created by the trg_rental_return_fines trigger when an item is returned.
    // Paying a fine inserts a Payment row linked to the fine and marks the fine paid.
    public class FinesModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public FinesModel(LibraryDbContext db) => _db = db;

        public static readonly string[] Methods = { "cash", "card", "check" };

        [BindProperty(SupportsGet = true)] public string? Search { get; set; }

        [TempData] public string? Message { get; set; }
        [TempData] public string? Error { get; set; }

        public List<UnpaidFineRow> Unpaid { get; set; } = new();
        public List<MemberBalanceRow> Balances { get; set; } = new();
        public List<RecentPaymentRow> RecentPayments { get; set; } = new();
        public decimal TotalOutstanding { get; set; }

        public async Task OnGetAsync()
        {
            var names = await _db.LibraryUsers.AsNoTracking().ToDictionaryAsync(u => u.LibraryUserId, u => u.FullName);
            var itemNames = await _db.Inventory.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Name);
            var rentalItem = await _db.Rentals.AsNoTracking().ToDictionaryAsync(r => r.RentalId, r => r.ItemId);
            string Member(int id) => names.GetValueOrDefault(id) ?? $"Member #{id}";

            var fines = await _db.Fines.AsNoTracking().ToListAsync();
            var fineById = fines.ToDictionary(f => f.FineId);

            Unpaid = fines.Where(f => !f.IsPaid)
                .Select(f => new UnpaidFineRow(f.FineId, f.LibraryUserId, Member(f.LibraryUserId),
                    rentalItem.TryGetValue(f.RentalId, out var itemId) ? itemNames.GetValueOrDefault(itemId) : null,
                    f.FineType, f.Amount, f.DaysLate, f.DateAssessed, f.Notes))
                .Where(f => string.IsNullOrWhiteSpace(Search)
                         || f.Member.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)
                         || (f.ItemName ?? "").Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Member).ThenBy(f => f.DateAssessed)
                .ToList();

            TotalOutstanding = Unpaid.Sum(f => f.Amount);

            Balances = Unpaid.GroupBy(f => f.LibraryUserId)
                .Select(g => new MemberBalanceRow(g.Key, g.First().Member, g.Count(), g.Sum(f => f.Amount),
                                                  g.Min(f => f.DateAssessed)))
                .OrderByDescending(b => b.Balance)
                .ToList();

            RecentPayments = (await _db.Payments.AsNoTracking()
                    .OrderByDescending(p => p.PaymentDate).Take(10).ToListAsync())
                .Select(p => new RecentPaymentRow(p.PaymentId, Member(p.LibraryUserId),
                    p.FineId != null && fineById.TryGetValue(p.FineId.Value, out var f) ? f.FineType : "—",
                    p.Amount, p.PaymentMethod, p.PaymentDate))
                .ToList();
        }

        public async Task<IActionResult> OnPostPayAsync(int fineId, string method)
        {
            if (!Methods.Contains(method))
            {
                Error = "Choose a payment method: cash, card or check.";
                return RedirectToPage(new { Search });
            }

            var fine = await _db.Fines.FirstOrDefaultAsync(f => f.FineId == fineId);
            if (fine == null || fine.IsPaid)
            {
                Error = "That fine was not found or is already paid.";
                return RedirectToPage(new { Search });
            }

            // Same id format as the existing payments: P + fine id (6 digits)
            var paymentId = $"P{fineId:D6}";
            if (await _db.Payments.AnyAsync(p => p.PaymentId == paymentId))
            {
                Error = $"A payment ({paymentId}) already exists for this fine.";
                return RedirectToPage(new { Search });
            }

            _db.Payments.Add(new Payment
            {
                PaymentId = paymentId,
                LibraryUserId = fine.LibraryUserId,
                Amount = fine.Amount,
                PaymentDate = DateTime.Now,
                PaymentMethod = method,
                FineId = fine.FineId
            });
            fine.IsPaid = true;
            await _db.SaveChangesAsync();

            var member = await _db.LibraryUsers.AsNoTracking()
                .Where(u => u.LibraryUserId == fine.LibraryUserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync();
            decimal remaining = await _db.Fines
                .Where(f => f.LibraryUserId == fine.LibraryUserId && !f.IsPaid)
                .SumAsync(f => (decimal?)f.Amount) ?? 0;

            Message = $"Payment {paymentId}: {fine.Amount:C} ({method}) recorded for {member}'s {fine.FineType.ToLower()} fine. " +
                      $"Remaining balance: {remaining:C}.";
            return RedirectToPage(new { Search });
        }
    }
}