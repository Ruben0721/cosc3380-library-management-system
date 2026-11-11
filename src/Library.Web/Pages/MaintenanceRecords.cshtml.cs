using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;

namespace Project.Pages
{
    // Supporting records for the Equipment Maintenance report:
    // the maintenance requests, returns and items each summary number was counted from.
    // Uses the same rules as EquipmentMaintenanceModel, so the counts match the report.
    public class MaintenanceRecordsModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public MaintenanceRecordsModel(LibraryDbContext db) => _db = db;

        private static readonly string[] OpenStatuses = { "Open", "In Progress" };

        // Report filters
        [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
        [BindProperty(SupportsGet = true)] public string? Category { get; set; }
        [BindProperty(SupportsGet = true)] public string? RequestType { get; set; }

        // What to list: requests | returns | items | item
        [BindProperty(SupportsGet = true)] public string Show { get; set; } = "requests";

        // Narrowing
        [BindProperty(SupportsGet = true)] public string? Scope { get; set; }   // requests: opened | resolved | open
        [BindProperty(SupportsGet = true)] public string? Month { get; set; }   // yyyy-MM
        [BindProperty(SupportsGet = true)] public string? State { get; set; }   // items: out
        [BindProperty(SupportsGet = true)] public int? ItemId { get; set; }

        public RecordTable Table { get; set; } = new();
        public RecordTable? Second { get; set; }      // item history shows loans under the requests
        public List<string> Narrowing { get; set; } = new();

        public async Task OnGetAsync() => await BuildAsync();

        public async Task<IActionResult> OnGetCsvAsync()
        {
            await BuildAsync();
            return File(Table.ToCsv(), "text/csv", $"maintenance-{Show}-{DateTime.Today:yyyy-MM-dd}.csv");
        }

        public Dictionary<string, string> RouteValues()
        {
            var d = new Dictionary<string, string> { ["Show"] = Show };
            void Put(string k, string? v) { if (!string.IsNullOrEmpty(v)) d[k] = v; }
            Put("From", From?.ToString("yyyy-MM-dd")); Put("To", To?.ToString("yyyy-MM-dd"));
            Put("Category", Category); Put("RequestType", RequestType); Put("Scope", Scope);
            Put("Month", Month); Put("State", State); Put("ItemId", ItemId?.ToString());
            return d;
        }

        private async Task BuildAsync()
        {
            From ??= DateTime.Today.AddDays(-90);
            To ??= DateTime.Today;
            var start = From.Value.Date;
            var end = To.Value.Date.AddDays(1);
            var now = DateTime.Now;

            var items = await _db.Inventory.AsNoTracking()
                .Select(i => new { i.Id, i.Name, i.Type, i.Status })
                .ToDictionaryAsync(i => i.Id);
            var names = await _db.LibraryUsers.AsNoTracking().ToDictionaryAsync(u => u.LibraryUserId, u => u.FullName);
            var rentals = (await _db.Rentals.AsNoTracking()
                    .Select(r => new { r.RentalId, r.ItemId, r.LibraryUserId, r.StartDate, r.EndDate, r.ReturnedDate })
                    .ToListAsync())
                .Where(r => items.ContainsKey(r.ItemId))
                .ToList();
            var rentalById = rentals.ToDictionary(r => r.RentalId);
            var allRequests = (await _db.MaintenanceRequests.AsNoTracking().ToListAsync())
                .Where(m => items.ContainsKey(m.ItemId))
                .ToList();
            var fines = await _db.Fines.AsNoTracking()
                .Where(f => f.FineType == "Damaged" || f.FineType == "Lost")
                .ToListAsync();

            bool InCategory(int id) => string.IsNullOrEmpty(Category) || items[id].Type == Category;
            bool InMonth(DateTime? d) => string.IsNullOrEmpty(Month) || (d != null && d.Value.ToString("yyyy-MM") == Month);
            string ItemName(int id) => items[id].Name ?? $"Item #{id}";
            string Cat(int id) => LabelOf(items[id].Type);
            string Borrower(int? rentalId) => rentalId != null && rentalById.TryGetValue(rentalId.Value, out var r)
                ? names.GetValueOrDefault(r.LibraryUserId) ?? $"Member #{r.LibraryUserId}" : "—";

            Narrowing.Add($"{From:MMM d, yyyy} – {To:MMM d, yyyy}");
            if (!string.IsNullOrEmpty(Category)) Narrowing.Add(LabelOf(Category));
            if (!string.IsNullOrEmpty(RequestType)) Narrowing.Add(RequestType);
            if (!string.IsNullOrEmpty(Month) && DateTime.TryParse(Month + "-01", out var mo)) Narrowing.Add(mo.ToString("MMMM yyyy"));

            void RequestColumns(RecordTable t) =>
                t.Column("Request #").Column("Item").Column("Category").Column("Type").Column("Reason").Column("Status")
                 .Column("Opened").Column("Resolved").Column("Days", true).Column("Loan #").Column("Borrower");

            void AddRequest(RecordTable t, Project.Models.MaintenanceRequest m) =>
                t.Add(m.RequestId, ItemName(m.ItemId), Cat(m.ItemId), m.RequestType, m.Reason, m.Status,
                      m.CreatedAt, m.ResolvedAt, ((m.ResolvedAt ?? now) - m.CreatedAt).Days, m.RentalId, Borrower(m.RentalId));

            switch (Show)
            {
                case "returns":
                {
                    // Every return in the period, with its condition (from the triggers' records)
                    var damagedRentals = allRequests.Where(m => m.RequestType == "Damage Repair" && m.RentalId != null)
                                                    .Select(m => m.RentalId!.Value).ToHashSet();
                    var lostRentals = fines.Where(f => f.FineType == "Lost").Select(f => f.RentalId).ToHashSet();
                    var rows = rentals.Where(r => InCategory(r.ItemId) && r.ReturnedDate >= start && r.ReturnedDate < end && InMonth(r.ReturnedDate))
                                      .OrderByDescending(r => r.ReturnedDate).ToList();
                    Table = new RecordTable { Title = "Items returned", Description = "Every return in the period and the condition recorded at the desk." };
                    Table.Column("Loan #").Column("Item").Column("Category").Column("Borrower").Column("Checked out")
                         .Column("Returned").Column("Condition").Column("Damage / loss fee", true);
                    foreach (var r in rows)
                    {
                        string cond = lostRentals.Contains(r.RentalId) ? "Lost" : damagedRentals.Contains(r.RentalId) ? "Damaged" : "Good";
                        decimal fee = fines.Where(f => f.RentalId == r.RentalId).Sum(f => f.Amount);
                        Table.Add(r.RentalId, ItemName(r.ItemId), Cat(r.ItemId), names.GetValueOrDefault(r.LibraryUserId) ?? "—",
                                  r.StartDate, r.ReturnedDate, cond, fee);
                    }
                    int damaged = rows.Count(r => damagedRentals.Contains(r.RentalId));
                    Table.Totals = new[] { "Total", $"{rows.Count} returns", "", "", "", "", $"{damaged} damaged",
                                           RecordTable.Money(rows.Sum(r => fines.Where(f => f.RentalId == r.RentalId).Sum(f => f.Amount))) };
                    break;
                }

                case "items":
                {
                    bool outOnly = State == "out";
                    if (outOnly) Narrowing.Add("Out of service");
                    Narrowing[0] = "Current status";
                    var rows = items.Values.Where(i => InCategory(i.Id))
                        .Where(i => !outOnly || i.Status == "Under Repair" || i.Status == "Lost")
                        .OrderBy(i => i.Type).ThenBy(i => i.Name).ToList();
                    Table = new RecordTable { Title = outOnly ? "Items out of service" : "Items", Description = "Each copy, its current status and its maintenance history." };
                    Table.Column("Item #").Column("Item").Column("Category").Column("Status").Column("Loans", true)
                         .Column("Damage repairs", true).Column("Open requests", true).Column("Last request");
                    foreach (var i in rows)
                    {
                        var reqs = allRequests.Where(m => m.ItemId == i.Id).ToList();
                        var last = reqs.OrderByDescending(m => m.CreatedAt).FirstOrDefault();
                        Table.Add(i.Id, i.Name, LabelOf(i.Type), i.Status, rentals.Count(r => r.ItemId == i.Id),
                                  reqs.Count(m => m.RequestType == "Damage Repair"),
                                  reqs.Count(m => OpenStatuses.Contains(m.Status)),
                                  last == null ? "—" : $"{last.RequestType}, {last.CreatedAt:MMM d}");
                    }
                    Table.Totals = new[] { "Total", $"{rows.Count} items", "", "", "", "", "", "" };
                    break;
                }

                case "item":
                {
                    if (ItemId == null || !items.ContainsKey(ItemId.Value)) { Show = "requests"; goto default; }
                    int id = ItemId.Value;
                    Narrowing.Clear();
                    Narrowing.Add($"{ItemName(id)} · {Cat(id)} · now {items[id].Status}");
                    Table = new RecordTable { Title = $"History of {ItemName(id)}", Description = "Every maintenance request for this item, newest first." };
                    RequestColumns(Table);
                    foreach (var m in allRequests.Where(m => m.ItemId == id).OrderByDescending(m => m.CreatedAt))
                        AddRequest(Table, m);

                    Second = new RecordTable { Title = "Loans of this item", Description = "Every time it was borrowed, with the condition it came back in." };
                    Second.Column("Loan #").Column("Borrower").Column("Checked out").Column("Due").Column("Returned").Column("Condition").Column("Fee", true);
                    var damaged = allRequests.Where(m => m.ItemId == id && m.RequestType == "Damage Repair" && m.RentalId != null)
                                             .Select(m => m.RentalId!.Value).ToHashSet();
                    foreach (var r in rentals.Where(r => r.ItemId == id).OrderByDescending(r => r.StartDate))
                    {
                        string cond = r.ReturnedDate == null ? "Still out"
                            : fines.Any(f => f.RentalId == r.RentalId && f.FineType == "Lost") ? "Lost"
                            : damaged.Contains(r.RentalId) ? "Damaged" : "Good";
                        Second.Add(r.RentalId, names.GetValueOrDefault(r.LibraryUserId) ?? "—", r.StartDate, r.EndDate,
                                   r.ReturnedDate, cond, fines.Where(f => f.RentalId == r.RentalId).Sum(f => f.Amount));
                    }
                    break;
                }

                default: // requests
                {
                    Show = "requests";
                    var scope = Scope ?? "opened";
                    var baseSet = allRequests.Where(m => InCategory(m.ItemId) && (string.IsNullOrEmpty(RequestType) || m.RequestType == RequestType));
                    List<Project.Models.MaintenanceRequest> rows;
                    switch (scope)
                    {
                        case "open":
                            Narrowing[0] = "Open now, any date";
                            rows = baseSet.Where(m => OpenStatuses.Contains(m.Status)).OrderBy(m => m.CreatedAt).ToList();
                            Table = new RecordTable { Title = "Open maintenance requests", Description = "Requests still Open or In Progress." };
                            break;
                        case "resolved":
                            rows = baseSet.Where(m => m.ResolvedAt >= start && m.ResolvedAt < end && InMonth(m.ResolvedAt))
                                          .OrderByDescending(m => m.ResolvedAt).ToList();
                            Table = new RecordTable { Title = "Requests resolved", Description = "Requests closed in the period. Days = resolved date − opened date." };
                            break;
                        default:
                            rows = baseSet.Where(m => m.CreatedAt >= start && m.CreatedAt < end && InMonth(m.CreatedAt))
                                          .OrderByDescending(m => m.CreatedAt).ToList();
                            Table = new RecordTable { Title = "Requests opened", Description = "Requests created in the period by the return trigger (damage, inspection, replacement review)." };
                            break;
                    }
                    RequestColumns(Table);
                    foreach (var m in rows) AddRequest(Table, m);
                    string avg = rows.Count == 0 ? "" : scope == "resolved"
                        ? $"avg {rows.Average(m => (m.ResolvedAt!.Value - m.CreatedAt).TotalDays):0.0}" : "";
                    Table.Totals = new[] { "Total", $"{rows.Count} requests", "", "", "", "", "", "", avg, "", "" };
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
