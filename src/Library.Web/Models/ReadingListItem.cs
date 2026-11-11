using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Project.Models
{
    // A title a member saved to their reading list.
    [Table("ReadingList")]
    public class ReadingListItem
    {
        [Key]
        public int ReadingListId { get; set; }

        public int LibraryUserId { get; set; }

        public int ItemId { get; set; }

        public DateTime AddedAt { get; set; }
    }
}
