using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Pages.Shared
{
    [Table("Staff")]
    public class StaffMember
    {
        [Key]
        [Column("Staff_Id")]
        public int StaffId { get; set; }

        public bool IsActive { get; set; }
    }
}