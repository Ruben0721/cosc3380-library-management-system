using System.ComponentModel.DataAnnotations;

namespace Project.Models
{
    public class Media
    {
        public int ItemId { get; set; }
        [Required, StringLength(50)]
        public string ItemType { get; set; }
        [StringLength(50)]
        public string? Genre { get; set; }
        [StringLength(200)]
        public string? Creator { get; set; }

        public Inventory Inventory { get; set; }
    }
}
