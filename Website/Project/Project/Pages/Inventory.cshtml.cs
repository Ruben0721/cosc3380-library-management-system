using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Pages.Shared;
using System;

namespace Project.Pages
{
    public class IndexModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public IndexModel(LibraryDbContext db) => _db = db;

        public List<Inventory> Inventory { get; set; } = new();

        public async Task OnGetAsync()
        {
            Inventory = await _db.GetInventoryAsync();
        }

        
    }
}
