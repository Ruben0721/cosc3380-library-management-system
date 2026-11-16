using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Services;

namespace Project.Pages;

public sealed class IndexModel : PageModel
{
    private readonly LibraryDbContext _db;
    private readonly NoShowProcessor _noShowProcessor;

    public IndexModel(
        LibraryDbContext db,
        NoShowProcessor noShowProcessor)
    {
        _db = db;
        _noShowProcessor = noShowProcessor;
    }

    public List<NoticeRow> Notifications { get; private set; } = new();

    public record NoticeRow(
        long NotificationId,
        string NotificationType,
        string Title,
        string Message,
        DateTime CreatedAt)
    {
        public string NextStep => NotificationType switch
        {
            "RoomReservationReminder" => "Go to Room Reservations and check in during the check-in window.",
            "RoomReservationNoShow" => "Contact library staff if you have reached the room no-show limit or believe this was a mistake.",
            "HoldNoShow" => "Place another hold from the catalog or contact the circulation desk.",
            _ => "Review your account for more information."
        };

        public string? ActionPage => NotificationType switch
        {
            "RoomReservationReminder" or "RoomReservationNoShow" => "/RoomReservations",
            "HoldNoShow" => "/Search",
            _ => null
        };

        public string ActionLabel => NotificationType == "HoldNoShow"
            ? "Browse the catalog"
            : "View room reservations";
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
            return;

        await _noShowProcessor.ProcessAsync(DateTime.Now, cancellationToken);

        var libraryUserId = await GetLibraryUserIdAsync(cancellationToken);
        if (!libraryUserId.HasValue)
            return;

        Notifications = await _db.Notifications
            .AsNoTracking()
            .Where(notification => notification.LibraryUserId == libraryUserId.Value
                                && notification.ReadAt == null
                                && (notification.NotificationType == "RoomReservationReminder"
                                    || notification.NotificationType.EndsWith("NoShow")))
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(5)
            .Select(notification => new NoticeRow(
                notification.NotificationId,
                notification.NotificationType,
                notification.Title,
                notification.Message,
                notification.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<IActionResult> OnPostAcknowledgeAsync(long notificationId) =>
        MarkNotificationReadAsync(notificationId);

    public Task<IActionResult> OnPostDismissAsync(long notificationId) =>
        MarkNotificationReadAsync(notificationId);

    private async Task<IActionResult> MarkNotificationReadAsync(long notificationId)
    {
        var libraryUserId = await GetLibraryUserIdAsync(HttpContext.RequestAborted);
        if (!libraryUserId.HasValue)
            return Forbid();

        var notification = await _db.Notifications
            .SingleOrDefaultAsync(item => item.NotificationId == notificationId
                                       && item.LibraryUserId == libraryUserId.Value);

        if (notification != null && notification.ReadAt == null)
        {
            notification.ReadAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    private async Task<int?> GetLibraryUserIdAsync(CancellationToken cancellationToken)
    {
        if (int.TryParse(User.FindFirstValue("library_user_id"), out var libraryUserId))
            return libraryUserId;

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            return null;

        return await _db.UserAccounts
            .AsNoTracking()
            .Where(account => account.AccountId == accountId && account.IsEnabled)
            .Select(account => (int?)account.LibraryUserId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
