using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Jellyfin.Plugin.Wun;

/// <summary>Coalesces catalog notifications without losing changes received during reconciliation.</summary>
public sealed class AvailabilitySignal
{
    readonly object gate=new();long generation,acknowledged;DateTimeOffset first,last;
    public void Request(DateTimeOffset now){lock(gate){if(generation==acknowledged)first=now;last=now;generation++;}}
    public long? Ready(DateTimeOffset now,bool scanning){lock(gate)return !scanning&&generation!=acknowledged&&(now-last>=TimeSpan.FromSeconds(20)||now-first>=TimeSpan.FromMinutes(2))?generation:null;}
    public void Acknowledge(long value){lock(gate)acknowledged=Math.Max(acknowledged,value);}
}
public sealed class AvailabilityWorker(ILibraryManager library,ITaskManager tasks,MaintenanceRunner runner,ILogger<AvailabilityWorker> log,AvailabilitySignal signal):BackgroundService
{
    void Changed(object? sender,ItemChangeEventArgs args){if(args.Item.GetType().Name is "Movie" or "Series" or "Season" or "Episode")signal.Request(DateTimeOffset.UtcNow);}
    void Completed(object? sender,TaskCompletionEventArgs args){if(args.Task.ScheduledTask.Key=="RefreshLibrary"&&args.Result.Status==TaskCompletionStatus.Completed)signal.Request(DateTimeOffset.UtcNow);}
    public override Task StartAsync(CancellationToken ct){library.ItemAdded+=Changed;library.ItemUpdated+=Changed;tasks.TaskCompleted+=Completed;signal.Request(DateTimeOffset.UtcNow);return base.StartAsync(ct);}
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try {await Task.Delay(TimeSpan.FromSeconds(30),ct);
        while(!ct.IsCancellationRequested){var generation=signal.Ready(DateTimeOffset.UtcNow,library.IsScanRunning);
            if(generation.HasValue){try{await runner.Run(false,new Progress<double>(),ct,availabilityOnly:true);signal.Acknowledge(generation.Value);log.LogInformation("WUN catalog availability reconciliation completed.");}
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception error){log.LogWarning(error,"WUN catalog reconciliation deferred; pending changes retained.");await Task.Delay(TimeSpan.FromMinutes(1),ct);}}
            await Task.Delay(TimeSpan.FromSeconds(5),ct);
        }}catch(OperationCanceledException)when(ct.IsCancellationRequested){}
    }
    public override async Task StopAsync(CancellationToken ct){library.ItemAdded-=Changed;library.ItemUpdated-=Changed;tasks.TaskCompleted-=Completed;await base.StopAsync(ct);}
}
