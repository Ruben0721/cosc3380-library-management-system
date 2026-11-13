using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Project.Configuration;
using Project.Data;
using Project.Models;

namespace Project.Pages
{
   
    public class BorrowModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public BorrowModel(LibraryDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public int ItemId { get; set; }

        [TempData] public string? Message { get; set; }
        [TempData] public string? Error { get; set; }

        // The title
        public string? ItemName { get; set; }
        public string? ItemType { get; set; }
        public string CategoryLabel { get; set; } = "";
        public int Copies { get; set; }
        public int CopiesAvailable { get; set; }
        public int CopiesSetAside { get; set; }        
        public DateTime? NextDue { get; set; }          
        public int InLine { get; set; }                 
        public bool NeedsPickup { get; set; }
        public int LoanDays { get; set; }

        // The signed-in member
        public int? MemberId { get; set; }
        public string? Blocked { get; set; }           
        public DateTime? MyLoanDue { get; set; }        
        public DateTime? MyPickupBy { get; set; }      
        public int? MyPlaceInLine { get; set; }        
        public bool InReadingList { get; set; }
        public bool BlockedByFines { get; set; }
        public bool AtHoldLimit { get; set; }
        public int HoldLimit { get; set; }

        private int? _availableCopyId;
        private List<int> _titleCopyIds = new();

        public bool CanRequest => Blocked == null && MyLoanDue == null && MyPickupBy == null && MyPlaceInLine == null && _availableCopyId != null && !AtHoldLimit;
        public bool CanHold => Blocked == null && MyLoanDue == null && MyPickupBy == null && MyPlaceInLine == null && _availableCopyId == null && !AtHoldLimit;

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await LoadAsync()) return NotFound();
            return Page();
        }

        
        public async Task<IActionResult> OnPostRequestAsync()
        {
            if (!await LoadAsync()) return NotFound();
            if (!CanRequest) { Error = Blocked ?? "This title can't be requested right now."; return Back(); }

            int pending = await _db.Holds.CountAsync(h => h.LibraryUserId == MemberId && h.Status == "Ready");
            if (pending >= Circulation.MaxPickupReservations)
            {
                Error = $"You already have {pending} items waiting for pickup. Pick those up first.";
                return Back();
            }

            var deadline = DateTime.Today.AddDays(Circulation.PickupDays).AddHours(23).AddMinutes(59);
            var refused = await AddHoldAsync(_availableCopyId!.Value, "Ready", deadline);
            if (refused != null) { Error = refused; return Back(); }
            Message = $"Request placed. \"{ItemName}\" will be held for you at the circulation desk until {deadline:dddd, MMMM d}. Your {LoanDays}-day loan starts when you pick it up.";
            return Back();
        }

       
        public async Task<IActionResult> OnPostHoldAsync()
        {
            if (!await LoadAsync()) return NotFound();
            if (!CanHold) { Error = Blocked ?? (AtHoldLimit ? $"You already have {HoldLimit} holds, your limit." : "A hold can't be placed right now."); return Back(); }

            var refused = await AddHoldAsync(ItemId, "Pending", null);
            if (refused != null) { Error = refused; return Back(); }
            Message = $"Hold placed. You're #{InLine + 1} in line for \"{ItemName}\". When a copy comes back, it's set aside for you and you'll see it under Ready for pickup in your account.";
            return Back();
        }

        
        public async Task<IActionResult> OnPostCancelAsync()
        {
            if (!await LoadAsync()) return NotFound();
            var mine = (await _db.Holds.Where(h => h.LibraryUserId == MemberId && h.ItemId != null &&
                                                   (h.Status == "Ready" || h.Status == "Pending")).ToListAsync())
                .Where(h => _titleCopyIds.Contains(h.ItemId!.Value))
                .ToList();
            foreach (var h in mine) h.Status = "Cancelled";
            await _db.SaveChangesAsync();

            // A copy that was set aside for this member goes to the next person waiting
            foreach (var h in mine.Where(h => h.ReadyDate != null))
                await Circulation.PromoteNextHoldAsync(_db, h.ItemId!.Value);

            Message = mine.Count == 0 ? "Nothing to cancel." : "Cancelled.";
            return Back();
        }

        public async Task<IActionResult> OnPostSaveAsync()
        {
            if (!await LoadAsync()) return NotFound();
            if (MemberId == null) { Error = "Sign in to save titles to your reading list."; return Back(); }
            if (!InReadingList)
            {
                _db.ReadingList.Add(new ReadingListItem { LibraryUserId = MemberId.Value, ItemId = ItemId, AddedAt = DateTime.Now });
                await _db.SaveChangesAsync();
            }
            Message = $"\"{ItemName}\" is on your reading list.";
            return Back();
        }

        public async Task<IActionResult> OnPostUnsaveAsync()
        {
            if (!await LoadAsync()) return NotFound();
            var rows = (await _db.ReadingList.Where(r => r.LibraryUserId == MemberId).ToListAsync())
                .Where(r => _titleCopyIds.Contains(r.ItemId)).ToList();
            foreach (var r in rows) _db.ReadingList.Remove(r);
            await _db.SaveChangesAsync();
            Message = $"Removed \"{ItemName}\" from your reading list.";
            return Back();
        }

       
        public async Task<IActionResult> OnPostBorrowAsync()
        {
            if (!await LoadAsync()) return NotFound();
            if (NeedsPickup || !CanRequest) { Error = Blocked ?? "Place a checkout request instead."; return Back(); }
            var refused = await Circulation.CreateLoanAsync(_db, _availableCopyId!.Value, MemberId!.Value, ItemType);
            Message = refused == null ? $"You borrowed \"{ItemName}\"." : null;
            Error = refused == null ? null : $"Not borrowed: {refused}";
            return Back();
        }

        private IActionResult Back() => RedirectToPage(new { ItemId });

        // Returns null on success, or the reason a database rule (trigger) refused it.
        private async Task<string?> AddHoldAsync(int copyId, string status, DateTime? deadline)
        {
            var hold = new Hold
            {
                HoldId = await Circulation.NextHoldIdAsync(_db),
                LibraryUserId = MemberId!.Value,
                ItemId = copyId,
                RequestDatetime = DateTime.Now,
                Status = status,
                ReadyDate = status == "Ready" ? DateTime.Now : null,
                PickupDeadline = deadline
            };
            _db.Holds.Add(hold);
            try
            {
                await _db.SaveChangesAsync();
                return null;
            }
            catch (DbUpdateException ex) when (ex.InnerException is MySqlException { SqlState: "45000" } rule)
            {
               
                _db.Entry(hold).State = EntityState.Detached;
                return rule.Message.Contains("hold limit", StringComparison.OrdinalIgnoreCase)
                    ? $"You've reached your limit of active requests and holds ({HoldLimit}). Pick up or cancel one from your account first."
                    : rule.Message;
            }
        }

       
        private async Task<bool> LoadAsync()
        {
            await Circulation.ExpirePickupsAsync(_db);

            var item = await _db.Inventory.AsNoTracking()
                .Where(i => i.Id == ItemId)
                .Select(i => new { i.Name, i.Type })
                .FirstOrDefaultAsync();
            if (item == null) return false;

            ItemName = item.Name;
            ItemType = item.Type;
            CategoryLabel = LibraryOptions.Types.Where(t => t.Value == item.Type).Select(t => t.Label).FirstOrDefault() ?? item.Type ?? "";
            NeedsPickup = Circulation.NeedsPickup(item.Type);
            LoanDays = await Circulation.LoanDaysAsync(_db, item.Type);

            var copies = await _db.Inventory.AsNoTracking()
                .Where(i => i.Name == item.Name && i.Type == item.Type)
                .Select(i => new { i.Id, i.Status })
                .ToListAsync();
            _titleCopyIds = copies.Select(c => c.Id).ToList();
            Copies = copies.Count;

            var titleHolds = (await _db.Holds.AsNoTracking()
                    .Where(h => h.ItemId != null && (h.Status == "Ready" || h.Status == "Pending"))
                    .ToListAsync())
                .Where(h => _titleCopyIds.Contains(h.ItemId!.Value))
                .ToList();
            var setAside = titleHolds.Where(h => h.Status == "Ready").Select(h => h.ItemId!.Value).ToHashSet();
            var free = copies.Where(c => c.Status == "Available" && !setAside.Contains(c.Id)).Select(c => c.Id).ToList();
            CopiesAvailable = free.Count;
            CopiesSetAside = copies.Count(c => c.Status == "Available" && setAside.Contains(c.Id));
            _availableCopyId = free.Contains(ItemId) ? ItemId : free.Cast<int?>().FirstOrDefault();

            var pendingInOrder = titleHolds.Where(h => h.Status == "Pending").OrderBy(h => h.RequestDatetime).ToList();
            InLine = pendingInOrder.Count;

            var openLoans = await _db.Rentals.AsNoTracking()
                .Where(r => !r.IsReturned && _titleCopyIds.Contains(r.ItemId))
                .Select(r => new { r.LibraryUserId, r.EndDate })
                .ToListAsync();
            NextDue = openLoans.Where(r => r.EndDate != null).Select(r => r.EndDate).Min();

            // Signed-in borrower (students and faculty carry a library_user_id claim)
            MemberId = int.TryParse(User.FindFirst("library_user_id")?.Value, out var id) ? id : null;
            if (MemberId == null)
            {
                Blocked = "Sign in with a student or faculty account to request or hold items.";
                return true;
            }

            InReadingList = await _db.ReadingList.AnyAsync(r => r.LibraryUserId == MemberId && _titleCopyIds.Contains(r.ItemId));

            var myLoan = openLoans.FirstOrDefault(r => r.LibraryUserId == MemberId);
            if (myLoan != null) MyLoanDue = myLoan.EndDate;

            var myReady = titleHolds.FirstOrDefault(h => h.LibraryUserId == MemberId && h.Status == "Ready");
            if (myReady != null) MyPickupBy = myReady.PickupDeadline;

            int place = pendingInOrder.FindIndex(h => h.LibraryUserId == MemberId);
            if (place >= 0) MyPlaceInLine = place + 1;

            var member = await _db.LibraryUsers.AsNoTracking().FirstOrDefaultAsync(u => u.LibraryUserId == MemberId);
            if (member == null || !member.IsActive)
            {
                Blocked = "Your library membership is inactive. Please contact the circulation desk.";
                return true;
            }

            decimal owed = await Circulation.AmountOwedAsync(_db, MemberId.Value);
            if (owed > Circulation.FineBlockLimit)
            {
                Blocked = $"You owe {owed:C} in fines and late fees (over the {Circulation.FineBlockLimit:C} limit). Pay your fines and return overdue items before requesting or holding items.";
                BlockedByFines = true;
                return true;
            }

            HoldLimit = await Circulation.WaitlistLimitAsync(_db, MemberId.Value);
            // Requests waiting for pickup (Ready) and holds in line (Pending) both count, same as the database rule
            int myActive = await _db.Holds.CountAsync(h => h.LibraryUserId == MemberId && (h.Status == "Pending" || h.Status == "Ready"));
            AtHoldLimit = myActive >= HoldLimit;
            return true;
        }
    }
}
