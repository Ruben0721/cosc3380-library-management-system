using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;

namespace Project.Pages;

public class RoomsModel : PageModel
{
    private readonly LibraryDbContext _db;

    public RoomsModel(LibraryDbContext db) => _db = db;

    public List<RoomAvailability> Rooms { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var now = DateTime.Now;

        var rooms = await _db.Rooms
            .AsNoTracking()
            .Where(room => room.Active)
            .OrderBy(room => room.Capacity)
            .ThenBy(room => room.RoomId)
            .Select(room => new RoomAvailability(room.RoomId, room.Capacity, room.Status))
            .ToListAsync();

        var reservations = await _db.RoomReservations
            .AsNoTracking()
            .Where(reservation => reservation.Status == "Confirmed"
                               && reservation.EndTime >= now)
            .OrderBy(reservation => reservation.StartTime)
            .Select(reservation => new RoomReservationPeriod(
                reservation.RoomId,
                reservation.StartTime,
                reservation.EndTime))
            .ToListAsync();

        var reservationsByRoom = reservations
            .GroupBy(reservation => reservation.RoomId)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var room in rooms)
        {
            reservationsByRoom.TryGetValue(room.RoomId, out var unavailablePeriods);
            room.UnavailablePeriods = unavailablePeriods ?? new List<RoomReservationPeriod>();
        }

        Rooms = rooms;
    }

    public sealed class RoomAvailability
    {
        public RoomAvailability(string roomId, int capacity, string? status)
        {
            RoomId = roomId;
            Capacity = capacity;
            Status = status;
        }

        public string RoomId { get; }
        public int Capacity { get; }
        public string? Status { get; }
        public List<RoomReservationPeriod> UnavailablePeriods { get; set; } = new();
    }

    public record RoomReservationPeriod(string RoomId, DateTime StartTime, DateTime EndTime);
}
