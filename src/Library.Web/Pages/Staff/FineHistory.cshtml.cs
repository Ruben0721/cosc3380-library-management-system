using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;

namespace Project.Pages.Staff
{
    public record FineMemberRow(int MemberId, string Name, bool IsActive, int Fines, decimal Charged, decimal Paid, decimal Unpaid);
    public record FineHistoryRow(int FineId, DateTime DateAssessed, string? Item, string FineType, int? DaysLate,
                                 decimal Amount, bool IsPaid, string? Notes);
    public record PaymentHistoryRow(string PaymentId, DateTime PaymentDate, string Method, decimal Amount, string For);

    // Query: look up a member by name and see every fine they were charged and every payment they made.
    public class FineHistoryModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public FineHistoryModel(LibraryDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public string? Name { get; set; }
        [BindProperty(SupportsGet = true)] public int? MemberId { get; set; }

        public List<FineMemberRow> Matches { get; set; } = new();

        // Selected member
        public string? MemberName { get; set; }
        public List<FineHistoryRow> Fines { get; set; } = new();
        public List<PaymentHistoryRow> Payments { get; set; } = new();
        public decimal TotalCharged => Fines.Sum(f => f.Amount);
        public decimal TotalUnpaid => Fines.Where(f => !f.IsPaid).Sum(f => f.Amount);
        public decimal TotalPaid => Payments.Sum(p => p.Amount);
        public decimal Owed { get; set; }   // unpaid fines + late fees building up (same total as the $10 borrowing block)

        public async Task<IActionResult> OnGetAsync()
        {
            if (!string.IsNullOrWhiteSpace(Name) && MemberId == null)
            {
                var term = Name.Trim();
                var members = await _db.LibraryUsers.AsNoTracking()
                    .Where(u => u.FullName != null && u.FullName.Contains(term))
                    .OrderBy(u => u.FullName)
                    .Take(50)
                    .Select(u => new { u.LibraryUserId, u.FullName, u.IsActive })
                    .ToListAsync();
                var ids = members.Select(m => m.LibraryUserId).ToList();
                var fines = await _db.Fines.AsNoTracking()
                    .Where(f => ids.Contains(f.LibraryUserId))
                    .Select(f => new { f.LibraryUserId, f.Amount, f.IsPaid })
                    .ToListAsync();

                Matches = members.Select(m =>
                {
                    var mine = fines.Where(f => f.LibraryUserId == m.LibraryUserId).ToList();
                    decimal charged = mine.Sum(f => f.Amount), unpaid = mine.Where(f => !f.IsPaid).Sum(f => f.Amount);
                    return new FineMemberRow(m.LibraryUserId, m.FullName ?? $"Member #{m.LibraryUserId}", m.IsActive,
                                             mine.Count, charged, charged - unpaid, unpaid);
                }).ToList();

                // Exactly one match: go straight to their history
                if (Matches.Count == 1) return RedirectToPage(new { MemberId = Matches[0].MemberId, Name });
            }

            if (MemberId != null)
            {
                MemberName = await _db.LibraryUsers.AsNoTracking()
                    .Where(u => u.LibraryUserId == MemberId).Select(u => u.FullName).FirstOrDefaultAsync();
                if (MemberName == null) return NotFound();
                Owed = await Circulation.AmountOwedAsync(_db, MemberId.Value);

                var itemByRental = await (from r in _db.Rentals
                                          join i in _db.Inventory on r.ItemId equals i.Id
                                          where r.LibraryUserId == MemberId
                                          select new { r.RentalId, i.Name }).ToDictionaryAsync(x => x.RentalId, x => x.Name);

                var fines = await _db.Fines.AsNoTracking()
                    .Where(f => f.LibraryUserId == MemberId)
                    .OrderByDescending(f => f.DateAssessed)
                    .ToListAsync();
                Fines = fines.Select(f => new FineHistoryRow(f.FineId, f.DateAssessed, itemByRental.GetValueOrDefault(f.RentalId),
                                                             f.FineType, f.DaysLate, f.Amount, f.IsPaid, f.Notes)).ToList();

                var fineById = fines.ToDictionary(f => f.FineId);
                Payments = (await _db.Payments.AsNoTracking()
                        .Where(p => p.LibraryUserId == MemberId)
                        .OrderByDescending(p => p.PaymentDate)
                        .ToListAsync())
                    .Select(p =>
                    {
                        var f = p.FineId != null ? fineById.GetValueOrDefault(p.FineId.Value) : null;
                        string forWhat = f == null ? "—" : $"{f.FineType} fine #{f.FineId}" +
                            (itemByRental.GetValueOrDefault(f.RentalId) is string n ? $" ({n})" : "");
                        return new PaymentHistoryRow(p.PaymentId, p.PaymentDate, p.PaymentMethod, p.Amount, forWhat);
                    })
                    .ToList();
            }
            return Page();
        }
    }
}
