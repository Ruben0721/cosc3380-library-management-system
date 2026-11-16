using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models;

[Table("Student")]
public class Student
{
    [Key, Column("Student_ID"), StringLength(7)] public string StudentId { get; set; } = "";
    public int LibraryUserId { get; set; }
    public LibraryUser Borrower { get; set; } = null!;
    [Column("Registration_Date")] public DateTime RegistrationDate { get; set; }
    [Column(TypeName = "date")] public DateTime? DateOfBirth { get; set; }
    public int? RentalLimit { get; set; } = 5;
    public int? WaitlistLimit { get; set; } = 3;
    public string? AcademicLevel { get; set; }
    public string? EnrollStatus { get; set; }
}
