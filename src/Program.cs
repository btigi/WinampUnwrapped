using System.Text.Json;
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
        SELECT title, artist, album, played_at, duration_ms, genre
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
            PlayedAt = reader.IsDBNull(3) ? "" : reader.GetString(3),
            DurationMs = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            Genre = reader.IsDBNull(5) ? "" : reader.GetString(5)
        });
    }
}

if (songs.Count == 0)
{
    Console.WriteLine($"No songs found for year {year}");
    return 0;
}

var stats = CalculateStats(songs);

var outputFile = $"PlayHistory_{year}.html";
var html = GenerateHtml(template, year, songs, stats);
File.WriteAllText(outputFile, html);

Console.WriteLine($"Generated {outputFile} with {songs.Count} songs");
return 0;

static Stats CalculateStats(List<SongInfo> songs)
{
    var topSong = songs
        .GroupBy(s => new { s.Title, s.Artist })
        .OrderByDescending(g => g.Count())
        .Select(g => new TopItem { Name = g.Key.Title, SubName = g.Key.Artist, Count = g.Count() })
        .FirstOrDefault() ?? new TopItem { Name = "N/A", SubName = "", Count = 0 };

    var topArtist = songs
        .GroupBy(s => s.Artist)
        .OrderByDescending(g => g.Count())
        .Select(g => new TopItem { Name = g.Key, Count = g.Count() })
        .FirstOrDefault() ?? new TopItem { Name = "N/A", Count = 0 };

    var topAlbum = songs
        .GroupBy(s => new { s.Album, s.Artist })
        .OrderByDescending(g => g.Count())
        .Select(g => new TopItem { Name = g.Key.Album, SubName = g.Key.Artist, Count = g.Count() })
        .FirstOrDefault() ?? new TopItem { Name = "N/A", SubName = "", Count = 0 };

    var topGenre = songs
        .Where(s => !string.IsNullOrEmpty(s.Genre))
        .GroupBy(s => s.Genre)
        .OrderByDescending(g => g.Count())
        .Select(g => new TopItem { Name = g.Key, Count = g.Count() })
        .FirstOrDefault() ?? new TopItem { Name = "N/A", Count = 0 };

    var totalMs = songs.Sum(s => (long)s.DurationMs);
    var totalHours = totalMs / 3600000.0;
    var totalDays = totalHours / 24.0;

    var topArtists = songs
        .GroupBy(s => s.Artist)
        .OrderByDescending(g => g.Count())
        .Take(10)
        .Select(g => new ChartItem { Label = g.Key, Value = g.Count() })
        .ToList();

    var genreDistribution = songs
        .Where(s => !string.IsNullOrEmpty(s.Genre))
        .GroupBy(s => s.Genre)
        .OrderByDescending(g => g.Count())
        .Take(10)
        .Select(g => new ChartItem { Label = g.Key, Value = g.Count() })
        .ToList();

    var topAlbums = songs
        .GroupBy(s => s.Album)
        .OrderByDescending(g => g.Count())
        .Take(10)
        .Select(g => new ChartItem { Label = g.Key, Value = g.Count() })
        .ToList();

    var monthlyPattern = songs
        .Where(s => !string.IsNullOrEmpty(s.PlayedAt) && s.PlayedAt.Length >= 7)
        .GroupBy(s => s.PlayedAt[..7]) // YYYY-MM
        .OrderBy(g => g.Key)
        .Select(g => new ChartItem { Label = g.Key, Value = g.Count() })
        .ToList();

    return new Stats
    {
        TopSong = topSong,
        TopArtist = topArtist,
        TopAlbum = topAlbum,
        TopGenre = topGenre,
        TotalMs = totalMs,
        TotalHours = totalHours,
        TotalDays = totalDays,
        TopArtists = topArtists,
        GenreDistribution = genreDistribution,
        TopAlbums = topAlbums,
        MonthlyPattern = monthlyPattern
    };
}

static string GenerateHtml(string template, int year, List<SongInfo> songs, Stats stats)
{
    var songRows = string.Join("\n", songs.Select(s => $"""
                <tr>
                    <td>{HtmlEncode(s.Title)}</td>
                    <td>{HtmlEncode(s.Artist)}</td>
                    <td>{HtmlEncode(s.Album)}</td>
                    <td>{HtmlEncode(s.PlayedAt)}</td>
                </tr>
        """));

    var durationString = stats.TotalHours >= 24 ? $"{stats.TotalDays:F1} days ({stats.TotalHours:F0} hours)" : $"{stats.TotalHours:F1} hours";

    var artistLabels = JsonSerializer.Serialize(stats.TopArtists.Select(a => a.Label).ToList());
    var artistData = JsonSerializer.Serialize(stats.TopArtists.Select(a => a.Value).ToList());
    var genreLabels = JsonSerializer.Serialize(stats.GenreDistribution.Select(g => g.Label).ToList());
    var genreData = JsonSerializer.Serialize(stats.GenreDistribution.Select(g => g.Value).ToList());
    var albumLabels = JsonSerializer.Serialize(stats.TopAlbums.Select(a => a.Label).ToList());
    var albumData = JsonSerializer.Serialize(stats.TopAlbums.Select(a => a.Value).ToList());
    var monthLabels = JsonSerializer.Serialize(stats.MonthlyPattern.Select(m => m.Label).ToList());
    var monthData = JsonSerializer.Serialize(stats.MonthlyPattern.Select(m => m.Value).ToList());

    var hasGenre = stats.GenreDistribution.Count > 0;
    var hasDuration = stats.TotalMs > 0;

    return template
        .Replace("{{year}}", year.ToString())
        .Replace("{{songCount}}", songs.Count.ToString())
        .Replace("{{songRows}}", songRows)
        .Replace("{{topSongTitle}}", HtmlEncode(stats.TopSong.Name))
        .Replace("{{topSongArtist}}", HtmlEncode(stats.TopSong.SubName ?? ""))
        .Replace("{{topSongCount}}", stats.TopSong.Count.ToString())
        .Replace("{{topArtistName}}", HtmlEncode(stats.TopArtist.Name))
        .Replace("{{topArtistCount}}", stats.TopArtist.Count.ToString())
        .Replace("{{topAlbumTitle}}", HtmlEncode(stats.TopAlbum.Name))
        .Replace("{{topAlbumArtist}}", HtmlEncode(stats.TopAlbum.SubName ?? ""))
        .Replace("{{topAlbumCount}}", stats.TopAlbum.Count.ToString())
        .Replace("{{topGenreName}}", HtmlEncode(stats.TopGenre.Name))
        .Replace("{{topGenreCount}}", stats.TopGenre.Count.ToString())
        .Replace("{{totalDuration}}", durationString)
        .Replace("{{hasDuration}}", hasDuration.ToString().ToLower())
        .Replace("{{hasGenre}}", hasGenre.ToString().ToLower())
        .Replace("{{artistLabels}}", artistLabels)
        .Replace("{{artistData}}", artistData)
        .Replace("{{genreLabels}}", genreLabels)
        .Replace("{{genreData}}", genreData)
        .Replace("{{albumLabels}}", albumLabels)
        .Replace("{{albumData}}", albumData)
        .Replace("{{monthLabels}}", monthLabels)
        .Replace("{{monthData}}", monthData);
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
    public int DurationMs { get; init; } = 0;
    public string Genre { get; init; } = "";
}

record TopItem
{
    public string Name { get; init; } = "";
    public string? SubName { get; init; }
    public int Count { get; init; }
}

record ChartItem
{
    public string Label { get; init; } = "";
    public int Value { get; init; }
}

record Stats
{
    public TopItem TopSong { get; init; } = new();
    public TopItem TopArtist { get; init; } = new();
    public TopItem TopAlbum { get; init; } = new();
    public TopItem TopGenre { get; init; } = new();
    public long TotalMs { get; init; }
    public double TotalHours { get; init; }
    public double TotalDays { get; init; }
    public List<ChartItem> TopArtists { get; init; } = [];
    public List<ChartItem> GenreDistribution { get; init; } = [];
    public List<ChartItem> TopAlbums { get; init; } = [];
    public List<ChartItem> MonthlyPattern { get; init; } = [];
}