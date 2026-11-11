using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Configuration;
using Project.Data;
using Project.Models;

namespace Project.Pages
{
    // Member self-service for one title:
    //   available  -> place a checkout request (staff set it aside, member picks it up)
    //   all out    -> place a hold and join the line (a database trigger sets the next returned copy aside)
    //   any time   -> save it to the reading list
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
        public DateTime? NextDue { get; set; }          // earliest due date among copies on loan
        public int InLine { get; set; }                 // members with a pending hold on this title
        public bool NeedsPickup { get; set; }
        public int LoanDays { get; set; }

        // The signed-in member
        public int? MemberId { get; set; }
        public string? Blocked { get; set; }            // can't request or hold at all (sign-in, inactive, fines)
        public DateTime? MyLoanDue { get; set; }        // has a copy checked out
        public DateTime? MyPickupBy { get; set; }       // a copy is set aside for them
        public int? MyPlaceInLine { get; set; }         // waiting in the hold queue
        public bool InReadingList { get; set; }
        public bool BlockedByFines { get; set; }
        public bool AtHoldLimit { get; set; }
        public int HoldLimit { get; set; }

        private int? _availableCopyId;
        private List<int> _titleCopyIds = new();

        public bool CanRequest => Blocked == null && MyLoanDue == null && MyPickupBy == null && MyPlaceInLine == null && _availableCopyId != null;
        public bool CanHold => Blocked == null && MyLoanDue == null && MyPickupBy == null && MyPlaceInLine == null && _availableCopyId == null && !AtHoldLimit;

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await LoadAsync()) return NotFound();
            return Page();
        }

        // Available: ask staff to set a copy aside for pickup
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
            await AddHoldAsync(_availableCopyId!.Value, "Ready", deadline);
            Message = $"Request placed. \"{ItemName}\" will be held for you at the circulation desk until {deadline:dddd, MMMM d}. Your {LoanDays}-day loan starts when you pick it up.";
            return Back();
        }

        // All copies out: join the line
        public async Task<IActionResult> OnPostHoldAsync()
        {
            if (!await LoadAsync()) return NotFound();
            if (!CanHold) { Error = Blocked ?? (AtHoldLimit ? $"You already have {HoldLimit} holds, your limit." : "A hold can't be placed right now."); return Back(); }

            await AddHoldAsync(ItemId, "Pending", null);
            Message = $"Hold placed. You're #{InLine + 1} in line for \"{ItemName}\". When a copy comes back, it's set aside for you and you'll see it under Ready for pickup in your account.";
            return Back();
        }

        // Cancel the member's request or hold on this title
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

        // Self-checkout, only for types listed in Circulation.SelfCheckoutTypes (none by default)
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

        private async Task AddHoldAsync(int copyId, string status, DateTime? deadline)
        {
            _db.Holds.Add(new Hold
            {
                HoldId = await Circulation.NextHoldIdAsync(_db),
                LibraryUserId = MemberId!.Value,
                ItemId = copyId,
                RequestDatetime = DateTime.Now,
                Status = status,
                ReadyDate = status == "Ready" ? DateTime.Now : null,
                PickupDeadline = deadline
            });
            await _db.SaveChangesAsync();
        }

        // Loads the title (all copies with the same name and category) and the member's situation.
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

            decimal unpaid = await Circulation.UnpaidFinesAsync(_db, MemberId.Value);
            if (unpaid > Circulation.FineBlockLimit)
            {
                Blocked = $"You have {unpaid:C} in unpaid fines. Pay them online or at the desk before requesting or holding items.";
                BlockedByFines = true;
                return true;
            }

            HoldLimit = await Circulation.WaitlistLimitAsync(_db, MemberId.Value);
            int myPending = await _db.Holds.CountAsync(h => h.LibraryUserId == MemberId && h.Status == "Pending");
            AtHoldLimit = myPending >= HoldLimit;
            return true;
        }
    }
}
