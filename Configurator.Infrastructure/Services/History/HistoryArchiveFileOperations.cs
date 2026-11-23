namespace Configurator.Infrastructure.Services.History;

/// <summary>Local filesystem boundary, also used to reproduce publication failures in tests.</summary>
public class HistoryArchiveFileOperations
{
    public virtual FileStream Create(string path) => new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);

    public virtual void Move(string temporaryPath, string finalPath) => File.Move(temporaryPath, finalPath, overwrite: false);

    public virtual void Published(string finalPath) { }

    public virtual void Delete(string path) => File.Delete(path);

    public virtual long GetAvailableBytes(string directory)
    {
        // On Unix DriveInfo passes this directory to statvfs, including its mounted filesystem and symlinks.
        var drive = new DriveInfo(Path.GetFullPath(directory));
        if (OperatingSystem.IsWindows() && drive.DriveType == DriveType.Network)
            throw new IOException("Выберите локальный диск для архива истории.");
        return drive.AvailableFreeSpace;
    }
}
