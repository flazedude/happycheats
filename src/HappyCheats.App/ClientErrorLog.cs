using System.IO;

namespace HappyCheats.App;

internal static class ClientErrorLog
{
    internal static void Write(string source, string message, string? directory = null)
    {
        try
        {
            directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HappyCheats");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "client-errors.log"),
                $"{DateTimeOffset.UtcNow:O} [{source}] {message.ReplaceLineEndings(" ")}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
