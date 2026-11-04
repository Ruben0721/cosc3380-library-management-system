using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;

namespace Project.Pages.Staff
{
    public record CheckoutItemRow(int Id, string? Name, string Category, string Status,
                                  string? Borrower, DateTime? DueDate);

  
    public class StartCheckoutModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public StartCheckoutModel(LibraryDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public string? Category { get; set; }
        [BindProperty(SupportsGet = true)] public bool AvailableOnly { get; set; } = true;

        public List<CheckoutItemRow> Items { get; set; } = new();
        public bool HasMore { get; set; }

        public async Task OnGetAsync()
        {
            var query = _db.Inventory.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var term = Search.Trim();
                query = query.Where(i => i.Name != null && i.Name.Contains(term));
            }
            if (!string.IsNullOrEmpty(Category))
                query = query.Where(i => i.Type == Category);
            if (AvailableOnly)
                query = query.Where(i => i.Status == "Available");

            var items = await query
                .OrderBy(i => i.Name)
                .Select(i => new { i.Id, i.Name, i.Type, i.Status })
                .Take(51)
                .ToListAsync();

            HasMore = items.Count > 50;
            items = items.Take(50).ToList();

            // Who has the item now (for items that are out)
            var ids = items.Select(i => i.Id).ToList();
            var loans = await _db.Rentals.AsNoTracking()
                .Where(r => !r.IsReturned && ids.Contains(r.ItemId))
                .Select(r => new { r.ItemId, r.LibraryUserId, r.EndDate })
                .ToListAsync();
            var names = await _db.LibraryUsers.AsNoTracking()
                .ToDictionaryAsync(u => u.LibraryUserId, u => u.FullName);

            Items = items.Select(i =>
            {
                var loan = loans.FirstOrDefault(l => l.ItemId == i.Id);
                return new CheckoutItemRow(i.Id, i.Name, LabelOf(i.Type), i.Status ?? "",
                    loan == null ? null : names.GetValueOrDefault(loan.LibraryUserId),
                    loan?.EndDate);
            }).ToList();
        }

        public static string LabelOf(string? type)
        {
            foreach (var t in LibraryOptions.Types)
                if (t.Value == type) return t.Label;
            return type ?? "";
        }
    }
}