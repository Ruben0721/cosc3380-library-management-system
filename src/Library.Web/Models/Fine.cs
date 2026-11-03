using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models
{
    
    [Table("Fine")]
    public class Fine
    {
        [Key]
        public int FineId { get; set; }
        public int RentalId { get; set; }
        public int LibraryUserId { get; set; }

        [StringLength(20)]
        public string FineType { get; set; } = "";     // Overdue | Damaged | Lost

        public decimal Amount { get; set; }
        public int? DaysLate { get; set; }
        public bool IsPaid { get; set; }
        public DateTime DateAssessed { get; set; }
        public int? AssessedByStaffId { get; set; }

        [StringLength(200)]
        public string? Notes { get; set; }
    }
}