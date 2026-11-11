using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;

namespace Project.Pages
{
    // Supporting records for the Overdue Returns & Fines report:
    // the individual loans, fines and payments each summary number was counted from.
    // Uses the same rules as OverdueFinesModel, so the counts match the report.
    public class OverdueRecordsModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public OverdueRecordsModel(LibraryDbContext db) => _db = db;

        // Report filters (same names as the report, so links carry them over)
        [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
        [BindProperty(SupportsGet = true)] public string? Category { get; set; }
        [BindProperty(SupportsGet = true)] public string? BorrowerType { get; set; }

        // What to list: loans | overdue-now | fines | payments
        [BindProperty(SupportsGet = true)] public string Show { get; set; } = "loans";

        // Narrowing from a table row
        [BindProperty(SupportsGet = true)] public string? Outcome { get; set; }   // ontime | late | returned-late | still-overdue | not-due
        [BindProperty(SupportsGet = true)] public string? FineType { get; set; }  // Overdue | Damaged | Lost
        [BindProperty(SupportsGet = true)] public string? Paid { get; set; }      // paid | unpaid
        [BindProperty(SupportsGet = true)] public string? Method { get; set; }    // cash | card | check
        [BindProperty(SupportsGet = true)] public string? Month { get; set; }     // yyyy-MM
        [BindProperty(SupportsGet = true)] public int? MemberId { get; set; }

        public RecordTable Table { get; set; } = new();
        public List<string> Narrowing { get; set; } = new();

        public async Task OnGetAsync() => await BuildAsync();

        // The current filters, for the CSV link
        public Dictionary<string, string> RouteValues()
        {
            var d = new Dictionary<string, string> { ["Show"] = Show };
            void Put(string k, string? v) { if (!string.IsNullOrEmpty(v)) d[k] = v; }
            Put("From", From?.ToString("yyyy-MM-dd")); Put("To", To?.ToString("yyyy-MM-dd"));
            Put("Category", Category); Put("BorrowerType", BorrowerType); Put("Outcome", Outcome);
            Put("FineType", FineType); Put("Paid", Paid); Put("Method", Method); Put("Month", Month);
            Put("MemberId", MemberId?.ToString());
            return d;
        }

        public async Task<IActionResult> OnGetCsvAsync()
        {
            await BuildAsync();
            return File(Table.ToCsv(), "text/csv", $"overdue-{Show}-{DateTime.Today:yyyy-MM-dd}.csv");
        }

        private class Loan
        {
            public int RentalId, ItemId, LibraryUserId;
            public DateTime Start, Due;
            public DateTime? Returned;
            public string Type = "";
            public string BorrowerType = "";
        }

        private async Task BuildAsync()
        {
            From ??= DateTime.Today.AddDays(-90);
            To ??= DateTime.Today;
            var start = From.Value.Date;
            var end = To.Value.Date.AddDays(1);
            var now = DateTime.Now;

            var names = await _db.LibraryUsers.AsNoTracking().ToDictionaryAsync(u => u.LibraryUserId, u => u.FullName);
            var studentIds = (await _db.Students.AsNoTracking().Select(s => s.LibraryUserId).ToListAsync()).ToHashSet();
            var facultyIds = (await _db.FacultyMembers.AsNoTracking().Select(f => f.LibraryUserId).ToListAsync()).ToHashSet();
            string TypeOfBorrower(int id) => studentIds.Contains(id) ? "Student" : facultyIds.Contains(id) ? "Faculty" : "Other";
            string Name(int id) => names.GetValueOrDefault(id) ?? $"Member #{id}";

            var items = await _db.Inventory.AsNoTracking()
                .Select(i => new { i.Id, i.Name, i.Type })
                .ToDictionaryAsync(i => i.Id);
            var policies = await _db.FinePolicies.AsNoTracking().ToDictionaryAsync(p => p.ItemType);

            var loans = (await _db.Rentals.AsNoTracking()
                    .Select(r => new { r.RentalId, r.ItemId, r.LibraryUserId, r.StartDate, r.EndDate, r.ReturnedDate })
                    .ToListAsync())
                .Where(r => items.ContainsKey(r.ItemId))
                .Select(r => new Loan
                {
                    RentalId = r.RentalId, ItemId = r.ItemId, LibraryUserId = r.LibraryUserId,
                    Start = r.StartDate, Due = r.EndDate ?? r.StartDate, Returned = r.ReturnedDate,
                    Type = items[r.ItemId].Type, BorrowerType = TypeOfBorrower(r.LibraryUserId)
                })
                .ToList();
            var loanById = loans.ToDictionary(l => l.RentalId);

            bool MatchesFilters(Loan l) =>
                (string.IsNullOrEmpty(Category) || l.Type == Category) &&
                (string.IsNullOrEmpty(BorrowerType) || l.BorrowerType == BorrowerType) &&
                (MemberId == null || l.LibraryUserId == MemberId);

            bool IsLate(Loan l) => l.Returned != null ? l.Returned.Value.Date > l.Due.Date : l.Due < now;
            int DaysLate(Loan l) => Math.Max(((l.Returned ?? now).Date - l.Due.Date).Days, 0);
            bool InMonth(DateTime d) => string.IsNullOrEmpty(Month) || d.ToString("yyyy-MM") == Month;
            string Item(int id) => items.TryGetValue(id, out var i) ? i.Name ?? $"Item #{id}" : $"Item #{id}";
            string Cat(int id) => items.TryGetValue(id, out var i) ? LabelOf(i.Type) : "";

            var allFines = await _db.Fines.AsNoTracking().ToListAsync();
            var fineById = allFines.ToDictionary(f => f.FineId);

            // Describe the narrowing so the reader knows what they are looking at
            Narrowing.Add($"{From:MMM d, yyyy} – {To:MMM d, yyyy}");
            if (!string.IsNullOrEmpty(Category)) Narrowing.Add(LabelOf(Category));
            if (!string.IsNullOrEmpty(BorrowerType)) Narrowing.Add(BorrowerType);
            if (MemberId != null) Narrowing.Add(Name(MemberId.Value));
            if (!string.IsNullOrEmpty(Month) && DateTime.TryParse(Month + "-01", out var mo)) Narrowing.Add(mo.ToString("MMMM yyyy"));

            switch (Show)
            {
                case "overdue-now":
                {
                    Narrowing[0] = "Today, any due date";
                    var rows = loans.Where(l => l.Returned == null && l.Due < now && MatchesFilters(l))
                                    .OrderByDescending(l => (now.Date - l.Due.Date).Days).ToList();
                    Table = new RecordTable { Title = "Items overdue right now", Description = "Loans still out past their due date. Fine so far = days overdue × daily rate, up to the category's cap." };
                    Table.Column("Loan #").Column("Item").Column("Category").Column("Borrower").Column("Borrower type")
                         .Column("Checked out").Column("Due").Column("Days overdue", true).Column("Fine so far", true);
                    decimal total = 0;
                    foreach (var l in rows)
                    {
                        int days = (now.Date - l.Due.Date).Days;
                        decimal est = policies.TryGetValue(l.Type, out var p) ? Math.Min(days * p.DailyOverdueRate, p.OverdueCap) : 0;
                        total += est;
                        Table.Add(l.RentalId, Item(l.ItemId), Cat(l.ItemId), Name(l.LibraryUserId), l.BorrowerType, l.Start, l.Due, days, est);
                    }
                    Table.Totals = new[] { "Total", $"{rows.Count} loans", "", "", "", "", "", "", RecordTable.Money(total) };
                    break;
                }

                case "fines":
                {
                    if (!string.IsNullOrEmpty(FineType)) Narrowing.Add($"{FineType} fines");
                    if (!string.IsNullOrEmpty(Paid)) Narrowing.Add(Paid == "paid" ? "Paid" : "Unpaid");
                    var rows = allFines
                        .Where(f => f.DateAssessed >= start && f.DateAssessed < end && InMonth(f.DateAssessed))
                        .Where(f => loanById.TryGetValue(f.RentalId, out var l) && MatchesFilters(l))
                        .Where(f => string.IsNullOrEmpty(FineType) || f.FineType == FineType)
                        .Where(f => string.IsNullOrEmpty(Paid) || (Paid == "paid") == f.IsPaid)
                        .OrderByDescending(f => f.DateAssessed).ToList();
                    Table = new RecordTable { Title = "Fines assessed", Description = "Each fine the return trigger charged, with whether it has been paid." };
                    Table.Column("Fine #").Column("Assessed").Column("Member").Column("Item").Column("Category")
                         .Column("Type").Column("Days late", true).Column("Amount", true).Column("Paid").Column("Notes");
                    foreach (var f in rows)
                    {
                        var l = loanById[f.RentalId];
                        Table.Add(f.FineId, f.DateAssessed, Name(f.LibraryUserId), Item(l.ItemId), Cat(l.ItemId),
                                  f.FineType, f.DaysLate, f.Amount, f.IsPaid ? "Yes" : "No", f.Notes);
                    }
                    Table.Totals = new[] { "Total", $"{rows.Count} fines", "", "", "", "", "", RecordTable.Money(rows.Sum(f => f.Amount)),
                                           $"{RecordTable.Money(rows.Where(f => !f.IsPaid).Sum(f => f.Amount))} unpaid", "" };
                    break;
                }

                case "payments":
                {
                    if (!string.IsNullOrEmpty(Method)) Narrowing.Add(Method);
                    var rows = (await _db.Payments.AsNoTracking()
                            .Where(p => p.PaymentDate >= start && p.PaymentDate < end)
                            .ToListAsync())
                        .Where(p => InMonth(p.PaymentDate))
                        .Where(p => string.IsNullOrEmpty(Method) || p.PaymentMethod == Method)
                        .Where(p => string.IsNullOrEmpty(BorrowerType) || TypeOfBorrower(p.LibraryUserId) == BorrowerType)
                        .Where(p => MemberId == null || p.LibraryUserId == MemberId)
                        .Where(p => string.IsNullOrEmpty(Category) ||
                                    (p.FineId != null && fineById.TryGetValue(p.FineId.Value, out var f)
                                     && loanById.TryGetValue(f.RentalId, out var l) && l.Type == Category))
                        .OrderByDescending(p => p.PaymentDate).ToList();
                    Table = new RecordTable { Title = "Payments received", Description = "Each payment recorded at the desk, and the fine it paid." };
                    Table.Column("Payment #").Column("Date").Column("Member").Column("For fine").Column("Item").Column("Method").Column("Amount", true);
                    foreach (var p in rows)
                    {
                        string forFine = "—", item = "—";
                        if (p.FineId != null && fineById.TryGetValue(p.FineId.Value, out var f))
                        {
                            forFine = $"#{f.FineId} {f.FineType}";
                            if (loanById.TryGetValue(f.RentalId, out var l)) item = Item(l.ItemId);
                        }
                        Table.Add(p.PaymentId, p.PaymentDate, Name(p.LibraryUserId), forFine, item, p.PaymentMethod, p.Amount);
                    }
                    Table.Totals = new[] { "Total", $"{rows.Count} payments", "", "", "", "", RecordTable.Money(rows.Sum(p => p.Amount)) };
                    break;
                }

                default: // loans due in the period
                {
                    Show = "loans";
                    string outcomeLabel = Outcome switch
                    {
                        "ontime" => "Returned on time", "late" => "Late (returned late or still overdue)",
                        "returned-late" => "Returned late", "still-overdue" => "Still out, overdue",
                        "not-due" => "Still out, not yet due", _ => ""
                    };
                    if (outcomeLabel != "") Narrowing.Add(outcomeLabel);

                    var rows = loans.Where(l => l.Due >= start && l.Due < end && InMonth(l.Due) && MatchesFilters(l))
                        .Where(l => Outcome switch
                        {
                            "ontime" => l.Returned != null && !IsLate(l),
                            "late" => IsLate(l),
                            "returned-late" => l.Returned != null && IsLate(l),
                            "still-overdue" => l.Returned == null && IsLate(l),
                            "not-due" => l.Returned == null && !IsLate(l),
                            _ => true
                        })
                        .OrderBy(l => l.Due).ToList();

                    Table = new RecordTable { Title = "Loans due", Description = "Every loan whose due date falls in the period. Late = returned after the due date, or still out past it." };
                    Table.Column("Loan #").Column("Item").Column("Category").Column("Borrower").Column("Borrower type")
                         .Column("Checked out").Column("Due").Column("Returned").Column("Days late", true).Column("Outcome").Column("Fines", true);
                    foreach (var l in rows)
                    {
                        string outcome = l.Returned != null ? (IsLate(l) ? "Returned late" : "On time")
                                                            : (IsLate(l) ? "Still overdue" : "Not yet due");
                        decimal loanFines = allFines.Where(f => f.RentalId == l.RentalId).Sum(f => f.Amount);
                        Table.Add(l.RentalId, Item(l.ItemId), Cat(l.ItemId), Name(l.LibraryUserId), l.BorrowerType,
                                  l.Start, l.Due, l.Returned, IsLate(l) ? DaysLate(l) : 0, outcome, loanFines);
                    }
                    int lateCount = rows.Count(IsLate);
                    Table.Totals = new[] { "Total", $"{rows.Count} loans", "", "", "", "", "", "",
                                           lateCount == 0 ? "0" : $"avg {rows.Where(IsLate).Average(DaysLate):0.0}",
                                           $"{lateCount} late", "" };
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
