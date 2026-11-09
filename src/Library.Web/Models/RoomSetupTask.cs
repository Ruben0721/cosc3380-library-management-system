using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models;

[Table("Room_setup_task")]
public class RoomSetupTask
{
    [Key]
    [Column("task_id")]
    public int TaskId { get; set; }

    [Column("reservation_id")]
    [StringLength(7)]
    public string ReservationId { get; set; } = "";

    [Column("room_id")]
    [StringLength(5)]
    public string RoomId { get; set; } = "";

    [Column("task_description")]
    [StringLength(255)]
    public string TaskDescription { get; set; } = "";

    [StringLength(20)]
    public string Status { get; set; } = "Open";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    public Room? Room { get; set; }
    public RoomReservation? Reservation { get; set; }
}
