using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Pages
{
    public record MyFineRow(int FineId, string? ItemName, string FineType, int? DaysLate,
                            DateTime DateAssessed, decimal Amount, string? Notes);

    public record MyPaymentRow(string PaymentId, string? ItemName, string FineType, decimal Amount,
                               string Method, DateTime PaymentDate);

    
    public class MyFinesModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public MyFinesModel(LibraryDbContext db) => _db = db;

        public const string OnlineMethod = "card";

        [TempData] public string? Message { get; set; }
        [TempData] public string? Error { get; set; }

        public bool SignedIn { get; set; }
        public List<MyFineRow> Unpaid { get; set; } = new();
        public List<MyPaymentRow> Paid { get; set; } = new();
        public decimal Balance => Unpaid.Sum(f => f.Amount);

        // Card form (validated, never stored)
        [BindProperty] public List<int> FineIds { get; set; } = new();
        [BindProperty, Required(ErrorMessage = "Enter the name on the card.")] public string? CardName { get; set; }
        [BindProperty, Required(ErrorMessage = "Enter the card number.")] public string? CardNumber { get; set; }
        [BindProperty, Required(ErrorMessage = "Enter the expiry date.")] public string? CardExpiry { get; set; }
        [BindProperty, Required(ErrorMessage = "Enter the security code.")] public string? CardCvc { get; set; }

        private int? MemberId => int.TryParse(User.FindFirst("library_user_id")?.Value, out var id) ? id : null;

        public async Task OnGetAsync() => await LoadAsync();

        public async Task<IActionResult> OnPostPayAsync()
        {
            if (MemberId == null) return RedirectToPage("/Account/Login", new { ReturnUrl = "/MyFines" });

            string? cardProblem = CheckCard();
            if (!ModelState.IsValid || cardProblem != null)
            {
                Error = cardProblem ?? ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
                return RedirectToPage();
            }

            // Only this member's unpaid fines, whatever ids were posted
            var fines = await _db.Fines
                .Where(f => f.LibraryUserId == MemberId && !f.IsPaid && FineIds.Contains(f.FineId))
                .ToListAsync();
            if (fines.Count == 0)
            {
                Error = "Choose at least one fine to pay.";
                return RedirectToPage();
            }

            var ids = fines.Select(f => $"P{f.FineId:D6}").ToList();
            if (await _db.Payments.AnyAsync(p => ids.Contains(p.PaymentId)))
            {
                Error = "One of these fines already has a payment on record. Please contact the circulation desk.";
                return RedirectToPage();
            }

            var now = DateTime.Now;
            foreach (var f in fines)
            {
                _db.Payments.Add(new Payment
                {
                    PaymentId = $"P{f.FineId:D6}",
                    LibraryUserId = f.LibraryUserId,
                    Amount = f.Amount,
                    PaymentDate = now,
                    PaymentMethod = OnlineMethod,
                    FineId = f.FineId
                });
                f.IsPaid = true;
            }
            await _db.SaveChangesAsync();  

            string last4 = new string(CardNumber!.Where(char.IsDigit).ToArray())[^4..];
            decimal total = fines.Sum(f => f.Amount);
            decimal remaining = await Circulation.UnpaidFinesAsync(_db, MemberId.Value);
            Message = $"Paid {total:C} for {fines.Count} fine{(fines.Count == 1 ? "" : "s")} with the card ending in {last4}. " +
                      (remaining > 0 ? $"Remaining balance: {remaining:C}." : "You have no unpaid fines.");
            return RedirectToPage();
        }

        // Format checks only; nothing is sent anywhere.
        private string? CheckCard()
        {
            var digits = new string((CardNumber ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length < 12 || digits.Length > 19) return "Enter a card number of 12 to 19 digits.";

            var parts = (CardExpiry ?? "").Split('/');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int mm) || !int.TryParse(parts[1], out int yy) || mm < 1 || mm > 12)
                return "Enter the expiry date as MM/YY.";
            var endOfMonth = new DateTime(2000 + yy % 100, mm, 1).AddMonths(1);
            if (endOfMonth <= DateTime.Today) return "That card has expired.";

            var cvc = (CardCvc ?? "").Trim();
            if (cvc.Length < 3 || cvc.Length > 4 || !cvc.All(char.IsDigit)) return "The security code is 3 or 4 digits.";
            return null;
        }


        private async Task LoadAsync()
        {
            SignedIn = MemberId != null;
            if (!SignedIn) return;

            var itemNames = await _db.Inventory.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Name);
            var rentalItem = await _db.Rentals.AsNoTracking()
                .Where(r => r.LibraryUserId == MemberId)
                .ToDictionaryAsync(r => r.RentalId, r => r.ItemId);
            string? ItemOf(int rentalId) => rentalItem.TryGetValue(rentalId, out var itemId) ? itemNames.GetValueOrDefault(itemId) : null;

            var fines = await _db.Fines.AsNoTracking().Where(f => f.LibraryUserId == MemberId).ToListAsync();
            var fineById = fines.ToDictionary(f => f.FineId);

            Unpaid = fines.Where(f => !f.IsPaid)
                .OrderBy(f => f.DateAssessed)
                .Select(f => new MyFineRow(f.FineId, ItemOf(f.RentalId), f.FineType, f.DaysLate, f.DateAssessed, f.Amount, f.Notes))
                .ToList();

            Paid = (await _db.Payments.AsNoTracking()
                    .Where(p => p.LibraryUserId == MemberId)
                    .OrderByDescending(p => p.PaymentDate)
                    .ToListAsync())
                .Select(p =>
                {
                    var f = p.FineId != null ? fineById.GetValueOrDefault(p.FineId.Value) : null;
                    return new MyPaymentRow(p.PaymentId, f != null ? ItemOf(f.RentalId) : null, f?.FineType ?? "—",
                                            p.Amount, p.PaymentMethod, p.PaymentDate);
                })
                .ToList();
        }
    }
}
