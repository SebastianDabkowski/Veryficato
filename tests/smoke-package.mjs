// Builds are intentionally separate: run scripts/package.ps1 first.
import { chromium } from '../src/Verificato.UI/node_modules/playwright-core/index.mjs';
import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import assert from 'node:assert/strict';

const root = resolve(import.meta.dirname, '..');
const executable = resolve(process.argv.find(x => x.startsWith('--exe='))?.slice(6) || join(root, 'artifacts/verificato-win-x64/Verificato.Host.exe'));
const headed = process.argv.includes('--headed');
const data = await mkdtemp(join(tmpdir(), 'verificato-package-'));
async function freePort() {
  const server = createServer();
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const { port } = server.address();
  await new Promise(r => server.close(r));
  return port;
}
async function stop(child) {
  if (!child || child.exitCode !== null) return;
  child.kill();
  await Promise.race([new Promise(r => child.once('exit', r)), new Promise(r => setTimeout(r, 3000))]);
}
let host, target, browser;
try {
  await mkdir(join(data, 'source/Core'), { recursive: true });
  await writeFile(join(data, 'source/App.csproj'), '<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="Core/Core.csproj" /></ItemGroup></Project>');
  await writeFile(join(data, 'source/Program.cs'), 'System.Console.WriteLine("application");');
  await writeFile(join(data, 'source/Core/Core.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>');
  await writeFile(join(data, 'source/Core/Service.cs'), 'namespace Example;\npublic class Service { public string GetGreeting() => "Hello"; }');
  const port = await freePort(), targetPort = await freePort();
  target = spawn(process.execPath, ['tests/Fixtures/BrowserTarget/server.mjs'], { cwd: root, env: { ...process.env, VERIFICATO_FIXTURE_PORT: String(targetPort) }, stdio: 'pipe' });
  host = spawn(executable, ['--no-open', 'true', '--print-bootstrap', 'true', '--port', String(port), '--data-dir', join(data, 'store')], {
    cwd: root, env: { ...process.env, VERIFICATO_HEADLESS: headed ? '0' : '1' }, stdio: ['ignore', 'pipe', 'pipe'],
  });
  const token = await new Promise((resolveToken, reject) => {
    let output = '';
    const timeout = setTimeout(() => reject(new Error('Published host startup timed out')), 20000);
    host.stdout.on('data', chunk => {
      output += chunk.toString(); const match = output.match(/#bootstrap=([A-Za-z0-9_-]+)/);
      if (match) { clearTimeout(timeout); resolveToken(match[1]); }
    });
    host.once('exit', code => { clearTimeout(timeout); reject(new Error(`Published host exited: ${code}`)); });
  });
  browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.goto(`http://127.0.0.1:${port}/#bootstrap=${token}`);
  await page.getByRole('button', { name: 'Utwórz pierwszy projekt' }).click();
  await page.getByLabel('Nazwa projektu', { exact: true }).fill('Published package');
  await page.getByLabel('Katalog kodu źródłowego').fill(join(data, 'source'));
  await page.getByLabel('Adres uruchomionej aplikacji').fill(`http://127.0.0.1:${targetPort}/automated`);
  await page.getByRole('button', { name: 'Utwórz projekt', exact: true }).click();
  await page.getByRole('button', { name: 'Rozpocznij sesję', exact: true }).click();
  await page.getByText('Nieobsłużony wyjątek JavaScript', { exact: true }).first().waitFor();
  await page.getByRole('button', { name: 'Zatrzymaj sesję', exact: true }).click();
  await page.getByRole('button', { name: 'Rozpocznij sesję', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Architektura C4', exact: true }).click();
  await page.getByRole('button', { name: 'Zbuduj diagram architektury', exact: true }).click();
  await page.getByRole('button', { name: 'Komponent Core', exact: true }).click();
  await page.getByRole('tab', { name: 'Typy (1)', exact: true }).click();
  await page.locator('.type-detail summary').filter({ hasText: 'Service' }).click();
  await page.getByRole('button', { name: 'Otwórz deklarację · L2', exact: true }).click();
  await page.getByRole('dialog', { name: 'Kod źródłowy' }).waitFor();
  await page.locator('.source-code').waitFor();
  assert.match(await page.locator('.source-code').textContent(), /GetGreeting/);
  assert.deepEqual(errors, []);
  console.log(`Published package smoke passed (C4/source preview + ${headed ? 'headed target Chromium' : 'headless target Chromium'}).`);
} finally {
  await browser?.close();
  await stop(host); await stop(target);
  await rm(data, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
}
