# Plan 1 — Cricket Rules Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A pure C# class library that takes a match setup plus an ordered list of match events and returns the full, correct match state (scores, batters, bowlers, whose turn, what's allowed next, result), fully covered by xUnit tests.

**Architecture:** Event-sourced. Every scorer action is an immutable `MatchEvent` record. `MatchEngine.Validate` checks if an event is allowed in the current state, `MatchEngine.Apply` returns a new immutable `MatchState`, and `MatchEngine.Replay` folds a whole event list. Undo means replaying all events except the last one. There's no database, web or UI code here: the Blazor app (offline) and the API (server-side checking) both reference this library.

**Tech Stack:** .NET 10 (SDK 10.0.302 is installed), C# records, `System.Collections.Immutable`, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-18-cricket-scorer-design.md` (section 7 is the source of truth for every rule here).

## Roadmap (later plans, each written when its turn comes)

| Plan | Builds | Depends on |
|------|--------|-----------|
| **1 — Rules engine** (this file) | `CricketScorer.Engine` + tests | — |
| 2 — Database and API | ASP.NET Core API, EF Core + SQL Server (LocalDB for dev), Identity auth, schools and join codes, teams and players, matches, event sync endpoint (idempotent, scoring sessions), scorecard endpoint, API tests | 1 |
| 3 — Scorer front end | Scoring-screen mockup → Tahir's feedback → Blazor WASM PWA: login, school, teams, match setup, scoring screen, scorecard | 1, 2 |
| 4 — Offline and sync | Local event store (IndexedDB), sync queue, sync indicator, takeover and rejected-event warnings, offline app shell | 2, 3 |
| 5 — Live viewer | SignalR hub, public share-code page, live updates | 2, 3 |
| 6 — Ship it | Free hosting (check the Azure SQL free offer's limits first), README + screenshot, portfolio row | all |

## Global Constraints

- Target `net10.0`. Solution file at the repo root; library in `src/CricketScorer.Engine`, tests in `tests/CricketScorer.Engine.Tests`.
- The engine has **no** dependencies on ASP.NET, EF Core, or any UI package. The only package allowed is `System.Collections.Immutable` (in-box).
- **A short one-line `//` comment above every method** (including constructors and test methods) saying what it does. No other comments unless something is genuinely tricky.
- State types are immutable (records + `ImmutableList`/`ImmutableDictionary`). `Apply` never mutates its input.
- Player and team IDs are `Guid`. Event IDs are `Guid`.
- Playing XI size: **2 to 15** players (Claude's call: the spec said 1–15, but a match can't start with fewer than 2 batters).
- Commits: no AI attribution lines. Never commit secrets. Don't push. Tahir is asked before every push.
- Run all tests with: `dotnet test` from the repo root.

## Review Focus

1. **A wicket on the last legal ball of an over:** the new batter fills the dismissed batter's end *after* the end-of-over swap, and `NextAction` is `ChooseBatter` and then `ChooseBowler` (in that order). → Task 6, test `WicketOnLastBallAsksForBatterThenBowler`.
2. **A target reached with a wide, no-ball or bye:** the innings ends immediately with no further ball allowed. → Task 8, test `TargetReachedByWideEndsInnings`.
3. **Undo across an over end, a wicket, or an innings end** gives exactly the state from before that event. → Task 10, test `UndoAcrossOverEndWicketAndInningsEnd`.
4. **5 wides (a wide that runs to the boundary):** the team gets 5, the bowler is charged 5, no ball is faced, and there's no strike swap. → Task 4, test `WideToBoundaryIsFiveWides`.
5. **Overs reduced (rain) to fewer overs than already bowled:** the innings ends straight away. A bowler already over the reduced limit just can't bowl again. → Task 8, test `OversReducedBelowBowledEndsInnings`.

---

## File Structure

```
CricketScorer.slnx
src/CricketScorer.Engine/
  CricketScorer.Engine.csproj
  Setup/MatchFormat.cs          enum MatchFormat
  Setup/MatchSettings.cs        record MatchSettings + ForFormat factory
  Setup/MatchSetup.cs           records TeamLineup, MatchSetup + Validate
  Events/MatchEvents.cs         abstract record MatchEvent + every event record (they change together)
  Events/ExtraType.cs, DismissalType.cs, Dismissal.cs
  State/MatchState.cs           MatchState, NextAction enum
  State/InningsState.cs         InningsState, Extras, InningsEndReason enum
  State/BatterScore.cs          BatterScore, BatterStatus enum
  State/BowlerFigures.cs, FallOfWicket.cs, MatchResult.cs
  Rules/BallRules.cs            pure maths for a single ball (runs, charges, swaps, chip text)
  Rules/DismissalRules.cs       which dismissals are allowed when, and bowler credit
  Rules/ResultCalculator.cs     result from the first two innings
  MatchEngine.cs                Validate / Apply / Replay
  InvalidEventException.cs
tests/CricketScorer.Engine.Tests/
  MatchHarness.cs               test helper (defined in Task 2)
  SettingsTests.cs, InningsStartTests.cs, LegalBallTests.cs, ExtrasTests.cs, BowlingTests.cs,
  WicketTests.cs, RetirementTests.cs, InningsEndTests.cs, ResultTests.cs, ReplayTests.cs
```

---

### Task 1: Solution, settings and match setup

**Files:**
- Create: `CricketScorer.slnx`, `src/CricketScorer.Engine/CricketScorer.Engine.csproj`, `Setup/*.cs`
- Test: `tests/CricketScorer.Engine.Tests/SettingsTests.cs`

**Interfaces:**
- Produces:
  - `enum MatchFormat { T20, FiftyOvers, CustomOvers, Timed }`
  - `record MatchSettings(MatchFormat Format, int? OversPerSide, int? MaxOversPerBowler, int BallsPerOver, bool FreeHitsEnabled, bool DeclarationsAllowed)`
  - `static MatchSettings MatchSettings.ForFormat(MatchFormat format, int? customOvers = null)`
  - `record TeamLineup(Guid TeamId, ImmutableList<Guid> Players)` (players in batting order)
  - `record MatchSetup(MatchSettings Settings, TeamLineup TeamA, TeamLineup TeamB)` with `string? Validate()` (null = OK)

- [ ] **Step 1: Scaffold.** `dotnet new sln -n CricketScorer`, `dotnet new classlib -o src/CricketScorer.Engine -f net10.0`, `dotnet new xunit -o tests/CricketScorer.Engine.Tests -f net10.0`, add both to the solution, and add a project reference from tests to engine. Delete the template `Class1.cs` / `UnitTest1.cs`.
- [ ] **Step 2: Write failing tests**

```csharp
[Fact] public void T20Defaults() { var s = MatchSettings.ForFormat(MatchFormat.T20);
  Assert.Equal((20, 4, 6, true, false), (s.OversPerSide, s.MaxOversPerBowler, s.BallsPerOver, s.FreeHitsEnabled, s.DeclarationsAllowed)); }
[Fact] public void FiftyOverDefaults() => /* 50, 10, 6, true, false */
[Theory, InlineData(30, 6), InlineData(40, 8), InlineData(1, 1), InlineData(12, 3)]
public void CustomOversBowlerLimitIsOversDividedByFiveRoundedUp(int overs, int max) => /* ForFormat(CustomOvers, overs).MaxOversPerBowler == max */
[Fact] public void CustomOversWithoutCountThrows() => Assert.Throws<ArgumentException>(() => MatchSettings.ForFormat(MatchFormat.CustomOvers));
[Fact] public void TimedDefaults() => /* null, null, 6, false, true */
[Fact] public void SetupRejectsXiSmallerThanTwoOrLargerThanFifteen()
[Fact] public void SetupRejectsDuplicatePlayersAndSameTeamTwiceAndPlayerOnBothSides()
[Fact] public void ValidSetupReturnsNull()
```

- [ ] **Step 3: Run** `dotnet test` → FAIL (types missing).
- [ ] **Step 4: Implement** the records and `ForFormat` / `Validate` in `Setup/`.
- [ ] **Step 5: Run** `dotnet test` → all PASS.
- [ ] **Step 6: Commit** `git add -A && git commit -m "Add engine solution, match settings and setup"`

---

### Task 2: Events, state, engine skeleton, starting an innings

**Files:**
- Create: `Events/*.cs`, `State/*.cs`, `MatchEngine.cs`, `InvalidEventException.cs`
- Test: `tests/.../MatchHarness.cs`, `InningsStartTests.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces (all events are records deriving from `abstract record MatchEvent(Guid Id)`):
  - `InningsStarted(Guid Id, Guid BattingTeamId, Guid StrikerId, Guid NonStrikerId, Guid BowlerId, ImmutableList<Guid>? BattingOrder = null)`
  - `BallBowled(Guid Id, int BatRuns = 0, ExtraType Extra = ExtraType.None, int ExtraRuns = 0, bool Boundary = false, Dismissal? Dismissal = null)`
  - `BowlerChanged(Guid Id, Guid BowlerId)`, `BatterIn(Guid Id, Guid BatterId)`, `StrikeSwapped(Guid Id)`
  - `BatterRetired(Guid Id, Guid BatterId, bool Hurt)`
  - `PenaltyRuns(Guid Id, Guid TeamId, int Runs = 5)`
  - `OversChanged(Guid Id, int OversPerSide)`, `TargetSet(Guid Id, int Target)`
  - `InningsEnded(Guid Id, InningsEndReason Reason)` (scorer-triggered: `Declared`, `Manual`, `AllOut` for the retired-hurt decision)
  - `ResultOverridden(Guid Id, ResultType Type, Guid? WinnerTeamId, string Text)`, `MatchClosed(Guid Id)`
  - `enum ResultType { Win, Tie, Draw, NoResult }` (lives in `State/MatchResult.cs`, created here; Task 9 adds the record)
  - `enum ExtraType { None, Wide, NoBall, Bye, LegBye }`
  - `enum DismissalType { Bowled, Caught, Lbw, Stumped, HitWicket, RunOut, ObstructingField, Other }`
  - `record Dismissal(DismissalType Type, Guid BatterOutId, Guid? FielderId = null)`
  - `enum NextAction { StartInnings, Bowl, ChooseBowler, ChooseBatter, RetiredHurtDecision, MatchClosed }`
  - `enum InningsEndReason { AllOut, OversComplete, TargetReached, Declared, Manual }`
  - `record MatchState(MatchSetup Setup, ImmutableList<InningsState> Innings, NextAction Next, MatchResult? Result, bool Closed, ImmutableDictionary<Guid,int> PendingPenalties)` with `static MatchState Initial(MatchSetup setup)` and `InningsState? Current` (last innings if not complete)
  - `record InningsState(Guid BattingTeamId, Guid BowlingTeamId, ImmutableList<Guid> BattingOrder, int Runs, int Wickets, int LegalBalls, Extras Extras, ImmutableList<BatterScore> Batters, ImmutableList<BowlerFigures> Bowlers, ImmutableList<FallOfWicket> FallOfWickets, Guid? StrikerId, Guid? NonStrikerId, Guid? BowlerId, Guid? PreviousOverBowlerId, ImmutableList<string> ThisOver, int RunsThisOverOffBowler, bool FreeHitNext, int? OversLimit, int? Target, InningsEndReason? EndReason)` with `bool IsComplete => EndReason != null`
  - `record Extras(int Wides, int NoBalls, int Byes, int LegByes, int Penalties)` with `int Total`
  - `record BatterScore(Guid PlayerId, int Runs, int BallsFaced, int Fours, int Sixes, BatterStatus Status, Dismissal? Dismissal, Guid? DismissedByBowlerId)`; `enum BatterStatus { Batting, Out, RetiredHurt, RetiredOut }` (players not in `Batters` did not bat)
  - `record BowlerFigures(Guid PlayerId, int LegalBalls, int Maidens, int RunsConceded, int Wickets, int Wides, int NoBalls)`
  - `record FallOfWicket(int WicketNumber, int Score, Guid PlayerId, int LegalBallsAtFall)`
  - `static class MatchEngine { string? Validate(MatchState s, MatchEvent e); MatchState Apply(MatchState s, MatchEvent e); MatchState Replay(MatchSetup setup, IEnumerable<MatchEvent> events); }`. `Apply` throws `InvalidEventException(string message)` when `Validate` returns non-null.
  - **Test helper `MatchHarness`** (used by every later test file): `static MatchHarness Create(MatchSettings? settings = null, int xiSize = 11)` (defaults to T20). Properties `Setup`, `TeamA`, `TeamB`, `A`, `B` (player ID lists), `State`, `Events`, `Inn` (`State.Innings[^1]`). Methods (each returns `this`): `Start(bool teamABats = true)` (striker = batting[0], non-striker = batting[1], bowler = fielding[^1]), `Send(MatchEvent e)`, `Bowl(int bat = 0, ExtraType extra = ExtraType.None, int extraRuns = 0, bool boundary = false, Dismissal? d = null)`, `Dots(int n)`, `NewBowler(Guid id)`, `In(Guid id)`, `Undo()` (replays `Events` minus the last). Plus `string? Try(MatchEvent e)` returning the validation error.

- [ ] **Step 1: Write failing tests** (`InningsStartTests`)
  - `NewMatchWaitsForInningsStart`: `State.Next == StartInnings`, `Innings` empty.
  - `StartingSetsBattersAndBowler`: after `Start()`, `Inn.StrikerId == A[0]`, `NonStrikerId == A[1]`, `BowlerId == B[10]`, `Next == Bowl`, `OversLimit == 20`, `Target == null`, two `Batting` entries in `Batters`.
  - `StartRejects`: same striker and non-striker; a batter not in the batting team; a bowler not in the fielding team; a team not in the match; any `BallBowled` before `InningsStarted` → each `Try(...)` returns non-null.
  - `ApplyThrowsOnInvalidEvent`: `Assert.Throws<InvalidEventException>`.
  - `CustomBattingOrderIsUsed`: `InningsStarted` with a reversed `BattingOrder` → `Inn.BattingOrder` is reversed.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** the types, `MatchState.Initial`, and the `InningsStarted` handling in `MatchEngine` (a `switch` on event type delegating to one private method per event). Other events return the error `"Not supported yet"` until their task.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `"Add match events, state and innings start"`

---

### Task 3: Legal balls, runs, strike rotation, over end

**Files:** Create `Rules/BallRules.cs`. Modify `MatchEngine.cs`. Test `LegalBallTests.cs`.

**Interfaces:**
- Produces in `static class BallRules`: `int TeamRuns(BallBowled b)`, `int RunsOffBowler(BallBowled b)`, `int RunsRun(BallBowled b)`, `bool IsLegal(BallBowled b)`, `bool CountsAsBallFaced(BallBowled b)`, `string ChipText(BallBowled b)`. Exact rules (all from spec §7):

| Extra | Team runs | Charged to bowler | Runs run (for strike) | Legal | Ball faced | Batter credited |
|---|---|---|---|---|---|---|
| None | BatRuns | BatRuns | Boundary ? 0 : BatRuns | yes | yes | BatRuns |
| Wide | 1 + ExtraRuns | 1 + ExtraRuns | Boundary ? 0 : ExtraRuns | no | no | 0 |
| NoBall | 1 + BatRuns + ExtraRuns | 1 + BatRuns | Boundary ? 0 : BatRuns + ExtraRuns | no | yes | BatRuns |
| Bye / LegBye | ExtraRuns | 0 | Boundary ? 0 : ExtraRuns | yes | yes | 0 |

  Extras: Wide → `Wides += 1 + ExtraRuns`. NoBall → `NoBalls += 1`, `Byes += ExtraRuns`. Bye → `Byes`, LegBye → `LegByes`. A boundary 4 / 6 off the bat (None or NoBall) adds to the batter's `Fours` / `Sixes`.
  Validation: BatRuns and ExtraRuns 0–7. Wide/Bye/LegBye need `BatRuns == 0`. Bye/LegBye need `ExtraRuns >= 1` unless there's a dismissal. `Boundary` needs BatRuns ∈ {4, 6} (None/NoBall) or ExtraRuns == 4 (Wide/Bye/LegBye). Rejected unless `Next == Bowl`.
  Chip text: `"0"`, `"1"`, `"4"`, `"6"`, `"wd"` (1 wide) / `"3wd"`, `"nb"` / `"nb+4"` (no-ball with bat runs), `"2b"`, `"1lb"`. A dismissal appends `"W"` (`"W"` alone for a 0-run legal ball, e.g. `"1W"` for a run out after 1 run).

- [ ] **Step 1: Write failing tests**
  - `SingleSwapsStrike` (A[0] on 1 → `StrikerId == A[1]`, `Runs == 1`, batter A[0] runs 1 balls 1).
  - `TwoKeepsStrike`, `ThreeSwaps`, `BoundaryFourAndSixNoSwapAndCounted` (`Fours == 1`, `Sixes == 1`).
  - `SixLegalBallsEndOverSwapAndAskForBowler`: `Dots(6)` → `LegalBalls == 6`, striker swapped, `BowlerId == null`, `PreviousOverBowlerId == B[10]`, `Next == ChooseBowler`, `ThisOver` empty after the next bowler, bowler figures `LegalBalls == 6`.
  - `SingleOffLastBallMeansSameBatterFacesNextOver`: 5 dots then 1 → original striker A[0] on strike after the over end.
  - `ChipTextCases` (Theory over the table above).
  - `RejectsBadRunValues` (BatRuns 8, Boundary with 3).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** `BallRules` + ball handling for `ExtraType.None` and the over end. **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `"Score legal balls, strike rotation and over end"`

---

### Task 4: Extras and free hits

**Files:** Modify `MatchEngine.cs`. Test `ExtrasTests.cs`.

- [ ] **Step 1: Write failing tests**
  - `WideAddsOneNotLegalNotFaced`: `Runs == 1`, `LegalBalls == 0`, striker balls faced 0, bowler `Wides == 1`, `RunsConceded == 1`.
  - `WideToBoundaryIsFiveWides`: `Bowl(extra: Wide, extraRuns: 4, boundary: true)` → `Runs == 5`, `Extras.Wides == 5`, bowler conceded 5, no swap. *(Review Focus 4)*
  - `WidesRunOneSwapsStrike`: wide + 1 run → swap.
  - `NoBallWithFourOffBat`: `Runs == 5`, batter runs 4, `Fours == 1`, balls faced 1, bowler conceded 5, `NoBalls == 1`, `LegalBalls == 0`.
  - `NoBallGivesFreeHitWhenEnabled` / `NoFreeHitInTimedMatch`.
  - `FreeHitCarriesOverAWide`: no-ball, then wide → `FreeHitNext` still true; then a legal ball → false.
  - `ByesAndLegByes`: team gets runs, `Extras.Byes` / `LegByes`, batter 0 runs but 1 ball faced, bowler not charged, odd runs swap strike.
  - `SevenExtraBallsStillOnlySixLegal`: 6 dots + 2 wides inside the over → over ends after the 6th legal ball.
- [ ] **Step 2–4:** run (FAIL) → implement → run (PASS).
- [ ] **Step 5: Commit** `"Handle wides, no-balls, byes, leg byes and free hits"`

---

### Task 5: Bowler changes, limits and maidens

**Files:** Modify `MatchEngine.cs`. Test `BowlingTests.cs`.

- [ ] **Step 1: Write failing tests**
  - `NextOverNeedsDifferentBowler`: after an over, `Try(BowlerChanged(B[10]))` is non-null and `B[9]` is OK.
  - `BowlerMustBeOnFieldingSide`.
  - `BowlerAtLimitRejected`: T20, a bowler with 4 overs bowled (alternate two bowlers for 8 overs) → their 5th is rejected.
  - `TimedHasNoBowlerLimit` (alternating, 15 overs each is fine).
  - `MaidenCountedWhenNoRunsOffBowler`: 6 dots → `Maidens == 1`. 5 dots + 1 leg bye → still a maiden. 5 dots + 1 wide → not a maiden.
  - `MidOverChangeAllowed`: after 3 balls a `BowlerChanged` (injury) to a bowler who didn't bowl the previous over is accepted. The over continues and both bowlers keep their own balls.
  - `BowlerOversText`: 3 overs and 2 balls shows as `"3.2"` via `InningsState.OversText(int legalBalls, int ballsPerOver)` (static helper, also used for the team).
- [ ] **Step 2–4:** run → implement (`BowlerChanged` is valid when `Next == ChooseBowler` or mid-over when `Next == Bowl`) → run.
- [ ] **Step 5: Commit** `"Add bowler changes, limits and maidens"`

---

### Task 6: Wickets

**Files:** Create `Rules/DismissalRules.cs`. Modify `MatchEngine.cs`. Test `WicketTests.cs`.

**Interfaces:**
- Produces: `static bool DismissalRules.IsAllowed(DismissalType type, ExtraType extra, bool freeHit)`, `static bool DismissalRules.CreditsBowler(DismissalType type)`, `static bool DismissalRules.NeedsFielder(DismissalType type)` (Caught, Stumped).
  - Allowed: Extra None → all types, **except** on a free hit only RunOut, ObstructingField, Other. Wide → Stumped, HitWicket, RunOut, ObstructingField, Other. NoBall / Bye / LegBye → RunOut, ObstructingField, Other.
  - Credits bowler: Bowled, Caught, Lbw, Stumped, HitWicket.
  - The batter out must be the striker, except RunOut / ObstructingField / Other (either batter).
- Rules: a wicket raises `Wickets`, sets the batter to `Out` with the dismissal (and `DismissedByBowlerId` when credited), and adds a `FallOfWicket(Wickets, Runs, playerId, LegalBalls)`. Runs on the ball are applied first and strike swaps from runs happen, then the over-end swap, and then the dismissed batter's slot is cleared. `Next` = `ChooseBatter` if any batter is available, otherwise the innings handles all-out (Task 7/8). A `BatterIn` fills the empty slot. After that, `Next` = `ChooseBowler` if the over had ended, otherwise `Bowl`. Caught: no runs, so the new batter takes the striker's slot (Laws 2022).

- [ ] **Step 1: Write failing tests**
  - `BowledCreditsBowlerAndAsksForBatter`, `CaughtNeedsFielder`, `CaughtAndBowledFielderCanBeBowler`.
  - `DismissalMatrix` (Theory: e.g. Bowled+Wide rejected, Stumped+Wide OK, Lbw+NoBall rejected, RunOut+NoBall OK, Caught on free hit rejected, RunOut on free hit OK).
  - `RunOutNonStrikerAfterOneRun`: run out of the non-striker with 1 run completed → team +1, striker gets 1, the correct batter is out and not credited to the bowler.
  - `NewBatterTakesDismissedBattersEnd` and `CaughtNewBatterOnStrike`.
  - `WicketOnLastBallAsksForBatterThenBowler` *(Review Focus 1)*: 5 dots, then bowled → `Next == ChooseBatter`. After `In(A[2])`: `Next == ChooseBowler`, and A[2] is the **non-striker** (the over-end swap put the dismissed end at the non-striker's end).
  - `BatterInRejectsPlayerWhoAlreadyBatted` and players not in the batting XI.
  - `FallOfWicketsRecorded` (score and wicket number).
- [ ] **Step 2–4:** run → implement → run.
- [ ] **Step 5: Commit** `"Add wickets, dismissal rules and fall of wickets"`

---

### Task 7: Retirements and all out

**Files:** Modify `MatchEngine.cs`. Test `RetirementTests.cs`.

- Rules: `BatterRetired(Hurt: true)` → status `RetiredHurt`, slot cleared, **not** a wicket. `Hurt: false` → `RetiredOut`, counts as a wicket and a fall of wicket, not credited to the bowler. Allowed only between balls (`Next == Bowl` or `ChooseBowler`). Then `ChooseBatter`. A `RetiredHurt` batter may come back through `BatterIn` (status back to `Batting`). **Available batters** = batting order minus anyone in `Batters` with status Batting/Out/RetiredOut, minus RetiredHurt. When a slot is empty and no batter is available: if any batter is `RetiredHurt` → `Next = RetiredHurtDecision` (the scorer sends `BatterIn(that batter)` or `InningsEnded(AllOut)`), otherwise the innings ends with `AllOut`.

- [ ] **Step 1: Write failing tests**
  - `RetiredHurtIsNotAWicket`, `RetiredHurtCanReturn`, `RetiredOutCountsAsWicket`.
  - `AllOutWithElevenIsTenWickets`: 10 wickets → `EndReason == AllOut`, `Next == StartInnings`.
  - `AllOutWithSmallXi`: xiSize 6 → ends at 5 wickets.
  - `LastBatterOutWithRetiredHurtAsksScorer`: xiSize 3, A[0] retired hurt, then wickets until no fresh batter → `Next == RetiredHurtDecision`. `InningsEnded(AllOut)` ends it, and `BatterIn(A[0])` continues instead.
  - `RetireRejectedForPlayerNotBatting`.
- [ ] **Step 2–4:** run → implement → run.
- [ ] **Step 5: Commit** `"Add retirements and all-out rules"`

---

### Task 8: Innings end, chases, declarations, rain changes, penalties

**Files:** Modify `MatchEngine.cs`. Test `InningsEndTests.cs`.

- Rules:
  - The innings ends automatically with `OversComplete` when `LegalBalls == OversLimit * BallsPerOver`, or `TargetReached` the moment `Runs >= Target`. This is checked after every ball, wides and no-balls included. Then `Next = StartInnings`.
  - The second innings of the match automatically gets `Target = first.Runs + 1`. Extra innings (index ≥ 2) get no target unless `TargetSet` arrives.
  - `InningsEnded(Declared)` is only valid when `DeclarationsAllowed`. `InningsEnded(Manual)` is always valid while an innings is in progress.
  - `OversChanged`: sets `OversLimit`. If it's now ≤ overs already bowled, the innings ends with `OversComplete`. For `CustomOvers`, `T20` and `FiftyOvers`, the max per bowler is recalculated as overs ÷ 5 rounded up (bowlers already over the limit just can't bowl again). Not valid for Timed.
  - `TargetSet`: valid on the current innings while it's in progress.
  - `PenaltyRuns`: to the batting team → `Extras.Penalties` + `Runs`. To the fielding team → added to that team's most recent innings if it has batted, otherwise stored in `PendingPenalties` and added when its next innings starts.
  - `StrikeSwapped`: valid whenever `Next == Bowl`.
  - `InningsStarted` for innings index 2+ is only valid after at least two innings are complete (this is "Add another innings").

- [ ] **Step 1: Write failing tests**
  - `OversCompleteEndsInnings` (T20 → 120 legal balls), `ChaseShowsTargetAndEndsWhenReached`.
  - `TargetReachedByWideEndsInnings` *(Review Focus 2)*: target 10, on 9, a wide → `Runs == 10`, `EndReason == TargetReached`, `Try(Bowl)` non-null.
  - `DeclarationOnlyInTimed`.
  - `OversReducedBelowBowledEndsInnings` *(Review Focus 5)*: 12 overs bowled, `OversChanged(10)` → ended.
  - `OversReducedRecalculatesBowlerLimit` (T20 → 10 overs → max 2).
  - `PenaltyToBattingTeam` and `PenaltyToFieldingTeamCarriedToTheirInnings`.
  - `ManualSwapStrike`.
  - `ExtraInningsHasNoTargetUnlessSet`.
- [ ] **Step 2–4:** run → implement → run.
- [ ] **Step 5: Commit** `"Add innings end, chases, declarations, rain changes and penalties"`

---

### Task 9: Results, extra innings, overrides, closing a match

**Files:** Create `Rules/ResultCalculator.cs`, `State/MatchResult.cs`. Modify `MatchEngine.cs`. Test `ResultTests.cs`.

**Interfaces:**
- Consumes: `ResultType` from Task 2.
- Produces: `record MatchResult(ResultType Type, Guid? WinnerTeamId, int? ByRuns, int? ByWickets, string? OverrideText)`, `static MatchResult? ResultCalculator.FromFirstInnings(MatchSetup setup, InningsState first, InningsState second)`.
- Rules (the result comes **only from innings 0 and 1** and is set as soon as innings 1 is complete):
  - Second side passes the target → Win for them, `ByWickets = (their XI size − 1) − Wickets`.
  - Otherwise, if innings 1 ended `AllOut` or `OversComplete` (limited overs) → scores equal → `Tie`, else Win for the first side, `ByRuns = first.Runs − second.Runs`.
  - Timed: ended any other way (`Manual`) and not all out → `Draw` (even if the scores are level). All out with level scores → `Tie`.
  - Limited overs ended `Manual` without reaching the target → `null` (result pending, the scorer must override).
- `ResultOverridden` replaces `Result` with `MatchResult(Type, WinnerTeamId, null, null, Text)`. `WinnerTeamId` is required when `Type == Win` and must be null otherwise. Valid any time after innings 1 has started.
- `MatchClosed`: valid when no innings is in progress and at least one innings exists. Sets `Closed = true`, `Next = MatchClosed`. After that, every other event is rejected.

- [ ] **Step 1: Write failing tests** (Theory-driven where possible)
  - `ChaseWonBySevenWickets` (XI 11, 3 down), `ChaseWonWithXiEightByFourWickets` (3 down → 7 − 3 = 4).
  - `DefendedAllOutWonByThirtyRuns`, `OversCompleteWonByOneRun`, `LimitedLevelScoresTie`.
  - `TimedNotAllOutIsDraw`, `TimedLevelNotAllOutIsDraw`, `TimedLevelAllOutIsTie`, `DeclaredThenChasedWonBySixWickets`.
  - `RainEndedLimitedHasNoResultUntilOverridden`.
  - `ExtraInningsDoNotChangeResult`: after the first-innings result, two extra innings where the loser scores more → `Result` unchanged.
  - `OverrideReplacesResult` (text kept).
  - `ClosedMatchRejectsEverything`.
- [ ] **Step 2–4:** run → implement → run.
- [ ] **Step 5: Commit** `"Add match results, extra innings and closing"`

---

### Task 10: Replay, undo, display helpers, full-match check

**Files:** Modify `MatchEngine.cs`, `InningsState.cs`. Test `ReplayTests.cs`.

**Interfaces:**
- Produces on `InningsState`: `string Overs` (e.g. `"14.3"`), `decimal RunRate` (runs per 6-ball-equivalent over, 2 dp, 0 when no balls), `decimal? RequiredRunRate` (null without a target or over limit), `int? RunsNeeded`, `int? BallsLeft`, `IEnumerable<Guid> AvailableBatters` (in batting order, used by the UI to suggest the next batter).
- `MatchEngine.Replay` folds `Apply` from `MatchState.Initial(setup)`.

- [ ] **Step 1: Write failing tests**
  - `ReplayEqualsStepByStep` (the same events → states with the same scores and figures).
  - `UndoAcrossOverEndWicketAndInningsEnd` *(Review Focus 3)*: for each of (the last ball of an over, a wicket, the ball that ends an innings), capture the state, apply the event, then `Undo()` → the scores, `Next`, striker/non-striker/bowler, `ThisOver` and `EndReason` equal the captured state.
  - `RunRateAndRequiredRate`: 45 off 5.0 overs → 9.00. Target 151 at 45/2 after 5 overs → needs 106 off 90 balls, RRR 7.07.
  - `FullT20Scorecard`: a scripted short match (custom 2 overs, XI 4) with a mix of runs, a wide, a no-ball four, a bye, a caught, a run out, a bowler change, and a successful chase. Assert both totals, every batter's runs/balls, both bowlers' figures, extras, fall of wickets, and the result. Work out the expected numbers by hand in the test. This test is the "do the totals add up" anti-goal check: **sum of batter runs + extras == team runs** for both innings.
  - `TotalsAlwaysAddUp`: after every event in `FullT20Scorecard`, assert batter runs + extras total == innings runs.
- [ ] **Step 2–4:** run → implement → run. Then run `dotnet test` once more for the whole suite → all PASS.
- [ ] **Step 5: Commit** `"Add replay, undo checks, run rates and full-match test"`
