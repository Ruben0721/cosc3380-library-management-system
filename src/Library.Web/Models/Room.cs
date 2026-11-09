using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models;

[Table("Room")]
public class Room
{
    [Key]
    [Column("room_id")]
    [StringLength(5)]
    public string RoomId { get; set; } = "";

    public int Capacity { get; set; }

    [StringLength(10)]
    public string? Status { get; set; }

    public bool Active { get; set; }
}
