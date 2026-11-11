"""FortBox 3D -> Unity data exporter.

Runs the PC game's own builders (gameplay.visuals 2D ground, panda_maps 3D
scene, panda_models figures / guns / vehicles, panda_sky lighting) and writes
everything the Unity mobile client needs into Assets/Resources/FortBox:

  maps/<id>_mesh.bytes   gzip binary: every mesh of the 3D map (static,
                         glow, skyline, vehicles, mountains, grass), already
                         placed on the hills, in Unity axes and units
  maps/<id>_ground.png   the 2D ground the ground shader paints materials on
  maps/<id>_tex<N>.png   texture atlas pages
  maps/<id>_meta.json    size, terrain height grid, ground material, sky /
                         sun / fog / light colours, server obstacle list
  models/actors.bytes    every enemy style + the player (shirt marked so the
                         app can recolour it), split into animated parts
  models/guns.bytes      every weapon model
  textures/*.png         grass / detail / noise textures of the ground shader
  thumbs/<id>.png        first-person map previews (menu map strip + hero)
  data/*.json            weapons, quests, colours, maps catalogue

Unity axes: X = world x, Y = up, Z = -world y. Units: world px / 25.
Run (needs Panda3D + a display):
  xvfb-run -a python3 export_fortbox.py <fortbox folder> <Unity project>
"""
import gzip
import io
import json
import math
import os
import struct
import sys

S = 1.0 / 25.0                        # world px -> Unity units

GAME = os.path.abspath(sys.argv[1])
OUT = os.path.join(os.path.abspath(sys.argv[2]), "Assets", "Resources", "FortBox")
ONLY = sys.argv[3].split(",") if len(sys.argv) > 3 else None
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")
os.chdir(GAME)
sys.path.insert(0, GAME)

import pygame                           # noqa: E402
import FortRoyal2D as g                 # noqa: E402
from panda3d.core import GeomVertexReader, NodePath, Point3, Vec3, PNMImage, Texture   # noqa: E402

g.enter_offline_mode()
pm = g.panda_maps
gv = g.gameplay_visuals
import panda_models as pmod             # noqa: E402
import panda_sky                        # noqa: E402
import maps3d                           # noqa: E402

for sub in ("maps", "models", "textures", "thumbs", "data"):
    os.makedirs(os.path.join(OUT, sub), exist_ok=True)

WORLD_W, WORLD_H = g.BACKEND_MATCH_WORLD_WIDTH, g.BACKEND_MATCH_WORLD_HEIGHT
BOTTOM = g.BOTTOM_BAR_HEIGHT
PLAY_H = WORLD_H - BOTTOM


# ---------------------------------------------------------------------------
# binary writer
# ---------------------------------------------------------------------------
class Out(object):
    def __init__(self):
        self.b = io.BytesIO()

    def i32(self, v):
        self.b.write(struct.pack("<i", int(v)))

    def f32(self, v):
        self.b.write(struct.pack("<f", float(v)))

    def u8(self, v):
        self.b.write(struct.pack("<B", int(v) & 255))

    def s(self, txt):
        raw = str(txt).encode("utf-8")
        self.i32(len(raw))
        self.b.write(raw)

    def floats(self, arr):
        self.b.write(struct.pack("<%df" % len(arr), *arr))

    def ints(self, arr):
        self.b.write(struct.pack("<%di" % len(arr), *arr))

    def bytes_(self, arr):
        self.b.write(bytes(bytearray(arr)))

    def save(self, path):
        with gzip.open(path, "wb", compresslevel=9) as f:
            f.write(self.b.getvalue())


def inherited_input(np_, name, stop, default=0.0):
    """Shader input as the node would see it (nearest ancestor that sets it)."""
    n = np_
    while not n.isEmpty():
        try:
            si = n.getShaderInput(name)
            if si is not None and si.getValueType() != 0:
                return float(si.getVector()[0])
        except Exception:
            pass
        if n == stop:
            break
        n = n.getParent()
    return default


def to_unity(x, y, z):
    """Panda coords (x, -world_y, z) -> Unity (x, z, y) scaled."""
    return (x * S, z * S, y * S)


def geom_arrays(np_, rel, terrain=None, on_terrain=0.0, color_override=None):
    """Read every geom of a GeomNode: positions (net transform to `rel`,
    terrain baked), normals, colours, uvs, triangle indices (Unity winding)."""
    gn = np_.node()
    mat = np_.getMat(rel)
    origin = mat.xformPoint(Point3(0, 0, 0))
    lift_node = 0.0
    if terrain is not None and on_terrain > 1.5:
        lift_node = terrain.height(origin.x, -origin.y)
    P, N, C, U, I = [], [], [], [], []
    for gi in range(gn.getNumGeoms()):
        geom = gn.getGeom(gi).decompose()
        vd = geom.getVertexData()
        base = len(P) // 3
        rv = GeomVertexReader(vd, "vertex")
        has_n = vd.hasColumn("normal")
        has_c = vd.hasColumn("color")
        has_t = vd.hasColumn("texcoord")
        rn = GeomVertexReader(vd, "normal") if has_n else None
        rc = GeomVertexReader(vd, "color") if has_c else None
        rt = GeomVertexReader(vd, "texcoord") if has_t else None
        for _ in range(vd.getNumRows()):
            p = mat.xformPoint(Point3(rv.getData3()))
            z = p.z
            if terrain is not None:
                if on_terrain > 1.5:
                    z += lift_node
                elif on_terrain > 0.5:
                    z += terrain.height(p.x, -p.y)
            P.extend(to_unity(p.x, p.y, z))
            if rn is not None:
                nv = mat.xformVec(Vec3(rn.getData3()))
                if nv.length() > 1e-6:
                    nv.normalize()
                N.extend((nv.x, nv.z, nv.y))
            else:
                N.extend((0.0, 1.0, 0.0))
            if rc is not None:
                c = rc.getData4()
                C.extend((int(max(0, min(1, c[0])) * 255), int(max(0, min(1, c[1])) * 255),
                          int(max(0, min(1, c[2])) * 255), int(max(0, min(1, c[3])) * 255)))
            else:
                C.extend(color_override or (255, 255, 255, 255))
            if rt is not None:
                t = rt.getData2()
                U.extend((t[0], t[1]))
            else:
                U.extend((0.0, 0.0))
        for pi in range(geom.getNumPrimitives()):
            prim = geom.getPrimitive(pi).decompose()
            for k in range(prim.getNumPrimitives()):
                s0, s1 = prim.getPrimitiveStart(k), prim.getPrimitiveEnd(k)
                vs = [prim.getVertex(j) for j in range(s0, s1)]
                if len(vs) == 3:
                    # Panda (right-handed, z up) -> Unity (left-handed, y up): flip winding
                    I.extend((base + vs[0], base + vs[2], base + vs[1]))
    return P, N, C, U, I


def write_mesh(o, name, kind, tex_index, flags, P, N, C, U, I):
    o.s(name)
    o.s(kind)
    o.i32(tex_index)
    o.f32(flags.get("spec_k", 0.0))
    o.f32(flags.get("foliage", 0.0))
    o.f32(flags.get("translucent", 0.0))
    o.f32(flags.get("alpha_cut", 0.0))
    o.u8(1 if flags.get("fp_only") else 0)
    o.u8(1 if flags.get("sway") else 0)
    nv = len(P) // 3
    o.i32(nv)
    o.floats(P)
    o.floats(N)
    o.bytes_(C)
    o.floats(U)
    o.i32(len(I))
    o.ints(I)


def tex_png(tex, path):
    """Panda texture -> PNG (via PNMImage; falls back to raw RAM image)."""
    img = PNMImage()
    if tex.store(img):
        img.write(path)
        return True
    return False


# ---------------------------------------------------------------------------
# lighting per map (same values the PC first-person view uses)
# ---------------------------------------------------------------------------
def sky_params(map_id):
    sky, fog_range = pm.sky_for(map_id)
    st = pm._R._sky_frame((WORLD_W / 2.0, -PLAY_H / 2.0, 40.0), True, fog_range, map_id, sky)
    d = {}
    for k, v in vars(st).items():
        if isinstance(v, (int, float)):
            d[k] = float(v)
        elif isinstance(v, (tuple, list)) and all(isinstance(q, (int, float)) for q in v):
            d[k] = [float(q) for q in v]
    # Panda axes -> Unity axes for the direction vectors
    for k in ("sun", "moon", "light_dir"):
        if k in d and len(d[k]) == 3:
            x, y, z = d[k]
            d[k] = [x, z, y]
    d["fog_range"] = [float(fog_range[0]) * S, float(fog_range[1]) * S] if fog_range else [0.0, 0.0]
    return d


# ---------------------------------------------------------------------------
# maps
# ---------------------------------------------------------------------------
def export_map(map_id):
    print("map", map_id, flush=True)
    g.map_features.ENABLED = False                 # server scenery = the plain map (same as the PC client)
    gv._cached_world = None if hasattr(gv, "_cached_world") else None
    res = gv._render_game_world(WORLD_W, WORLD_H, map_id, BOTTOM)
    ground = res[0]
    meta = res[2] if len(res) > 2 and isinstance(res[2], dict) else {}
    info = meta.get("panda")
    if info is None:
        print("  no 3D info for", map_id)
        return None
    # server obstacles, exactly what the PC lobby leader sends with start_match
    hb = g.get_all_scenery_hitboxes(max(WORLD_W, g.WIDTH), max(WORLD_H, g.HEIGHT), map_id, BOTTOM)
    obstacles = [[r.x, r.y, r.w, r.h] for r in hb][:400]

    scene = pm.build_scene(ground, WORLD_W, PLAY_H, info, meta.get("features"), fp_extras=True)
    root = scene["root"]
    terr = scene.get("terrain")
    # sky / light for this map (needs the scene on the renderer)
    R = pm._R
    hold = (R.scene, R.scene_key)
    R.scene, R.scene_key = scene, None
    try:
        sky = sky_params(map_id)
    finally:
        R.scene, R.scene_key = hold

    # textures
    textures, tex_ids = [], {}
    o = Out()
    meshes = []
    for np_ in root.findAllMatches("**/+GeomNode"):
        name = np_.getName()
        if name.startswith("ground") or name == "layer":
            continue                               # the ground is rebuilt in Unity from the height grid
        tex = np_.findTexture("*")
        ti = -1
        if tex is not None:
            # identical atlas pages are shared: key on the pixels, not the object
            img = PNMImage()
            key = None
            if tex.store(img):
                sio = __import__("hashlib").md5()
                ram = tex.getRamImage()
                sio.update(bytes(memoryview(ram)) if ram else str(tex.getName()).encode())
                key = sio.hexdigest()
            if key is not None and key not in tex_ids:
                fn = "%s_tex%d.png" % (map_id, len(textures))
                img.write(os.path.join(OUT, "maps", fn))
                tex_ids[key] = len(textures)
                textures.append(fn[:-4])
            ti = tex_ids.get(key, -1)
        unlit = inherited_input(np_, "unlit", root)
        on_t = inherited_input(np_, "on_terrain", root, 0.0)
        fp_only = any(np_ == n or np_.isAncestorOf(n) or n.isAncestorOf(np_) for n in scene.get("fp_only") or [])
        kind = "glow" if unlit > 0.5 else "world"
        lname = name.lower()
        if "mountain" in lname:
            kind = "mountains"
        elif lname == "grass":
            kind = "grass"
        flags = dict(spec_k=inherited_input(np_, "spec_k", root), foliage=inherited_input(np_, "foliage", root),
                     translucent=inherited_input(np_, "translucent", root), alpha_cut=inherited_input(np_, "alpha_cut", root),
                     fp_only=fp_only, sway=inherited_input(np_, "sway", root) > 0)
        P, N, C, U, I = geom_arrays(np_, root, terr, on_t)
        if not I:
            continue
        meshes.append((name, kind, ti, flags, P, N, C, U, I))
    o.s("FBMAP1")
    o.i32(len(meshes))
    for m in meshes:
        write_mesh(o, *m)
    o.save(os.path.join(OUT, "maps", map_id + "_mesh.bytes"))

    # ground texture (only the play area: the shader paints the outside colour beyond it)
    pygame.image.save(ground.subsurface(pygame.Rect(0, 0, WORLD_W, PLAY_H)), os.path.join(OUT, "maps", map_id + "_ground.png"))
    mat = maps3d.ground_mat(map_id) if maps3d.is_builtin(map_id) else "grass"
    gnode = root.find("**/ground")
    gin = {}
    for k in ("outside_col", "outside_grass", "grass_ref", "grass_tint", "base_col", "base_mat", "wet", "neon", "neon_col"):
        try:
            si = gnode.getShaderInput(k)
            v = si.getVector()
            gin[k] = [float(v[0]), float(v[1]), float(v[2])] if k in ("outside_col", "grass_tint", "base_col", "neon_col") else float(v[0])
        except Exception:
            pass
    bounce = inherited_input(root.find("**/scenery") if not root.find("**/scenery").isEmpty() else root, "ground_bounce", None, 0.25)
    terrain = None
    if terr is not None and not terr.flat:
        terrain = dict(x0=terr.x0, y0=terr.y0, cell=float(pm.terrain.CELL) if hasattr(pm, "terrain") else 40.0,
                       n=terr.n, m=terr.m, h=[round(float(v), 2) for v in terr.h])
    meta_out = dict(id=map_id, world_w=WORLD_W, world_h=WORLD_H, play_h=PLAY_H, scale=S,
                    textures=textures, ground=dict(mat=mat, bounce=bounce, **gin), sky=sky,
                    terrain=terrain, obstacles=obstacles, eye=pm.FP_EYE, hfov=pm.FP_HFOV_BASE)
    with open(os.path.join(OUT, "maps", map_id + "_meta.json"), "w") as f:
        json.dump(meta_out, f, separators=(",", ":"))

    # first-person preview for the menu
    try:
        th = pm.thumbnail_fp(res, WORLD_W, PLAY_H, 640, 360)
        pygame.image.save(th, os.path.join(OUT, "thumbs", map_id + ".png"))
    except Exception as exc:
        print("  thumb failed:", exc)
    root.removeNode()
    return dict(verts=sum(len(m[4]) // 3 for m in meshes), meshes=len(meshes), obstacles=len(obstacles))


# ---------------------------------------------------------------------------
# figures and guns
# ---------------------------------------------------------------------------
PLAYER_SHIRT_KEY = (255, 0, 255)       # marked colour: the app swaps in the player's colour

ACTOR_PARTS = ("body", "head", "legL", "legR", "armL", "armR", "gun")


def export_actor_parts(o, style, shirt=None, headless=False):
    """One figure: each animated part with its pivot (relative to its parent)
    and its geometry in part-local coordinates."""
    np_ = pmod._build_actor(style, shirt, None, (200, 200, 210), headless=headless)
    parts = []
    for pname in ACTOR_PARTS:
        node = np_.find("**/" + pname)
        if node.isEmpty():
            continue
        parent = node.getParent()
        pname_parent = parent.getName() if parent.getName() in ACTOR_PARTS else ""
        pos = node.getPos(parent)
        geos = []
        for child in node.getChildren():
            if child.getName() in ACTOR_PARTS:
                continue
            for gnp in ([child] if child.node().isGeomNode() else []) + list(child.findAllMatches("**/+GeomNode")):
                unlit = inherited_input(gnp, "unlit", np_)
                P, N, C, U, I = geom_arrays(gnp, node)
                if I:
                    geos.append(("glow" if unlit > 0.5 else "world", P, N, C, U, I))
        parts.append((pname, pname_parent, to_unity(pos.x, pos.y, pos.z), geos))
    o.s(style)
    o.i32(len(parts))
    for pname, parent, pos, geos in parts:
        o.s(pname)
        o.s(parent)
        o.floats(pos)
        o.i32(len(geos))
        for kind, P, N, C, U, I in geos:
            write_mesh(o, pname, kind, -1, {}, P, N, C, U, I)
    np_.removeNode()


def export_actors():
    import character_renderer as cr
    styles = sorted(cr.STYLES.keys())
    o = Out()
    o.s("FBACT1")
    o.i32(len(styles) + 1)
    for st in styles:
        export_actor_parts(o, st, PLAYER_SHIRT_KEY if st == "player" else None)
    export_actor_parts_named(o, "player_fp", "player")
    o.save(os.path.join(OUT, "models", "actors.bytes"))
    return styles


def export_actor_parts_named(o, name, style):
    """Your own body in first person (headless; arms used for the view model)."""
    buf = Out()
    export_actor_parts(buf, style, PLAYER_SHIRT_KEY, headless=True)
    raw = buf.b.getvalue()
    # replace the leading style string with `name`
    n = struct.unpack("<i", raw[:4])[0]
    o.s(name)
    o.b.write(raw[4 + n:])


def export_guns():
    names = [w for w in g.WEAPONS.keys()]
    o = Out()
    o.s("FBGUN1")
    out = []
    for w in names:
        try:
            prof = g._fp_gun_profile(w)
            gg, ggl = pmod.Geo("gun"), pmod.Geo("glow")
            info = pmod.build_gun(gg, ggl, prof, g.WEAPON_ACCENT_COLORS.get(w, (200, 200, 210)))
            holder = NodePath("gun")
            gg.node().reparentTo(holder)
            gl = ggl.node()
            gl.reparentTo(holder)
            geos = []
            for gnp in holder.findAllMatches("**/+GeomNode"):
                P, N, C, U, I = geom_arrays(gnp, holder)
                if I:
                    geos.append(("glow" if gnp == gl else "world", P, N, C, U, I))
            out.append((w, bool(info.get("melee")), info, geos))
            holder.removeNode()
        except Exception as exc:
            print("  gun failed", w, exc)
    o.i32(len(out))
    for w, melee, info, geos in out:
        o.s(w)
        o.u8(1 if melee else 0)
        muzzle = info.get("muzzle") or info.get("tip") or (0.0, 30.0, 0.0)
        try:
            o.floats(to_unity(*muzzle))
        except Exception:
            o.floats(to_unity(0.0, 30.0, 0.0))
        o.i32(len(geos))
        for kind, P, N, C, U, I in geos:
            write_mesh(o, w, kind, -1, {}, P, N, C, U, I)
    o.save(os.path.join(OUT, "models", "guns.bytes"))
    return [w for w, *_ in out]


def export_viewmodels():
    """First-person view models: each gun with the hands and sleeves holding
    it (sleeve marked PLAYER_SHIRT_KEY), in view-model space (x right,
    y forward, z up -> Unity), plus the pose data the app needs."""
    o = Out()
    o.s("FBVM1")
    items = [None] + [w for w in g.WEAPONS.keys()]
    rows = []
    for w in items:
        try:
            if w is None:
                prof = dict(kind="melee", style="fists")
                accent = (200, 200, 210)
            else:
                prof = dict(g._fp_gun_profile(w))
                accent = g.WEAPON_ACCENT_COLORS.get(w, (200, 200, 210))
            gg, ggl = pmod.Geo("vm-gun"), pmod.Geo("vm-glow")
            info = pmod.build_gun(gg, ggl, prof, accent)
            pmod._hands(gg, info, PLAYER_SHIRT_KEY)
            holder = NodePath("vm")
            gg.node().reparentTo(holder)
            gl = ggl.node()
            gl.reparentTo(holder)
            geos = []
            for gnp in holder.findAllMatches("**/+GeomNode"):
                P, N, C, U, I = geom_arrays(gnp, holder)
                if I:
                    geos.append(("glow" if gnp == gl else "world", P, N, C, U, I))
            kind = "fists" if info.get("fists") else info.get("kind", "pistol")
            energy = bool(prof.get("energy"))
            rows.append((w or "_fists", kind, bool(info.get("melee")), float(info.get("flash") or 0.0),
                         info.get("muzzle") or (0.0, 30.0, 0.0), energy, accent, geos))
            holder.removeNode()
        except Exception as exc:
            print("  viewmodel failed", w, exc)
    o.i32(len(rows))
    for name, kind, melee, flash, muzzle, energy, accent, geos in rows:
        o.s(name)
        o.s(kind)
        o.u8(1 if melee else 0)
        o.u8(1 if energy else 0)
        o.floats((flash, accent[0] / 255.0, accent[1] / 255.0, accent[2] / 255.0))
        o.floats(to_unity(*muzzle))
        o.i32(len(geos))
        for k, P, N, C, U, I in geos:
            write_mesh(o, name, k, -1, {}, P, N, C, U, I)
    o.save(os.path.join(OUT, "models", "viewmodels.bytes"))
    return len(rows)


def export_textures():
    for name, fn in (("grass", panda_sky.grass_texture), ("detail", panda_sky.detail_texture), ("noise", panda_sky.noise_texture)):
        tex = fn()
        tex_png(tex, os.path.join(OUT, "textures", name + ".png"))


def export_data(maps_done, guns):
    weapons = {}
    for w, d in g.WEAPONS.items():
        weapons[w] = {k: v for k, v in d.items() if isinstance(v, (int, float, str, bool))}
        weapons[w]["accent"] = list(g.WEAPON_ACCENT_COLORS.get(w, (200, 200, 210)))[:3]
    with open(os.path.join(OUT, "data", "weapons.json"), "w") as f:
        json.dump(weapons, f)
    with open(os.path.join(OUT, "data", "quests.json"), "w") as f:
        json.dump(dict(pool=g.QUEST_POOL, daily=g.DAILY_QUEST_COUNT), f)
    with open(os.path.join(OUT, "data", "colors.json"), "w") as f:
        json.dump({k: list(v)[:3] for k, v in g.color_map.items()}, f)
    cat = []
    for (label, mid, base, hover, desc) in g.MAP_CATALOG:
        if mid in maps_done:
            cat.append(dict(id=mid, label=label, desc=desc, base=list(base)[:3]))
    with open(os.path.join(OUT, "data", "maps.json"), "w") as f:
        json.dump(dict(maps=cat, pvp=("pvp" in maps_done)), f)
    enemy = dict(weapons=dict(g.ENEMY_HUMAN_WEAPON), gun_scale=pmod.ACTOR_GUN_SCALE, hip=pmod.HIP)
    with open(os.path.join(OUT, "data", "enemies.json"), "w") as f:
        json.dump(enemy, f)


def main():
    if ONLY == ["vm"]:
        print("viewmodels", export_viewmodels())
        return
    stats = {}
    map_ids = [m[1] for m in g.MAP_CATALOG]
    if "pvp" in getattr(maps3d, "BY_ID", {}) and "pvp" not in map_ids:
        map_ids.append("pvp")
    if ONLY:
        map_ids = [m for m in map_ids if m in ONLY]
    done = []
    for mid in map_ids:
        try:
            r = export_map(mid)
            if r:
                stats[mid] = r
                done.append(mid)
        except Exception as exc:
            import traceback
            traceback.print_exc()
            print("  FAILED", mid, exc)
    export_textures()
    styles = export_actors()
    guns = export_guns()
    export_viewmodels()
    export_data(done, guns)
    print(json.dumps(dict(maps=stats, styles=styles, guns=len(guns)), indent=1))


main()
