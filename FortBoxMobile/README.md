# FortBox 3D – Mobile (Unity)

The PC game as an Android/iOS app: same login and accounts, same game server
and APIs, same maps, figures, guns and look. Mobile controls only.

## Open and build (Windows/Linux/Mac)

1. Install **Unity 2022.3 LTS** with **Android Build Support** (OpenJDK + SDK/NDK) from Unity Hub.
2. Unity Hub -> Open -> this folder. The first import takes a few minutes.
   On open the project applies its settings by itself (menu **FortBox -> Apply project settings**).
3. **FortBox -> Build Android APK** -> `Builds/FortBox3D.apk` (put it on your site as FortBox3D.apk).
   **FortBox -> Build Android App Bundle** makes the `.aab` for Google Play.
4. iOS: **FortBox -> Build iOS** makes an Xcode project; that last step needs a Mac.

Command line: `Unity -batchmode -quit -projectPath . -executeMethod FortBox.EditorTools.FortBoxBuild.BuildApk`

## Controls

| Control | What it does |
|---|---|
| Left side – drag | Move joystick (appears where your thumb lands) |
| Right side – drag | Look / aim |
| Big orange button | Shoot (drag while holding to aim at the same time) |
| Small orange button (top left) | Second shoot button |
| AIM | Aim down sights / zoom (scoped guns zoom 3x with a scope overlay) |
| JUMP | Jump |
| Lightning | Throw a grenade |
| Arrow circle | Reload |
| Ammo | Next weapon (weapons you own on your account) |
| Gear | Pause / sensitivity / leave match |

In the Unity editor you can test with WASD, right-mouse look, F to shoot, Space, E, Q, R, G.

## Friends, group chat, sound

- **Friends** (main menu): friends list with online status, players online now, incoming/sent
  requests (accept / decline / cancel), search players and add them, remove friends.
  New requests pop up as a toast with a sound and show as a count on the menu.
- **Group chat**: your groups, browse all groups, join by ID or invite link
  (`fortroyal2d://join-group/<id>`, same as PC), create a group, text channels, live messages
  (server push + 5 s backup poll), "is typing", @mention alerts, unread counts, copy invite
  link, leave, and owners can buy the 40/mo or 900/yr subscription that activates the group.
- **Sound**: the PC sound files - Background.mp3 music, every gun's own shot sound (Gun Pack
  guns use the same mapping as PC), looping flamethrower, explosions, game over, menu clicks -
  plus other players' and enemies' shots by distance, hit / kill markers, pickups, melee swing,
  hurt and jump. Music, effects and click volume are in Settings.

## Server

Uses the same backend as the PC client: `ws://77.90.42.18:1040/ws` and HTTP on the same port
(login `player_verify` with "remember this device" tokens, `player_create`, `email_send_code`,
servers / lobby / `start_match`, `match_input` + `match_state`, `match_grenade`, `shop_buy`,
`/shop-items`, leaderboards, `/api/coins/earn` for quest rewards, `accept_tos`).
Another server can be set with the gear on the sign-in screen.

Every match runs on the game server (singleplayer is a private one-player server),
so enemies, damage, coins and XP come from the server exactly as on PC, and phone
and PC players can join the same servers.

## Where things are

- `Assets/Resources/FortBox/` – data exported from the PC game: maps (meshes, ground,
  lighting, hills, scenery hitboxes), enemy/player figures, guns, first-person view models,
  textures, icons, fonts, thumbnails, weapon/quest/colour tables.
  Re-export after changing the PC maps: `export_fortbox.py <fortbox folder> <this project>`.
- `Assets/Resources/FortBox/shaders/` – ports of the PC GLSL (world, ground, sky, glow, fx).
- `Assets/FortBox/Scripts/` – Core (network, session, quests, settings), World (maps,
  figures, view model, effects), Game (match, HUD, touch controls, lobby), UI (screens).

## Auto-update

The app checks `GET /mobile/version` on your game server when it starts (and every 30 min in
the menus, plus Settings -> Check for updates). If a newer version is published it shows the
release notes, downloads the APK inside the app, checks its SHA-256 and opens Android's installer.
"Required" updates and versions below the minimum can't be skipped.

Publishing a new version:
- **Dashboard**: FortBox Dashboard -> **Mobile App** tab -> choose the APK, type a version higher
  than the last one (e.g. 1.0.2), notes, optional "required" -> Upload & publish.
- **Automatic from GitHub**: set `MOBILE_UPLOAD_TOKEN` on the server (any long random text, 16+
  characters) and the same value as a GitHub secret. Every cloud build (version 1.0.<build number>)
  then publishes itself.

Updates only install over an app signed with the same key: every build must use
`Signing/fortbox.keystore`. Its password is in `Signing/password.txt` - keep that file private
(it is git-ignored) and put the password in the GitHub secret `ANDROID_KEYSTORE_PASS`.
Back up both files: if the key is lost, players have to uninstall and reinstall once.

Website download link for the newest APK: `http://<server>/mobile/FortBox3D.apk`
