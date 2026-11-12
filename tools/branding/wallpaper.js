// Обои Bare OS: графит, тонкая сетка, знак «b_» и строка-квитанция с мятным курсором.
// Запуск: node tools/branding/wallpaper.js  ->  branding/out/wallpaper-16x9.png (3840x2160)
const { chromium } = require('playwright');
const fs = require('fs');
(async () => {
  const mark = fs.readFileSync('branding/src/mark.svg', 'utf8');
  const html = `<body style="margin:0;background:#101113;width:3840px;height:2160px;position:relative;overflow:hidden;font-family:'Cascadia Mono',Consolas,'DejaVu Sans Mono',monospace">
  <div style="position:absolute;inset:0;background-image:linear-gradient(rgba(236,234,228,.035) 1px,transparent 1px),linear-gradient(90deg,rgba(236,234,228,.035) 1px,transparent 1px);background-size:120px 120px"></div>
  <div style="position:absolute;left:50%;top:50%;width:360px;height:360px;transform:translate(-50%,-62%)">${mark.replace(/width="\d+" height="\d+"/, 'width="360" height="360"')}</div>
  <div style="position:absolute;left:0;right:0;top:50%;margin-top:240px;text-align:center;color:#8F8D87;font-size:44px;letter-spacing:.18em">bare os</div>
  <div style="position:absolute;left:96px;bottom:180px;color:#8F8D87;font-size:34px;letter-spacing:.04em">bare · 26H2<span style="display:inline-block;width:22px;height:5px;background:#3DDC97;margin-left:10px"></span></div>
  </body>`;
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 3840, height: 2160 } });
  await p.setContent(html);
  await p.screenshot({ path: 'branding/out/wallpaper-16x9.png' });
  await b.close();
})();
