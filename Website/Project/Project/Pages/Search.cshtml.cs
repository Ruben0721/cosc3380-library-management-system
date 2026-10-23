using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Pages.Shared;

namespace Project.Pages
{
    public class SearchModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public SearchModel(LibraryDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SelectedType { get; set; }

        public List<(string Value, string Label)> Types { get; set; } = new();
        public List<Inventory> Results { get; set; } = new();

     public async Task OnGetAsync()
    {
        Types = new()
        {
            ("book", "Book"),
            ("headphones", "HeadPhones"),
            ("media", "Media"),
            ("laptop", "Laptop"),
            ("map", "Map")
        };

        if (string.IsNullOrEmpty(SearchTerm) && string.IsNullOrEmpty(SelectedType))
            return;

        var query = _db.Inventory
            .AsNoTracking()
            .Include(i => i.Book)
            .Include(i => i.HeadPhone)
            .Include(i => i.Laptop)
            .Include(i => i.Map)
            .Include(i => i.Media)
            .AsQueryable();

        if (!string.IsNullOrEmpty(SelectedType))
            query = query.Where(i => i.Type == SelectedType);

        if (!string.IsNullOrEmpty(SearchTerm))
        {
            var s = SearchTerm;

            query = query.Where(i =>
                (i.Name != null && i.Name.Contains(s))
                || i.Type.Contains(s)
                || (i.barcode != null && i.barcode.Contains(s))

                || (i.Book != null && (i.Book.Title.Contains(s)
                                    || i.Book.Author.Contains(s)
                                    || i.Book.ISBN.Contains(s)))

                || (i.HeadPhone != null && (i.HeadPhone.Brand.Contains(s)
                                       || i.HeadPhone.Model.Contains(s)))

                || (i.Laptop != null && ((i.Laptop.Brand != null && i.Laptop.Brand.Contains(s))
                                    || (i.Laptop.Model != null && i.Laptop.Model.Contains(s))
                                    || (i.Laptop.SerialNumber != null && i.Laptop.SerialNumber.Contains(s))))

                || (i.Map != null && ((i.Map.Title != null && i.Map.Title.Contains(s))
                                || (i.Map.Region != null && i.Map.Region.Contains(s))))

                || (i.Media != null && (i.Media.ItemType.Contains(s)
                                    || (i.Media.Genre != null && i.Media.Genre.Contains(s))
                                    || (i.Media.Creator != null && i.Media.Creator.Contains(s)))));
        }

        Results = await query
            .OrderBy(i => i.Type).ThenBy(i => i.Name)
            .ToListAsync();
    }
}
}