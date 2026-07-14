using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace AIMS.Infrastructure.FileTransfer;

public class FileUploadHelper
{
    private static readonly string[] AllowedImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];
    private const long MaxPictureSizeBytes = 2 * 1024 * 1024;

    public (bool IsValid, string Error) ValidatePicture(IFormFile file)
    {
        if (file.Length > MaxPictureSizeBytes)
            return (false, "File size must not exceed 2 MB.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(ext))
            return (false, "Only image files (.jpg, .jpeg, .png, .gif, .webp) are allowed.");

        return (true, string.Empty);
    }

    public async Task<string> SaveAssetPictureAsync(IFormFile file, string webRootPath)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var dir = Path.Combine(webRootPath, "asset-pictures");
        Directory.CreateDirectory(dir);
        var fileName = $"{Guid.NewGuid()}{ext}";
        using var stream = new FileStream(Path.Combine(dir, fileName), FileMode.Create);
        await file.CopyToAsync(stream);
        return $"/asset-pictures/{fileName}";
    }

    public void DeleteFile(string webRelativePath, string webRootPath)
    {
        if (string.IsNullOrEmpty(webRelativePath)) return;
        var segments = webRelativePath.TrimStart('/').Split('/');
        var fullPath = Path.GetFullPath(Path.Combine([webRootPath, .. segments]));

        // Containment check: canonicalize and refuse anything that resolves outside
        // webRootPath (e.g. a "/../appsettings.json" traversal). Current callers only
        // pass server-generated paths from the DB, but this function must not rely
        // on every future caller upholding that invariant.
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(webRootPath));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return;

        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }

    public async Task<string> SaveFileAsync(Stream file, string pathToUpload, string filename)
    {
        Directory.CreateDirectory(pathToUpload);
        var fullPath = Path.Combine(pathToUpload, filename);
        using var fileStream = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(fileStream);
        return fullPath;
    }

    public string SaveFile(Stream file, string pathToUpload, string filename)
    {
        Directory.CreateDirectory(pathToUpload);
        var fullPath = Path.Combine(pathToUpload, filename);
        using var fileStream = new FileStream(fullPath, FileMode.Create);
        file.CopyTo(fileStream);
        return fullPath;
    }

    public string GetFileName(string fileName, bool buildUniqueName)
    {
        var ext = Path.GetExtension(fileName).ToLower();
        return buildUniqueName ? $"{GetUniqueName("doc")}{ext}" : fileName;
    }

    public string GetUniqueName(string prefix) =>
        $"{prefix}{Guid.NewGuid()}{DateTime.Now:yyyyMMddHHmmss}";

    public string GetFileExtension(string fileName) =>
        fileName is not null ? Path.GetExtension(fileName).ToLower() : string.Empty;

    public string GetContentType(string path)
    {
        var mimeTypes = new Dictionary<string, string>
        {
            { ".txt",  "text/plain" },
            { ".pdf",  "application/pdf" },
            { ".doc",  "application/vnd.ms-word" },
            { ".docx", "application/vnd.ms-word" },
            { ".xls",  "application/vnd.ms-excel" },
            { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
            { ".png",  "image/png" },
            { ".jpg",  "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".gif",  "image/gif" },
            { ".csv",  "text/csv" }
        };
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return mimeTypes.TryGetValue(ext, out var type) ? type : "application/octet-stream";
    }
}
