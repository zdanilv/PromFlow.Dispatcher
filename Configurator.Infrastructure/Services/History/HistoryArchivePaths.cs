using System.Globalization;
using System.Text.RegularExpressions;
using Configurator.Application.Services.History;

namespace Configurator.Infrastructure.Services.History;

public static class HistoryArchivePaths
{
    public static string GetDirectory(HistoryArchiveOptions options) =>
        string.IsNullOrWhiteSpace(options.Directory)
            ? ApplicationConfigPaths.DefaultHistoryArchiveDirectory
            : Path.GetFullPath(options.Directory);

    public static void Validate(HistoryArchiveOptions options)
    {
        if (options.ArchiveIntervalMinutes is < 15 or > 10080)
            throw new ArgumentException("Частота архивации должна быть от 15 до 10080 минут.");
        if (options.RetentionDays < 0 || options.LocalDatabaseRetentionDays < 1 || options.MinimumFreeSpaceMb < 0)
            throw new ArgumentException("Срок хранения БД должен быть положительным, остальные ограничения — неотрицательными.");
        if (!string.IsNullOrWhiteSpace(options.Directory) &&
            (!Path.IsPathFullyQualified(options.Directory) || options.Directory.StartsWith("\\\\", StringComparison.Ordinal) ||
             options.Directory.StartsWith("//", StringComparison.Ordinal)))
            throw new ArgumentException("Выберите абсолютный путь на локальном диске.");
        if (OperatingSystem.IsWindows() && new DriveInfo(GetDirectory(options)).DriveType == DriveType.Network)
            throw new ArgumentException("Выберите локальный диск для архива истории.");

        var template = options.FileNameTemplate ?? "";
        if (!template.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || template.Length > 180 ||
            template.Contains("..", StringComparison.Ordinal) || template.EndsWith(' ') || template.EndsWith('.'))
            throw new ArgumentException("Укажите безопасное имя файла с расширением .zip.");
        var fileStem = template.Length >= 4 ? template[..^4] : "";
        if (fileStem.Length == 0 || fileStem.StartsWith('.') || fileStem.EndsWith(' ') || fileStem.EndsWith('.'))
            throw new ArgumentException("Укажите непустое имя без точек и завершающих пробелов.");
        var literal = fileStem.Replace("{date}", "", StringComparison.Ordinal).Replace("{seq}", "", StringComparison.Ordinal);
        if (literal.Any(c => !char.IsLetterOrDigit(c) && c is not '_' and not '-' and not ' ') ||
            template.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("В имени допустимы буквы, цифры, пробел, _, - и шаблоны {date}, {seq}.");
        var stem = template.Split('.')[0].TrimEnd(' ');
        if (Regex.IsMatch(stem, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("Это имя зарезервировано операционной системой.");
        _ = GetDirectory(options);
    }

    public static string GetUniquePath(HistoryArchiveOptions options, DateTimeOffset utc, long sequence,
        IEnumerable<string>? reservedPaths = null)
    {
        Validate(options);
        var directory = GetDirectory(options);
        var name = options.FileNameTemplate
            .Replace("{date}", utc.UtcDateTime.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{seq}", sequence.ToString("D6", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var path = Path.GetFullPath(Path.Combine(directory, name));
        if (!string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(directory),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Имя архива выходит за пределы выбранного каталога.");
        var reserved = new HashSet<string>(reservedPaths ?? [],
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        for (var collision = 1; File.Exists(path) || Directory.Exists(path) || reserved.Contains(path); collision++)
            path = Path.Combine(directory, Path.GetFileNameWithoutExtension(name) + "_" + collision.ToString("D3", CultureInfo.InvariantCulture) + ".zip");
        return path;
    }
}
