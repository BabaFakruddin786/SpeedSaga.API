using Microsoft.Extensions.Options;
using SpeedSaga.API.Infrastructure;

namespace SpeedSaga.API.Services;

public record LogFileInfo(string Name, string Date, long SizeBytes, DateTime LastModifiedUtc);

public record LogSourceInfo(string Source, string Label, string Directory, IReadOnlyList<LogFileInfo> Files);

public record LogEntryDto(
    int LineNo,
    string Raw,
    string? Timestamp,
    string? Category,
    string? Level,
    string? EventName,
    string? Detail);

public interface ILogViewerService
{
    IReadOnlyList<LogSourceInfo> ListSources();
    IReadOnlyList<LogEntryDto> ReadEntries(string source, string? date, string logType, int tail, string? level, string? category, string? search, Guid? playerId);
    Task<int> AppendAppUploadAsync(Guid playerId, IReadOnlyList<string> lines, CancellationToken ct = default);
}

public class LogViewerService : ILogViewerService
{
    static readonly string[] AllowedLogTypes = ["speedsaga", "payments"];

    readonly LogViewerOptions _options;
    readonly string _contentRoot;

    public LogViewerService(IOptions<LogViewerOptions> options, IWebHostEnvironment env)
    {
        _options = options.Value;
        _contentRoot = env.ContentRootPath;
    }

    public IReadOnlyList<LogSourceInfo> ListSources()
    {
        var list = new List<LogSourceInfo>
        {
            BuildSource("api", "API logs", ResolveApiLogDirectory())
        };

        var appDir = ResolveAppLogDirectory();
        if (Directory.Exists(appDir))
            list.Add(BuildSource("app", "App logs (local)", appDir));

        var uploadRoot = ResolveUploadRoot();
        if (Directory.Exists(uploadRoot))
            list.Add(BuildSource("app-uploads", "App logs (uploaded)", uploadRoot, recursive: true));

        return list;
    }

    public IReadOnlyList<LogEntryDto> ReadEntries(string source, string? date, string logType, int tail, string? level, string? category, string? search, Guid? playerId)
    {
        source = (source ?? "api").Trim().ToLowerInvariant();
        logType = NormalizeLogType(logType);
        tail = tail is < 1 or > 2000 ? _options.MaxLines : tail;

        var files = ResolveFiles(source, date, logType, playerId);
        if (files.Count == 0) return Array.Empty<LogEntryDto>();

        var entries = new List<LogEntryDto>();
        foreach (var file in files.OrderBy(f => f))
        {
            if (!File.Exists(file)) continue;
            entries.AddRange(ReadFile(file));
        }

        if (!string.IsNullOrWhiteSpace(level))
        {
            var lvl = level.Trim().ToUpperInvariant();
            entries = entries.Where(e => string.Equals(e.Level, lvl, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim();
            entries = entries.Where(e => string.Equals(e.Category, cat, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            entries = entries.Where(e =>
                (e.Raw?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.EventName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Detail?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        return entries.TakeLast(tail).Select((e, i) => e with { LineNo = i + 1 }).ToList();
    }

    public Task<int> AppendAppUploadAsync(Guid playerId, IReadOnlyList<string> lines, CancellationToken ct = default)
    {
        if (lines.Count == 0) return Task.FromResult(0);
        if (lines.Count > 200) throw new InvalidOperationException("Too many log lines in one upload (max 200).");

        var dir = Path.Combine(ResolveUploadRoot(), playerId.ToString("D"));
        Directory.CreateDirectory(dir);
        var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var path = Path.Combine(dir, $"speedsaga_{day}.log");

        var sanitized = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => AppFileLogger.Sanitize(l.Trim()))
            .Where(l => l.Length > 0)
            .Select(l => l.Length > 900 ? l[..900] + "..." : l)
            .ToList();

        if (sanitized.Count == 0) return Task.FromResult(0);

        File.AppendAllLines(path, sanitized);
        return Task.FromResult(sanitized.Count);
    }

    LogSourceInfo BuildSource(string source, string label, string directory, bool recursive = false)
    {
        var files = new List<LogFileInfo>();
        if (Directory.Exists(directory))
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.GetFiles(directory, "*.log", option).OrderByDescending(File.GetLastWriteTimeUtc))
            {
                var name = Path.GetFileName(file);
                var info = new FileInfo(file);
                var date = TryParseLogDate(name) ?? info.LastWriteTimeUtc.ToString("yyyy-MM-dd");
                files.Add(new LogFileInfo(name, date, info.Length, info.LastWriteTimeUtc));
            }
        }

        return new LogSourceInfo(source, label, directory, files);
    }

    List<string> ResolveFiles(string source, string? date, string logType, Guid? playerId)
    {
        var pattern = $"{logType}_*.log";
        var files = new List<string>();

        switch (source)
        {
            case "api":
                files.AddRange(SafeGlob(ResolveApiLogDirectory(), pattern));
                break;
            case "app":
                files.AddRange(SafeGlob(ResolveAppLogDirectory(), pattern));
                break;
            case "app-uploads":
                var root = ResolveUploadRoot();
                if (playerId.HasValue)
                    files.AddRange(SafeGlob(Path.Combine(root, playerId.Value.ToString("D")), pattern));
                else
                    files.AddRange(Directory.Exists(root)
                        ? Directory.GetFiles(root, pattern, SearchOption.AllDirectories)
                        : Array.Empty<string>());
                break;
            default:
                return files;
        }

        if (!string.IsNullOrWhiteSpace(date))
        {
            var target = $"{logType}_{date.Trim()}.log";
            files = files.Where(f => string.Equals(Path.GetFileName(f), target, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static List<string> SafeGlob(string directory, string pattern)
    {
        if (!Directory.Exists(directory)) return new List<string>();
        try
        {
            return Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    static List<LogEntryDto> ReadFile(string path)
    {
        var lines = File.ReadAllLines(path);
        var list = new List<LogEntryDto>(lines.Length);
        for (var i = 0; i < lines.Length; i++)
            list.Add(ParseLine(i + 1, lines[i]));
        return list;
    }

    static LogEntryDto ParseLine(int lineNo, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new LogEntryDto(lineNo, raw, null, null, null, null, null);

        var parts = raw.Split(" | ", 5);
        if (parts.Length < 4)
            return new LogEntryDto(lineNo, raw, null, null, null, null, raw);

        return new LogEntryDto(
            lineNo,
            raw,
            parts[0],
            parts[1],
            parts[2],
            parts[3],
            parts.Length > 4 ? parts[4] : null);
    }

    string ResolveApiLogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiLogDirectory))
            return Path.GetFullPath(Path.Combine(_contentRoot, _options.ApiLogDirectory));
        return AppFileLogger.LogDirectory;
    }

    string ResolveAppLogDirectory()
    {
        var configured = _options.AppLogDirectory;
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine("..", "SpeedSaga.Unity", "Logs");
        return Path.GetFullPath(Path.Combine(_contentRoot, configured));
    }

    string ResolveUploadRoot() =>
        Path.GetFullPath(Path.Combine(_contentRoot, _options.AppUploadDirectory));

    static string NormalizeLogType(string? logType)
    {
        var t = (logType ?? "speedsaga").Trim().ToLowerInvariant();
        return AllowedLogTypes.Contains(t) ? t : "speedsaga";
    }

    static string? TryParseLogDate(string fileName)
    {
        var parts = fileName.Split('_', '.');
        if (parts.Length < 2) return null;
        return DateTime.TryParse(parts[^2], out _) ? parts[^2] : null;
    }
}
