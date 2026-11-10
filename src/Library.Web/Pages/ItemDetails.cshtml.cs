using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Models;
using Project.Data;
using Project.Configuration;

namespace Project.Pages
{
    public class ItemDetailsModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public ItemDetailsModel(LibraryDbContext db) => _db = db;

        public Inventory? Item { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            Item = await _db.Inventory
                .AsNoTracking()
                .Include(i => i.Book)
                .Include(i => i.HeadPhone)
                .Include(i => i.Laptop)
                .Include(i => i.Map)
                .Include(i => i.Media)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (Item == null)
                return NotFound();
            return Page();
        }
    }
}