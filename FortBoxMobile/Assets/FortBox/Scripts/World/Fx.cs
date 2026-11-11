// Particles and small 3D objects of the first-person view (PC panda_fx):
// glows, tracer streaks, soft smoke, explosions, hit sparks, muzzle flashes,
// power-up chests, cover crates and grenades. Glows/streaks are batched into
// one dynamic mesh per frame.
using System.Collections.Generic;
using UnityEngine;

namespace FortBox
{
    /// Builds simple box geometry in PC coordinates (x, y up-map is -y world, z up).
    public class MeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Color32> c = new List<Color32>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        // Panda local (x, y, z) -> Unity local (x, z, y) scaled
        static Vector3 P(float x, float y, float z) { return new Vector3(x, z, y) * MapWorld.S; }

        void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color32 col)
        {
            var nr = -Vector3.Cross(b - a, d - a).normalized;
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
            for (int k = 0; k < 4; k++) { n.Add(nr); c.Add(col); }
            uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.one); uv.Add(Vector2.up);
            t.Add(i); t.Add(i + 2); t.Add(i + 1);     // Unity is left-handed: reversed
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }

        /// Eight corners (bottom 4 then top 4, counter-clockwise from above in PC space).
        public void Hexa(Vector3[] q, Color col)
        {
            Color32 cc = col;
            // faces wound clockwise as seen from outside in Unity (left-handed)
            Quad(q[0], q[3], q[2], q[1], Shade(cc, 0.7f));   // bottom
            Quad(q[4], q[5], q[6], q[7], cc);                 // top
            Quad(q[0], q[1], q[5], q[4], Shade(cc, 0.86f));
            Quad(q[1], q[2], q[6], q[5], Shade(cc, 0.92f));
            Quad(q[2], q[3], q[7], q[6], Shade(cc, 0.86f));
            Quad(q[3], q[0], q[4], q[7], Shade(cc, 0.92f));
        }

        static Color32 Shade(Color32 c, float k) { return new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), c.a); }

        public void Cuboid(float x0, float x1, float y0, float y1, float z0, float z1, Color col)
        {
            Hexa(new[] { P(x0, y0, z0), P(x1, y0, z0), P(x1, y1, z0), P(x0, y1, z0), P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1) }, col);
        }

        public void Beam(Vector3 p0, Vector3 p1, float w, float h, Color col)
        {
            var d = (p1 - p0).normalized;
            var up = Mathf.Abs(d.z) < 0.95f ? Vector3.forward : Vector3.up;   // PC z-up
            var side = Vector3.Cross(d, up).normalized;
            var up2 = Vector3.Cross(side, d).normalized;
            var pts = new Vector3[8];
            int k = 0;
            foreach (var p in new[] { p0, p1 })
                foreach (var s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
                {
                    var q = p + side * s.x * w / 2f + up2 * s.y * h / 2f;
                    pts[k++] = P(q.x, q.y, q.z);
                }
            // beam corners are ordered around the axis; reorder to bottom/top quads
            Hexa(new[] { pts[0], pts[3], pts[2], pts[1], pts[4], pts[7], pts[6], pts[5] }, col);
        }

        public Mesh ToMesh()
        {
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        public bool Empty { get { return v.Count == 0; } }
    }

    public class Fx : MonoBehaviour
    {
        public static Fx I;
        Material addMat, alphaMat, worldMat, glowMat;
        Mesh addMesh, alphaMesh;
        readonly List<Vector3> av = new List<Vector3>(), sv = new List<Vector3>();
        readonly List<Color32> ac = new List<Color32>(), sc = new List<Color32>();
        readonly List<Vector2> au = new List<Vector2>(), su = new List<Vector2>();
        readonly List<int> at = new List<int>(), st = new List<int>();
        Camera cam;

        struct Particle
        {
            public Vector3 p, v;           // world px (x, y, z up)
            public float life, age, size, grow, drag, gravity;
            public Color col;
            public bool smoke;
        }
        readonly List<Particle> parts = new List<Particle>();

        public static Fx Create(Transform parent)
        {
            var go = new GameObject("fx");
            go.transform.SetParent(parent, false);
            I = go.AddComponent<Fx>();
            I.Init();
            return I;
        }

        void Init()
        {
            var sh = MapWorld.Sh("FortBoxFx");
            addMat = new Material(sh);
            addMat.SetTexture("_MainTex", SoftDot());
            addMat.SetFloat("_Src", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            addMat.SetFloat("_Dst", (float)UnityEngine.Rendering.BlendMode.One);
            addMat.renderQueue = 3100;
            alphaMat = new Material(sh);
            alphaMat.SetTexture("_MainTex", SoftDot());
            alphaMat.SetFloat("_Src", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            alphaMat.SetFloat("_Dst", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            alphaMat.renderQueue = 3050;
            worldMat = new Material(MapWorld.Sh("FortBoxWorld"));
            worldMat.SetFloat("_SpecK", 0.1f);
            glowMat = new Material(MapWorld.Sh("FortBoxGlow"));
            addMesh = new Mesh(); addMesh.MarkDynamic();
            alphaMesh = new Mesh(); alphaMesh.MarkDynamic();
        }

        void OnDestroy() { if (I == this) I = null; }

        static Texture2D dot;
        public static Texture2D SoftDot()
        {
            if (dot != null) return dot;
            int n = 64;
            dot = new Texture2D(n, n, TextureFormat.RGBA32, false);
            dot.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f)) / 31.5f;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3 - 2 * a);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            dot.SetPixels32(px);
            dot.Apply();
            return dot;
        }

        static Vector3 U(Vector3 w) { return MapWorld.W2U(w.x, w.y, w.z); }

        // ---- immediate items (call every frame) ----------------------------------
        void Billboard(List<Vector3> V, List<Color32> C, List<Vector2> UV, List<int> T, Vector3 pu, float size, Color col)
        {
            if (cam == null) return;
            var r = cam.transform.right * size;
            var u = cam.transform.up * size;
            int i = V.Count;
            V.Add(pu - r - u); V.Add(pu + r - u); V.Add(pu + r + u); V.Add(pu - r + u);
            Color32 c = col;
            C.Add(c); C.Add(c); C.Add(c); C.Add(c);
            UV.Add(new Vector2(0, 0)); UV.Add(new Vector2(1, 0)); UV.Add(new Vector2(1, 1)); UV.Add(new Vector2(0, 1));
            T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3);
        }

        /// Glow dot at world px (x, y, z), radius in px.
        public void Glow(float x, float y, float z, float r, Color col, float a = 1f)
        {
            col.a *= a;
            Billboard(av, ac, au, at, U(new Vector3(x, y, z)), r * MapWorld.S * 2.2f, col);
        }

        /// Camera-facing streak between two world px points.
        public void Streak(Vector3 a, Vector3 b, float w, Color col, float alpha = 1f)
        {
            if (cam == null) return;
            var pa = U(a); var pb = U(b);
            var d = pb - pa;
            if (d.sqrMagnitude < 1e-8f) return;
            var mid = (pa + pb) * 0.5f;
            var toCam = (cam.transform.position - mid).normalized;
            var side = Vector3.Cross(d.normalized, toCam).normalized * w * MapWorld.S;
            int i = av.Count;
            av.Add(pa - side); av.Add(pa + side); av.Add(pb + side); av.Add(pb - side);
            col.a *= alpha;
            Color32 tail = col; tail.a = 0;
            Color32 head = col;
            ac.Add(tail); ac.Add(tail); ac.Add(head); ac.Add(head);
            au.Add(new Vector2(0, 0.5f)); au.Add(new Vector2(1, 0.5f)); au.Add(new Vector2(1, 0.5f)); au.Add(new Vector2(0, 0.5f));
            at.Add(i); at.Add(i + 1); at.Add(i + 2); at.Add(i); at.Add(i + 2); at.Add(i + 3);
        }

        public void Soft(float x, float y, float z, float r, Color col, float a = 1f)
        {
            col.a *= a;
            Billboard(sv, sc, su, st, U(new Vector3(x, y, z)), r * MapWorld.S * 2f, col);
        }

        // ---- particles -------------------------------------------------------------
        public void Spawn(Vector3 p, Vector3 v, float life, float size, Color col, bool smoke = false, float grow = 0f, float drag = 1.5f, float gravity = 0f)
        {
            if (parts.Count > 900) return;
            parts.Add(new Particle { p = p, v = v, life = life, size = size, col = col, smoke = smoke, grow = grow, drag = drag, gravity = gravity });
        }

        /// Grenade / boom explosion (radius px).
        public void Explosion(float x, float y, float r)
        {
            var c = new Vector3(x, y, 6);
            Spawn(c + Vector3.forward * 10, Vector3.zero, 0.35f, r * 0.9f, new Color(1f, 0.85f, 0.55f, 1f), false, r * 1.2f, 0);
            for (int i = 0; i < 26; i++)
            {
                var dir = Random.insideUnitSphere; dir.z = Mathf.Abs(dir.z) + 0.2f;
                Spawn(c, dir * Random.Range(120f, 360f), Random.Range(0.35f, 0.8f), Random.Range(6f, 14f), new Color(1f, Random.Range(0.45f, 0.75f), 0.18f, 1f), false, -6f, 3.2f, 240f);
            }
            for (int i = 0; i < 14; i++)
            {
                var dir = Random.insideUnitCircle;
                Spawn(c + new Vector3(dir.x * r * 0.3f, dir.y * r * 0.3f, 8), new Vector3(dir.x * 60, dir.y * 60, Random.Range(20f, 70f)), Random.Range(1.4f, 2.6f), Random.Range(18f, 30f), new Color(0.22f, 0.21f, 0.2f, 0.65f), true, 26f, 1.2f);
            }
        }

        /// Sparks where a shot lands.
        public void Hit(Vector3 p, Color col, int n = 6)
        {
            for (int i = 0; i < n; i++)
                Spawn(p, Random.insideUnitSphere * Random.Range(60f, 180f) + Vector3.forward * 40, Random.Range(0.15f, 0.35f), Random.Range(1.6f, 3f), col, false, -4f, 4f, 300f);
        }

        /// Enemy down: a burst in its colour and some dust.
        public void Kill(float x, float y, Color col)
        {
            for (int i = 0; i < 16; i++)
                Spawn(new Vector3(x, y, 26), Random.insideUnitSphere * Random.Range(80f, 200f) + Vector3.forward * 60, Random.Range(0.3f, 0.6f), Random.Range(2.5f, 4.5f), col, false, -4f, 3f, 320f);
            for (int i = 0; i < 6; i++)
                Spawn(new Vector3(x + Random.Range(-10f, 10f), y + Random.Range(-10f, 10f), 4), new Vector3(Random.Range(-20f, 20f), Random.Range(-20f, 20f), 20), Random.Range(0.8f, 1.4f), Random.Range(10f, 16f), new Color(0.55f, 0.5f, 0.42f, 0.4f), true, 14f, 1.5f);
        }

        void LateUpdate()
        {
            cam = Camera.main;
            float dt = Time.deltaTime;
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var p = parts[i];
                p.age += dt;
                if (p.age >= p.life) { parts.RemoveAt(i); continue; }
                p.v *= Mathf.Max(0f, 1f - p.drag * dt);
                p.v.z -= p.gravity * dt;
                p.p += p.v * dt;
                if (p.p.z < 0.5f) { p.p.z = 0.5f; p.v.z = Mathf.Abs(p.v.z) * 0.3f; }
                p.size = Mathf.Max(0.2f, p.size + p.grow * dt);
                parts[i] = p;
                float k = 1f - p.age / p.life;
                if (p.smoke) Soft(p.p.x, p.p.y, p.p.z, p.size, p.col, k);
                else Glow(p.p.x, p.p.y, p.p.z, p.size, p.col, k);
            }
            Flush(addMesh, av, ac, au, at, addMat);
            Flush(alphaMesh, sv, sc, su, st, alphaMat);
        }

        void Flush(Mesh m, List<Vector3> V, List<Color32> C, List<Vector2> UV, List<int> T, Material mat)
        {
            m.Clear();
            if (V.Count > 0)
            {
                if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V); m.SetColors(C); m.SetUVs(0, UV); m.SetTriangles(T, 0);
                m.bounds = new Bounds(cam != null ? cam.transform.position : Vector3.zero, Vector3.one * 2000f);
                Graphics.DrawMesh(m, Matrix4x4.identity, mat, 0);
            }
            V.Clear(); C.Clear(); UV.Clear(); T.Clear();
        }

        // ---- props ------------------------------------------------------------------
        readonly Dictionary<string, Mesh> propMeshes = new Dictionary<string, Mesh>();
        readonly Dictionary<string, Mesh> propGlow = new Dictionary<string, Mesh>();

        /// Power-up chest (panda_fx._build_chest) with a glowing lock in its colour.
        public void Chest(string key, float x, float y, Color accent)
        {
            string k = "chest:" + ColorUtility.ToHtmlStringRGB(accent);
            Mesh body, glow;
            if (!propMeshes.TryGetValue(k, out body))
            {
                var g = new MeshBuilder(); var gl = new MeshBuilder();
                Color wood = Ui.C(96, 60, 28), wood2 = Ui.C(150, 100, 48), band = Ui.C(58, 58, 64);
                g.Cuboid(-11, 11, -7, 7, 0, 10, wood);
                g.Cuboid(-11.6f, 11.6f, -7.6f, 7.6f, 10, 15.5f, wood2);
                foreach (var bx in new[] { -8f, 8f }) g.Cuboid(bx - 1.2f, bx + 1.2f, -7.8f, 7.8f, 0, 15.8f, band);
                foreach (var by in new[] { -7.4f, 7.4f }) gl.Cuboid(-2.4f, 2.4f, by - 0.5f, by + 0.5f, 7.5f, 12.5f, accent);
                body = propMeshes[k] = g.ToMesh();
                glow = propGlow[k] = gl.ToMesh();
            }
            else glow = propGlow[k];
            float ph = (Mathf.Abs(key.GetHashCode()) & 1023) / 160f;
            float t = Time.time * 1000f;
            var pos = MapWorld.W2U(x, y, 3f + Mathf.Sin(t / 300f + ph) * 2.5f);
            var rot = Quaternion.Euler(0, -((t / 18f + ph * 57f) % 360f), 0);
            var mtx = Matrix4x4.TRS(pos, rot, Vector3.one);
            Graphics.DrawMesh(body, mtx, worldMat, 0);
            Graphics.DrawMesh(glow, mtx, glowMat, 0);
            Glow(x, y, 8, 18, accent, 0.25f + 0.1f * Mathf.Sin(t / 200f));
        }

        /// Server cover crate w x d px (panda_fx._build_cover).
        public static Mesh CoverMesh(float w, float d)
        {
            var g = new MeshBuilder();
            Color wood = Ui.C(122, 86, 52), frame = Ui.C(88, 60, 36);
            const float H = 34f;
            float hw = Mathf.Max(4f, w / 2f), hd = Mathf.Max(4f, d / 2f);
            g.Cuboid(-hw, hw, -hd, hd, 0, H, wood);
            foreach (var x in new[] { -hw - 0.5f, hw - 2.5f })
                foreach (var y in new[] { -hd - 0.5f, hd - 2.5f })
                    g.Cuboid(x, x + 3, y, y + 3, 0, H + 0.5f, frame);
            foreach (var z in new[] { 0f, H - 2.5f })
                g.Cuboid(-hw - 0.5f, hw + 0.5f, -hd - 0.5f, hd + 0.5f, z, z + 3, frame);
            foreach (var sy in new[] { -1f, 1f })
            {
                float y = sy * (hd + 0.2f);
                g.Beam(new Vector3(-hw + 2, y, 3), new Vector3(hw - 2, y, H - 3), 1f, 3f, frame);
                g.Beam(new Vector3(-hw + 2, y, H - 3), new Vector3(hw - 2, y, 3), 1f, 3f, frame);
            }
            return g.ToMesh();
        }

        Mesh grenadeMesh;
        public void Grenade(float x, float y, float z)
        {
            if (grenadeMesh == null)
            {
                var g = new MeshBuilder();
                g.Cuboid(-3.2f, 3.2f, -3.2f, 3.2f, 0, 7, Ui.C(52, 64, 40));
                g.Cuboid(-1.2f, 1.2f, -1.2f, 1.2f, 7, 9, Ui.C(70, 70, 76));
                grenadeMesh = g.ToMesh();
            }
            var pos = MapWorld.W2U(x, y, z);
            Graphics.DrawMesh(grenadeMesh, Matrix4x4.TRS(pos, Quaternion.Euler(Time.time * 400f, Time.time * 220f, 0), Vector3.one), worldMat, 0);
            bool blink = ((int)(Time.time * 1000f) / 130) % 2 == 0;
            Glow(x, y, z + 8, 3.4f, new Color(1f, 0.27f, 0.16f), blink ? 0.95f : 0.3f);
        }

        public Material WorldMat { get { return worldMat; } }
    }
}
