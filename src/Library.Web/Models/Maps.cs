using System.ComponentModel.DataAnnotations;

namespace Project.Models
{
    public class Maps
    {
        public int ItemId { get; set; }
        [Required,StringLength(50)]
        public string? Title { get; set; }
        [StringLength(200)]
        public string? Region { get; set; }
        public int? Edition { get; set; }

        public Inventory? Inventory { get; set; }
    }
}
