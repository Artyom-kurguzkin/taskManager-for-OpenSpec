// Renders an Excalidraw scene to <name>.excalidraw.svg next to it, so markdown can show it.
//   node DevResources/tools/excalidraw-to-svg.mjs <file.excalidraw> [...]
// The scene may use the shorthand of the Excalidraw MCP server (a `label` on a shape or arrow);
// it is expanded into real bound text elements and the .excalidraw file is rewritten, so it opens in Excalidraw.
// ponytail: plain SVG shapes, no hand-drawn roughness and only rectangle, ellipse, diamond, text, arrow and line;
// switch to Excalidraw's own exportToSvg (needs a browser) if drawings outgrow that.
import { readFileSync, writeFileSync } from 'node:fs';

const FONT = 'Segoe UI, system-ui, sans-serif';
const esc = s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const textSize = (text, fs) => ({
  width: Math.max(...text.split('\n').map(l => l.length)) * fs * 0.55,
  height: text.split('\n').length * fs * 1.25,
});

function expand(elements) {
  const out = [];
  for (const e of elements) {
    if (e.type === 'cameraUpdate' || e.type === 'delete') continue;
    const { label, ...el } = e;
    if (el.type === 'text' && el.width == null) Object.assign(el, textSize(el.text, el.fontSize ?? 16));
    out.push(el);
    if (!label) continue;
    const fontSize = label.fontSize ?? 16, { width, height } = textSize(label.text, fontSize);
    const mid = el.points ? el.points[Math.floor((el.points.length - 1) / 2)] : null;
    const next = el.points ? el.points[Math.floor((el.points.length - 1) / 2) + 1] : null;
    const cx = el.points ? el.x + (mid[0] + next[0]) / 2 : el.x + el.width / 2;
    const cy = el.points ? el.y + (mid[1] + next[1]) / 2 : el.y + el.height / 2;
    el.boundElements = [...(el.boundElements ?? []), { type: 'text', id: el.id + '-label' }];
    out.push({
      type: 'text', id: el.id + '-label', containerId: el.id, text: label.text, originalText: label.text, fontSize,
      x: cx - width / 2, y: cy - height / 2, width, height, textAlign: 'center', verticalAlign: 'middle',
      strokeColor: label.strokeColor ?? '#1e1e1e',
    });
  }
  return out;
}

function render(elements) {
  const byId = Object.fromEntries(elements.map(e => [e.id, e]));
  const xs = [], ys = [];
  for (const e of elements) {
    const pts = e.points ? e.points.map(p => [e.x + p[0], e.y + p[1]]) : [[e.x, e.y], [e.x + (e.width ?? 0), e.y + (e.height ?? 0)]];
    for (const [x, y] of pts) { xs.push(x); ys.push(y); }
  }
  const pad = 24, x0 = Math.min(...xs) - pad, y0 = Math.min(...ys) - pad;
  const w = Math.max(...xs) + pad - x0, h = Math.max(...ys) + pad - y0;
  const parts = [];
  for (const e of elements) {
    const stroke = e.strokeColor ?? '#1e1e1e', fill = e.backgroundColor ?? 'none', sw = e.strokeWidth ?? 2;
    const style = `stroke="${stroke}" stroke-width="${sw}" fill="${fill === 'transparent' ? 'none' : fill}" opacity="${(e.opacity ?? 100) / 100}"`
      + (e.strokeStyle === 'dashed' ? ' stroke-dasharray="8 6"' : '');
    if (e.type === 'rectangle') parts.push(`<rect x="${e.x}" y="${e.y}" width="${e.width}" height="${e.height}" rx="${e.roundness ? 10 : 0}" ${style}/>`);
    else if (e.type === 'ellipse') parts.push(`<ellipse cx="${e.x + e.width / 2}" cy="${e.y + e.height / 2}" rx="${e.width / 2}" ry="${e.height / 2}" ${style}/>`);
    else if (e.type === 'diamond') parts.push(`<polygon points="${e.x + e.width / 2},${e.y} ${e.x + e.width},${e.y + e.height / 2} ${e.x + e.width / 2},${e.y + e.height} ${e.x},${e.y + e.height / 2}" ${style}/>`);
    else if (e.type === 'arrow' || e.type === 'line') {
      const pts = e.points.map(p => `${e.x + p[0]},${e.y + p[1]}`).join(' ');
      const head = e.type === 'arrow' && e.endArrowhead !== null ? ` marker-end="url(#head-${stroke.slice(1)})"` : '';
      parts.push(`<polyline points="${pts}" fill="none" stroke="${stroke}" stroke-width="${sw}" stroke-linejoin="round"${e.strokeStyle === 'dashed' ? ' stroke-dasharray="8 6"' : ''}${head}/>`);
    } else if (e.type === 'text') {
      const lines = e.text.split('\n'), fs = e.fontSize ?? 16, lh = fs * 1.25, box = byId[e.containerId];
      const centered = !!box, cx = centered ? e.x + e.width / 2 : e.x;
      if (box?.points) parts.push(`<rect x="${e.x - 4}" y="${e.y - 2}" width="${e.width + 8}" height="${e.height + 4}" fill="#ffffff"/>`);
      parts.push(`<text x="${cx}" y="${e.y + fs}" font-family="${FONT}" font-size="${fs}" fill="${e.strokeColor ?? '#1e1e1e'}"${centered ? ' text-anchor="middle"' : ''}>`
        + lines.map((l, i) => `<tspan x="${cx}" dy="${i ? lh : 0}">${esc(l)}</tspan>`).join('') + '</text>');
    }
  }
  const colors = [...new Set(elements.filter(e => e.type === 'arrow').map(e => e.strokeColor ?? '#1e1e1e'))];
  const defs = colors.map(c => `<marker id="head-${c.slice(1)}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="${c}"/></marker>`).join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${x0} ${y0} ${w} ${h}" width="${w}" height="${h}"><defs>${defs}</defs><rect x="${x0}" y="${y0}" width="${w}" height="${h}" rx="10" fill="#ffffff"/>${parts.join('')}</svg>\n`;
}

for (const file of process.argv.slice(2)) {
  const raw = JSON.parse(readFileSync(file, 'utf8'));
  const elements = expand(Array.isArray(raw) ? raw : raw.elements);
  writeFileSync(file, JSON.stringify({ type: 'excalidraw', version: 2, source: 'repo-tasks', elements, appState: { viewBackgroundColor: '#ffffff' }, files: {} }, null, 1) + '\n');
  writeFileSync(file + '.svg', render(elements));
  console.log(file + '.svg', elements.length, 'elements');
}
