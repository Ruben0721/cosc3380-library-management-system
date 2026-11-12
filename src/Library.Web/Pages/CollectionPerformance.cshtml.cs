using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;
using Project.Models;

namespace Project.Pages
{
    public record CategoryRow(string Category, string Label, int Items, int Available, int UnderRepair,
                              int Checkouts, int PrevCheckouts, double PerItem, double NeverPct, string Demand);

    // One row per TITLE (all copies of the same work combined)
    public record TitleRow(string Title, string Category, int Copies, int Available,
                           int Checkouts, double PerCopy, int Waiting, int HoldsInPeriod, string Demand);

    public class CollectionPerformanceModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public CollectionPerformanceModel(LibraryDbContext db) => _db = db;

        // Hold statuses that mean "the member still wants the item"
        private static readonly string[] UnmetHoldStatuses = { "Pending", "Ready" };

        // ----- Filters -----
        [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
        [BindProperty(SupportsGet = true)] public string? Category { get; set; }
        [BindProperty(SupportsGet = true)] public string? DemandFilter { get; set; }

        public List<(string Value, string Label)> Types => LibraryOptions.Types;

        // ----- Summary metrics -----
        public int TotalCopies { get; set; }
        public int TotalTitles { get; set; }
        public int TotalCheckouts { get; set; }
        public int PrevCheckouts { get; set; }
        public int UniqueBorrowers { get; set; }
        public double UtilizationPct { get; set; }
        public double AvgPerItem { get; set; }
        public double LibraryAvgPerCopy { get; set; }   // whole library, ignoring the category filter
        public int TotalWaiting { get; set; }
        public DateTime PrevFrom { get; set; }
        public DateTime PrevTo { get; set; }

        // ----- Report sections -----
        public List<CategoryRow> Categories { get; set; } = new();
        public List<string> Months { get; set; } = new();
        public Dictionary<string, Dictionary<string, int>> Trend { get; set; } = new();
        public List<TitleRow> TopTitles { get; set; } = new();
        public List<TitleRow> MostWaitlisted { get; set; } = new();
        public List<TitleRow> LeastBorrowed { get; set; } = new();
        public List<TitleRow> NeverBorrowed { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();

        public async Task OnGetAsync()
        {
            // Default period: the last 90 days
            From ??= DateTime.Today.AddDays(-90);
            To ??= DateTime.Today;
            var start = From.Value.Date;
            var end = To.Value.Date.AddDays(1);              // include the whole "To" day

            // The previous period of the same length, for comparison
            var length = end - start;
            PrevFrom = start - length;
            PrevTo = start.AddDays(-1);

            // ---- 1. Copies (optionally one category) ----
            var itemQuery = _db.Inventory.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(Category))
                itemQuery = itemQuery.Where(i => i.Type == Category);

            var items = await itemQuery
                .Select(i => new { i.Id, i.Name, i.Type, i.Status })
                .ToListAsync();
            var itemIds = items.Select(i => i.Id).ToList();
            var itemById = items.ToDictionary(i => i.Id);

            // ---- 2. Rentals in this period and the previous one ----
            var rentals = await _db.Rentals.AsNoTracking()
                .Where(r => itemIds.Contains(r.ItemId) && r.StartDate >= start && r.StartDate < end)
                .Select(r => new { r.ItemId, r.LibraryUserId, r.StartDate })
                .ToListAsync();

            var prevRentals = await _db.Rentals.AsNoTracking()
                .Where(r => itemIds.Contains(r.ItemId) && r.StartDate >= PrevFrom && r.StartDate < start)
                .Select(r => new { r.ItemId })
                .ToListAsync();

            var countByItem = rentals.GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Count());
            var prevByItem = prevRentals.GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Count());
            int Checkouts(int id) => countByItem.TryGetValue(id, out var c) ? c : 0;

            // ---- 3. Holds (waitlist) per copy ----
            var holds = await _db.Holds.AsNoTracking()
                .Where(h => h.ItemId != null && itemIds.Contains(h.ItemId.Value))
                .Select(h => new { ItemId = h.ItemId!.Value, h.RequestDatetime, h.Status })
                .ToListAsync();

            var waitingByItem = holds.Where(h => UnmetHoldStatuses.Contains(h.Status))
                                     .GroupBy(h => h.ItemId).ToDictionary(g => g.Key, g => g.Count());
            var holdsInPeriodByItem = holds.Where(h => h.RequestDatetime >= start && h.RequestDatetime < end)
                                           .GroupBy(h => h.ItemId).ToDictionary(g => g.Key, g => g.Count());

            // ---- 4. Summary metrics ----
            TotalCopies = items.Count;
            TotalTitles = items.Select(i => new { i.Type, i.Name }).Distinct().Count();
            TotalCheckouts = rentals.Count;
            PrevCheckouts = prevRentals.Count;
            UniqueBorrowers = rentals.Select(r => r.LibraryUserId).Distinct().Count();
            UtilizationPct = TotalCopies == 0 ? 0 : 100.0 * items.Count(i => Checkouts(i.Id) > 0) / TotalCopies;
            AvgPerItem = TotalCopies == 0 ? 0 : (double)TotalCheckouts / TotalCopies;
            TotalWaiting = waitingByItem.Values.Sum();

            // Library-wide benchmark for demand levels (independent of the category filter),
            // so a category is compared with the whole library, not with itself
            var allCopies = await _db.Inventory.CountAsync();
            var allCheckouts = await _db.Rentals.CountAsync(r => r.StartDate >= start && r.StartDate < end);
            LibraryAvgPerCopy = allCopies == 0 ? 0 : (double)allCheckouts / allCopies;

            // ---- 5. Performance by category ----
            foreach (var t in Types)
            {
                var catItems = items.Where(i => i.Type == t.Value).ToList();
                if (catItems.Count == 0) continue;

                int checkouts = catItems.Sum(i => Checkouts(i.Id));
                int prev = catItems.Sum(i => prevByItem.TryGetValue(i.Id, out var c) ? c : 0);
                double perItem = (double)checkouts / catItems.Count;
                double neverPct = 100.0 * catItems.Count(i => Checkouts(i.Id) == 0) / catItems.Count;

                Categories.Add(new CategoryRow(
                    t.Value, t.Label,
                    catItems.Count,
                    catItems.Count(i => i.Status == "Available"),
                    catItems.Count(i => i.Status == "Under Repair"),
                    checkouts, prev, perItem, neverPct,
                    CategoryDemand(perItem)));
            }

            // ---- 6. Monthly trend (category -> month -> checkouts) ----
            for (var m = new DateTime(start.Year, start.Month, 1); m < end; m = m.AddMonths(1))
                Months.Add(m.ToString("yyyy-MM"));

            foreach (var c in Categories)
                Trend[c.Label] = Months.ToDictionary(m => m, m => 0);

            foreach (var r in rentals)
            {
                var label = LabelOf(itemById[r.ItemId].Type);
                var month = r.StartDate.ToString("yyyy-MM");
                if (Trend.ContainsKey(label) && Trend[label].ContainsKey(month))
                    Trend[label][month]++;
            }

            // ---- 7. Title-level performance (all copies of a work combined) ----
            var titles = items
                .GroupBy(i => new { i.Type, Title = i.Name ?? "(untitled)" })
                .Select(g =>
                {
                    int copies = g.Count();
                    int checkouts = g.Sum(i => Checkouts(i.Id));
                    double perCopy = (double)checkouts / copies;
                    return new TitleRow(
                        g.Key.Title,
                        LabelOf(g.Key.Type),
                        copies,
                        g.Count(i => i.Status == "Available"),
                        checkouts,
                        perCopy,
                        g.Sum(i => waitingByItem.GetValueOrDefault(i.Id)),
                        g.Sum(i => holdsInPeriodByItem.GetValueOrDefault(i.Id)),
                        TitleDemand(perCopy));
                })
                .ToList();

            if (!string.IsNullOrEmpty(DemandFilter))
                titles = titles.Where(t => t.Demand == DemandFilter).ToList();

            TopTitles = titles.Where(t => t.Checkouts > 0)
                              .OrderByDescending(t => t.PerCopy).ThenByDescending(t => t.Waiting)
                              .Take(10).ToList();

            MostWaitlisted = titles.Where(t => t.Waiting > 0 || t.HoldsInPeriod > 0)
                                   .OrderByDescending(t => t.Waiting).ThenByDescending(t => t.HoldsInPeriod)
                                   .Take(10).ToList();

            LeastBorrowed = titles.Where(t => t.Checkouts > 0 && t.Waiting == 0)   
                                  .OrderBy(t => t.PerCopy).ThenBy(t => t.Title)
                                  .Take(10).ToList();

            NeverBorrowed = titles.Where(t => t.Checkouts == 0)
                                  .OrderBy(t => t.Category).ThenBy(t => t.Title)
                                  .ToList();

            // ---- 8. Recommendations ----
            BuildRecommendations();
        }

        // Demand level of a category, compared with the library-wide average
        private string CategoryDemand(double perItem)
        {
            if (LibraryAvgPerCopy == 0) return perItem > 0 ? "High" : "None";
            var ratio = perItem / LibraryAvgPerCopy;
            return ratio >= 1.5 ? "High" : ratio >= 0.75 ? "Medium" : perItem > 0 ? "Low" : "None";
        }

        // Demand level of a title, by checkouts per copy in the period
        private static string TitleDemand(double perCopy) =>
            perCopy >= 4 ? "High" : perCopy >= 2 ? "Medium" : perCopy > 0 ? "Low" : "Never borrowed";

        private string LabelOf(string type)
        {
            var match = Types.FirstOrDefault(t => t.Value == type);
            return string.IsNullOrEmpty(match.Label) ? type : match.Label;
        }

        private void BuildRecommendations()
        {
            // Titles where members are waiting: compare the line with the number of copies
            foreach (var t in MostWaitlisted.Where(t => t.Waiting >= 3 || (t.Waiting > 0 && t.Available == 0 && t.Waiting >= t.Copies)))
                Recommendations.Add($"{t.Title}: {t.Waiting} member(s) waiting, {t.Copies} cop{(t.Copies == 1 ? "y" : "ies")} owned, {t.Available} available. Consider buying another copy.");

            // Heavily used titles with nothing on the shelf
            foreach (var t in TopTitles.Where(t => t.Demand == "High" && t.Available == 0 && t.Waiting == 0))
                Recommendations.Add($"{t.Title} is in high demand ({t.PerCopy:0.0} checkouts per copy) and no copies are available. Monitor for a waiting list.");

            foreach (var c in Categories)
            {
                double change = c.PrevCheckouts == 0 ? 0 : 100.0 * (c.Checkouts - c.PrevCheckouts) / c.PrevCheckouts;

                if (c.Demand == "High" && c.Available <= c.Items * 0.2)
                    Recommendations.Add($"Purchase more {c.Label.ToLower()}: demand is {c.PerItem / Math.Max(LibraryAvgPerCopy, 0.01):0.0}× the library average and only {c.Available} of {c.Items} are available.");

                if (c.NeverPct >= 50)
                    Recommendations.Add($"Review the {c.Label.ToLower()} collection: {c.NeverPct:0}% of copies were not borrowed in this period. Consider weeding or promoting them.");

                if (c.UnderRepair >= Math.Max(1, c.Items * 0.25))
                    Recommendations.Add($"{c.UnderRepair} of {c.Items} {c.Label.ToLower()} are under repair. Review maintenance or replacement.");

                if (c.PrevCheckouts > 0 && change >= 25)
                    Recommendations.Add($"Demand for {c.Label.ToLower()} rose {change:0}% vs. the previous period. Monitor availability.");
                else if (c.PrevCheckouts > 0 && change <= -25)
                    Recommendations.Add($"Demand for {c.Label.ToLower()} fell {Math.Abs(change):0}% vs. the previous period.");
            }

            if (Recommendations.Count == 0)
                Recommendations.Add("No issues detected for this period.");
        }
    }
}