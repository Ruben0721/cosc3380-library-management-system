namespace Project.Configuration
{
    public static class LibraryOptions
    {
        public static readonly List<(string Value, string Label)> Types = new()
        {
            ("book", "Book"),
            ("headphones", "HeadPhones"),
            ("media", "Media"),
            ("laptop", "Laptop"),
            ("map", "Map")
        };

        public static readonly List<string> Statuses = new()
        {
            "Available", "Checked Out", "On Hold", "Under Repair", "Lost"
        };
    }
}