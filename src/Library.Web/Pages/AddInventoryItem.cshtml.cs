using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Models;
using Project.Data;
using Project.Configuration;

namespace Project.Pages
{
    public class AddInventoryItemModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public AddInventoryItemModel(LibraryDbContext db) => _db = db;
        [BindProperty(SupportsGet = true)]
        public string? SelectedType { get; set; }

        public Inventory inventoryItem { get; set; } = new();
        public Media Media { get; set; } = new();
        public Maps Map { get; set; } = new();
        public HeadPhones Headphone { get; set; } = new();
        public Laptops Laptop { get; set; } = new();

        public Books Book { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public int? ItemId { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!ItemId.HasValue)
                return Page();

            var item = await _db.Set<Inventory>()
                .Include(i => i.Book)
                .Include(i => i.Media)
                .Include(i => i.Laptop)
                .Include(i => i.HeadPhone)
                .Include(i => i.Map)
                .FirstOrDefaultAsync(i => i.Id == ItemId.Value);

            if (item == null)
                return NotFound();

            inventoryItem = item;

            SelectedType = item.Type?.Trim().ToLowerInvariant();

            // Match the values used by your Razor page.
            if (SelectedType == "headphone")
                SelectedType = "headphones";

            Book = item.Book ?? new Books();
            Media = item.Media ?? new Media();
            Laptop = item.Laptop ?? new Laptops();
            Headphone = item.HeadPhone ?? new HeadPhones();
            Map = item.Map ?? new Maps();

            return Page();
        }

        private async Task<Inventory?> GetExistingItemAsync()
        {
            if (!ItemId.HasValue)
                return null;

            return await _db.Inventory
                .Include(i => i.Book)
                .Include(i => i.Media)
                .Include(i => i.Laptop)
                .Include(i => i.HeadPhone)
                .Include(i => i.Map)
                .FirstOrDefaultAsync(i => i.Id == ItemId.Value);
        }

        public async Task<IActionResult> OnPostSaveItemAsync()
        {
            if (!await TryUpdateModelAsync(inventoryItem, nameof(inventoryItem))) return Page();

            await _db.AddInventoryItemAsync(inventoryItem);
            return RedirectToPage("Inventory");
        }

        public async Task<IActionResult> OnPostSaveBookAsync()
        {
            await TryUpdateModelAsync(Book, nameof(Book));
            await TryUpdateModelAsync(inventoryItem, nameof(inventoryItem));

            ModelState.Remove("Book.Inventory");
            ModelState.Remove("Book.InventoryId");

            inventoryItem.Type = SelectedType ?? "book";
            inventoryItem.Name = Book.Title;

            ModelState.Remove("inventoryItem.Type");
            ModelState.Remove("inventoryItem.Name");

            if (string.IsNullOrWhiteSpace(inventoryItem.Type))
            {
                ModelState.AddModelError(
                    "inventoryItem.Type",
                    "Inventory item type could not be assigned.");
            }

            if (!ModelState.IsValid)
                return Page();

            var duplicateBarcode = await _db.Inventory
                .AnyAsync(i =>
                i.barcode == inventoryItem.barcode &&
                (!ItemId.HasValue || i.Id != ItemId.Value));

            if (duplicateBarcode)
            {
                ModelState.AddModelError(
                    "inventoryItem.barcode",
                    "This barcode is already assigned to another inventory item.");

                return Page();
            }

            if (ItemId.HasValue)
            {
                var existing = await GetExistingItemAsync();

                if (existing == null)
                    return NotFound();

                if (!string.Equals(existing.Type, "book",
                        StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Inventory item type mismatch.");

                if (existing.Book == null)
                    return BadRequest("Book record not found.");

                existing.Name = Book.Title;
                existing.Status = inventoryItem.Status;
                existing.barcode = inventoryItem.barcode;

                existing.Book.Title = Book.Title;
                existing.Book.Author = Book.Author;
                existing.Book.PublicationYear = Book.PublicationYear;
                existing.Book.BookLanguage = Book.BookLanguage;
                existing.Book.ISBN = Book.ISBN;



                await _db.SaveChangesAsync();
            }
            else
            {
                inventoryItem.Book = Book;
                await _db.AddInventoryItemAsync(inventoryItem);
            }

            return RedirectToPage("Inventory");
        }

        public async Task<IActionResult> OnPostSaveMediaAsync()
        {
            await TryUpdateModelAsync(Media, nameof(Media));
            await TryUpdateModelAsync(inventoryItem, nameof(inventoryItem));

            ModelState.Remove("Media.Inventory");
            ModelState.Remove("Media.InventoryId");

            inventoryItem.Type = SelectedType ?? "media";

            ModelState.Remove("inventoryItem.Type");

            if (string.IsNullOrWhiteSpace(inventoryItem.Type))
            {
                ModelState.AddModelError(
                    "inventoryItem.Type",
                    "Inventory item type could not be assigned.");
            }

            if (!ModelState.IsValid)
                return Page();

            var duplicateBarcode = await _db.Inventory
                .AnyAsync(i =>
                i.barcode == inventoryItem.barcode &&
                (!ItemId.HasValue || i.Id != ItemId.Value));

            if (duplicateBarcode)
            {
                ModelState.AddModelError(
                    "inventoryItem.barcode",
                    "This barcode is already assigned to another inventory item.");

                return Page();
            }

            if (ItemId.HasValue)
            {
                var existing = await GetExistingItemAsync();

                if (existing == null)
                    return NotFound();

                if (!string.Equals(existing.Type, "media",
                        StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Inventory item type mismatch.");

                if (existing.Media == null)
                    return BadRequest("Media record not found.");

                existing.Name = inventoryItem.Name;
                existing.Status = inventoryItem.Status;
                existing.barcode = inventoryItem.barcode;

                existing.Media.ItemType = Media.ItemType;
                existing.Media.Genre = Media.Genre;
                existing.Media.Creator = Media.Creator;

                await _db.SaveChangesAsync();
            }
            else
            {
                inventoryItem.Media = Media;
                await _db.AddInventoryItemAsync(inventoryItem);
            }

            return RedirectToPage("Inventory");
        }

        public async Task<IActionResult> OnPostSaveLaptopAsync()
        {
            await TryUpdateModelAsync(Laptop, nameof(Laptop));
            await TryUpdateModelAsync(inventoryItem, nameof(inventoryItem));

            ModelState.Remove("Laptop.Inventory");
            ModelState.Remove("Laptop.InventoryId");

            inventoryItem.Type = SelectedType ?? "laptop";
            inventoryItem.Name = $"{Laptop.Brand}: {Laptop.Model}";

            ModelState.Remove("inventoryItem.Type");
            ModelState.Remove("inventoryItem.Name");

            if (string.IsNullOrWhiteSpace(inventoryItem.Type))
            {
                ModelState.AddModelError(
                    "inventoryItem.Type",
                    "Inventory item type could not be assigned.");
            }

            if (!ModelState.IsValid)
                return Page();

            var duplicateBarcode = await _db.Inventory
                .AnyAsync(i =>
                i.barcode == inventoryItem.barcode &&
                (!ItemId.HasValue || i.Id != ItemId.Value));

            if (duplicateBarcode)
            {
                ModelState.AddModelError(
                    "inventoryItem.barcode",
                    "This barcode is already assigned to another inventory item.");

                return Page();
            }

            if (ItemId.HasValue)
            {
                var existing = await GetExistingItemAsync();

                if (existing == null)
                    return NotFound();

                if (!string.Equals(existing.Type, "laptop",
                        StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Inventory item type mismatch.");

                if (existing.Laptop == null)
                    return BadRequest("Laptop record not found.");

                existing.Name = $"{Laptop.Brand}: {Laptop.Model}";
                existing.Status = inventoryItem.Status;
                existing.barcode = inventoryItem.barcode;

                existing.Laptop.Brand = Laptop.Brand;
                existing.Laptop.Model = Laptop.Model;
                existing.Laptop.SerialNumber = Laptop.SerialNumber;

                await _db.SaveChangesAsync();
            }
            else
            {
                inventoryItem.Laptop = Laptop;
                await _db.AddInventoryItemAsync(inventoryItem);
            }

            return RedirectToPage("Inventory");
        }

        public async Task<IActionResult> OnPostSaveHeadphoneAsync()
        {
            await TryUpdateModelAsync(Headphone, nameof(Headphone));
            await TryUpdateModelAsync(inventoryItem, nameof(inventoryItem));

            ModelState.Remove("Headphone.Inventory");
            ModelState.Remove("Headphone.InventoryId");

            inventoryItem.Type = SelectedType ?? "headphones";
            inventoryItem.Name = $"{Headphone.Brand}: {Headphone.Model}";

            ModelState.Remove("inventoryItem.Type");
            ModelState.Remove("inventoryItem.Name");

            if (string.IsNullOrWhiteSpace(inventoryItem.Type))
            {
                ModelState.AddModelError(
                    "inventoryItem.Type",
                    "Inventory item type could not be assigned.");
            }

            if (!ModelState.IsValid)
                return Page();

            var duplicateBarcode = await _db.Inventory
                .AnyAsync(i =>
                i.barcode == inventoryItem.barcode &&
                (!ItemId.HasValue || i.Id != ItemId.Value));

            if (duplicateBarcode)
            {
                ModelState.AddModelError(
                    "inventoryItem.barcode",
                    "This barcode is already assigned to another inventory item.");

                return Page();
            }

            if (ItemId.HasValue)
            {
                var existing = await GetExistingItemAsync();

                if (existing == null)
                    return NotFound();

                if (!string.Equals(existing.Type, "headphones",
                        StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Inventory item type mismatch.");

                if (existing.HeadPhone == null)
                    return BadRequest("Headphone record not found.");

                existing.Name = $"{Headphone.Brand}: {Headphone.Model}";
                existing.Status = inventoryItem.Status;
                existing.barcode = inventoryItem.barcode;

                existing.HeadPhone.Brand = Headphone.Brand;
                existing.HeadPhone.Model = Headphone.Model;

                await _db.SaveChangesAsync();
            }
            else
            {
                inventoryItem.HeadPhone = Headphone;
                await _db.AddInventoryItemAsync(inventoryItem);
            }

            return RedirectToPage("Inventory");
        }

        public async Task<IActionResult> OnPostSaveMapAsync()
        {
            await TryUpdateModelAsync(Map, nameof(Map));
            await TryUpdateModelAsync(inventoryItem, nameof(inventoryItem));

            ModelState.Remove("Map.Inventory");
            ModelState.Remove("Map.InventoryId");

            inventoryItem.Type = SelectedType ?? "map";
            inventoryItem.Name = Map.Title;

            ModelState.Remove("inventoryItem.Type");
            ModelState.Remove("inventoryItem.Name");

            if (string.IsNullOrWhiteSpace(inventoryItem.Type))
            {
                ModelState.AddModelError(
                    "inventoryItem.Type",
                    "Inventory item type could not be assigned.");
            }

            if (!ModelState.IsValid)
                return Page();

            var duplicateBarcode = await _db.Inventory
                .AnyAsync(i =>
                i.barcode == inventoryItem.barcode &&
                (!ItemId.HasValue || i.Id != ItemId.Value));

            if (duplicateBarcode)
            {
                ModelState.AddModelError(
                    "inventoryItem.barcode",
                    "This barcode is already assigned to another inventory item.");

                return Page();
            }

            if (ItemId.HasValue)
            {
                var existing = await GetExistingItemAsync();

                if (existing == null)
                    return NotFound();

                if (!string.Equals(existing.Type, "map",
                        StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Inventory item type mismatch.");

                if (existing.Map == null)
                    return BadRequest("Map record not found.");

                existing.Name = Map.Title;
                existing.Status = inventoryItem.Status;
                existing.barcode = inventoryItem.barcode;

                existing.Map.Title = Map.Title;
                existing.Map.Region = Map.Region;
                existing.Map.Edition = Map.Edition;

                await _db.SaveChangesAsync();
            }
            else
            {
                inventoryItem.Map = Map;
                await _db.AddInventoryItemAsync(inventoryItem);
            }

            return RedirectToPage("Inventory");
        }
    }
}
