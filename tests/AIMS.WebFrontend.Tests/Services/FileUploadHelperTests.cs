using AIMS.Infrastructure.FileTransfer;

namespace AIMS.WebFrontend.Tests.Services;

/// <summary>
/// DeleteFile must never escape the web root, whatever the stored path claims.
/// </summary>
public class FileUploadHelperDeleteFileTests : IDisposable
{
    private readonly string _tempRoot;     // parent — stands in for content root
    private readonly string _webRoot;      // child  — stands in for wwwroot
    private readonly FileUploadHelper _helper = new();

    public FileUploadHelperDeleteFileTests()
    {
        _tempRoot = Directory.CreateTempSubdirectory("aims-delete-test-").FullName;
        _webRoot = Path.Combine(_tempRoot, "wwwroot");
        Directory.CreateDirectory(_webRoot);
    }

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    [Fact]
    public void DeletesFileInsideWebRoot()
    {
        var dir = Path.Combine(_webRoot, "asset-pictures");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "pic.jpg");
        File.WriteAllText(file, "x");

        _helper.DeleteFile("/asset-pictures/pic.jpg", _webRoot);

        Assert.False(File.Exists(file));
    }

    [Fact]
    public void RefusesTraversalOutsideWebRoot()
    {
        // Sensitive file next to wwwroot, like appsettings.json in the content root.
        var outside = Path.Combine(_tempRoot, "appsettings.json");
        File.WriteAllText(outside, "secret");

        _helper.DeleteFile("/../appsettings.json", _webRoot);
        _helper.DeleteFile("/asset-pictures/../../appsettings.json", _webRoot);

        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void RefusesDeepTraversal()
    {
        var outside = Path.Combine(_tempRoot, "victim.txt");
        File.WriteAllText(outside, "x");
        var traversal = string.Concat(Enumerable.Repeat("../", 8)) +
                        Path.GetRelativePath("/", _tempRoot).Replace('\\', '/') + "/victim.txt";

        _helper.DeleteFile("/" + traversal, _webRoot);

        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void RefusesSiblingDirectoryWithWebRootPrefix()
    {
        // "wwwroot-evil" starts with "wwwroot" — a naive StartsWith on the raw
        // root string would let this through.
        var sibling = Path.Combine(_tempRoot, "wwwroot-evil");
        Directory.CreateDirectory(sibling);
        var file = Path.Combine(sibling, "f.txt");
        File.WriteAllText(file, "x");

        _helper.DeleteFile("/../wwwroot-evil/f.txt", _webRoot);

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void NullOrEmptyPath_IsANoOp()
    {
        _helper.DeleteFile(null!, _webRoot);
        _helper.DeleteFile(string.Empty, _webRoot);
    }
}
