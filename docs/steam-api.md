# Steam API — verified reference

Everything here was confirmed with live requests on 2026-07-25/26 rather than
recalled from memory. Re-verification commands are included at the bottom — if
reality stops matching this file, fix the file.

## Endpoints

| Method | Key | Parameters | Returns | Cache TTL |
|---|---|---|---|---|
| `IPlayerService/GetOwnedGames/v1` | yes | `steamid`, `include_appinfo=1`, `include_played_free_games=1` | library + playtime | every sync |
| `ISteamUserStats/GetSchemaForGame/v2` | yes | `appid`, `l` | achievement schema | 30 days |
| `ISteamUserStats/GetPlayerAchievements/v1` | yes | `steamid`, `appid`, `l` | unlocks + `unlocktime` | driven by playtime |
| `ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2` | **no** | `gameid` | global rarity | 7 days |
| `IPlayerService/GetRecentlyPlayedGames/v1` | yes | `steamid` | recently played | available, unused |
| `/profiles/<id>/?xml=1` (community) | **no** | — | persona name, avatar | onboarding |
| Image CDN | **no** | — | cover art, icons | forever |

API base URL: `https://api.steampowered.com/`

`GetRecentlyPlayedGames` is not called anywhere in the codebase: `GetOwnedGames`
already returns `playtime_2weeks` and `rtime_last_played` for every game, which
is the equivalent data this method would add.

### Parameter gotchas

- `GetGlobalAchievementPercentagesForApp` takes **`gameid`**, not `appid`. It
  is the only method in the API that does this.
- `include_appinfo=1` is mandatory for `GetOwnedGames`; without it the
  response contains bare appids with no names or icons.
- `l=russian` (or any locale) returns localized achievement names and
  descriptions.

## Error codes

Confirmed against live requests:

| Situation | HTTP | Body | Reaction |
|---|---|---|---|
| `GetOwnedGames` without key | 401 | HTML | require a key |
| `GetSchemaForGame` without key | **400** | HTML | require a key |
| `GetPlayerAchievements` without key | **400** | HTML | require a key |
| Invalid key | 401 | HTML `Access is denied` | mark key invalid, **do not retry** |
| Game without achievements | 400 | `Requested app has no stats` | set `has_achievements=false` permanently |
| Private profile | **200** | `{"response":{}}` | privacy diagnostics |
| Rate limited | 429 | — | back off using `Retry-After` |

Two conclusions that shape the client:

1. **Status codes are inconsistent.** A missing key yields 400 or 401
   depending on the method, so errors cannot be classified by status alone.
2. **Errors arrive as HTML, not JSON.** The body literally starts with
   `<html><head><title>Unauthorized</title>`. The deserializer must survive
   this, otherwise users see a `JsonException` instead of "invalid key".

Separately: **400 does not always mean we made a mistake.** For
`GetPlayerAchievements` it is the normal response for "this app has no stats",
which applies to 30-40% of a typical library. Without persisting
`has_achievements=false`, every sync wastes hundreds of requests on
soundtracks, demos and utilities.

## What is closed off

- **Community XML for libraries.** `/profiles/<id>/games?tab=all&xml=1`
  redirects anonymous callers to `/login` (HTTP 302). It used to work without
  a key; it no longer does. Verified against two public profiles, with and
  without a browser User-Agent — the UA is not the cause.
- Only `/profiles/<id>/?xml=1` remains public: persona name, avatar, online
  state. Used during onboarding for the "is this you?" step. Re-verified
  2026-07-26.

  The document's root element is `<profile>`, and every text field is wrapped
  in `CDATA`. The two fields onboarding uses are `<steamID>` (the persona name,
  *not* the id) and `<avatarFull>`.

  A `privacyState` of `friendsonly` still returns the name and the avatar, so
  privacy does not have to be treated as a failure.

  **A profile that does not exist answers HTTP 200**, with
  `<response><error>The specified profile could not be found.</error></response>`.
  The status code carries no information here; a parser has to branch on the
  root element.

## Limits

- 100,000 requests per day per key. A full sync of a 1500-game library spends
  about 2700, so the daily quota is not the constraint — throughput is.
- The practical ceiling is around 5 requests per second; beyond that 429s
  begin.

## Image CDN

All verified, served with HTTP 200 and no authentication:

```
https://cdn.cloudflare.steamstatic.com/steam/apps/<appid>/library_600x900.jpg   grid cover
https://cdn.cloudflare.steamstatic.com/steam/apps/<appid>/header.jpg            game screen banner
https://cdn.cloudflare.steamstatic.com/steam/apps/<appid>/capsule_231x87.jpg    compact card
```

Achievement icons arrive as absolute URLs inside the schema, in two variants:
colored (`icon`) and greyed out for locked ones (`icongray`).

## DLC

Splitting achievements into base game and DLC **does not exist** at the data
level in Steam. Verified:

- Witcher 3 (292030): 78 achievements on the base game, exactly matching
  `achievements.total`. All 22 DLC appids (`355880`, `378648`, `378649`, …)
  have no achievement pages at all — zero entries.
- Stellaris (281990): 219 achievements in a single flat list with no DLC
  markers.

Neither `GetSchemaForGame` nor `store/appdetails` exposes a DLC flag.

## Re-verification commands

```bash
# Public method list with exact parameter signatures
curl -s "https://api.steampowered.com/ISteamWebAPIUtil/GetSupportedAPIList/v1/" | python3 -m json.tool

# Global rarity (no key) — note gameid, not appid
curl -s "https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid=440"

# Public profile (no key)
curl -s "https://steamcommunity.com/profiles/76561197960287930/?xml=1"

# Error codes without a key
curl -s -o /dev/null -w "%{http_code}\n" "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?steamid=76561197960287930"
```
