// Рендер брендовых растров из branding/src/*.svg (Playwright + ImageMagick).
// Запуск: node tools/branding/render.js  → branding/out/
const { chromium } = require('playwright');
const fs = require('fs'), path = require('path'), { execSync } = require('child_process');
const root = path.resolve(__dirname, '..', '..');
const src = n => fs.readFileSync(path.join(root, 'branding/src', n), 'utf8');
const out = path.join(root, 'branding/out'); fs.mkdirSync(out, { recursive: true });
const wordmark = (fg, accent) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 100" width="400" height="100">
<text x="6" y="76" font-family="Inter,Segoe UI Variable,Segoe UI,sans-serif" font-weight="600" font-size="84" letter-spacing="-2" fill="${fg}">bare<tspan font-family="DejaVu Sans Mono,Cascadia Mono,monospace" font-weight="400" fill="#8F8D87" letter-spacing="0">OS</tspan><tspan font-family="DejaVu Sans Mono,Cascadia Mono,monospace" font-weight="400" fill="${accent}" letter-spacing="0">_</tspan></text></svg>`;
(async () => {
  const b = await chromium.launch(); const p = await b.newPage();
  async function png(svg, size, file, bg) {
    await p.setViewportSize({ width: size[0], height: size[1] });
    await p.setContent(`<body style="margin:0;background:${bg || 'transparent'}"><div style="width:${size[0]}px;height:${size[1]}px;display:flex">${svg.replace(/width="\d+" height="\d+"/, `width="${size[0]}" height="${size[1]}"`)}</div>`);
    await p.screenshot({ path: path.join(out, file), omitBackground: !bg });
  }
  const tile = src('mark-tile.svg'), small = src('mark-small.svg'), mark = src('mark.svg');
  for (const s of [16, 20, 24, 32]) await png(tile, [s, s], `tile-${s}.png`);
  for (const s of [48, 64, 128, 256]) await png(tile, [s, s], `tile-${s}.png`);
  await png(tile, [256, 256], 'playbook.png');
  await png(wordmark('#ECEAE4', '#3DDC97'), [800, 200], 'wordmark-dark.png');
  await png(wordmark('#101113', '#127A50'), [800, 200], 'wordmark-light.png');
  fs.writeFileSync(path.join(out, 'wordmark-dark.svg'), wordmark('#ECEAE4', '#3DDC97'));
  // 120×120 для OEMInformation\Logo (BMP, на графитовом фоне)
  await png(mark.replace('viewBox="0 0 100 100"', 'viewBox="-20 -20 140 140"'), [120, 120], 'oem-logo-src.png', '#101113');
  execSync(`convert "${out}/oem-logo-src.png" -background "#101113" -alpha remove BMP3:"${out}/oem-logo.bmp"`);
  fs.unlinkSync(path.join(out, 'oem-logo-src.png'));
  const t = n => `"${out}/tile-${n}.png"`;
  execSync(`convert ${t(16)} ${t(24)} ${t(32)} ${t(48)} ${t(64)} ${t(128)} ${t(256)} "${out}/bareos.ico"`);
  await b.close();
})();
