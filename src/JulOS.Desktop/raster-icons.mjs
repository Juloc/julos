// MOB-002: deterministic PNG icon generation.
//
// iOS Safari ignores the web manifest's icon list and installs the PNG referenced by
// <link rel="apple-touch-icon">, and an installability audit wants raster 192/512 icons,
// so SVG alone is not enough. Rather than committing binary artwork that can drift from
// the SVG, the icons are rasterized at build time from the same shapes.
//
// The rasterizer covers exactly what the two JulOS icons use: a filled rounded rectangle,
// a vertical linear gradient fill and one path of lines and cubic Béziers. Output is
// byte-deterministic, so an unchanged icon produces an unchanged file.

import { deflateSync } from 'node:zlib';

const brandStart = [0x1f, 0x6f, 0xeb];
const brandEnd = [0x0d, 0x41, 0x9d];
const white = [0xff, 0xff, 0xff];

/** The "J" mark of the full-bleed icon, in a 512x512 coordinate space. */
const markPath =
  'M330 120h60v170c0 62-46 104-112 104-58 0-99-32-110-84l58-16c6 26 24 42 52 42 31 0 52-21 52-56z';

/** The maskable mark, kept inside the central 80% safe zone. */
const maskablePath =
  'M300 170h44v126c0 47-35 79-85 79-44 0-75-24-84-64l44-12c5 19 18 31 40 31 23 0 41-16 41-42z';

/** Renders the full-bleed icon: rounded rectangle, diagonal gradient, white mark. */
export function renderIcon(size) {
  return render(size, {
    cornerRadius: 112,
    gradient: true,
    path: markPath,
  });
}

/** Renders the maskable icon: full-bleed solid background, mark inside the safe zone. */
export function renderMaskableIcon(size) {
  return render(size, {
    cornerRadius: 0,
    gradient: false,
    path: maskablePath,
  });
}

function render(size, options) {
  const scale = size / 512;
  const samples = 4; // 4x4 supersampling; enough for these shapes at 192px and up.
  const pixels = new Uint8Array(size * size * 4);
  const polygon = flattenPath(options.path).map((point) => [point[0] * scale, point[1] * scale]);
  const radius = options.cornerRadius * scale;

  for (let y = 0; y < size; y += 1) {
    for (let x = 0; x < size; x += 1) {
      let background = 0;
      let mark = 0;

      for (let sy = 0; sy < samples; sy += 1) {
        for (let sx = 0; sx < samples; sx += 1) {
          const px = x + (sx + 0.5) / samples;
          const py = y + (sy + 0.5) / samples;
          if (insideRoundedRect(px, py, size, radius)) {
            background += 1;
          }
          if (insidePolygon(px, py, polygon)) {
            mark += 1;
          }
        }
      }

      const total = samples * samples;
      const backgroundAlpha = background / total;
      const markAlpha = mark / total;
      const base = options.gradient
        ? mix(brandStart, brandEnd, (x + y) / (2 * size))
        : brandEnd;
      const colour = mix(base, white, markAlpha);

      const offset = (y * size + x) * 4;
      pixels[offset] = colour[0];
      pixels[offset + 1] = colour[1];
      pixels[offset + 2] = colour[2];
      pixels[offset + 3] = Math.round(backgroundAlpha * 255);
    }
  }

  return encodePng(size, size, pixels);
}

function mix(from, to, amount) {
  const t = Math.min(1, Math.max(0, amount));
  return [
    Math.round(from[0] + (to[0] - from[0]) * t),
    Math.round(from[1] + (to[1] - from[1]) * t),
    Math.round(from[2] + (to[2] - from[2]) * t),
  ];
}

function insideRoundedRect(x, y, size, radius) {
  if (radius <= 0) {
    return x >= 0 && y >= 0 && x <= size && y <= size;
  }
  const cx = Math.min(Math.max(x, radius), size - radius);
  const cy = Math.min(Math.max(y, radius), size - radius);
  const dx = x - cx;
  const dy = y - cy;
  return dx * dx + dy * dy <= radius * radius;
}

/** Even-odd containment test. The JulOS mark is a single non-self-intersecting outline. */
function insidePolygon(x, y, points) {
  let inside = false;
  for (let i = 0, j = points.length - 1; i < points.length; j = i, i += 1) {
    const [xi, yi] = points[i];
    const [xj, yj] = points[j];
    if ((yi > y) !== (yj > y) && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) {
      inside = !inside;
    }
  }
  return inside;
}

/** Flattens the SVG path subset (M, h, v, l, c, z) into a polygon. */
function flattenPath(path) {
  const tokens = path.match(/[MmHhVvLlCcZz]|-?\d+(?:\.\d+)?/g) ?? [];
  const points = [];
  let index = 0;
  let x = 0;
  let y = 0;
  let command = '';

  const number = () => Number(tokens[index++]);
  const isNumber = (token) => token !== undefined && /^-?\d/.test(token);

  while (index < tokens.length) {
    // SVG allows a command letter to be followed by several parameter sets; the letter is
    // then implicit for every set after the first. `M` repeats as an implicit lineto.
    if (isNumber(tokens[index])) {
      if (command === '') {
        throw new Error('The JulOS icon path starts with a parameter instead of a command.');
      }
      if (command === 'M') {
        command = 'L';
      }
    } else {
      command = tokens[index++];
    }

    switch (command) {
      case 'M':
        x = number();
        y = number();
        points.push([x, y]);
        break;
      case 'h':
        x += number();
        points.push([x, y]);
        break;
      case 'v':
        y += number();
        points.push([x, y]);
        break;
      case 'l': {
        x += number();
        y += number();
        points.push([x, y]);
        break;
      }
      case 'L': {
        x = number();
        y = number();
        points.push([x, y]);
        break;
      }
      case 'c': {
        const x1 = x + number();
        const y1 = y + number();
        const x2 = x + number();
        const y2 = y + number();
        const x3 = x + number();
        const y3 = y + number();
        const steps = 24;
        for (let step = 1; step <= steps; step += 1) {
          const t = step / steps;
          points.push(cubic(x, y, x1, y1, x2, y2, x3, y3, t));
        }
        x = x3;
        y = y3;
        break;
      }
      case 'z':
      case 'Z':
        break;
      default:
        throw new Error(`Unsupported path command '${command}' in the JulOS icon.`);
    }
  }

  return points;
}

function cubic(x0, y0, x1, y1, x2, y2, x3, y3, t) {
  const u = 1 - t;
  const a = u * u * u;
  const b = 3 * u * u * t;
  const c = 3 * u * t * t;
  const d = t * t * t;
  return [a * x0 + b * x1 + c * x2 + d * x3, a * y0 + b * y1 + c * y2 + d * y3];
}

function encodePng(width, height, rgba) {
  const raw = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y += 1) {
    raw[y * (width * 4 + 1)] = 0; // filter type: none, which keeps output deterministic
    Buffer.from(rgba.buffer, rgba.byteOffset + y * width * 4, width * 4)
      .copy(raw, y * (width * 4 + 1) + 1);
  }

  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8; // bit depth
  header[9] = 6; // colour type: RGBA
  header[10] = 0;
  header[11] = 0;
  header[12] = 0;

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

function chunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length, 0);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body), 0);
  return Buffer.concat([length, body, crc]);
}

const crcTable = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n += 1) {
    let c = n;
    for (let k = 0; k < 8; k += 1) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    table[n] = c >>> 0;
  }
  return table;
})();

function crc32(buffer) {
  let c = 0xffffffff;
  for (const byte of buffer) {
    c = crcTable[(c ^ byte) & 0xff] ^ (c >>> 8);
  }
  return (c ^ 0xffffffff) >>> 0;
}
