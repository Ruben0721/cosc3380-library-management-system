using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Pages.Shared;

namespace Project.Pages
{
    public record BorrowerRow(int LibraryUserId, string Name, string Type, int Checkouts);

    public class CheckoutActivityModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public CheckoutActivityModel(LibraryDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
        [BindProperty(SupportsGet = true)] public string? BorrowerType { get; set; } = "student";

        public int BorrowerCount { get; set; }
        public int TotalCheckouts { get; set; }
        public List<BorrowerRow> Rows { get; set; } = new();

        public async Task OnGetAsync()
        {
            From ??= DateTime.Today.AddDays(-30);
            To ??= DateTime.Today;

            var start = From.Value.Date;
            var endExclusive = To.Value.Date.AddDays(1);


            var rentals =
                from r in _db.Rentals
                join u in _db.LibraryUsers on r.LibraryUserId equals u.LibraryUserId
                where r.StartDate >= start && r.StartDate < endExclusive
                select new
                {
                    r.LibraryUserId,
                    u.FullName,
                    IsStudent = _db.Students.Any(s => s.LibraryUserId == r.LibraryUserId),
                    IsFaculty = _db.FacultyMembers.Any(f => f.LibraryUserId == r.LibraryUserId)
                };

           
            if (BorrowerType == "student")
                rentals = rentals.Where(x => x.IsStudent);
            else if (BorrowerType == "faculty")
                rentals = rentals.Where(x => x.IsFaculty);

            TotalCheckouts = await rentals.CountAsync();

            BorrowerCount = await rentals
                .Select(x => x.LibraryUserId)
                .Distinct()
                .CountAsync();

            var grouped = await rentals
                .GroupBy(x => new { x.LibraryUserId, x.FullName, x.IsStudent, x.IsFaculty })
                .Select(g => new { g.Key.LibraryUserId, g.Key.FullName, g.Key.IsStudent, g.Key.IsFaculty, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();

            Rows = grouped
                .Select(g => new BorrowerRow(
                    g.LibraryUserId,
                    g.FullName,
                    g.IsStudent ? "Student" : g.IsFaculty ? "Faculty" : "Other",
                    g.Count))
                .ToList();
        }
    }
}