using System.ComponentModel.DataAnnotations;

namespace Project.Pages.Shared
{
    public class Rental
    {
        [Key]
        public int RentalId { get; set; }
        public int ItemId { get; set; }
        public int LibraryUserId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }        
        public DateTime? ReturnedDate { get; set; } 
    }
}