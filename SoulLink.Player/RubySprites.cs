using SoulLink;

namespace SoulLink.Player;

internal sealed class RubySprites(string directory, string? cache = null, HttpClient? source = null) : IDisposable
{
    private const string Source = "https://raw.githubusercontent.com/PokeAPI/sprites/c0542f7c51215d9672439012838c99f520596cbd/sprites/";
    private const int ImageLimit = 256 * 1024;
    private static readonly HttpClient Http = new() { BaseAddress = new Uri(Source) };
    private static readonly Dictionary<string, (int Id, string Label)> Types = new(StringComparer.Ordinal)
    {
        ["normal"] = (1, "Normal"),
        ["fighting"] = (2, "Kampf"),
        ["flying"] = (3, "Flug"),
        ["poison"] = (4, "Gift"),
        ["ground"] = (5, "Boden"),
        ["rock"] = (6, "Gestein"),
        ["bug"] = (7, "Käfer"),
        ["ghost"] = (8, "Geist"),
        ["steel"] = (9, "Stahl"),
        ["fire"] = (10, "Feuer"),
        ["water"] = (11, "Wasser"),
        ["grass"] = (12, "Pflanze"),
        ["electric"] = (13, "Elektro"),
        ["psychic"] = (14, "Psycho"),
        ["ice"] = (15, "Eis"),
        ["dragon"] = (16, "Drache"),
        ["dark"] = (17, "Unlicht"),
    };

    private readonly Dictionary<string, Bitmap?> loaded = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource closing = new();
    private bool disposed;

    public event Action? Changed;

    public Image? Find(RubySpeciesAppearance appearance, bool shiny)
    {
        string key = $"{appearance.NationalDex}{(shiny ? "-shiny" : "")}.png";
        string path = $"pokemon/versions/generation-iii/ruby-sapphire/{(shiny ? "shiny/" : "")}{appearance.NationalDex}.png";
        return this.Load(key, path);
    }

    public (Image? Image, string Label) TypeIcon(string type)
    {
        if (!Types.TryGetValue(type, out var entry)) return (null, type);
        return (this.Load(Path.Combine("type-icons", type + ".png"),
            $"types/generation-viii/legends-arceus/small/{entry.Id}.png"), entry.Label);
    }

    private Image? Load(string key, string remotePath)
    {
        if (this.disposed) return null;
        if (this.loaded.TryGetValue(key, out var result)) return result;
        string? cachedPath = cache is null ? null : Path.Combine(cache, key);
        result = ReadLocal(Path.Combine(directory, key)) ?? ReadLocal(cachedPath);
        this.loaded.Add(key, result);
        if (result is null && cachedPath is not null)
            _ = this.FetchAsync(key, remotePath, cachedPath);
        return result;
    }

    private static Bitmap? ReadLocal(string? path)
    {
        try
        {
            if (path is null || !File.Exists(path) || new FileInfo(path).Length > ImageLimit) return null;
            using var file = File.OpenRead(path);
            return ReadImage(file);
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or OutOfMemoryException) { return null; }
    }

    private static Bitmap? ReadImage(Stream source)
    {
        using var image = Image.FromStream(source, useEmbeddedColorManagement: false, validateImageData: true);
        return image.Width <= 256 && image.Height <= 256 ? new Bitmap(image) : null;
    }

    private async Task FetchAsync(string key, string remotePath, string cachedPath)
    {
        byte[] bytes = new byte[ImageLimit + 1];
        int count;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(this.closing.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var stream = await (source ?? Http).GetStreamAsync(remotePath, timeout.Token);
            count = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, timeout.Token);
            if (count == 0 || count > ImageLimit || this.disposed) return;
            using var data = new MemoryStream(bytes, 0, count, writable: false);
            if (ReadImage(data) is not { } image) return;
            this.loaded[key] = image;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or ArgumentException or OutOfMemoryException) { return; }

        this.Changed?.Invoke();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachedPath)!);
            await File.WriteAllBytesAsync(cachedPath, bytes.AsMemory(0, count), timeout.Token);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException) { }
    }

    public void Dispose()
    {
        if (this.disposed) return;
        this.disposed = true;
        this.closing.Cancel();
        this.closing.Dispose();
        foreach (var sprite in this.loaded.Values) sprite?.Dispose();
        this.loaded.Clear();
    }
}
