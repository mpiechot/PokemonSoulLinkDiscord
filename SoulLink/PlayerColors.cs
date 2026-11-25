namespace SoulLink;

public static class PlayerColors
{
    public static IReadOnlyList<int> Automatic { get; } = Array.AsReadOnly(new[]
    {
        0x6EE1BD, 0x6FBFFF, 0xFFD372, 0xCBA4FF, 0xFA919F, 0xADE37C,
    });

    public static bool IsValid(int? color) => color is null or (>= 0 and <= 0xFFFFFF);

    public static int Resolve(int index, int? color) => color ?? Automatic[index % Automatic.Count];
}
