# Plan 2 — Database and API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An ASP.NET Core Web API backed by SQL Server that lets scorers sign up, create or join a school, manage teams and players, create matches, sync match events safely (idempotent, one scoring device at a time, every event re-checked by the rules engine), and read a scorecard, all covered by integration tests against a real LocalDB database.

**Architecture:** One API project (`src/CricketScorer.Api`) with controllers, EF Core code-first entities and migrations, and ASP.NET Core Identity for the user store. The server never stores scores: it stores the ordered event list per match and rebuilds the match state with `MatchEngine.Replay` (from Plan 1) whenever it needs to check an event or build a scorecard. Integration tests use `WebApplicationFactory` against a throwaway LocalDB database, so they exercise the real SQL constraints, migrations and auth.

**Tech Stack:** .NET 10 (SDK 10.0.302), ASP.NET Core controllers, EF Core 10 + `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.OpenApi`, xUnit + `Microsoft.AspNetCore.Mvc.Testing`. `dotnet-ef` 10.0.12 is installed globally. LocalDB instance `MSSQLLocalDB` is installed.

**Spec:** `docs/superpowers/specs/2026-10-18-cricket-scorer-design.md` (sections 5, 6, 9 and 10 drive this plan). Plan 1: `docs/superpowers/plans/2026-10-18-plan-1-rules-engine.md` (roadmap table at the top).

## Decisions in this plan (Claude's calls, with the why)

Tahir: these are the choices you'd be asked about in an interview. Push back on any of them in review.

1. **Controllers, not minimal APIs.** Controllers are what IIE modules and most job ads use, so the code reads like what interviewers expect. Routes are grouped by resource (`SchoolsController`, `TeamsController`, ...).
2. **JWT bearer tokens, valid for 7 days, no refresh tokens.** The Blazor PWA (Plan 3) runs on a phone and calls the API from JavaScript, so a token in a header suits it better than cookies. 7 days covers a weekend of matches without signal. The spec already says an expired login must not stop offline scoring (events wait until sign-in), so refresh tokens add code without fixing a real problem. Identity still does the hard parts: password hashing, lockout, unique emails.
3. **Relaxed password rules: at least 8 characters, no forced symbols or capitals.** Scorers are teachers and parents typing on a phone at the boundary. Current NIST guidance favours length over character rules.
4. **School membership is checked in the database on every request, not stored in the token.** When the admin removes a scorer, that scorer loses access straight away, even though their token is still valid for days.
5. **Another school's data returns 404, not 403.** A scorer can't even confirm that another school's team or match exists. Inside your own school, a match you don't own returns 403 for writes (you can see it, so hiding it makes no sense).
6. **One event sequence per match, not per innings.** The engine treats a match as one ordered list (`InningsStarted` is itself an event), so one counter per match is simpler and still gives every event a fixed position. The spec's "sequence number in the innings" becomes "sequence number in the match".
7. **Undo is a soft delete.** A removed event keeps its row with `IsRemoved = 1`. A filtered unique index (`MatchId, Sequence` where `IsRemoved = 0`) lets the next event reuse the sequence number. Keeping the row makes "send the same thing twice" harmless: an add for an event ID the server has already seen (even if it was undone) is skipped, never re-added. It also leaves an undo history you can show in SSMS.
8. **A sync batch stops at the first rejected operation.** Every later event depends on the ones before it, so applying anything after a rejected event would build on a state the scorer never saw. Accepted operations before it are saved.
9. **Connection string and JWT signing key live in user-secrets only.** Even the LocalDB connection string (which has no password) goes in user-secrets, so there is one rule with no exceptions. The app fails at startup with a clear message if either is missing.
10. **Tests use a real LocalDB database, not SQLite or the EF in-memory provider.** The sync rules depend on SQL Server features (filtered unique index, `rowversion`). A fake database would pass tests that the real one fails. The cost: these tests run on Windows only. Plan 6 can switch CI to a SQL Server container.
11. **One shared `EventJson` options object, in the engine.** The client (Plan 3) and the server must agree on the exact JSON for events. Putting the options next to the event records means there's one copy. `System.Text.Json` ships with .NET, so the engine still has no package dependencies.

## Global Constraints

- Target `net10.0`. API in `src/CricketScorer.Api`, tests in `tests/CricketScorer.Api.Tests`, both added to `CricketScorer.slnx`. All NuGet packages on their latest stable `10.0.x` (use `dotnet add package <name>`; check the version it picked is 10.0.x). xUnit packages match the versions in `tests/CricketScorer.Engine.Tests`.
- The engine project stays free of ASP.NET, EF Core and any NuGet package. The API references the engine.
- **A short one-line `//` comment above every method** (including constructors, controller actions and test methods) saying what it does. No other comments unless something is genuinely tricky.
- **Secrets:** `ConnectionStrings:Default` and `Jwt:SigningKey` come from user-secrets (`dotnet user-secrets` on the API project) and are never written into `appsettings*.json`, `launchSettings.json`, `.http` files or tests. Before every commit run `git diff --cached` and search it for `Password=`, `SigningKey`, `Server=` and `User Id=`. The test factory generates its own random signing key at runtime.
- IDs are `Guid`. Teams, players, matches and events accept a **client-generated** ID (spec section 6) so records made offline sync without clashes.
- Errors use ProblemDetails (`AddProblemDetails`). Validation failures are 400, missing token 401, same-school-but-not-allowed 403, other school or missing 404, sync conflicts 409.
- All JSON uses `EventJson.Options` settings: camelCase, enums as strings, case-insensitive reads, and the polymorphic `type` property allowed anywhere in an object.
- Commits: no AI attribution lines. Never push; ask Tahir first. Work on branch `plan-2-database-api`.
- Run all tests with `dotnet test` from the repo root. The 129 engine tests must stay green.

## Review Focus

1. **A retried sync batch whose response was lost, containing adds and an undo** (`[add X@5, remove X]` sent twice, then `[add Y@5]`): the server ends with exactly Y at 5 and X removed once, no duplicates and no error. → Task 8, test `RetriedBatchWithUndoLeavesSameEvents`.
2. **Event JSON with `"type"` not first** (a hand-written Postman body, or a different serializer on the client) is accepted, not a 400 or 500. → Task 7, test `EventWithTypeAfterOtherFieldsDeserializes`.
3. **An event ID that already exists in a different match** (a GUID clash or a forged request) is rejected; it never moves or overwrites the other match's event. → Task 8, test `EventIdUsedInOtherMatchIsRejected`.
4. **A scorer removed from the school while holding a valid token** can no longer read or sync that school's matches. → Task 4, test `RemovedScorerLosesAccessImmediately`.
5. **Malformed sync bodies** (unknown event `type`, missing `event`, more than 500 operations) return 400 with a message, never 500. → Task 8, test `MalformedSyncBodiesReturn400`.

---

## File Structure

```
src/CricketScorer.Engine/Events/EventJson.cs      shared JSON options for events (Task 7)
src/CricketScorer.Api/
  CricketScorer.Api.csproj                         references the engine; UserSecretsId set
  Program.cs                                       service registration + pipeline; `public partial class Program;`
  appsettings.json                                 Logging + Jwt:Issuer/Audience only (no secrets)
  CricketScorer.Api.http                           sample requests for Rider/VS Code (no secrets)
  Infrastructure/ApiException.cs                   exception carrying a status code + message
  Infrastructure/ApiExceptionHandler.cs            IExceptionHandler -> ProblemDetails
  Infrastructure/StartupChecks.cs                  fails fast when secrets are missing
  Infrastructure/CodeGenerator.cs                  random join codes and share codes
  Data/AppDbContext.cs                             IdentityDbContext + model config
  Data/Entities/*.cs                               AppUser, School, Team, Player, Match, MatchPlayer, ScoringSession, StoredEvent
  Data/Migrations/                                 generated by dotnet-ef
  Auth/AuthController.cs, Auth/TokenService.cs, Auth/AuthDtos.cs
  Auth/CurrentUser.cs                              loads the signed-in user and their school per request
  Schools/SchoolsController.cs, Schools/SchoolDtos.cs
  Teams/TeamsController.cs, Teams/PlayersController.cs, Teams/TeamDtos.cs
  Matches/MatchesController.cs, Matches/MatchDtos.cs
  Matches/MatchLoader.cs                           match + setup + active events + replayed state
  Sync/SyncController.cs, Sync/SyncDtos.cs, Sync/SyncService.cs
  Scorecards/ScorecardController.cs, Scorecards/ScorecardBuilder.cs, Scorecards/ScorecardDtos.cs
tests/CricketScorer.Api.Tests/
  ApiFactory.cs                                    WebApplicationFactory on a throwaway LocalDB database
  ApiCollection.cs                                 xUnit collection sharing one ApiFactory
  TestApi.cs                                       helpers: register, create school, teams, match, sync
  StartupTests.cs, SchemaTests.cs, AuthTests.cs, SchoolTests.cs, TeamTests.cs,
  MatchTests.cs, EventJsonTests.cs, SessionTests.cs, SyncTests.cs, ScorecardTests.cs
```

---

### Task 1: API project, test factory and secrets

**Files:**
- Create: `src/CricketScorer.Api/*` (project, `Program.cs`, `appsettings.json`, `Infrastructure/ApiException.cs`, `Infrastructure/ApiExceptionHandler.cs`, `Infrastructure/StartupChecks.cs`)
- Create: `tests/CricketScorer.Api.Tests/ApiFactory.cs`, `ApiCollection.cs`, `StartupTests.cs`
- Modify: `CricketScorer.slnx`

**Interfaces:**
- Produces:
  - `public partial class Program;` (so tests can use `WebApplicationFactory<Program>`)
  - `sealed class ApiException(int statusCode, string message) : Exception` with `int StatusCode`
  - `static class StartupChecks { static void RequireSecrets(IConfiguration config); }` throws `InvalidOperationException` naming the missing key and the `dotnet user-secrets set ...` command to fix it. `Jwt:SigningKey` must be at least 32 characters.
  - `GET /api/health` → 200 `{ "status": "ok" }` (anonymous)
  - Test side: `sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime` with `string ConnectionString`, `HttpClient CreateClient()` (inherited). It sets `ConnectionStrings:Default` to `Server=(localdb)\MSSQLLocalDB;Database=CricketScorerTests_{Guid:N};Trusted_Connection=True;TrustServerCertificate=True` and `Jwt:SigningKey` to 64 random characters via `ConfigureAppConfiguration`, environment `Testing`. `[CollectionDefinition("Api")] class ApiCollection : ICollectionFixture<ApiFactory>`; every API test class is `[Collection("Api")]`.

- [ ] **Step 1: Scaffold.** `dotnet new webapi --use-controllers -o src/CricketScorer.Api -f net10.0`, `dotnet new xunit -o tests/CricketScorer.Api.Tests -f net10.0`, add both to `CricketScorer.slnx`, reference the engine from the API and the API from the tests, add `Microsoft.AspNetCore.Mvc.Testing` to the tests, delete the template WeatherForecast files and `UnitTest1.cs`. Run `dotnet user-secrets init --project src/CricketScorer.Api`.
- [ ] **Step 2: Write failing tests** in `StartupTests.cs`

```csharp
// Checks the API starts and answers the health check.
[Fact] public async Task HealthCheckReturnsOk()
{ var r = await factory.CreateClient().GetAsync("/api/health"); Assert.Equal(HttpStatusCode.OK, r.StatusCode); }

// Checks a missing signing key stops startup with a helpful message.
[Fact] public void MissingSigningKeyFailsFast()
{ var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "x" }).Build();
  var ex = Assert.Throws<InvalidOperationException>(() => StartupChecks.RequireSecrets(config));
  Assert.Contains("Jwt:SigningKey", ex.Message); Assert.Contains("dotnet user-secrets set", ex.Message); }

// Checks a signing key shorter than 32 characters is refused.
[Fact] public void ShortSigningKeyFailsFast() => /* SigningKey = "short" -> InvalidOperationException containing "32" */

// Checks a missing connection string stops startup.
[Fact] public void MissingConnectionStringFailsFast() => /* message contains "ConnectionStrings:Default" */

// Checks an ApiException becomes a ProblemDetails response with its status code.
[Fact] public async Task ApiExceptionBecomesProblemDetails()
// The factory adds an IStartupFilter that maps GET /test/throw -> throw new ApiException(418, "teapot") (test-only, never in Program.cs).
// Assert status 418, content-type application/problem+json, title == "teapot".
```

- [ ] **Step 3: Run** `dotnet test tests/CricketScorer.Api.Tests` → FAIL (types missing).
- [ ] **Step 4: Implement.** `Program.cs` calls `StartupChecks.RequireSecrets(builder.Configuration)` first, then `AddControllers()` (JSON options added in Task 7), `AddProblemDetails()`, `AddExceptionHandler<ApiExceptionHandler>()`, `AddOpenApi()`; pipeline `UseExceptionHandler()`, `MapOpenApi()` in Development only, `MapControllers()`. Health check is a tiny `HealthController`. `ApiExceptionHandler` writes `ProblemDetails { Status = ex.StatusCode, Title = ex.Message }`. `appsettings.json` holds only logging and `"Jwt": { "Issuer": "CricketScorer", "Audience": "CricketScorer" }`. `ApiFactory` creates the database in `InitializeAsync` (Task 2 swaps this to `Migrate`) and drops it in `DisposeAsync`.
- [ ] **Step 5: Set Tahir's dev secrets** (values never appear in files):

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Server=(localdb)\MSSQLLocalDB;Database=CricketScorer;Trusted_Connection=True;TrustServerCertificate=True" --project src/CricketScorer.Api
dotnet user-secrets set "Jwt:SigningKey" "<64 random characters, generated with [Convert]::ToBase64String((1..48 | % {Get-Random -Max 256}))>" --project src/CricketScorer.Api
```

- [ ] **Step 6: Run** `dotnet test` → all PASS (engine 129 + new). `dotnet run --project src/CricketScorer.Api` then `GET /api/health` → 200.
- [ ] **Step 7: Secrets check, then commit.** `git add -A`, then `git diff --cached | grep -inE "password=|signingkey|server=|user id="`. Every hit must be a key *name* (for example the `"Jwt:SigningKey"` string in `StartupChecks`) or the LocalDB test template in `ApiFactory` (no password); never a value from user-secrets. `git commit -m "Add API project, startup checks and test factory"`

---

### Task 2: Database model and first migration

**Files:**
- Create: `Data/AppDbContext.cs`, `Data/Entities/*.cs`, `Data/Migrations/*`
- Modify: `Program.cs`, `tests/.../ApiFactory.cs`
- Test: `tests/CricketScorer.Api.Tests/SchemaTests.cs`

**Interfaces:**
- Produces (entities, all in namespace `CricketScorer.Api.Data`):
  - `AppUser : IdentityUser<Guid>` + `string DisplayName` (max 80), `Guid? SchoolId`, `School? School`
  - `School { Guid Id; string Name (max 100); string JoinCode (8, unique); Guid AdminUserId; DateTimeOffset CreatedAt }`
  - `Team { Guid Id; Guid SchoolId; string Name (max 100); bool IsOpponent; List<Player> Players }`, unique `(SchoolId, Name)`
  - `Player { Guid Id; Guid TeamId; string Name (max 80) }`
  - `enum TossDecision { Bat, Bowl }`, `enum MatchStatus { Upcoming, Live, Finished }` (stored as strings)
  - `Match { Guid Id; Guid SchoolId; Guid OwnerUserId; string ShareCode (10, unique); DateOnly Date; string? Venue (max 100); Guid TeamAId; Guid TeamBId; Guid? TossWinnerTeamId; TossDecision? TossDecision; MatchFormat Format; int? OversPerSide; int? MaxOversPerBowler; int BallsPerOver; bool FreeHitsEnabled; bool DeclarationsAllowed; MatchStatus Status; Guid? ActiveSessionId; DateTimeOffset CreatedAt; byte[] Version ([Timestamp]); List<MatchPlayer> Players }`
  - `MatchPlayer { Guid MatchId; Guid TeamId; Guid PlayerId; int Order }`, key `(MatchId, PlayerId)`
  - `ScoringSession { Guid Id; Guid MatchId; string? DeviceLabel (max 60); DateTimeOffset StartedAt }`
  - `StoredEvent { Guid Id; Guid MatchId; int Sequence; Guid SessionId; string Type (max 40); string Json (nvarchar(max)); bool IsRemoved; DateTimeOffset CreatedAt; DateTimeOffset? RemovedAt }`, filtered unique index `(MatchId, Sequence)` **where `[IsRemoved] = 0`**
  - `AppDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>` with `DbSet`s `Schools, Teams, Players, Matches, MatchPlayers, ScoringSessions, Events`
- Foreign keys: **cascade** only for `Team→Players`, `Match→Players(MatchPlayer)`, `Match→ScoringSessions`, `Match→Events`. Every other FK (`AppUser→School` (SetNull), `Team→School`, `Match→School`, `Match→TeamA/TeamB`, `MatchPlayer→Player`, `School.AdminUserId`, `Match.OwnerUserId`) is `Restrict`, because SQL Server refuses multiple cascade paths. `Match.MatchPlayer→Team` has no FK (TeamId is checked in code).

- [ ] **Step 1: Write failing tests** in `SchemaTests.cs` (use `factory.Services.CreateScope()` to get `AppDbContext`, seed a school/user/teams/match directly)

```csharp
// Checks two live events can't share a sequence number in the same match.
[Fact] public async Task TwoActiveEventsCannotShareASequence() => /* insert 2 StoredEvent (same MatchId, Sequence 1, IsRemoved false) -> SaveChangesAsync throws DbUpdateException */

// Checks a removed event frees its sequence number for the next event.
[Fact] public async Task RemovedEventFreesItsSequence() => /* first has IsRemoved = true -> second saves fine */

// Checks the match row version changes on every save.
[Fact] public async Task MatchVersionChangesOnSave() => /* save, read Version, change Venue, save, Version differs */
```

- [ ] **Step 2: Run** → FAIL (no DbContext).
- [ ] **Step 3: Implement** entities and `OnModelCreating` as above; register `AddDbContext<AppDbContext>(o => o.UseSqlServer(config.GetConnectionString("Default")))` in `Program.cs`.
- [ ] **Step 4: Create the migration** `dotnet ef migrations add InitialCreate --project src/CricketScorer.Api --output-dir Data/Migrations`. Open it and check the filtered index has `filter: "[IsRemoved] = 0"` and no `onDelete: ReferentialAction.Cascade` on the Restrict list above. Change `ApiFactory.InitializeAsync` to `db.Database.MigrateAsync()` so tests prove the migration itself works.
- [ ] **Step 5: Run** `dotnet test` → PASS. Then `dotnet ef database update --project src/CricketScorer.Api` creates Tahir's dev database `CricketScorer`, visible in SSMS under `(localdb)\MSSQLLocalDB`.
- [ ] **Step 6: Secrets check, commit** `"Add database model and initial migration"`

---

### Task 3: Register, log in and JWT auth

**Files:**
- Create: `Auth/AuthController.cs`, `Auth/TokenService.cs`, `Auth/AuthDtos.cs`, `Auth/CurrentUser.cs`
- Modify: `Program.cs`
- Test: `tests/.../AuthTests.cs`, `tests/.../TestApi.cs`

**Interfaces:**
- Consumes: `AppUser`, `AppDbContext`, `ApiException`.
- Produces:
  - `POST /api/auth/register` body `RegisterRequest(string Email, string Password, string DisplayName)` → 200 `AuthResponse(string Token, DateTimeOffset ExpiresAt, UserDto User)`; duplicate email or weak password → 400 ValidationProblem with Identity's messages.
  - `POST /api/auth/login` body `LoginRequest(string Email, string Password)` → 200 `AuthResponse`; wrong email or password, or locked out → 401 with title `"Email or password is wrong."` (same message for all three, so it doesn't reveal which emails exist).
  - `GET /api/auth/me` → 200 `UserDto(Guid Id, string Email, string DisplayName, Guid? SchoolId)`.
  - `sealed class TokenService { string Create(AppUser user, out DateTimeOffset expiresAt); }` HMAC-SHA256, claims `sub` (user id), `email`, `name`; issuer/audience from `Jwt:Issuer`/`Jwt:Audience`; lifetime 7 days.
  - `sealed class CurrentUser` (scoped): `Guid UserId` (from `sub`), `Task<AppUser> GetAsync()` (401 ApiException if the user no longer exists), `Task<Guid> RequireSchoolIdAsync()` (403 `"Join or create a school first."` when `SchoolId` is null).
  - Identity options: `Password.RequiredLength = 8`, `RequireDigit/Lowercase/Uppercase/NonAlphanumeric = false`, `User.RequireUniqueEmail = true`, lockout on (5 tries, 5 minutes). JwtBearer: `MapInboundClaims = false`, validate issuer, audience, lifetime and key. A fallback policy requires an authenticated user, so every endpoint is locked unless marked `[AllowAnonymous]` (health, register, login).
  - Test helpers in `TestApi.cs`: `static async Task<Scorer> RegisterAsync(this ApiFactory f, string name = "Scorer")` → `record Scorer(HttpClient Client, Guid UserId, string Email, Guid? SchoolId = null)` with the bearer header already set (random `{guid}@test.local` email, password `"long-enough-pw"`); `static Task<T> ReadAsync<T>(this HttpResponseMessage r)` using `EventJson.Options` (until Task 7 exists, use `JsonSerializerDefaults.Web` and switch then).

- [ ] **Step 1: Write failing tests**

```csharp
// Checks a new scorer can register and gets a working token.
[Fact] public async Task RegisterReturnsTokenThatWorksOnMe() => /* register; GET /api/auth/me with token -> 200, Email matches, SchoolId null */
// Checks registering the same email twice is refused.
[Fact] public async Task DuplicateEmailIsRejected() => /* 400 */
// Checks a 7-character password is refused and a plain 8-character one is allowed.
[Fact] public async Task PasswordNeedsEightCharactersOnly() => /* "short12" -> 400; "abcdefgh" -> 200 */
// Checks logging in with the right password returns a token valid for about 7 days.
[Fact] public async Task LoginReturnsSevenDayToken() => /* ExpiresAt within 1 minute of now + 7 days */
// Checks a wrong password and an unknown email give the same 401 message.
[Fact] public async Task WrongPasswordAndUnknownEmailLookTheSame()
// Checks endpoints need a token unless marked anonymous.
[Fact] public async Task MeWithoutTokenIs401()
// Checks a token signed with a different key is refused.
[Fact] public async Task TokenWithWrongKeyIs401() => /* hand-build a JWT with another 64-char key -> 401 */
```

- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** as in Interfaces. Register Identity with `AddIdentityCore<AppUser>().AddEntityFrameworkStores<AppDbContext>()` (no cookie UI). Use `UserManager.CheckPasswordAsync` plus `AccessFailedAsync` / `IsLockedOutAsync` for the lockout.
- [ ] **Step 4: Run** `dotnet test` → PASS.
- [ ] **Step 5: Secrets check, commit** `"Add register, login and JWT auth"`

---

### Task 4: Schools, join codes and members

**Files:**
- Create: `Schools/SchoolsController.cs`, `Schools/SchoolDtos.cs`, `Infrastructure/CodeGenerator.cs`
- Test: `tests/.../SchoolTests.cs`; extend `TestApi.cs`

**Interfaces:**
- Consumes: `CurrentUser`, `AppDbContext`.
- Produces:
  - `static class CodeGenerator { const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; static string New(int length); }` using `RandomNumberGenerator.GetString`. (No 0/O/1/I, so codes read out loud or copied off a WhatsApp message don't get mistyped.)
  - `POST /api/schools` `CreateSchoolRequest(string Name)` → 201 `SchoolDto`; caller becomes admin and member. 409 if the caller already has a school.
  - `POST /api/schools/join` `JoinSchoolRequest(string JoinCode)` (trimmed, case-insensitive) → 200 `SchoolDto`; unknown code → 404; already in a school → 409.
  - `GET /api/schools/me` → `SchoolDto(Guid Id, string Name, bool IsAdmin, string? JoinCode, List<MemberDto> Members)`; `JoinCode` only filled in for the admin. `MemberDto(Guid UserId, string DisplayName, string Email, bool IsAdmin)`.
  - `POST /api/schools/me/join-code` (admin) → 200 `{ joinCode }` with a new code; old code stops working.
  - `DELETE /api/schools/me/members/{userId}` (admin) → 204; sets that user's `SchoolId` to null. Admin removing themselves → 400. Non-admin → 403. User not in this school → 404.
  - Join codes are 8 characters, regenerated on the rare unique-index clash.
  - Test helpers: `static async Task<Scorer> NewSchoolAdminAsync(this ApiFactory f, string school = "Westville")` (register + create school; returns the `Scorer` `with { SchoolId = ... }`), `static async Task<Scorer> JoinAsync(this ApiFactory f, Scorer admin)`.

- [ ] **Step 1: Write failing tests**

```csharp
// Checks creating a school makes the creator its admin and shows them the join code.
[Fact] public async Task CreatorIsAdminAndSeesJoinCode() => /* JoinCode length 8, all chars in Alphabet, IsAdmin true */
// Checks another scorer can join with the code, in lower case with spaces.
[Fact] public async Task ScorerJoinsWithCodeCaseInsensitive() => /* $" {code.ToLower()} " -> 200; GET me -> IsAdmin false, JoinCode null, 2 members */
// Checks a scorer can't be in two schools.
[Fact] public async Task CantJoinASecondSchool() => /* 409 */
// Checks resetting the join code stops the old one working.
[Fact] public async Task ResetJoinCodeInvalidatesOldCode() => /* old -> 404, new -> 200 */
// Checks only the admin can reset the code or remove members.
[Fact] public async Task NonAdminCantManageSchool() => /* 403 for both */
// Checks the admin can't remove themselves.
[Fact] public async Task AdminCantRemoveThemselves() => /* 400 */
// Checks a removed scorer loses access straight away even with a valid token. (Review Focus 4)
[Fact] public async Task RemovedScorerLosesAccessImmediately()
// admin removes member; member's existing client: GET /api/schools/me -> 403 "Join or create a school first.";
// after Task 6 exists, extend: member GET /api/matches/{id} of a school match -> 404.
// Checks a scorer without a school is told to join one.
[Fact] public async Task NoSchoolGets403() => /* GET /api/schools/me -> 403 */
```

- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** `dotnet test` → PASS.
- [ ] **Step 5: Secrets check, commit** `"Add schools, join codes and members"`

---

### Task 5: Teams and players

**Files:**
- Create: `Teams/TeamsController.cs`, `Teams/PlayersController.cs`, `Teams/TeamDtos.cs`
- Test: `tests/.../TeamTests.cs`; extend `TestApi.cs`

**Interfaces:**
- Consumes: `CurrentUser.RequireSchoolIdAsync()`.
- Produces:
  - `GET /api/teams` → `List<TeamSummaryDto(Guid Id, string Name, bool IsOpponent, int PlayerCount)>` for the caller's school, own teams first then opponents, each by name.
  - `GET /api/teams/{id}` → `TeamDto(Guid Id, string Name, bool IsOpponent, List<PlayerDto> Players)`; players by name. `PlayerDto(Guid Id, string Name)`.
  - `POST /api/teams` `CreateTeamRequest(Guid? Id, string Name, bool IsOpponent)` → 201 `TeamDto`. If `Id` is given and already exists in this school → 200 with the existing team (idempotent retry). If it exists in another school → 409. Duplicate name in the school → 409 `"A team with that name already exists."`.
  - `PUT /api/teams/{id}` `UpdateTeamRequest(string Name, bool IsOpponent)` → 200 `TeamDto`.
  - `POST /api/teams/{teamId}/players` `CreatePlayerRequest(Guid? Id, string Name)` → 201 `PlayerDto`, same idempotency rules as teams (the spec's "quick-add on the day").
  - `PUT /api/players/{id}` `UpdatePlayerRequest(string Name)` → 200 `PlayerDto`.
  - Names are trimmed; empty or too long → 400. Teams and players of another school → 404. Any scorer in the school may create and edit (spec section 6).
  - Test helper: `static async Task<(Guid TeamId, List<Guid> Players)> CreateTeamAsync(this Scorer s, string name, int players, bool isOpponent = false)`.

- [ ] **Step 1: Write failing tests**

```csharp
// Checks a team and its squad are shared with every scorer in the school.
[Fact] public async Task TeamsAreSharedWithinSchool() => /* admin creates team + 3 players; joined scorer GET /api/teams/{id} -> 3 players */
// Checks another school can't see or edit a team.
[Fact] public async Task OtherSchoolGets404() => /* GET, PUT, POST players -> 404 */
// Checks sending the same create twice with a client ID makes one team.
[Fact] public async Task CreateWithSameIdTwiceIsIdempotent() => /* second -> 200, GET /api/teams count 1 */
// Checks a client ID already used by another school is refused.
[Fact] public async Task IdFromAnotherSchoolIs409()
// Checks duplicate team names in one school are refused but allowed across schools.
[Fact] public async Task DuplicateNameRules()
// Checks blank names are refused.
[Fact] public async Task BlankNamesAre400() => /* "   " team and player -> 400 */
```

- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Secrets check, commit** `"Add teams and players"`

---

### Task 6: Creating and reading matches

**Files:**
- Create: `Matches/MatchesController.cs`, `Matches/MatchDtos.cs`, `Matches/MatchLoader.cs`
- Test: `tests/.../MatchTests.cs`; extend `TestApi.cs`

**Interfaces:**
- Consumes: Plan 1 `MatchSettings.ForFormat`, `MatchSetup(...).Validate()`, `MatchEngine.Replay`; `CodeGenerator.New(10)`.
- Produces:
  - `POST /api/matches` `CreateMatchRequest(Guid Id, DateOnly Date, string? Venue, Guid TeamAId, Guid TeamBId, List<Guid> TeamAPlayers, List<Guid> TeamBPlayers, Guid? TossWinnerTeamId, TossDecision? TossDecision, MatchFormat Format, int? OversPerSide, int? MaxOversPerBowler, int? BallsPerOver, bool? FreeHitsEnabled, string? DeviceLabel)` → 201 `MatchCreatedDto(Guid Id, string ShareCode, Guid SessionId)`.
    - Settings: start from `MatchSettings.ForFormat(Format, OversPerSide)` then apply any non-null override. Ranges: `BallsPerOver` 4–10; limited formats `OversPerSide` 1–100 and `MaxOversPerBowler` 1–`OversPerSide`; Timed ignores overs fields.
    - Player lists are in batting order and become `MatchPlayer` rows with `Order` 0..n-1. Every player must belong to that team (`Player.TeamId`), both teams must be in the caller's school (else 404), the toss winner must be one of the two teams. Then `MatchSetup.Validate()` (XI 2–15, no duplicates, not the same team) → 400 with its message.
    - The caller becomes owner, a first `ScoringSession` is created and set as `ActiveSessionId` (the creating device can sync straight away). Same `Id` sent again by the same owner → 200 with the same `MatchCreatedDto` (idempotent); used by anyone else → 409.
  - `GET /api/matches` → `List<MatchSummaryDto(Guid Id, DateOnly Date, string? Venue, string TeamAName, string TeamBName, MatchStatus Status, string OwnerName, bool IsOwner)>` for the school, newest date first, then newest created.
  - `GET /api/matches/{id}` → `MatchDetailDto(Guid Id, string ShareCode, DateOnly Date, string? Venue, LineupDto TeamA, LineupDto TeamB, Guid? TossWinnerTeamId, TossDecision? TossDecision, MatchSettings Settings, MatchStatus Status, Guid OwnerUserId, string OwnerName, bool IsOwner, Guid? ActiveSessionId, List<EventDto> Events)`; `LineupDto(Guid TeamId, string Name, List<PlayerDto> Players)` in batting order; `EventDto(int Sequence, MatchEvent Event)` (active events only, by sequence). Other scorers in the school can read it (read-only, spec section 6). `ActiveSessionId` is only filled in for the owner.
  - `sealed class MatchLoader` (scoped): `Task<LoadedMatch> LoadAsync(Guid matchId, Guid schoolId)` (404 if not in that school) → `record LoadedMatch(Match Match, MatchSetup Setup, List<StoredEvent> Events, MatchState State)`; `Events` are active only, by sequence; `State = MatchEngine.Replay(Setup, events)`. Also `Task<LoadedMatch> LoadByShareCodeAsync(string shareCode)` (404 if unknown). Event JSON is read with `EventJson.Options` (Task 7; until then, Events is always empty).
  - Test helper: `static async Task<TestMatch> CreateMatchAsync(this Scorer s, int xi = 11, MatchFormat format = MatchFormat.T20, int? overs = null)` → `record TestMatch(Guid Id, string ShareCode, Guid SessionId, Guid TeamA, List<Guid> A, Guid TeamB, List<Guid> B)` (creates two fresh teams, the second as an opponent).

- [ ] **Step 1: Write failing tests**

```csharp
// Checks a T20 match is created with the format's defaults and a 10-character share code.
[Fact] public async Task CreateT20MatchUsesDefaults() => /* GET detail: Settings (20, 4, 6, true, false), ShareCode length 10, Status Upcoming, IsOwner, ActiveSessionId == SessionId */
// Checks custom overs work out the bowler limit and overrides win.
[Fact] public async Task CustomOversAndOverrides() => /* CustomOvers 30 -> max 6; with MaxOversPerBowler 8 -> 8; BallsPerOver 8 -> 8 */
// Checks a player from another team can't be put in the XI.
[Fact] public async Task PlayerFromWrongTeamIs400()
// Checks the engine's setup rules are enforced (XI of 1, same team twice, player listed twice).
[Theory, InlineData("xi1"), InlineData("sameTeam"), InlineData("duplicate")] public async Task EngineSetupRulesAre400(string kind)
// Checks a team from another school can't be used.
[Fact] public async Task OtherSchoolsTeamIs404()
// Checks another scorer at the school can read the match but isn't the owner.
[Fact] public async Task SchoolMateSeesMatchReadOnly() => /* IsOwner false, ActiveSessionId null; appears in GET /api/matches */
// Checks another school can't see the match.
[Fact] public async Task OtherSchoolGets404()
// Checks sending the same create twice makes one match.
[Fact] public async Task CreateWithSameIdTwiceIsIdempotent()
// Checks out-of-range settings are refused.
[Theory, InlineData(3, null), InlineData(11, null), InlineData(6, 0)] public async Task BadSettingsAre400(int ballsPerOver, int? overs)
```
Also extend `RemovedScorerLosesAccessImmediately` (Task 4) with the match read → 404.

- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Secrets check, commit** `"Add match creation and match reads"`

---

### Task 7: Event JSON and scoring sessions (takeover)

**Files:**
- Create: `src/CricketScorer.Engine/Events/EventJson.cs`
- Modify: `Program.cs` (MVC JSON options), `Matches/MatchesController.cs`, `Matches/MatchLoader.cs`
- Test: `tests/CricketScorer.Engine.Tests/EventJsonTests.cs` (engine side), `tests/CricketScorer.Api.Tests/SessionTests.cs`

**Interfaces:**
- Produces:
  - `public static class EventJson { static void Configure(JsonSerializerOptions o); static readonly JsonSerializerOptions Options; static string Serialize(MatchEvent e); static MatchEvent Deserialize(string json); }`. `Configure` adds `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` and sets `AllowOutOfOrderMetadataProperties = true`. `Options` = `new(JsonSerializerDefaults.Web)` + `Configure`. `Program.cs` calls `AddControllers().AddJsonOptions(o => EventJson.Configure(o.JsonSerializerOptions))` so request bodies, responses and stored rows all use the same format.
  - `POST /api/matches/{id}/sessions` `StartSessionRequest(string? DeviceLabel)` (owner only; school mate → 403) → 201 `SessionStartedDto(Guid SessionId, List<EventDto> Events)`: creates a new `ScoringSession`, sets it as `Match.ActiveSessionId` (the previous device's session is now stale), and returns the latest active events so the new device downloads them first (spec section 9).
  - `MatchLoader` now deserialises `StoredEvent.Json` with `EventJson.Deserialize`.

- [ ] **Step 1: Write failing tests**

```csharp
// (engine) Checks every event type round-trips with camelCase names and string enums.
[Fact] public void EventsRoundTripWithWebOptions() => /* one of each of the 12 event types; Serialize then Deserialize equal (compare via Serialize); ball json contains "\"type\":\"ball\"" and "\"extra\":\"wide\"" */
// (engine) Checks the "type" property can come after other properties. (Review Focus 2)
[Fact] public void EventWithTypeAfterOtherFieldsDeserializes() => /* "{\"id\":\"...\",\"batRuns\":4,\"boundary\":true,\"type\":\"ball\"}" -> BallBowled with BatRuns 4 */
// (api) Checks taking over gives a new session and the existing events.
[Fact] public async Task TakeoverStartsNewSessionAndReturnsEvents() => /* seed 2 StoredEvent rows; new SessionId != old; Events count 2; GET detail ActiveSessionId == new */
// (api) Checks only the owner can start a scoring session.
[Fact] public async Task SchoolMateCantTakeOver() => /* 403 */
```
Seed events in `SessionTests` by inserting `StoredEvent` rows (JSON from `EventJson.Serialize`) through `AppDbContext`, because the sync endpoint doesn't exist until Task 8.

- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** `dotnet test` → PASS (engine tests included).
- [ ] **Step 5: Secrets check, commit** `"Add shared event JSON and scoring session takeover"`

---

### Task 8: The sync endpoint

**Files:**
- Create: `Sync/SyncController.cs`, `Sync/SyncDtos.cs`, `Sync/SyncService.cs`
- Test: `tests/.../SyncTests.cs`; extend `TestApi.cs`

**Interfaces:**
- Consumes: `MatchLoader.LoadAsync`, `MatchEngine.Validate/Apply/Replay`, `EventJson`.
- Produces:
  - `POST /api/matches/{id}/sync` body `SyncRequest(Guid SessionId, List<SyncOp> Ops)`; `SyncOp(string Kind, int? Sequence, MatchEvent? Event, Guid? EventId)` where `Kind` is `"add"` (needs `Sequence` and `Event`) or `"remove"` (needs `EventId`).
  - Response 200 `SyncResult(int Applied, int Skipped, int LastSequence, SyncRejection? Rejected)`; `SyncRejection(int Index, Guid? EventId, string Reason)`.
  - Errors, checked in this order: body invalid (missing fields, unknown kind, unknown event `type`, 0 or more than 500 ops) → 400; match not in school → 404; caller not owner → 403; `SessionId != Match.ActiveSessionId` → 409 ProblemDetails with title `"Another device has taken over scoring."` and extension `"code": "session_replaced"`, **nothing saved**; `DbUpdateConcurrencyException` or unique-index clash on save → 409 with `"code": "retry"`, nothing saved.
  - `sealed class SyncService { Task<SyncResult> ApplyAsync(LoadedMatch match, Guid sessionId, IReadOnlyList<SyncOp> ops); }` Processes ops in order against the replayed `state`, stops at the first rejection, saves once at the end (one transaction), and updates `Match.Status` from the final state: `Closed` → `Finished`, any active event → `Live`, else `Upcoming`. Saving touches the `Match` row so its `rowversion` catches two syncs racing.
  - Op rules (the heart of idempotency; this is the algorithm):

```
add (seq, e):
  row = events with Id == e.Id (any match, including removed)
  if row in another match            -> reject "That event ID is already used by another match."
  if row is removed                  -> skip   (already sent and undone)
  if row.Sequence == seq             -> skip   (duplicate send)
  if row exists                      -> reject "That event is already saved at position {row.Sequence}."
  if seq != lastSequence + 1         -> reject "Expected event {lastSequence + 1}."
  if MatchEngine.Validate(state, e) is string why -> reject why
  insert StoredEvent(Type = e.GetType().Name, Json = EventJson.Serialize(e), SessionId); state = Apply(state, e)
remove (id):
  row = events of this match with Id == id
  if none                            -> reject "Unknown event."
  if row is removed                  -> skip
  if row is not the last active event-> reject "Only the last event can be undone."
  mark IsRemoved, RemovedAt; state = Replay(setup, active events)
```
  - Test helpers: `static Task<HttpResponseMessage> SyncAsync(this Scorer s, TestMatch m, params SyncOp[] ops)` (uses `m.SessionId`), `static SyncOp Add(int seq, MatchEvent e)`, `static SyncOp Remove(Guid id)`, `static InningsStarted StartFor(TestMatch m)` (A[0], A[1] batting, B[^1] bowling).

- [ ] **Step 1: Write failing tests**

```csharp
// Checks a batch of valid events is saved and the match goes live.
[Fact] public async Task ValidEventsAreSavedAndMatchGoesLive() => /* start + 3 balls -> Applied 4, LastSequence 4; GET detail: 4 events, Status Live */
// Checks sending the same batch twice is harmless.
[Fact] public async Task SameBatchTwiceIsSkipped() => /* second -> Applied 0, Skipped 4, LastSequence 4 */
// Checks a retried batch containing an undo ends in the same place. (Review Focus 1)
[Fact] public async Task RetriedBatchWithUndoLeavesSameEvents() => /* [start@1, X@2, remove X] twice; then [Y@2] -> Applied 1; detail events = [start, Y] */
// Checks a gap in the sequence is rejected.
[Fact] public async Task SequenceGapIsRejected() => /* start@1, ball@3 -> Applied 1, Rejected.Index 1, Reason "Expected event 2." */
// Checks the engine's reason comes back for an illegal event and later ops are not applied.
[Fact] public async Task EngineRejectsIllegalEventAndStops() => /* start@1, a second InningsStarted@2, a valid ball@3 -> Applied 1, Rejected.Index 1, Reason "An innings is already in progress.", LastSequence 1 */
// Checks undo removes the last event and frees its sequence number.
[Fact] public async Task UndoRemovesLastEvent() => /* start, ball; remove ball -> LastSequence 1; add new ball @2 -> ok */
// Checks only the last event can be undone.
[Fact] public async Task UndoOfEarlierEventIsRejected()
// Checks an event ID from another match is refused. (Review Focus 3)
[Fact] public async Task EventIdUsedInOtherMatchIsRejected() => /* same InningsStarted Id sent to match 2 -> rejected; match 1 still has it at 1 */
// Checks a device whose session was taken over can't save anything.
[Fact] public async Task OldSessionGets409AndNothingSaved() => /* take over via sessions endpoint; old SessionId sync -> 409, code session_replaced; event count unchanged */
// Checks a school mate can't sync someone else's match.
[Fact] public async Task NonOwnerGets403()
// Checks broken request bodies are 400, not 500. (Review Focus 5)
[Fact] public async Task MalformedSyncBodiesReturn400() => /* unknown "type":"teleport"; add without event; kind "zap"; 501 ops; empty ops */
// Checks closing the match marks it finished.
[Fact] public async Task MatchClosedMarksFinished() => /* use a 2-player-XI CustomOvers 1-over match, play to a result with all-out, then MatchClosed -> Status Finished */
```

- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** `dotnet test` → PASS.
- [ ] **Step 5: Secrets check, commit** `"Add idempotent event sync with sessions"`

---

### Task 9: Scorecards (school and share link), wrap-up

**Files:**
- Create: `Scorecards/ScorecardController.cs`, `Scorecards/ScorecardBuilder.cs`, `Scorecards/ScorecardDtos.cs`, `CricketScorer.Api.http`
- Modify: `docs/superpowers/plans/2026-10-18-plan-1-rules-engine.md` roadmap row 2 → mark built
- Test: `tests/.../ScorecardTests.cs`

**Interfaces:**
- Consumes: `MatchLoader.LoadAsync` / `LoadByShareCodeAsync`, `MatchState`, `InningsState`, team and player names from `Match`, `Team`, `Player`.
- Produces:
  - `GET /api/matches/{id}/scorecard` (school members) and `GET /api/share/{shareCode}/scorecard` (`[AllowAnonymous]`, GET only, so a viewer can't change anything) → `ScorecardDto`.
  - `ScorecardDto(Guid MatchId, string TeamAName, string TeamBName, DateOnly Date, string? Venue, MatchStatus Status, string? ResultText, List<InningsCardDto> Innings, DateTimeOffset? LastUpdated)`; `LastUpdated` = latest event `CreatedAt`/`RemovedAt`.
  - `InningsCardDto(string BattingTeamName, int Runs, int Wickets, string Overs, decimal RunRate, int? Target, ExtrasDto Extras, List<BatterLineDto> Batting, List<string> DidNotBat, List<BowlerLineDto> Bowling, List<FallOfWicketDto> FallOfWickets, bool IsComplete)`.
  - `BatterLineDto(string Name, string HowOut, int Runs, int Balls, int Fours, int Sixes, decimal StrikeRate)`; `BowlerLineDto(string Name, string Overs, int Maidens, int Runs, int Wickets, decimal Economy, int Wides, int NoBalls)`; `ExtrasDto(int Wides, int NoBalls, int Byes, int LegByes, int Penalties, int Total)`; `FallOfWicketDto(int Wicket, int Score, string Name, string Overs)`.
  - `static class ScorecardBuilder { static ScorecardDto Build(LoadedMatch m, IReadOnlyDictionary<Guid,string> playerNames, IReadOnlyDictionary<Guid,string> teamNames); static string HowOut(BatterScore b, IReadOnlyDictionary<Guid,string> names); static string? ResultText(MatchResult? r, IReadOnlyDictionary<Guid,string> teamNames); }`
  - Strike rate = runs × 100 / balls (0 when no balls), economy = runs × ballsPerOver / legal balls (0 when none), both rounded to 2 places. Bowler `Overs` uses `InningsState.OversText`.
  - `HowOut` text (exact copy): batting → `"not out"`; Bowled `"b {bowler}"`; Caught `"c {fielder} b {bowler}"`, and when fielder == bowler `"c & b {bowler}"`; Lbw `"lbw b {bowler}"`; Stumped `"st {fielder} b {bowler}"`; HitWicket `"hit wicket b {bowler}"`; RunOut `"run out ({fielder})"` or `"run out"` without a fielder; ObstructingField `"obstructing the field"`; Other `"out"`; RetiredHurt `"retired hurt"`; RetiredOut `"retired out"`.
  - `ResultText` (exact copy): override text wins when set; Win by runs `"{team} won by {n} run(s)"`; Win by wickets `"{team} won by {n} wicket(s)"` (singular for 1); Tie `"Match tied"`; Draw `"Match drawn"`; NoResult `"No result"`; null → null.
  - `CricketScorer.Api.http`: register, login, create school, team, match, sync and scorecard requests using `@token = ...` variables (no real secrets).

- [ ] **Step 1: Write failing tests**

```csharp
// Checks every dismissal type produces the right "how out" text.
[Theory, MemberData(nameof(HowOutCases))] public void HowOutText(BatterScore b, string expected) /* one case per line in the HowOut copy above, incl. "c & b" */
// Checks the result wording, including singular run/wicket.
[Theory, ...] public void ResultTextWording() /* won by 1 run, won by 24 runs, won by 1 wicket, Match tied, Match drawn, No result, override */
// Checks a full innings' scorecard adds up: batter runs + extras = total, bowler runs = total minus byes, leg byes and penalties.
[Fact] public async Task ScorecardTotalsAddUp() => /* sync a short innings with a 4, a 6, a wide, a no-ball+1, a bye, a caught wicket; GET scorecard -> sums hold; HowOut "c X b Y"; DidNotBat lists the rest in batting order */
// Checks the share link works with no login and an unknown code is 404.
[Fact] public async Task ShareScorecardIsPublic() => /* anonymous client: GET /api/share/{code}/scorecard -> 200; /api/share/NOPE/scorecard -> 404 */
// Checks the share link can't be used to change the match.
[Fact] public async Task ShareLinkIsReadOnly() => /* anonymous POST /api/share/{code}/scorecard -> not 2xx (405); anonymous POST /api/matches/{id}/sync -> 401; event count unchanged */
// Checks another school can't read the scorecard by match ID.
[Fact] public async Task OtherSchoolGets404()
```

- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** `dotnet test` → all PASS (129 engine + all API tests).
- [ ] **Step 5: Manual check.** `dotnet ef database update`, run the API, walk through `CricketScorer.Api.http` (or Postman via `/openapi/v1.json`), then open the `CricketScorer` database in SSMS and confirm the `Events` table shows the rows (including an undone one with `IsRemoved = 1`).
- [ ] **Step 6: Mark Plan 2 built** in the Plan 1 roadmap table. Secrets check, commit `"Add scorecards and share link"`.

---

## Known gaps handed to later plans

- **Adding a substitute to the XI mid-match:** the engine's `MatchSetup` lineups are fixed and there's no event for it. The players endpoint already accepts quick-adds; wiring a substitute into a live match is a Plan 3/4 engine change.
- **Refresh tokens / re-login UX** when the 7-day token expires mid-sync: Plan 4 (events wait until sign-in).
- **CI on Linux:** the API tests need LocalDB (Windows). Plan 6 switches the test connection string to a SQL Server container.
- **Live push (SignalR)** of each synced event: Plan 5 hooks into `SyncService` after save.
