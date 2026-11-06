using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;

namespace Project.Pages.Staff;

[Authorize(Policy = "LibraryStaff")]
public class RoomSetupTasksModel : PageModel
{
    private readonly LibraryDbContext _db;

    public RoomSetupTasksModel(LibraryDbContext db) => _db = db;

    public record TaskRow(int TaskId, string RoomId, string ReservationId,
        DateTime StartTime, DateTime EndTime, int NumberOfPeople,
        string Description, string Status, DateTime CreatedAt);

    public List<TaskRow> Tasks { get; private set; } = new();

    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        Tasks = await (from task in _db.RoomSetupTasks.AsNoTracking()
                       join reservation in _db.RoomReservations.AsNoTracking()
                           on task.ReservationId equals reservation.ReservationId
                       where task.Status == "Open"
                       orderby reservation.StartTime, task.CreatedAt
                       select new TaskRow(
                           task.TaskId,
                           task.RoomId,
                           task.ReservationId,
                           reservation.StartTime,
                           reservation.EndTime,
                           reservation.NumberOfPeople,
                           task.TaskDescription,
                           task.Status,
                           task.CreatedAt))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostCompleteAsync(int taskId)
    {
        var task = await _db.RoomSetupTasks
            .SingleOrDefaultAsync(t => t.TaskId == taskId && t.Status == "Open");

        if (task == null)
        {
            Message = "That setup task was not found or is already closed.";
            return RedirectToPage();
        }

        task.Status = "Completed";
        task.CompletedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        Message = $"Setup task #{task.TaskId} marked completed.";
        return RedirectToPage();
    }
}
