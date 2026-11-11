using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;

namespace Project.Pages
{
    // Supporting records for the Circulation & Collection Performance report:
    // the checkouts, copies, holds and borrowers each summary number was counted from.
    // Uses the same rules as CollectionPerformanceModel (checkouts by checkout date).
    public class CirculationRecordsModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public CirculationRecordsModel(LibraryDbContext db) => _db = db;

        private static readonly string[] WaitingStatuses = { "Pending", "Ready" };

        // Report filters
        [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
        [BindProperty(SupportsGet = true)] public string? Category { get; set; }

        // What to list: checkouts | copies | holds | borrowers
        [BindProperty(SupportsGet = true)] public string Show { get; set; } = "checkouts";

        // Narrowing
        [BindProperty(SupportsGet = true)] public string? Title { get; set; }       // one title (all its copies)
        [BindProperty(SupportsGet = true)] public string? Month { get; set; }       // yyyy-MM
        [BindProperty(SupportsGet = true)] public bool Unborrowed { get; set; }     // copies with no checkout
        [BindProperty(SupportsGet = true)] public bool Waiting { get; set; }        // holds still waiting
        [BindProperty(SupportsGet = true)] public bool InPeriod { get; set; }       // holds placed in the period

        public RecordTable Table { get; set; } = new();
        public List<string> Narrowing { get; set; } = new();

        public async Task OnGetAsync() => await BuildAsync();

        public async Task<IActionResult> OnGetCsvAsync()
        {
            await BuildAsync();
            return File(Table.ToCsv(), "text/csv", $"circulation-{Show}-{DateTime.Today:yyyy-MM-dd}.csv");
        }

        public Dictionary<string, string> RouteValues()
        {
            var d = new Dictionary<string, string> { ["Show"] = Show };
            void Put(string k, string? v) { if (!string.IsNullOrEmpty(v)) d[k] = v; }
            Put("From", From?.ToString("yyyy-MM-dd")); Put("To", To?.ToString("yyyy-MM-dd"));
            Put("Category", Category); Put("Title", Title); Put("Month", Month);
            if (Unborrowed) d["Unborrowed"] = "true";
            if (Waiting) d["Waiting"] = "true";
            if (InPeriod) d["InPeriod"] = "true";
            return d;
        }

        private async Task BuildAsync()
        {
            From ??= DateTime.Today.AddDays(-90);
            To ??= DateTime.Today;
            var start = From.Value.Date;
            var end = To.Value.Date.AddDays(1);

            var items = (await _db.Inventory.AsNoTracking()
                    .Select(i => new { i.Id, i.Name, i.Type, i.Status })
                    .ToListAsync())
                .Where(i => string.IsNullOrEmpty(Category) || i.Type == Category)
                .Where(i => string.IsNullOrEmpty(Title) || (i.Name ?? "(untitled)") == Title)
                .ToList();
            var itemById = items.ToDictionary(i => i.Id);
            var names = await _db.LibraryUsers.AsNoTracking().ToDictionaryAsync(u => u.LibraryUserId, u => u.FullName);
            var studentIds = (await _db.Students.AsNoTracking().Select(s => s.LibraryUserId).ToListAsync()).ToHashSet();
            var facultyIds = (await _db.FacultyMembers.AsNoTracking().Select(f => f.LibraryUserId).ToListAsync()).ToHashSet();
            string TypeOf(int id) => studentIds.Contains(id) ? "Student" : facultyIds.Contains(id) ? "Faculty" : "Other";
            string Name(int id) => names.GetValueOrDefault(id) ?? $"Member #{id}";

            var allRentals = (await _db.Rentals.AsNoTracking()
                    .Select(r => new { r.RentalId, r.ItemId, r.LibraryUserId, r.StartDate, r.EndDate, r.ReturnedDate })
                    .ToListAsync())
                .Where(r => itemById.ContainsKey(r.ItemId))
                .ToList();
            var periodRentals = allRentals.Where(r => r.StartDate >= start && r.StartDate < end).ToList();

            Narrowing.Add($"{From:MMM d, yyyy} – {To:MMM d, yyyy}");
            if (!string.IsNullOrEmpty(Category)) Narrowing.Add(LabelOf(Category));
            if (!string.IsNullOrEmpty(Title)) Narrowing.Add($"\"{Title}\"");
            if (!string.IsNullOrEmpty(Month) && DateTime.TryParse(Month + "-01", out var mo)) Narrowing.Add(mo.ToString("MMMM yyyy"));

            switch (Show)
            {
                case "copies":
                {
                    if (Unborrowed) Narrowing.Add("Not borrowed in the period");
                    var rows = items.Where(i => !Unborrowed || !periodRentals.Any(r => r.ItemId == i.Id))
                                    .OrderBy(i => i.Type).ThenBy(i => i.Name).ToList();
                    Table = new RecordTable { Title = Unborrowed ? "Copies not borrowed" : "Copies", Description = "Each physical copy, its status, and how often it was checked out in the period." };
                    Table.Column("Item #").Column("Title").Column("Category").Column("Status")
                         .Column("Checkouts in period", true).Column("Last checked out");
                    foreach (var i in rows)
                    {
                        var mine = allRentals.Where(r => r.ItemId == i.Id).ToList();
                        Table.Add(i.Id, i.Name, LabelOf(i.Type), i.Status, periodRentals.Count(r => r.ItemId == i.Id),
                                  mine.Count == 0 ? (DateTime?)null : mine.Max(r => r.StartDate));
                    }
                    int used = rows.Count(i => periodRentals.Any(r => r.ItemId == i.Id));
                    Table.Totals = new[] { "Total", $"{rows.Count} copies", "", "",
                                           periodRentals.Count(r => rows.Any(i => i.Id == r.ItemId)).ToString(), $"{used} borrowed at least once" };
                    break;
                }

                case "holds":
                {
                    if (Waiting) Narrowing.Add("Still waiting");
                    if (InPeriod) Narrowing.Add("Placed in the period");
                    var holds = (await _db.Holds.AsNoTracking().Where(h => h.ItemId != null).ToListAsync())
                        .Where(h => itemById.ContainsKey(h.ItemId!.Value))
                        .Where(h => !Waiting || WaitingStatuses.Contains(h.Status))
                        .Where(h => !InPeriod || (h.RequestDatetime >= start && h.RequestDatetime < end))
                        .OrderBy(h => h.RequestDatetime).ToList();
                    Table = new RecordTable { Title = "Holds", Description = "Members waiting for, or reserving, a copy." };
                    Table.Column("Hold #").Column("Title").Column("Category").Column("Member").Column("Requested")
                         .Column("Status").Column("Ready").Column("Pickup by");
                    foreach (var h in holds)
                    {
                        var i = itemById[h.ItemId!.Value];
                        Table.Add(h.HoldId, i.Name, LabelOf(i.Type), Name(h.LibraryUserId), h.RequestDatetime, h.Status, h.ReadyDate, h.PickupDeadline);
                    }
                    Table.Totals = new[] { "Total", $"{holds.Count} holds", "", "", "", $"{holds.Count(h => WaitingStatuses.Contains(h.Status))} waiting", "", "" };
                    break;
                }

                case "borrowers":
                {
                    var rows = periodRentals.Where(r => string.IsNullOrEmpty(Month) || r.StartDate.ToString("yyyy-MM") == Month)
                        .GroupBy(r => r.LibraryUserId)
                        .Select(g => new { Id = g.Key, Count = g.Count(), Last = g.Max(r => r.StartDate) })
                        .OrderByDescending(x => x.Count).ThenBy(x => Name(x.Id)).ToList();
                    Table = new RecordTable { Title = "Borrowers", Description = "Each member who checked out at least one item in the period." };
                    Table.Column("Member #").Column("Member").Column("Type").Column("Checkouts", true).Column("Last checkout");
                    foreach (var x in rows)
                        Table.Add(x.Id, Name(x.Id), TypeOf(x.Id), x.Count, x.Last);
                    Table.Totals = new[] { "Total", $"{rows.Count} borrowers", "", rows.Sum(x => x.Count).ToString(), "" };
                    break;
                }

                default: // checkouts
                {
                    Show = "checkouts";
                    var rows = periodRentals.Where(r => string.IsNullOrEmpty(Month) || r.StartDate.ToString("yyyy-MM") == Month)
                                            .OrderByDescending(r => r.StartDate).ToList();
                    Table = new RecordTable { Title = "Checkouts", Description = "Every checkout that started in the period." };
                    Table.Column("Loan #").Column("Title").Column("Category").Column("Item #").Column("Borrower")
                         .Column("Borrower type").Column("Checked out").Column("Due").Column("Returned");
                    foreach (var r in rows)
                    {
                        var i = itemById[r.ItemId];
                        Table.Add(r.RentalId, i.Name, LabelOf(i.Type), i.Id, Name(r.LibraryUserId), TypeOf(r.LibraryUserId),
                                  r.StartDate, r.EndDate, r.ReturnedDate);
                    }
                    Table.Totals = new[] { "Total", $"{rows.Count} checkouts", "", "", $"{rows.Select(r => r.LibraryUserId).Distinct().Count()} borrowers", "", "", "", "" };
                    break;
                }
            }
        }

        public static string LabelOf(string? type)
        {
            foreach (var t in LibraryOptions.Types)
                if (t.Value == type) return t.Label;
            return type ?? "";
        }
    }
}
