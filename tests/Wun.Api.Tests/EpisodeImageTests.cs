using Jellyfin.Plugin.Wun;
using Xunit;

public class EpisodeImageTests
{
 [Theory]
 [InlineData("/90wDMfxHNSWtVlG8Z4uwU3Dy4cm.jpg",true)]
 [InlineData(null,false)]
 [InlineData("https://other.example/image.jpg",false)]
 [InlineData("/../image.jpg",false)]
 public void OnlyProviderStillPathsBecomeImageUrls(string? path,bool valid)
 {
  var result=NativeSchedule.Still(new{StillPath=path});
  if(valid)Assert.StartsWith("https://image.tmdb.org/t/p/w300/",result);else Assert.Null(result);
 }
}
