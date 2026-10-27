using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Pages.Shared;
using Project.Models;
using Project.Data;

namespace Project.Pages
{
    public record RentalRow(int ItemId, string? ItemName, string ItemType,
                            DateTime StartDate, DateTime? DueDate, DateTime? ReturnedDate);

    public class BorrowerRentalsModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public BorrowerRentalsModel(LibraryDbContext db) => _db = db;

        // Passed from the Checkout Activity page
        [BindProperty(SupportsGet = true)] public int UserId { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
        [BindProperty(SupportsGet = true)] public string? BorrowerType { get; set; }

        public string BorrowerName { get; set; } = "";
        public Student? StudentInfo { get; set; }
    public FacultyMember? FacultyInfo { get; set; }
        public List<RentalRow> Rows { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _db.LibraryUsers.FirstOrDefaultAsync(u => u.LibraryUserId == UserId);
            if (user == null) return NotFound();
            BorrowerName = user.FullName;
            StudentInfo = await _db.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LibraryUserId == UserId);

if (StudentInfo == null)
    FacultyInfo = await _db.FacultyMembers.AsNoTracking()
        .FirstOrDefaultAsync(f => f.LibraryUserId == UserId);

            From ??= DateTime.Today.AddDays(-30);
            To ??= DateTime.Today;
            var start = From.Value.Date;
            var endExclusive = To.Value.Date.AddDays(1);

            
            Rows = await (
                from r in _db.Rentals
                join i in _db.Inventory on r.ItemId equals i.Id
                where r.LibraryUserId == UserId
                   && r.StartDate >= start && r.StartDate < endExclusive
                orderby r.StartDate descending
                select new RentalRow(i.Id, i.Name, i.Type, r.StartDate, r.EndDate, r.ReturnedDate)
            ).ToListAsync();

            return Page();
        }
    }
}