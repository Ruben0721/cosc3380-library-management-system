using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models;

[Table("Notification")]
public class Notification
{
    [Key]
    public long NotificationId { get; set; }

    public int LibraryUserId { get; set; }

    [Required, StringLength(50)]
    public string NotificationType { get; set; } = "";

    [Required, StringLength(20)]
    public string RelatedId { get; set; } = "";

    [Required, StringLength(200)]
    public string Title { get; set; } = "";

    [Required, StringLength(500)]
    public string Message { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
