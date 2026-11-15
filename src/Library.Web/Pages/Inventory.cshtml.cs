using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Models;
using Project.Data;
using Project.Configuration;
using System;

namespace Project.Pages
{
    public class InventoryModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public InventoryModel(LibraryDbContext db) => _db = db;

 
        [BindProperty(SupportsGet = true)] public string? SelectedType { get; set; }
        [BindProperty(SupportsGet = true)] public string? SelectedStatus { get; set; }
        [BindProperty(SupportsGet = true)] public string? Sort { get; set; }  

        public List<(string Value, string Label)> Types => LibraryOptions.Types;
        public List<string> Statuses => LibraryOptions.Statuses.Concat(new[] { "Awaiting Pickup" }).ToList();
        public HashSet<int> AwaitingPickupIds { get; set; } = new();

        public List<Inventory> Inventory { get; set; } = new();

        public int TotalItems { get; set; }

        public async Task OnGetAsync()
        {
            
            TotalItems = await _db.Inventory.CountAsync();

            var query = _db.Inventory.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(SelectedType))
                query = query.Where(i => i.Type == SelectedType);

            var reserved = _db.Holds.AsNoTracking().Where(h => h.Status == "Ready" && h.ItemId != null).Select(h => h.ItemId!.Value);
            if (SelectedStatus == "Awaiting Pickup")
                query = query.Where(i => reserved.Contains(i.Id));
            else if (!string.IsNullOrEmpty(SelectedStatus))
                query = query.Where(i => i.Status == SelectedStatus && !reserved.Contains(i.Id));

            query = Sort switch
            {
                "type"   => query.OrderBy(i => i.Type).ThenBy(i => i.Name),
                "status" => query.OrderBy(i => i.Status).ThenBy(i => i.Name),
                "newest" => query.OrderByDescending(i => i.Id),
                _        => query.OrderBy(i => i.Name)
            };

            Inventory = await query.ToListAsync();
            var ids = Inventory.Select(i => i.Id).ToList();
            AwaitingPickupIds = (await reserved.Where(id => ids.Contains(id)).ToListAsync()).ToHashSet();
        }
        }

        
    }

