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
var html = GenerateHtml(year, songs);
File.WriteAllText(outputFile, html);

Console.WriteLine($"Generated {outputFile} with {songs.Count} songs");
return 0;

static string GenerateHtml(int year, List<SongInfo> songs)
{
    var songRows = string.Join("\n", songs.Select(s => $"""
                <tr>
                    <td>{HtmlEncode(s.Title)}</td>
                    <td>{HtmlEncode(s.Artist)}</td>
                    <td>{HtmlEncode(s.Album)}</td>
                    <td>{HtmlEncode(s.PlayedAt)}</td>
                </tr>
        """));

    return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Play History - {{year}}</title>
            <style>
                * {
                    margin: 0;
                    padding: 0;
                    box-sizing: border-box;
                }
                body {
                    font-family: 'Segoe UI', system-ui, sans-serif;
                    background: linear-gradient(135deg, #1a1a2e 0%, #16213e 50%, #0f3460 100%);
                    min-height: 100vh;
                    padding: 2rem;
                    color: #e4e4e7;
                }
                .container {
                    max-width: 1200px;
                    margin: 0 auto;
                }
                h1 {
                    text-align: center;
                    font-size: 2.5rem;
                    margin-bottom: 0.5rem;
                    background: linear-gradient(90deg, #f472b6, #818cf8);
                    -webkit-background-clip: text;
                    -webkit-text-fill-color: transparent;
                    background-clip: text;
                }
                .subtitle {
                    text-align: center;
                    color: #a1a1aa;
                    margin-bottom: 2rem;
                    font-size: 1.1rem;
                }
                table {
                    width: 100%;
                    border-collapse: collapse;
                    background: rgba(255, 255, 255, 0.05);
                    border-radius: 12px;
                    overflow: hidden;
                    backdrop-filter: blur(10px);
                }
                th, td {
                    padding: 1rem 1.25rem;
                    text-align: left;
                }
                th {
                    background: rgba(129, 140, 248, 0.2);
                    font-weight: 600;
                    text-transform: uppercase;
                    font-size: 0.85rem;
                    letter-spacing: 0.05em;
                    color: #c4b5fd;
                }
                tr:nth-child(even) {
                    background: rgba(255, 255, 255, 0.02);
                }
                tr:hover {
                    background: rgba(129, 140, 248, 0.1);
                }
                td:first-child {
                    font-weight: 500;
                    color: #f9fafb;
                }
            </style>
        </head>
        <body>
            <div class="container">
                <h1>🎵 Play History</h1>
                <p class="subtitle">{{songs.Count}} songs played in {{year}}</p>
                <table>
                    <thead>
                        <tr>
                            <th>Title</th>
                            <th>Artist</th>
                            <th>Album</th>
                            <th>Played At</th>
                        </tr>
                    </thead>
                    <tbody>
        {{songRows}}
                    </tbody>
                </table>
            </div>
        </body>
        </html>
        """;
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