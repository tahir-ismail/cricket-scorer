# Cricket Scorer — Design

Date: 2026-10-18
Owner: Tahir Ismail
Status: complete, waiting for Tahir's approval

## 1. What this is and why

A ball-by-ball cricket scoring app for **South African high school cricket**. A scorer at the boundary records the game on a **phone (most of the time) or an iPad**. Parents and coaches follow the score live through a share link.

The reference is **WebCricket / WebSports** (websports.co.za), which Tahir's old high school uses. Tahir played and scored with it. In his words it was "simple and easy to use, just the UI needed some work". The goal is to **keep WebCricket's simple scoring flow and give it a modern 2026 look.**

It's also a **portfolio project**. It replaces a placeholder project row on tahir-ismail.github.io and has to be something Tahir can explain in interviews: the C# rules engine, the SQL data model, and the live updates.

## 2. Goals

1. A scorer can log in, set up a match, and score it ball by ball quickly with one hand on a phone.
2. The app applies the cricket rules automatically: overs, legal and illegal balls, strike rotation, bowler limits, all-out, targets, declarations, and results.
3. Scoring keeps working with **no signal** and syncs when the connection returns.
4. Anyone with a match's share link sees the live score update by itself, without refreshing.
5. Every ball is stored in SQL. Scorecards (and later player stats) come from that data.
6. The live demo can be hosted **for free long term, without depending on the Azure for Students credit**.

## 3. Non-goals (not in this version)

- Leagues, logs, fixtures calendars, other sports, news, ads, player bios and photos, video. These make WebSports a business, and they're out of scope here.
- Duckworth-Lewis-Stern or any automatic rain calculation. The scorer can change overs and target by hand.
- Wagon wheels, pitch maps, or tracking where each ball went.
- Multiple scorers on one match at the same time, or handing a match to another scorer.
- Native iOS or Android apps. It's an installable web app.

## 4. Anti-goals (would count as a failure even if it "works")

- **More taps per ball than WebCricket.** Scoring a normal ball (dot, 1, 2, 4, 6) must be **one tap**. Speed at the boundary matters more than how it looks.
- Losing balls when the signal drops, or the app freezing because it's offline.
- A scorecard that's wrong. If the totals don't add up, nothing else matters.
- A viewer being able to change a match.
- Secrets (connection strings, signing keys, passwords) committed to GitHub.

## 5. Users

| User | How they use it | Account? |
|------|-----------------|----------|
| **Scorer** (teacher, student, parent) | Sets up teams and matches, scores ball by ball. Phone or iPad, outside, maybe bad signal. | Yes. Email and password. |
| **Viewer** (parents, coaches, players) | Opens the share link and watches the live score and scorecard. | No. Read-only. |

A match belongs to the scorer who created it. Only that scorer can score or edit it.

## 6. Teams and players

- A scorer saves **teams** (for example "Westville U16A") and each team has a **squad list** of player names.
- When setting up a match, the scorer **ticks the playing XI** from the squad and sets the batting order. The XI can be any size from 1 to 15, because school games are sometimes short or have extra players.
- **Quick-add on the day:** a new name typed during setup (or mid-match, for example a substitute) is added to that team's squad for next time.
- The opposition is set up the same way: a saved team, or a new one typed in on the spot.
- A player belongs to one team. Moving a player between teams isn't needed in this version.
- **Ownership — shared within a school (Tahir's decision):** teams and players belong to a **school**, not to one scorer. All scorers at that school use the same teams and players, so one real player is one record and stats stay together.
  - A scorer either **creates a school** (and becomes its admin) or **joins one** with a join code the admin shares. A scorer belongs to one school. _(Claude's call.)_
  - Any scorer at the school can create and edit teams and players. The admin can remove scorers and reset the join code. _(Claude's call.)_
  - Opposition teams entered by a school are saved under that school as opponents. Linking them to another school's own records in the app isn't supported in this version. _(Claude's call.)_
  - Matches still belong to the scorer who created them for scoring purposes. Other scorers at the school can see them (read-only) in the school's match list. _(Claude's call.)_
- All records (teams, players, matches, innings, events) use IDs made on the device (GUIDs), so anything created offline syncs without clashes. _(Claude's call.)_
- Players are matched across games by being the same player record, so stats can be added later.

## 7. Match formats and rules

When a match is created, the scorer picks a format. The format sets the defaults, and the scorer can change them:

| Format | Overs per side | Max overs per bowler | Declarations | Possible results |
|--------|----------------|----------------------|--------------|------------------|
| T20 | 20 | 4 | No | Win / Tie / No result |
| 50 overs | 50 | 10 | No | Win / Tie / No result |
| Custom limited overs | Any (e.g. 30, 40) | overs ÷ 5, rounded up (editable) | No | Win / Tie / No result |
| Timed | No limit | No limit | Yes | Win / Draw / Tie / No result |

Match setup also records: date, venue (free text), the two teams, the toss winner and their choice, balls per over (default 6), and whether **free hits** apply after a no-ball (default on for limited overs, off for timed).

### Rules the engine must handle

- **Legal balls:** only legal deliveries count toward the over. Wides and no-balls don't count, and the over continues.
- **Extras:**
  - Wide: 1 run, plus any runs run or boundary. Not credited to the batter, not a ball faced, and charged to the bowler.
  - No-ball: 1 run, plus runs off the bat, which go to the batter. Counts as a ball faced. Charged to the bowler. The next ball is a free hit if enabled.
  - Bye and leg bye: runs go to the team only, not the batter or the bowler. Counts as a legal ball and a ball faced.
  - Penalty runs (5) can be awarded to either team by hand.
- **Strike rotation:** batters swap ends on odd runs, counted from the runs actually run (a boundary 4 or 6 doesn't swap). They also swap at the end of each over. The scorer can always manually swap the striker to fix mistakes.
- **Over change:** at the end of an over the scorer picks the next bowler. The previous over's bowler isn't allowed. A bowler who has reached the format's max overs isn't allowed.
- **Wickets:** bowled, caught, LBW, stumped, hit wicket, run out, retired hurt, retired out, obstructing the field, and "other". Each records the dismissed batter, plus the fielder where relevant (caught, run out, stumped).
  - A run out records which batter is out and how many runs were completed first. It can happen on a wide or a no-ball.
  - On a free hit, only a run out (and the other non-bowler dismissals) is allowed.
  - Stumped can happen on a wide. Bowled, caught, LBW and hit wicket can't happen on a no-ball.
  - A retired-hurt batter is not out and can come back later in the innings. Retired hurt doesn't count as a wicket. If the last available batter is out while someone is still retired hurt, the app asks the scorer whether that batter is coming back. If not, the innings ends. _(Claude's call, following the Laws.)_
  - Bowled, caught, LBW, stumped and hit wicket are credited to the bowler. Run out, retired and obstructing aren't.
  - After a wicket the scorer picks the incoming batter, with the next in the batting order suggested first.
- **End of innings:** all out (no batters left who can bat, which is 10 wickets with 11 players and adjusts to XI size), overs completed, target reached, declaration (timed only), or ended by hand (for example rain or bad light).
- **Second side's innings:** a target is shown (first total + 1) with runs needed and balls left (limited overs), plus the required run rate.
- **Results:**
  - Limited overs: won by X runs, won by X wickets, tie, or no result.
  - Timed: as above, plus **draw** when the side batting second didn't reach the target and wasn't all out.
  - The scorer can always override the result by hand with a short text note (for example "Westville won on first innings" or a "winning draw").
- **Innings model:** a match holds an ordered **list** of innings, not a fixed pair. Each innings records its batting side and its own overs and bowler limits (copied from the match settings, editable). _(Claude's call.)_
- **Extra innings (school cricket "play on"):** if the game finishes with time left, the scorer can tap **"Add another innings"** and both sides can bat again. Each extra innings is scored exactly like the first, with the same settings by default, and the batting order can be changed. **The first-innings result is the official result by default.** Extra innings have no automatic target (totals aren't added together). The scorer can optionally type in a target to chase. Extra innings show on the scorecard, and their runs and wickets count in stats. The scorer can override the result by hand. _(Target handling is Claude's call.)_
- Two innings each with totals added together (proper two-day cricket, follow-on) is **not** supported in this version. Tahir said timed games are usually one innings each.
- **Rain and changes:** the scorer can change the overs per side, the target, or end an innings at any point. No DLS.
- **Undo:** "undo last ball" is always one tap away and can be repeated back to the start of the innings. This is how mistakes get fixed. Editing a single ball further back isn't required in this version.

## 8. Screens

The scorer uses these screens on phone and iPad. Viewers only see the live match page.

1. **Login / sign up**, then create or join a school (first time only)
2. **My matches:** live, upcoming, and finished matches, plus a "New match" button.
3. **Teams:** list of teams. Open a team to see and edit its squad.
4. **New match setup:** format, teams, XIs and batting order, toss, settings. Short and step by step.
5. **Scoring screen** (the heart of the app):
   - Top: score and wickets, overs, run rate, and target / required rate when chasing.
   - Striker (marked) and non-striker with their runs and balls faced. Current bowler with their figures.
   - "This over": the balls so far as small chips (`1 · 4 · W · wd · 0`).
   - A big button grid: **0, 1, 2, 3, 4, 6**, **Wide, No-ball, Bye, Leg bye**, **Wicket**, and **Undo**. Extras and wickets open a small follow-up sheet (how many runs? which dismissal? which fielder?). Normal balls are a single tap.
   - Swap strike, change bowler, end innings / declare, and match menu.
   - A small sync indicator (synced / saving / offline, N balls waiting).
6. **Scorecard:** full batting and bowling cards for each innings, extras breakdown, fall of wickets, result.
7. **Public live match page** (share link, no login): live score, the current over, batters and bowler, recent balls, and the full scorecard. Updates live. Shows "last updated" and a clear "finished" state.

### Look and feel

- Modern, clean, high-contrast. Readable in bright sunlight. Light and dark mode.
- Big tap targets (at least 56px on phones) that you can reach with one thumb on the scoring screen.
- Clear colour coding: boundaries (4 and 6) and wickets stand out in the over chips and on the live page.
- Small animations in the style of Skyline Weather (score ticking up, a ball chip sliding in). They must never slow down scoring.
- Works in portrait on phones. On an iPad, the scoring screen can show the scorecard next to the buttons.
- **A mockup of the scoring screen is shown to Tahir for feedback before the UI is built.**

## 9. Offline and sync

- The scoring device saves every ball locally first (browser storage), so a ball is never lost if the signal drops.
- Balls are sent to the server in order, each with a client-generated ID and its sequence number in the innings. Sending the same ball twice must be harmless.
- If offline, scoring carries on normally and the sync indicator shows how many balls are waiting. They upload when the signal returns.
- An undo that's already synced is sent to the server as a removal of the last ball.
- Only one device scores a match at a time. If the scorer opens the same match on a second device, it shows read-only, with an option to "take over scoring". That device downloads the latest events first. _(Claude's calls below.)_
  - A takeover starts a new **scoring session** on the server. From then on, events from the old device are rejected.
  - If the old device had events that never synced, it shows a clear warning ("N balls weren't saved because another device took over"). It lists them so the scorer can re-enter them on the new device. They're never silently thrown away.
- **What works offline:** everything that happens during a match. That means balls, bowler and batter changes, quick-adding a player (for example a substitute), declaring, ending an innings, and undo. **Creating a new match needs a connection** (set it up before you leave for the ground). An expired login doesn't stop offline scoring. Events wait until the scorer is signed in again. _(Claude's call.)_
- **Everything in a match is an event:** balls, picking the next bowler, the incoming batter, manual strike swaps, penalty runs, changes to overs or target, declarations, ending an innings, adding an innings, and result overrides. They're all stored in the same ordered event list, synced the same way, and "undo" steps back through them. The rules engine rebuilds the match state from the events alone. _(Claude's call.)_
- Viewers just see the score catch up when the scorer's balls arrive.
- The app is installable to the home screen and loads without signal once it has been opened before.

## 10. How it's built (decided)

These were decided with Tahir, or are Claude's calls with the reasons given. Tahir handed the build to Claude.

- **Back end:** C# **ASP.NET Core Web API** (current LTS .NET).
- **Database:** **SQL Server** through **Entity Framework Core** (code-first migrations).
  - Development: SQL Server Express or LocalDB on Tahir's PC, viewable in SSMS.
  - Live demo: a host that stays free **without** the Azure for Students credit. The first choice is the Azure SQL Database free offer, and the limits get checked before deploying. EF Core keeps the database swappable, so changing host means changing a connection string, not the code.
- **Live updates:** **SignalR**. Viewers join a group for the match and receive each new ball and score update.
- **Front end — Claude's call: Blazor WebAssembly as an installable PWA,** not plain JavaScript as first suggested. The reason: offline scoring means the rules have to run on the phone. With Blazor, the **same C# rules engine** runs in the browser and on the server. There's one tested copy of the cricket rules instead of a JavaScript copy and a C# copy that can disagree, and the project shows more C#. The cost is a slower first load (a few MB, cached after that).
- **Rules engine:** a separate C# class library with no database or web code in it. It takes the match settings plus the list of balls and returns the full match state (score, batters, bowler figures, whose turn it is, what's allowed next, result). Both the front end and the API use it. The server re-checks each ball with it before saving.
- **Storage model:** balls are stored as an ordered list of events per innings. Scorecards and state are worked out from those events. Undo means removing the last event.
- **Auth:** ASP.NET Core Identity with email and password for scorers. Viewer pages need no login. Share links use a random, hard-to-guess code, not the database ID.
- **Tests:** xUnit tests for the rules engine covering every rule in section 7 (wides, no-balls, free hits, strike changes, over ends, bowler limits, all dismissal types, all-out with small XIs, targets, declarations, extra innings, results, undo). Plus a few API tests for saving balls, sync duplicates, and ownership.
- **Who writes it:** Claude builds the whole app (Tahir's choice) and explains the why behind each part as it goes, so Tahir can explain it in interviews.
- **Code style:** a short one-line comment above each method describing what it does. No other comments unless something is genuinely tricky.
- **Repo:** its own public GitHub repo under `tahir-ismail`, with a README and a screenshot. Work lives in `C:\Users\tahir\Projects\Cricket Scoring`.
- **Secrets:** connection strings and signing keys live in .NET user-secrets or a git-ignored local settings file, and are checked before every commit. Never committed.
- **Git:** no AI attribution in commits or PRs. Ask Tahir before every push.

## 11. Order of work (suggested)

1. Rules engine plus its tests. This is the core, and it's checkable without any UI.
2. Database and API (auth, teams, players, matches, balls, scorecard).
3. Scoring screen mockup → Tahir's feedback → front end for setup, scoring and scorecard.
4. Offline storage and sync.
5. Live share page with SignalR.
6. Deploy the free live demo, README, and swap in the portfolio row.
7. Later (next version): player stats across matches (runs, average, strike rate, wickets, economy) from SQL queries.

## 12. Left to the builder

- Exact table and column names, API routes, and the project or solution layout.
- The app's name (working name: "Cricket Scorer").
- Colours, fonts and exact layout within the look-and-feel rules above, after mockup feedback.
- Which browser storage to use for offline, and the exact sync protocol, within the rules in section 9.
- The exact free hosting service for the API and the front end, as long as it costs nothing after the student credit ends.
- How the scorer-side match list is sorted and filtered.
