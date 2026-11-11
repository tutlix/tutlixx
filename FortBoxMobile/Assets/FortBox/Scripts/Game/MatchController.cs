// A running match: server-authoritative like the PC backend mode. Applies
// match_state snapshots (players, enemies, bullets, grenades, power-ups,
// cover, events), predicts your own movement at the server's speed, sends
// match_input over the persistent socket, and renders everything in first
// person with the PC game's 3D map, figures, view model and effects.
using System.Collections.Generic;
using UnityEngine;

namespace FortBox
{
    public class MatchController : MonoBehaviour
    {
        public static MatchController Active;

        // world constants (backend.js)
        const float WORLD_W = 3200f, WORLD_H = 1800f, TOP_BOUND = 60f, BOTTOM_BOUND = 84f, PSIZE = 32f, INSET = 6f;
        const float SPEED = 5f * 20f;                 // px per second per axis (5 px/tick at 20 Hz)
        const float FP_EYE = 40f, JUMP_DURATION = 0.45f, GUN_Z = 30f, BODY_BACK = 5.5f;
        const float PITCH_MIN = -1.3f, PITCH_MAX = 0.75f, ADS_ZOOM = 1.6f, ADS_SCOPE_ZOOM = 3.0f;

        int serverId;
        string mapId, difficulty;
        bool pvp;
        MapWorld world;
        ActorSet actors;
        Fx fx;
        Camera cam;
        ViewModelRig vm;
        Hud hud;
        GameObject hudCanvas;
        Transform coverRoot;
        string coverSig = "";
        readonly List<Rect> solids = new List<Rect>();
        readonly List<Rect> mapSolids = new List<Rect>();
        readonly List<Rect> coverRects = new List<Rect>();

        // local player
        float px, py;                                  // top-left, world px
        bool havePos, alive = true, shield, rapid, dmgBoost;
        int health = 100, maxHealth = 100, score, kills, deaths, lastHealth = -1, killStreak;
        float respawnIn;
        string weapon = "Pistol";
        List<string> weapons = new List<string>();
        int grenades;
        float yaw, pitch = -0.12f, adsT, zoom = 1f, jumpT = -1f, walkT, shake;
        bool moving;
        float startedAt;

        // shooting (client mirror of the server's fire timer, for the view model)
        float lastShot = -10f, reloadUntil = -1f, reloadStart;
        int mag = 12, magSize = 12;
        float swingT = -1f;

        // network
        int seq;
        float lastSend, lastInputHeartbeat;
        string lastInputSig = "";
        int lastTick = -1;
        float snapTime, snapInterval = 0.05f, lastSnapAt;
        bool ended;
        Dictionary<string, object> lastSnap;

        class Mover
        {
            public Vector2 prev, cur;
            public float w = 32, h = 32, anim, flashAt = -10f, seenAt;
            public string type, st, tag, weapon;
            public float? f;
            public bool aim, alive = true;
            public int hp, maxHp, health = 100, maxHealth = 100;
            public Vector2 Pos(float a) { return Vector2.Lerp(prev, cur, a); }
        }
        readonly Dictionary<string, Mover> bots = new Dictionary<string, Mover>();
        readonly Dictionary<string, Mover> others = new Dictionary<string, Mover>();
        readonly Dictionary<string, Color32> playerColors = new Dictionary<string, Color32>();
        readonly HashSet<string> colorAsked = new HashSet<string>();

        struct Shot { public string id, owner; public Vector2 p, v; }
        readonly List<Shot> bullets = new List<Shot>();
        readonly List<Shot> enemyShots = new List<Shot>();
        readonly Dictionary<string, Vector2> lastBulletPos = new Dictionary<string, Vector2>();
        struct Nade { public string id; public Vector3 p; public Vector2 v; }
        readonly List<Nade> nades = new List<Nade>();
        struct Pickup { public string id, type; public Vector2 p; }
        readonly List<Pickup> pickups = new List<Pickup>();

        static readonly Dictionary<string, Color> EnemyColors = new Dictionary<string, Color> {
            {"scout", Ui.C(110, 225, 120)}, {"soldier", Ui.C(90, 150, 255)}, {"heavy", Ui.C(235, 145, 60)},
            {"sniper", Ui.C(205, 95, 225)}, {"boss", Ui.C(255, 70, 70)} };
        static readonly Dictionary<string, string> EnemyLabels = new Dictionary<string, string> {
            {"scout", "Scout"}, {"soldier", "Soldier"}, {"heavy", "Heavy"}, {"sniper", "Sniper"}, {"boss", "Warlord"} };
        static readonly Dictionary<string, Color> PowerColors = new Dictionary<string, Color> {
            {"health", Ui.C(0, 255, 0)}, {"rapid_fire", Ui.C(255, 255, 0)}, {"shield", Ui.C(0, 100, 255)},
            {"damage_boost", Ui.C(255, 0, 255)}, {"star_charge", Ui.C(170, 120, 255)} };
        static Dictionary<string, object> weaponData, enemyWeapons;

        // ---- start / stop ------------------------------------------------------------
        public static void Begin(Dictionary<string, object> info)
        {
            if (Active != null) return;
            var go = new GameObject("Match");
            Active = go.AddComponent<MatchController>();
            Active.Init(info);
        }

        static void LoadData()
        {
            if (weaponData != null) return;
            var ta = Resources.Load<TextAsset>("FortBox/data/weapons");
            weaponData = ta != null ? Json.Obj(ta.text) : new Dictionary<string, object>();
            var te = Resources.Load<TextAsset>("FortBox/data/enemies");
            var e = te != null ? Json.Obj(te.text) : null;
            enemyWeapons = e != null ? (e.O("weapons") ?? new Dictionary<string, object>()) : new Dictionary<string, object>();
        }

        static Dictionary<string, object> W(string name) { LoadData(); return weaponData.O(name ?? "Pistol") ?? weaponData.O("Pistol") ?? new Dictionary<string, object>(); }
        static bool IsMelee(string name) { return W(name).Bool("melee") || name == "Fists"; }

        void Init(Dictionary<string, object> info)
        {
            LoadData();
            serverId = info.Int("server_id", Lobby.ServerId);
            mapId = Lobby.LocalMap(info.Str("map", Lobby.Map));
            pvp = info.Bool("pvp") || mapId == "pvp";
            difficulty = info.Str("difficulty", "normal");
            int tr = info.Int("tick_rate", 20);
            snapInterval = 1f / Mathf.Max(5, tr);
            startedAt = Time.time;

            App.I.ShowMenus(false);
            world = MapWorld.Build(mapId);
            if (world == null) { Toast.Show("Map data missing: " + mapId, Ui.Red); Quit(true); return; }
            world.transform.SetParent(transform, false);
            foreach (var o in world.pack.obstacles)
                if (o.Length >= 4) mapSolids.Add(new Rect(o[0], o[1], o[2], o[3]));
            var wd = info.O("world");
            if (wd != null) ApplyWorld(wd);

            var cgo = new GameObject("Main Camera");
            cgo.tag = "MainCamera";
            cgo.transform.SetParent(transform, false);
            cam = cgo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 900f;
            cam.allowMSAA = Settings.Quality >= 1;
            vm = ViewModelRig.Attach(cam);
            actors = new ActorSet(transform);
            fx = Fx.Create(transform);
            coverRoot = new GameObject("cover").transform;
            coverRoot.SetParent(transform, false);
            RebuildCover();

            // HUD canvas
            hudCanvas = new GameObject("HUD", typeof(RectTransform));
            hudCanvas.transform.SetParent(transform, false);
            hudCanvas.layer = 5;
            var c = hudCanvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 20;
            var sc = hudCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
            sc.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1600, 900);
            sc.matchWidthOrHeight = 1f;
            hudCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            hud = Hud.Create(hudCanvas.transform);

            // weapons you own (server checks ownership on every input)
            weapons.Clear();
            var owned = Session.OwnedWeapons();
            foreach (var kv in weaponData)
            {
                var wcfg = kv.Value as Dictionary<string, object>;
                if (wcfg == null || wcfg.Bool("admin")) continue;
                if (kv.Key == "Pistol" || kv.Key == "Fists" || owned.Contains(kv.Key)) weapons.Add(kv.Key);
            }
            if (weapons.Count == 0) weapons.Add("Pistol");
            string want = PlayerPrefs.GetString("fb_weapon", "Pistol");
            SetWeapon(weapons.Contains(want) ? want : weapons[0]);
            grenades = Session.Player.Int("grenades");
            hud.touch.SetGrenades(grenades);
            TouchControls.Aim = false;

            // spawn guess until the first snapshot (server picks the real spot)
            px = 140; py = 560;
            hud.Announce(pvp ? "PVP ARENA" : Lobby.MapLabel(mapId).ToUpperInvariant(), pvp ? "FIRST TO THE KILL TARGET WINS" : "SURVIVE THE WAVES", 3f);
            hud.ShowPause(false, () => Quit(true));
            Push.TryIdentify();
        }

        public void Quit(bool backToMenu)
        {
            if (Active == this) Active = null;
            if (!ended) Quests.Report("session_ended");
            Quests.Report("play_time", Mathf.RoundToInt(Time.time - startedAt));
            int sid = serverId;
            if (sid > 0 && Lobby.ServerId == sid) { var _ = Lobby.Leave(); }
            Destroy(gameObject);
            App.I.ShowMenus(true);
            if (backToMenu)
            {
                App.ClearHistory();
                App.Show<MenuScreen>(false);
            }
            var __ = Session.Refresh();
        }

        public void OnBackPressed()
        {
            if (ended) { Quit(true); return; }
            hud.ShowPause(!hud.Paused, null);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) { TouchControls.Move = Vector2.zero; TouchControls.Fire = false; SendInput(true); }
        }

        // ---- server messages -----------------------------------------------------------
        public void OnPush(string type, Dictionary<string, object> msg, Dictionary<string, object> data)
        {
            int sid = data.Int("server_id", msg.Int("id", serverId));
            if (sid != serverId) return;
            if (type == "match_state") ApplyState(data);
            else if (type == "match_over") MatchOver(data);
            else if (type == "lobby_closed" && !ended)
            {
                Toast.Show("The match was closed by its leader.", Ui.Amber, 4f);
                Quit(true);
            }
        }

        void ApplyWorld(Dictionary<string, object> wd)
        {
            var cov = wd.L("cover");
            if (wd.ContainsKey("cover"))
            {
                coverRects.Clear();
                foreach (var o in cov)
                {
                    var l = o as List<object>;
                    if (l == null || l.Count < 4) continue;
                    float w = l.F(2), h = l.F(3);
                    if (w <= 0 || h <= 0 || w > 900 || h > 900) continue;
                    coverRects.Add(new Rect(l.F(0), l.F(1), w, h));
                    if (coverRects.Count >= 64) break;
                }
                if (coverRoot != null) RebuildCover();
            }
            if (wd.ContainsKey("scenery"))
            {
                var sc = wd.L("scenery");
                if (sc.Count > 0)
                {
                    mapSolids.Clear();
                    foreach (var o in sc)
                    {
                        var l = o as List<object>;
                        if (l != null && l.Count >= 4) mapSolids.Add(new Rect(l.F(0), l.F(1), l.F(2), l.F(3)));
                    }
                }
            }
            solids.Clear();
            solids.AddRange(mapSolids);
            solids.AddRange(coverRects);
        }

        void RebuildCover()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in coverRects) sb.Append(r.x).Append(',').Append(r.y).Append(',').Append(r.width).Append(',').Append(r.height).Append(';');
            string sig = sb.ToString();
            if (sig == coverSig) return;
            coverSig = sig;
            Ui.Clear(coverRoot);
            var mat = fx != null ? fx.WorldMat : new Material(MapWorld.Sh("FortBoxWorld"));
            foreach (var r in coverRects)
            {
                var go = new GameObject("crate");
                go.transform.SetParent(coverRoot, false);
                float cx = r.x + r.width / 2, cy = r.y + r.height / 2;
                go.transform.localPosition = MapWorld.W2U(cx, cy, world.HeightPx(cx, cy));
                go.AddComponent<MeshFilter>().sharedMesh = Fx.CoverMesh(r.width, r.height);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
            }
        }

        void ApplyState(Dictionary<string, object> s)
        {
            int tick = s.Int("tick", -1);
            if (tick >= 0)
            {
                if (lastTick >= 0 && tick <= lastTick) return;      // out of order
                lastTick = tick;
            }
            float now = Time.time;
            if (lastSnapAt > 0) snapInterval = Mathf.Lerp(snapInterval, Mathf.Clamp(now - lastSnapAt, 0.03f, 0.2f), 0.2f);
            lastSnapAt = now;
            snapTime = now;
            lastSnap = s;
            var wd = s.O("world");
            if (wd != null) ApplyWorld(wd);

            // players
            var seenP = new HashSet<string>();
            foreach (var o in s.L("players"))
            {
                var p = o as Dictionary<string, object>;
                if (p == null) continue;
                string u = p.Str("username");
                if (string.IsNullOrEmpty(u)) continue;
                float x = (float)p.Num("x", 140), y = (float)p.Num("y", 560);
                if (u == Session.Username)
                {
                    health = p.Int("health", health);
                    maxHealth = Mathf.Max(1, p.Int("max_health", maxHealth));
                    bool wasAlive = alive;
                    alive = p.Bool("alive", health > 0);
                    shield = p.Bool("shield"); rapid = p.Bool("rapid"); dmgBoost = p.Bool("damage_boost");
                    int nk = p.Int("kills");
                    if (nk > kills) killStreak += nk - kills;
                    kills = nk;
                    int ns = p.Int("score");
                    if (ns > score) Quests.Report("score_milestone", ns);
                    score = ns;
                    int nd = p.Int("deaths");
                    if (nd > deaths) { killStreak = 0; }
                    deaths = nd;
                    respawnIn = (float)p.Num("respawn_in") / 1000f;
                    if (lastHealth >= 0 && health < lastHealth)
                    {
                        hud.Damage(lastHealth - health);
                        shake = Mathf.Min(1f, shake + 0.35f);
                    }
                    lastHealth = health;
                    if (!havePos || !wasAlive && alive) { px = x; py = y; havePos = true; }
                    else
                    {
                        // smooth reconciliation like the PC client
                        float dx = x - px, dy = y - py;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d > 400f) { px = x; py = y; }
                        else if (d > 4f) { px += dx * 0.25f; py += dy * 0.25f; }
                    }
                    string sw = p.Str("weapon", weapon);
                    if (sw != weapon && weapons.Contains(weapon) && sw == "Pistol" && weapon != "Pistol" && weapon != "Fists")
                    {
                        // the server refused this weapon (not owned there)
                        Toast.Show(weapon + " isn't available on the server - using Pistol", Ui.Amber);
                        SetWeapon("Pistol");
                    }
                    continue;
                }
                seenP.Add(u);
                Mover m;
                if (!others.TryGetValue(u, out m)) { m = others[u] = new Mover { prev = new Vector2(x, y), cur = new Vector2(x, y) }; AskColor(u); }
                m.prev = m.Pos(Alpha());
                m.cur = new Vector2(x, y);
                m.health = p.Int("health", 100);
                m.maxHealth = Mathf.Max(1, p.Int("max_health", 100));
                int oh = m.hp;
                m.hp = m.health;
                if (m.hp < oh) m.flashAt = now;
                m.alive = p.Bool("alive", m.health > 0);
                m.weapon = p.Str("weapon", "Pistol");
                m.seenAt = now;
            }
            foreach (var k in new List<string>(others.Keys)) if (!seenP.Contains(k)) others.Remove(k);

            // enemies
            var seenB = new HashSet<string>();
            bool hitSomething = false;
            foreach (var o in s.L("bots"))
            {
                var b = o as Dictionary<string, object>;
                if (b == null) continue;
                string id = b.Str("id");
                if (string.IsNullOrEmpty(id)) id = "b" + seenB.Count;
                seenB.Add(id);
                float w = (float)b.Num("w", 32), h = (float)b.Num("h", 32);
                var c = new Vector2((float)b.Num("x") + w / 2, (float)b.Num("y") + h / 2);
                Mover m;
                if (!bots.TryGetValue(id, out m)) m = bots[id] = new Mover { prev = c, cur = c };
                m.prev = m.Pos(Alpha());
                m.cur = c;
                m.w = w; m.h = h;
                m.type = b.Str("type", w >= 64 ? "boss" : "scout");
                m.st = b.Str("st");
                m.tag = b.Str("tag");
                m.f = b.ContainsKey("f") && b["f"] != null ? (float?)(float)b.Num("f") : null;
                m.aim = m.st == "a" || b.Bool("aim");
                int hp = b.Int("hp", 50);
                if (m.hp > 0 && hp < m.hp) { m.flashAt = now; if (now - lastShot < 0.6f) hitSomething = true; }
                m.hp = hp;
                m.maxHp = Mathf.Max(1, b.Int("max_hp", Mathf.Max(hp, 1)));
                m.seenAt = now;
            }
            foreach (var k in new List<string>(bots.Keys)) if (!seenB.Contains(k)) bots.Remove(k);
            if (hitSomething) hud.HitMarker(false);

            // bullets: those that vanished hit something -> sparks
            var newPos = new Dictionary<string, Vector2>();
            bullets.Clear();
            foreach (var o in s.L("bullets"))
            {
                var b = o as Dictionary<string, object>;
                if (b == null) continue;
                var sh = new Shot { id = b.Str("id"), owner = b.Str("owner"), p = new Vector2((float)b.Num("x"), (float)b.Num("y")), v = new Vector2((float)b.Num("vx"), (float)b.Num("vy")) };
                bullets.Add(sh);
                if (!string.IsNullOrEmpty(sh.id)) newPos[sh.id] = sh.p + sh.v;
            }
            foreach (var kv in lastBulletPos)
                if (!newPos.ContainsKey(kv.Key) && Near(kv.Value, 1600f))
                    fx.Hit(new Vector3(kv.Value.x, kv.Value.y, GUN_Z), new Color(1f, 0.85f, 0.5f), 5);
            lastBulletPos.Clear();
            foreach (var kv in newPos) lastBulletPos[kv.Key] = kv.Value;

            enemyShots.Clear();
            int n = 0;
            foreach (var o in s.L("enemy_bullets"))
            {
                var b = o as Dictionary<string, object>;
                if (b == null || n++ > 500) continue;
                enemyShots.Add(new Shot { owner = b.Str("k"), p = new Vector2((float)b.Num("x"), (float)b.Num("y")), v = new Vector2((float)b.Num("vx"), (float)b.Num("vy")) });
            }
            nades.Clear();
            foreach (var o in s.L("grenades"))
            {
                var g = o as Dictionary<string, object>;
                if (g == null) continue;
                nades.Add(new Nade { id = g.Str("id"), p = new Vector3((float)g.Num("x"), (float)g.Num("y"), (float)g.Num("z")), v = new Vector2((float)g.Num("vx"), (float)g.Num("vy")) });
            }
            // power-ups: one that vanished right next to you was yours
            var newPick = new List<Pickup>();
            foreach (var o in s.L("powerups"))
            {
                var p = o as Dictionary<string, object>;
                if (p == null) continue;
                newPick.Add(new Pickup { id = p.Str("id"), type = p.Str("type", "health"), p = new Vector2((float)p.Num("x") + 8, (float)p.Num("y") + 8) });
            }
            foreach (var old in pickups)
            {
                bool still = newPick.Exists(q => q.id == old.id);
                if (!still && Vector2.Distance(old.p, new Vector2(px + 16, py + 16)) < 48f)
                {
                    Quests.Report("powerup_collected");
                    hud.Announce(PowerLabel(old.type), "", 1.6f);
                }
            }
            pickups.Clear();
            pickups.AddRange(newPick);

            Events(s.L("events"));
        }

        static string PowerLabel(string t)
        {
            switch (t)
            {
                case "health": return "HEALTH RESTORED";
                case "rapid_fire": return "RAPID FIRE!";
                case "shield": return "SHIELD UP!";
                case "damage_boost": return "DOUBLE DAMAGE!";
            }
            return t.ToUpperInvariant();
        }

        bool Near(Vector2 p, float r) { float dx = p.x - (px + 16), dy = p.y - (py + 16); return dx * dx + dy * dy < r * r; }

        void Events(List<object> evs)
        {
            foreach (var o in evs)
            {
                var e = o as Dictionary<string, object>;
                if (e == null) continue;
                string t = e.Str("t");
                string u = e.Str("u");
                switch (t)
                {
                    case "kill":
                        {
                            string et = e.Str("e", "scout");
                            string label;
                            if (!EnemyLabels.TryGetValue(et, out label)) label = "Enemy";
                            Color col;
                            if (!EnemyColors.TryGetValue(et, out col)) col = Ui.C(235, 235, 235);
                            hud.Feed(u + " eliminated " + label, col);
                            fx.Kill((float)e.Num("x"), (float)e.Num("y"), col);
                            if (u == Session.Username)
                            {
                                hud.HitMarker(true);
                                Quests.Report("enemy_killed");
                                if (et == "boss") Quests.Report("boss_killed");
                                if (et != "scout" && et != "soldier") Quests.Report("special_killed");
                                Quests.Report("streak_reached", killStreak);
                            }
                            break;
                        }
                    case "wave":
                        {
                            int wv = e.Int("n");
                            hud.Announce("WAVE " + wv, e.Bool("boss") ? "BOSS INCOMING!" : "GET READY", 2.6f);
                            Quests.Report("wave_reached", wv);
                            break;
                        }
                    case "boss": hud.Announce("THE WARLORD HAS ARRIVED!", "", 3f); break;
                    case "down":
                        hud.Feed(u + " is down!", Ui.C(255, 110, 110));
                        break;
                    case "pk":
                        {
                            string k = e.Str("k"), v = e.Str("v");
                            hud.Feed((string.IsNullOrEmpty(k) ? "?" : k) + " eliminated " + v, k == Session.Username ? Ui.Orange : Ui.C(235, 235, 235));
                            if (k == Session.Username) { hud.HitMarker(true); Quests.Report("enemy_killed"); Quests.Report("streak_reached", killStreak); }
                            break;
                        }
                    case "respawn":
                        if (u == Session.Username) hud.Announce("RESPAWNED", "", 1.4f);
                        break;
                    case "boom":
                        {
                            float x = (float)e.Num("x"), y = (float)e.Num("y"), r = (float)e.Num("r", 110);
                            fx.Explosion(x, y, r);
                            float d = Vector2.Distance(new Vector2(x, y), new Vector2(px + 16, py + 16));
                            if (d < 600f) shake = Mathf.Min(1f, shake + (1f - d / 600f) * 0.8f);
                            break;
                        }
                    case "whit":
                        {
                            float x = (float)e.Num("x"), y = (float)e.Num("y");
                            fx.Explosion(x, y, (float)e.Num("r", 120));
                            break;
                        }
                    case "wev":
                        if (e.Str("ph") == "warn") hud.Announce(e.Str("type", "event").Replace('_', ' ').ToUpperInvariant(), "WORLD EVENT", 2.6f);
                        break;
                    case "left":
                        hud.Feed(u + " left the match", Ui.Text3);
                        break;
                }
            }
        }

        void MatchOver(Dictionary<string, object> d)
        {
            if (ended) return;
            ended = true;
            Quests.Report("session_ended");
            Quests.Report("wave_reached", d.Int("wave"));
            if (d.Bool("pvp") && d.Str("winner") == Session.Username) Quests.Report("mode_won");
            TouchControls.Fire = false;
            SendInput(true);
            bool solo = Lobby.Solo;
            string map = mapId, diff = difficulty;
            hud.ShowResults(d, Session.Username,
                solo ? (System.Action)(() => { Quit(false); var _ = Lobby.PlaySolo(map, diff); }) : null,
                () => Quit(true));
            var __ = Session.Refresh();
        }

        void AskColor(string user)
        {
            if (colorAsked.Contains(user)) return;
            colorAsked.Add(user);
            var t = Net.Rpc("get_player", new Dictionary<string, object> { { "username", user } });
            t.ContinueWith(r =>
            {
                var res = r.Result;
                var pl = res != null ? (res.O("player") ?? res.O("data")) : null;
                if (pl != null) { string cn = pl.Str("color", "blue"); MainThread.Post(() => playerColors[user] = Ui.PlayerColor(cn)); }
            });
        }

        float Alpha() { return Mathf.Clamp01((Time.time - snapTime) / Mathf.Max(0.02f, snapInterval)); }

        // ---- per frame -------------------------------------------------------------------
        void SetWeapon(string w)
        {
            weapon = w;
            PlayerPrefs.SetString("fb_weapon", w);
            var cfg = W(w);
            magSize = Mathf.Max(1, cfg.Int("mag_size", 12));
            mag = magSize;
            reloadUntil = -1f;
            swingT = -1f;
            bool melee = IsMelee(w);
            if (melee) TouchControls.Aim = false;
            hud.SetAmmo(w, mag, magSize, -1f, melee);
            reloadStart = Time.time - 1f;
        }

        bool Blocked(float nx, float ny)
        {
            float s = PSIZE - INSET * 2;
            var r = new Rect(nx + INSET, ny + INSET, s, s);
            foreach (var o in solids) if (r.Overlaps(o)) return true;
            return false;
        }

        void Update()
        {
            if (world == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            bool paused = hud.Paused || ended;
            var cfg = W(weapon);
            bool melee = IsMelee(weapon);

            // look
            if (!paused)
            {
                var look = TouchControls.Look;
                float sens = 2.9f * Settings.LookSens;
                if (adsT > 0.5f) sens *= Settings.AimSens / Mathf.Max(1f, zoom * 0.7f);
                yaw += look.x * sens;
                pitch += look.y * sens * (Settings.InvertY ? -1f : 1f);
                pitch = Mathf.Clamp(pitch, PITCH_MIN, PITCH_MAX);
                yaw = Mathf.Repeat(yaw + Mathf.PI, Mathf.PI * 2) - Mathf.PI;
            }

            // buttons
            if (TouchControls.PausePressed) hud.ShowPause(true, null);
            if (!paused)
            {
                if (TouchControls.JumpPressed && jumpT < 0f && alive) jumpT = 0f;
                if (TouchControls.WeaponPressed && weapons.Count > 1)
                {
                    int i = weapons.IndexOf(weapon);
                    SetWeapon(weapons[(i + 1) % weapons.Count]);
                    hud.Announce(weapon.ToUpperInvariant(), "", 0.9f);
                }
                if (TouchControls.ReloadPressed && !melee && mag < magSize && reloadUntil < 0f) StartReload(cfg);
                if (TouchControls.GrenadePressed) ThrowGrenade();
            }
            if (jumpT >= 0f) { jumpT += dt; if (jumpT >= JUMP_DURATION) jumpT = -1f; }

            // aim-down-sights zoom
            float want = (TouchControls.Aim && alive && !melee && !paused) ? 1f : 0f;
            adsT += (want - adsT) * Mathf.Min(1f, dt * 14f);
            if (Mathf.Abs(want - adsT) < 0.004f) adsT = want;
            bool scoped = cfg.Bool("scope");
            float top = scoped ? ADS_SCOPE_ZOOM : ADS_ZOOM;
            float sm = adsT * adsT * (3f - 2f * adsT);
            zoom = 1f + (top - 1f) * sm;

            // move: joystick relative to the view -> world direction -> server flags
            var mv = paused || !alive ? Vector2.zero : TouchControls.Move;
            bool left = false, right = false, up = false, down = false;
            if (mv.magnitude > 0.22f)
            {
                float ix = mv.x, iy = -mv.y;
                float fx_ = Mathf.Sin(yaw), fy_ = -Mathf.Cos(yaw);
                float rx = Mathf.Cos(yaw), ry = Mathf.Sin(yaw);
                float mx = rx * ix - fx_ * iy, my = ry * ix - fy_ * iy;
                float nrm = Mathf.Sqrt(mx * mx + my * my);
                if (nrm > 0) { mx /= nrm; my /= nrm; }
                left = mx < -0.38f; right = mx > 0.38f; up = my < -0.38f; down = my > 0.38f;
            }
            moving = left || right || up || down;
            if (moving) walkT += dt;
            if (alive && havePos)
            {
                float step = SPEED * dt;
                if (left) { float nx = Mathf.Max(0, px - step); if (!Blocked(nx, py)) px = nx; }
                if (right) { float nx = Mathf.Min(WORLD_W - PSIZE, px + step); if (!Blocked(nx, py)) px = nx; }
                if (up) { float ny = Mathf.Max(TOP_BOUND, py - step); if (!Blocked(px, ny)) py = ny; }
                if (down) { float ny = Mathf.Min(WORLD_H - BOTTOM_BOUND - PSIZE, py + step); if (!Blocked(px, ny)) py = ny; }
            }

            // shooting
            bool firing = TouchControls.Fire && alive && !paused;
            if (Settings.AutoFire && !firing && alive && !paused && !melee) firing = AimedAtEnemy();
            float now = Time.time;
            if (reloadUntil > 0f && now >= reloadUntil) { reloadUntil = -1f; mag = magSize; }
            bool reloading = reloadUntil > 0f;
            bool canShoot = firing && (melee || (!reloading && (mag > 0 || cfg.Bool("infinite_ammo"))));
            if (firing && !melee && !reloading && mag <= 0) StartReload(cfg);
            float delay = melee ? Mathf.Max(0.08f, cfg.Int("shoot_cooldown", 400) / 1000f * (rapid ? 0.6f : 1f) - 0.015f)
                                : (rapid ? 0.11f : 0.22f);
            if (canShoot && now - lastShot >= delay)
            {
                lastShot = now;
                if (melee) swingT = 0f;
                else if (!cfg.Bool("infinite_ammo")) mag = Mathf.Max(0, mag - 1);
                shake = Mathf.Min(1f, shake + (melee ? 0.05f : 0.08f));
            }
            if (swingT >= 0f) { swingT += dt / Mathf.Max(0.12f, cfg.Int("swing_ms", 220) / 1000f); if (swingT >= 1f) swingT = -1f; }

            // camera
            float hgt = world.HeightPx(px + 16, py + 16);
            float ez = FP_EYE + hgt + (jumpT >= 0f ? 20f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, jumpT / JUMP_DURATION)) : 0f);
            if (!alive) ez = 14f + hgt;
            float cx = px + 16, cy = py + 16;
            shake = Mathf.Max(0f, shake - dt * 2.5f);
            float sy = (Random.value - 0.5f) * shake * 0.02f, sp = (Random.value - 0.5f) * shake * 0.02f;
            cam.transform.position = MapWorld.W2U(cx, cy, ez);
            cam.transform.rotation = Quaternion.Euler(-(pitch + sp) * Mathf.Rad2Deg, (yaw + sy) * Mathf.Rad2Deg, alive ? 0f : 18f);
            float hfov = 2f * Mathf.Atan(Mathf.Tan(45f * Mathf.Deg2Rad) / zoom);
            cam.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(hfov / 2f) / cam.aspect) * Mathf.Rad2Deg;
            var aim = GroundPoint(new Vector3(cx, cy, ez), yaw, pitch, 700f);

            SendInputMaybe(left, right, up, down, canShoot, aim);

            // view model
            float shotAge = (now - lastShot) * 1000f;
            float tilt = 0f;
            if (reloading)
            {
                float k = Mathf.InverseLerp(reloadStart, reloadUntil, now);
                tilt = Mathf.Sin(k * Mathf.PI) * 28f;
            }
            Color32 shirt = Ui.PlayerColor(Session.Color);
            vm.UpdateGun(weapon == "Fists" ? null : weapon, shirt, yaw, pitch, moving, walkT, adsT, shotAge, tilt, swingT, alive && !(scoped && adsT > 0.85f));

            UpdateActors(new Vector3(cx, cy, ez), shirt);
            DrawFx(now, cx, cy);
            UpdateHud(cfg, melee, reloading, scoped);
        }

        bool AimedAtEnemy()
        {
            var a = Alpha();
            var eye = new Vector2(px + 16, py + 16);
            var fwd = new Vector2(Mathf.Sin(yaw), -Mathf.Cos(yaw));
            foreach (var b in bots.Values)
            {
                var p = b.Pos(a) - eye;
                float d = p.magnitude;
                if (d < 20f || d > 650f) continue;
                if (Vector2.Dot(p / d, fwd) > Mathf.Cos(Mathf.Atan2(b.w * 0.55f, d))) return true;
            }
            return false;
        }

        void StartReload(Dictionary<string, object> cfg)
        {
            reloadStart = Time.time;
            reloadUntil = Time.time + Mathf.Max(0.3f, cfg.Int("reload_ms", 1500) / 1000f);
        }

        Vector2 GroundPoint(Vector3 eye, float yw, float pt, float maxDist)
        {
            float fx_ = Mathf.Sin(yw), fy_ = -Mathf.Cos(yw);
            var pack = world.pack;
            if (pack.th != null)
            {
                float tp = Mathf.Tan(pt);
                for (float t = 12f; t < maxDist; t += 12f)
                    if (eye.z + tp * t <= pack.Height(eye.x + fx_ * t, eye.y + fy_ * t))
                        return new Vector2(eye.x + fx_ * t, eye.y + fy_ * t);
                return new Vector2(eye.x + fx_ * maxDist, eye.y + fy_ * maxDist);
            }
            float d = maxDist;
            if (pt < -0.01f) d = Mathf.Min(maxDist, eye.z / Mathf.Tan(-pt));
            return new Vector2(eye.x + fx_ * d, eye.y + fy_ * d);
        }

        void SendInputMaybe(bool l, bool r, bool u, bool d, bool shoot, Vector2 aim)
        {
            string sig = (l ? "1" : "0") + (r ? "1" : "0") + (u ? "1" : "0") + (d ? "1" : "0") + (shoot ? "1" : "0") + weapon + ((int)(aim.x / 6)) + "," + ((int)(aim.y / 6));
            float now = Time.time;
            bool changed = sig != lastInputSig;
            if ((changed && now - lastSend >= 1f / 30f) || now - lastInputHeartbeat >= 0.25f)
            {
                lastInputSig = sig;
                lastSend = lastInputHeartbeat = now;
                pendL = l; pendR = r; pendU = u; pendD = d; pendShoot = shoot; pendAim = aim;
                SendInput(false);
            }
        }
        bool pendL, pendR, pendU, pendD, pendShoot;
        Vector2 pendAim;

        void SendInput(bool stop)
        {
            if (serverId <= 0) return;
            Push.Send("match_input", new Dictionary<string, object> {
                { "server_id", serverId }, { "username", Session.Username }, { "token", Session.Token }, { "seq", ++seq },
                { "left", !stop && pendL }, { "right", !stop && pendR }, { "up", !stop && pendU }, { "down", !stop && pendD },
                { "shoot", !stop && pendShoot }, { "mouse_x", Mathf.RoundToInt(pendAim.x) }, { "mouse_y", Mathf.RoundToInt(pendAim.y) },
                { "weapon", weapon } });
        }

        async void ThrowGrenade()
        {
            if (!alive) return;
            if (grenades <= 0) { Toast.Show("No grenades - buy them in the Shop", Ui.Amber); return; }
            var target = GroundPoint(new Vector3(px + 16, py + 16, FP_EYE), yaw, Mathf.Max(pitch, -0.6f), 520f);
            var r = await Net.Rpc("match_grenade", new Dictionary<string, object> {
                { "username", Session.Username }, { "token", Session.Token }, { "server_id", serverId },
                { "target_x", Mathf.RoundToInt(target.x) }, { "target_y", Mathf.RoundToInt(target.y) } });
            if (this == null) return;
            if (r != null && r.ContainsKey("grenades")) grenades = r.Int("grenades");
            if (r == null || r.Str("status") != "ok")
            {
                string code = r != null ? r.Str("code") : "";
                if (code != "cooldown") Toast.Show(r != null ? r.Str("msg", "Grenade failed") : "Server unreachable", Ui.Amber);
            }
            Session.Player["grenades"] = grenades;
            hud.touch.SetGrenades(grenades);
        }

        static readonly List<ActorInfo> actorList = new List<ActorInfo>();

        void UpdateActors(Vector3 eye, Color32 shirt)
        {
            actorList.Clear();
            float a = Alpha();
            float dt = Time.deltaTime;
            foreach (var kv in bots)
            {
                var m = kv.Value;
                var p = m.Pos(a);
                bool mov = (m.cur - m.prev).sqrMagnitude > 0.5f;
                if (mov) m.anim += dt;
                string et = m.type;
                object wobj;
                string style = et, wpn = null;
                if (!enemyWeapons.ContainsKey(et)) { style = m.w >= 64 ? "boss" : "scout"; et = style; }
                if (enemyWeapons.TryGetValue(et, out wobj)) wpn = wobj as string;
                float? f = m.f;
                if (f.HasValue && mov && !m.aim) f = null;
                actorList.Add(new ActorInfo {
                    key = "e" + kv.Key, x = p.x, y = p.y, z = world.HeightPx(p.x, p.y), scale = m.w / 32f, style = style, weapon = wpn,
                    facing = f, aiming = m.aim, moving = mov, anim = m.anim + (Mathf.Abs(kv.Key.GetHashCode()) % 97) * 0.013f,
                    flash = Mathf.Clamp01(1f - (Time.time - m.flashAt) / 0.15f), tag = m.tag });
            }
            foreach (var kv in others)
            {
                var m = kv.Value;
                if (!m.alive) continue;
                var p = m.Pos(a) + new Vector2(16, 16);
                bool mov = (m.cur - m.prev).sqrMagnitude > 0.5f;
                if (mov) m.anim += dt;
                Color32 c;
                if (!playerColors.TryGetValue(kv.Key, out c)) c = Ui.PlayerColor("blue");
                actorList.Add(new ActorInfo {
                    key = "p" + kv.Key, x = p.x, y = p.y, z = world.HeightPx(p.x, p.y), style = "player", shirt = c,
                    weapon = m.weapon, aiming = true, moving = mov, anim = m.anim, flash = Mathf.Clamp01(1f - (Time.time - m.flashAt) / 0.15f) });
            }
            if (alive)
            {
                // your own body (torso and legs) under the camera
                float fx_ = Mathf.Sin(yaw), fy_ = -Mathf.Cos(yaw);
                float bx = px + 16 - fx_ * BODY_BACK, by = py + 16 - fy_ * BODY_BACK;
                actorList.Add(new ActorInfo {
                    key = "me", x = bx, y = by, z = world.HeightPx(bx, by) + (jumpT >= 0f ? 20f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, jumpT / JUMP_DURATION)) : 0f),
                    style = "player", shirt = shirt, local = true, facing = Mathf.Atan2(fy_, fx_), moving = moving, anim = walkT });
            }
            actors.Update(actorList, eye, new Vector2(px + 16, py + 16));
        }

        void DrawFx(float now, float cx, float cy)
        {
            float since = now - snapTime;
            float ticks = since * 20f;
            foreach (var b in bullets)
            {
                var p = b.p + b.v * ticks;
                if (b.owner == Session.Username && (p - new Vector2(cx, cy)).sqrMagnitude < 95f * 95f) continue;
                if ((p - new Vector2(cx, cy)).sqrMagnitude > 2200f * 2200f) continue;
                float spd = b.v.magnitude;
                if (spd < 0.01f) { fx.Glow(p.x, p.y, GUN_Z, 3f, Ui.C(255, 74, 74)); continue; }
                string wpn = b.owner == Session.Username ? weapon : (others.ContainsKey(b.owner ?? "") ? others[b.owner].weapon : "Pistol");
                var acc = W(wpn).L("accent");
                Color col = acc.Count >= 3 ? new Color((acc.F(0) + 255) / 510f, (acc.F(1) + 230) / 510f, (acc.F(2) + 170) / 510f) : Ui.C(255, 214, 120);
                float ln = Mathf.Min(45f, spd * 2f);
                bool big = wpn == "Plasma Cannon" || wpn == "Railgun" || wpn == "Grenade Launcher" || wpn == "Minigun";
                var dir = b.v / spd;
                fx.Streak(new Vector3(p.x - dir.x * ln, p.y - dir.y * ln, GUN_Z), new Vector3(p.x, p.y, GUN_Z), big ? 2.6f : 1.4f, col, 0.8f);
                fx.Glow(p.x, p.y, GUN_Z, big ? 4.5f : 2.2f, Ui.C(255, 244, 214), 0.9f);
            }
            foreach (var b in enemyShots)
            {
                var p = b.p + b.v * ticks;
                Color col;
                if (!EnemyColors.TryGetValue(b.owner ?? "", out col)) col = Ui.C(255, 120, 90);
                fx.Streak(new Vector3(p.x - b.v.x * 1.5f, p.y - b.v.y * 1.5f, 28f), new Vector3(p.x, p.y, 28f), 2.8f, col, 0.95f);
                fx.Glow(p.x, p.y, 28f, 3.6f, Ui.C(255, 245, 220));
            }
            foreach (var g in nades)
            {
                var p = new Vector2(g.p.x, g.p.y) + g.v * ticks;
                fx.Grenade(p.x, p.y, g.p.z + world.HeightPx(p.x, p.y));
            }
            foreach (var pu in pickups)
            {
                Color c;
                if (!PowerColors.TryGetValue(pu.type, out c)) c = Color.white;
                fx.Chest(pu.id, pu.p.x, pu.p.y, c);
            }
            // world-event tags: alien glow, bounty crown
            float a = Alpha();
            foreach (var m in bots.Values)
            {
                if (string.IsNullOrEmpty(m.tag)) continue;
                var p = m.Pos(a);
                float k = m.w / 32f;
                if (m.tag == "alien") fx.Glow(p.x, p.y, 24 * k, 30 * k, Ui.C(60, 255, 150), 0.3f + 0.1f * Mathf.Sin(now * 7f));
                else if (m.tag == "bounty" || m.tag == "event_boss")
                {
                    Color col = m.tag == "bounty" ? Ui.C(255, 210, 60) : Ui.C(255, 80, 70);
                    float hz = 60 * k + 4 * Mathf.Sin(now * 5f);
                    for (int j = 0; j < 6; j++)
                    {
                        float ang = j * 1.0472f + now * 1.667f;
                        fx.Glow(p.x + Mathf.Cos(ang) * 6 * k, p.y + Mathf.Sin(ang) * 6 * k, hz, 2.6f * k, col);
                    }
                    fx.Glow(p.x, p.y, 4, 26 * k, col, 0.35f);
                }
            }
        }

        void UpdateHud(Dictionary<string, object> cfg, bool melee, bool reloading, bool scoped)
        {
            hud.SetHealth(health, maxHealth, shield, rapid, dmgBoost);
            hud.SetScore(score, kills);
            var s = lastSnap;
            if (s != null)
            {
                if (pvp || s.Bool("pvp"))
                {
                    int target = s.Int("kill_target", 20);
                    float endsIn = (float)s.Num("ends_in") / 1000f;
                    endsIn = Mathf.Max(0f, endsIn - (Time.time - snapTime));
                    hud.SetStatus("FIRST TO " + target + " KILLS", string.Format("{0}:{1:00} LEFT  ·  YOUR KILLS {2}", (int)endsIn / 60, (int)endsIn % 60, kills));
                }
                else
                {
                    int wave = s.Int("wave", 1);
                    hud.SetStatus("WAVE " + wave, s.Bool("between_waves") ? "NEXT WAVE INCOMING..." : ("ENEMIES " + s.Int("enemies_alive") + " / " + Mathf.Max(s.Int("enemies_target"), s.Int("enemies_alive"))));
                }
            }
            else hud.SetStatus("CONNECTING", "waiting for the server...");
            float since = Time.time - lastSnapAt;
            hud.SetNet(lastSnapAt > 0 && since > 2f ? "CONNECTION LOST - RECONNECTING..." : (!Push.Connected ? "OFFLINE" : ""));

            float reload01 = reloading ? Mathf.InverseLerp(reloadStart, reloadUntil, Time.time) : -1f;
            hud.SetAmmo(weapon, mag, magSize, reload01, melee);
            float spread = (moving ? 8f : 0f) + Mathf.Clamp01(1f - (Time.time - lastShot) / 0.15f) * 6f;
            spread *= 1f - 0.8f * adsT;
            hud.SetCrosshair(spread, adsT > 0.6f, scoped && adsT > 0.85f, alive && !hud.Paused);
            if (!alive && !ended)
            {
                if (pvp) hud.SetDown(true, "ELIMINATED", respawnIn > 0f ? "Respawning in " + Mathf.CeilToInt(respawnIn) : "Respawning...");
                else hud.SetDown(true, "YOU ARE DOWN", others.Count > 0 ? "Your squad fights on - the match ends when everyone is down" : "The match is ending...");
            }
            else hud.SetDown(false, "", "");

            // health bars over enemies and players in view
            hud.BeginBars();
            float a = Alpha();
            var me = new Vector2(px + 16, py + 16);
            foreach (var m in bots.Values)
            {
                var p = m.Pos(a);
                if ((p - me).sqrMagnitude > 1400f * 1400f) continue;
                float top = m.w * 1.55f + 6f + world.HeightPx(p.x, p.y);
                var sp = cam.WorldToScreenPoint(MapWorld.W2U(p.x, p.y, top));
                Color col;
                if (!EnemyColors.TryGetValue(m.type ?? "", out col)) col = Ui.Red;
                if (m.hp < m.maxHp || m.type == "boss") hud.Bar(sp, m.hp / (float)m.maxHp, col, m.type == "boss" ? "WARLORD" : null);
            }
            foreach (var kv in others)
            {
                var m = kv.Value;
                if (!m.alive) continue;
                var p = m.Pos(a) + new Vector2(16, 16);
                if ((p - me).sqrMagnitude > 1600f * 1600f) continue;
                var sp = cam.WorldToScreenPoint(MapWorld.W2U(p.x, p.y, 58f + world.HeightPx(p.x, p.y)));
                hud.Bar(sp, m.health / (float)m.maxHealth, pvp ? Ui.Red : Ui.Green, kv.Key);
            }
            hud.EndBars();
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            if (world != null) Destroy(world.gameObject);
            if (actors != null) actors.Clear();
        }
    }
}
