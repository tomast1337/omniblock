using System.Reflection;
using System.Text.Json;
using BetaSharp.DataAsset;

namespace BetaSharp.Tests;

public class DataAssetBugTests : IDisposable
{
    private readonly string _tempDir;

    public DataAssetBugTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        // Force finalization of any leaked FileStream handles (Bug 2) before
        // trying to delete the temp directory, otherwise cleanup fails on Windows.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Bug 1: infinite recursion when lazy asset's JSON deserializes to null
    //
    // Root cause: UnresolvedDataAsset.Asset called _loader.FromJsonReplace then
    // unconditionally returned _parent.Asset. When the JSON file contains "null",
    // FromJson returns null, the static FromJsonReplace overload returns early
    // without calling target.Asset = v, leaving _dataAssetProvider unchanged.
    // The recursive _parent.Asset call re-entered this getter → StackOverflowException.
    //
    // Fix: after FromJsonReplace returns, check if _dataAssetProvider was replaced.
    // If not, throw InvalidOperationException instead of recursing.
    // -------------------------------------------------------------------------

    [Fact]
    public void Lazy_asset_with_null_json_content_throws_instead_of_recursing()
    {
        File.WriteAllText(Path.Combine(_tempDir, "test.json"), "null");

        var loader = new DataAssetLoader<GameMode.GameMode>("gamemode", LoadLocations.Assets);
        var assetRef = new DataAssetRef<GameMode.GameMode>(loader, _tempDir, Namespace.BetaSharp, "test");

        // FromJson returns null for a null JSON element, so FromJsonReplace returns
        // without setting target.Asset. The fix detects that _dataAssetProvider was
        // not replaced and throws instead of recursing infinitely.
        Assert.Throws<InvalidOperationException>(() => _ = assetRef.Asset);
    }

    // -------------------------------------------------------------------------
    // Bug 2: File handle leak when JsonSerializer.Deserialize throws during lazy load
    //
    // Root cause: the internal FromJsonReplace(string, DataAssetRef<T>) method
    // opens a FileStream with File.OpenRead but does not use a using block.
    // If JsonSerializer.Deserialize throws (e.g., malformed JSON), json.Close()
    // is never reached and the file handle is leaked.
    // -------------------------------------------------------------------------

    [Fact]
    public void File_handle_is_leaked_when_json_deserialization_throws_during_lazy_load()
    {
        string jsonPath = Path.Combine(_tempDir, "test.json");
        File.WriteAllText(jsonPath, "{not valid json");

        var loader = new DataAssetLoader<GameMode.GameMode>("gamemode", LoadLocations.Assets);
        var assetRef = new DataAssetRef<GameMode.GameMode>(loader, _tempDir, Namespace.BetaSharp, "test");

        // Triggers the lazy load path: File.OpenRead succeeds, Deserialize throws JsonException,
        // json.Close() is never called because there is no using block.
        Assert.Throws<JsonException>(() => _ = assetRef.Asset);

        // On Windows, File.Delete fails with IOException if any open handle does not
        // have FILE_SHARE_DELETE set. File.OpenRead does not set that flag, so a leaked
        // handle will cause this to throw.
        // Bug:  no using block → handle is still open → IOException
        // Fix:  using block   → handle is closed  → succeeds
        File.Delete(jsonPath);
    }

    // -------------------------------------------------------------------------
    // Bug 3: Wrong directory existence check in LoadDatapackAssets (same pattern
    //        repeated in LoadWorldAssets and LoadResourcepackAssets)
    //
    // Root cause: the code checks !Directory.Exists(pack) instead of
    // !Directory.Exists(assets). Because `pack` was just obtained from
    // Directory.EnumerateDirectories it always exists, so the guard never fires
    // and OnLoadAssets is called with the non-existent "data/" path, faulting
    // the internal async task with DirectoryNotFoundException.
    // -------------------------------------------------------------------------

    [Fact]
    public void LoadDatapackAssets_skips_pack_without_data_directory()
    {
        // Create a datapack folder with no "data/" subdirectory inside it.
        string packDir = Path.Combine(_tempDir, "datapacks", "mypack");
        Directory.CreateDirectory(packDir);

        var testLoader = new DataAssetLoader<GameMode.GameMode>("gamemode", LoadLocations.GameDatapack);

        // Replace s_assetLoaders temporarily so only testLoader is processed,
        // leaving the global GameModesLoader unaffected.
        FieldInfo loadersField = typeof(DataAssetLoader)
            .GetField("s_assetLoaders", BindingFlags.NonPublic | BindingFlags.Static)!;
        var loaders = (List<DataAssetLoader>)loadersField.GetValue(null)!;
        var saved = loaders.ToList();
        loaders.Clear();
        loaders.Add(testLoader);

        try
        {
            DataAssetLoader.LoadDatapackAssets(_tempDir);

            // Correct behavior: a pack with no "data/" directory should be silently skipped.
            // Bug:  check is !Directory.Exists(pack), which is always false because pack
            //       came from EnumerateDirectories → OnLoadAssets is called with the
            //       non-existent "data/" path → LoadAssetsFromFolders faults with
            //       DirectoryNotFoundException → accessing Assets throws AggregateException.
            // Fix:  check is !Directory.Exists(assets) → pack is correctly skipped → no throw.
            _ = testLoader.Assets;
        }
        finally
        {
            loaders.Clear();
            loaders.AddRange(saved);
        }
    }
}
