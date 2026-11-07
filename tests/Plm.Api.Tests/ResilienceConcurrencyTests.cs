using System.Reflection;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Plm;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Plm.Core;
using Xunit;
namespace Plm.Api.Tests;

public sealed class ResilienceConcurrencyTests
{
    [Fact]
    public async Task HostCallbacksCanReenterCaptureWhileTheBackgroundCycleIsQueryingUsers()
    {
        var dir=Path.Combine(Path.GetTempPath(),"plm-callback-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var path=Path.Combine(dir,"plm.db");var lists=new LibraryStore(path);var memory=new CatalogResilienceStore(path);
            var library=DispatchProxy.Create<ILibraryManager,CallbackProxy>();
            var users=DispatchProxy.Create<IUserManager,CallbackProxy>();
            var data=DispatchProxy.Create<IUserDataManager,CallbackProxy>();
            using var resilience=new CatalogResilience(memory,lists,library,users,data,NullLogger<CatalogResilience>.Instance,new FavoriteMutationGate());
            var callbacks=0;
            ((CallbackProxy)(object)users).Call=(method,args)=>
            {
                if(method.Name=="GetUsers")
                {
                    // Model a host service dispatching a callback on another thread
                    // before returning. A monitor spanning host calls deadlocks here.
                    Task.Run(()=>resilience.Capture(Guid.NewGuid(),Guid.NewGuid())).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                    callbacks++;return Array.Empty<User>();
                }
                return null;
            };
            try
            {
                await resilience.StartAsync(CancellationToken.None);
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(16));
                while(!resilience.BaselineComplete && resilience.LastError is null) await Task.Delay(20,deadline.Token);
                Assert.Null(resilience.LastError);
                Assert.Equal(1,callbacks);Assert.True(resilience.BaselineComplete);
            }
            finally { await resilience.StopAsync(CancellationToken.None); }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true); }
    }
    public class CallbackProxy:DispatchProxy
    {
        public Func<MethodInfo,object?[]?,object?>? Call {get;set;}
        protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>Call?.Invoke(targetMethod!,args) ?? (targetMethod!.ReturnType != typeof(void) && targetMethod.ReturnType.IsValueType ? Activator.CreateInstance(targetMethod.ReturnType) : null);
    }
}
