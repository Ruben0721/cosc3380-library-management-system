using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models
{
    
    [Table("MaintenanceRequest")]
    public class MaintenanceRequest
    {
        [Key]
        public int RequestId { get; set; }

        public int ItemId { get; set; }

        public int? RentalId { get; set; }

        [StringLength(30)]
        public string RequestType { get; set; } = "";

        [StringLength(200)]
        public string? Reason { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Open";

        public DateTime CreatedAt { get; set; }

        public DateTime? ResolvedAt { get; set; }

        [StringLength(200)]
        public string? Notes { get; set; }
    }
}