using SoulLink;

namespace SoulLink.Player;

internal sealed class RubySprites(string directory, string? cache = null, HttpClient? source = null) : IDisposable
{
    private const string SpriteSource = "https://raw.githubusercontent.com/PokeAPI/sprites/c0542f7c51215d9672439012838c99f520596cbd/sprites/";
    private const string EnergySource = "https://www.pokepedia.fr/Sp%C3%A9cial:Chemin_du_fichier/";
    private const int ImageLimit = 256 * 1024;
    private static readonly HttpClient Http = new();
    private static readonly Dictionary<string, (string Energy, string Label)> EnergyTypes = new(StringComparer.Ordinal)
    {
        ["normal"] = ("Incolore", "Normal"),
        ["fighting"] = ("Combat", "Kampf"),
        ["steel"] = ("Métal", "Stahl"),
        ["fire"] = ("Feu", "Feuer"),
        ["water"] = ("Eau", "Wasser"),
        ["grass"] = ("Plante", "Pflanze"),
        ["electric"] = ("Électrique", "Elektro"),
        ["psychic"] = ("Psy", "Psycho"),
        ["dragon"] = ("Dragon", "Drache"),
        ["dark"] = ("Obscurité", "Unlicht"),
        ["fairy"] = ("Fée", "Fee"),
    };
    private static readonly Dictionary<string, (int Id, string Label)> GameTypes = new(StringComparer.Ordinal)
    {
        ["flying"] = (3, "Flug"),
        ["poison"] = (4, "Gift"),
        ["ground"] = (5, "Boden"),
        ["rock"] = (6, "Gestein"),
        ["bug"] = (7, "Käfer"),
        ["ghost"] = (8, "Geist"),
        ["ice"] = (15, "Eis"),
    };

    private readonly Dictionary<string, Bitmap?> loaded = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource closing = new();
    private bool disposed;

    public event Action? Changed;

    public Image? Find(RubySpeciesAppearance appearance, bool shiny)
    {
        string key = $"{appearance.NationalDex}{(shiny ? "-shiny" : "")}.png";
        string path = $"pokemon/versions/generation-iii/ruby-sapphire/{(shiny ? "shiny/" : "")}{appearance.NationalDex}.png";
        return this.Load(key, SpriteSource + path);
    }

    public (Image? Image, string Label) TypeIcon(string type)
    {
        if (GameTypes.TryGetValue(type, out var game))
            return (this.Load(Path.Combine("type-icons", type + ".png"),
                $"{SpriteSource}types/generation-viii/legends-arceus/small/{game.Id}.png"), game.Label);
        if (!EnergyTypes.TryGetValue(type, out var entry)) return (null, type);
        string filename = Uri.EscapeDataString($"Miniature_Type_{entry.Energy}_(flat)_JCC.svg");
        return (this.Load(Path.Combine("tcg-icons", entry.Energy + ".png"), $"{EnergySource}{filename}?width=120"), entry.Label);
    }

    public Image? FindItem(string name) => this.Load(Path.Combine("items", name + ".png"), $"{SpriteSource}items/{name}.png");

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
        string pendingPath = cachedPath + ".download";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachedPath)!);
            await File.WriteAllBytesAsync(pendingPath, bytes.AsMemory(0, count), timeout.Token);
            File.Move(pendingPath, cachedPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException) { }
        finally
        {
            try { File.Delete(pendingPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
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
