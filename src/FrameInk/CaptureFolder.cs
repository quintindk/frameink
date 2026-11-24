using System;
using System.IO;
using System.Text.Json;

namespace FrameInk;

internal static class CaptureFolder
{
    private static readonly string Profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameInk");
    private static readonly string Preference = Path.Combine(Profile, "save-folder.json");
    public static string Default => Path.Combine(Profile, "Captures");

    public static string Load() => File.Exists(Preference)
        ? Normalise(JsonSerializer.Deserialize<string>(File.ReadAllText(Preference)) ?? "")
        : Default;

    public static void Save(string folder)
    {
        Directory.CreateDirectory(Profile);
        string temp = Preference + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(folder));
            File.Move(temp, Preference, true);
        }
        finally { Capture.RemoveStagingFile(temp); }
    }

    public static string Ensure(string path)
    {
        string folder = Normalise(path);
        if (new DriveInfo(Path.GetPathRoot(folder)!).DriveType != DriveType.Fixed)
            throw new IOException("Choose a folder on a local fixed drive.");

        foreach (string variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            if (Environment.GetEnvironmentVariable(variable) is not { Length: > 0 } cloud) continue;
            string relative = Path.GetRelativePath(cloud, folder);
            if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith("..\\", StringComparison.Ordinal)))
                throw new IOException("Choose a folder outside OneDrive to keep captures local.");
        }

        CheckDirectories(folder);
        Directory.CreateDirectory(folder);
        CheckDirectories(folder);
        return folder;
    }

    private static string Normalise(string path)
    {
        path = path.Trim();
        string? root = Path.GetPathRoot(path);
        if (!Path.IsPathFullyQualified(path) || root is not { Length: 3 } || root[1] != ':')
            throw new IOException("Choose an absolute Windows folder on a local drive, not a network path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void CheckDirectories(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(directory.FullName); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Capture directory must not be a link or cloud placeholder: {directory.FullName}");
        }
    }
}
