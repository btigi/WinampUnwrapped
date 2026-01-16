using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

if (args.Length == 0)
{
    Console.WriteLine("Usage: PlayHistoryReport <year>");
    Console.WriteLine("Example: PlayHistoryReport 2025");
    return 1;
}

if (!int.TryParse(args[0], out int year) || year < 2025 || year > 9999)
{
    Console.WriteLine($"Invalid year: {args[0]}");
    return 1;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var databasePath = configuration["DatabasePath"];
if (string.IsNullOrEmpty(databasePath))
{
    Console.WriteLine("DatabasePath not configured in appsettings.json");
    return 1;
}

if (!File.Exists(databasePath))
{
    Console.WriteLine($"Database file not found: {databasePath}");
    return 1;
}

var templatePath = configuration["TemplatePath"] ?? "template.html";
var fullTemplatePath = Path.Combine(AppContext.BaseDirectory, templatePath);
if (!File.Exists(fullTemplatePath))
{
    Console.WriteLine($"Template file not found: {fullTemplatePath}");
    return 1;
}

var template = File.ReadAllText(fullTemplatePath);

var songs = new List<SongInfo>();

using (var connection = new SqliteConnection($"Data Source={databasePath}"))
{
    connection.Open();

    var command = connection.CreateCommand();
    command.CommandText = """
        SELECT title, artist, album, played_at
        FROM play_history
        WHERE played_at LIKE @yearPattern
        ORDER BY played_at DESC
        """;
    command.Parameters.AddWithValue("@yearPattern", $"{year}%");

    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        songs.Add(new SongInfo
        {
            Title = reader.IsDBNull(0) ? "Unknown Title" : reader.GetString(0),
            Artist = reader.IsDBNull(1) ? "Unknown Artist" : reader.GetString(1),
            Album = reader.IsDBNull(2) ? "Unknown Album" : reader.GetString(2),
            PlayedAt = reader.IsDBNull(3) ? "" : reader.GetString(3)
        });
    }
}

if (songs.Count == 0)
{
    Console.WriteLine($"No songs found for year {year}");
    return 0;
}

var outputFile = $"PlayHistory_{year}.html";
var html = GenerateHtml(template, year, songs);
File.WriteAllText(outputFile, html);

Console.WriteLine($"Generated {outputFile} with {songs.Count} songs");
return 0;

static string GenerateHtml(string template, int year, List<SongInfo> songs)
{
    var songRows = string.Join("\n", songs.Select(s => $"""
                <tr>
                    <td>{HtmlEncode(s.Title)}</td>
                    <td>{HtmlEncode(s.Artist)}</td>
                    <td>{HtmlEncode(s.Album)}</td>
                    <td>{HtmlEncode(s.PlayedAt)}</td>
                </tr>
        """));

    return template
        .Replace("{{year}}", year.ToString())
        .Replace("{{songCount}}", songs.Count.ToString())
        .Replace("{{songRows}}", songRows);
}

static string HtmlEncode(string? text)
{
    if (string.IsNullOrEmpty(text)) return "";
    return text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}

record SongInfo
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string PlayedAt { get; init; } = "";
}
