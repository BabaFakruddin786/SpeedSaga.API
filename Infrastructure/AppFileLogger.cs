using System.Text;
using System.Text.RegularExpressions;

namespace SpeedSaga.API.Infrastructure;

/// <summary>
/// File logger matching the Unity AppLogger format.
/// Files: Logs/speedsaga_yyyy-MM-dd.log and Logs/payments_yyyy-MM-dd.log
/// </summary>
public static class AppFileLogger
{
    public enum Category
    {
        App,
        Auth,
        Payment,
        Api,
        Game,
        Admin,
        Kyc,
        Exception
    }

    const int MaxLineLength = 900;
    const int RetentionDays = 10;

    static readonly object Lock = new();
    static string? _logDir;
    static bool _initialized;

    public static string LogDirectory
    {
        get
        {
            EnsureInitialized();
            return _logDir!;
        }
    }

    public static void Initialize(string? logDirectory = null)
    {
        lock (Lock)
        {
            _logDir = string.IsNullOrWhiteSpace(logDirectory)
                ? Path.Combine(Directory.GetCurrentDirectory(), "Logs")
                : Path.GetFullPath(logDirectory);
            Directory.CreateDirectory(_logDir);
            PurgeOldLogs();
            _initialized = true;
        }

        Info(Category.App, "LOGGER_READY", $"dir={_logDir}");
    }

    static void EnsureInitialized()
    {
        if (_initialized) return;
        Initialize();
    }

    static void PurgeOldLogs()
    {
        if (string.IsNullOrEmpty(_logDir) || !Directory.Exists(_logDir)) return;

        var cutoff = DateTime.Today.AddDays(-RetentionDays);
        foreach (var file in Directory.GetFiles(_logDir, "*.log", SearchOption.AllDirectories))
        {
            try
            {
                var name = Path.GetFileName(file);
                if (TryParseLogDate(name, out var fileDate))
                {
                    if (fileDate >= cutoff) continue;
                }
                else if (File.GetLastWriteTime(file).Date >= cutoff)
                {
                    continue;
                }

                File.Delete(file);
            }
            catch
            {
                // Ignore per-file delete failures.
            }
        }
    }

    public static void Info(Category category, string eventName, string? detail = null)
        => Write(category, "INFO", eventName, detail);

    public static void Warn(Category category, string eventName, string? detail = null)
        => Write(category, "WARN", eventName, detail);

    public static void Error(Category category, string eventName, string? detail = null)
        => Write(category, "ERROR", eventName, detail);

    public static void Exception(Category category, string eventName, Exception ex, string? detail = null)
    {
        var msg = string.IsNullOrEmpty(detail) ? ex.Message : detail + " | " + ex.Message;
        Write(category, "ERROR", eventName, msg + " | " + ex.GetType().Name);
    }

    public static void Payment(string eventName, string? detail = null)
        => Write(Category.Payment, "INFO", eventName, detail, paymentsFile: true);

    public static void PaymentError(string eventName, string? detail = null)
        => Write(Category.Payment, "ERROR", eventName, detail, paymentsFile: true);

    public static void Api(string method, string path, int responseCode, string? detail = null, bool isError = false)
    {
        var level = isError ? "ERROR" : "INFO";
        var evt = isError ? "API_FAIL" : "API_OK";
        var msg = $"method={method} path={Sanitize(path)} code={responseCode}";
        if (!string.IsNullOrWhiteSpace(detail))
            msg += " | " + Sanitize(detail);
        Write(Category.Api, level, evt, msg);
    }

    static void Write(Category category, string level, string eventName, string? detail, bool paymentsFile = false)
    {
        EnsureInitialized();
        var line = FormatLine(category, level, eventName, detail);

        lock (Lock)
        {
            try
            {
                var day = DateTime.Now.ToString("yyyy-MM-dd");
                Append(Path.Combine(_logDir!, $"speedsaga_{day}.log"), line);
                if (paymentsFile || category == Category.Payment)
                    Append(Path.Combine(_logDir!, $"payments_{day}.log"), line);
            }
            catch
            {
                // Ignore write failures.
            }
        }
    }

    public static string FormatLine(Category category, string level, string eventName, string? detail)
    {
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var sb = new StringBuilder();
        sb.Append(ts).Append(" | ").Append(category).Append(" | ").Append(level)
            .Append(" | ").Append(eventName);
        if (!string.IsNullOrWhiteSpace(detail))
            sb.Append(" | ").Append(Sanitize(detail));

        var text = sb.ToString();
        if (text.Length > MaxLineLength)
            text = text[..MaxLineLength] + "...";
        return text;
    }

    static void Append(string path, string line) =>
        File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);

    static bool TryParseLogDate(string fileName, out DateTime date)
    {
        date = default;
        var parts = fileName.Split('_', '.');
        if (parts.Length < 2) return false;
        return DateTime.TryParse(parts[^2], out date);
    }

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var s = value.Replace('\r', ' ').Replace('\n', ' ').Trim();

        s = Regex.Replace(s, @"(?i)(password|passwd|otp|otpcode|token|authorization|signature|secret|dev code)\s*[:=]\s*\S+", "$1=***");
        s = Regex.Replace(s, @"(?i)\bdev code:\s*\S+", "dev code=***");
        s = Regex.Replace(s, @"(?i)(Bearer\s+)[A-Za-z0-9\-\._~\+/]+=*", "$1***");
        s = Regex.Replace(s, @"(?i)(paymentId|orderId|order_id|pay_)[=:]\s*(\S+)", m =>
        {
            var id = m.Groups[2].Value;
            if (id.Length <= 8) return m.Groups[1].Value + "=***";
            return m.Groups[1].Value + "=" + id[..4] + "..." + id[^4..];
        });

        if (s.Contains('@'))
        {
            var at = s.IndexOf('@');
            if (at > 2)
                s = s[..2] + "***" + s[at..];
        }

        s = Regex.Replace(s, @"(?<!\d)([6-9]\d{9})(?!\d)", m =>
        {
            var p = m.Value;
            return p[..2] + "****" + p[^2..];
        });

        return s;
    }
}
