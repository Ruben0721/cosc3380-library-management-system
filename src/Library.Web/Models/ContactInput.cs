using System.ComponentModel.DataAnnotations;

namespace Project.Models;

public class ContactInput
{
    [Phone, StringLength(30), Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }
    [StringLength(150), Display(Name = "Street address")]
    public string? AddressLine1 { get; set; }
    [StringLength(100), Display(Name = "Apartment, suite, or unit")]
    public string? AddressLine2 { get; set; }
    [StringLength(50)] public string? City { get; set; }
    [StringLength(30), Display(Name = "State / province")]
    public string? State { get; set; }
    [StringLength(20), Display(Name = "ZIP / postal code")]
    public string? PostalCode { get; set; }

    public static ContactInput FromPerson(LibraryUser person) => new()
    {
        PhoneNumber = person.PhoneNumber, AddressLine1 = person.AddressLine1,
        AddressLine2 = person.AddressLine2, City = person.City,
        State = person.State, PostalCode = person.PostalCode
    };

    public void ApplyTo(LibraryUser person)
    {
        person.PhoneNumber = Clean(PhoneNumber);
        person.AddressLine1 = Clean(AddressLine1);
        person.AddressLine2 = Clean(AddressLine2);
        person.City = Clean(City);
        person.State = Clean(State);
        person.PostalCode = Clean(PostalCode);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
