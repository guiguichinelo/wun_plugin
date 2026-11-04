using Jellyfin.Plugin.Wun;
using Xunit;

public class TitleCoverageTests
{
 [Fact]public void MidnightLikeEndedSeriesIsCompleteOnlyWithEveryNumber()
 {
  var expected=Enumerable.Range(1,8).Select(n=>(1,n)).ToHashSet();var owned=expected.ToDictionary(x=>x,_=>Guid.NewGuid());
  Assert.Equal((true,true),TitleCoverage.Evaluate(expected,expected,owned,true,true));
  owned.Remove((1,8));owned[(1,9)]=Guid.NewGuid();
  Assert.Equal((false,false),TitleCoverage.Evaluate(expected,expected,owned,true,true));
 }
 [Fact]public void CurrentOngoingSeriesStillHasRoomForFutureReleases()
 {
  HashSet<(int,int)> released=[(1,1)],expected=[(1,1),(1,2)];var owned=released.ToDictionary(x=>x,_=>Guid.NewGuid());
  Assert.Equal((true,false),TitleCoverage.Evaluate(expected,released,owned,true,false));
  Assert.Equal((false,false),TitleCoverage.Evaluate(expected,released,owned,false,true));
  Assert.Equal((false,false),TitleCoverage.Evaluate([],[],owned,true,true));
 }
}
