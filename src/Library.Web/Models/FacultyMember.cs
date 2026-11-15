using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models;

[Table("Faculty")]
public class FacultyMember
{
    [Key, Column("faculty_id"), StringLength(7)] public string FacultyId { get; set; } = "";
    public int LibraryUserId { get; set; }
    public LibraryUser Borrower { get; set; } = null!;
    [Column("position"), StringLength(50)] public string? Position { get; set; }
    [Column("rental_limit"), Range(0, int.MaxValue)] public int? RentalLimit { get; set; }
    [Column("waitlist_limit"), Range(0, int.MaxValue)] public int? WaitlistLimit { get; set; }
}
