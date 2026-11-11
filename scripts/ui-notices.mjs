// Preserve complete upstream license notices alongside the bundled runtime UI.
import { readFile, readdir, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const ui = resolve(dirname(fileURLToPath(import.meta.url)), '../src/Verificato.UI');
const supplemental = resolve(ui, '../../third-party-licenses');
const fallbacks = { '@microsoft/signalr@10.0.0': 'signalr-LICENSE.txt', 'react-remove-scroll-bar@2.3.8': 'react-remove-scroll-bar-LICENSE.txt', 'tr46@0.0.3': 'tr46-LICENSE.txt' };
const radixFallbacks = new Set(['@radix-ui/react-compose-refs@1.1.2', '@radix-ui/react-context@1.1.2', '@radix-ui/react-id@1.1.1', '@radix-ui/react-use-callback-ref@1.1.1', '@radix-ui/react-use-escape-keydown@1.1.1', '@radix-ui/react-use-layout-effect@1.1.1']);
const lock = JSON.parse(await readFile(join(ui, 'package-lock.json'), 'utf8'));
const notices = ['Verificato UI — third-party runtime dependency notices', 'Generated from package-lock.json and installed package license files. Dependencies retain their own licenses.'];
for (const [path, metadata] of Object.entries(lock.packages).sort(([a], [b]) => a.localeCompare(b))) {
  if (!path || metadata.dev) continue;
  const directory = join(ui, path);
  let entries;
  try { entries = await readdir(directory, { withFileTypes: true }); }
  catch (error) { if (metadata.optional && error.code === 'ENOENT') continue; throw error; }
  const pkg = JSON.parse(await readFile(join(directory, 'package.json'), 'utf8'));
  const files = entries.filter(entry => entry.isFile() && /^(?:UNLICENSE|LICEN[CS]E|NOTICE|COPYING)(?:[.-].*)?$/i.test(entry.name));
  const key = `${pkg.name}@${pkg.version}`;
  let fallback;
  if (!files.length && pkg.license === 'MIT') {
    fallback = radixFallbacks.has(key) ? join(ui, 'node_modules/@radix-ui/react-dialog/LICENSE') : fallbacks[key] ? join(supplemental, fallbacks[key]) : undefined;
  }
  if (!files.length && !fallback) throw new Error(`Missing installed license notice: ${key}. Review upstream license before distributing.`);
  notices.push(`\n${'='.repeat(78)}\n${pkg.name}@${pkg.version} — ${metadata.license ?? pkg.license ?? 'see license text'}`);
  if (fallback) notices.push(`--- Supplemental upstream license (see third-party-licenses/README.md) ---\n${await readFile(fallback, 'utf8')}`);
  for (const file of files) notices.push(`--- ${file.name} ---\n${await readFile(join(directory, file.name), 'utf8')}`);
}
await writeFile(join(ui, 'dist/UI-THIRD-PARTY-LICENSES.txt'), notices.join('\n\n') + '\n', 'utf8');
console.log('Preserved UI dependency license notices in dist/UI-THIRD-PARTY-LICENSES.txt');
