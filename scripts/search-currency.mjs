import fs from 'node:fs';
import path from 'node:path';

function walk(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory() && entry.name !== 'node_modules' && entry.name !== '.git') {
      walk(full);
    } else if (entry.isFile() && (entry.name.endsWith('.js') || entry.name.endsWith('.html'))) {
      const content = fs.readFileSync(full, 'utf8');
      const lines = content.split('\n');
      lines.forEach((line, idx) => {
        if (/simulatedRevenue|\.amount\s*\+|\.winningPrice\s*\+|\.depositAmount\s*\+|14000000|\.total\s*\+/.test(line)) {
          console.log(`${full}:${idx + 1}: ${line.trim().slice(0, 140)}`);
        }
      });
    }
  }
}
walk('assets/js');
walk('pages');

