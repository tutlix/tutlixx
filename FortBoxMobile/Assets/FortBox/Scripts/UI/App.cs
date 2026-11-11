// App bootstrap: creates the canvases, the screen router, the push socket and
// the global push handlers (kicked / banned / server notices / match start).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FortBox
{
    public abstract class UiScreen : MonoBehaviour
    {
        public RectTransform root;
        /// Build the screen's widgets under `root` (called once).
        public abstract void Build();
        public virtual void OnShow() { }
        public virtual void OnHide() { }
        /// Android back button; return true when handled.
        public virtual bool OnBack() { return false; }
    }

    public class App : MonoBehaviour
    {
        public static App I;
        public static RectTransform Screens, Overlay;
        public static Canvas UiCanvas;
        public static Camera MenuCam;
        readonly Dictionary<Type, UiScreen> screens = new Dictionary<Type, UiScreen>();
        readonly Stack<UiScreen> history = new Stack<UiScreen>();
        public UiScreen Current { get; private set; }
        RawImage backdrop;
        RectTransform backdropRoot;
        string backdropMap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("FortBox App");
            DontDestroyOnLoad(go);
            I = go.AddComponent<App>();
            I.Setup();
        }

        void Setup()
        {
            Config.Load();
            Settings.Apply();
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = Screen.autorotateToPortraitUpsideDown = false;
            Input.multiTouchEnabled = true;

            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                DontDestroyOnLoad(es);
            }
            // menu camera (the match makes its own)
            var cgo = new GameObject("MenuCamera");
            DontDestroyOnLoad(cgo);
            MenuCam = cgo.AddComponent<Camera>();
            MenuCam.clearFlags = CameraClearFlags.SolidColor;
            MenuCam.backgroundColor = Ui.Bg;
            MenuCam.cullingMask = 0;

            UiCanvas = MakeCanvas("UI", 10);
            backdropRoot = Ui.Node(UiCanvas.transform, "backdrop").Fill();
            backdrop = backdropRoot.gameObject.AddComponent<RawImage>();
            backdrop.color = Color.white;
            backdrop.raycastTarget = false;
            var shade = Ui.Node(backdropRoot, "shade").Fill().gameObject.AddComponent<RawImage>();
            shade.texture = HeroShade();
            shade.raycastTarget = false;
            Screens = Ui.Node(UiCanvas.transform, "screens").Fill();
            var ov = MakeCanvas("Overlay", 50);
            Overlay = Ui.Node(ov.transform, "overlay").Fill();

            Push.OnMessage += OnPush;
            Push.Start();
            Show<LoginScreen>();
        }

        static Canvas MakeCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            DontDestroyOnLoad(go);
            go.layer = 5;
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1600, 900);
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            s.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        /// Darkening like _uo_hero_backdrop: heavier left (rail) and bottom, scanlines.
        static Texture2D HeroShade()
        {
            int w = 64, h = 108;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float fx = x / (float)(w - 1), fy = 1f - y / (float)(h - 1);
                    float a = 0.60f + 0.34f * Mathf.Max(0f, 1f - fx * 2.2f) + 0.34f * Mathf.Max(0f, (fy - 0.5f) / 0.5f) + 0.2f * Mathf.Max(0f, (fx - 0.7f) / 0.3f);
                    a = Mathf.Min(0.95f, a);
                    if (y % 3 == 0) a = Mathf.Min(0.97f, a + 0.05f);
                    px[y * w + x] = new Color32(8, 9, 12, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// The selected map's first-person thumbnail behind the menus.
        public void SetBackdrop(string mapId, bool visible = true)
        {
            backdropRoot.gameObject.SetActive(visible);
            if (!visible || mapId == backdropMap) return;
            backdropMap = mapId;
            var tex = Resources.Load<Texture2D>("FortBox/thumbs/" + mapId);
            backdrop.texture = tex;
            backdrop.color = tex != null ? Color.white : Ui.Bg;
            var fit = backdrop.GetComponent<AspectRatioFitter>() ?? backdrop.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = tex != null ? tex.width / (float)tex.height : 16f / 9f;
        }

        public void ShowMenus(bool on)
        {
            UiCanvas.gameObject.SetActive(on);
            MenuCam.enabled = on;
        }

        public static T Show<T>(bool remember = true) where T : UiScreen
        {
            return I.ShowImpl<T>(remember);
        }

        T ShowImpl<T>(bool remember) where T : UiScreen
        {
            UiScreen s;
            if (!screens.TryGetValue(typeof(T), out s) || s == null)
            {
                var rt = Ui.Node(Screens, typeof(T).Name).Fill();
                s = rt.gameObject.AddComponent<T>();
                s.root = rt;
                s.Build();
                screens[typeof(T)] = s;
            }
            if (Current == s) { s.OnShow(); return (T)s; }
            if (Current != null)
            {
                Current.OnHide();
                Current.gameObject.SetActive(false);
                if (remember) history.Push(Current);
            }
            Current = s;
            s.gameObject.SetActive(true);
            s.transform.SetAsLastSibling();
            s.OnShow();
            return (T)s;
        }

        public static T Get<T>() where T : UiScreen
        {
            UiScreen s;
            return I.screens.TryGetValue(typeof(T), out s) ? (T)s : null;
        }

        public static void Back()
        {
            if (I.history.Count == 0) return;
            var prev = I.history.Pop();
            if (I.Current != null) { I.Current.OnHide(); I.Current.gameObject.SetActive(false); }
            I.Current = prev;
            prev.gameObject.SetActive(true);
            prev.OnShow();
        }

        public static void ClearHistory() { I.history.Clear(); }

        void Update()
        {
            MainThread.Pump();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (MatchController.Active != null) MatchController.Active.OnBackPressed();
                else if (Current != null && !Current.OnBack()) Back();
            }
        }

        void OnApplicationQuit() { Push.Stop(); }

        // ---- server pushes -------------------------------------------------------
        void OnPush(string type, Dictionary<string, object> msg)
        {
            var data = msg.O("data") ?? new Dictionary<string, object>();
            switch (type)
            {
                case "kicked":
                case "banned":
                case "force_logout":
                    {
                        string why = data.Str("reason", msg.Str("msg", type == "banned" ? "You have been banned." : "You were signed out."));
                        if (MatchController.Active != null) MatchController.Active.Quit(false);
                        Session.Logout(type != "kicked");
                        Push.Reidentify();
                        ClearHistory();
                        Show<LoginScreen>(false);
                        Toast.Show(why, Ui.Red, 5f);
                        break;
                    }
                case "server_notice":
                    {
                        string t = data.Str("title"), m = data.Str("message");
                        Toast.Show(string.IsNullOrEmpty(t) ? m : (t + (string.IsNullOrEmpty(m) ? "" : " - " + m)), Ui.Amber, 4f);
                        break;
                    }
                case "match_start":
                    if (MatchController.Active == null && Lobby.ServerId > 0) Lobby.OnMatchStart(data);
                    break;
                case "lobby_closed":
                    if (MatchController.Active == null && Lobby.ServerId > 0)
                    {
                        Lobby.ServerId = 0;
                        Toast.Show("The lobby was closed by its leader.", Ui.Amber);
                        if (Current is LobbyScreen) { ClearHistory(); Show<MenuScreen>(false); }
                    }
                    break;
                case "match_kicked":
                    if (MatchController.Active != null) MatchController.Active.Quit(false);
                    Toast.Show(data.Str("reason", "Removed from the match"), Ui.Red, 4f);
                    break;
            }
            if (MatchController.Active != null) MatchController.Active.OnPush(type, msg, data);
            var lobby = Current as LobbyScreen;
            if (lobby != null && (type == "lobby_update" || type == "lobby_members")) lobby.Refresh();
        }
    }
}
