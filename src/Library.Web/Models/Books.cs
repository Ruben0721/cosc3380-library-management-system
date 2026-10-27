using System.ComponentModel.DataAnnotations;

namespace Project.Models
{
    public class Books
    {
        public int ItemId { get; set; }
        [Required, StringLength(200)]
        public string Title { get; set; }
        [StringLength(200)]
        public string? Author { get; set; }
        public int? PublicationYear { get; set; }
        [Required, StringLength(50)]
        public string BookLanguage { get; set; }
        [Required, StringLength(20)]
        public string ISBN { get; set; }

        public Inventory Inventory { get; set; }
    }
}
