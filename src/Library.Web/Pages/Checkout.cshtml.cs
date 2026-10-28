using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Project.Data;
using Project.Models;

namespace Project.Pages
{
    public class CheckoutModel : PageModel
    {
        private readonly LibraryDbContext _db;
        public CheckoutModel(LibraryDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public int ItemId { get; set; }
        [BindProperty(SupportsGet = true)] public string? BorrowerId { get; set; }

        // Loaded data
        public Inventory? Item { get; set; }
        public int? LibraryUserId { get; set; }
        public string BorrowerName { get; set; } = "";
        public string BorrowerType { get; set; } = "";
        public int ActiveRentals { get; set; }
        public int Limit { get; set; }
        public DateTime DueDate { get; } = DateTime.Today.AddDays(14);

        public string? Error { get; set; }
        public bool CanCheckout => Item != null && LibraryUserId != null && Error == null;

       
        public async Task<IActionResult> OnGetAsync()
        {
            await LoadAsync();
            return Item == null ? NotFound() : Page();
        }

      
        public async Task<IActionResult> OnPostAsync()
        {
            await LoadAsync();                    
            if (Item == null) return NotFound();
            if (!CanCheckout) return Page();

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                
                _db.Rentals.Add(new Rental
                {
                    ItemId = Item.Id,
                    LibraryUserId = LibraryUserId!.Value,
                    StartDate = DateTime.Now,
                    EndDate = DueDate,
                    IsReturned = false
                });
                await _db.SaveChangesAsync();

               
                var row = await _db.Inventory.FindAsync(Item.Id);
                row!.Status = "Checked Out";
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                Error = ex.InnerException?.Message ?? ex.Message;  
                return Page();
            }

            TempData["Message"] = $"{Item.Name} checked out to {BorrowerName}, due {DueDate:MMM d, yyyy}.";
            return RedirectToPage("/ItemDetails", new { id = Item.Id });
        }

        private async Task LoadAsync()
        {
            Item = await _db.Inventory.AsNoTracking().FirstOrDefaultAsync(i => i.Id == ItemId);
            if (Item == null) return;

            if (Item.Status != "Available")
            {
                Error = $"This item is not available (status: {Item.Status}).";
                return;
            }

            if (string.IsNullOrWhiteSpace(BorrowerId)) return;  
            var id = BorrowerId.Trim();

            // Is it a Student ID?
            var student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.StudentId == id);
            if (student != null)
            {
                LibraryUserId = student.LibraryUserId;
                BorrowerName = $"{student.FirstName} {student.LastName}";
                BorrowerType = "Student";
                Limit = 5;
            }
            else
            {
               
                var faculty = await _db.FacultyMembers.AsNoTracking().FirstOrDefaultAsync(f => f.FacultyId == id);
                if (faculty != null)
                {
                    LibraryUserId = faculty.LibraryUserId;
                    BorrowerName = $"{faculty.FirstName} {faculty.LastName}";
                    BorrowerType = "Faculty";
                    Limit = faculty.RentalLimit ?? 10;
                }
            }

            if (LibraryUserId == null)
            {
                Error = $"No student or faculty member found with ID {id}.";
                return;
            }

            
            ActiveRentals = await _db.Rentals.CountAsync(r =>
                r.LibraryUserId == LibraryUserId && r.ReturnedDate == null);

            if (ActiveRentals >= Limit)
                Error = $"{BorrowerName} has reached the limit of {Limit} items.";
        }
    }
}