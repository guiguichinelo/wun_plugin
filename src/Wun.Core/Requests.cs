namespace Wun.Core;

public enum MediaKind { Movie, Series }
public enum Decision { Pending, Approved, Deferred, Rejected, Completed, Canceled }
public enum Availability { Unknown, Absent, Partial, Available }

/// <summary>Only server-resolved metadata may be passed into the store.</summary>
public sealed record Selection(MediaKind Kind, string TmdbId, string Title,
    int? Season = null, int? Episode = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || !int.TryParse(TmdbId, out var id) || id <= 0
            || TmdbId != id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || string.IsNullOrWhiteSpace(Title) || Title.Length > 300
            || Season < 0 || Episode < 1 || (Episode.HasValue && !Season.HasValue)
            || (Kind == MediaKind.Movie && (Season.HasValue || Episode.HasValue)))
            throw new ArgumentException("Invalid metadata selection.");
    }

    public string Identity => $"{Kind}:Tmdb:{TmdbId}:{Season}:{Episode}";
}

// Public projections deliberately have no requester names, internal notes or user identifiers.
public sealed record RequestCard(long Id, Selection Selection, Decision Decision,
    Availability Availability, bool Monitor, long Revision);
public sealed record HistoryEntry(Decision Decision, string Message, DateTimeOffset Created);
public sealed record AdminHistoryEntry(HistoryEntry Public, Guid? Actor, string InternalNote,
    DateTimeOffset? ReviewAt);
public sealed record Notification(long Id, long RequestId, string Event, bool Read);
public sealed record Page<T>(T[] Items, long Total, int Start, int Limit);
public sealed class RevisionConflictException : Exception { }
