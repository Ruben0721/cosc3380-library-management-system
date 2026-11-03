using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models
{

    [Table("Hold")]
    public class Hold
    {
        [Key, Column("Hold_ID")]
        [StringLength(6)]
        public string HoldId { get; set; } = "";

        public int LibraryUserId { get; set; }

        [Column("Request_Datetime")]
        public DateTime RequestDatetime { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Pending";

        [Column("Ready_Date")]
        public DateTime? ReadyDate { get; set; }

        [Column("Pickup_Deadline")]
        public DateTime? PickupDeadline { get; set; }

        // The requested item (added by hold_setup.sql)
        public int? ItemId { get; set; }
    }
}