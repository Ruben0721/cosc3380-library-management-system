using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models
{
    
    [Table("FinePolicy")]
    public class FinePolicy
    {
        [Key, StringLength(50)]
        public string ItemType { get; set; } = "";

        public int LoanDays { get; set; }
        public decimal DailyOverdueRate { get; set; }
        public decimal OverdueCap { get; set; }
        public decimal DamageFee { get; set; }
        public decimal ReplacementCost { get; set; }
        public int? InspectionEvery { get; set; }
    }
}