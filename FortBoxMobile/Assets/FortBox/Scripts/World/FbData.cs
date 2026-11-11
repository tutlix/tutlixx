// Loaders for the data exported from the PC game (export_fortbox.py):
// maps (meshes + JSON), figures (actors.bytes), guns (guns.bytes).
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace FortBox
{
    public class MeshData
    {
        public string name, kind;
        public int tex = -1;
        public float specK, foliage, translucent, alphaCut;
        public bool fpOnly, sway;
        public Vector3[] pos, nrm;
        public Color32[] col;
        public Vector2[] uv;
        public int[] idx;

        public Mesh ToMesh()
        {
            var m = new Mesh();
            m.name = name;
            if (pos.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = pos;
            m.normals = nrm;
            m.colors32 = col;
            m.uv = uv;
            m.triangles = idx;
            m.RecalculateBounds();
            return m;
        }
    }

    public class BinReader
    {
        readonly BinaryReader r;

        public BinReader(byte[] gz)
        {
            var ms = new MemoryStream();
            using (var z = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress)) z.CopyTo(ms);
            ms.Position = 0;
            r = new BinaryReader(ms);
        }

        public int I() { return r.ReadInt32(); }
        public float F() { return r.ReadSingle(); }
        public byte B() { return r.ReadByte(); }
        public string S() { int n = r.ReadInt32(); return Encoding.UTF8.GetString(r.ReadBytes(n)); }
        public Vector3 V3() { return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }

        public MeshData Mesh()
        {
            var m = new MeshData();
            m.name = S();
            m.kind = S();
            m.tex = I();
            m.specK = F();
            m.foliage = F();
            m.translucent = F();
            m.alphaCut = F();
            m.fpOnly = B() != 0;
            m.sway = B() != 0;
            int nv = I();
            m.pos = new Vector3[nv];
            for (int i = 0; i < nv; i++) m.pos[i] = V3();
            m.nrm = new Vector3[nv];
            for (int i = 0; i < nv; i++) m.nrm[i] = V3();
            m.col = new Color32[nv];
            for (int i = 0; i < nv; i++) { var c = r.ReadBytes(4); m.col[i] = new Color32(c[0], c[1], c[2], c[3]); }
            m.uv = new Vector2[nv];
            for (int i = 0; i < nv; i++) m.uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
            int ni = I();
            m.idx = new int[ni];
            for (int i = 0; i < ni; i++) m.idx[i] = r.ReadInt32();
            return m;
        }
    }

    public class MapPack
    {
        public string id;
        public Dictionary<string, object> meta;
        public List<MeshData> meshes = new List<MeshData>();
        public List<Texture2D> textures = new List<Texture2D>();
        public Texture2D ground;
        public float worldW, worldH, playH;
        // terrain height grid (world px), same bilinear lookup as terrain.py
        public float tx0, ty0, cell = 24f;
        public int tn, tm;
        public float[] th;
        public List<float[]> obstacles = new List<float[]>();

        public static MapPack Load(string id)
        {
            var ta = Resources.Load<TextAsset>("FortBox/maps/" + id + "_mesh");
            var js = Resources.Load<TextAsset>("FortBox/maps/" + id + "_meta");
            if (ta == null) return null;
            var p = new MapPack { id = id };
            var rd = new BinReader(ta.bytes);
            if (rd.S() != "FBMAP1") return null;
            int n = rd.I();
            for (int i = 0; i < n; i++) p.meshes.Add(rd.Mesh());
            p.meta = js != null ? Json.Obj(js.text) : new Dictionary<string, object>();
            p.worldW = (float)p.meta.Num("world_w", 3200);
            p.worldH = (float)p.meta.Num("world_h", 1800);
            p.playH = (float)p.meta.Num("play_h", 1716);
            var texs = p.meta.L("textures");
            if (texs != null) foreach (var t in texs) p.textures.Add(Resources.Load<Texture2D>("FortBox/maps/" + t));
            p.ground = Resources.Load<Texture2D>("FortBox/maps/" + id + "_ground");
            var tr = p.meta.O("terrain");
            if (tr != null)
            {
                p.tx0 = (float)tr.Num("x0"); p.ty0 = (float)tr.Num("y0"); p.cell = (float)tr.Num("cell", 24);
                p.tn = tr.Int("n"); p.tm = tr.Int("m");
                var hl = tr.L("h");
                if (hl != null)
                {
                    p.th = new float[hl.Count];
                    for (int i = 0; i < hl.Count; i++) p.th[i] = hl.F(i);
                }
            }
            var ob = p.meta.L("obstacles");
            if (ob != null)
                foreach (var o in ob)
                {
                    var l = o as List<object>;
                    if (l != null && l.Count >= 4) p.obstacles.Add(new[] { l.F(0), l.F(1), l.F(2), l.F(3) });
                }
            return p;
        }

        /// Ground height at world (x, y) px, in world px (terrain.py height()).
        public float Height(float x, float y)
        {
            if (th == null || tn < 2 || tm < 2) return 0f;
            float fx = (x - tx0) / cell, fy = (y - ty0) / cell;
            int i = Mathf.FloorToInt(fx), j = Mathf.FloorToInt(fy);
            if (i < 0 || j < 0 || i >= tn - 1 || j >= tm - 1)
            {
                i = Mathf.Clamp(i, 0, tn - 2); j = Mathf.Clamp(j, 0, tm - 2);
                fx = Mathf.Clamp(fx, 0, tn - 1); fy = Mathf.Clamp(fy, 0, tm - 1);
            }
            float tx = Mathf.Clamp01(fx - i), ty = Mathf.Clamp01(fy - j);
            float a = th[j * tn + i], b = th[j * tn + i + 1], c = th[(j + 1) * tn + i], d = th[(j + 1) * tn + i + 1];
            float top = a + (b - a) * tx;
            return top + (c + (d - c) * tx - top) * ty;
        }

        public List<object> ObstacleList()
        {
            var l = new List<object>();
            foreach (var o in obstacles) l.Add(new List<object> { (double)o[0], (double)o[1], (double)o[2], (double)o[3] });
            return l;
        }
    }

    public class ActorPart
    {
        public string name, parent;
        public Vector3 pos;
        public List<MeshData> geos = new List<MeshData>();
    }

    public class GunModel
    {
        public string name;
        public bool melee;
        public Vector3 muzzle;
        public List<MeshData> geos = new List<MeshData>();
    }

    /// Figures and guns, loaded once.
    public static class Models
    {
        static Dictionary<string, List<ActorPart>> actors;
        static Dictionary<string, GunModel> guns;
        static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

        public static void Ensure()
        {
            if (actors != null) return;
            actors = new Dictionary<string, List<ActorPart>>();
            guns = new Dictionary<string, GunModel>();
            var a = Resources.Load<TextAsset>("FortBox/models/actors");
            if (a != null)
            {
                var r = new BinReader(a.bytes);
                if (r.S() == "FBACT1")
                {
                    int n = r.I();
                    for (int i = 0; i < n; i++)
                    {
                        string style = r.S();
                        int np = r.I();
                        var parts = new List<ActorPart>();
                        for (int k = 0; k < np; k++)
                        {
                            var ap = new ActorPart { name = r.S(), parent = r.S(), pos = r.V3() };
                            int ng = r.I();
                            for (int q = 0; q < ng; q++) ap.geos.Add(r.Mesh());
                            parts.Add(ap);
                        }
                        actors[style] = parts;
                    }
                }
            }
            var gta = Resources.Load<TextAsset>("FortBox/models/guns");
            if (gta != null)
            {
                var r = new BinReader(gta.bytes);
                if (r.S() == "FBGUN1")
                {
                    int n = r.I();
                    for (int i = 0; i < n; i++)
                    {
                        var gm = new GunModel { name = r.S(), melee = r.B() != 0, muzzle = r.V3() };
                        int ng = r.I();
                        for (int q = 0; q < ng; q++) gm.geos.Add(r.Mesh());
                        guns[gm.name] = gm;
                    }
                }
            }
        }

        public static List<ActorPart> Actor(string style)
        {
            Ensure();
            List<ActorPart> p;
            if (style != null && actors.TryGetValue(style, out p)) return p;
            return actors.TryGetValue("soldier", out p) ? p : null;
        }

        public static GunModel Gun(string name)
        {
            Ensure();
            GunModel g;
            if (name != null && guns.TryGetValue(name, out g)) return g;
            return guns.TryGetValue("Pistol", out g) ? g : null;
        }

        public static Mesh CachedMesh(string key, MeshData d)
        {
            Mesh m;
            if (meshCache.TryGetValue(key, out m) && m != null) return m;
            m = d.ToMesh();
            meshCache[key] = m;
            return m;
        }
    }
}

namespace FortBox
{
    /// First-person gun + hands (sleeve marked magenta for the player colour).
    public class ViewModelData
    {
        public string name, kind;
        public bool melee, energy;
        public float flash;
        public Color accent;
        public UnityEngine.Vector3 muzzle;
        public System.Collections.Generic.List<MeshData> geos = new System.Collections.Generic.List<MeshData>();

        static System.Collections.Generic.Dictionary<string, ViewModelData> all;

        public static ViewModelData Get(string weapon)
        {
            if (all == null)
            {
                all = new System.Collections.Generic.Dictionary<string, ViewModelData>();
                var ta = Resources.Load<TextAsset>("FortBox/models/viewmodels");
                if (ta != null)
                {
                    var r = new BinReader(ta.bytes);
                    if (r.S() == "FBVM1")
                    {
                        int n = r.I();
                        for (int i = 0; i < n; i++)
                        {
                            var v = new ViewModelData { name = r.S(), kind = r.S(), melee = r.B() != 0, energy = r.B() != 0 };
                            v.flash = r.F();
                            v.accent = new Color(r.F(), r.F(), r.F(), 1);
                            v.muzzle = r.V3();
                            int ng = r.I();
                            for (int q = 0; q < ng; q++) v.geos.Add(r.Mesh());
                            all[v.name] = v;
                        }
                    }
                }
            }
            ViewModelData d;
            if (!string.IsNullOrEmpty(weapon) && weapon != "Fists" && all.TryGetValue(weapon, out d)) return d;
            if (all.TryGetValue("_fists", out d)) return d;
            return null;
        }
    }

    public static class Recolor
    {
        /// Mesh copy with the magenta-marked shirt/sleeve vertices tinted to `shirt`
        /// (keeps the shading baked into the marker).
        public static Mesh Shirt(MeshData d, Color32 shirt)
        {
            var m = d.ToMesh();
            var c = new Color32[d.col.Length];
            for (int i = 0; i < c.Length; i++)
            {
                var k = d.col[i];
                if (k.g < 40 && k.r > 40 && System.Math.Abs(k.r - k.b) < 24)
                {
                    float f = k.r / 255f;
                    c[i] = new Color32((byte)(shirt.r * f), (byte)(shirt.g * f), (byte)(shirt.b * f), k.a);
                }
                else c[i] = k;
            }
            m.colors32 = c;
            return m;
        }
    }
}
