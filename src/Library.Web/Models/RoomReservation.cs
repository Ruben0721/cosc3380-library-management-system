using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models;

[Table("Room_reservation")]
public class RoomReservation
{
    [Key]
    [Column("reservation_id")]
    [StringLength(7)]
    public string ReservationId { get; set; } = "";

    [Column("room_id")]
    [StringLength(5)]
    public string RoomId { get; set; } = "";

    [Column("start_time")]
    public DateTime StartTime { get; set; }

    [Column("end_time")]
    public DateTime EndTime { get; set; }

    [Column("num_of_people")]
    [Range(5, int.MaxValue)]
    public int NumberOfPeople { get; set; }

    public int LibraryUserId { get; set; }

    [StringLength(200)]
    public string Purpose { get; set; } = "";

    [StringLength(20)]
    public string Status { get; set; } = "Confirmed";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("canceled_at")]
    public DateTime? CanceledAt { get; set; }

    public Room? Room { get; set; }
    public LibraryUser? LibraryUser { get; set; }
}
