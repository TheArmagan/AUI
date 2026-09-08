// Renders the AUI favicon set from the logo's own A glyph.
// The glyph is straight-line only, so an even-odd scanline fill with 4x4 supersampling is
// enough and needs no rasteriser dependency. Output: PNGs + a multi-size .ico.

import { deflateSync } from 'node:zlib'
import { writeFileSync } from 'node:fs'
import { join } from 'node:path'

const OUT = process.argv[2]
if (!OUT) throw new Error('usage: node gen-icons.mjs <public dir>')

const AMBER = [0xe0, 0xa8, 0x6a]
const DARK = [0x15, 0x0f, 0x0b]

// Path data from tools~/vscode-aui/icons/aui.svg, A glyph only, in a 32-unit box.
const RAW_OUTER = [
  [3.3, 22.5], [7.55, 9.5], [9.15, 9.5], [13.4, 22.5],
  [10.5, 22.5], [10.09, 20.8], [6.61, 20.8], [6.2, 22.5],
]
const RAW_HOLE = [[7.13, 18.6], [9.57, 18.6], [8.35, 13.5]]

// translate(4.43 -6.16) scale(1.385): centres the glyph in the 32-unit tile.
const tx = (p) => [4.43 + 1.385 * p[0], -6.16 + 1.385 * p[1]]
const RINGS = [RAW_OUTER.map(tx), RAW_HOLE.map(tx)]

function edges(rings) {
  const e = []
  for (const ring of rings) {
    for (let i = 0; i < ring.length; i++) {
      const a = ring[i]
      const b = ring[(i + 1) % ring.length]
      if (a[1] !== b[1]) e.push([a[0], a[1], b[0], b[1]])
    }
  }
  return e
}
const GLYPH_EDGES = edges(RINGS)

// Even-odd: count edges the upward ray from (x,y) crosses.
function inGlyph(x, y) {
  let c = 0
  for (const [x0, y0, x1, y1] of GLYPH_EDGES) {
    if ((y0 > y) !== (y1 > y)) {
      const t = (y - y0) / (y1 - y0)
      if (x < x0 + t * (x1 - x0)) c++
    }
  }
  return (c & 1) === 1
}

function inRoundRect(x, y, size, r) {
  if (r <= 0) return x >= 0 && y >= 0 && x <= size && y <= size
  const cx = Math.min(Math.max(x, r), size - r)
  const cy = Math.min(Math.max(y, r), size - r)
  const dx = x - cx
  const dy = y - cy
  if (dx === 0 && dy === 0) return x >= 0 && y >= 0 && x <= size && y <= size
  return dx * dx + dy * dy <= r * r
}

function render(px, { rounded = true, opaque = false } = {}) {
  const SS = 4
  const buf = Buffer.alloc(px * px * 4)
  const scale = 32 / px          // pixel space -> 32-unit design space
  const radius = rounded ? 7 : 0

  for (let py = 0; py < px; py++) {
    for (let pxi = 0; pxi < px; pxi++) {
      let rectCov = 0
      let glyphCov = 0
      for (let sy = 0; sy < SS; sy++) {
        for (let sx = 0; sx < SS; sx++) {
          const ux = (pxi + (sx + 0.5) / SS) * scale
          const uy = (py + (sy + 0.5) / SS) * scale
          if (inRoundRect(ux, uy, 32, radius)) {
            rectCov++
            if (inGlyph(ux, uy)) glyphCov++
          }
        }
      }
      const n = SS * SS
      let a = rectCov / n
      const g = rectCov === 0 ? 0 : glyphCov / rectCov
      const r = Math.round(AMBER[0] * (1 - g) + DARK[0] * g)
      const gg = Math.round(AMBER[1] * (1 - g) + DARK[1] * g)
      const b = Math.round(AMBER[2] * (1 - g) + DARK[2] * g)
      if (opaque) a = 1
      const o = (py * px + pxi) * 4
      buf[o] = r
      buf[o + 1] = gg
      buf[o + 2] = b
      buf[o + 3] = Math.round(a * 255)
    }
  }
  return buf
}

// ---- minimal PNG encoder -------------------------------------------------
const CRC_TABLE = (() => {
  const t = new Int32Array(256)
  for (let n = 0; n < 256; n++) {
    let c = n
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
    t[n] = c
  }
  return t
})()

function crc32(buf) {
  let c = 0xffffffff
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8)
  return (c ^ 0xffffffff) >>> 0
}

function chunk(type, data) {
  const len = Buffer.alloc(4)
  len.writeUInt32BE(data.length)
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data])
  const crc = Buffer.alloc(4)
  crc.writeUInt32BE(crc32(td))
  return Buffer.concat([len, td, crc])
}

function png(rgba, px) {
  const ihdr = Buffer.alloc(13)
  ihdr.writeUInt32BE(px, 0)
  ihdr.writeUInt32BE(px, 4)
  ihdr[8] = 8      // bit depth
  ihdr[9] = 6      // colour type RGBA
  const raw = Buffer.alloc((px * 4 + 1) * px)
  for (let y = 0; y < px; y++) {
    raw[y * (px * 4 + 1)] = 0      // filter: none
    rgba.copy(raw, y * (px * 4 + 1) + 1, y * px * 4, (y + 1) * px * 4)
  }
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ])
}

// ---- ICO (PNG-compressed entries) ---------------------------------------
function ico(entries) {
  const header = Buffer.alloc(6)
  header.writeUInt16LE(0, 0)
  header.writeUInt16LE(1, 2)          // type: icon
  header.writeUInt16LE(entries.length, 4)
  const dir = Buffer.alloc(16 * entries.length)
  let offset = 6 + dir.length
  entries.forEach((e, i) => {
    const o = i * 16
    dir[o] = e.size >= 256 ? 0 : e.size
    dir[o + 1] = e.size >= 256 ? 0 : e.size
    dir[o + 2] = 0                     // palette
    dir[o + 3] = 0
    dir.writeUInt16LE(1, o + 4)        // colour planes
    dir.writeUInt16LE(32, o + 6)       // bits per pixel
    dir.writeUInt32LE(e.data.length, o + 8)
    dir.writeUInt32LE(offset, o + 12)
    offset += e.data.length
  })
  return Buffer.concat([header, dir, ...entries.map((e) => e.data)])
}

// ---- emit ----------------------------------------------------------------
const written = []
const emit = (name, px, opts) => {
  const data = png(render(px, opts), px)
  writeFileSync(join(OUT, name), data)
  written.push(`${name} (${px}x${px}, ${data.length} bytes)`)
  return data
}

emit('favicon-16.png', 16)
emit('favicon-32.png', 32)
emit('favicon-48.png', 48)
emit('icon-192.png', 192)
emit('icon-512.png', 512)
// iOS masks the corners itself and shows artefacts on transparency, so ship it square and opaque.
emit('apple-touch-icon.png', 180, { rounded: false, opaque: true })

const icoBuf = ico([16, 32, 48].map((size) => ({ size, data: png(render(size), size) })))
writeFileSync(join(OUT, 'favicon.ico'), icoBuf)
written.push(`favicon.ico (16+32+48, ${icoBuf.length} bytes)`)

console.log(written.join('\n'))
