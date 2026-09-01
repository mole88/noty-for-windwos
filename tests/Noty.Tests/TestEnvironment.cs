using Microsoft.Data.Sqlite;
using NUnit.Framework;
using System.IO;

[assembly: LevelOfParallelism(1)]

namespace Noty.Tests;

[SetUpFixture]
public sealed class TestEnvironment
{
    private static string? _root;

    public static string Root => _root ?? throw new InvalidOperationException("Test environment is not ready");

    [OneTimeSetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "Noty.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("NOTY_DATA_DIR", _root);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        Environment.SetEnvironmentVariable("NOTY_DATA_DIR", null);
        if (_root is not null && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
