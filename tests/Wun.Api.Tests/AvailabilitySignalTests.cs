using Jellyfin.Plugin.Wun;
using Xunit;
using Wun.Core;
using MediaBrowser.Controller.Library;
using System.Reflection;
public sealed partial class AccessTests
{
 [Fact]public async Task AutomaticAvailabilityWaitsForCatalogAndNotifiesOnce(){var library=DispatchProxy.Create<ILibraryManager,LibraryFixtureProxy>();var fake=(LibraryFixtureProxy)(object)library;fake.Present=false;var runner=new MaintenanceRunner(store,library,null!,null!);var c=store.Create(user,new(MediaKind.Movie,"777","Fixture"),[]);store.Transition(c.Id,c.Revision,Decision.Approved,"Approved");var notices=store.Notifications(user).Length;await runner.Run(false,new Progress<double>(),default,availabilityOnly:true);Assert.Equal(Decision.Approved,store.Get(c.Id).Decision);fake.Present=true;await runner.Run(false,new Progress<double>(),default,availabilityOnly:true);Assert.Equal(Decision.Completed,store.Get(c.Id).Decision);Assert.Equal(notices+1,store.Notifications(user).Length);await runner.Run(false,new Progress<double>(),default,availabilityOnly:true);Assert.Equal(notices+1,store.Notifications(user).Length);}
}
public sealed class AvailabilitySignalTests
{
 [Fact]public void DebouncesDefersScansAndRetainsConcurrentEvents(){var s=new AvailabilitySignal();var now=DateTimeOffset.UtcNow;s.Request(now);Assert.Null(s.Ready(now.AddSeconds(19),false));Assert.Null(s.Ready(now.AddSeconds(30),true));var generation=s.Ready(now.AddSeconds(30),false);Assert.NotNull(generation);s.Request(now.AddSeconds(31));s.Acknowledge(generation.Value);Assert.NotNull(s.Ready(now.AddSeconds(60),false));s.Acknowledge(s.Ready(now.AddSeconds(60),false)!.Value);Assert.Null(s.Ready(now.AddHours(1),false));}
 [Fact]public void ContinuousEventsHaveBoundedWait(){var s=new AvailabilitySignal();var now=DateTimeOffset.UtcNow;for(var i=0;i<121;i++)s.Request(now.AddSeconds(i));Assert.NotNull(s.Ready(now.AddSeconds(121),false));}
}
