using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Models;
using Project.Data;
using Project.Configuration;
using System;
namespace Project.Pages
{
    public class ReportModel : PageModel
    {
        private readonly LibraryDbContext _db;

        public ReportModel(LibraryDbContext db) => _db = db;

        [BindProperty (SupportsGet =true)] public string? SelectedType {get; set;}
        [BindProperty (SupportsGet =true)] public string? SelectedStatus {get; set;}
        [BindProperty (SupportsGet =true)] public bool Generate {get; set;}

        public List<(string Value, string Label)> Types => LibraryOptions.Types;
        public List<string> Statuses => LibraryOptions.Statuses;

        public List<Inventory> Items { get; set; } = new();
        public DateTime GeneratedAt { get; set; }

        public IEnumerable<(string Value, string Label)> TypesInReport => 
            Types.Where(t=>Items.Any(i =>i.Type ==t.Value));
        public int Count(string type, string status) =>
            Items.Count(i => i.Type == type && i.Status == status);

        public async Task OnGetAsync()
        {
            if (!Generate) return;

            var query = _db.InventoryWithDetails();
            if (!string.IsNullOrEmpty(SelectedType))
                query = query.Where(i => i.Type == SelectedType);

            if (!string.IsNullOrEmpty(SelectedStatus))
                query = query.Where(i => i.Status == SelectedStatus);

            Items = await query
                .OrderBy(i => i.Type).ThenBy(i => i.Name)
                .ToListAsync();

            GeneratedAt = DateTime.Now;
        }

    }
}