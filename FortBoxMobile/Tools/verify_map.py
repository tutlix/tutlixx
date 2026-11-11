"""Re-read an exported map and draw it top-down (Unity X/Z) to check it."""
import gzip, struct, sys, json, pygame
path, out = sys.argv[1], sys.argv[2]
d = gzip.open(path).read(); o = 0
def i32():
    global o; v = struct.unpack_from("<i", d, o)[0]; o += 4; return v
def f32():
    global o; v = struct.unpack_from("<f", d, o)[0]; o += 4; return v
def s():
    n = i32(); global o; v = d[o:o+n].decode(); o += n; return v
def arr(fmt, n):
    global o; v = struct.unpack_from("<%d%s" % (n, fmt), d, o); o += n * struct.calcsize(fmt); return v
assert s() == "FBMAP1"
n = i32(); tris = []
for k in range(n):
    name, kind, ti = s(), s(), i32(); f32(); f32(); f32(); f32(); fp = d[o]; o += 2
    nv = i32(); P = arr("f", nv*3); N = arr("f", nv*3); C = arr("B", nv*4); U = arr("f", nv*2); ni = i32(); I = arr("i", ni)
    if fp: continue
    for t in range(0, ni, 3):
        a, b, c = I[t], I[t+1], I[t+2]
        ys = (P[a*3+1] + P[b*3+1] + P[c*3+1]) / 3
        tris.append((ys, [(P[a*3], P[a*3+2]), (P[b*3], P[b*3+2]), (P[c*3], P[c*3+2])], C[a*4:a*4+3]))
tris.sort()
W, H = 1280, 720; sx = W / 128.0
img = pygame.Surface((W, H)); img.fill((60, 70, 60))
for ys, pts, col in tris:
    q = [(x * sx, -z * sx) for x, z in pts]
    if len(set(q)) > 2: pygame.draw.polygon(img, col, q)
pygame.image.save(img, out); print("tris", len(tris))
