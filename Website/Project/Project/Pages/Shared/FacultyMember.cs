using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Pages.Shared
{
    [Table("Faculty")]
    public class FacultyMember
    {
        [Key]
        [Column("faculty_id")]
        [StringLength(7)]
        public string FacultyId { get; set; } = "";

        [Required]
        [Column("first_name")]
        [StringLength(30)]
        public string FirstName { get; set; } = "";

        [Required]
        [Column("last_name")]
        [StringLength(30)]
        public string LastName { get; set; } = "";

        [Column("position")]
        [StringLength(50)]
        public string? Position { get; set; }

        [Column("rental_limit")]
        [Range(0, int.MaxValue)]
        public int? RentalLimit { get; set; }

        [Column("waitlist_limit")]
        [Range(0, int.MaxValue)]
        public int? WaitlistLimit { get; set; }

        public int LibraryUserId { get; set; }
    }
}