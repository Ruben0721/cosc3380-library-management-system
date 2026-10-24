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

 
        [BindProperty(SupportsGet = true)] public string? SelectedType { get; set; }
        [BindProperty(SupportsGet = true)] public string? SelectedStatus { get; set; }
        [BindProperty(SupportsGet = true)] public string? Sort { get; set; }  

        public List<(string Value, string Label)> Types => LibraryOptions.Types;
        public List<string> Statuses => LibraryOptions.Statuses;

        public List<Inventory> Inventory { get; set; } = new();

        public int TotalItems { get; set; }

        public async Task OnGetAsync()
        {
            
            TotalItems = await _db.Inventory.CountAsync();

            var query = _db.Inventory.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(SelectedType))
                query = query.Where(i => i.Type == SelectedType);

            if (!string.IsNullOrEmpty(SelectedStatus))
                query = query.Where(i => i.Status == SelectedStatus);

            query = Sort switch
            {
                "type"   => query.OrderBy(i => i.Type).ThenBy(i => i.Name),
                "status" => query.OrderBy(i => i.Status).ThenBy(i => i.Name),
                "newest" => query.OrderByDescending(i => i.Id),
                _        => query.OrderBy(i => i.Name)
            };

            Inventory = await query.ToListAsync();
        }
        }

        
    }

