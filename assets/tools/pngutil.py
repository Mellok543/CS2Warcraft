"""Minimal PNG writer (stdlib only) for generated textures."""
import struct
import zlib


def read_png(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "not a PNG"
    pos, idat, width, height, color_type, bit_depth = 8, b"", 0, 0, 0, 0
    while pos < len(data):
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        kind = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        if kind == b"IHDR":
            width, height, bit_depth, color_type = struct.unpack(">IIBB", body[:10])
        elif kind == b"IDAT":
            idat += body
        pos += 12 + length
    assert bit_depth == 8 and color_type in (2, 6), f"unsupported PNG {bit_depth}/{color_type}"
    channels = 3 if color_type == 2 else 4
    raw = zlib.decompress(idat)
    stride = width * channels
    rows, previous = [], bytearray(stride)
    offset = 0
    for _ in range(height):
        filter_type = raw[offset]
        line = bytearray(raw[offset + 1:offset + 1 + stride])
        offset += 1 + stride
        for i in range(stride):
            left = line[i - channels] if i >= channels else 0
            up = previous[i]
            upper_left = previous[i - channels] if i >= channels else 0
            if filter_type == 1:
                line[i] = (line[i] + left) & 255
            elif filter_type == 2:
                line[i] = (line[i] + up) & 255
            elif filter_type == 3:
                line[i] = (line[i] + (left + up) // 2) & 255
            elif filter_type == 4:
                p = left + up - upper_left
                pa, pb, pc = abs(p - left), abs(p - up), abs(p - upper_left)
                predictor = left if pa <= pb and pa <= pc else up if pb <= pc else upper_left
                line[i] = (line[i] + predictor) & 255
        rows.append(line)
        previous = line
    return width, height, channels, rows


def write_png(path, width, height, pixels, channels=4):
    """pixels: bytes/bytearray of width*height*channels (RGBA or RGB), row-major, top row first."""
    color_type = 6 if channels == 4 else 2
    stride = width * channels
    raw = b"".join(b"\x00" + bytes(pixels[y * stride:(y + 1) * stride]) for y in range(height))

    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, color_type, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


def downscale_png(source, target, factor=2):
    """Box-filter downscale by an integer factor; keeps RGB or RGBA."""
    width, height, channels, rows = read_png(source)
    out_w, out_h = width // factor, height // factor
    pixels = bytearray(out_w * out_h * channels)
    area = factor * factor
    for y in range(out_h):
        block = rows[y * factor:(y + 1) * factor]
        for x in range(out_w):
            base = x * factor * channels
            for c in range(channels):
                total = 0
                for row in block:
                    for dx in range(factor):
                        total += row[base + dx * channels + c]
                pixels[(y * out_w + x) * channels + c] = total // area
    write_png(target, out_w, out_h, pixels, channels=channels)
    return out_w, out_h
