using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;
using Project.Models;

namespace Project.Pages
{
    public record MaintCategoryRow(string Category, int Items, int OutOfService, double AvailabilityPct,
                                   int Returns, int Damaged, double DamageRate,
                                   int RequestsOpened, int StillOpen, double? AvgDaysToResolve, decimal DamageFees);

    public record MaintOpenRow(int RequestId, string? ItemName, string Category, string RequestType, string? Reason,
                               string Status, DateTime CreatedAt, int DaysOpen, string ItemStatus, string? LastBorrower, int ItemId);

    public record MaintItemRow(int ItemId, string? ItemName, string Category, string ItemStatus, int Loans,
                               int DamageRepairs180, int DamageRepairsTotal, double DamageRate,
                               decimal FeesAssessed, decimal? ReplacementCost, string Recommendation, string Why);

    public record MaintInspectionRow(string? ItemName, string Category, int Every, int ReturnsSinceLast,
                                     int ReturnsUntilNext, DateTime? LastInspection, string ItemStatus, int ItemId);

    public record MaintMonthRow(string Month, int DamageRepairs, int Inspections, int ReplacementReviews, int Resolved);

    public class EquipmentMaintenanceModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public EquipmentMaintenanceModel(LibraryDbContext db) => _db = db;

        // ----- Filters -----
        [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
        [BindProperty(SupportsGet = true)] public string? Category { get; set; }
        [BindProperty(SupportsGet = true)] public string? RequestType { get; set; }

        public static readonly string[] RequestTypes = { "Damage Repair", "Routine Inspection", "Replacement Review" };
        private static readonly string[] OpenStatuses = { "Open", "In Progress" };

        // ----- Summary -----
        public int ItemsTracked { get; set; }
        public int OutOfServiceNow { get; set; }
        public int RequestsOpened { get; set; }
        public int PrevRequestsOpened { get; set; }
        public int RequestsResolved { get; set; }
        public int OpenNow { get; set; }
        public int OldestOpenDays { get; set; }
        public double? AvgDaysToResolve { get; set; }
        public int ReturnsInPeriod { get; set; }
        public int DamagedReturns { get; set; }
        public double DamageRate { get; set; }
        public decimal DamageFees { get; set; }
        public DateTime PrevFrom { get; set; }
        public DateTime PrevTo { get; set; }
        public Dictionary<string, int> OpenedByType { get; set; } = new();

        // ----- Sections -----
        public List<MaintCategoryRow> ByCategory { get; set; } = new();
        public List<MaintOpenRow> OpenQueue { get; set; } = new();
        public List<MaintItemRow> ItemReliability { get; set; } = new();
        public List<MaintInspectionRow> Inspections { get; set; } = new();
        public List<MaintMonthRow> Monthly { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();

        public async Task OnGetAsync()
        {
            From ??= DateTime.Today.AddDays(-90);
            To ??= DateTime.Today;
            var start = From.Value.Date;
            var end = To.Value.Date.AddDays(1);
            var now = DateTime.Now;
            var length = end - start;
            PrevFrom = start - length;
            PrevTo = start.AddDays(-1);

            // ---- Load data ----
            var items = await _db.Inventory.AsNoTracking()
                .Select(i => new { i.Id, i.Name, i.Type, i.Status })
                .ToDictionaryAsync(i => i.Id);

            var names = await _db.LibraryUsers.AsNoTracking()
                .ToDictionaryAsync(u => u.LibraryUserId, u => u.FullName);

            var policies = await _db.FinePolicies.AsNoTracking().ToDictionaryAsync(p => p.ItemType);

            var rentals = (await _db.Rentals.AsNoTracking()
                    .Select(r => new { r.RentalId, r.ItemId, r.LibraryUserId, r.StartDate, r.ReturnedDate })
                    .ToListAsync())
                .Where(r => items.ContainsKey(r.ItemId))
                .ToList();
            var rentalById = rentals.ToDictionary(r => r.RentalId);

            var allRequests = (await _db.MaintenanceRequests.AsNoTracking().ToListAsync())
                .Where(m => items.ContainsKey(m.ItemId))
                .ToList();

            // Damage / lost fines, linked to the item through the rental
            var damageFines = (await _db.Fines.AsNoTracking()
                    .Where(f => f.FineType == "Damaged" || f.FineType == "Lost")
                    .ToListAsync())
                .Where(f => rentalById.ContainsKey(f.RentalId))
                .Select(f => new { f.Amount, f.DateAssessed, rentalById[f.RentalId].ItemId })
                .ToList();

            bool InCategory(int itemId) => string.IsNullOrEmpty(Category) || items[itemId].Type == Category;
            bool IsOutOfService(string? status) => status == "Under Repair" || status == "Lost";

            var catItems = items.Values.Where(i => InCategory(i.Id)).ToList();
            var requests = allRequests.Where(m => InCategory(m.ItemId)
                                               && (string.IsNullOrEmpty(RequestType) || m.RequestType == RequestType))
                                      .ToList();

            var opened = requests.Where(m => m.CreatedAt >= start && m.CreatedAt < end).ToList();
            var resolved = requests.Where(m => m.ResolvedAt >= start && m.ResolvedAt < end).ToList();
            var openNow = requests.Where(m => OpenStatuses.Contains(m.Status)).ToList();

            // Damaged returns are counted from Damage Repair requests (one per damaged return)
            var damageInPeriod = allRequests.Where(m => InCategory(m.ItemId) && m.RequestType == "Damage Repair"
                                                     && m.CreatedAt >= start && m.CreatedAt < end).ToList();
            var returns = rentals.Where(r => InCategory(r.ItemId)
                                          && r.ReturnedDate >= start && r.ReturnedDate < end).ToList();

            // ---- Summary ----
            ItemsTracked = catItems.Count;
            OutOfServiceNow = catItems.Count(i => IsOutOfService(i.Status));
            RequestsOpened = opened.Count;
            PrevRequestsOpened = requests.Count(m => m.CreatedAt >= PrevFrom && m.CreatedAt < start);
            RequestsResolved = resolved.Count;
            OpenNow = openNow.Count;
            OldestOpenDays = openNow.Count == 0 ? 0 : openNow.Max(m => (now - m.CreatedAt).Days);
            AvgDaysToResolve = resolved.Count == 0 ? null
                : resolved.Average(m => (m.ResolvedAt!.Value - m.CreatedAt).TotalDays);
            ReturnsInPeriod = returns.Count;
            DamagedReturns = damageInPeriod.Count;
            DamageRate = ReturnsInPeriod == 0 ? 0 : 100.0 * DamagedReturns / ReturnsInPeriod;
            DamageFees = damageFines.Where(f => InCategory(f.ItemId) && f.DateAssessed >= start && f.DateAssessed < end)
                                    .Sum(f => f.Amount);
            OpenedByType = RequestTypes.ToDictionary(t => t, t => opened.Count(m => m.RequestType == t));

            // ---- By category ----
            ByCategory = catItems
                .GroupBy(i => i.Type)
                .Select(g =>
                {
                    var ids = g.Select(i => i.Id).ToHashSet();
                    int oos = g.Count(i => IsOutOfService(i.Status));
                    int ret = returns.Count(r => ids.Contains(r.ItemId));
                    int dmg = damageInPeriod.Count(m => ids.Contains(m.ItemId));
                    var res = resolved.Where(m => ids.Contains(m.ItemId)).ToList();
                    return new MaintCategoryRow(
                        LabelOf(g.Key), g.Count(), oos, 100.0 * (g.Count() - oos) / g.Count(),
                        ret, dmg, ret == 0 ? 0 : 100.0 * dmg / ret,
                        opened.Count(m => ids.Contains(m.ItemId)),
                        openNow.Count(m => ids.Contains(m.ItemId)),
                        res.Count == 0 ? null : res.Average(m => (m.ResolvedAt!.Value - m.CreatedAt).TotalDays),
                        damageFines.Where(f => ids.Contains(f.ItemId) && f.DateAssessed >= start && f.DateAssessed < end)
                                   .Sum(f => f.Amount));
                })
                .OrderByDescending(c => c.DamageRate).ThenByDescending(c => c.RequestsOpened)
                .ToList();

            // ---- Open work queue (all open requests, oldest first) ----
            OpenQueue = openNow
                .Select(m =>
                {
                    var it = items[m.ItemId];
                    string? borrower = m.RentalId != null && rentalById.TryGetValue(m.RentalId.Value, out var r)
                        ? names.GetValueOrDefault(r.LibraryUserId) : null;
                    return new MaintOpenRow(m.RequestId, it.Name, LabelOf(it.Type), m.RequestType, m.Reason,
                        m.Status, m.CreatedAt, (now - m.CreatedAt).Days, it.Status ?? "", borrower, m.ItemId);
                })
                .OrderBy(o => o.RequestType == "Replacement Review" ? 0 : o.RequestType == "Damage Repair" ? 1 : 2)
                .ThenByDescending(o => o.DaysOpen)
                .ToList();

            // ---- Item reliability: items with any damage repair ----
            var since180 = end.AddDays(-180);
            ItemReliability = allRequests
                .Where(m => m.RequestType == "Damage Repair" && InCategory(m.ItemId))
                .GroupBy(m => m.ItemId)
                .Select(g =>
                {
                    var it = items[g.Key];
                    int loans = rentals.Count(r => r.ItemId == g.Key);
                    int total = g.Count();
                    int recent = g.Count(m => m.CreatedAt >= since180 && m.CreatedAt < end);
                    decimal fees = damageFines.Where(f => f.ItemId == g.Key).Sum(f => f.Amount);
                    decimal? replacement = policies.TryGetValue(it.Type, out var p) ? p.ReplacementCost : null;
                    bool reviewOpen = allRequests.Any(m => m.ItemId == g.Key && m.RequestType == "Replacement Review"
                                                        && OpenStatuses.Contains(m.Status));

                    string rec, why;
                    if (it.Status == "Lost")
                    { rec = "Replace"; why = "Item is lost"; }
                    else if (recent >= 3 || reviewOpen)
                    { rec = "Replace"; why = $"{recent} damage repairs in the last 180 days" + (reviewOpen ? "; replacement review open" : ""); }
                    else if (replacement != null && fees >= replacement)
                    { rec = "Replace"; why = $"Damage fees ({fees:C}) have reached its replacement cost ({replacement:C})"; }
                    else if (recent == 2)
                    { rec = "Monitor"; why = "2 damage repairs in the last 180 days"; }
                    else
                    { rec = "Repair"; why = "Isolated damage"; }

                    return new MaintItemRow(g.Key, it.Name, LabelOf(it.Type), it.Status ?? "", loans, recent, total,
                        loans == 0 ? 0 : 100.0 * total / loans, fees, replacement, rec, why);
                })
                .OrderBy(r => r.Recommendation == "Replace" ? 0 : r.Recommendation == "Monitor" ? 1 : 2)
                .ThenByDescending(r => r.DamageRepairs180)
                .ToList();

            // ---- Routine inspection schedule (categories with an inspection interval) ----
            foreach (var it in catItems)
            {
                if (!policies.TryGetValue(it.Type, out var p) || p.InspectionEvery is not int every || every <= 0) continue;

                var lastInspection = allRequests
                    .Where(m => m.ItemId == it.Id && m.RequestType == "Routine Inspection")
                    .Select(m => (DateTime?)m.CreatedAt)
                    .Max();
                int sinceLast = rentals.Count(r => r.ItemId == it.Id && r.ReturnedDate != null
                                                && (lastInspection == null || r.ReturnedDate > lastInspection));
                int untilNext = Math.Max(every - sinceLast, 0);
                Inspections.Add(new MaintInspectionRow(it.Name, LabelOf(it.Type), every, sinceLast, untilNext,
                    lastInspection, it.Status ?? "", it.Id));
            }
            Inspections = Inspections.OrderBy(i => i.ReturnsUntilNext).ThenBy(i => i.ItemName).ToList();

            // ---- Monthly trend ----
            for (var m = new DateTime(start.Year, start.Month, 1); m < end; m = m.AddMonths(1))
            {
                bool InMonth(DateTime? d) => d != null && d.Value.Year == m.Year && d.Value.Month == m.Month;
                Monthly.Add(new MaintMonthRow(m.ToString("MMM yyyy"),
                    requests.Count(r => r.RequestType == "Damage Repair" && InMonth(r.CreatedAt)),
                    requests.Count(r => r.RequestType == "Routine Inspection" && InMonth(r.CreatedAt)),
                    requests.Count(r => r.RequestType == "Replacement Review" && InMonth(r.CreatedAt)),
                    requests.Count(r => InMonth(r.ResolvedAt))));
            }

            BuildRecommendations();
        }

        private void BuildRecommendations()
        {
            // Items to replace
            var replace = ItemReliability.Where(r => r.Recommendation == "Replace").ToList();
            if (replace.Count > 0)
            {
                decimal cost = replace.Sum(r => r.ReplacementCost ?? 0);
                Recommendations.Add($"Replace {replace.Count} item(s): {string.Join(", ", replace.Select(r => r.ItemName))}. " +
                                    $"Estimated replacement budget: {cost:C}.");
            }

            var monitor = ItemReliability.Where(r => r.Recommendation == "Monitor").ToList();
            if (monitor.Count > 0)
                Recommendations.Add($"Watch {string.Join(", ", monitor.Select(r => r.ItemName))}: one more damage repair " +
                                    "within 180 days will trigger a replacement review.");

            // Categories damaged much more often than average
            foreach (var c in ByCategory.Where(c => c.Damaged >= 2 && DamageRate > 0 && c.DamageRate >= DamageRate * 1.5))
                Recommendations.Add($"{c.Category}: {c.DamageRate:0}% of returns came back damaged vs. {DamageRate:0}% overall. " +
                                    "Inspect at checkout, add protective cases, or raise the damage fee.");

            // Low availability because of repairs
            foreach (var c in ByCategory.Where(c => c.OutOfService > 0 && c.AvailabilityPct < 80))
                Recommendations.Add($"{c.Category}: only {c.AvailabilityPct:0}% of copies are in service ({c.OutOfService} of {c.Items} out). " +
                                    "Prioritize these repairs or add a spare unit.");

            // Slow repairs / stale tickets
            int stale = OpenQueue.Count(o => o.DaysOpen > 14);
            if (stale > 0)
                Recommendations.Add($"{stale} maintenance request(s) have been open more than 14 days (oldest {OldestOpenDays} days).");
            if (AvgDaysToResolve > 7)
                Recommendations.Add($"Requests took {AvgDaysToResolve:0.0} days on average to resolve. Aim for under a week.");

            // Inspections coming up
            int dueSoon = Inspections.Count(i => i.ReturnsUntilNext <= 2);
            if (dueSoon > 0)
                Recommendations.Add($"{dueSoon} item(s) are within 2 returns of their next routine inspection.");

            // Trend
            if (PrevRequestsOpened > 0 && RequestsOpened >= PrevRequestsOpened * 1.5 && RequestsOpened - PrevRequestsOpened >= 2)
                Recommendations.Add($"Maintenance requests rose from {PrevRequestsOpened} to {RequestsOpened} vs. the previous period.");

            if (Recommendations.Count == 0)
                Recommendations.Add("No maintenance issues detected for this period.");
        }

        private static string LabelOf(string type)
        {
            foreach (var t in LibraryOptions.Types)
                if (t.Value == type) return t.Label;
            return type;
        }
    }
}
