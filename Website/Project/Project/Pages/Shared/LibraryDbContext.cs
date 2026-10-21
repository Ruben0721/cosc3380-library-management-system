using Microsoft.EntityFrameworkCore;
using System;
namespace Project.Pages.Shared
{
    public class LibraryDbContext : DbContext
    {
        public LibraryDbContext(DbContextOptions<LibraryDbContext> options) : base(options) { }
        public DbSet<Inventory> Inventory => Set<Inventory>();
        public DbSet<LibraryUser> LibraryUsers => Set<LibraryUser>();
        public DbSet<Books> Books => Set<Books>();
        public DbSet<HeadPhones> HeadPhones => Set<HeadPhones>();
        public DbSet<Media> Medias => Set<Media>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Books>().HasKey(x => x.ItemId);
            modelBuilder.Entity<Books>().Property(x => x.ItemId).ValueGeneratedNever();
            modelBuilder.Entity<Books>().HasOne(x => x.Inventory).WithOne(i => i.Book).HasForeignKey<Books>(x => x.ItemId);

            modelBuilder.Entity<HeadPhones>().HasKey(x => x.ItemId);
            modelBuilder.Entity<HeadPhones>().Property(x => x.ItemId).ValueGeneratedNever();
            modelBuilder.Entity<HeadPhones>().HasOne(x => x.Inventory).WithOne(i => i.HeadPhone).HasForeignKey<HeadPhones>(x => x.ItemId);

            modelBuilder.Entity<Media>().HasKey(x => x.ItemId);
            modelBuilder.Entity<Media>().Property(x => x.ItemId).ValueGeneratedNever();
            modelBuilder.Entity<Media>().HasOne(x => x.Inventory).WithOne(i => i.Media).HasForeignKey<Media>(x => x.ItemId);

            base.OnModelCreating(modelBuilder);
        }

        public async Task<List<Books>> GetBooksAsync()
        {
            return await Books.AsNoTracking().Include(x => x.Inventory).ToListAsync();
        }

        public async Task<List<Inventory>> GetInventoryAsync()
        {
            return await Inventory.AsNoTracking().ToListAsync();
        }

        public async Task<Inventory> AddInvetoryItemAsync(Inventory item)
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
