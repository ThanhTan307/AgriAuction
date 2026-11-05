import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

// Build only assets used by the first real page; keep original Stitch CSS/pages intact.
execFileSync(process.execPath, ['node_modules/@tailwindcss/cli/dist/index.mjs',
  '--input', 'assets/css/tailwind-input.css', '--output', 'assets/css/tailwind.generated.css', '--minify'], { stdio: 'inherit', windowsHide: true });
const target = 'assets/vendor/fonts';
fs.mkdirSync(target, { recursive: true });
const faces = [];
for (const [name, family, weights] of [
  ['roboto', 'Roboto', [300, 400, 500, 600, 700, 900]]
]) {
  for (const weight of weights) {
    for (const subset of ['latin', 'latin-ext', 'vietnamese']) {
      const filename = `${name}-${subset}-${weight}-normal.woff2`;
      const source = `node_modules/@fontsource/${name}/files/${filename}`;
      if (!fs.existsSync(source)) continue;
      fs.copyFileSync(source, path.join(target, filename));
      // Latin supplies ASCII; extended subsets use their source unicode ranges.
      // Separate @font-face declarations with the same family allow browser fallback.
      const stylesheet = `node_modules/@fontsource/${name}/${subset}-${weight}.css`;
      let range = '';
      if (fs.existsSync(stylesheet)) {
        const text = fs.readFileSync(stylesheet, 'utf8');
        const match = text.match(/unicode-range:\s*([^;]+);/);
        if (match) range = `unicode-range:${match[1]};`;
      }
      faces.push(`@font-face{font-family:'${family}';font-style:normal;font-weight:${weight};font-display:swap;src:url('../vendor/fonts/${filename}') format('woff2');${range}}`);
    }
  }
  const license = `node_modules/@fontsource/${name}/LICENSE`;
  if (fs.existsSync(license)) fs.copyFileSync(license, path.join(target, `LICENSE-${name}.txt`));
}
const iconName = 'material-symbols-outlined.woff2';
fs.copyFileSync(`node_modules/material-symbols/${iconName}`, path.join(target, iconName));
const iconLicense = 'node_modules/material-symbols/LICENSE';
if (fs.existsSync(iconLicense)) fs.copyFileSync(iconLicense, path.join(target, 'LICENSE-material-symbols.txt'));
faces.push(`@font-face{font-family:'Material Symbols Outlined';font-style:normal;font-weight:100 700;font-display:block;src:url('../vendor/fonts/${iconName}') format('woff2')}`);
faces.push(".material-symbols-outlined{font-family:'Material Symbols Outlined';font-weight:normal;font-style:normal;font-size:24px;line-height:1;letter-spacing:normal;text-transform:none;display:inline-block;white-space:nowrap;word-wrap:normal;direction:ltr;font-feature-settings:'liga';-webkit-font-smoothing:antialiased}");
fs.writeFileSync('assets/css/fonts.generated.css', faces.join('\n') + '\n');
const original = fs.readFileSync('assets/css/custom.css', 'utf8');
fs.mkdirSync('assets/vendor/signalr', { recursive: true });
fs.copyFileSync('node_modules/@microsoft/signalr/dist/browser/signalr.min.js', 'assets/vendor/signalr/signalr.min.js');
fs.copyFileSync('node_modules/@microsoft/signalr/package.json', 'assets/vendor/signalr/package.json');
// Font URLs themselves contain semicolons; match the complete import line.
fs.writeFileSync('assets/css/app-theme.generated.css', original.replace(/^@import[^\r\n]+;[ \t]*\r?\n?/gm, ''));
console.log('Local Stitch CSS and font assets built. Original custom.css preserved.');
