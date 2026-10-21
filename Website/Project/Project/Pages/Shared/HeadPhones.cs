using System.ComponentModel.DataAnnotations;

namespace Project.Pages.Shared
{
    public class HeadPhones
    {
        public int ItemId { get; set; }
        [Required, StringLength(50)]
        public string Brand { get; set; }
        [Required, StringLength(50)]
        public string Model { get; set; }

        public Inventory Inventory { get; set; }
    }
}
