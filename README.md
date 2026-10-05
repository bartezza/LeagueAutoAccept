# Automatic queue accepter for League of Legends
Ever wanted to go pee or grab something but you're already 10 minutes into the queue? LeagueAutoAccept is a C# console app made using the LCU API to automatically accept queue and more!

## Enhanced edition (by bartezza)
This fork adds a few improvements on top of the original:

**Added:**
- **Per-role setup** – configure a champion, backup champion and ban for each role (Top/Jungle/Mid/Bottom/Support); the one matching your assigned position is used.
- **Global ban list (3 slots)** – one shared ban list used by every role, so you only set your bans once. It bans the first champion still available, skipping any already banned or picked/hovered by a teammate.
- **Per-role ban overrides** – any role can flip on *Override global bans* to use its own 3-slot list instead of the global one.
- **Smarter backup pick** – if your champion gets taken (e.g. the enemy picks it), it falls back to your backup automatically.
- **Granular versioning** – full `MAJOR.MINOR.PATCH` version, shown on the main screen.

**Fixed:**
- **Backup pick on ban** – the backup champion is now selected when your pick is banned, not just when it's taken.
- **Pick order swaps** – teammate pick-order swap requests are now auto-accepted (previously ignored due to a wrong LCU endpoint).
- **Manual ban override** – selecting a different ban yourself is now kept (and locked) instead of being instantly replaced by the configured ban.
- **On-time locking** – pick/ban now reliably locks near the end of the turn instead of occasionally missing the lock window.
- **More reliable client detection** – no longer gets stuck when the client's auth data isn't ready yet.

## Screenshot
![LeagueAutoAccept Screenshot](screenshot.png?raw=true)

## Warnings/Disclaimer
- Be aware that the use of the LCU API is not allowed on the Korean server (which this application does)
- This application is not endorsed nor approved by Riot
- It's basically a gray area but should be fine to use on servers other than Korea

## Download/How to run
- Download the latest release from releases or from [here](https://github.com/sweetriverfish/LeagueAutoAccept/releases/latest/download/Leauge.Auto.Accept.exe)
- If the application fails to launch, chances are you don't have .NET 9 Runtime installed. You can find the latest version [here](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)

## Features
- Automatically accept queue
- Pick a champion
- Ban a champion
- Can instalock
- Pick summoner spells
- Send a chat message when entering lobby

## Planned
- Backup champion to pick/ban
- Automatic runes picker (from the premade sets)
- Manual runes creator, maybe

## Notes and stuff
- You can build your rune page via collection>runes. The last rune page you clicked is the currently selected one.
- Mac os is (currently) not supported
- Feel free to suggest stuff
- If it looks like I don't know what I'm doing that's probably because I don't know what I'm doing

## License
Distributed under the MIT License. See [LICENSE](LICENSE) for more information.
