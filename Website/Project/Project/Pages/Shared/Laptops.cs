using System.ComponentModel.DataAnnotations;

namespace Project.Pages.Shared
{
    public class Laptops
    {
        public int ItemId { get; set; }
        [StringLength(50)]
        public string? Brand { get; set; }
        [StringLength(50)]
        public string? Model { get; set; }
        [StringLength(50)]
        public string? SerialNumber { get; set; }

        public Inventory? Inventory { get; set; }
    }
}
