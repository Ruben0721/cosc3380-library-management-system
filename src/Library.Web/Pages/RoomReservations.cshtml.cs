using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Pages;

[Authorize]
public class RoomReservationsModel : PageModel
{
    private readonly LibraryDbContext _db;

    public RoomReservationsModel(LibraryDbContext db) => _db = db;

    public class ReservationInput
    {
        [Required]
        [Display(Name = "Room")]
        public string RoomId { get; set; } = "";

        [Required]
        [Display(Name = "Starts")]
        public DateTime StartTime { get; set; } = DateTime.Now.AddHours(1);

        [Required]
        [Display(Name = "Ends")]
        public DateTime EndTime { get; set; } = DateTime.Now.AddHours(2);

        [Range(1, 100000)]
        [Display(Name = "Number of people")]
        public int NumberOfPeople { get; set; } = 5;

        [Required, StringLength(200)]
        public string Purpose { get; set; } = "";

        [Display(Name = "Reserve for")]
        public int? ReserveForLibraryUserId { get; set; }
    }

    public record RoomOption(string RoomId, int Capacity, string? Status);
    public record MemberOption(int LibraryUserId, string FullName);
    public record ReservationRow(string ReservationId, string RoomId, DateTime StartTime,
        DateTime EndTime, int NumberOfPeople, string Purpose, string Status, string Owner);

    [BindProperty]
    public ReservationInput Input { get; set; } = new();

    public List<RoomOption> Rooms { get; private set; } = new();
    public List<MemberOption> Members { get; private set; } = new();
    public List<ReservationRow> Reservations { get; private set; } = new();
    public bool IsStaff => User.IsInRole("Staff") || User.IsInRole("Admin");

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadPageAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        await LoadRoomsAsync();
        await LoadMembersAsync();

        if (!ModelState.IsValid)
        {
            await LoadReservationsAsync();
            return Page();
        }

        if (Input.EndTime <= Input.StartTime)
            ModelState.AddModelError("Input.EndTime", "The end time must be after the start time.");

        if (Input.StartTime < DateTime.Now.AddMinutes(-1))
            ModelState.AddModelError("Input.StartTime", "Reservations must start in the future.");

        var room = await _db.Rooms.AsNoTracking()
            .SingleOrDefaultAsync(r => r.RoomId == Input.RoomId && r.Active);

        if (room == null)
            ModelState.AddModelError("Input.RoomId", "Choose an active room.");
        else if (Input.NumberOfPeople > room.Capacity)
            ModelState.AddModelError("Input.NumberOfPeople", $"This room holds {room.Capacity} people.");

        var libraryUserId = await GetCurrentLibraryUserIdAsync();
        if (libraryUserId == null)
            return Forbid();

        var reservationOwnerId = libraryUserId.Value;
        if (IsStaff && Input.ReserveForLibraryUserId.HasValue)
        {
            var selectedMemberExists = await _db.LibraryUsers.AsNoTracking()
                .AnyAsync(u => u.LibraryUserId == Input.ReserveForLibraryUserId.Value
                            && u.IsActive);

            if (!selectedMemberExists)
                ModelState.AddModelError("Input.ReserveForLibraryUserId", "Choose an active library user.");
            else
                reservationOwnerId = Input.ReserveForLibraryUserId.Value;
        }

        if (!ModelState.IsValid)
        {
            await LoadReservationsAsync();
            return Page();
        }

        var reservation = new RoomReservation
        {
            ReservationId = await GenerateReservationIdAsync(),
            RoomId = Input.RoomId,
            StartTime = Input.StartTime,
            EndTime = Input.EndTime,
            NumberOfPeople = Input.NumberOfPeople,
            LibraryUserId = reservationOwnerId,
            Purpose = Input.Purpose.Trim(),
            Status = "Confirmed",
            CreatedAt = DateTime.Now
        };

        _db.RoomReservations.Add(reservation);

        try
        {
            await _db.SaveChangesAsync();
            TempData["Message"] = $"Room {reservation.RoomId} reserved. A setup task was created for staff.";
            return RedirectToPage();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty,
                "The reservation could not be created. The room may have just been reserved by someone else.");
            await LoadReservationsAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCancelAsync(string reservationId)
    {
        var libraryUserId = await GetCurrentLibraryUserIdAsync();
        if (libraryUserId == null)
            return Forbid();

        var reservation = await _db.RoomReservations
            .SingleOrDefaultAsync(r => r.ReservationId == reservationId);

        if (reservation == null)
            return NotFound();

        if (!IsStaff && reservation.LibraryUserId != libraryUserId.Value)
            return Forbid();

        if (reservation.Status != "Confirmed")
        {
            TempData["Error"] = "Only confirmed reservations can be cancelled.";
            return RedirectToPage();
        }

        reservation.Status = "Cancelled";
        reservation.CanceledAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["Message"] = $"Reservation {reservation.ReservationId} was cancelled.";
        return RedirectToPage();
    }

    private async Task LoadPageAsync()
    {
        await LoadRoomsAsync();
        await LoadMembersAsync();
        await LoadReservationsAsync();
    }

    private async Task LoadRoomsAsync()
    {
        Rooms = await _db.Rooms.AsNoTracking()
            .Where(r => r.Active)
            .OrderBy(r => r.Capacity)
            .ThenBy(r => r.RoomId)
            .Select(r => new RoomOption(r.RoomId, r.Capacity, r.Status))
            .ToListAsync();
    }

    private async Task LoadMembersAsync()
    {
        if (!IsStaff)
            return;

        Members = await _db.LibraryUsers.AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new MemberOption(u.LibraryUserId, u.FullName))
            .ToListAsync();
    }

    private async Task LoadReservationsAsync()
    {
        var libraryUserId = await GetCurrentLibraryUserIdAsync();
        var query = _db.RoomReservations.AsNoTracking();

        if (!IsStaff && libraryUserId.HasValue)
            query = query.Where(r => r.LibraryUserId == libraryUserId.Value);

        var rows = await query
            .Where(r => r.Status != "Cancelled" || r.StartTime >= DateTime.Today.AddDays(-1))
            .OrderBy(r => r.StartTime)
            .Join(_db.LibraryUsers.AsNoTracking(),
                r => r.LibraryUserId,
                u => u.LibraryUserId,
                (r, u) => new ReservationRow(
                    r.ReservationId, r.RoomId, r.StartTime, r.EndTime,
                    r.NumberOfPeople, r.Purpose, r.Status, u.FullName))
            .ToListAsync();

        Reservations = rows;
    }

    private async Task<int?> GetCurrentLibraryUserIdAsync()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            return null;

        var account = await _db.UserAccounts.AsNoTracking()
            .Where(a => a.AccountId == accountId && a.IsEnabled)
            .Select(a => new { a.LibraryUserId, a.StaffId })
            .SingleOrDefaultAsync();

        if (account == null)
            return null;

        if (account.StaffId.HasValue)
        {
            return await _db.StaffMembers.AsNoTracking()
                .Where(s => s.StaffId == account.StaffId.Value)
                .Select(s => s.LibraryUserId)
                .SingleOrDefaultAsync();
        }

        return account.LibraryUserId;
    }

    private async Task<string> GenerateReservationIdAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var id = $"R{Random.Shared.Next(0, 1_000_000):D6}";
            if (!await _db.RoomReservations.AnyAsync(r => r.ReservationId == id))
                return id;
        }

        throw new InvalidOperationException("Unable to generate a reservation identifier.");
    }
}
