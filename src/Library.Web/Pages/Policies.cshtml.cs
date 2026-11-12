using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;

namespace Project.Pages
{
    public record PolicyRow(string Category, int LoanDays, decimal PerDay, decimal Cap, decimal DamageFee, decimal ReplacementCost);

    // Public: loan periods, fines and borrowing rules, read from the FinePolicy table
    public class PoliciesModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public PoliciesModel(LibraryDbContext db) => _db = db;

        public List<PolicyRow> Rows { get; set; } = new();

        public async Task OnGetAsync()
        {
            var policies = await _db.FinePolicies.AsNoTracking().ToListAsync();
            Rows = policies
                .Select(p => new PolicyRow(
                    LibraryOptions.Types.Where(t => t.Value == p.ItemType).Select(t => t.Label).FirstOrDefault() ?? p.ItemType,
                    p.LoanDays, p.DailyOverdueRate, p.OverdueCap, p.DamageFee, p.ReplacementCost))
                .OrderBy(r => r.Category)
                .ToList();
        }
    }
}
