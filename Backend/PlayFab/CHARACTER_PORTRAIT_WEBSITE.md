# Character portrait website handoff

Unity renders the saved wardrobe look as a transparent 512×512 PNG and stores
it on the signed-in player's `title_player_account` Entity Files profile.

- Entity file: `characterPortrait.png`
- Upload trigger: the player presses **Save Look**
- Replacement behavior: every later save atomically replaces the same filename
- Access: private; the website must use the signed-in player's entity token
- No PlayFab developer secret is required in the browser

The dashboard projection exposes these private User Data keys:

- `CharacterPortraitFile`
- `CharacterPortraitUpdatedAt`
- `CharacterPortraitChecksum`

Do not use or persist `CharacterPortraitUrl`. PlayFab file download URLs are
temporary and should be requested when the profile/dashboard loads.

## Website retrieval flow

1. Keep `EntityToken.EntityToken`, `EntityToken.Entity.Id`, and
   `EntityToken.Entity.Type` from the PlayFab login result. If the site did not
   retain them, request a fresh entity token using the signed-in client session.
2. Read `CharacterPortraitFile` with the existing `Client/GetUserData` call.
3. Call the PlayFab Data API:

```http
POST https://17FA03.playfabapi.com/File/GetFiles
X-EntityToken: <the signed-in player's entity token>
Content-Type: application/json

{
  "Entity": {
    "Id": "<EntityToken.Entity.Id>",
    "Type": "<EntityToken.Entity.Type>"
  }
}
```

4. Read `data.Metadata["characterPortrait.png"].DownloadUrl` from the response
   and use that temporary URL as the character `<img>` source.
5. Fall back to the default character art when the metadata or download URL is
   missing. Request a new URL after a page reload or an image authorization
   failure. Use `CharacterPortraitChecksum` or `CharacterPortraitUpdatedAt` as
   the React/query cache key so a newly saved look replaces the cached image.

The website must only request the current signed-in player's entity file. It
must never receive the PlayFab developer secret or the encrypted full save.
