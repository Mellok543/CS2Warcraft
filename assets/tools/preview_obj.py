"""Render a textured OBJ to a PNG contact sheet (front + side view).

Dependency-free (stdlib only) so model sources can be checked without a DCC tool.
Usage: python preview_obj.py model.obj texture.png out.png [--up y|z]
"""
import math
import struct
import sys
import zlib

from pngutil import read_png


def write_png(path, width, height, pixels):
    raw = b"".join(b"\x00" + bytes(pixels[y * width * 3:(y + 1) * width * 3]) for y in range(height))

    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 6)))
        f.write(chunk(b"IEND", b""))


def read_obj(path):
    vertices, uvs, faces = [], [], []
    for line in open(path, encoding="utf-8", errors="ignore"):
        parts = line.split()
        if not parts:
            continue
        if parts[0] == "v":
            vertices.append(tuple(map(float, parts[1:4])))
        elif parts[0] == "vt":
            uvs.append(tuple(map(float, parts[1:3])))
        elif parts[0] == "f":
            corners = []
            for token in parts[1:]:
                pieces = token.split("/")
                vi = int(pieces[0])
                ti = int(pieces[1]) if len(pieces) > 1 and pieces[1] else 0
                corners.append((vi - 1 if vi > 0 else len(vertices) + vi,
                                ti - 1 if ti > 0 else (len(uvs) + ti if ti < 0 else -1)))
            for i in range(1, len(corners) - 1):
                faces.append((corners[0], corners[i], corners[i + 1]))
    return vertices, uvs, faces


def render(vertices, uvs, faces, texture, axis_right, axis_up, axis_depth, size):
    tex_w, tex_h, channels, rows = texture
    xs = [v[axis_right] for v in vertices]
    ys = [v[axis_up] for v in vertices]
    span = max(max(xs) - min(xs), max(ys) - min(ys)) or 1
    scale = (size - 20) / span
    cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    pixels = bytearray([40, 40, 48] * size * size)
    depth = [-1e30] * (size * size)
    light = (0.4, 0.8, 0.45)

    def project(v):
        return (size / 2 + (v[axis_right] - cx) * scale,
                size / 2 - (v[axis_up] - cy) * scale,
                v[axis_depth])

    for tri in faces:
        a, b, c = (vertices[i] for i, _ in tri)
        ux, uy, uz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
        vx, vy, vz = c[0] - a[0], c[1] - a[1], c[2] - a[2]
        n = (uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx)
        length = math.sqrt(n[0] ** 2 + n[1] ** 2 + n[2] ** 2) or 1
        shade = 0.35 + 0.65 * abs(sum(n[i] / length * light[i] for i in range(3)))

        color = (180, 180, 180)
        uv_indices = [t for _, t in tri]
        if all(t >= 0 for t in uv_indices):
            u = sum(uvs[t][0] for t in uv_indices) / 3
            v = sum(uvs[t][1] for t in uv_indices) / 3
            px = min(tex_w - 1, max(0, int(u * tex_w)))
            py = min(tex_h - 1, max(0, int((1 - v) * tex_h)))
            row = rows[py]
            color = tuple(row[px * channels + k] for k in range(3))

        p = [project(vertices[i]) for i, _ in tri]
        min_x, max_x = max(0, int(min(q[0] for q in p))), min(size - 1, int(max(q[0] for q in p)) + 1)
        min_y, max_y = max(0, int(min(q[1] for q in p))), min(size - 1, int(max(q[1] for q in p)) + 1)
        denominator = (p[1][1] - p[2][1]) * (p[0][0] - p[2][0]) + (p[2][0] - p[1][0]) * (p[0][1] - p[2][1])
        if abs(denominator) < 1e-9:
            continue
        for y in range(min_y, max_y + 1):
            for x in range(min_x, max_x + 1):
                w0 = ((p[1][1] - p[2][1]) * (x - p[2][0]) + (p[2][0] - p[1][0]) * (y - p[2][1])) / denominator
                w1 = ((p[2][1] - p[0][1]) * (x - p[2][0]) + (p[0][0] - p[2][0]) * (y - p[2][1])) / denominator
                w2 = 1 - w0 - w1
                if w0 < 0 or w1 < 0 or w2 < 0:
                    continue
                z = w0 * p[0][2] + w1 * p[1][2] + w2 * p[2][2]
                index = y * size + x
                if z <= depth[index]:
                    continue
                depth[index] = z
                pixels[index * 3:index * 3 + 3] = bytes(min(255, int(ch * shade)) for ch in color)
    return pixels


def main():
    obj, texture_path, out = sys.argv[1:4]
    up = sys.argv[5] if len(sys.argv) > 5 and sys.argv[4] == "--up" else "y"
    vertices, uvs, faces = read_obj(obj)
    texture = read_png(texture_path)
    size = 360
    up_axis = 1 if up == "y" else 2
    depth_axis = 2 if up == "y" else 1
    front = render(vertices, uvs, faces, texture, 0, up_axis, depth_axis, size)
    side = render(vertices, uvs, faces, texture, depth_axis, up_axis, 0, size)
    sheet = bytearray()
    for y in range(size):
        sheet += front[y * size * 3:(y + 1) * size * 3] + side[y * size * 3:(y + 1) * size * 3]
    write_png(out, size * 2, size, sheet)

    lo = [min(v[i] for v in vertices) for i in range(3)]
    hi = [max(v[i] for v in vertices) for i in range(3)]
    print(f"{obj}: {len(vertices)} verts, {len(faces)} tris, bounds {lo} .. {hi}")


if __name__ == "__main__":
    main()
