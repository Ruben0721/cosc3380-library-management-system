using Microsoft.EntityFrameworkCore;
using Project.Data;

namespace Project.Services;

public sealed class NoShowProcessor
{
    private const int RoomGraceMinutes = 30;
    private readonly LibraryDbContext _db;
    private readonly ILogger<NoShowProcessor> _logger;

    public NoShowProcessor(LibraryDbContext db, ILogger<NoShowProcessor> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ProcessAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await CompleteCheckedInReservationsAsync(now, cancellationToken);
        await MarkRoomReservationsNoShowAsync(now, cancellationToken);
        await CreateRoomReservationRemindersAsync(now, cancellationToken);
        await MarkReadyHoldsNoShowAsync(now, cancellationToken);
    }

    private async Task CompleteCheckedInReservationsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var reservations = await _db.RoomReservations
            .Where(reservation => reservation.Status == "Checked In"
                               && reservation.EndTime <= now)
            .ToListAsync(cancellationToken);

        if (reservations.Count == 0)
            return;

        foreach (var reservation in reservations)
            reservation.Status = "Completed";

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkRoomReservationsNoShowAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddMinutes(-RoomGraceMinutes);
        var reservations = await _db.RoomReservations
            .Where(reservation => reservation.Status == "Confirmed"
                               && reservation.StartTime <= cutoff)
            .ToListAsync(cancellationToken);

        foreach (var reservation in reservations)
        {
            reservation.Status = "No Show";
            var recentNoShowCount = await _db.RoomReservations
                .CountAsync(existing => existing.LibraryUserId == reservation.LibraryUserId
                                     && existing.Status == "No Show"
                                     && existing.StartTime >= now.AddDays(-90),
                    cancellationToken);

            var user = await _db.LibraryUsers
                .SingleAsync(existing => existing.LibraryUserId == reservation.LibraryUserId,
                    cancellationToken);

            if (recentNoShowCount >= 3)
                user.RoomReservationRestricted = true;

            var restrictionMessage = recentNoShowCount >= 3
                ? " You have reached three room no-shows in the last 90 days, so future room reservations require staff assistance."
                : $" This is room no-show {recentNoShowCount} of 3 within the last 90 days.";

            await _db.RoomSetupTasks
                .Where(task => task.ReservationId == reservation.ReservationId
                            && (task.Status == "Open" || task.Status == "In Progress"))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(task => task.Status, "Cancelled"), cancellationToken);

            await AddNotificationIfMissingAsync(
                reservation.LibraryUserId,
                "RoomReservationNoShow",
                reservation.ReservationId,
                "Room reservation marked no-show",
                $"Your reservation for room {reservation.RoomId} on {reservation.StartTime:MMM d, yyyy h:mm tt} was marked no-show because it was not checked in within 30 minutes of the start time.{restrictionMessage}",
                now,
                cancellationToken);
        }

        if (reservations.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task CreateRoomReservationRemindersAsync(DateTime now, CancellationToken cancellationToken)
    {
        var reminderWindowEnd = now.AddMinutes(30);
        var reservations = await _db.RoomReservations
            .Where(reservation => reservation.Status == "Confirmed"
                               && reservation.StartTime > now
                               && reservation.StartTime <= reminderWindowEnd)
            .ToListAsync(cancellationToken);

        foreach (var reservation in reservations)
        {
            await AddNotificationIfMissingAsync(
                reservation.LibraryUserId,
                "RoomReservationReminder",
                reservation.ReservationId,
                "Room check-in reminder",
                $"Your reservation for room {reservation.RoomId} starts at {reservation.StartTime:MMM d, yyyy h:mm tt}. Check in from 30 minutes before the start time through 30 minutes after it begins.",
                now,
                cancellationToken);
        }

        if (reservations.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkReadyHoldsNoShowAsync(DateTime now, CancellationToken cancellationToken)
    {
        var holds = await _db.Holds
            .Where(hold => hold.Status == "Ready"
                        && hold.PickupDeadline != null
                        && hold.PickupDeadline <= now)
            .ToListAsync(cancellationToken);

        foreach (var hold in holds)
        {
            var itemId = hold.ItemId;
            hold.Status = "No Show";

            var itemName = itemId == null
                ? null
                : await _db.Inventory.AsNoTracking()
                    .Where(item => item.Id == itemId)
                    .Select(item => item.Name)
                    .SingleOrDefaultAsync(cancellationToken);

            await AddNotificationIfMissingAsync(
                hold.LibraryUserId,
                "HoldNoShow",
                hold.HoldId,
                "Hold marked no-show",
                $"Your hold{(itemName == null ? "" : $" for {itemName}")} was marked no-show because it was not picked up by {hold.PickupDeadline:MMM d, yyyy h:mm tt}.",
                now,
                cancellationToken);

            if (itemId.HasValue)
                await _db.SaveChangesAsync(cancellationToken);

            if (itemId.HasValue)
            {
                var next = await Pages.Circulation.PromoteNextHoldAsync(_db, itemId.Value);
                if (next != null)
                    _logger.LogInformation("Promoted the next hold after hold {HoldId} became a no-show.", hold.HoldId);
            }
        }

        if (holds.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task AddNotificationIfMissingAsync(
        int libraryUserId,
        string notificationType,
        string relatedId,
        string title,
        string message,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        var exists = await _db.Notifications.AnyAsync(notification =>
            notification.LibraryUserId == libraryUserId
            && notification.NotificationType == notificationType
            && notification.RelatedId == relatedId, cancellationToken);

        if (!exists)
        {
            _db.Notifications.Add(new Models.Notification
            {
                LibraryUserId = libraryUserId,
                NotificationType = notificationType,
                RelatedId = relatedId,
                Title = title,
                Message = message,
                CreatedAt = createdAt
            });
        }
    }
}
