using PicView.Core.DebugTools;

namespace PicView.Avalonia.FileSystem;

/// <summary>
/// Records a single delete operation so it can be undone (Ctrl+Z).
/// </summary>
public sealed class DeleteHistoryEntry
{
    /// <summary>Original full path of the deleted image.</summary>
    public required string ImagePath { get; init; }

    /// <summary>True if the image was sent to the recycle bin (restorable), false if permanently deleted.</summary>
    public required bool Recycled { get; init; }

    /// <summary> Original full path of the associated video, if any.</summary>
    public string? VideoOriginalPath { get; init; }

    /// <summary>Path the associated video was moved to inside the "deleted" folder, if any.</summary>
    public string? VideoDeletedPath { get; init; }

    /// <summary>
    /// Path the image itself was moved to inside the "deleted" folder, when the move-to-deleted
    /// -folder mode is used instead of a recycle-bin delete. Null for recycle-bin deletes.
    /// </summary>
    public string? ImageDeletedPath { get; init; }
}

/// <summary>
/// Centralizes image deletion so an associated video is moved to a "deleted" folder and the
/// operation is recorded for undo. Undo restores the image from the recycle bin and moves the
/// companion video back to its original location.
/// </summary>
public static class DeleteManager
{
    public const string DeletedFolderName = "deleted";

    private static readonly Stack<DeleteHistoryEntry> History = new();
    private static readonly object HistoryLock = new();

    /// <summary>
    /// Moves the companion video (if any) of <paramref name="imagePath"/> into a "deleted"
    /// subfolder and records a history entry for undo. Call this immediately before the image
    /// itself is deleted.
    /// </summary>
    public static void MoveCompanionVideoAndRecord(string imagePath, bool recycle)
    {
        // A deletion invalidates any pending label undo: a label can only be undone while it
        // is still the most recent action.
        LabelUndoManager.Clear();

        try
        {
            string? videoOriginal = null;
            string? videoDeleted = null;

            var companion = LabelHelper.GetExistingCompanionVideoPath(imagePath);
            if (companion is not null)
            {
                videoOriginal = companion;
                videoDeleted = MoveToDeletedFolder(companion);
            }

            lock (HistoryLock)
            {
                History.Push(new DeleteHistoryEntry
                {
                    ImagePath = imagePath,
                    Recycled = recycle,
                    VideoOriginalPath = videoOriginal,
                    VideoDeletedPath = videoDeleted
                });
            }
        }
        catch (Exception ex)
        {
            DebugHelper.LogDebug(nameof(DeleteManager), nameof(MoveCompanionVideoAndRecord), ex);
        }
    }

    /// <summary>
    /// Moves the image (and its companion video, if any) into a "deleted" subfolder instead of
    /// deleting it, and records a history entry for undo. Returns true if the image was moved.
    /// </summary>
    public static bool MoveImageToDeletedFolderAndRecord(string imagePath)
    {
        // A deletion invalidates any pending label undo: a label can only be undone while it
        // is still the most recent action.
        LabelUndoManager.Clear();

        try
        {
            string? videoOriginal = null;
            string? videoDeleted = null;

            var companion = LabelHelper.GetExistingCompanionVideoPath(imagePath);
            if (companion is not null)
            {
                videoOriginal = companion;
                videoDeleted = MoveToDeletedFolder(companion);
            }

            var imageDeleted = MoveToDeletedFolder(imagePath);
            if (imageDeleted is null)
            {
                return false;
            }

            lock (HistoryLock)
            {
                History.Push(new DeleteHistoryEntry
                {
                    ImagePath = imagePath,
                    Recycled = false,
                    VideoOriginalPath = videoOriginal,
                    VideoDeletedPath = videoDeleted,
                    ImageDeletedPath = imageDeleted
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            DebugHelper.LogDebug(nameof(DeleteManager), nameof(MoveImageToDeletedFolderAndRecord), ex);
            return false;
        }
    }

    /// <summary>
    /// Retrieves and removes the most recent delete history entry, if any.
    /// </summary>
    public static DeleteHistoryEntry? PopLast()
    {
        lock (HistoryLock)
        {
            return History.Count > 0 ? History.Pop() : null;
        }
    }

    /// <summary>
    /// Moves the image back from the "deleted" folder to its original location, when it was
    /// deleted via the move-to-deleted-folder mode. Returns true if the image was moved back.
    /// </summary>
    public static bool RestoreImageFromDeletedFolder(DeleteHistoryEntry entry)
    {
        try
        {
            if (entry.ImageDeletedPath is null)
            {
                return false;
            }

            if (!File.Exists(entry.ImageDeletedPath) || File.Exists(entry.ImagePath))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(entry.ImagePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Move(entry.ImageDeletedPath, entry.ImagePath);
            return true;
        }
        catch (Exception ex)
        {
            DebugHelper.LogDebug(nameof(DeleteManager), nameof(RestoreImageFromDeletedFolder), ex);
            return false;
        }
    }

    /// <summary>
    /// Moves the companion video back from the "deleted" folder to its original location.
    /// Returns true if a video was moved back.
    /// </summary>
    public static bool RestoreCompanionVideo(DeleteHistoryEntry entry)
    {
        try
        {
            if (entry.VideoDeletedPath is null || entry.VideoOriginalPath is null)
            {
                return false;
            }

            if (!File.Exists(entry.VideoDeletedPath) || File.Exists(entry.VideoOriginalPath))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(entry.VideoOriginalPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Move(entry.VideoDeletedPath, entry.VideoOriginalPath);
            return true;
        }
        catch (Exception ex)
        {
            DebugHelper.LogDebug(nameof(DeleteManager), nameof(RestoreCompanionVideo), ex);
            return false;
        }
    }

    private static string? MoveToDeletedFolder(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            var deletedFolder = Path.Combine(directory, DeletedFolderName);
            Directory.CreateDirectory(deletedFolder);

            var destination = Path.Combine(deletedFolder, Path.GetFileName(filePath));

            // Avoid overwriting an existing file in the deleted folder.
            if (File.Exists(destination))
            {
                var name = Path.GetFileNameWithoutExtension(filePath);
                var ext = Path.GetExtension(filePath);
                destination = Path.Combine(deletedFolder,
                    $"{name}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
            }

            File.Move(filePath, destination);
            return destination;
        }
        catch (Exception ex)
        {
            DebugHelper.LogDebug(nameof(DeleteManager), nameof(MoveToDeletedFolder), ex);
            return null;
        }
    }
}
