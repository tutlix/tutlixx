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
