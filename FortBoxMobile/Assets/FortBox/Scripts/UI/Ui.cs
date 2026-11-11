// Procedural uGUI kit in the PC "Urban Ops" style: dark flat panels with
// 1px borders, orange primary blocks with a hard shadow, small-caps labels
// with an orange dash, the Fredoka font and the game's white icon set.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FortBox
{
    public static class Ui
    {
        // ---- palette (FortRoyal2D UO_*) ---------------------------------------
        public static readonly Color Bg = C(12, 13, 16);
        public static readonly Color Rail = C(16, 17, 21);
        public static readonly Color PanelBg = C(21, 23, 28);
        public static readonly Color PanelHi = C(31, 34, 41);
        public static readonly Color Line = C(46, 50, 60);
        public static readonly Color Text = C(242, 244, 247);
        public static readonly Color Text2 = C(160, 167, 180);
        public static readonly Color Text3 = C(106, 112, 126);
        public static readonly Color Orange = C(255, 106, 31);
        public static readonly Color OrangeDk = C(206, 76, 12);
        public static readonly Color Green = C(64, 210, 120);
        public static readonly Color Red = C(236, 72, 80);
        public static readonly Color Amber = C(255, 184, 48);
        public const int Radius = 5;

        public static Color C(int r, int g, int b, int a = 255) { return new Color32((byte)r, (byte)g, (byte)b, (byte)a); }
        public static Color A(Color c, float a) { c.a = a; return c; }

        // ---- fonts / sprites ----------------------------------------------------
        static Font bold, semi, regular;
        public static Font Bold { get { return bold ?? (bold = LoadFont("Fredoka-Bold")); } }
        public static Font Semi { get { return semi ?? (semi = LoadFont("Fredoka-SemiBold")); } }
        public static Font Regular { get { return regular ?? (regular = LoadFont("Fredoka-Regular")); } }
        static Font LoadFont(string n)
        {
            var f = Resources.Load<Font>("FortBox/fonts/" + n);
            return f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        static readonly Dictionary<int, Sprite> rounds = new Dictionary<int, Sprite>();
        static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
        static Sprite circle, white;

        /// White rounded-rect 9-slice sprite.
        public static Sprite Round(int r = Radius)
        {
            Sprite s;
            if (rounds.TryGetValue(r, out s)) return s;
            int n = r * 2 + 4;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            s = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r + 1, r + 1, r + 1, r + 1));
            rounds[r] = s;
            return s;
        }

        /// Rounded rect outline (1px) 9-slice sprite.
        static Sprite outline;
        public static Sprite Outline()
        {
            if (outline != null) return outline;
            int r = Radius, n = r * 2 + 4;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float outer = Mathf.Clamp01(r - d + 0.5f);
                    float inner = Mathf.Clamp01(r - 1f - d + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(outer - inner) * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            outline = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r + 1, r + 1, r + 1, r + 1));
            return outline;
        }

        public static Sprite Circle()
        {
            if (circle != null) return circle;
            int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - 63.5f) * (x - 63.5f) + (y - 63.5f) * (y - 63.5f));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(63.5f - d) * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            circle = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return circle;
        }

        public static Sprite White()
        {
            if (white != null) return white;
            var t = Texture2D.whiteTexture;
            white = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));
            return white;
        }

        public static Sprite Icon(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Sprite s;
            if (icons.TryGetValue(name, out s)) return s;
            var t = Resources.Load<Texture2D>("FortBox/icons/" + name);
            s = t != null ? Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f)) : null;
            icons[name] = s;
            return s;
        }

        // ---- layout helpers ----------------------------------------------------
        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// Anchors (0..1) + pixel offsets.
        public static RectTransform Place(this RectTransform rt, float ax0, float ay0, float ax1, float ay1, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = new Vector2(ax0, ay0);
            rt.anchorMax = new Vector2(ax1, ay1);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static RectTransform Fill(this RectTransform rt, float pad = 0) { return rt.Place(0, 0, 1, 1, pad, pad, pad, pad); }

        /// Fixed-size box anchored at (ax, ay) with pivot at the same point.
        public static RectTransform Box(this RectTransform rt, float ax, float ay, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(ax, ay);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static LayoutElement Size(this RectTransform rt, float w = -1, float h = -1, float flexW = -1, float flexH = -1)
        {
            var le = rt.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
            if (w >= 0) le.preferredWidth = le.minWidth = w;
            if (h >= 0) le.preferredHeight = le.minHeight = h;
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        public static VerticalLayoutGroup VList(this RectTransform rt, float spacing = 8, int pad = 0, bool expandW = true)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childControlHeight = true; v.childControlWidth = true;
            v.childForceExpandHeight = false; v.childForceExpandWidth = expandW;
            return v;
        }

        public static HorizontalLayoutGroup HList(this RectTransform rt, float spacing = 8, int pad = 0, bool expandH = true)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childControlHeight = true; h.childControlWidth = true;
            h.childForceExpandHeight = expandH; h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            return h;
        }

        // ---- widgets -----------------------------------------------------------
        public static Image Img(Transform parent, Color c, Sprite s = null, string name = "img")
        {
            var rt = Node(parent, name);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = s;
            im.color = c;
            if (s != null && s.border != Vector4.zero) im.type = Image.Type.Sliced;
            im.raycastTarget = false;
            return im;
        }

        /// Flat dark panel with a 1px border and an optional accent tab top-left.
        public static RectTransform Panel(Transform parent, float alpha = 0.93f, Color? accent = null, bool border = true, string name = "panel")
        {
            var im = Img(parent, A(PanelBg, alpha), Round(), name);
            im.raycastTarget = true;
            var rt = im.rectTransform;
            if (border) Img(rt, Line, Outline(), "border").rectTransform.Fill();
            if (accent.HasValue) Img(rt, accent.Value, null, "accent").rectTransform.Place(0, 1, 0, 1, 0, -26, 0, 0).sizeDelta = new Vector2(3, 26);
            return rt;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, Font font = null, string name = "label")
        {
            var rt = Node(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font ?? Semi;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = false;
            return t;
        }

        /// Small-caps section label with an orange dash in front.
        public static RectTransform Section(Transform parent, string text, Color? color = null)
        {
            var rt = Node(parent, "section");
            rt.Size(-1, 22);
            Img(rt, Orange, null, "dash").rectTransform.Box(0, 0.5f, 0, 0, 10, 2);
            var t = Label(rt, text.ToUpperInvariant(), 15, color ?? Text3, TextAnchor.MiddleLeft, Bold);
            t.rectTransform.Place(0, 0, 1, 1, 16, 0, 0, 0);
            return rt;
        }

        public static Image IconImg(Transform parent, string icon, Color color, float size)
        {
            var im = Img(parent, color, Icon(icon), "icon-" + icon);
            im.preserveAspect = true;
            im.rectTransform.sizeDelta = new Vector2(size, size);
            return im;
        }

        public enum Style { Primary, Secondary, Ghost, Danger, Success }

        public class Btn : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public Style style;
            public Image fill, border, shade;
            public Text text;
            public Image icon;
            public bool disabled;
            public Button button;
            bool down, over;

            public void SetDisabled(bool d) { disabled = d; Refresh(); }
            public void SetLabel(string s) { if (text != null) text.text = s; }

            public void Refresh()
            {
                Color f, b, t;
                bool hot = down || over;
                if (disabled) { f = C(28, 30, 36); b = Line; t = Text3; }
                else switch (style)
                    {
                        case Style.Primary: f = hot ? C(255, 132, 64) : Orange; b = Color.clear; t = C(20, 10, 4); break;
                        case Style.Danger: f = hot ? C(250, 96, 104) : Red; b = Color.clear; t = Color.white; break;
                        case Style.Success: f = hot ? C(90, 228, 146) : Green; b = Color.clear; t = C(6, 30, 14); break;
                        case Style.Ghost: f = hot ? A(Color.white, 0.07f) : Color.clear; b = Color.clear; t = hot ? Text : Text2; break;
                        default: f = hot ? C(40, 44, 53) : PanelHi; b = hot ? Text2 : Line; t = Text; break;
                    }
                if (fill != null) fill.color = f;
                if (border != null) border.color = b;
                if (shade != null) shade.enabled = !disabled && (style == Style.Primary || style == Style.Danger || style == Style.Success);
                if (shade != null) shade.color = style == Style.Primary ? OrangeDk : (style == Style.Danger ? C(160, 40, 48) : C(30, 140, 76));
                if (text != null) text.color = t;
                if (icon != null) icon.color = t;
                transform.localScale = down && !disabled ? Vector3.one * 0.97f : Vector3.one;
            }

            public void OnPointerDown(PointerEventData e) { down = true; Refresh(); }
            public void OnPointerUp(PointerEventData e) { down = false; Refresh(); }
            public void OnPointerEnter(PointerEventData e) { over = true; Refresh(); }
            public void OnPointerExit(PointerEventData e) { over = false; down = false; Refresh(); }
        }

        public static Btn Button(Transform parent, string label, Style style, string icon, UnityAction onClick, int fontSize = 18)
        {
            var rt = Node(parent, "btn-" + label);
            var fill = rt.gameObject.AddComponent<Image>();
            fill.sprite = Round();
            fill.type = Image.Type.Sliced;
            var b = rt.gameObject.AddComponent<Btn>();
            b.style = style;
            b.fill = fill;
            b.shade = Img(rt, OrangeDk, null, "shade");
            b.shade.rectTransform.Place(0, 0, 1, 0, 0, 0, 0, -3);
            b.border = Img(rt, Line, Outline(), "border");
            b.border.rectTransform.Fill();
            var row = Node(rt, "row");
            row.Fill(10);
            var h = row.HList(8, 0, false);
            h.childAlignment = TextAnchor.MiddleCenter;
            if (!string.IsNullOrEmpty(icon))
            {
                b.icon = IconImg(row, icon, Text, fontSize * 1.15f);
                ((RectTransform)b.icon.transform).Size(fontSize * 1.15f, fontSize * 1.15f);
            }
            if (!string.IsNullOrEmpty(label))
            {
                b.text = Label(row, label, fontSize, Text, TextAnchor.MiddleCenter, Bold);
                b.text.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            b.button = rt.gameObject.AddComponent<Button>();
            b.button.transition = Selectable.Transition.None;
            b.button.onClick.AddListener(() => { if (!b.disabled && onClick != null) onClick(); });
            b.Refresh();
            return b;
        }

        public static InputField Input(Transform parent, string placeholder, bool password = false, int size = 20)
        {
            var rt = Node(parent, "input-" + placeholder);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = Round();
            bg.type = Image.Type.Sliced;
            bg.color = C(14, 15, 19);
            Img(rt, Line, Outline(), "border").rectTransform.Fill();
            var ph = Label(rt, placeholder, size, Text3, TextAnchor.MiddleLeft, Regular, "placeholder");
            ph.rectTransform.Fill(); ph.rectTransform.offsetMin = new Vector2(14, 0); ph.rectTransform.offsetMax = new Vector2(-14, 0);
            var tx = Label(rt, "", size, Text, TextAnchor.MiddleLeft, Semi, "text");
            tx.rectTransform.Fill(); tx.rectTransform.offsetMin = new Vector2(14, 0); tx.rectTransform.offsetMax = new Vector2(-14, 0);
            tx.supportRichText = false;
            tx.horizontalOverflow = HorizontalWrapMode.Overflow;
            var f = rt.gameObject.AddComponent<InputField>();
            f.targetGraphic = bg;
            f.textComponent = tx;
            f.placeholder = ph;
            f.caretColor = Orange;
            f.selectionColor = A(Orange, 0.35f);
            f.customCaretColor = true;
            f.caretWidth = 2;
            if (password) { f.contentType = InputField.ContentType.Password; f.inputType = InputField.InputType.Password; }
            return f;
        }

        public static Toggle Check(Transform parent, string label, bool on, UnityAction<bool> changed)
        {
            var rt = Node(parent, "toggle-" + label);
            rt.Size(-1, 40);
            var bg = Img(rt, C(14, 15, 19), Round(), "box");
            bg.raycastTarget = true;
            bg.rectTransform.Box(0, 0.5f, 0, 0, 30, 30);
            Img(bg.transform, Line, Outline(), "border").rectTransform.Fill();
            var mark = Img(bg.transform, Orange, Round(3), "mark");
            mark.rectTransform.Fill(6);
            var t = Label(rt, label, 18, Text2);
            t.rectTransform.Place(0, 0, 1, 1, 42, 0, 0, 0);
            t.raycastTarget = true;
            var tg = rt.gameObject.AddComponent<Toggle>();
            tg.targetGraphic = bg;
            tg.graphic = mark;
            tg.isOn = on;
            if (changed != null) tg.onValueChanged.AddListener(changed);
            return tg;
        }

        public static Slider Slide(Transform parent, string label, float min, float max, float value, UnityAction<float> changed, Func<float, string> fmt = null)
        {
            var rt = Node(parent, "slider-" + label);
            rt.Size(-1, 54);
            var t = Label(rt, label, 17, Text2);
            t.rectTransform.Place(0, 1, 1, 1, 0, -24, 0, 0);
            var val = Label(rt, "", 17, Text, TextAnchor.MiddleRight, Bold);
            val.rectTransform.Place(0, 1, 1, 1, 0, -24, 0, 0);
            var track = Node(rt, "track").Place(0, 0, 1, 0, 0, 6, 0, -22);
            Img(track, C(38, 41, 49), Round(3), "bg").rectTransform.Place(0, 0.5f, 1, 0.5f, 0, -3, 0, -3);
            var fillArea = Node(track, "fillarea").Place(0, 0.5f, 1, 0.5f, 0, -3, 0, -3);
            var fill = Img(fillArea, Orange, Round(3), "fill");
            fill.rectTransform.Fill();
            var handleArea = Node(track, "handlearea").Fill();
            var handle = Img(handleArea, Text, Circle(), "handle");
            handle.raycastTarget = true;
            handle.rectTransform.sizeDelta = new Vector2(24, 24);
            var s = rt.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = min; s.maxValue = max;
            s.value = value;
            fmt = fmt ?? (v => v.ToString("0.00"));
            val.text = fmt(value);
            s.onValueChanged.AddListener(v => { val.text = fmt(v); if (changed != null) changed(v); });
            return s;
        }

        /// Vertical scroll view; returns the content node (VerticalLayoutGroup).
        public static RectTransform Scroll(Transform parent, out ScrollRect sr, float spacing = 8)
        {
            var rt = Node(parent, "scroll");
            var vp = Node(rt, "viewport").Fill();
            vp.gameObject.AddComponent<RectMask2D>();
            var hit = vp.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var content = Node(vp, "content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            content.VList(spacing, 0);
            var csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr = rt.gameObject.AddComponent<ScrollRect>();
            sr.viewport = vp;
            sr.content = content;
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 30;
            return content;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        /// Thin bar with 25% tick marks (_uo_stat_bar).
        public static Image StatBar(Transform parent, float value, Color color, out RectTransform root)
        {
            root = Node(parent, "statbar");
            Img(root, C(38, 41, 49), null, "bg").rectTransform.Fill();
            var f = Img(root, color, null, "fill");
            f.rectTransform.Place(0, 0, Mathf.Clamp01(value), 1);
            for (int i = 1; i < 4; i++)
                Img(root, C(14, 15, 19), null, "tick").rectTransform.Place(i / 4f, 0, i / 4f, 1, -0.5f, 0, -0.5f, 0);
            return f;
        }

        public static Color32 PlayerColor(string name)
        {
            var map = ColorTable();
            List<object> l;
            object o;
            if (name != null && map.TryGetValue(name, out o) && (l = o as List<object>) != null && l.Count >= 3)
                return new Color32((byte)l.F(0), (byte)l.F(1), (byte)l.F(2), 255);
            return new Color32(0, 0, 255, 255);
        }

        static Dictionary<string, object> colors;
        public static Dictionary<string, object> ColorTable()
        {
            if (colors != null) return colors;
            var ta = Resources.Load<TextAsset>("FortBox/data/colors");
            colors = ta != null ? (Json.Obj(ta.text) ?? new Dictionary<string, object>()) : new Dictionary<string, object>();
            return colors;
        }
    }

    /// Short message at the bottom of the screen.
    public class Toast : MonoBehaviour
    {
        static Toast inst;
        Text text;
        Image bar;
        CanvasGroup cg;
        float until;

        public static void Show(string msg, Color? color = null, float secs = 2.6f)
        {
            if (inst == null)
            {
                var root = App.Overlay;
                if (root == null) { Debug.Log("[toast] " + msg); return; }
                var p = Ui.Panel(root, 0.97f, null, true, "toast");
                p.Box(0.5f, 0, 0, 40, 720, 64);
                inst = p.gameObject.AddComponent<Toast>();
                inst.cg = p.gameObject.AddComponent<CanvasGroup>();
                inst.cg.blocksRaycasts = false;
                inst.bar = Ui.Img(p, Ui.Orange, null, "bar");
                inst.bar.rectTransform.Place(0, 0, 0, 1, 0, 0, 0, 0);
                inst.bar.rectTransform.sizeDelta = new Vector2(4, 0);
                inst.text = Ui.Label(p, "", 20, Ui.Text, TextAnchor.MiddleCenter, Ui.Semi);
                inst.text.rectTransform.Fill(14);
            }
            inst.transform.SetAsLastSibling();
            inst.text.text = msg;
            inst.bar.color = color ?? Ui.Orange;
            inst.until = Time.unscaledTime + secs;
            inst.gameObject.SetActive(true);
        }

        void Update()
        {
            float left = until - Time.unscaledTime;
            cg.alpha = Mathf.Clamp01(left / 0.3f);
            if (left <= 0) gameObject.SetActive(false);
        }
    }
}
