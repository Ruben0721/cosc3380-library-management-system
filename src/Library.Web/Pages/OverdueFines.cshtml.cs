using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;
using Project.Models;

namespace Project.Pages
{
    // One row of a "late returns by ..." table
    public record LateGroupRow(string Group, int Due, int Late, int StillOverdue,
                               double LateRate, double AvgDaysLate, decimal Fines);

    public record OverdueNowRow(int ItemId, string? ItemName, string Category, string Borrower,
                                string BorrowerType, DateTime DueDate, int DaysOverdue, decimal EstimatedFine);

    public record LateRepeatRow(int MemberId, string Borrower, string BorrowerType, int LateReturns,
                            decimal FinesAssessed, decimal Unpaid);

    public record FineTypeRow(string FineType, int Count, decimal Assessed, decimal Paid, decimal Outstanding);

    public record PaymentMethodRow(string Method, int Count, decimal Amount);

    public record LateMonthRow(string Month, int Due, int Late, double LateRate);

    // Money per month: fines charged, payments received, and what is still unpaid
    public record FineMonthRow(string Month, int FinesCount, decimal Assessed,
                               int PaymentsCount, decimal Collected, decimal StillUnpaid);

    public class OverdueFinesModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public OverdueFinesModel(LibraryDbContext db) => _db = db;

        // ----- Filters -----
        [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
        [BindProperty(SupportsGet = true)] public string? Category { get; set; }
        [BindProperty(SupportsGet = true)] public string? BorrowerType { get; set; }   // "", "Student", "Faculty"

        // ----- Summary metrics -----
        public int LoansDue { get; set; }
        public int ReturnedOnTime { get; set; }
        public int ReturnedLate { get; set; }
        public int StillOutOverdue { get; set; }
        public double LateRate { get; set; }
        public double PrevLateRate { get; set; }
        public double AvgDaysLate { get; set; }
        public decimal FinesAssessed { get; set; }
        public decimal FinesPaid { get; set; }
        public decimal FinesOutstanding => FinesAssessed - FinesPaid;
        public double CollectionRate => FinesAssessed == 0 ? 0 : (double)(FinesPaid / FinesAssessed) * 100;
        public decimal PaymentsReceived { get; set; }
        public DateTime PrevFrom { get; set; }
        public DateTime PrevTo { get; set; }

        // ----- Report sections -----
        public List<LateGroupRow> ByCategory { get; set; } = new();
        public List<LateGroupRow> ByBorrowerType { get; set; } = new();
        public List<FineTypeRow> FinesByType { get; set; } = new();
        public List<PaymentMethodRow> PaymentsByMethod { get; set; } = new();
        public List<LateMonthRow> Monthly { get; set; } = new();
        public List<FineMonthRow> MonthlyFines { get; set; } = new();
        public List<LateRepeatRow> RepeatLateReturners { get; set; } = new();
        public List<OverdueNowRow> OverdueNow { get; set; } = new();
        public List<FinePolicy> Policies { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();

        private class Loan
        {
            public int RentalId, ItemId, LibraryUserId;
            public DateTime Due;
            public DateTime? Returned;
            public string Type = "";
            public string BorrowerType = "";
        }

        public async Task OnGetAsync()
        {
            // Default period: the last 90 days (by due date)
            From ??= DateTime.Today.AddDays(-90);
            To ??= DateTime.Today;
            var start = From.Value.Date;
            var end = To.Value.Date.AddDays(1);
            var now = DateTime.Now;

            var length = end - start;
            PrevFrom = start - length;
            PrevTo = start.AddDays(-1);

            // ---- Lookups ----
            var names = await _db.LibraryUsers.AsNoTracking()
                .ToDictionaryAsync(u => u.LibraryUserId, u => u.FullName);
            var studentIds = (await _db.Students.AsNoTracking().Select(s => s.LibraryUserId).ToListAsync()).ToHashSet();
            var facultyIds = (await _db.FacultyMembers.AsNoTracking().Select(f => f.LibraryUserId).ToListAsync()).ToHashSet();
            string TypeOfBorrower(int id) => studentIds.Contains(id) ? "Student" : facultyIds.Contains(id) ? "Faculty" : "Other";

            var items = await _db.Inventory.AsNoTracking()
                .Select(i => new { i.Id, i.Name, i.Type })
                .ToDictionaryAsync(i => i.Id);

            Policies = await _db.FinePolicies.AsNoTracking().OrderBy(p => p.ItemType).ToListAsync();
            var policyByType = Policies.ToDictionary(p => p.ItemType);

            // ---- All loans, with category and borrower type ----
            var loans = (await _db.Rentals.AsNoTracking()
                    .Select(r => new { r.RentalId, r.ItemId, r.LibraryUserId, r.StartDate, r.EndDate, r.ReturnedDate })
                    .ToListAsync())
                .Where(r => items.ContainsKey(r.ItemId))
                .Select(r => new Loan
                {
                    RentalId = r.RentalId,
                    ItemId = r.ItemId,
                    LibraryUserId = r.LibraryUserId,
                    Due = r.EndDate ?? r.StartDate,
                    Returned = r.ReturnedDate,
                    Type = items[r.ItemId].Type,
                    BorrowerType = TypeOfBorrower(r.LibraryUserId)
                })
                .ToList();

            bool MatchesFilters(Loan l) =>
                (string.IsNullOrEmpty(Category) || l.Type == Category) &&
                (string.IsNullOrEmpty(BorrowerType) || l.BorrowerType == BorrowerType);

            var filtered = loans.Where(MatchesFilters).ToList();
            var loanById = loans.ToDictionary(l => l.RentalId);

            // Late = returned after the due date, or still out past the due date
            bool IsLate(Loan l) => l.Returned != null ? l.Returned.Value.Date > l.Due.Date : l.Due < now;
            int DaysLate(Loan l) => ((l.Returned ?? now).Date - l.Due.Date).Days;

            // ---- Loans due in the period ----
            var due = filtered.Where(l => l.Due >= start && l.Due < end).ToList();
            var late = due.Where(IsLate).ToList();

            LoansDue = due.Count;
            ReturnedLate = late.Count(l => l.Returned != null);
            StillOutOverdue = late.Count(l => l.Returned == null);
            ReturnedOnTime = due.Count(l => l.Returned != null && !IsLate(l));
            LateRate = LoansDue == 0 ? 0 : 100.0 * late.Count / LoansDue;
            AvgDaysLate = late.Count == 0 ? 0 : late.Average(DaysLate);

            var prevDue = filtered.Where(l => l.Due >= PrevFrom && l.Due < start).ToList();
            PrevLateRate = prevDue.Count == 0 ? 0 : 100.0 * prevDue.Count(IsLate) / prevDue.Count;

            // ---- Fines assessed in the period (same filters, via the rental) ----
            var fines = (await _db.Fines.AsNoTracking()
                    .Where(f => f.DateAssessed >= start && f.DateAssessed < end)
                    .ToListAsync())
                .Where(f => loanById.TryGetValue(f.RentalId, out var l) && MatchesFilters(l))
                .ToList();

            FinesAssessed = fines.Sum(f => f.Amount);
            FinesPaid = fines.Where(f => f.IsPaid).Sum(f => f.Amount);

            FinesByType = new[] { "Overdue", "Damaged", "Lost" }
                .Select(t =>
                {
                    var g = fines.Where(f => f.FineType == t).ToList();
                    var paid = g.Where(f => f.IsPaid).Sum(f => f.Amount);
                    return new FineTypeRow(t, g.Count, g.Sum(f => f.Amount), paid, g.Sum(f => f.Amount) - paid);
                })
                .Where(r => r.Count > 0)
                .ToList();

            decimal FinesFor(IEnumerable<Loan> group)
            {
                var ids = group.Select(l => l.RentalId).ToHashSet();
                return fines.Where(f => ids.Contains(f.RentalId)).Sum(f => f.Amount);
            }

            // ---- Payments received in the period ----
            var fineById = await _db.Fines.AsNoTracking().ToDictionaryAsync(f => f.FineId);
            var payments = (await _db.Payments.AsNoTracking()
                    .Where(p => p.PaymentDate >= start && p.PaymentDate < end)
                    .ToListAsync())
                .Where(p =>
                {
                    if (!string.IsNullOrEmpty(BorrowerType) && TypeOfBorrower(p.LibraryUserId) != BorrowerType) return false;
                    if (string.IsNullOrEmpty(Category)) return true;
                    return p.FineId != null
                        && fineById.TryGetValue(p.FineId.Value, out var f)
                        && loanById.TryGetValue(f.RentalId, out var l)
                        && l.Type == Category;
                })
                .ToList();

            PaymentsReceived = payments.Sum(p => p.Amount);
            PaymentsByMethod = payments
                .GroupBy(p => p.PaymentMethod)
                .Select(g => new PaymentMethodRow(g.Key, g.Count(), g.Sum(p => p.Amount)))
                .OrderByDescending(r => r.Amount)
                .ToList();

            // ---- Late returns by category and by borrower type ----
            LateGroupRow GroupRow(string label, List<Loan> g)
            {
                var gl = g.Where(IsLate).ToList();
                return new LateGroupRow(label, g.Count, gl.Count, gl.Count(l => l.Returned == null),
                    g.Count == 0 ? 0 : 100.0 * gl.Count / g.Count,
                    gl.Count == 0 ? 0 : gl.Average(DaysLate),
                    FinesFor(g));
            }

            ByCategory = due.GroupBy(l => l.Type)
                .Select(g => GroupRow(LabelOf(g.Key), g.ToList()))
                .OrderByDescending(r => r.LateRate)
                .ToList();

            ByBorrowerType = due.GroupBy(l => l.BorrowerType)
                .Select(g => GroupRow(g.Key, g.ToList()))
                .OrderByDescending(r => r.LateRate)
                .ToList();

            // ---- Monthly trend (by due month) ----
            for (var m = new DateTime(start.Year, start.Month, 1); m < end; m = m.AddMonths(1))
            {
                var g = due.Where(l => l.Due.Year == m.Year && l.Due.Month == m.Month).ToList();
                int lateCount = g.Count(IsLate);
                Monthly.Add(new LateMonthRow(m.ToString("MMM yyyy"), g.Count, lateCount,
                    g.Count == 0 ? 0 : 100.0 * lateCount / g.Count));

                // Fines charged this month, payments received this month
                var monthFines = fines.Where(f => f.DateAssessed.Year == m.Year && f.DateAssessed.Month == m.Month).ToList();
                var monthPayments = payments.Where(p => p.PaymentDate.Year == m.Year && p.PaymentDate.Month == m.Month).ToList();
                MonthlyFines.Add(new FineMonthRow(m.ToString("MMM yyyy"),
                    monthFines.Count, monthFines.Sum(f => f.Amount),
                    monthPayments.Count, monthPayments.Sum(p => p.Amount),
                    monthFines.Where(f => !f.IsPaid).Sum(f => f.Amount)));
            }

            // ---- Members with 2+ late returns in the period ----
            RepeatLateReturners = late
                .GroupBy(l => l.LibraryUserId)
                .Where(g => g.Count() >= 2)
                .Select(g =>
                {
                    var memberFines = fines.Where(f => f.LibraryUserId == g.Key).ToList();
                    return new LateRepeatRow(
                        g.Key,
                        names.GetValueOrDefault(g.Key, $"Member #{g.Key}"),
                        TypeOfBorrower(g.Key),
                        g.Count(),
                        memberFines.Sum(f => f.Amount),
                        memberFines.Where(f => !f.IsPaid).Sum(f => f.Amount));
                })
                .OrderByDescending(r => r.LateReturns).ThenByDescending(r => r.Unpaid)
                .ToList();

            // ---- Items overdue right now (not limited to the period) ----
            OverdueNow = filtered
                .Where(l => l.Returned == null && l.Due < now)
                .Select(l =>
                {
                    int days = (now.Date - l.Due.Date).Days;
                    decimal est = 0;
                    if (policyByType.TryGetValue(l.Type, out var p))
                        est = Math.Min(days * p.DailyOverdueRate, p.OverdueCap);
                    return new OverdueNowRow(l.ItemId, items[l.ItemId].Name, LabelOf(l.Type),
                        names.GetValueOrDefault(l.LibraryUserId, $"Member #{l.LibraryUserId}"),
                        l.BorrowerType, l.Due, days, est);
                })
                .OrderByDescending(r => r.DaysOverdue)
                .ToList();

            BuildRecommendations(policyByType);
        }

        // Category value ("laptop") -> label ("Laptop"), and back
        private static string LabelOf(string type)
        {
            foreach (var t in LibraryOptions.Types)
                if (t.Value == type) return t.Label;
            return type;
        }

        private static string ValueOf(string label)
        {
            foreach (var t in LibraryOptions.Types)
                if (t.Label == label) return t.Value;
            return label;
        }

        private void BuildRecommendations(Dictionary<string, FinePolicy> policyByType)
        {
            // Categories returned late much more often than average
            foreach (var c in ByCategory.Where(c => c.Late >= 2 && LateRate > 0 && c.LateRate >= LateRate * 1.5))
            {
                var type = ValueOf(c.Group);
                var rate = policyByType.TryGetValue(type, out var p) ? $"${p.DailyOverdueRate:0.00}/day, {p.LoanDays}-day loan" : "current policy";
                Recommendations.Add($"{c.Group}: {c.LateRate:0}% of loans came back late vs. {LateRate:0}% overall ({rate}). " +
                                    "Consider a shorter loan period, a due-date reminder, or a higher daily rate.");
            }

            // Students vs faculty
            var s = ByBorrowerType.FirstOrDefault(r => r.Group == "Student");
            var f = ByBorrowerType.FirstOrDefault(r => r.Group == "Faculty");
            if (s != null && f != null && Math.Abs(s.LateRate - f.LateRate) >= 15)
            {
                var worse = s.LateRate > f.LateRate ? s : f;
                var better = worse == s ? f : s;
                Recommendations.Add($"{worse.Group} borrowers return late {worse.LateRate:0}% of the time vs. {better.LateRate:0}% for {better.Group.ToLower()}. " +
                                    $"Target reminders and outreach at {worse.Group.ToLower()} borrowers.");
            }

            // Fine collection
            if (FinesAssessed > 0 && CollectionRate < 60)
                Recommendations.Add($"Only {CollectionRate:0}% of fines assessed in this period were paid (${FinesOutstanding:0.00} outstanding). " +
                                    "Send fine notices and consider blocking new checkouts for members with unpaid balances.");

            // Repeat late returners
            if (RepeatLateReturners.Count > 0)
                Recommendations.Add($"{RepeatLateReturners.Count} member(s) returned items late two or more times. " +
                                    "Consider a warning notice or a temporary reduction of their rental limit.");

            // Items overdue right now
            if (OverdueNow.Count > 0)
                Recommendations.Add($"{OverdueNow.Count} item(s) are overdue right now, with ${OverdueNow.Sum(o => o.EstimatedFine):0.00} in fines accruing. " +
                                    "Contact these borrowers.");

            // Trend vs previous period
            if (PrevLateRate > 0 && LateRate - PrevLateRate >= 10)
                Recommendations.Add($"The late-return rate rose from {PrevLateRate:0}% to {LateRate:0}% vs. the previous period.");
            else if (PrevLateRate > 0 && PrevLateRate - LateRate >= 10)
                Recommendations.Add($"The late-return rate improved from {PrevLateRate:0}% to {LateRate:0}% vs. the previous period.");

            if (Recommendations.Count == 0)
                Recommendations.Add("No issues detected for this period.");
        }
    }
}
