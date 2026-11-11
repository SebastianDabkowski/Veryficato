import { lazy, Suspense, useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { Activity, ArrowRight, ArrowUpRight, Braces, Check, ChevronDown, CircleAlert, Code2, Database, Folder, FolderGit2, Globe, History, Layers3, LayoutDashboard, LoaderCircle, LockKeyhole, Moon, MoreHorizontal, Play, Plus, Settings2, ShieldCheck, Square, Sun, Terminal, Trash2, TriangleAlert, X } from 'lucide-react';
import { Button } from './components/ui/button';
import { ProjectForm } from './features/ProjectForm';
import { RunPanel } from './features/RunPanel';
import { api, authenticate, message, post } from './lib/api';
import { date, duration, number, stateLabel, categoryLabel } from './lib/format';
import type { Project, ProjectInput, Run, SystemStatus } from './lib/types';
import { useRun } from './lib/use-run';
import { initialTheme, THEME_STORAGE_KEY, type Theme } from './lib/theme';

const ArchitectureView = lazy(() => import('./features/architecture/ArchitectureView').then(module => ({ default: module.ArchitectureView })));
type View = 'overview' | 'history' | 'analysis' | 'architecture' | 'settings';
export function App() {
  const [auth, setAuth] = useState<'loading' | 'ready' | 'failed'>('loading');
  const [projects, setProjects] = useState<Project[]>([]);
  const [selectedId, setSelectedId] = useState(localStorage.getItem('verificato-project') ?? '');
  const [runs, setRuns] = useState<Run[]>([]);
  const [runId, setRunId] = useState<string>();
  const [view, setView] = useState<View>('overview');
  const viewRef = useRef(view); viewRef.current = view;
  const [form, setForm] = useState<'new' | 'edit' | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [system, setSystem] = useState<SystemStatus>();
  const [theme, setTheme] = useState(() => initialTheme(localStorage));
  const { details, events, connection, error: runError } = useRun(auth === 'ready' ? runId : undefined);
  const project = projects.find(p => p.id === selectedId);
  const environment = details?.run.configuration ?? project;

  useEffect(() => {
    if (details) setRuns(old => old.map(run => run.id === details.run.id ? details.run : run));
  }, [details]);
  useEffect(() => { document.documentElement.dataset.theme = theme; localStorage.setItem(THEME_STORAGE_KEY, theme); }, [theme]);
  const loadProjects = useCallback(async () => {
    const data = await api<Project[]>('/projects');
    setProjects(data);
    setSelectedId(current => data.some(p => p.id === current) ? current : data[0]?.id ?? '');
  }, []);
  useEffect(() => {
    void authenticate().then(async () => {
      setAuth('ready'); await loadProjects(); setSystem(await api<SystemStatus>('/system/status'));
    }).catch(e => { setError(message(e)); setAuth('failed'); });
  }, [loadProjects]);
  useEffect(() => {
    if (auth !== 'ready' || !selectedId) { setRuns([]); setRunId(undefined); return; }
    localStorage.setItem('verificato-project', selectedId);
    let disposed = false;
    setRunId(undefined); setRuns([]);
    async function load() {
      try {
        const data = await api<Run[]>(`/projects/${selectedId}/runs?limit=100`);
        if (disposed) return;
        setRuns(data);
        const preferred = viewRef.current === 'analysis' ? data.find(r => r.kind === 'CodeAnalysis') : data.find(r => r.kind === 'Interactive') ?? data[0];
        setRunId(current => current && data.some(r => r.id === current) ? current : preferred?.id);
      } catch (e) { if (!disposed) setError(message(e)); }
    }
    void load();
    const timer = setInterval(() => void load(), 4000);
    return () => { disposed = true; clearInterval(timer); };
  }, [selectedId, auth]);
  const act = async (operation: () => Promise<void>) => { setBusy(true); setError(''); try { await operation(); } catch (e) { setError(message(e)); } finally { setBusy(false); } };
  async function saveProject(input: ProjectInput) {
    const saved = form === 'edit' && project ? await api<Project>(`/projects/${project.id}`, { method: 'PATCH', body: JSON.stringify(input) }) : await post<Project>('/projects', input);
    await loadProjects(); setSelectedId(saved.id);
  }
  const start = (kind: 'Interactive' | 'CodeAnalysis', nextView?: View) => void act(async () => {
    if (!project) return;
    const run = await post<Run>(`/projects/${project.id}/${kind === 'Interactive' ? 'browser-sessions' : 'analysis-runs'}`, undefined, true);
    setRuns(old => [run, ...old.filter(r => r.id !== run.id)]); setRunId(run.id); setView(nextView ?? (kind === 'Interactive' ? 'overview' : 'analysis'));
  });
  const stop = (id: string) => void act(async () => { await post(`/runs/${id}/stop`); setRuns(await api<Run[]>(`/projects/${selectedId}/runs?limit=100`)); });
  const deleteRun = (id: string) => {
    if (!confirm('Usunąć to wykonanie i wszystkie jego wyniki? Tej operacji nie można cofnąć.')) return;
    void act(async () => { await api(`/runs/${id}`, { method: 'DELETE' }); setRuns(old => old.filter(x => x.id !== id)); if (runId === id) setRunId(undefined); });
  };
  const navigate = (next: View) => {
    setView(next);
    if (next === 'analysis') setRunId(runs.find(r => r.kind === 'CodeAnalysis')?.id);
    if (next === 'overview') setRunId(runs.find(r => r.kind === 'Interactive')?.id ?? runs[0]?.id);
  };
  const activeBrowser = runs.find(r => r.kind === 'Interactive' && r.isActive);
  const activeAnalysis = runs.find(r => r.kind === 'CodeAnalysis' && r.isActive);

  if (auth !== 'ready') return <div className="welcome-screen"><Logo /><div className="welcome-card">{auth === 'loading' ? <><LoaderCircle size={28} className="spin" /><h1>Twoje lokalne centrum jakości</h1><p>Łączenie z Verificato…</p></> : <><LockKeyhole size={32} /><h1>Bezpieczna sesja lokalna</h1><p>{error}</p><div className="privacy-note">Uruchom Verificato z launchera. Otworzy on jednorazowy link uwierzytelniający. Istniejąca historia pozostaje na dysku.</div></>}</div><span className="muted">Open source. Local first. Bez konta.</span></div>;

  return <div className="app-shell">
    <aside className="sidebar"><Logo /><div className="workspace-label">TWOJE ŚRODOWISKO <span>LOCAL</span></div>
      <div className="sidebar-project"><div className="project-avatar">{project?.name.slice(0, 1).toUpperCase() || 'V'}</div><div><strong>{project?.name || 'Brak projektu'}</strong><small>Osobista przestrzeń</small></div><ChevronDown size={14} /></div>
      <nav aria-label="Główna nawigacja"><Nav active={view === 'overview'} icon={<LayoutDashboard size={18} />} onClick={() => navigate('overview')}>Przegląd projektu</Nav><Nav active={view === 'history'} icon={<History size={18} />} onClick={() => navigate('history')}>Historia weryfikacji <span className="nav-count">{runs.length}</span></Nav><Nav active={view === 'analysis'} icon={<Code2 size={18} />} onClick={() => navigate('analysis')}>Analiza kodu</Nav><Nav active={view === 'architecture'} icon={<Layers3 size={18} />} onClick={() => navigate('architecture')}>Architektura C4</Nav></nav>
      <div className="sidebar-section-title">PROJEKTY <button onClick={() => setForm('new')} aria-label="Dodaj projekt"><Plus size={16} /></button></div>
      <div className="project-list">{projects.map(p => <button className={p.id === selectedId ? 'selected' : ''} key={p.id} onClick={() => { setSelectedId(p.id); setView('overview'); }}><FolderGit2 size={16} /><span>{p.name}</span>{p.id === selectedId && <span className="project-selected-dot" />}</button>)}{!projects.length && <p className="sidebar-empty">Dodaj pierwszy projekt, aby zacząć.</p>}</div>
      <button className="add-project" onClick={() => setForm('new')}><Plus size={16} /> Nowy projekt</button>
      <div className="sidebar-bottom"><div className="local-card"><ShieldCheck size={20} /><strong>Twój kod. Twoje urządzenie.</strong><p>Wyniki zostają lokalnie.<br />Bez kont i usług chmurowych.</p><span><span className="live-dot" /> Local-first</span></div><Nav active={view === 'settings'} icon={<Settings2 size={18} />} onClick={() => setView('settings')}>Ustawienia lokalne</Nav><div className="sidebar-footer"><span>Verificato <small>v{system?.version ?? '0.1.0'}</small></span><span>OPEN SOURCE</span></div></div>
    </aside>
    <div className="main-shell"><header className="topbar"><div><span>Workspace</span><span className="breadcrumb-slash">/</span><Folder size={14} /><strong>{project?.name || 'Projekty'}</strong></div><div><span className="local-pill"><span className="live-dot" /> Działa lokalnie</span><span className="topbar-divider" /><Button variant="ghost" size="icon" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')} aria-label={theme === 'dark' ? 'Włącz jasny motyw' : 'Włącz ciemny motyw'}>{theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}</Button></div></header>
    <main>
      {(error || runError) && <div className="error-banner" role="alert"><CircleAlert size={18} /><span>{error || runError}</span>{error && <button aria-label="Zamknij komunikat" onClick={() => setError('')}><X size={16} /></button>}</div>}
      {view === 'architecture' && project ? <Suspense fallback={<div className="empty-results"><LoaderCircle className="spin" size={24} />Ładowanie widoku architektury…</div>}><ArchitectureView key={project.id} project={project} latestAnalysis={runs.find(r => r.kind === 'CodeAnalysis')} busy={busy} onAnalyze={() => start('CodeAnalysis', 'architecture')} onStop={stop} theme={theme} /></Suspense> : view === 'settings' ? <Settings status={system} theme={theme} onTheme={setTheme} /> : !project ? <div className="onboarding"><div className="onboarding-icon"><ShieldCheck size={42} /></div><span className="eyebrow">WERYFIKUJ Z PEWNOŚCIĄ</span><h1>Jakość zaczyna się<br />od pierwszej sesji.</h1><p>Przeklikaj swoją aplikację. Verificato zbierze błędy i metryki,<br />a kod i wyniki pozostaną na Twoim komputerze.</p><Button onClick={() => setForm('new')}><Plus size={17} /> Utwórz pierwszy projekt <ArrowRight size={16} /></Button><div className="onboarding-steps">{[['01', 'Wskaż projekt', 'Katalog źródeł i URL aplikacji'], ['02', 'Testuj ręcznie', 'W izolowanym oknie Chromium'], ['03', 'Zobacz wyniki', 'Błędy i metryki na żywo']].map(([n, title, text]) => <div key={n}><span>{n}</span><h3>{title}</h3><p>{text}</p></div>)}</div></div> : <>
        <section className="page-heading"><div><span className="eyebrow">{view === 'analysis' ? 'ANALIZA TYLKO DO ODCZYTU' : view === 'history' ? 'HISTORIA PROJEKTU' : 'PRZEGLĄD PROJEKTU'}</span><h1>{view === 'history' ? 'Historia weryfikacji' : view === 'analysis' ? 'Spójrz głębiej w kod.' : project.name}<span className="project-label">LOCAL</span></h1><p>{view === 'analysis' ? 'Diagnostyka Roslyn i podstawowe metryki. Bez wykonywania kodu projektu.' : view === 'history' ? 'Każde wykonanie, jego zakres i zebrane dowody. Zapisane na Twoim urządzeniu.' : 'Sprawdzaj działanie. Wykrywaj problemy. Buduj z pewnością.'}</p></div><div className="page-actions"><Button variant="outline" size="icon" aria-label="Edytuj projekt" onClick={() => setForm('edit')}><MoreHorizontal size={19} /></Button>{activeAnalysis ? <Button variant="outline" disabled={busy} onClick={() => stop(activeAnalysis.id)}><Square size={14} /> Zatrzymaj analizę</Button> : <Button variant="outline" disabled={busy} onClick={() => start('CodeAnalysis')}><Braces size={17} /> Analizuj kod</Button>}{activeBrowser ? <Button variant="destructive" disabled={busy} onClick={() => stop(activeBrowser.id)}><Square size={14} /> Zatrzymaj sesję</Button> : <Button disabled={busy || system?.browserInstalled === false} onClick={() => start('Interactive')}>{busy ? <LoaderCircle size={16} className="spin" /> : <Play size={16} />} Rozpocznij sesję</Button>}</div></section>
        {system?.browserInstalled === false && <div className="warning-banner"><TriangleAlert size={18} /><span>Chromium nie jest zainstalowane. Uruchom <code>scripts/install-browser.ps1</code> lub <code>scripts/install-browser.sh</code>, a następnie odśwież stronę.</span></div>}
        {view === 'history' ? <HistoryTable runs={runs} selectedId={runId} onSelect={id => { setRunId(id); setView('overview'); }} onDelete={deleteRun} /> : <>
          <div className="run-context"><div><span className={details?.run.isActive ? 'live-dot' : 'status-dot'} /><strong>{details ? stateLabel[details.run.state] : 'Gotowy do weryfikacji'}</strong>{details && <><span className="context-separator">·</span><span>{details.run.kind === 'Interactive' ? 'Sesja interaktywna' : 'Analiza kodu'}</span><span className="context-separator">·</span><span className="mono">{duration(details.run.startedAt, details.run.endedAt)}</span>{details.run.completeness !== 'CompleteWithinScope' && <span className="partial-badge">Niepełny zakres</span>}</>}</div><select aria-label="Wybierz wykonanie" value={runId ?? ''} onChange={e => setRunId(e.target.value || undefined)}><option value="">Wybierz wykonanie</option>{runs.map(run => <option value={run.id} key={run.id}>{run.kind === 'Interactive' ? 'Przeglądarka' : 'Kod'} · {date(run.startedAt)} · {stateLabel[run.state]}</option>)}</select></div>
          {details?.run.failure && <div className="error-banner"><InfoIcon /><span>{details.run.failure}</span></div>}
          {details?.run.limitations && <div className={details.run.completeness === 'CompleteWithinScope' ? 'scope-note' : 'warning-banner'}><ShieldCheck size={17} /><span>{details.run.limitations}</span></div>}
          <div className="stats-grid"><Stat title="Wykryte błędy" value={details?.summary.errorCount} icon={<CircleAlert size={18} />} tone="red" note="Unikalne problemy · error / critical" /><Stat title="Ostrzeżenia" value={details?.summary.warningCount} icon={<TriangleAlert size={18} />} tone="amber" note="Warto przyjrzeć się bliżej" /><Stat title={details?.run.kind === 'CodeAnalysis' ? 'Przeanalizowane pliki' : 'Żądania HTTP'} value={details?.run.kind === 'CodeAnalysis' ? details.metrics.find(m => m.ruleId === 'code.files')?.value : details?.summary.requestCount} icon={details?.run.kind === 'CodeAnalysis' ? <Code2 size={18} /> : <Globe size={18} />} tone="blue" note="Wyłącznie wskazany zakres" /><Stat title={details?.run.kind === 'CodeAnalysis' ? 'Fizyczne linie kodu' : 'Czas odpowiedzi · p95'} value={details?.run.kind === 'CodeAnalysis' ? details.metrics.find(m => m.ruleId === 'code.loc')?.value : details?.summary.p95ResponseMs} suffix={details?.run.kind === 'CodeAnalysis' ? '' : ' ms'} icon={<Activity size={18} />} tone="accent" note={details?.run.kind === 'CodeAnalysis' ? 'Z komentarzami i pustymi liniami' : `Mediana: ${number(details?.summary.medianResponseMs, ' ms')} · próba ${number(details?.summary.measuredRequestCount)}`} /></div>
          <div className="workspace-grid"><RunPanel key={runId ?? 'empty'} details={details} events={events} connection={connection} /><aside className="details-column"><section className="panel environment-panel"><div className="panel-heading"><h2>{details ? 'Zakres tego wykonania' : 'Środowisko projektu'}</h2><ShieldCheck size={17} className="accent-text" /></div><div className="environment-field"><span><Globe size={14} /> Aplikacja</span><code>{environment?.applicationUrl}</code></div><div className="environment-field"><span><Folder size={14} /> Kod źródłowy</span><code>{environment?.sourcePath}</code></div><div className="environment-field"><span><LockKeyhole size={14} /> Dozwolone originy</span><div className="origin-list">{environment?.allowedOrigins.map(origin => <code key={origin}>{origin}</code>)}</div></div><div className="environment-footer"><ShieldCheck size={15} /> Czysty profil na każdą sesję</div></section>
          <section className="panel categories-panel"><div className="panel-heading"><h2>Problemy wg kategorii</h2><span className="count-pill">{details?.summary.issueCount ?? 0}</span></div>{details?.issues.length ? <div className="category-bars">{Object.entries(categoryLabel).map(([category, label]) => { const count = details.issues.filter(i => i.category === category).length; return count ? <div key={category}><div><span>{label}</span><strong>{count}</strong></div><div className="bar-track"><span style={{ width: `${Math.max(4, count / details.summary.issueCount * 100)}%` }} /></div></div> : null; })}</div> : <div className="category-empty"><Check size={24} /><p>{details ? 'Bez wykrytych problemów' : 'Jeszcze bez wyników'}</p><small>Wynik dotyczy tylko badanego zakresu.</small></div>}</section>
          <div className="tip-card"><Terminal size={19} /><div><strong>Testujesz Ty. Obserwuje Verificato.</strong><p>Ustaw okno Chromium obok panelu. Przechodź między widokami i sprawdzaj formularze — zdarzenia pojawią się automatycznie.</p></div></div></aside></div>
          <div className="section-heading"><div><History size={17} /><h2>Ostatnie weryfikacje</h2></div><Button variant="ghost" size="sm" onClick={() => setView('history')}>Zobacz wszystkie <ArrowRight size={14} /></Button></div><HistoryTable runs={runs.slice(0, 5)} selectedId={runId} onSelect={setRunId} onDelete={deleteRun} />
        </>}
        <div className="project-danger"><Button size="sm" variant="ghost" onClick={() => { if (confirm(`Usunąć projekt „${project.name}” i całą jego historię? Źródła pozostaną bez zmian.`)) void act(async () => { await api(`/projects/${project.id}`, { method: 'DELETE' }); await loadProjects(); }); }} disabled={busy || !!activeBrowser || !!activeAnalysis}><Trash2 size={13} /> Usuń projekt i historię</Button></div>
      </>}
      <footer className="main-footer"><span><ShieldCheck size={13} /> Prywatność z założenia. Weryfikacja lokalnie.</span><span>Verificato · Open Source Software Verification</span></footer>
    </main></div>
    {form && <ProjectForm key={`${form}-${project?.id}`} project={form === 'edit' ? project : undefined} onClose={() => setForm(null)} onSave={saveProject} />}
  </div>;
}
function Logo() { return <div className="logo"><div className="logo-mark"><Check size={24} strokeWidth={3} /></div><strong>verificato<span>.</span></strong></div>; }
function Nav({ children, icon, active, onClick }: { children: ReactNode; icon: ReactNode; active: boolean; onClick: () => void }) { return <button className={`nav-item ${active ? 'active' : ''}`} onClick={onClick}>{icon}{children}</button>; }
function InfoIcon() { return <CircleAlert size={17} />; }
function Stat({ title, value, suffix = '', icon, tone, note }: { title: string; value?: number | null; suffix?: string; icon: ReactNode; tone: string; note: string }) { return <div className="stat-card"><div className="stat-top"><span>{title}</span><span className={`stat-icon ${tone}`}>{icon}</span></div><strong className="stat-value">{number(value)}{value != null && suffix && <small>{suffix}</small>}</strong><p>{note}</p></div>; }
function HistoryTable({ runs, selectedId, onSelect, onDelete }: { runs: Run[]; selectedId?: string; onSelect: (id: string) => void; onDelete: (id: string) => void }) {
  return <section className="panel history-panel">{runs.length ? <div className="table-scroll"><table><thead><tr><th>Wykonanie</th><th>Rozpoczęto</th><th>Czas</th><th>Status</th><th>Wynik</th><th><span className="sr-only">Akcje</span></th></tr></thead><tbody>{runs.map(run => <tr key={run.id} className={run.id === selectedId ? 'selected-row' : ''}><td><button className="run-link" onClick={() => onSelect(run.id)}>{run.kind === 'Interactive' ? <Globe size={16} /> : <Code2 size={16} />}<span>{run.kind === 'Interactive' ? 'Sesja interaktywna' : 'Analiza C#'}<small>#{run.id.slice(0, 8)}</small></span></button></td><td>{date(run.startedAt)}</td><td className="mono">{duration(run.startedAt, run.endedAt)}</td><td><span className={`run-badge ${run.state.toLowerCase()}`}>{run.isActive && <span className="live-dot" />}{stateLabel[run.state]}</span></td><td><span className={`outcome ${run.outcome}`}>{run.isActive ? 'Zbieranie danych' : run.outcome === 'IssuesDetected' ? 'Wykryto problemy' : run.outcome === 'NoIssuesDetected' ? 'Bez wykrytych problemów' : 'Nierozstrzygający'}</span>{run.completeness !== 'CompleteWithinScope' && <small className="partial-text">Niepełny zakres</small>}</td><td><Button variant="ghost" size="icon" disabled={run.isActive} aria-label={`Usuń wykonanie ${run.id.slice(0, 8)}`} onClick={() => onDelete(run.id)}><Trash2 size={14} /></Button></td></tr>)}</tbody></table></div> : <div className="history-empty"><History size={21} /><span>Tu pojawią się Twoje weryfikacje.</span><span>Każda sesja i analiza są zapisywane automatycznie.</span></div>}</section>;
}
function Settings({ status, theme, onTheme }: { status?: SystemStatus; theme: Theme; onTheme: (value: Theme) => void }) {
  return <><section className="page-heading"><div><span className="eyebrow">TWOJE URZĄDZENIE</span><h1>Ustawienia lokalne</h1><p>Bez konta. Bez synchronizacji. Pod Twoją kontrolą.</p></div></section><div className="settings-grid"><section className="panel settings-panel"><h2><Database size={19} /> Dane i środowisko</h2><dl><dt>Wersja Verificato</dt><dd>{status?.version ?? '—'}</dd><dt>Katalog danych</dt><dd className="mono">{status?.dataDirectory ?? '—'}</dd><dt>Chromium</dt><dd>{status?.browserInstalled ? 'Zainstalowane' : 'Wymaga instalacji — scripts/install-browser.ps1'}</dd><dt>Analiza kodu</dt><dd>{status?.analysisMode ?? '—'}</dd><dt>Limity sesji</dt><dd>{status?.maxSessionMinutes} min · {number(status?.maxObservations)} obserwacji</dd></dl><div className="privacy-note"><ShieldCheck size={18} /><p>Historia trafia do SQLite. Usuwaj wyniki w historii projektu. Kopię bazy wykonuj przy wyłączonym hoście.</p></div></section><section className="panel settings-panel"><h2><Sun size={19} /> Wygląd</h2><p className="muted">Wybierz motyw interfejsu.</p><div className="theme-buttons"><Button variant={theme === 'light' ? 'default' : 'outline'} onClick={() => onTheme('light')}><Sun size={17} /> Jasny</Button><Button variant={theme === 'dark' ? 'default' : 'outline'} onClick={() => onTheme('dark')}><Moon size={17} /> Ciemny</Button></div><h2 className="settings-privacy"><LockKeyhole size={19} /> Prywatność</h2><p className="muted">Verificato nie wysyła wyników do usług zewnętrznych. Sama testowana aplikacja może korzystać z sieci. Dowody nie zawierają body żądań, nagrań formularzy ani cookies.</p><p className="muted">Analiza źródeł nie wykonuje MSBuild, restore, generatorów ani analizatorów z badanego projektu. Nietypowe projekty są oznaczane jako częściowo przeanalizowane.</p><Button variant="ghost" onClick={() => void post('/auth/logout').then(() => location.reload())}><ArrowUpRight size={15} /> Zakończ sesję dostępu do panelu</Button></section></div></>;
}
