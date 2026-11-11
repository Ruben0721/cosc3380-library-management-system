using System.Globalization;
using System.Text;

namespace Project.Pages
{
    // A simple table of supporting records: what a report number was counted from.
    // The same table is shown on the page and downloaded as CSV, so they always match.
    public class RecordTable
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Headers { get; } = new();
        public HashSet<int> NumericColumns { get; } = new();
        public List<string[]> Rows { get; } = new();
        public string[]? Totals { get; set; }

        public RecordTable Column(string header, bool numeric = false)
        {
            if (numeric) NumericColumns.Add(Headers.Count);
            Headers.Add(header);
            return this;
        }

        public void Add(params object?[] cells) => Rows.Add(cells.Select(Format).ToArray());

        public static string Money(decimal value) => value.ToString("C", CultureInfo.CurrentCulture);
        public static string Day(DateTime? value) => value?.ToString("MMM d, yyyy") ?? "—";

        private static string Format(object? value) => value switch
        {
            null => "—",
            DateTime d => Day(d),
            decimal m => Money(m),
            double x => x.ToString("0.#"),
            _ => value.ToString() ?? ""
        };

        public byte[] ToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Headers.Select(Escape)));
            foreach (var row in Rows)
                sb.AppendLine(string.Join(",", row.Select(Escape)));
            if (Totals != null)
                sb.AppendLine(string.Join(",", Totals.Select(Escape)));
            // UTF-8 with BOM so Excel shows the dollar signs and accents correctly
            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        private static string Escape(string s) =>
            s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}
