using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Pages.Shared
{
    [Table("UserAccount")]
    public class UserAccount
    {
        [Key]
        public int AccountId { get; set; }

        [Required, StringLength(255)]
        public string Email { get; set; } = "";

        [Required, StringLength(255)]
        public string NormalizedEmail { get; set; } = "";

        [Required, StringLength(512)]
        public string PasswordHash { get; set; } = "";

        [Required, StringLength(20)]
        public string Role { get; set; } = "Student";

        public bool IsEnabled { get; set; } = true;

        public int? LibraryUserId { get; set; }

        public int? StaffId { get; set; }

        public int FailedLoginAttempts { get; set; } = 0;

        public DateTime? LockoutUntilUtc { get; set; }
    }
}