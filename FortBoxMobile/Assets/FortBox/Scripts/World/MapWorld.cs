// Builds a FortBox 3D map in Unity from the data exported from the PC game:
// the same meshes, ground materials, hills, sky, sun, fog and shadows.
using System.Collections.Generic;
using UnityEngine;

namespace FortBox
{
    public class MapWorld : MonoBehaviour
    {
        public const float S = 1f / 25f;               // world px -> Unity units
        public static MapWorld Current;

        public MapPack pack;
        public Light sun;
        Transform sky;
        readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        public static Vector3 W2U(float x, float y, float zPx) { return new Vector3(x * S, zPx * S, -y * S); }
        public static Vector2 U2W(Vector3 u) { return new Vector2(u.x / S, -u.z / S); }

        public float HeightPx(float x, float y) { return pack != null ? pack.Height(x, y) : 0f; }

        public static Shader Sh(string name)
        {
            var s = Resources.Load<Shader>("FortBox/shaders/" + name);
            if (s == null) s = Shader.Find("FortBox/" + name.Replace("FortBox", ""));
            return s;
        }

        static Texture2D tex(string name) { return Resources.Load<Texture2D>("FortBox/textures/" + name); }

        public static MapWorld Build(string id)
        {
            if (Current != null) Destroy(Current.gameObject);
            var pack = MapPack.Load(id);
            if (pack == null) { Debug.LogError("Map data missing: " + id); return null; }
            var go = new GameObject("Map " + id);
            var w = go.AddComponent<MapWorld>();
            w.pack = pack;
            Current = w;
            w.ApplyLighting();
            w.BuildMeshes();
            w.BuildGround();
            w.BuildSky();
            return w;
        }

        void OnDestroy() { if (Current == this) Current = null; }

        /// Neutral daylight for menu previews (shop gun) when no map is loaded.
        public static void DefaultLighting()
        {
            if (Current != null) return;
            var sun = new Vector3(0.45f, 0.75f, -0.5f).normalized;
            Shader.SetGlobalVector("_FbSunDir", sun);
            Shader.SetGlobalVector("_FbLightDir", sun);
            Shader.SetGlobalVector("_FbLightCol", new Vector3(2.1f, 2.0f, 1.9f));
            Shader.SetGlobalVector("_FbAmbSky", new Vector3(0.42f, 0.46f, 0.55f));
            Shader.SetGlobalVector("_FbAmbGround", new Vector3(0.12f, 0.11f, 0.1f));
            Shader.SetGlobalVector("_FbFogCol", new Vector3(0.2f, 0.22f, 0.26f));
            Shader.SetGlobalVector("_FbGlowCol", new Vector3(0.5f, 0.47f, 0.44f));
            Shader.SetGlobalVector("_FbFogRange", new Vector4(400f, 800f, 0, 0));
            Shader.SetGlobalFloat("_FbExposure", 0.52f);
            Shader.SetGlobalFloat("_FbGroundBounce", 0.25f);
        }

        // ---- lighting / fog: the PC sky state for this map -----------------
        static Vector3 V3(List<object> l, Vector3 def)
        {
            if (l == null || l.Count < 3) return def;
            return new Vector3(l.F(0), l.F(1), l.F(2));
        }

        void ApplyLighting()
        {
            var sk = pack.meta.O("sky") ?? new Dictionary<string, object>();
            Vector3 sunDir = V3(sk.L("sun"), new Vector3(0, 0.8f, -0.6f)).normalized;
            Vector3 lightDir = V3(sk.L("light_dir"), sunDir).normalized;
            Shader.SetGlobalVector("_FbSunDir", sunDir);
            Shader.SetGlobalVector("_FbLightDir", lightDir);
            Shader.SetGlobalVector("_FbLightCol", V3(sk.L("light_col"), new Vector3(2.1f, 2f, 1.9f)));
            Shader.SetGlobalVector("_FbAmbSky", V3(sk.L("amb_sky"), new Vector3(0.29f, 0.4f, 0.65f)));
            Shader.SetGlobalVector("_FbAmbGround", V3(sk.L("amb_ground"), new Vector3(0.07f, 0.08f, 0.05f)));
            Shader.SetGlobalVector("_FbFogCol", V3(sk.L("fog"), new Vector3(0.23f, 0.3f, 0.42f)));
            Shader.SetGlobalVector("_FbGlowCol", V3(sk.L("glow"), new Vector3(0.5f, 0.47f, 0.44f)));
            var fr = sk.L("fog_range");
            Shader.SetGlobalVector("_FbFogRange", new Vector4(fr.F(0, 40), fr.F(1, 96), 0, 0));
            Shader.SetGlobalFloat("_FbExposure", 0.52f);
            var gr = pack.meta.O("ground");
            Shader.SetGlobalFloat("_FbGroundBounce", gr != null ? (float)gr.Num("bounce", 0.25) : 0.25f);

            var lgo = new GameObject("Sun");
            lgo.transform.SetParent(transform, false);
            sun = lgo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1f;
            sun.shadows = Settings.Shadows ? LightShadows.Soft : LightShadows.None;
            sun.shadowStrength = 1f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
            lgo.transform.rotation = Quaternion.LookRotation(-lightDir);
            QualitySettings.shadows = Settings.Shadows ? ShadowQuality.All : ShadowQuality.Disable;
            QualitySettings.shadowDistance = 70f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.shadowResolution = Settings.Quality >= 2 ? ShadowResolution.High : ShadowResolution.Medium;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;
        }

        // ---- meshes ---------------------------------------------------------
        Material MatFor(MeshData d)
        {
            bool glow = d.kind == "glow";
            string key = (glow ? "g" : "w") + d.tex + "|" + d.specK + "|" + d.foliage + "|" + d.translucent + "|" + d.alphaCut + "|" + (d.sway ? 1 : 0);
            Material m;
            if (mats.TryGetValue(key, out m)) return m;
            m = new Material(Sh(glow ? "FortBoxGlow" : "FortBoxWorld"));
            if (d.tex >= 0 && d.tex < pack.textures.Count && pack.textures[d.tex] != null)
            {
                var t = pack.textures[d.tex];
                t.wrapMode = TextureWrapMode.Clamp;
                m.SetTexture("_MainTex", t);
            }
            if (!glow)
            {
                m.SetFloat("_SpecK", d.specK);
                m.SetFloat("_Foliage", d.foliage);
                m.SetFloat("_Translucent", d.translucent);
                m.SetFloat("_AlphaCut", d.alphaCut);
                m.SetFloat("_Sway", d.sway ? 1f : 0f);
            }
            mats[key] = m;
            return m;
        }

        void BuildMeshes()
        {
            // merge everything that shares a material (fewer draw calls on phones)
            var groups = new Dictionary<Material, List<MeshData>>();
            var noShadow = new HashSet<Material>();
            foreach (var d in pack.meshes)
            {
                if (d.kind == "grass" && Settings.Quality == 0) continue;     // low quality: no 3D grass tufts
                var m = MatFor(d);
                List<MeshData> l;
                if (!groups.TryGetValue(m, out l)) groups[m] = l = new List<MeshData>();
                l.Add(d);
                if (d.kind == "glow" || d.kind == "grass" || d.kind == "mountains" || d.fpOnly) noShadow.Add(m);
            }
            int gi = 0;
            foreach (var kv in groups)
            {
                foreach (var merged in Merge(kv.Value))
                {
                    var go = new GameObject("mesh" + (gi++));
                    go.transform.SetParent(transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = merged;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = kv.Key;
                    r.shadowCastingMode = noShadow.Contains(kv.Key) ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
            }
        }

        static IEnumerable<Mesh> Merge(List<MeshData> list)
        {
            const int MAX = 60000;
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var col = new List<Color32>();
            var uv = new List<Vector2>(); var idx = new List<int>();
            foreach (var d in list)
            {
                if (pos.Count > 0 && pos.Count + d.pos.Length > MAX)
                {
                    yield return Make(pos, nrm, col, uv, idx);
                    pos = new List<Vector3>(); nrm = new List<Vector3>(); col = new List<Color32>(); uv = new List<Vector2>(); idx = new List<int>();
                }
                int b = pos.Count;
                pos.AddRange(d.pos); nrm.AddRange(d.nrm); col.AddRange(d.col); uv.AddRange(d.uv);
                foreach (var i in d.idx) idx.Add(b + i);
            }
            if (pos.Count > 0) yield return Make(pos, nrm, col, uv, idx);
        }

        static Mesh Make(List<Vector3> p, List<Vector3> n, List<Color32> c, List<Vector2> u, List<int> i)
        {
            var m = new Mesh();
            if (p.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(p);
            m.SetNormals(n);
            m.SetColors(c);
            m.SetUVs(0, u);
            m.SetTriangles(i, 0);
            m.RecalculateBounds();
            return m;
        }

        // ---- ground with hills ----------------------------------------------
        void BuildGround()
        {
            var gr = pack.meta.O("ground") ?? new Dictionary<string, object>();
            var mat = new Material(Sh("FortBoxGround"));
            if (pack.ground != null) { pack.ground.wrapMode = TextureWrapMode.Clamp; mat.SetTexture("_GroundTex", pack.ground); }
            var grass = tex("grass"); var detail = tex("detail"); var noise = tex("noise");
            foreach (var t in new[] { grass, detail, noise }) if (t != null) t.wrapMode = TextureWrapMode.Repeat;
            mat.SetTexture("_GrassTex", grass);
            mat.SetTexture("_DetailTex", detail);
            mat.SetTexture("_NoiseTex", noise);
            mat.SetVector("_MapSize", new Vector4(pack.worldW, pack.playH, 0, 0));
            mat.SetColor("_OutsideCol", Col(gr.L("outside_col"), new Color(0.3f, 0.55f, 0.24f)));
            mat.SetFloat("_OutsideGrass", (float)gr.Num("outside_grass", 1));
            mat.SetFloat("_GrassRef", (float)gr.Num("grass_ref", 0.5));
            mat.SetColor("_GrassTint", Col(gr.L("grass_tint"), Color.white));
            mat.SetColor("_BaseCol", Col(gr.L("base_col"), new Color(0.5f, 0.5f, 0.5f)));
            mat.SetFloat("_BaseMat", (float)gr.Num("base_mat", 0));
            mat.SetFloat("_Wet", (float)gr.Num("wet", 0));
            mat.SetFloat("_Neon", (float)gr.Num("neon", 0));
            mat.SetColor("_NeonCol", Col(gr.L("neon_col"), new Color(1, 0.35f, 0.85f)));
            mat.SetFloat("_Detail", Settings.Quality == 0 ? 0.5f : 1f);

            // hills grid over the terrain extent (or a plain grid when flat), plus a far skirt
            float x0, y0, x1, y1, step;
            if (pack.th != null)
            {
                x0 = pack.tx0; y0 = pack.ty0;
                x1 = pack.tx0 + (pack.tn - 1) * pack.cell; y1 = pack.ty0 + (pack.tm - 1) * pack.cell;
                step = pack.cell * 2f;
            }
            else
            {
                x0 = -960; y0 = -960; x1 = pack.worldW + 960; y1 = pack.playH + 960; step = 120f;
            }
            int nx = Mathf.Max(2, Mathf.CeilToInt((x1 - x0) / step) + 1);
            int ny = Mathf.Max(2, Mathf.CeilToInt((y1 - y0) / step) + 1);
            var pos = new Vector3[nx * ny];
            var nrm = new Vector3[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = Mathf.Min(x1, x0 + i * step), y = Mathf.Min(y1, y0 + j * step);
                    float h = pack.Height(x, y);
                    pos[j * nx + i] = W2U(x, y, h);
                    float hx = pack.Height(x + 24, y) - pack.Height(x - 24, y);
                    float hy = pack.Height(x, y + 24) - pack.Height(x, y - 24);
                    // PC gnrm (-hx, -hy_panda, 1) with hy_panda = -hy -> Unity (x, up, z)
                    nrm[j * nx + i] = new Vector3(-hx / 48f, 1f, hy / 48f).normalized;
                }
            var tris = new List<int>((nx - 1) * (ny - 1) * 6);
            for (int j = 0; j < ny - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    // Unity Z = -world y: these wind clockwise seen from above (front faces up)
                    tris.Add(a); tris.Add(b); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                }
            var m = new Mesh();
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = pos;
            m.normals = nrm;
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            AddRenderer("ground", m, mat, false);

            // far skirt to the horizon
            float big = 12000f;
            var far = new Mesh();
            far.vertices = new[] { W2U(-big, -big, -0.6f), W2U(pack.worldW + big, -big, -0.6f), W2U(pack.worldW + big, pack.playH + big, -0.6f), W2U(-big, pack.playH + big, -0.6f) };
            far.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            far.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            far.RecalculateBounds();
            AddRenderer("ground-far", far, mat, false);
        }

        static Color Col(List<object> l, Color def)
        {
            if (l == null || l.Count < 3) return def;
            return new Color(l.F(0), l.F(1), l.F(2), 1);
        }

        void AddRenderer(string name, Mesh m, Material mat, bool cast)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
        }

        // ---- sky --------------------------------------------------------------
        void BuildSky()
        {
            var sk = pack.meta.O("sky") ?? new Dictionary<string, object>();
            var mat = new Material(Sh("FortBoxSky"));
            var noise = tex("noise");
            if (noise != null) noise.wrapMode = TextureWrapMode.Repeat;
            mat.SetTexture("_NoiseTex", noise);
            mat.SetColor("_Zenith", Col(sk.L("zenith"), new Color(0.05f, 0.16f, 0.45f)));
            mat.SetColor("_Horizon", Col(sk.L("horizon"), new Color(0.23f, 0.3f, 0.42f)));
            mat.SetColor("_SunCol", Col(sk.L("sun_col"), new Color(1, 0.95f, 0.88f)));
            mat.SetFloat("_SunInt", (float)sk.Num("sun_int", 2.15));
            mat.SetFloat("_Golden", (float)sk.Num("golden", 0));
            mat.SetFloat("_Cover", (float)sk.Num("cover", 0.4));
            var go = new GameObject("sky");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = SphereMesh(48, 24);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.transform.localScale = Vector3.one * 380f;
            sky = go.transform;
        }

        static Mesh SphereMesh(int n, int m)
        {
            var v = new List<Vector3>();
            for (int j = 0; j <= m; j++)
            {
                float ph = -Mathf.PI / 2 + Mathf.PI * j / m;
                for (int i = 0; i <= n; i++)
                {
                    float a = 2 * Mathf.PI * i / n;
                    v.Add(new Vector3(Mathf.Cos(ph) * Mathf.Cos(a), Mathf.Sin(ph), Mathf.Cos(ph) * Mathf.Sin(a)));
                }
            }
            var t = new List<int>();
            for (int j = 0; j < m; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    t.Add(a); t.Add(d); t.Add(b);
                    t.Add(a); t.Add(c); t.Add(d);
                }
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (sky != null && cam != null) sky.position = cam.transform.position;
            if (sun != null && cam != null)
            {
                // keep the shadow area centred on the player
                var p = cam.transform.position;
                sun.transform.position = new Vector3(p.x, 0, p.z);
            }
        }
    }
}
