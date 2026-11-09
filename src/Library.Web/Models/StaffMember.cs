using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models
{
    [Table("Staff")]
    public class StaffMember
    {
        [Key]
        [Column("Staff_Id")]
        public int StaffId { get; set; }

        public int? LibraryUserId { get; set; }

        [ForeignKey(nameof(LibraryUserId))]
        public LibraryUser? Borrower { get; set; }

        [Required]
        [StringLength(30)]
        public string FirstName { get; set; } = "";

        [Required]
        [StringLength(30)]
        public string LastName { get; set; } = "";

        [Required]
        [StringLength(30)]
        public string Role { get; set; } = "";

        [Column(TypeName = "date")]
        public DateTime StartDate { get; set; }

        public bool IsActive { get; set; } = true;

        [StringLength(260)]
        public string? Email { get; set; }
    }
}
