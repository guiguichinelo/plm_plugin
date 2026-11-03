namespace Jellyfin.Plugin.Plm;

/// <summary>Serializes native writes with host creation until the ownership ledger is durable.</summary>
public sealed class MirrorMutationGate : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public void Dispose() => Semaphore.Dispose();
}
