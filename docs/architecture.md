# Team integration contracts

## Boundaries

`Features/Accounts` owns identity/JWT and account endpoints. `Features/Rooms` owns seats, host identity, room codes, commands, snapshots, and realtime room notifications. `Features/Invitations` owns invitation inbox/actions. `Features/Games` owns the board model and the independent `IMovementEngine` implementation. `Data` owns EF mappings and migrations. `Infrastructure` owns error mapping, current-user extraction, and cookie CSRF protection.

Controllers and hubs call scoped services. Add new behavior through services and explicit DTOs rather than accessing controller internals or putting rules into JavaScript. The hub is notification-only; HTTP commands are the single path for mutations. Keep membership authorization and database concurrency checks on every new operation.

The persistent room currently has `HostId` (also the red player) and `BlackPlayerId`. When adding host transfer, introduce an independent red-player identity first; changing `HostId` alone would otherwise change red ownership. There is no spectator, timer, AI, chat or video implementation or placeholder signaling in this milestone.

## HTTP API

All request/response JSON uses camelCase. Except registration/login/CSRF, endpoints require a valid JWT. DTOs are documented in development Swagger. Error responses use ProblemDetails (`status`, `title`; invalid DTOs also have `errors`). API authorization returns HTTP 401/403 rather than HTML redirects.

| Method | Route | Request / behavior |
| --- | --- | --- |
| GET | `/api/auth/csrf` | Sets antiforgery cookie, returns `{ token }` |
| POST | `/api/auth/register` | `{ username, password }`; creates account and signs in |
| POST | `/api/auth/login` | `{ username, password }`; signs in |
| GET | `/api/auth/me` | `{ id, username }` |
| POST | `/api/auth/logout` | Revokes the user's JWTs through security-stamp rotation; clears cookie |
| GET | `/api/rooms` | Current player's room snapshots, most recently updated first |
| POST | `/api/rooms` | Creates initial room, returns snapshot |
| GET | `/api/rooms/{code}` | Member-only snapshot/load |
| POST | `/api/rooms/{code}/join` | Claims black seat or restores existing membership; snapshot |
| POST | `/api/rooms/{code}/moves` | `{ pieceId, row, col, expectedRevision }`; snapshot |
| POST | `/api/rooms/{code}/invitations` | Host sends `{ username }`; 204 |
| GET | `/api/invitations` | Pending `{ id, code, hostUsername, createdAt }[]` for current recipient |
| POST | `/api/invitations/{id}/accept` | Atomically claims room seat and consumes invite; snapshot |
| POST | `/api/invitations/{id}/decline` | Marks pending invite declined; 204 |

Login/registration return `{ user: { id, username }, accessToken, expiresAt }`, and set the `xiangqi_token` HttpOnly, SameSite=Strict cookie (Secure over HTTPS and in production). Browser code uses the cookie and does not persist/read the token. Bearer clients can use `Authorization: Bearer <accessToken>`. JWTs expire after eight hours; there are no refresh tokens. Logout revokes all that account's HTTP sessions. Existing realtime connections are closed by the current browser on logout and at token expiry; realtime payloads remain room snapshots, and writes always pass HTTP authentication.

For cookie/browser writes, fetch `/api/auth/csrf` and send its token in `X-CSRF-TOKEN`; fetch a new token after account changes. This includes login/registration. Bearer-only clients without the authentication cookie do not need antiforgery tokens. Login/registration have per-IP rate limiting, and five failed logins lock an account for five minutes.

Room codes are eight characters, normalized to uppercase; generated codes avoid ambiguous characters. Links use `/rooms/join/{code}` and lead to confirmation. There is no anonymous board lookup, user directory, or open room listing.

## Snapshots and concurrency

A `RoomState` contains `id`, `code`, `hostId`, `players: [{ id, username, color }]`, `board`, `status`, `revision`, and `updatedAt`. Colors are `do` / `den`. Room status is `waiting`, `active`, or `finished`.

Board JSON contains `schemaVersion: 1`, `pieces`, `turn`, `status`, and nullable `winner`. A piece contains stable `id`, `type`, `color`, asset `file`, `row`, and `col`. Types from the reference are `xe`, `ma`, `tuong` (elephant), `si`, `chu_tuong` (general), `phao`, and `tot`. Coordinates are zero-based rows 0–9 / columns 0–8 from top left, with black at the top. Captured pieces are absent from the array. Files map to the existing `/images/` assets.

Rooms and invitations use optimistic concurrency. A room mutation increments its revision; a move must supply the last observed revision. HTTP 409 means refetch state and let the player choose again. Never automatically retry a move following a timeout, because it may already have committed. EF transactions ensure seat claims, affected invitation statuses, and board updates are atomic. A claimed seat closes other pending invites; a room never gains more than two players.

The database is the source of truth. Broadcasts occur after commit; a failed broadcast is logged without undoing a saved move. Clients discard older revisions and reload on reconnect or uncertain responses. The UI also polls every 15 seconds to recover missed notifications. Snapshot schema changes require an explicit compatibility/migration decision before changing the engine.

## SignalR

The locally vendored Microsoft SignalR browser client is version 8.0.7. Hub endpoint: `/hubs/game`, authenticated by the browser cookie or an HTTP bearer header. It rejects cross-origin browser handshakes and does not accept query-string access tokens.

- `Subscribe(code)` verifies membership, joins that code's group, and returns a freshly loaded snapshot.
- `Unsubscribe(code)` leaves that room group.
- Server event `RoomUpdated(snapshot)` is sent only to the affected room group after commit.

Clients use HTTP for moves and invites. On reconnect, resubscribe and use the returned snapshot. Group names and connection IDs are internal transport details, never proof of authorization. A future spectator feature needs an explicit read authorization path; do not weaken current player checks to implement it.
