# Steam refresh token helper

One-off CLI to log into Steam and print a refresh token for the backend's
`Steam__MarketRefreshToken` setting (see `SteamAccessTokenProvider` in
`LootBase.Infrastructure`). Only needed once, or again if the token gets
revoked (password change, "log out all devices", etc.) - the backend rotates
and persists it on its own after that.

## Usage

```bash
cd scripts/steam-refresh-token
npm install
node get-refresh-token.js
```

Enter your Steam username and password (input is hidden), then confirm the
login on your phone (Steam Mobile App push) or type the Steam Guard code if
prompted. Nothing is sent anywhere except Steam itself - this talks directly
to Steam's login API, not the LootBase backend.

Copy the printed refresh token into `Steam__MarketRefreshToken` in `.env`,
then restart the backend container.
