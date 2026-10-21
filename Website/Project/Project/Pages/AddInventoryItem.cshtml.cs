using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Project.Pages.Shared;

namespace Project.Pages
{
    public class PrivacyModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public PrivacyModel(LibraryDbContext db) => _db = db;
        [BindProperty(SupportsGet = true)]
        public string? SelectedType { get; set; }

        public Inventory inventoryItem { get; set; } = new();

        public Books Book { get; set; } = new();

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostSaveItemAsync()
        {
            if (!await TryUpdateModelAsync(inventoryItem, nameof(inventoryItem))) return Page();

            await _db.AddInvetoryItemAsync(inventoryItem);
            return RedirectToPage("Inventory");
        }

        public async Task<IActionResult> OnPostSaveBookAsync()
        {
            await TryUpdateModelAsync(Book, nameof(Book));

            inventoryItem.Type = SelectedType ?? "book";
            inventoryItem.Name = Book.Title;
            await TryUpdateModelAsync(inventoryItem, nameof(inventoryItem));

            ModelState.Remove("Book.Inventory");
            ModelState.Remove("Book.InventoryId");

            if (!ModelState.IsValid) return Page();

            inventoryItem.Book = Book;
            await _db.AddInvetoryItemAsync(inventoryItem);
            return RedirectToPage("Inventory");
        }
    }

}
