using Microsoft.VisualBasic.FileIO;
using PicView.Core.DebugTools;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

namespace PicView.Core.WindowsNT.FileHandling;

public static class WinFileHelper
{
    public static bool DeleteFile(string filePath, bool moveToRecycleBin)
    {
        try
        {
            var recycleOption = moveToRecycleBin ? RecycleOption.SendToRecycleBin : RecycleOption.DeletePermanently;
            FileSystem.DeleteFile(filePath, UIOption.AllDialogs, recycleOption);
            return File.Exists(filePath);
        }
        catch (Exception e)
        {
            DebugHelper.LogDebug(nameof(WinFileHelper), nameof(DeleteFile), e);
            return false;
        }
    }

    /// <summary>
    /// Restores a file previously sent to the recycle bin back to its original location.
    /// Reads the recycle bin's "$I..." metadata files directly (deterministic, locale-independent)
    /// to find the entry whose recorded original path matches <paramref name="originalPath"/>,
    /// then moves the paired "$R..." data file back and removes the metadata.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool RestoreFromRecycleBin(string originalPath)
    {
        if (File.Exists(originalPath))
        {
            return true;
        }

        try
        {
            var recycleRoots = GetRecycleBinRoots();
            if (recycleRoots.Count == 0)
            {
                return false;
            }

            foreach (var root in recycleRoots)
            {
                IEnumerable<string> metaFiles;
                try
                {
                    metaFiles = Directory.EnumerateFiles(root, "$I*");
                }
                catch (Exception ex)
                {
                    DebugHelper.LogDebug(nameof(WinFileHelper), nameof(RestoreFromRecycleBin), ex);
                    continue;
                }

                foreach (var metaFile in metaFiles)
                {
                    var recordedPath = ReadOriginalPathFromMetadata(metaFile);
                    if (string.IsNullOrEmpty(recordedPath))
                    {
                        continue;
                    }

                    if (!string.Equals(recordedPath, originalPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // The paired data file has the same name but "$R" instead of "$I".
                    var fileName = Path.GetFileName(metaFile);
                    var dataFile = Path.Combine(root, "$R" + fileName[2..]);
                    if (!File.Exists(dataFile))
                    {
                        continue;
                    }

                    var destDir = Path.GetDirectoryName(originalPath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    File.Move(dataFile, originalPath, overwrite: false);
                    try { File.Delete(metaFile); } catch { /* best effort */ }

                    return File.Exists(originalPath);
                }
            }

            return false;
        }
        catch (Exception e)
        {
            DebugHelper.LogDebug(nameof(WinFileHelper), nameof(RestoreFromRecycleBin), e);
            return false;
        }
    }

    /// <summary>
    /// Returns the per-user recycle bin folders across all fixed drives (e.g.
    /// "C:\$Recycle.Bin\&lt;SID&gt;").
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static List<string> GetRecycleBinRoots()
    {
        var roots = new List<string>();
        string? sid = null;
        try
        {
            sid = WindowsIdentity.GetCurrent().User?.Value;
        }
        catch (Exception ex)
        {
            DebugHelper.LogDebug(nameof(WinFileHelper), nameof(GetRecycleBinRoots), ex);
        }

        if (string.IsNullOrEmpty(sid))
        {
            return roots;
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                {
                    continue;
                }

                var candidate = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
                if (Directory.Exists(candidate))
                {
                    roots.Add(candidate);
                }
            }
            catch (Exception ex)
            {
                DebugHelper.LogDebug(nameof(WinFileHelper), nameof(GetRecycleBinRoots), ex);
            }
        }

        return roots;
    }

    /// <summary>
    /// Parses a recycle bin "$I..." metadata file to extract the original full path of the
    /// deleted file. Supports the Vista+ (header 1) and Windows 10+ (header 2) formats.
    /// </summary>
    private static string? ReadOriginalPathFromMetadata(string metadataFile)
    {
        try
        {
            var bytes = File.ReadAllBytes(metadataFile);
            if (bytes.Length < 24)
            {
                return null;
            }

            var header = BitConverter.ToInt64(bytes, 0);

            // Format 2 (Windows 10+): [0-7] header=2, [8-15] size, [16-23] delete time,
            // [24-27] path length (chars incl. null), [28..] UTF-16LE path.
            if (header == 2)
            {
                if (bytes.Length < 28)
                {
                    return null;
                }

                var charCount = BitConverter.ToInt32(bytes, 24);
                var byteCount = (charCount - 1) * 2; // exclude null terminator
                if (byteCount <= 0 || 28 + byteCount > bytes.Length)
                {
                    return null;
                }

                return Encoding.Unicode.GetString(bytes, 28, byteCount);
            }

            // Format 1 (Vista - Win8.1): [0-7] header=1, [8-15] size, [16-23] delete time,
            // [24..] fixed 260-char (520-byte) UTF-16LE path.
            if (header == 1)
            {
                if (bytes.Length < 24 + 520)
                {
                    // Some files may be shorter; read whatever is available.
                    var available = bytes.Length - 24;
                    if (available <= 0)
                    {
                        return null;
                    }
                    return TrimAtNull(Encoding.Unicode.GetString(bytes, 24, available));
                }

                return TrimAtNull(Encoding.Unicode.GetString(bytes, 24, 520));
            }

            return null;
        }
        catch (Exception ex)
        {
            DebugHelper.LogDebug(nameof(WinFileHelper), nameof(ReadOriginalPathFromMetadata), ex);
            return null;
        }
    }

    private static string TrimAtNull(string value)
    {
        var nullIndex = value.IndexOf('\0');
        return nullIndex >= 0 ? value[..nullIndex] : value;
    }
}
