using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models;

[Table("Staff")]
public class StaffMember
{
    [Key, Column("Staff_Id")] public int StaffId { get; set; }
    public int LibraryUserId { get; set; }
    public LibraryUser Borrower { get; set; } = null!;
    [Required, StringLength(30)] public string JobTitle { get; set; } = "";
    [Column(TypeName = "date")] public DateTime StartDate { get; set; }
    [Range(0, int.MaxValue)] public int RentalLimit { get; set; } = 20;
    [Range(0, int.MaxValue)] public int WaitlistLimit { get; set; } = 20;
    public bool IsActive { get; set; } = true;
}
