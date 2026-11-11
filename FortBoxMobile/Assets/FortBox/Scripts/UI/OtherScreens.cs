// Multiplayer server list, lobby, shop (with a 3D gun preview), daily
// quests, leaderboards, profile and settings.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FortBox
{
    // ---------------------------------------------------------------------------
    public class ServersScreen : SubScreen
    {
        RectTransform list, createBox;
        Text empty;
        InputField cName, cPass;
        int cMax = 4;
        string cMap = "citadel";
        Text cMapLabel, cMaxLabel;
        bool loading;

        public override void Build()
        {
            Frame("Multiplayer", "users");
            var bar = Ui.Node(body, "bar").Place(0, 1, 1, 1, 0, -64, 0, 0);
            bar.HList(12, 0, true);
            Ui.Label(bar, "Join a server or host your own. Matches run on the FortBox server - PC and phone players play together.", 18, Ui.Text2, TextAnchor.MiddleLeft, Ui.Regular).rectTransform.Size(-1, -1, 1);
            Ui.Button(bar, "REFRESH", Ui.Style.Secondary, "refresh", Load, 17).GetComponent<RectTransform>().Size(180, -1);
            Ui.Button(bar, "HOST SERVER", Ui.Style.Primary, "plus", () => { createBox.gameObject.SetActive(true); createBox.SetAsLastSibling(); }, 17).GetComponent<RectTransform>().Size(220, -1);
            ScrollRect sr;
            list = Ui.Scroll(body, out sr, 10);
            sr.GetComponent<RectTransform>().Place(0, 0, 1, 1, 0, 0, 0, 80);
            empty = Ui.Label(body, "", 22, Ui.Text3, TextAnchor.MiddleCenter, Ui.Semi);
            empty.rectTransform.Place(0, 0, 1, 1, 0, 0, 0, 80);

            // host dialog
            createBox = Ui.Node(root, "create").Fill();
            var dim = Ui.Img(createBox, new Color(0.03f, 0.035f, 0.045f, 0.82f), null, "dim");
            dim.rectTransform.Fill(); dim.raycastTarget = true;
            var p = Ui.Panel(createBox, 0.98f, Ui.Orange);
            p.Box(0.5f, 0.5f, 0, 0, 720, 600);
            p.pivot = new Vector2(0.5f, 0.5f);
            var col = Ui.Node(p, "col").Fill(28);
            col.VList(14, 0);
            Ui.Section(col, "Host a server", Ui.Orange);
            cName = Ui.Input(col, "Server name");
            cName.GetComponent<RectTransform>().Size(-1, 58);
            cPass = Ui.Input(col, "Password (optional - makes it private)");
            cPass.GetComponent<RectTransform>().Size(-1, 58);
            var mapRow = Ui.Node(col, "map"); mapRow.Size(-1, 58); mapRow.HList(10, 0, true);
            Ui.Button(mapRow, "", Ui.Style.Secondary, "back", () => CycleMap(-1), 18).GetComponent<RectTransform>().Size(70, -1);
            cMapLabel = Ui.Label(mapRow, "", 22, Ui.Text, TextAnchor.MiddleCenter, Ui.Bold);
            cMapLabel.rectTransform.Size(-1, -1, 1);
            Ui.Button(mapRow, "", Ui.Style.Secondary, "chevron_right", () => CycleMap(1), 18).GetComponent<RectTransform>().Size(70, -1);
            var maxRow = Ui.Node(col, "max"); maxRow.Size(-1, 58); maxRow.HList(10, 0, true);
            Ui.Label(maxRow, "Players", 20, Ui.Text2, TextAnchor.MiddleLeft, Ui.Semi).rectTransform.Size(-1, -1, 1);
            Ui.Button(maxRow, "", Ui.Style.Secondary, "minus", () => { cMax = Mathf.Max(2, cMax - 1); cMaxLabel.text = cMax.ToString(); }, 18).GetComponent<RectTransform>().Size(70, -1);
            cMaxLabel = Ui.Label(maxRow, "4", 24, Ui.Text, TextAnchor.MiddleCenter, Ui.Bold);
            cMaxLabel.rectTransform.Size(60, -1);
            Ui.Button(maxRow, "", Ui.Style.Secondary, "plus", () => { cMax = Mathf.Min(4, cMax + 1); cMaxLabel.text = cMax.ToString(); }, 18).GetComponent<RectTransform>().Size(70, -1);
            var btns = Ui.Node(col, "btns"); btns.Size(-1, 70); btns.HList(12, 0, true);
            Ui.Button(btns, "CANCEL", Ui.Style.Secondary, "close", () => createBox.gameObject.SetActive(false), 18).GetComponent<RectTransform>().Size(200, -1);
            Ui.Button(btns, "CREATE", Ui.Style.Primary, "check", Create, 22).GetComponent<RectTransform>().Size(-1, -1, 1);
            createBox.gameObject.SetActive(false);
            cMap = Settings.LastMap;
            CycleMap(0);
        }

        void CycleMap(int d)
        {
            var ids = new List<string>();
            foreach (var m in Lobby.Maps()) ids.Add(m.Str("id"));
            ids.Add("pvp");
            int i = Mathf.Max(0, ids.IndexOf(cMap));
            cMap = ids[(i + d + ids.Count) % ids.Count];
            cMapLabel.text = Lobby.MapLabel(cMap).ToUpperInvariant();
        }

        public override void OnShow()
        {
            base.OnShow();
            App.I.SetBackdrop(Settings.LastMap);
            if (string.IsNullOrEmpty(cName.text)) cName.text = Session.Username + "'s server";
            Load();
        }

        async void Load()
        {
            if (loading) return;
            loading = true;
            empty.text = "LOADING SERVERS...";
            Ui.Clear(list);
            var servers = await Lobby.Servers();
            loading = false;
            if (this == null) return;
            empty.text = servers.Count == 0 ? (string.IsNullOrEmpty(Lobby.LastError) ? "No servers right now - host one!" : Lobby.LastError) : "";
            foreach (var s in servers) Row(s);
        }

        void Row(Dictionary<string, object> s)
        {
            var row = Ui.Panel(list, 0.92f, s.Bool("in_match") ? Ui.Amber : Ui.Green);
            row.Size(-1, 86);
            var h = Ui.Node(row, "h").Fill(14);
            h.HList(16, 0, true);
            bool priv = s.Bool("private") || s.Bool("is_private");
            Ui.IconImg(h, priv ? "lock" : "server", priv ? Ui.Amber : Ui.Text2, 28).rectTransform.Size(28, 28);
            var info = Ui.Node(h, "info"); info.Size(-1, -1, 1);
            Ui.Label(info, s.Str("name", "Server"), 22, Ui.Text, TextAnchor.UpperLeft, Ui.Bold).rectTransform.Place(0, 0.5f, 1, 1);
            string meta = Lobby.MapLabel(Lobby.LocalMap(s.Str("map"))) + "  ·  host " + s.Str("created_by", "?") + (s.Bool("in_match") ? "  ·  IN MATCH" : "");
            Ui.Label(info, meta.ToUpperInvariant(), 14, Ui.Text3, TextAnchor.LowerLeft, Ui.Bold).rectTransform.Place(0, 0, 1, 0.5f);
            int cur = s.Int("current_players", s.Int("players")), max = s.Int("max_players", 4);
            Ui.Label(h, cur + " / " + max, 22, cur >= max ? Ui.Red : Ui.Text, TextAnchor.MiddleRight, Ui.Bold).rectTransform.Size(90, -1);
            int id = s.Int("id");
            string map = Lobby.LocalMap(s.Str("map"));
            Ui.Button(h, "JOIN", Ui.Style.Primary, "play", () => Join(id, priv, map), 18).GetComponent<RectTransform>().Size(150, -1);
        }

        void Join(int id, bool priv, string map)
        {
            if (priv) Prompt.Ask("Server password", "Password", true, pw => DoJoin(id, pw, map));
            else DoJoin(id, "", map);
        }

        async void DoJoin(int id, string pw, string map)
        {
            Lobby.Solo = false;
            bool ok = await Lobby.Join(id, pw, map);
            if (!ok) { Toast.Show(Lobby.LastError, Ui.Red, 3.5f); return; }
            App.Show<LobbyScreen>();
        }

        async void Create()
        {
            Lobby.Solo = false;
            bool ok = await Lobby.Create(cName.text.Trim().Length > 0 ? cName.text.Trim() : Session.Username + "'s server", cMax, cPass.text, cMap);
            if (!ok) { Toast.Show(Lobby.LastError, Ui.Red, 3.5f); return; }
            createBox.gameObject.SetActive(false);
            App.Show<LobbyScreen>();
        }

        public override bool OnBack()
        {
            if (createBox.gameObject.activeSelf) { createBox.gameObject.SetActive(false); return true; }
            return false;
        }
    }

    /// Small text prompt dialog.
    public static class Prompt
    {
        public static void Ask(string title, string placeholder, bool password, System.Action<string> done)
        {
            var box = Ui.Node(App.Overlay, "prompt").Fill();
            var dim = Ui.Img(box, new Color(0.03f, 0.035f, 0.045f, 0.82f), null, "dim");
            dim.rectTransform.Fill(); dim.raycastTarget = true;
            var p = Ui.Panel(box, 0.98f, Ui.Orange);
            p.Box(0.5f, 0.5f, 0, 0, 620, 300);
            p.pivot = new Vector2(0.5f, 0.5f);
            var col = Ui.Node(p, "col").Fill(26);
            col.VList(14, 0);
            Ui.Section(col, title, Ui.Orange);
            var f = Ui.Input(col, placeholder, password);
            f.GetComponent<RectTransform>().Size(-1, 60);
            var btns = Ui.Node(col, "btns"); btns.Size(-1, 66); btns.HList(12, 0, true);
            Ui.Button(btns, "CANCEL", Ui.Style.Secondary, "close", () => Object.Destroy(box.gameObject), 18).GetComponent<RectTransform>().Size(200, -1);
            Ui.Button(btns, "OK", Ui.Style.Primary, "check", () => { Object.Destroy(box.gameObject); done(f.text); }, 20).GetComponent<RectTransform>().Size(-1, -1, 1);
            f.ActivateInputField();
        }

        public static void Confirm(string title, string text, string ok, System.Action yes)
        {
            var box = Ui.Node(App.Overlay, "confirm").Fill();
            var dim = Ui.Img(box, new Color(0.03f, 0.035f, 0.045f, 0.82f), null, "dim");
            dim.rectTransform.Fill(); dim.raycastTarget = true;
            var p = Ui.Panel(box, 0.98f, Ui.Orange);
            p.Box(0.5f, 0.5f, 0, 0, 640, 300);
            p.pivot = new Vector2(0.5f, 0.5f);
            var col = Ui.Node(p, "col").Fill(26);
            col.VList(14, 0);
            Ui.Section(col, title, Ui.Orange);
            Ui.Label(col, text, 20, Ui.Text2, TextAnchor.UpperLeft, Ui.Regular).rectTransform.Size(-1, 90);
            var btns = Ui.Node(col, "btns"); btns.Size(-1, 66); btns.HList(12, 0, true);
            Ui.Button(btns, "CANCEL", Ui.Style.Secondary, "close", () => Object.Destroy(box.gameObject), 18).GetComponent<RectTransform>().Size(200, -1);
            Ui.Button(btns, ok, Ui.Style.Primary, "check", () => { Object.Destroy(box.gameObject); yes(); }, 20).GetComponent<RectTransform>().Size(-1, -1, 1);
        }
    }

    // ---------------------------------------------------------------------------
    public class LobbyScreen : SubScreen
    {
        RectTransform members;
        Text mapText, info;
        Ui.Btn readyBtn, startBtn;
        bool ready, starting;
        float nextPoll;
        int difficulty = 1;
        Ui.Btn diffBtn;

        public override void Build()
        {
            Frame("Lobby", "server");
            var left = Ui.Panel(body, 0.9f, Ui.Orange);
            left.Place(0, 0, 0.62f, 1, 0, 100, 10, 0);
            Ui.Section(left, "Players").Place(0, 1, 1, 1, 18, -42, 18, 18);
            members = Ui.Node(left, "members").Place(0, 0, 1, 1, 18, 18, 18, 58);
            members.VList(8, 0);
            var right = Ui.Panel(body, 0.9f, null);
            right.Place(0.62f, 0, 1, 1, 10, 100, 0, 0);
            var rc = Ui.Node(right, "col").Fill(20);
            rc.VList(10, 0);
            Ui.Section(rc, "Map");
            mapText = Ui.Label(rc, "", 34, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold);
            mapText.rectTransform.Size(-1, 48);
            info = Ui.Label(rc, "", 17, Ui.Text3, TextAnchor.UpperLeft, Ui.Semi);
            info.rectTransform.Size(-1, 90);
            Ui.Section(rc, "Difficulty (leader)");
            diffBtn = Ui.Button(rc, "", Ui.Style.Secondary, "star", () => { difficulty = (difficulty + 1) % Lobby.Difficulties.Length; diffBtn.SetLabel(Lobby.DifficultyLabels[difficulty].ToUpperInvariant()); }, 18);
            diffBtn.GetComponent<RectTransform>().Size(-1, 56);
            var bar = Ui.Node(body, "actions").Place(0, 0, 1, 0, 0, 0, 0, -84);
            bar.HList(14, 0, true).childAlignment = TextAnchor.MiddleRight;
            Ui.Button(bar, "LEAVE LOBBY", Ui.Style.Danger, "logout", Leave, 18).GetComponent<RectTransform>().Size(240, -1);
            readyBtn = Ui.Button(bar, "READY UP", Ui.Style.Success, "check", ToggleReady, 18);
            readyBtn.GetComponent<RectTransform>().Size(240, -1);
            startBtn = Ui.Button(bar, "START MATCH", Ui.Style.Primary, "play", StartMatch, 20);
            startBtn.GetComponent<RectTransform>().Size(300, -1);
        }

        public override void OnShow()
        {
            base.OnShow();
            ready = false;
            difficulty = Mathf.Max(0, System.Array.IndexOf(Lobby.Difficulties, Settings.Difficulty));
            diffBtn.SetLabel(Lobby.DifficultyLabels[difficulty].ToUpperInvariant());
            Refresh();
        }

        public async void Refresh()
        {
            nextPoll = Time.time + 2f;
            var d = await Lobby.FetchLobby();
            if (this == null || !gameObject.activeInHierarchy) return;
            App.I.SetBackdrop(Lobby.Map);
            mapText.text = Lobby.MapLabel(Lobby.Map).ToUpperInvariant();
            if (d == null) { info.text = "Lobby not found - it may have been closed."; return; }
            var srv = d.O("server") ?? new Dictionary<string, object>();
            info.text = (srv.Str("name", "Server") + "\nLeader: " + Lobby.Leader + "  ·  max " + srv.Int("max_players", 4) + " players").ToUpperInvariant();
            Ui.Clear(members);
            foreach (var o in d.L("members"))
            {
                var m = o as Dictionary<string, object>;
                string u = m != null ? m.Str("username") : (o as string);
                bool r = m != null && m.Bool("ready");
                if (u == Session.Username) ready = r;
                var row = Ui.Panel(members, 0.9f, u == Lobby.Leader ? (Color?)Ui.Orange : null);
                row.Size(-1, 62);
                var h = Ui.Node(row, "h").Fill(12); h.HList(12, 0, true);
                Ui.IconImg(h, u == Lobby.Leader ? "star" : "user", u == Lobby.Leader ? Ui.Amber : Ui.Text2, 24).rectTransform.Size(24, 24);
                Ui.Label(h, u + (u == Session.Username ? "  (you)" : ""), 22, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold).rectTransform.Size(-1, -1, 1);
                Ui.Label(h, r ? "READY" : "NOT READY", 16, r ? Ui.Green : Ui.Text3, TextAnchor.MiddleRight, Ui.Bold).rectTransform.Size(140, -1);
                if (Lobby.IsLeader && u != Session.Username)
                {
                    string target = u;
                    Ui.Button(h, "KICK", Ui.Style.Danger, null, () => Kick(target), 14).GetComponent<RectTransform>().Size(90, -1);
                }
            }
            readyBtn.SetLabel(ready ? "UNREADY" : "READY UP");
            startBtn.gameObject.SetActive(Lobby.IsLeader);
            diffBtn.SetDisabled(!Lobby.IsLeader);
        }

        async void Kick(string u)
        {
            var r = await Net.Rpc("kick_player", new Dictionary<string, object> { { "id", Lobby.ServerId }, { "target", u }, { "requestor", Session.Username } });
            if (r == null || r.Str("status") != "ok") Toast.Show(r != null ? r.Str("msg", "Kick failed") : "Server unreachable", Ui.Red);
            Refresh();
        }

        void Update()
        {
            if (Time.time >= nextPoll && MatchController.Active == null) Refresh();
        }

        async void ToggleReady()
        {
            bool ok = await Lobby.SetReady(!ready);
            if (ok) { ready = !ready; Refresh(); }
        }

        async void StartMatch()
        {
            if (starting) return;
            starting = true;
            Settings.Difficulty = Lobby.Difficulties[difficulty];
            bool ok = await Lobby.StartMatch(Lobby.Difficulties[difficulty]);
            starting = false;
            if (!ok) Toast.Show(Lobby.LastError, Ui.Red, 4f);
        }

        async void Leave()
        {
            await Lobby.Leave();
            App.Back();
        }

        public override bool OnBack() { Leave(); return true; }
    }

    // ---------------------------------------------------------------------------
    /// Renders one gun model into a texture for the shop.
    public class GunPreview : MonoBehaviour
    {
        const int Layer = 30;
        public RenderTexture rt;
        Camera cam;
        Transform holder;
        string current;
        Material world, glow;

        public static GunPreview Create()
        {
            var go = new GameObject("gun-preview");
            DontDestroyOnLoad(go);
            go.transform.position = new Vector3(0, -500, 0);
            var g = go.AddComponent<GunPreview>();
            g.rt = new RenderTexture(768, 432, 24);
            g.rt.antiAliasing = 4;
            var cgo = new GameObject("cam");
            cgo.transform.SetParent(go.transform, false);
            cgo.transform.localPosition = new Vector3(0, 0.15f, -2.6f);
            g.cam = cgo.AddComponent<Camera>();
            g.cam.clearFlags = CameraClearFlags.SolidColor;
            g.cam.backgroundColor = new Color(0, 0, 0, 0);
            g.cam.cullingMask = 1 << Layer;
            g.cam.targetTexture = g.rt;
            g.cam.enabled = false;            // rendered by hand in Update
            g.cam.fieldOfView = 30f;
            g.cam.nearClipPlane = 0.1f; g.cam.farClipPlane = 20f;
            g.holder = new GameObject("holder").transform;
            g.holder.SetParent(go.transform, false);
            g.world = new Material(MapWorld.Sh("FortBoxWorld"));
            g.world.SetFloat("_SpecK", 0.4f);
            g.glow = new Material(MapWorld.Sh("FortBoxGlow"));
            return g;
        }

        public void Show(string weapon)
        {
            gameObject.SetActive(true);
            if (weapon == current) return;
            current = weapon;
            Ui.Clear(holder);
            var gm = Models.Gun(weapon);
            if (gm == null) return;
            var b = new Bounds();
            bool first = true;
            int i = 0;
            foreach (var d in gm.geos)
            {
                var go = new GameObject("g" + i);
                go.layer = Layer;
                go.transform.SetParent(holder, false);
                var mesh = Models.CachedMesh("gun:" + weapon + ":" + i, d);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = d.kind == "glow" ? glow : world;
                if (first) { b = mesh.bounds; first = false; } else b.Encapsulate(mesh.bounds);
                i++;
            }
            float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float s = size > 0 ? 1.6f / size : 1f;
            foreach (Transform c in holder) c.localPosition = -b.center * 1f;
            holder.localScale = Vector3.one * s;
        }

        public void Hide() { gameObject.SetActive(false); }

        void Update()
        {
            holder.localRotation = Quaternion.Euler(-8f, 90f + Mathf.Sin(Time.time * 0.6f) * 40f, 0);
            if (cam != null) cam.Render();
        }
    }

    public class ShopScreen : SubScreen
    {
        RectTransform grid, detail;
        RawImage preview;
        Text dName, dDesc, dStats, dPrice, status;
        Ui.Btn buyBtn;
        Dictionary<string, object> selected;
        static GunPreview gp;
        readonly List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();

        public override void Build()
        {
            Frame("Shop", "shop");
            ScrollRect sr;
            grid = Ui.Scroll(body, out sr, 8);
            sr.GetComponent<RectTransform>().Place(0, 0, 0.5f, 1, 0, 0, 10, 0);
            status = Ui.Label(body, "", 20, Ui.Text3, TextAnchor.MiddleCenter, Ui.Semi);
            status.rectTransform.Place(0, 0, 0.5f, 1);
            detail = Ui.Panel(body, 0.92f, Ui.Orange);
            detail.Place(0.5f, 0, 1, 1, 10, 0, 0, 0);
            preview = Ui.Node(detail, "preview").Place(0, 1, 1, 1, 16, -330, 16, 16).gameObject.AddComponent<RawImage>();
            preview.raycastTarget = false;
            dName = Ui.Label(detail, "", 34, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold);
            dName.rectTransform.Place(0, 1, 1, 1, 24, -380, 24, 334);
            dDesc = Ui.Label(detail, "", 18, Ui.Text2, TextAnchor.UpperLeft, Ui.Regular);
            dDesc.rectTransform.Place(0, 1, 1, 1, 24, -440, 24, 384);
            dStats = Ui.Label(detail, "", 16, Ui.Text3, TextAnchor.UpperLeft, Ui.Bold);
            dStats.rectTransform.Place(0, 0, 1, 1, 24, 100, 24, 446);
            dPrice = Ui.Label(detail, "", 28, Ui.Amber, TextAnchor.MiddleLeft, Ui.Bold);
            dPrice.rectTransform.Place(0, 0, 0.5f, 0, 24, 24, 0, -70);
            buyBtn = Ui.Button(detail, "BUY", Ui.Style.Primary, "coin", Buy, 22);
            buyBtn.GetComponent<RectTransform>().Place(0.5f, 0, 1, 0, 0, 20, 20, -84);
        }

        public override void OnShow()
        {
            base.OnShow();
            if (gp == null) gp = GunPreview.Create();
            MapWorld.DefaultLighting();
            preview.texture = gp.rt;
            Load();
        }

        public override void OnHide() { if (gp != null) gp.Hide(); }

        async void Load()
        {
            status.text = "LOADING...";
            Ui.Clear(grid);
            var raw = await Net.Get("/shop-items");
            if (this == null) return;
            var o = raw != null ? Json.Obj(raw) : null;
            items.Clear();
            if (o != null) foreach (var it in o.L("items")) { var d = it as Dictionary<string, object>; if (d != null) items.Add(d); }
            status.text = items.Count == 0 ? "Shop unavailable right now" : "";
            var owned = Session.OwnedWeapons();
            foreach (var it in items)
            {
                string type = it.Str("type");
                if (type != "weapon" && type != "grenade" && type != "grenades") continue;
                var item = it;
                string wk = it.Str("weapon_key");
                bool have = type == "weapon" && owned.Contains(wk);
                var row = Ui.Panel(grid, 0.9f, have ? (Color?)Ui.Green : null);
                row.Size(-1, 74);
                var h = Ui.Node(row, "h").Fill(12); h.HList(14, 0, true);
                var c = it.L("color");
                Color cc = c.Count >= 3 ? Ui.C((int)c.F(0), (int)c.F(1), (int)c.F(2)) : Ui.Orange;
                Ui.IconImg(h, type == "weapon" ? "crosshair" : "bolt", cc, 26).rectTransform.Size(26, 26);
                Ui.Label(h, it.Str("name"), 21, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold).rectTransform.Size(-1, -1, 1);
                Ui.Label(h, have ? "OWNED" : it.Int("price").ToString("N0"), 19, have ? Ui.Green : Ui.Amber, TextAnchor.MiddleRight, Ui.Bold).rectTransform.Size(130, -1);
                var btn = row.gameObject.AddComponent<Button>();
                btn.onClick.AddListener(() => Select(item));
                if (selected == null) Select(item);
            }
            if (selected != null) Select(selected);
        }

        void Select(Dictionary<string, object> it)
        {
            selected = it;
            string type = it.Str("type"), wk = it.Str("weapon_key");
            dName.text = it.Str("name").ToUpperInvariant();
            dDesc.text = it.Str("description");
            bool have = type == "weapon" && Session.OwnedWeapons().Contains(wk);
            dPrice.text = have ? "OWNED" : it.Int("price").ToString("N0") + " COINS";
            dPrice.color = have ? Ui.Green : Ui.Amber;
            buyBtn.SetDisabled(have);
            buyBtn.SetLabel(have ? "OWNED" : "BUY");
            if (type == "weapon")
            {
                var json = Resources.Load<TextAsset>("FortBox/data/weapons");
                var w = json != null ? (Json.Obj(json.text) ?? new Dictionary<string, object>()).O(wk) : null;
                dStats.text = w == null ? "" : string.Format("DAMAGE {0}   ·   FIRE DELAY {1} MS\nMAGAZINE {2}   ·   RELOAD {3:0.0} S   ·   PELLETS {4}",
                    w.Int("damage"), w.Int("shoot_cooldown"), w.Int("mag_size"), w.Int("reload_ms") / 1000f, w.Int("cartridges_per_shot", 1));
                if (gp != null) gp.Show(wk);
                preview.enabled = true;
            }
            else { dStats.text = ""; preview.enabled = false; if (gp != null) gp.Hide(); }
        }

        void Buy()
        {
            if (selected == null) return;
            var it = selected;
            Prompt.Confirm("Buy " + it.Str("name"), "Spend " + it.Int("price").ToString("N0") + " coins? You have " + Session.Coins.ToString("N0") + ".", "BUY", async () =>
            {
                var r = await Net.Rpc("shop_buy", new Dictionary<string, object> { { "username", Session.Username }, { "token", Session.Token }, { "item_id", it.Str("id") } });
                if (r != null && r.Str("status") == "ok")
                {
                    Toast.Show("Bought " + it.Str("name") + "!", Ui.Green);
                    if (r.ContainsKey("coins")) Session.Player["coins"] = r["coins"];
                    if (r.ContainsKey("grenades")) Session.Player["grenades"] = r["grenades"];
                    await Session.Refresh();
                }
                else Toast.Show(r != null ? r.Str("msg", "Purchase failed") : "Server unreachable", Ui.Red, 3.5f);
                if (this != null) { UpdateCoins(); Load(); }
            });
        }
    }

    // ---------------------------------------------------------------------------
    public class QuestsScreen : SubScreen
    {
        RectTransform col;

        public override void Build()
        {
            Frame("Daily quests", "star");
            Ui.Label(body, "Three new quests every day (UTC) - the same quests as on PC. Progress counts in server matches.", 18, Ui.Text2, TextAnchor.MiddleLeft, Ui.Regular)
                .rectTransform.Place(0, 1, 1, 1, 0, -40, 0, 0);
            col = Ui.Node(body, "col").Place(0, 0, 1, 1, 0, 0, 0, 56);
            col.VList(14, 0);
        }

        public override void OnShow() { base.OnShow(); Fill(); }

        void Fill()
        {
            Ui.Clear(col);
            foreach (var q in Quests.Today3())
            {
                var quest = q;
                var p = Ui.Panel(col, 0.92f, q.claimed ? Ui.Text3 : (q.Done ? Ui.Green : Ui.Orange));
                p.Size(-1, 150);
                Ui.Label(p, q.name.ToUpperInvariant(), 26, q.claimed ? Ui.Text3 : Ui.Text, TextAnchor.MiddleLeft, Ui.Bold).rectTransform.Place(0, 1, 0.7f, 1, 24, -56, 0, 14);
                Ui.Label(p, q.description, 18, Ui.Text2, TextAnchor.MiddleLeft, Ui.Regular).rectTransform.Place(0, 1, 0.7f, 1, 24, -92, 0, 58);
                Ui.Label(p, "REWARD  " + q.reward.ToUpperInvariant(), 15, Ui.Amber, TextAnchor.MiddleLeft, Ui.Bold).rectTransform.Place(0, 0, 0.7f, 0, 24, 46, 0, -70);
                RectTransform br;
                Ui.StatBar(p, q.progress / (float)q.target, q.Done ? Ui.Green : Ui.Orange, out br);
                br.Place(0, 0, 0.7f, 0, 24, 22, 0, -30);
                Ui.Label(p, q.progress + " / " + q.target, 20, Ui.Text, TextAnchor.MiddleRight, Ui.Bold).rectTransform.Place(0.7f, 1, 1, 1, 0, -56, 24, 14);
                var b = Ui.Button(p, q.claimed ? "CLAIMED" : (q.Done ? "CLAIM" : "IN PROGRESS"), q.Done && !q.claimed ? Ui.Style.Success : Ui.Style.Secondary, q.claimed ? "check" : "star", async () =>
                {
                    if (await Quests.Claim(quest)) { Toast.Show("Reward claimed: " + quest.reward, Ui.Green); if (this != null) { UpdateCoins(); Fill(); } }
                }, 18);
                b.SetDisabled(!q.Done || q.claimed);
                b.GetComponent<RectTransform>().Place(0.72f, 0, 1, 0, 0, 20, 24, -80);
            }
        }
    }

    // ---------------------------------------------------------------------------
    public class LeaderboardScreen : SubScreen
    {
        RectTransform list;
        Text empty;
        bool kills;
        Ui.Btn tabScore, tabKills;

        public override void Build()
        {
            Frame("Leaderboard", "trophy");
            var tabs = Ui.Node(body, "tabs").Place(0, 1, 1, 1, 0, -60, 0, 0);
            tabs.HList(10, 0, true);
            tabScore = Ui.Button(tabs, "TOP SCORE", Ui.Style.Primary, "star", () => { kills = false; Load(); }, 17);
            tabScore.GetComponent<RectTransform>().Size(220, -1);
            tabKills = Ui.Button(tabs, "MOST KILLS", Ui.Style.Secondary, "crosshair", () => { kills = true; Load(); }, 17);
            tabKills.GetComponent<RectTransform>().Size(220, -1);
            ScrollRect sr;
            list = Ui.Scroll(body, out sr, 6);
            sr.GetComponent<RectTransform>().Place(0, 0, 1, 1, 0, 0, 0, 76);
            empty = Ui.Label(body, "", 22, Ui.Text3, TextAnchor.MiddleCenter, Ui.Semi);
            empty.rectTransform.Place(0, 0, 1, 1, 0, 0, 0, 76);
        }

        public override void OnShow() { base.OnShow(); Load(); }

        async void Load()
        {
            tabScore.style = kills ? Ui.Style.Secondary : Ui.Style.Primary; tabScore.Refresh();
            tabKills.style = kills ? Ui.Style.Primary : Ui.Style.Secondary; tabKills.Refresh();
            Ui.Clear(list);
            empty.text = "LOADING...";
            var r = await Net.Rpc(kills ? "get_kills_leaderboard" : "get_leaderboard", new Dictionary<string, object> { { "limit", 50 } });
            if (this == null) return;
            var rows = r != null ? (r.L("leaderboard").Count > 0 ? r.L("leaderboard") : (r.O("data") ?? new Dictionary<string, object>()).L("leaderboard")) : new List<object>();
            empty.text = rows.Count == 0 ? "Leaderboard unavailable" : "";
            int i = 0;
            foreach (var o in rows)
            {
                var e = o as Dictionary<string, object>;
                if (e == null) continue;
                i++;
                bool me = e.Str("username") == Session.Username;
                var row = Ui.Panel(list, 0.9f, i <= 3 ? Ui.Amber : (me ? Ui.Orange : (Color?)null));
                row.Size(-1, 60);
                var h = Ui.Node(row, "h").Fill(10); h.HList(16, 0, true);
                Ui.Label(h, "#" + i, 22, i <= 3 ? Ui.Amber : Ui.Text3, TextAnchor.MiddleLeft, Ui.Bold).rectTransform.Size(70, -1);
                Ui.Label(h, e.Str("username"), 22, me ? Ui.Orange : Ui.Text, TextAnchor.MiddleLeft, Ui.Bold).rectTransform.Size(-1, -1, 1);
                Ui.Label(h, ("LV " + e.Int("level", 1) + "  ·  " + e.Str("rank", "player")).ToUpperInvariant(), 14, Ui.Text3, TextAnchor.MiddleRight, Ui.Bold).rectTransform.Size(220, -1);
                Ui.Label(h, (kills ? e.Int("kills") : e.Int("score")).ToString("N0"), 22, Ui.Text, TextAnchor.MiddleRight, Ui.Bold).rectTransform.Size(160, -1);
            }
        }
    }

    // ---------------------------------------------------------------------------
    public class ProfileScreen : SubScreen
    {
        Text stats;
        RectTransform colors;
        InputField display;

        public override void Build()
        {
            Frame("Profile", "user");
            var left = Ui.Panel(body, 0.92f, Ui.Orange);
            left.Place(0, 0, 0.45f, 1, 0, 0, 10, 0);
            var lc = Ui.Node(left, "col").Fill(22);
            lc.VList(12, 0);
            Ui.Section(lc, "Account");
            stats = Ui.Label(lc, "", 20, Ui.Text, TextAnchor.UpperLeft, Ui.Semi);
            stats.rectTransform.Size(-1, 260);
            Ui.Section(lc, "Display name");
            var row = Ui.Node(lc, "dn"); row.Size(-1, 58); row.HList(10, 0, true);
            display = Ui.Input(row, "Display name");
            display.characterLimit = 24;
            display.GetComponent<RectTransform>().Size(-1, -1, 1);
            Ui.Button(row, "SAVE", Ui.Style.Secondary, "save", SaveName, 16).GetComponent<RectTransform>().Size(130, -1);

            var right = Ui.Panel(body, 0.92f, null);
            right.Place(0.45f, 0, 1, 1, 10, 0, 0, 0);
            Ui.Section(right, "Colour (your 3D outfit)").Place(0, 1, 1, 1, 22, -44, 22, 22);
            ScrollRect sr;
            colors = Ui.Scroll(right, out sr, 0);
            sr.GetComponent<RectTransform>().Place(0, 0, 1, 1, 22, 22, 22, 60);
            Object.Destroy(colors.GetComponent<VerticalLayoutGroup>());
            var g = colors.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(150, 64);
            g.spacing = new Vector2(10, 10);
        }

        public override void OnShow()
        {
            base.OnShow();
            Fill();
            var _ = Reload();
        }

        async System.Threading.Tasks.Task Reload() { await Session.Refresh(); if (this != null && gameObject.activeInHierarchy) { UpdateCoins(); Fill(); } }

        void Fill()
        {
            var p = Session.Player;
            stats.text = string.Format("{0}\nRank: {1}\nLevel {2}  ·  {3:N0} XP\nScore {4:N0}\nCoins {5:N0}  ·  Gems {6:N0}\nGrenades {7}\nWeapons owned: {8}",
                Session.Username, Session.Rank, Session.Level, p.Int("xp"), Session.Score, Session.Coins, p.Int("gems"), p.Int("grenades"), Session.OwnedWeapons().Count);
            display.text = p.Str("display_name");
            Ui.Clear(colors);
            foreach (var kv in Ui.ColorTable())
            {
                string name = kv.Key;
                var c = Ui.PlayerColor(name);
                bool cur = name == Session.Color;
                var cell = Ui.Node(colors, "c-" + name);
                var bg = cell.gameObject.AddComponent<Image>();
                bg.sprite = Ui.Round(); bg.type = Image.Type.Sliced; bg.color = Ui.PanelHi;
                Ui.Img(cell, c, Ui.Round(), "sw").rectTransform.Place(0, 0, 0, 1, 8, 8, 0, 8).sizeDelta = new Vector2(48, -16);
                var t = Ui.Label(cell, name.Replace('_', ' '), 14, cur ? Ui.Orange : Ui.Text2, TextAnchor.MiddleLeft, Ui.Bold);
                t.rectTransform.Place(0, 0, 1, 1, 62, 0, 6, 0);
                if (cur) Ui.Img(cell, Ui.Orange, Ui.Outline(), "sel").rectTransform.Fill();
                cell.gameObject.AddComponent<Button>().onClick.AddListener(() => SetColor(name));
            }
        }

        async void SetColor(string name)
        {
            var r = await Net.Rpc("player_update", new Dictionary<string, object> { { "username", Session.Username }, { "token", Session.Token }, { "color", name } });
            if (r != null && r.Str("status") == "ok")
            {
                var pl = r.O("player");
                Session.Player["color"] = pl != null ? pl.Str("color", name) : name;
                if (Session.Color != name) Toast.Show("That colour isn't available for your account", Ui.Amber);
                else Toast.Show("Colour changed", Ui.Green);
            }
            else Toast.Show(r != null ? r.Str("msg", "Could not change colour") : "Server unreachable", Ui.Red);
            if (this != null) Fill();
        }

        async void SaveName()
        {
            var r = await Net.Rpc("update_display_name", new Dictionary<string, object> { { "username", Session.Username }, { "token", Session.Token }, { "display_name", display.text.Trim() } });
            if (r != null && r.Str("status") == "ok") { Session.Player["display_name"] = r.Str("display_name", display.text.Trim()); Toast.Show("Display name updated", Ui.Green); }
            else Toast.Show(r != null ? r.Str("msg", "Could not update") : "Server unreachable", Ui.Red);
        }
    }

    // ---------------------------------------------------------------------------
    public class SettingsScreen : SubScreen
    {
        Ui.Btn qBtn, fpsBtn;
        static readonly string[] Q = { "LOW", "MEDIUM", "HIGH" };

        public override void Build()
        {
            Frame("Settings", "gear");
            var left = Ui.Panel(body, 0.92f, Ui.Orange);
            left.Place(0, 0, 0.5f, 1, 0, 0, 10, 0);
            var lc = Ui.Node(left, "col").Fill(22);
            lc.VList(12, 0);
            Ui.Section(lc, "Graphics");
            qBtn = Ui.Button(lc, "QUALITY: " + Q[Settings.Quality], Ui.Style.Secondary, "star", () =>
            {
                Settings.Quality = (Settings.Quality + 1) % 3;
                qBtn.SetLabel("QUALITY: " + Q[Settings.Quality]);
                Toast.Show("Applies to the next match");
            }, 18);
            qBtn.GetComponent<RectTransform>().Size(-1, 56);
            Ui.Check(lc, "Real-time shadows", Settings.Shadows, v => Settings.Shadows = v);
            fpsBtn = Ui.Button(lc, "FRAME RATE: " + Settings.Fps, Ui.Style.Secondary, "bolt", () =>
            {
                Settings.Fps = Settings.Fps == 30 ? 60 : (Settings.Fps == 60 ? 90 : (Settings.Fps == 90 ? 120 : 30));
                fpsBtn.SetLabel("FRAME RATE: " + Settings.Fps);
            }, 18);
            fpsBtn.GetComponent<RectTransform>().Size(-1, 56);
            Ui.Section(lc, "App version " + Application.version);
            Ui.Button(lc, "CHECK FOR UPDATES", Ui.Style.Secondary, "refresh", () => AppUpdater.CheckNow(), 18).GetComponent<RectTransform>().Size(-1, 56);
            Ui.Section(lc, "Account");
            Ui.Label(lc, "Signed in as " + Session.Username, 18, Ui.Text2).rectTransform.Size(-1, 30);
            Ui.Button(lc, "SIGN OUT", Ui.Style.Danger, "logout", () =>
            {
                Session.Logout(true);
                App.ClearHistory();
                App.Show<LoginScreen>(false);
            }, 18).GetComponent<RectTransform>().Size(-1, 56);

            var right = Ui.Panel(body, 0.92f, null);
            right.Place(0.5f, 0, 1, 1, 10, 0, 0, 0);
            var rc = Ui.Node(right, "col").Fill(22);
            rc.VList(10, 0);
            Ui.Section(rc, "Controls");
            Ui.Slide(rc, "Look sensitivity", 0.2f, 3f, Settings.LookSens, v => Settings.LookSens = v);
            Ui.Slide(rc, "Aim (zoomed) sensitivity", 0.1f, 2f, Settings.AimSens, v => Settings.AimSens = v);
            Ui.Slide(rc, "Button size", 0.7f, 1.4f, Settings.ControlsScale, v => Settings.ControlsScale = v, v => Mathf.RoundToInt(v * 100) + "%");
            Ui.Slide(rc, "Button opacity", 0.3f, 1f, Settings.ControlsAlpha, v => Settings.ControlsAlpha = v, v => Mathf.RoundToInt(v * 100) + "%");
            Ui.Check(rc, "Invert look up/down", Settings.InvertY, v => Settings.InvertY = v);
            Ui.Check(rc, "Auto-fire when the crosshair is on an enemy", Settings.AutoFire, v => Settings.AutoFire = v);
            Ui.Section(rc, "Sound");
            Ui.Slide(rc, "Music", 0f, 1f, Sfx.MusicVol, v => Sfx.MusicVol = v, v => Mathf.RoundToInt(v * 100) + "%");
            Ui.Slide(rc, "Effects (guns, explosions)", 0f, 1f, Sfx.EffectsVol, v => Sfx.EffectsVol = v, v => Mathf.RoundToInt(v * 100) + "%");
            Ui.Slide(rc, "Menu clicks", 0f, 1f, Sfx.UiVol, v => Sfx.UiVol = v, v => Mathf.RoundToInt(v * 100) + "%");
        }

        public override void OnShow()
        {
            base.OnShow();
            qBtn.SetLabel("QUALITY: " + Q[Settings.Quality]);
            fpsBtn.SetLabel("FRAME RATE: " + Settings.Fps);
        }
    }
}
