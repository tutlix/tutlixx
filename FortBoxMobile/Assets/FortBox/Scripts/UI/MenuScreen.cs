// Main menu, Urban Ops layout: left rail with the wordmark, the big orange
// PLAY block, Multiplayer and the nav list; top bar with the player; the
// selected map's first-person render as the hero backdrop; the map strip at
// the bottom; daily quests on the right.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FortBox
{
    /// Screens with a top bar: back, title, coins.
    public abstract class SubScreen : UiScreen
    {
        protected RectTransform body;
        protected Text coinsText;

        protected void Frame(string title, string icon)
        {
            var bg = Ui.Img(root, Ui.A(Ui.Bg, 0.82f), null, "dim");
            bg.rectTransform.Fill();
            bg.raycastTarget = true;
            var top = Ui.Node(root, "top").Place(0, 1, 1, 1, 0, -84, 0, 0);
            Ui.Img(top, Ui.A(Ui.Rail, 0.96f), null, "bg").rectTransform.Fill();
            Ui.Img(top, Ui.Line, null, "line").rectTransform.Place(0, 0, 1, 0, 0, 0, 0, -1);
            Ui.Button(top, "BACK", Ui.Style.Ghost, "back", () => App.Back(), 18).GetComponent<RectTransform>().Box(0, 0.5f, 16, 0, 150, 56);
            Ui.Img(top, Ui.Orange, null, "dash").rectTransform.Box(0, 0.5f, 190, 0, 5, 30);
            if (!string.IsNullOrEmpty(icon)) Ui.IconImg(top, icon, Ui.Orange, 28).rectTransform.Box(0, 0.5f, 210, 0, 28, 28);
            Ui.Label(top, title.ToUpperInvariant(), 30, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold).rectTransform.Box(0, 0.5f, 250, 0, 800, 60);
            var coins = Ui.Panel(top, 0.9f, null);
            coins.Box(1, 0.5f, -20, 0, 210, 52);
            Ui.IconImg(coins, "coin", Ui.Amber, 24).rectTransform.Box(0, 0.5f, 14, 0, 24, 24);
            coinsText = Ui.Label(coins, "0", 22, Ui.Text, TextAnchor.MiddleRight, Ui.Bold);
            coinsText.rectTransform.Place(0, 0, 1, 1, 44, 0, 14, 0);
            body = Ui.Node(root, "body").Place(0, 0, 1, 1, 30, 24, 30, 104);
        }

        public override void OnShow() { UpdateCoins(); }
        protected void UpdateCoins() { if (coinsText != null) coinsText.text = Session.Coins.ToString("N0"); }
    }

    public class MenuScreen : UiScreen
    {
        Text nameText, levelText, coinsText, heroTitle, heroDesc, heroMeta;
        RectTransform strip, questsCol, playModal;
        string selected;
        int difficulty = 1;
        readonly List<Ui.Btn> diffBtns = new List<Ui.Btn>();
        readonly Dictionary<string, Image> cardBorders = new Dictionary<string, Image>();
        bool starting;

        public override void Build()
        {
            selected = Settings.LastMap;
            difficulty = System.Array.IndexOf(Lobby.Difficulties, Settings.Difficulty);
            if (difficulty < 0) difficulty = 1;
            const float railW = 300;

            // ---- left rail
            var rail = Ui.Node(root, "rail").Place(0, 0, 0, 1, 0, 0, 0, 0);
            rail.sizeDelta = new Vector2(railW, 0);
            rail.pivot = new Vector2(0, 0.5f);
            Ui.Img(rail, Ui.A(Ui.Rail, 0.94f), null, "bg").rectTransform.Fill();
            Ui.Img(rail, Ui.Line, null, "edge").rectTransform.Place(1, 0, 1, 1, -1, 0, 0, 0);
            Ui.Img(rail, Ui.Orange, null, "mark").rectTransform.Box(0, 1, 26, -26, 6, 36);
            var wm = Ui.Label(rail, "FORTBOX", 34, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold);
            wm.rectTransform.Box(0, 1, 44, -20, 200, 44);
            Ui.Label(rail, "3D", 34, Ui.Orange, TextAnchor.MiddleLeft, Ui.Bold).rectTransform.Box(0, 1, 196, -20, 80, 44);
            Ui.Label(rail, "TACTICAL SHOOTER", 12, Ui.Text3, TextAnchor.MiddleLeft, Ui.Bold).rectTransform.Box(0, 1, 46, -62, 220, 18);
            Ui.Img(rail, Ui.Line, null, "sep").rectTransform.Place(0, 1, 1, 1, 18, -88, 18, 87);

            var play = Ui.Button(rail, "PLAY", Ui.Style.Primary, "play", OpenPlay, 30);
            play.GetComponent<RectTransform>().Place(0, 1, 1, 1, 18, -188, 18, 104);
            var sub = Ui.Label(play.transform, "SURVIVAL · SERVER MATCH", 12, Ui.C(80, 32, 8), TextAnchor.LowerCenter, Ui.Bold);
            sub.rectTransform.Place(0, 0, 1, 0, 0, 8, 0, -24);
            var mp = Ui.Button(rail, "MULTIPLAYER", Ui.Style.Secondary, "users", () => App.Show<ServersScreen>(), 18);
            mp.GetComponent<RectTransform>().Place(0, 1, 1, 1, 18, -256, 18, 198);

            var nav = Ui.Node(rail, "nav").Place(0, 0, 1, 1, 10, 80, 10, 272);
            nav.VList(2, 0);
            Nav(nav, "Daily quests", "star", () => App.Show<QuestsScreen>());
            Nav(nav, "Shop", "shop", () => App.Show<ShopScreen>());
            Nav(nav, "Leaderboard", "trophy", () => App.Show<LeaderboardScreen>());
            Nav(nav, "Profile", "user", () => App.Show<ProfileScreen>());
            Nav(nav, "Settings", "gear", () => App.Show<SettingsScreen>());
            Nav(nav, "Sign out", "logout", () =>
            {
                Session.Logout(true);
                App.ClearHistory();
                App.Show<LoginScreen>(false);
            });
            var ver = Ui.Label(rail, "v" + Config.GameVersion, 12, Ui.Text3, TextAnchor.MiddleLeft, Ui.Bold);
            ver.rectTransform.Place(0, 0, 1, 0, 24, 20, 10, -40);

            // ---- top bar
            var top = Ui.Node(root, "top").Place(0, 1, 1, 1, railW, -70, 0, 0);
            Ui.Img(top, Ui.A(Ui.Rail, 0.6f), null, "bg").rectTransform.Fill();
            Ui.Img(top, Ui.Line, null, "line").rectTransform.Place(0, 0, 1, 0, 0, 0, 0, -1);
            var av = Ui.Img(top, Ui.Orange, Ui.Round(), "avatar");
            av.rectTransform.Box(1, 0.5f, -24, 0, 46, 46);
            Ui.IconImg(av.transform, "user", Ui.C(20, 10, 4), 26).rectTransform.Box(0.5f, 0.5f, 0, 0, 26, 26);
            av.raycastTarget = true;
            av.gameObject.AddComponent<Button>().onClick.AddListener(() => App.Show<ProfileScreen>());
            nameText = Ui.Label(top, "", 22, Ui.Text, TextAnchor.MiddleRight, Ui.Bold);
            nameText.rectTransform.Box(1, 0.5f, -84, 10, 400, 30);
            levelText = Ui.Label(top, "", 14, Ui.Text3, TextAnchor.MiddleRight, Ui.Bold);
            levelText.rectTransform.Box(1, 0.5f, -84, -14, 400, 20);
            var coins = Ui.Panel(top, 0.9f, null);
            coins.Box(0, 0.5f, 24, 0, 200, 46);
            Ui.IconImg(coins, "coin", Ui.Amber, 22).rectTransform.Box(0, 0.5f, 12, 0, 22, 22);
            coinsText = Ui.Label(coins, "0", 20, Ui.Text, TextAnchor.MiddleRight, Ui.Bold);
            coinsText.rectTransform.Place(0, 0, 1, 1, 40, 0, 12, 0);
            Ui.Button(top, "", Ui.Style.Ghost, "refresh", async () => { await Session.Refresh(); RefreshPlayer(); }, 20)
                .GetComponent<RectTransform>().Box(0, 0.5f, 236, 0, 50, 46);

            // ---- hero text
            var hero = Ui.Node(root, "hero").Place(0, 0, 1, 1, railW + 40, 330, 400, 110);
            Ui.Section(hero, "Selected map", Ui.Orange).Place(0, 1, 1, 1, 0, -30, 0, 8);
            heroTitle = Ui.Label(hero, "", 72, Ui.Text, TextAnchor.UpperLeft, Ui.Bold);
            heroTitle.rectTransform.Place(0, 1, 1, 1, 0, -128, 0, 36);
            heroDesc = Ui.Label(hero, "", 24, Ui.Text2, TextAnchor.UpperLeft, Ui.Regular);
            heroDesc.rectTransform.Place(0, 1, 1, 1, 0, -200, 0, 130);
            heroMeta = Ui.Label(hero, "", 15, Ui.Text3, TextAnchor.UpperLeft, Ui.Bold);
            heroMeta.rectTransform.Place(0, 1, 1, 1, 0, -236, 0, 206);

            // ---- right column: daily quests
            var qc = Ui.Panel(root, 0.9f, Ui.Orange);
            qc.Box(1, 1, -24, -92, 360, 408);
            Ui.Section(qc, "Daily quests").Place(0, 1, 1, 1, 16, -40, 16, 16);
            questsCol = Ui.Node(qc, "list").Place(0, 0, 1, 1, 16, 70, 16, 54);
            questsCol.VList(10, 0);
            Ui.Button(qc, "OPEN QUESTS", Ui.Style.Secondary, "star", () => App.Show<QuestsScreen>(), 16)
                .GetComponent<RectTransform>().Place(0, 0, 1, 0, 16, 14, 16, -58);

            // ---- map strip
            var stripBox = Ui.Node(root, "strip").Place(0, 0, 1, 0, railW + 24, 20, 24, -230);
            Ui.Section(stripBox, "Maps", Ui.Text3).Place(0, 1, 1, 1, 0, -24, 0, 0);
            var sv = Ui.Node(stripBox, "sv").Place(0, 0, 1, 1, 0, 0, 0, 30);
            var vp = Ui.Node(sv, "vp").Fill();
            vp.gameObject.AddComponent<RectMask2D>();
            vp.gameObject.AddComponent<Image>().color = Color.clear;
            strip = Ui.Node(vp, "content");
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(0, 1);
            strip.pivot = new Vector2(0, 0.5f);
            strip.offsetMin = strip.offsetMax = Vector2.zero;
            strip.HList(14, 0, true);
            strip.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = sv.gameObject.AddComponent<ScrollRect>();
            sr.viewport = vp; sr.content = strip; sr.vertical = false; sr.horizontal = true;
            var maps = Lobby.Maps();
            foreach (var m in maps) MapCard(m.Str("id"), m.Str("label"));
            MapCard("pvp", "PvP Arena");

            BuildPlayModal();
        }

        void Nav(Transform parent, string label, string icon, UnityEngine.Events.UnityAction a)
        {
            var b = Ui.Button(parent, label.ToUpperInvariant(), Ui.Style.Ghost, icon, a, 17);
            b.GetComponent<RectTransform>().Size(-1, 54);
            var row = b.transform.Find("row").GetComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleLeft;
            row.padding = new RectOffset(12, 0, 0, 0);
            row.spacing = 14;
        }

        void MapCard(string id, string label)
        {
            var card = Ui.Node(strip, "map-" + id);
            card.Size(300, -1);
            var bg = card.gameObject.AddComponent<Image>();
            bg.sprite = Ui.Round(); bg.type = Image.Type.Sliced; bg.color = Ui.PanelBg;
            var thumb = Ui.Node(card, "thumb").Fill(3).gameObject.AddComponent<RawImage>();
            thumb.texture = Resources.Load<Texture2D>("FortBox/thumbs/" + id);
            thumb.raycastTarget = false;
            var shade = Ui.Img(card, new Color(0.03f, 0.035f, 0.045f, 0.75f), null, "shade");
            shade.rectTransform.Place(0, 0, 1, 0, 3, 3, 3, -48);
            Ui.Label(card, label.ToUpperInvariant(), 18, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold).rectTransform.Place(0, 0, 1, 0, 14, 6, 10, -40);
            var border = Ui.Img(card, Ui.Line, Ui.Outline(), "border");
            border.rectTransform.Fill();
            cardBorders[id] = border;
            var btn = card.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => Select(id));
        }

        void Select(string id)
        {
            selected = id;
            Settings.LastMap = id;
            App.I.SetBackdrop(id);
            heroTitle.text = Lobby.MapLabel(id).ToUpperInvariant();
            string desc = id == "pvp" ? "Mirrored neon arena - first to the kill target wins" : "";
            foreach (var m in Lobby.Maps()) if (m.Str("id") == id) desc = m.Str("desc");
            heroDesc.text = desc;
            heroMeta.text = (id == "pvp" ? "PVP · SERVER MATCH" : "WAVE SURVIVAL · SERVER MATCH · " + Lobby.DifficultyLabels[difficulty].ToUpperInvariant());
            foreach (var kv in cardBorders) { kv.Value.color = kv.Key == id ? Ui.Orange : Ui.Line; kv.Value.rectTransform.offsetMin = Vector2.zero; }
        }

        public override void OnShow()
        {
            Select(selected);
            RefreshPlayer();
            RefreshQuests();
            var _ = RefreshAsync();
            if (Session.Player.Bool("tos_required")) ShowTos();
        }

        RectTransform tosBox;

        /// Same gate as the PC main menu: the server blocks in-game actions
        /// until the current Terms of Service are accepted.
        async void ShowTos()
        {
            if (tosBox != null) return;
            var r = await Net.Rpc("get_tos");
            string text = r != null ? r.Str("tos_text", "") : "";
            tosBox = Ui.Node(App.Overlay, "tos").Fill();
            var dim = Ui.Img(tosBox, new Color(0.03f, 0.035f, 0.045f, 0.9f), null, "dim");
            dim.rectTransform.Fill(); dim.raycastTarget = true;
            var p = Ui.Panel(tosBox, 0.98f, Ui.Orange);
            p.Box(0.5f, 0.5f, 0, 0, 1000, 760);
            p.pivot = new Vector2(0.5f, 0.5f);
            Ui.Section(p, "Terms of Service", Ui.Orange).Place(0, 1, 1, 1, 28, -50, 28, 26);
            ScrollRect sr;
            var c = Ui.Scroll(p, out sr, 0);
            sr.GetComponent<RectTransform>().Place(0, 0, 1, 1, 28, 120, 28, 70);
            var t = Ui.Label(c, string.IsNullOrEmpty(text) ? "Please accept the FortBox Terms of Service to continue." : text, 17, Ui.Text2, TextAnchor.UpperLeft, Ui.Regular);
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var btns = Ui.Node(p, "btns").Place(0, 0, 1, 0, 28, 28, 28, -96);
            btns.HList(14, 0, true);
            Ui.Button(btns, "SIGN OUT", Ui.Style.Secondary, "logout", () =>
            {
                Destroy(tosBox.gameObject); tosBox = null;
                Session.Logout(true); App.ClearHistory(); App.Show<LoginScreen>(false);
            }, 18).GetComponent<RectTransform>().Size(220, -1);
            Ui.Button(btns, "I ACCEPT", Ui.Style.Primary, "check", async () =>
            {
                if (await Session.AcceptTos()) { if (tosBox != null) Destroy(tosBox.gameObject); tosBox = null; Push.Reidentify(); }
                else Toast.Show("Could not reach the server", Ui.Red);
            }, 20).GetComponent<RectTransform>().Size(-1, -1, 1);
        }

        async System.Threading.Tasks.Task RefreshAsync()
        {
            await Session.Refresh();
            if (this != null && gameObject.activeInHierarchy) { RefreshPlayer(); RefreshQuests(); }
        }

        void RefreshPlayer()
        {
            nameText.text = Session.Player.Str("display_name", Session.Username);
            if (string.IsNullOrEmpty(nameText.text)) nameText.text = Session.Username;
            levelText.text = ("LEVEL " + Session.Level + "  ·  " + Session.Rank + "  ·  " + Session.Score.ToString("N0") + " SCORE").ToUpperInvariant();
            coinsText.text = Session.Coins.ToString("N0");
        }

        void RefreshQuests()
        {
            Ui.Clear(questsCol);
            foreach (var q in Quests.Today3())
            {
                var row = Ui.Node(questsCol, "q");
                row.Size(-1, 84);
                Ui.Label(row, q.name, 18, q.claimed ? Ui.Text3 : Ui.Text, TextAnchor.UpperLeft, Ui.Bold).rectTransform.Place(0, 1, 1, 1, 0, -26, 70, 0);
                Ui.Label(row, q.description, 14, Ui.Text3, TextAnchor.UpperLeft, Ui.Regular).rectTransform.Place(0, 1, 1, 1, 0, -50, 0, 26);
                Ui.Label(row, q.claimed ? "DONE" : (q.progress + "/" + q.target), 14, q.Done ? Ui.Green : Ui.Text2, TextAnchor.UpperRight, Ui.Bold).rectTransform.Place(0, 1, 1, 1, 0, -24, 0, 0);
                RectTransform br;
                Ui.StatBar(row, q.progress / (float)q.target, q.Done ? Ui.Green : Ui.Orange, out br);
                br.Place(0, 0, 1, 0, 0, 10, 0, -16);
            }
        }

        // ---- play modal: difficulty + start ------------------------------------
        void BuildPlayModal()
        {
            playModal = Ui.Node(root, "play-modal").Fill();
            var dim = Ui.Img(playModal, new Color(0.03f, 0.035f, 0.045f, 0.8f), null, "dim");
            dim.rectTransform.Fill();
            dim.raycastTarget = true;
            var p = Ui.Panel(playModal, 0.98f, Ui.Orange);
            p.Box(0.5f, 0.5f, 0, 0, 760, 520);
            p.pivot = new Vector2(0.5f, 0.5f);
            var col = Ui.Node(p, "col").Fill(30);
            col.VList(16, 0);
            Ui.Section(col, "Start a match", Ui.Orange);
            var t = Ui.Label(col, "", 44, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold);
            t.rectTransform.Size(-1, 56);
            t.name = "title";
            Ui.Label(col, "The match runs on the FortBox server: enemies, damage, coins and XP are the same as on PC.", 18, Ui.Text2, TextAnchor.UpperLeft, Ui.Regular).rectTransform.Size(-1, 52);
            Ui.Section(col, "Difficulty");
            var row = Ui.Node(col, "diff");
            row.Size(-1, 60);
            row.HList(10, 0, true);
            diffBtns.Clear();
            for (int i = 0; i < Lobby.Difficulties.Length; i++)
            {
                int k = i;
                var b = Ui.Button(row, Lobby.DifficultyLabels[i].ToUpperInvariant(), Ui.Style.Secondary, null, () => SetDiff(k), 17);
                b.GetComponent<RectTransform>().Size(-1, -1, 1);
                diffBtns.Add(b);
            }
            var btns = Ui.Node(col, "btns");
            btns.Size(-1, 76);
            btns.HList(14, 0, true);
            var cancel = Ui.Button(btns, "CANCEL", Ui.Style.Secondary, "close", () => playModal.gameObject.SetActive(false), 20);
            cancel.GetComponent<RectTransform>().Size(220, -1);
            var go = Ui.Button(btns, "DEPLOY", Ui.Style.Primary, "play", StartSolo, 24);
            go.GetComponent<RectTransform>().Size(-1, -1, 1);
            playModal.gameObject.SetActive(false);
        }

        void SetDiff(int k)
        {
            difficulty = k;
            Settings.Difficulty = Lobby.Difficulties[k];
            for (int i = 0; i < diffBtns.Count; i++) { diffBtns[i].style = i == k ? Ui.Style.Primary : Ui.Style.Secondary; diffBtns[i].Refresh(); }
            Select(selected);
        }

        void OpenPlay()
        {
            playModal.gameObject.SetActive(true);
            playModal.SetAsLastSibling();
            playModal.GetComponentInChildren<VerticalLayoutGroup>().transform.Find("title").GetComponent<Text>().text = Lobby.MapLabel(selected).ToUpperInvariant();
            SetDiff(difficulty);
        }

        async void StartSolo()
        {
            if (starting) return;
            starting = true;
            Toast.Show("Starting match on " + Lobby.MapLabel(selected) + "...", Ui.Orange, 2f);
            bool ok = await Lobby.PlaySolo(selected, Lobby.Difficulties[difficulty]);
            starting = false;
            if (this == null) return;
            playModal.gameObject.SetActive(false);
            if (!ok) Toast.Show(Lobby.LastError, Ui.Red, 4f);
        }

        public override bool OnBack()
        {
            if (playModal.gameObject.activeSelf) { playModal.gameObject.SetActive(false); return true; }
            Application.Quit();
            return true;
        }
    }
}
