using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;
using Project.Models;

namespace Project.Pages
{
    public class InventoryModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public InventoryModel(LibraryDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public string? SelectedType { get; set; }
        [BindProperty(SupportsGet = true)] public string? SelectedStatus { get; set; }
        [BindProperty(SupportsGet = true)] public string? Sort { get; set; }

        [TempData] public string? Message { get; set; }
        [TempData] public string? Error { get; set; }

        public List<(string Value, string Label)> Types => LibraryOptions.Types;
        public List<string> Statuses => LibraryOptions.Statuses;
        public bool CanRemoveItems => _db.InventoryRetirementEnabled;

        public List<Inventory> Inventory { get; private set; } = new();
        public int TotalItems { get; private set; }

        public async Task OnGetAsync()
        {
            var query = _db.ActiveInventory.AsNoTracking();
            TotalItems = await query.CountAsync();

            if (!string.IsNullOrEmpty(SelectedType))
                query = query.Where(i => i.Type == SelectedType);

            if (!string.IsNullOrEmpty(SelectedStatus))
                query = query.Where(i => i.Status == SelectedStatus);

            query = Sort switch
            {
                "type" => query.OrderBy(i => i.Type).ThenBy(i => i.Name),
                "status" => query.OrderBy(i => i.Status).ThenBy(i => i.Name),
                "newest" => query.OrderByDescending(i => i.Id),
                _ => query.OrderBy(i => i.Name)
            };

            Inventory = await query.ToListAsync();
        }

        public async Task<IActionResult> OnPostRemoveAsync(int itemId)
        {
            if (!CanRemoveItems)
            {
                Error = "Inventory removal is unavailable until the database update is applied.";
                return RedirectToPage(new { SelectedType, SelectedStatus, Sort });
            }

            var removed = await _db.ActiveInventory
                .Where(i => i.Id == itemId
                    && i.Status != "Checked Out" && i.Status != "On Hold" && i.Status != "Under Repair"
                    && !_db.Rentals.Any(r => r.ItemId == i.Id && !r.IsReturned && r.ReturnedDate == null)
                    && !_db.Holds.Any(h => h.ItemId == i.Id && (h.Status == "Pending" || h.Status == "Ready"))
                    && !_db.MaintenanceRequests.Any(m => m.ItemId == i.Id && (m.Status == "Open" || m.Status == "In Progress")))
                .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.RetiredAt, DateTime.UtcNow));

            if (removed == 1)
                Message = "Item removed from the catalog and inventory. Its circulation history is still available.";
            else
                Error = "The item was already removed, could not be found, or has an active checkout, hold, or repair.";

            return RedirectToPage(new { SelectedType, SelectedStatus, Sort });
        }
    }
}
