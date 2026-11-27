using System.IO;

namespace ClimaPulse.Desktop.Ui;

/// <summary>Appends errors to %LOCALAPPDATA%\ClimaPulse\logs\error.log for the developer (contract 7). Never throws.</summary>
public static class ErrorLog
{
    public static void Write(object? exception)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClimaPulse",
                "logs"
            );

            Directory.CreateDirectory(folder);

            // ToString includes inner exceptions, so Open-Meteo's own reason (#29) is kept.
            File.AppendAllText(
                Path.Combine(folder, "error.log"),
                $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ} {exception}{Environment.NewLine}{Environment.NewLine}"
            );
        }
        catch (Exception)
        {
            // Logging must never crash the app.
        }
    }
}
