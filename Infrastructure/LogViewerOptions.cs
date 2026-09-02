namespace SpeedSaga.API.Infrastructure;

public class LogViewerOptions
{
    public const string SectionName = "LogViewer";

    /// <summary>API log directory. Defaults to {ContentRoot}/Logs when empty.</summary>
    public string? ApiLogDirectory { get; set; }

    /// <summary>Unity editor / local app logs (e.g. ../SpeedSaga.Unity/Logs).</summary>
    public string? AppLogDirectory { get; set; }

    /// <summary>Uploaded mobile app logs stored under this folder.</summary>
    public string AppUploadDirectory { get; set; } = "Logs/app-uploads";

    public int MaxLines { get; set; } = 500;
}
