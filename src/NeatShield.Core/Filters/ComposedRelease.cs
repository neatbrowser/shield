using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using NeatShield.Core.Logging;

namespace NeatShield.Core.Filters;

/// <summary>
/// The user's own rules are added to a release by building a second folder that holds the same files plus
/// <c>custom.txt</c> and a manifest that lists it. The files are hard-linked (copied where that is not possible),
/// so this is quick and takes no extra space. The release folder itself is never changed.
/// </summary>
public static class ComposedRelease
{
    public const string CustomFileName = "custom.txt";
    private const string CompleteMarker = ".complete";

    /// <summary>Returns the folder to load. Same folder as <paramref name="releaseDir"/> when there are no custom rules.</summary>
    public static string Build(string releaseDir, string customRules, string composedRoot)
    {
        if (string.IsNullOrWhiteSpace(customRules)) return releaseDir;

        var manifestBytes = File.ReadAllBytes(Path.Combine(releaseDir, "manifest.json"));
        var text = customRules.Replace("\r\n", "\n").Replace('\r', '\n');
        var id = Hash(manifestBytes, text);
        Directory.CreateDirectory(composedRoot);
        var target = Path.Combine(composedRoot, id);

        if (!File.Exists(Path.Combine(target, CompleteMarker)))
        {
            var tmp = Path.Combine(composedRoot, ".tmp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                foreach (var file in Directory.GetFiles(releaseDir))
                {
                    var name = Path.GetFileName(file);
                    if (name == "manifest.json") continue;
                    LinkOrCopy(file, Path.Combine(tmp, name));
                }

                File.WriteAllText(Path.Combine(tmp, CustomFileName), text, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(tmp, "manifest.json"), ComposeManifest(manifestBytes, text), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(tmp, CompleteMarker), "");

                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                Directory.Move(tmp, target);
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
        }

        Prune(composedRoot, id);
        return target;
    }

    /// <summary>Original manifest plus a <c>custom.txt</c> entry. The text's hash is in the manifest so the engine cache key follows it.</summary>
    internal static string ComposeManifestForTest(byte[] manifestBytes, string customText) => ComposeManifest(manifestBytes, customText);

    internal static string ComposeManifest(byte[] manifestBytes, string customText)
    {
        var root = JsonNode.Parse(manifestBytes)?.AsObject() ?? throw new InvalidDataException("manifest.json is not an object");
        var files = root["files"]?.AsArray() ?? throw new InvalidDataException("manifest.json has no files");
        files.Add(new JsonObject
        {
            ["name"] = CustomFileName,
            ["kind"] = "list",
            ["role"] = "network",
            ["trusted"] = false,
        });
        root["customSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(customText))).ToLowerInvariant();
        return root.ToJsonString();
    }

    private static string Hash(byte[] manifestBytes, string text)
    {
        using var sha = SHA256.Create();
        sha.TransformBlock(manifestBytes, 0, manifestBytes.Length, null, 0);
        var sep = new byte[] { 0 };
        sha.TransformBlock(sep, 0, 1, null, 0);
        var t = Encoding.UTF8.GetBytes(text);
        sha.TransformFinalBlock(t, 0, t.Length);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant()[..20];
    }

    private static void Prune(string root, string keep)
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            if (Path.GetFileName(dir) != keep) TryDelete(dir);
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { Log.Warn($"could not delete {dir}: {ex.Message}"); }
    }

    private static void LinkOrCopy(string source, string destination)
    {
        if (OperatingSystem.IsWindows() && CreateHardLink(destination, source, IntPtr.Zero)) return;
        File.Copy(source, destination);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);
}
