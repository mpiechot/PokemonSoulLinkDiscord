namespace SoulLink.Player;

internal sealed class PlayerInstance(int slot, Mutex mutex) : IDisposable
{
    internal const int MaxSlots = 12;
    public int Slot { get; } = slot;
    public bool IsTest => this.Slot > 1;

    public static PlayerInstance? Open(int firstSlot, bool allowFallback, bool wait, string scope = @"Local\SoulLink.Player")
    {
        for (int slot = firstSlot; slot <= (allowFallback ? MaxSlots : firstSlot); slot++)
        {
            var candidate = new Mutex(false, Name(scope, slot));
            bool owns;
            try { owns = candidate.WaitOne(wait ? TimeSpan.FromSeconds(8) : TimeSpan.Zero); }
            catch (AbandonedMutexException) { owns = true; }
            if (owns) return new PlayerInstance(slot, candidate);
            candidate.Dispose();
        }
        return null;
    }

    public static bool HasTestPlayers()
    {
        for (int slot = 2; slot <= MaxSlots; slot++)
        {
            using var available = Open(slot, allowFallback: false, wait: false);
            if (available is null) return true;
        }
        return false;
    }

    public void Dispose()
    {
        mutex.ReleaseMutex();
        mutex.Dispose();
    }

    private static string Name(string scope, int slot) => slot == 1 ? scope : $"{scope}.Test.{slot}";
}
