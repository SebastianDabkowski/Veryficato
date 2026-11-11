import { test, expect } from '@playwright/test';
import { spawn, execFileSync, type ChildProcess } from 'node:child_process';
import { mkdtemp, mkdir, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve, join } from 'node:path';
import { createServer } from 'node:net';

const root = resolve(import.meta.dirname, '../../..');
let host: ChildProcess, target: ChildProcess, directory: string, origin: string, bootstrap: string, targetUrl: string;
async function port() {
  const server = createServer();
  await new Promise<void>(r => server.listen(0, '127.0.0.1', r));
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('No test port');
  await new Promise<void>((r, reject) => server.close(e => e ? reject(e) : r()));
  return address.port;
}
async function stop(child?: ChildProcess) {
  if (!child || child.exitCode !== null) return;
  child.kill();
  await Promise.race([new Promise(r => child.once('exit', r)), new Promise(r => setTimeout(r, 5000))]);
}
test.beforeAll(async () => {
  directory = await mkdtemp(join(tmpdir(), 'verificato-e2e-'));
  const source = join(directory, 'source'); await mkdir(source);
  await writeFile(join(source, 'Fixture.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>');
  await writeFile(join(source, 'Fixture.cs'), 'class Fixture { int value = "broken"; void Work() { try {} catch {} } }');
  const architecture = join(directory, 'architecture');
  for (const name of ['Fixture.App', 'Fixture.Core', 'Fixture.Store', 'Fixture.Worker', 'Fixture.Tests']) await mkdir(join(architecture, name), { recursive: true });
  await writeFile(join(architecture, 'Fixture.App/Fixture.App.csproj'), `<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>
    <ProjectReference Include="../Fixture.Core/Fixture.Core.csproj" />
    <ProjectReference Include="../Fixture.Store/Fixture.Store.csproj" Condition="'$(Configuration)' == 'Debug'" /></ItemGroup></Project>`);
  await writeFile(join(architecture, 'Fixture.App/Program.cs'), 'System.Console.WriteLine("app");');
  await writeFile(join(architecture, 'Fixture.Core/Fixture.Core.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>');
  await writeFile(join(architecture, 'Fixture.Core/OrderService.cs'), `namespace Example.Core;
/// <summary>Retrieves orders from the local catalog.</summary>
public class OrderService
{
    /// <summary>Find an order by identifier.</summary>
    public string FindOrder(int id) => "<img src=x onerror=alert(1)>";
}`);
  await writeFile(join(architecture, 'Fixture.Store/Fixture.Store.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Fixture.Core/Fixture.Core.csproj" /><PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.14" /></ItemGroup></Project>');
  await writeFile(join(architecture, 'Fixture.Store/Repository.cs'), 'namespace Example.Store; public class Repository {}');
  await writeFile(join(architecture, 'Fixture.Worker/Fixture.Worker.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>');
  await writeFile(join(architecture, 'Fixture.Tests/Fixture.Tests.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><ProjectReference Include="../Fixture.App/Fixture.App.csproj" /></ItemGroup></Project>');
  execFileSync('dotnet', ['build', 'src/Verificato.Host', '--nologo'], { cwd: root, stdio: 'pipe' });
  const hostPort = await port(), targetPort = await port();
  origin = `http://127.0.0.1:${hostPort}`; targetUrl = `http://127.0.0.1:${targetPort}`;
  target = spawn(process.execPath, ['tests/Fixtures/BrowserTarget/server.mjs'], { cwd: root, env: { ...process.env, VERIFICATO_FIXTURE_PORT: String(targetPort) }, stdio: 'pipe' });
  host = spawn('dotnet', ['src/Verificato.Host/bin/Debug/net10.0/Verificato.Host.dll', '--port', String(hostPort), '--data-dir', join(directory, 'data'), '--no-open', 'true', '--print-bootstrap', 'true'], {
    cwd: root, env: { ...process.env, VERIFICATO_HEADLESS: '1' }, stdio: ['ignore', 'pipe', 'pipe'],
  });
  await new Promise<void>((resolveReady, reject) => {
    let output = '';
    const timeout = setTimeout(() => reject(new Error('Host did not start within 20 seconds')), 20000);
    host.stdout!.on('data', (chunk: Buffer) => {
      output += chunk.toString();
      const match = output.match(/#bootstrap=([A-Za-z0-9_-]+)/);
      if (match) { bootstrap = match[1]; clearTimeout(timeout); resolveReady(); }
    });
    host.once('exit', code => { clearTimeout(timeout); reject(new Error(`Host exited: ${code}`)); });
  });
});
test.afterAll(async () => {
  await stop(host); await stop(target);
  if (directory) await rm(directory, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
});

test('project → live monitoring → saved report → Roslyn → interactive C4 architecture and source', async ({ page }) => {
  test.setTimeout(90_000);
  const browserErrors: string[] = [];
  page.on('pageerror', e => browserErrors.push(e.message));
  await page.goto(`${origin}/#bootstrap=${bootstrap}`);
  await expect(page.getByRole('button', { name: 'Utwórz pierwszy projekt' })).toBeVisible();
  expect(page.url()).not.toContain('bootstrap');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  const palette = await page.locator('html').evaluate(element => {
    const style = getComputedStyle(element);
    return { background: style.backgroundColor, accent: style.getPropertyValue('--accent').trim() };
  });
  expect(palette).toEqual({ background: 'rgb(11, 17, 32)', accent: '#2563eb' });
  await page.getByRole('button', { name: 'Utwórz pierwszy projekt' }).click();
  await page.getByLabel('Nazwa projektu', { exact: true }).fill('Customer Portal');
  await page.getByLabel('Katalog kodu źródłowego').fill(join(directory, 'source'));
  await page.getByLabel('Adres uruchomionej aplikacji').fill(`${targetUrl}/automated`);
  await page.getByRole('button', { name: 'Utwórz projekt', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Customer Portal LOCAL' })).toBeVisible();
  await page.getByRole('button', { name: 'Rozpocznij sesję', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Zatrzymaj sesję', exact: true })).toBeVisible();
  await expect(page.getByText('Na żywo', { exact: true })).toBeVisible();
  await expect(page.getByText('HTTP 500', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('Nieobsłużony wyjątek JavaScript', { exact: true }).first()).toBeVisible();
  await page.getByText('HTTP 500', { exact: true }).first().click();
  await expect(page.getByRole('dialog')).toContainText('GET → 500');
  await page.getByRole('button', { name: 'Zamknij', exact: true }).click();
  await page.getByRole('button', { name: 'Wstrzymaj widok', exact: true }).click();
  await expect(page.getByText('Widok wstrzymany. Zbieranie danych trwa nadal.')).toBeVisible();
  await page.getByRole('button', { name: 'Wznów widok', exact: true }).click();
  await page.evaluate(() => scrollTo(0, 0));
  await page.screenshot({ path: resolve(root, 'artifacts/e2e-dashboard.png'), fullPage: true });
  await page.getByRole('button', { name: 'Zatrzymaj sesję', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Rozpocznij sesję', exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByText('HTTP 500', { exact: true }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Analizuj kod', exact: true }).click();
  await expect(page.getByText('CS0029', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('Pusty blok catch', { exact: true }).first()).toBeVisible();
  await page.getByRole('tab', { name: /Metryki/ }).click();
  await expect(page.getByText('Fizyczne linie kodu', { exact: true }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Włącz jasny motyw' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.getByRole('button', { name: 'Włącz ciemny motyw' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.screenshot({ path: resolve(root, 'artifacts/e2e-dark.png'), fullPage: true });
  await page.getByRole('button', { name: /Historia weryfikacji/ }).click();
  await expect(page.getByRole('table').getByText('Zakończono', { exact: true })).toHaveCount(2);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: 'Przegląd projektu', exact: true }).click();
  await page.screenshot({ path: resolve(root, 'artifacts/e2e-mobile.png'), fullPage: true });
  const overflow = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, viewport: innerWidth,
    elements: [...document.querySelectorAll('body *')].filter(e => e.getBoundingClientRect().right > innerWidth + 1 && getComputedStyle(e).position !== 'fixed').slice(0, 20).map(e => `${e.tagName}.${e.className}: ${Math.round(e.getBoundingClientRect().right)}`) }));
  expect(overflow.width, JSON.stringify(overflow)).toBeLessThanOrEqual(overflow.viewport);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.getByRole('button', { name: 'Nowy projekt', exact: true }).click();
  await page.getByLabel('Nazwa projektu', { exact: true }).fill('Architecture Fixture');
  await page.getByLabel('Katalog kodu źródłowego').fill(join(directory, 'architecture'));
  await page.getByLabel('Adres uruchomionej aplikacji').fill(targetUrl);
  await page.getByRole('button', { name: 'Utwórz projekt', exact: true }).click();
  await page.getByRole('button', { name: 'Architektura C4', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Zobacz, jak zbudowana jest aplikacja.' })).toBeVisible();
  await page.getByRole('button', { name: 'Zbuduj diagram architektury', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Komponent Fixture.App', exact: true })).toBeVisible();
  await expect(page.locator('.c4-component')).toHaveCount(3);
  await expect(page.getByRole('button', { name: 'Komponent Fixture.Tests', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Komponent Fixture.Core', exact: true }).click();
  const inspector = page.getByRole('complementary', { name: 'Szczegóły komponentu Fixture.Core' });
  await expect(inspector).toContainText('Retrieves orders from the local catalog.');
  await page.screenshot({ path: resolve(root, 'artifacts/e2e-architecture.png'), fullPage: true });
  await inspector.getByRole('tab', { name: 'Typy (1)', exact: true }).click();
  await inspector.locator('summary').filter({ hasText: 'OrderService' }).click();
  await inspector.getByRole('button', { name: 'string FindOrder(int id) L6', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Kod źródłowy' })).toBeVisible();
  await expect(page.locator('.source-line.highlighted')).toContainText('public string FindOrder');
  await expect(page.locator('.source-code')).toContainText('<img src=x onerror=alert(1)>');
  await expect(page.locator('.source-code img')).toHaveCount(0);
  await page.screenshot({ path: resolve(root, 'artifacts/e2e-architecture-source.png'), fullPage: true });
  await page.getByRole('button', { name: 'Zamknij', exact: true }).click();
  await page.getByRole('button', { name: 'Komponent Fixture.App', exact: true }).click();
  await page.getByRole('tab', { name: 'Zależności', exact: true }).click();
  await page.getByRole('button', { name: 'Dowód zależności Fixture.Core', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Dowód zależności' })).toContainText('Fixture.App → Fixture.Core');
  await page.getByRole('button', { name: 'Otwórz ProjectReference w kodzie', exact: true }).click();
  await expect(page.locator('.source-line.highlighted')).toContainText('ProjectReference Include="../Fixture.Core/Fixture.Core.csproj"');
  await page.getByRole('button', { name: 'Zamknij', exact: true }).click();
  const downloadEvent = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Eksport C4 DSL', exact: true }).click();
  const download = await downloadEvent;
  expect(download.suggestedFilename()).toBe('verificato-components.dsl');
  const dsl = await readFile((await download.path())!, 'utf8');
  expect(dsl).toContain('component application "Components"');
  expect(dsl).toContain('Fixture.Core'); expect(dsl).not.toContain('Fixture.Tests'); expect(dsl).not.toContain('Fixture.Worker');
  await page.getByLabel('Pokaż projekty testowe', { exact: true }).check();
  await expect(page.locator('.c4-component')).toHaveCount(5);
  await expect(page.getByRole('button', { name: 'Eksport C4 DSL', exact: true })).toBeDisabled();
  await page.getByLabel('Granica kontenera C4').selectOption({ label: 'Fixture.Worker' });
  await expect(page.locator('.c4-component')).toHaveCount(1);
  await page.getByLabel('Granica kontenera C4').selectOption({ label: 'Fixture.App' });
  await expect(page.locator('.c4-component')).toHaveCount(3);
  const projects = await (await page.request.get(`${origin}/api/v1/projects`)).json() as { id: string; name: string }[];
  const projectId = projects.find(p => p.name === 'Architecture Fixture')!.id;
  const snapshot = await (await page.request.get(`${origin}/api/v1/projects/${projectId}/architecture`)).json() as { id: string; components: { name: string; files: { id: string }[] }[] };
  expect((await page.request.get(`${origin}/api/v1/projects/${projectId}/architecture/${snapshot.id}/files/not-a-snapshot-file`)).status()).toBe(404);
  const coreFile = snapshot.components.find(c => c.name === 'Fixture.Core')!.files[0];
  const corePath = join(directory, 'architecture/Fixture.Core/OrderService.cs');
  await writeFile(corePath, (await readFile(corePath, 'utf8')) + '\n// changed after the snapshot');
  expect((await page.request.get(`${origin}/api/v1/projects/${projectId}/architecture/${snapshot.id}/files/${coreFile.id}?line=6`)).status()).toBe(409);
  await page.getByRole('button', { name: 'Komponent Fixture.Core', exact: true }).click();
  await page.getByRole('tab', { name: 'Pliki (1)', exact: true }).click();
  await page.getByRole('button', { name: /Fixture.Core\/OrderService.cs/ }).click();
  await expect(page.getByRole('dialog', { name: 'Kod źródłowy' }).getByRole('alert')).toContainText('Plik zmienił się od analizy');
  await page.getByRole('button', { name: 'Zamknij', exact: true }).click();
  await page.getByRole('button', { name: 'Odśwież diagram', exact: true }).click();
  await expect.poll(async () => (await (await page.request.get(`${origin}/api/v1/projects/${projectId}/architecture`)).json() as { id: string }).id).not.toBe(snapshot.id);
  const refreshed = await (await page.request.get(`${origin}/api/v1/projects/${projectId}/architecture`)).json() as { id: string };
  await page.reload();
  await page.getByRole('button', { name: 'Architektura C4', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Komponent Fixture.Core', exact: true })).toBeVisible();
  const saved = await (await page.request.get(`${origin}/api/v1/projects/${projectId}/architecture`)).json() as { id: string };
  expect(saved.id).toBe(refreshed.id);
  await page.getByRole('button', { name: 'Komponent Fixture.Core', exact: true }).click();
  await page.getByRole('button', { name: 'Włącz jasny motyw' }).click();
  await page.screenshot({ path: resolve(root, 'artifacts/e2e-architecture-light.png'), fullPage: true, animations: 'disabled' });
  await page.getByRole('button', { name: 'Włącz ciemny motyw' }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect.poll(() => page.locator('.react-flow__node-component').first().evaluate(node => {
    const box = node.getBoundingClientRect(), canvas = node.closest('.c4-diagram')!.getBoundingClientRect();
    return box.left >= canvas.left && box.right <= canvas.right;
  })).toBe(true);
  await page.screenshot({ path: resolve(root, 'artifacts/e2e-architecture-mobile.png'), fullPage: true, animations: 'disabled' });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  expect(browserErrors).toEqual([]);
});

test('local API rejects unauthenticated, cross-origin and replayed bootstrap requests', async ({ request, playwright }) => {
  expect((await request.get(`${origin}/api/v1/projects`)).status()).toBe(401);
  expect((await request.get(`${origin}/api/v1/projects/00000000-0000-0000-0000-000000000001/architecture`)).status()).toBe(401);
  expect((await request.get(`${origin}/api/v1/projects`, { headers: { Origin: 'https://evil.test' } })).status()).toBe(403);
  // A preceding failed test restarts the worker/host, so explicitly consume a fresh token if needed.
  await request.post(`${origin}/api/v1/auth/bootstrap`, { headers: { Origin: origin }, data: { token: bootstrap } });
  expect((await request.post(`${origin}/api/v1/auth/bootstrap`, { headers: { Origin: origin }, data: { token: bootstrap } })).status()).toBe(401);
  const anonymous = await playwright.request.newContext();
  expect((await anonymous.post(`${origin}/hubs/verification/negotiate?negotiateVersion=1`, { headers: { Origin: origin } })).status()).toBe(401);
  await anonymous.dispose();
});
