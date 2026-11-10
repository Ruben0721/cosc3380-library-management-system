using Project.Models;
using Microsoft.EntityFrameworkCore;
using System;
namespace Project.Data
{
    public class LibraryDbContext : DbContext
    {
        public LibraryDbContext(DbContextOptions<LibraryDbContext> options) : base(options) { }
        public DbSet<Inventory> Inventory => Set<Inventory>();
        public DbSet<LibraryUser> LibraryUsers => Set<LibraryUser>();
        public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
        public DbSet<Student> Students => Set<Student>();
        public DbSet<Books> Books => Set<Books>();
        public DbSet<HeadPhones> HeadPhones => Set<HeadPhones>();
        public DbSet<Media> Medias => Set<Media>();
        public DbSet<Laptops> Laptops => Set<Laptops>();
        public DbSet<Maps> Maps => Set<Maps>();
        public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
        public DbSet<FacultyMember> FacultyMembers => Set<FacultyMember>();
        public DbSet<Rental> Rentals => Set<Rental>();
        public DbSet<Hold> Holds => Set<Hold>();
        public DbSet<Fine> Fines => Set<Fine>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<FinePolicy> FinePolicies => Set<FinePolicy>();
        public DbSet<MaintenanceRequest> MaintenanceRequests => Set<MaintenanceRequest>();
        public DbSet<Room> Rooms => Set<Room>();
        public DbSet<RoomReservation> RoomReservations => Set<RoomReservation>();
        public DbSet<RoomSetupTask> RoomSetupTasks => Set<RoomSetupTask>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Rental>().ToTable("Rentals");
            modelBuilder.Entity<Books>().HasKey(x => x.ItemId);
            modelBuilder.Entity<Books>().Property(x => x.ItemId).ValueGeneratedNever();
            modelBuilder.Entity<Books>().HasOne(x => x.Inventory).WithOne(i => i.Book).HasForeignKey<Books>(x => x.ItemId);

            modelBuilder.Entity<HeadPhones>().HasKey(x => x.ItemId);
            modelBuilder.Entity<HeadPhones>().Property(x => x.ItemId).ValueGeneratedNever();
            modelBuilder.Entity<HeadPhones>().HasOne(x => x.Inventory).WithOne(i => i.HeadPhone).HasForeignKey<HeadPhones>(x => x.ItemId);

            modelBuilder.Entity<Media>().HasKey(x => x.ItemId);
            modelBuilder.Entity<Media>().Property(x => x.ItemId).ValueGeneratedNever();
            modelBuilder.Entity<Media>().HasOne(x => x.Inventory).WithOne(i => i.Media).HasForeignKey<Media>(x => x.ItemId);

            modelBuilder.Entity<Laptops>().HasKey(x => x.ItemId);
            modelBuilder.Entity<Laptops>().Property(x => x.ItemId).ValueGeneratedNever();
            modelBuilder.Entity<Laptops>().HasOne(x => x.Inventory).WithOne(i => i.Laptop).HasForeignKey<Laptops>(x => x.ItemId);

            modelBuilder.Entity<Maps>().HasKey(x => x.ItemId);
            modelBuilder.Entity<Maps>().Property(x => x.ItemId).ValueGeneratedNever();
            modelBuilder.Entity<Maps>().HasOne(x => x.Inventory).WithOne(i => i.Map).HasForeignKey<Maps>(x => x.ItemId);

            modelBuilder.Entity<HeadPhones>().ToTable("Headphones");
            modelBuilder.Entity<Media>().ToTable("Media");
            modelBuilder.Entity<LibraryUser>().ToTable("Libraryuser");
            modelBuilder.Entity<Room>().ToTable("Room");
            modelBuilder.Entity<RoomReservation>().ToTable("Room_reservation");
            modelBuilder.Entity<RoomSetupTask>().ToTable("Room_setup_task");

            modelBuilder.Entity<RoomReservation>()
                .HasOne(r => r.Room)
                .WithMany()
                .HasForeignKey(r => r.RoomId);

            modelBuilder.Entity<RoomReservation>()
                .HasOne(r => r.LibraryUser)
                .WithMany()
                .HasForeignKey(r => r.LibraryUserId);

            modelBuilder.Entity<RoomSetupTask>()
                .HasOne(t => t.Room)
                .WithMany()
                .HasForeignKey(t => t.RoomId);

            modelBuilder.Entity<RoomSetupTask>()
                .HasOne(t => t.Reservation)
                .WithMany()
                .HasForeignKey(t => t.ReservationId);

            base.OnModelCreating(modelBuilder);
        }

        public async Task<List<Books>> GetBooksAsync()
        {
            return await Books.AsNoTracking().Include(x => x.Inventory).ToListAsync();
        }

        public async Task<List<Maps>> GetMapsAsync()
        {
            return await Maps.AsNoTracking().Include(x => x.Inventory).ToListAsync();
        }

        public async Task<List<Laptops>> GetLaptopsAsync()
        {
            return await Laptops.AsNoTracking().Include(x => x.Inventory).ToListAsync();
        }

        public async Task<bool> UpdateBookAsync(Books book)
        {
            try
            {
                var bookExisting = Books.AsNoTracking().FirstOrDefault(x => x.ItemId == book.ItemId);
                if (bookExisting == null) return false;
                bookExisting.ISBN = book.ISBN;
                bookExisting.BookLanguage = book.BookLanguage;
                bookExisting.PublicationYear = book.PublicationYear;
                bookExisting.Author = book.Author;
                bookExisting.Title = book.Title;
                await SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public async Task<List<Inventory>> GetInventoryAsync()
        {
            return await Inventory.AsNoTracking().ToListAsync();
        }
        public IQueryable<Inventory> ActiveInventory => Inventory.Where(i => i.RetiredAt == null);

        public IQueryable<Inventory> InventoryWithDetails()
                {
                    return Inventory
                        .AsNoTracking()
                        .Include(i => i.Book)
                        .Include(i => i.HeadPhone)
                        .Include(i => i.Laptop)
                        .Include(i => i.Map)
                        .Include(i => i.Media);
                }
        public async Task<Inventory> AddInventoryItemAsync(Inventory item)
        {
            Inventory.Add(item);
            await SaveChangesAsync();
            return item;
        }

        public async Task<Inventory?> GetInventoryItemAsync(int id)
        {
            return await Inventory.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<bool> UpdateInventoryItemAsync(Inventory item)
        {
            Inventory.Update(item);
            return await SaveChangesAsync() > 0;
        }

        public async Task<List<LibraryUser>> GetLibraryUsersAsync()
        {
            return await LibraryUsers.AsNoTracking().ToListAsync();
        }

        public async Task<LibraryUser> AddLibraryUserAsync(LibraryUser libraryUser)
        {
            LibraryUsers.Add(libraryUser);
            await SaveChangesAsync();
            return libraryUser;
        }

        public async Task<LibraryUser?> GetLibraryUser(int id)
        {
            return await LibraryUsers.FirstOrDefaultAsync(x => x.LibraryUserId == id);
        }
    }
}
