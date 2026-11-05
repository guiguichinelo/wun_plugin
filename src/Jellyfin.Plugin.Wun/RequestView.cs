using System.Text.Json.Serialization;
using Wun.Core;

namespace Jellyfin.Plugin.Wun;

public sealed record RequestView(long Id, Selection Selection, Decision Decision, Availability Availability,
    bool Monitor, long Revision, CatalogPresence Catalog, string? ImdbId, int? Year, string? ImageUrl,
    int SupportCount, bool Supporting, bool Following, bool OwnRequest, DateTimeOffset? Created)
{
    public bool Current {get;init;}
    public bool IsAnime { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public UserSummary? Requester { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? RequesterKnown { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public UserSummary[]? Supporters { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AdminHistoryEntry? LastDecision { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ExistingRequest { get; init; }
}
