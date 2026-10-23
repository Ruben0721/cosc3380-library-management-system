using System.ComponentModel.DataAnnotations;

namespace Project.Pages.Shared
{
    public class Inventory
    {
        [Key]
        public int Id { get; set; }
        [Required, StringLength(50)]
        public string Type { get; set; }
        [Required, StringLength(300)]
        public string? Name { get; set; }
        [Required, StringLength(50)]
        public string? Status { get; set; }
        [Required, StringLength(50)]
        public string? barcode { get; set; }

        public Books? Book { get; set; }
        public HeadPhones? HeadPhone { get; set; }
        public Media? Media { get; set; }
        public Laptops? Laptop { get; set; }
        public Maps? Map { get; set; }
    }
}
