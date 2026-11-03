using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models
{
    
    [Table("Payment")]
    public class Payment
    {
        [Key, Column("payment_id"), StringLength(7)]
        public string PaymentId { get; set; } = "";

        public int LibraryUserId { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("payment_date")]
        public DateTime PaymentDate { get; set; }

        [Column("payment_method"), StringLength(20)]
        public string PaymentMethod { get; set; } = "";   // cash | card | check

        public int? FineId { get; set; }
    }
}