using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Pages.Shared
{
    [Table("Student")]
    public class Student
    {
        [Key]
        [Column("Student_ID")]
        [StringLength(7)]
        public string StudentId { get; set; } = "";

        [Required]
        [Column("First_Name")]
        [StringLength(30)]
        public string FirstName { get; set; } = "";

        [Required]
        [Column("Last_Name")]
        [StringLength(30)]
        public string LastName { get; set; } = "";

        [Column("Registration_Date")]
        public DateTime RegistrationDate { get; set; }

        [StringLength(255)]
        public string? Email { get; set; }

        public int LibraryUserId { get; set; }
        public string? AcademicLevel { get; set; }
    public string? EnrollStatus { get; set; }
    }
}