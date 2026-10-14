using ERecycleHub.LegacyImport;

// Usage:
//   dotnet run --project tools/ERecycleHub.LegacyImport -- --source "<legacy connection>" --target "<new connection>"
//       [--files "<legacy Web/App_File folder>"] [--storage "<new storage root>"] [--report "<folder>"] [--dry-run]
// The legacy database is only read. The target must be empty; it is migrated first.
var a = args.ToList();
string? Arg(string name) => a.IndexOf(name) is var i and >= 0 && i + 1 < a.Count ? a[i + 1] : null;
var source = Arg("--source");
var target = Arg("--target");
if (source is null || target is null)
{
    Console.Error.WriteLine("Usage: --source <legacy connection string> --target <new connection string> [--files <App_File path>] [--storage <path>] [--report <path>] [--dry-run]");
    return 2;
}
var options = new ImportOptions(source, target, Arg("--files"), Arg("--storage") ?? "App_Data/files", Arg("--report") ?? "import-report", a.Contains("--dry-run"));
try
{
    var result = await new LegacyImporter(options).RunAsync();
    foreach (var (table, (legacy, imported)) in result.Counts) Console.WriteLine($"{table,-26} legacy {legacy,7}  imported {imported,7}");
    Console.WriteLine($"{result.Notes.Count} notes. Report: {Path.GetFullPath(result.ReportPath)}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Import failed and was rolled back: " + ex.Message);
    return 1;
}
