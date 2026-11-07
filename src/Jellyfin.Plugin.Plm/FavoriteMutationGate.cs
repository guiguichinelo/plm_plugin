using System.Threading;
namespace Jellyfin.Plugin.Plm;

/// <summary>Bounded per-user/item exclusion shared by native favorite actions and replay.
/// Event callbacks never acquire this gate, so host save notifications can re-enter.
/// </summary>
public sealed class FavoriteMutationGate : IDisposable
{
    private readonly SemaphoreSlim[] _stripes=Enumerable.Range(0,256).Select(_=>new SemaphoreSlim(1,1)).ToArray();
    private SemaphoreSlim For(Guid owner,Guid item)=>_stripes[(uint)HashCode.Combine(owner,item)%(uint)_stripes.Length];
    public async Task<IDisposable> AcquireAsync(Guid owner,Guid item,CancellationToken token)
    {var gate=For(owner,item);await gate.WaitAsync(token);return new Lease(gate);}
    public IDisposable Acquire(Guid owner,Guid item,CancellationToken token)
    {var gate=For(owner,item);gate.Wait(token);return new Lease(gate);}
    private sealed class Lease(SemaphoreSlim gate):IDisposable
    {private SemaphoreSlim? _gate=gate;public void Dispose()=>Interlocked.Exchange(ref _gate,null)?.Release();}
    public void Dispose(){foreach(var gate in _stripes)gate.Dispose();}
}
