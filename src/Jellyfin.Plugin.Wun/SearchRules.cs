using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace Jellyfin.Plugin.Wun;
public static class SearchRules
{
 public static bool ValidKinds(string value)=>value.Length<=64&&value.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries) is var values&&values.Length<=4&&values.All(x=>x is "All" or "Movie" or "Series" or "Anime");
 public static (string Kind,bool AnimeOnly)[] SearchPlan(string value)
 {
  if(!ValidKinds(value))throw new ArgumentException("Invalid types.");
  var values=value.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).ToHashSet();
  if(values.Count==0||values.Contains("All"))return [("Movie",false),("Series",false)];
  var result=new List<(string,bool)>();
  if(values.Contains("Movie")||values.Contains("Anime"))result.Add(("Movie",!values.Contains("Movie")));
  if(values.Contains("Series")||values.Contains("Anime"))result.Add(("Series",!values.Contains("Series")));
  return result.ToArray();
 }
 public static bool IsAnime(IEnumerable<string> genres,IEnumerable<string> tags,IEnumerable<string> countries)
 {
  if(genres.Any(value=>Normalize(value).Trim()=="anime"))return true;
  var animation=genres.Any(value=>Normalize(value).Trim() is "animation" or "animacao");
  return animation&&(tags.Any(value=>Normalize(value).Trim()=="anime")||countries.Any(value=>Normalize(value).Trim() is "japan" or "japao" or "jp"));
 }
 public static SearchCard[] Combine(SearchCard[] movies,SearchCard[] series)
 {
  var result=new List<SearchCard>();
  for(var i=0;i<Math.Max(movies.Length,series.Length)&&result.Count<12;i++){
   if(i<movies.Length)result.Add(movies[i]);
   if(i<series.Length&&result.Count<12)result.Add(series[i]);
  }
  return result.ToArray();
 }
 public static string? ImdbId(string? value)=>value!=null&&Regex.IsMatch(value,"^tt[0-9]{7,12}$",RegexOptions.IgnoreCase)?value.ToLowerInvariant():null;
 public static (string? Provider,string? Id) Identifier(string query)
 {
  var value=query.Trim();
  if(Uri.TryCreate(value,UriKind.Absolute,out var url)&&url.Scheme=="https"&&url.Host is "imdb.com" or "www.imdb.com"){
   var parts=url.AbsolutePath.Split('/',StringSplitOptions.RemoveEmptyEntries);if(parts.Length>=2&&parts[0]=="title")value=parts[1];
  }
  if(value.StartsWith("imdb:",StringComparison.OrdinalIgnoreCase))value=value[5..].Trim();
  if(ImdbId(value) is {} imdb)return ("Imdb",imdb);
  if(value.StartsWith("tmdb:",StringComparison.OrdinalIgnoreCase))value=value[5..].Trim();
  if(int.TryParse(value,out var number)&&number>0)return ("Tmdb",number.ToString(CultureInfo.InvariantCulture));
  return (null,null);
 }
 public static bool Valid(SearchFilters f)=>f.YearFrom is not(<1800 or >2200)&&f.YearTo is not(<1800 or >2200)&&!(f.YearFrom>f.YearTo)&&f.MinRating is not(<0 or >10)&&!(f.MinRating.HasValue&&!double.IsFinite(f.MinRating.Value))&&(f.Genres?.Length??0)<=200&&(f.Genres?.Split(',').Length??0)<=6&&f.Obtained is "any" or "yes" or "no";
 static string Normalize(string value)=>new string(value.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
 public static bool Matches(SearchCard card,SearchFilters f)
 {
  if(f.YearFrom.HasValue&&(!card.Year.HasValue||card.Year<f.YearFrom))return false;
  if(f.YearTo.HasValue&&(!card.Year.HasValue||card.Year>f.YearTo))return false;
  if(f.MinRating.HasValue&&(!card.Rating.HasValue||card.Rating<f.MinRating))return false;
  var wanted=(f.Genres??"").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
  if(wanted.Length>0&&!wanted.Any(w=>(card.Genres??[]).Any(g=>Normalize(g).Contains(Normalize(w)))))return false;
  return f.Obtained=="any"||card.Catalog!=null&&card.Catalog.Obtained==(f.Obtained=="yes");
 }
}
