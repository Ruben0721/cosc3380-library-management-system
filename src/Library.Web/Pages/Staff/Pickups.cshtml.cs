using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;

namespace Project.Pages.Staff
{
    public record PickupRow(string HoldId, int ItemId, string? ItemName, string Category, int MemberId,
                            string Member, DateTime Reserved, DateTime? Deadline, bool DueToday);

   
    public class PickupsModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public PickupsModel(LibraryDbContext db) => _db = db;

        [TempData] public string? Message { get; set; }
        [TempData] public string? Error { get; set; }

        public List<PickupRow> Waiting { get; set; } = new();

        public async Task OnGetAsync()
        {
            await Project.Pages.Circulation.ExpirePickupsAsync(_db);

            var names = await _db.LibraryUsers.AsNoTracking().ToDictionaryAsync(u => u.LibraryUserId, u => u.FullName);
            var items = await _db.Inventory.AsNoTracking().Select(i => new { i.Id, i.Name, i.Type }).ToDictionaryAsync(i => i.Id);

            Waiting = (await _db.Holds.AsNoTracking()
                    .Where(h => h.Status == "Ready" && h.ItemId != null)
                    .OrderBy(h => h.PickupDeadline)
                    .ToListAsync())
                .Where(h => items.ContainsKey(h.ItemId!.Value))
                .Select(h =>
                {
                    var i = items[h.ItemId!.Value];
                    return new PickupRow(h.HoldId, i.Id, i.Name, LabelOf(i.Type), h.LibraryUserId,
                        names.GetValueOrDefault(h.LibraryUserId) ?? $"Member #{h.LibraryUserId}",
                        h.RequestDatetime, h.PickupDeadline,
                        h.PickupDeadline != null && h.PickupDeadline.Value.Date == DateTime.Today);
                })
                .ToList();
        }

        public async Task<IActionResult> OnPostHandOverAsync(string holdId)
        {
            var hold = await _db.Holds.FirstOrDefaultAsync(h => h.HoldId == holdId && h.Status == "Ready");
            if (hold == null || hold.ItemId == null)
            {
                Error = "That request was not found or has already been handled.";
                return RedirectToPage();
            }

            var type = await _db.Inventory.Where(i => i.Id == hold.ItemId).Select(i => i.Type).FirstOrDefaultAsync();
            decimal unpaid = await Project.Pages.Circulation.UnpaidFinesAsync(_db, hold.LibraryUserId);
            if (unpaid > Project.Pages.Circulation.FineBlockLimit)
            {
                Error = $"The member owes {unpaid:C} in unpaid fines. Take payment before handing out the item.";
                return RedirectToPage();
            }

            var refused = await Project.Pages.Circulation.CreateLoanAsync(_db, hold.ItemId.Value, hold.LibraryUserId, type);
            if (refused != null)
            {
                Error = $"Not handed out: {refused}";
                return RedirectToPage();
            }

            hold.Status = "Fulfilled";
            await _db.SaveChangesAsync();

            int loanDays = await Project.Pages.Circulation.LoanDaysAsync(_db, type);
            Message = $"Handed out. The loan is due on {DateTime.Today.AddDays(loanDays):dddd, MMMM d}.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostCancelAsync(string holdId)
        {
            var hold = await _db.Holds.FirstOrDefaultAsync(h => h.HoldId == holdId && h.Status == "Ready");
            if (hold != null)
            {
                hold.Status = "Cancelled";
                await _db.SaveChangesAsync();
                var next = hold.ItemId == null ? null : await Project.Pages.Circulation.PromoteNextHoldAsync(_db, hold.ItemId.Value);
                Message = next == null
                    ? "Request cancelled. The item is free for others."
                    : $"Request cancelled. The copy is now set aside for {next}, the next member on hold.";
            }
            return RedirectToPage();
        }

        private static string LabelOf(string? type)
        {
            foreach (var t in LibraryOptions.Types)
                if (t.Value == type) return t.Label;
            return type ?? "";
        }
    }
}
