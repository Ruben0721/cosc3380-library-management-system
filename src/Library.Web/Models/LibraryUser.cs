using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models;

public class LibraryUser
{
    public int LibraryUserId { get; set; }
    [Required, StringLength(30)] public string FirstName { get; set; } = "";
    [Required, StringLength(30)] public string LastName { get; set; } = "";
    [StringLength(1)] public string? MiddleInitial { get; set; }
    // MySQL generates the display name from the authoritative first and last names.
    [DatabaseGenerated(DatabaseGeneratedOption.Computed), StringLength(200)]
    public string FullName { get; private set; } = "";
    [StringLength(30)] public string? PhoneNumber { get; set; }
    [StringLength(150)] public string? AddressLine1 { get; set; }
    [StringLength(100)] public string? AddressLine2 { get; set; }
    [StringLength(50)] public string? City { get; set; }
    [StringLength(30)] public string? State { get; set; }
    [StringLength(20)] public string? PostalCode { get; set; }
    public bool IsActive { get; set; }
    [StringLength(30)] public string? UserStatus { get; set; }
    public bool RoomReservationRestricted { get; set; }
}
