using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using Reprise.Platform.Windows;

/// <summary>Verifies the actual installer handoff independently of the network transport.</summary>
internal static class UpdateHandoff
{
    /// <summary>Validates a transferred installer, then starts the production helper and exits.</summary>
    /// <param name="metadataPath">Manifest downloaded and verified on the development host.</param>
    /// <param name="installerPath">The corresponding actual installer.</param>
    internal static void Verify(string metadataPath, string installerPath)
    {
        var metadata = JsonSerializer.Deserialize<WindowsUpdateManifest>(File.ReadAllText(metadataPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        using var bytes = File.OpenRead(installerPath);
        if (bytes.Length != metadata.Size || !Convert.ToHexString(SHA256.HashData(bytes))
            .Equals(metadata.Sha256, StringComparison.OrdinalIgnoreCase) ||
            FileVersionInfo.GetVersionInfo(installerPath).ProductVersion != metadata.Version)
        {
            throw new InvalidDataException("Installer validation failed.");
        }
        using var key = Registry.CurrentUser.OpenSubKey(WindowsInstallation.RegistryPath)!;
        var installed = new WindowsInstallation((string)key.GetValue("Version")!,
            (string)key.GetValue("BuildNumber")!, (string)key.GetValue("InstallLocation")!);
        // The test process is the parent that exits. Close the already-running Reprise before this check.
        WindowsInstallerHandoff.Start(installerPath, installed, metadata.Version, metadata.BuildNumber);
    }
}
