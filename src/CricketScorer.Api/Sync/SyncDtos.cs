using CricketScorer.Engine;

namespace CricketScorer.Api.Sync;

public sealed record SyncOp(string Kind, int? Sequence, MatchEvent? Event, Guid? EventId);

public sealed record SyncRequest(Guid SessionId, List<SyncOp> Ops);

public sealed record SyncRejection(int Index, Guid? EventId, string Reason);

public sealed record SyncResult(int Applied, int Skipped, int LastSequence, SyncRejection? Rejected);
