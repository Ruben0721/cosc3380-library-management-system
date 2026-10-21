using System.ComponentModel.DataAnnotations;

namespace Project.Pages.Shared
{
    public class LibraryUser
    {
        public int LibraryUserId { get; set; }
        [Required, StringLength(200)]
        public string FullName { get; set; }
        [Required]
        public bool IsActive { get; set; }
        [StringLength(30)]
        public string? UserStatus { get; set; }
    }
}
