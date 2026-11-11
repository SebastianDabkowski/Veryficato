import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Box, Download, FileCode2, GitBranch, Layers3, LoaderCircle, Maximize2, RefreshCw, Search, ShieldCheck, Square } from 'lucide-react';
import { Button } from '../../components/ui/button';
import { Dialog } from '../../components/ui/dialog';
import { api, ApiError, message } from '../../lib/api';
import { date } from '../../lib/format';
import type { Project, Run } from '../../lib/types';
import type { Theme } from '../../lib/theme';
import type { ArchitectureDocument, ArchitectureRelationship } from './types';
import { structurizrDsl, visibleComponents } from './graph';
import { ComponentDiagram } from './ComponentDiagram';
import { ComponentInspector } from './ComponentInspector';
import { SourcePreview } from './SourcePreview';
import './architecture.css';

export function ArchitectureView({ project, latestAnalysis, busy, onAnalyze, onStop, theme }: {
  project: Project; latestAnalysis?: Run; busy: boolean; onAnalyze: () => void; onStop: (id: string) => void; theme: Theme;
}) {
  const [document, setDocument] = useState<ArchitectureDocument>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [containerId, setContainerId] = useState('');
  const [includeTests, setIncludeTests] = useState(false);
  const [query, setQuery] = useState('');
  const [selectedId, setSelectedId] = useState<string>();
  const [source, setSource] = useState<{ fileId: string; line: number }>();
  const [relation, setRelation] = useState<ArchitectureRelationship>();
  const graphRef = useRef<HTMLDivElement>(null);
  const active = latestAnalysis?.isActive === true;
  useEffect(() => {
    const controller = new AbortController();
    let working = false;
    async function load() {
      if (working) return;
      working = true;
      try {
        const data = await api<ArchitectureDocument>(`/projects/${project.id}/architecture`, { signal: controller.signal });
        if (controller.signal.aborted) return;
        setDocument(previous => previous?.id === data.id ? previous : data);
        setContainerId(previous => previous === 'all' || data.containers.some(c => c.id === previous) ? previous : data.containers[0]?.id ?? 'all');
        setError('');
      } catch (e) {
        if (controller.signal.aborted) return;
        if (!(e instanceof ApiError && e.status === 404)) setError(message(e));
      } finally { if (!controller.signal.aborted) setLoading(false); working = false; }
    }
    void load();
    const timer = active ? setInterval(() => void load(), 2000) : undefined;
    return () => { controller.abort(); if (timer) clearInterval(timer); };
  }, [project.id, active, latestAnalysis?.id, latestAnalysis?.state]);
  useEffect(() => { setSource(undefined); setRelation(undefined); }, [document?.id]);
  const components = useMemo(() => document ? visibleComponents(document, containerId, includeTests) : [], [document, containerId, includeTests]);
  const relationships = useMemo(() => {
    const ids = new Set(components.map(c => c.id));
    return document?.relationships.filter(r => ids.has(r.sourceId) && ids.has(r.targetId)) ?? [];
  }, [document, components]);
  const select = useCallback((id: string) => { setSelectedId(id); setRelation(undefined); }, []);
  const selected = document?.components.find(c => c.id === selectedId);
  const container = document?.containers.find(c => c.id === containerId);
  const onSource = (fileId: string, line: number) => setSource({ fileId, line });
  function exportDsl() {
    if (!document) return;
    try {
      const blob = new Blob([structurizrDsl(document, containerId)], { type: 'text/plain;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const link = window.document.createElement('a'); link.href = url; link.download = 'verificato-components.dsl'; link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) { setError(message(e)); }
  }
  async function fullscreen() {
    try { if (window.document.fullscreenElement) await window.document.exitFullscreen(); else await graphRef.current?.requestFullscreen(); }
    catch { setError('Przeglądarka nie zezwoliła na tryb pełnoekranowy.'); }
  }
  const nameOf = (id: string) => document?.components.find(c => c.id === id)?.name ?? id;
  return <>
    <section className="page-heading architecture-page-heading"><div><span className="eyebrow">OD KODU DO ARCHITEKTURY</span><h1>Architektura aplikacji <span className="project-label">C4 · C3</span></h1><p>Odkrywaj komponenty, śledź zależności i przechodź do kodu źródłowego.</p></div><div className="page-actions">
      <Button variant="outline" disabled={!document || !container || includeTests} onClick={exportDsl}><Download size={16} /> Eksport C4 DSL</Button>
      {active ? <Button variant="outline" disabled={busy} onClick={() => onStop(latestAnalysis!.id)}><Square size={14} /> Zatrzymaj analizę</Button> : <Button disabled={busy} onClick={onAnalyze}><RefreshCw size={16} />{document ? 'Odśwież diagram' : 'Zbuduj diagram'}</Button>}
    </div></section>
    {error && <div className="error-banner" role="alert">{error}</div>}
    {latestAnalysis?.state === 'Failed' && <div className="error-banner" role="alert">Analiza nie powiodła się: {latestAnalysis.failure}</div>}
    {active && <div className="architecture-progress" role="status"><LoaderCircle className="spin" size={17} /><span>Analiza kodu i budowanie diagramu… {document ? 'Do czasu zakończenia pokazujemy poprzednią migawkę.' : 'Skanujemy .csproj, typy i referencje.'}</span></div>}
    {loading && !document ? <div className="architecture-loading"><LoaderCircle className="spin" size={26} /> Odczyt lokalnej architektury…</div> : !document || !document.components.length ? <section className="panel architecture-empty"><div><Layers3 size={38} /></div><span className="eyebrow">TWÓJ KOD MA SWOJĄ MAPĘ</span><h2>Zobacz, jak zbudowana jest aplikacja.</h2><p>Verificato odczyta projekty C#, znajdzie referencje i deklaracje typów. Każdy projekt stanie się komponentem, który możesz otworzyć i poznać od środka.</p><div className="architecture-empty-steps"><span><Box size={17} /> Projekty → komponenty</span><span><GitBranch size={17} /> Referencje → relacje</span><span><FileCode2 size={17} /> Kliknięcie → kod</span></div><Button disabled={busy || active} onClick={onAnalyze}><Layers3 size={17} /> Zbuduj diagram architektury</Button>{document?.components.length === 0 && <p className="inline-error">Nie znaleziono bezpiecznie odczytanych .csproj. Sprawdź katalog projektu i zakres analizy.</p>}</section> : <>
      <div className="architecture-summary"><span><Box size={14} /> <strong>{components.length}</strong> komponentów</span><span><GitBranch size={14} /> <strong>{relationships.length}</strong> relacji</span><span><FileCode2 size={14} /> <strong>{components.reduce((n, c) => n + c.files.length, 0)}</strong> plików C#</span><span className="architecture-snapshot">Migawka {date(document.generatedAt)} · #{document.id.slice(0, 8)}</span></div>
      <div className="architecture-workspace" ref={graphRef}>
        <section className="panel architecture-map"><div className="architecture-toolbar"><div className="search-input"><Search size={15} /><input aria-label="Szukaj komponentu" placeholder="Znajdź komponent…" value={query} onChange={e => setQuery(e.target.value)} /></div><select aria-label="Granica kontenera C4" value={containerId} onChange={e => { setContainerId(e.target.value); setSelectedId(undefined); if (e.target.value !== 'all') setIncludeTests(false); }}>{document.containers.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}<option value="all">Wszystkie projekty · projekcja</option></select><Button variant="ghost" size="icon" onClick={() => void fullscreen()} aria-label="Pełny ekran diagramu"><Maximize2 size={16} /></Button></div>
          <div className="architecture-scope"><span><Layers3 size={13} />{container ? 'C3 · pojedynczy kontener (wywnioskowany)' : 'Przegląd repozytorium — nie pojedynczy kontener C3'}</span><label><input type="checkbox" checked={includeTests} onChange={e => { setIncludeTests(e.target.checked); if (e.target.checked) setContainerId('all'); }} /> Pokaż projekty testowe</label></div>
          {components.length ? <ComponentDiagram components={components} relationships={relationships} container={container} selectedId={selectedId} query={query} onSelect={select} onRelation={setRelation} theme={theme} /> : <div className="architecture-loading">Brak komponentów w wybranym zakresie.</div>}
          <div className="architecture-legend"><span><i className="legend-component" /> Komponent = projekt C#</span><span><i className="legend-edge" /> ProjectReference</span><span><i className="legend-conditional" /> Warunek MSBuild</span><span>Granica przerywana = do potwierdzenia</span></div>
        </section>
        <ComponentInspector key={`${document.id}-${selectedId}`} component={selected} document={document} onSelect={id => {
          // Dependencies outside the active inferred container remain navigable in the repository projection.
          if (!components.some(c => c.id === id)) { setContainerId('all'); if (document.components.find(c => c.id === id)?.isTest) setIncludeTests(true); }
          select(id);
        }} onSource={onSource} onRelation={setRelation} />
      </div>
      <details className="architecture-limitations"><summary><ShieldCheck size={15} /> Jak czytać ten diagram · zakres i ograniczenia ({document.warnings.length})</summary><ul>{document.warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul><p>Korzeń: <code>{document.sourceRoot}</code><br />Fingerprint źródeł: <code>{document.sourceFingerprint}</code></p></details>
    </>}
    {document && source && <SourcePreview key={`${document.id}-${source.fileId}-${source.line}`} document={document} fileId={source.fileId} line={source.line} onClose={() => setSource(undefined)} />}
    {document && relation && <Dialog open onOpenChange={open => { if (!open) setRelation(undefined); }} title="Dowód zależności" description={`${nameOf(relation.sourceId)} → ${nameOf(relation.targetId)}`}>
      <div className="relation-evidence"><GitBranch size={25} /><h3>{relation.description}</h3><p>{relation.technology}</p><code>{relation.evidenceFile}:{relation.line}</code>{relation.condition && <div className="warning-banner">Warunek (nieewaluowany): {relation.condition}</div>}<p className="muted">Strzałka wskazuje cel referencji zadeklarowanej w projekcie źródłowym. Nie oznacza potwierdzonego wywołania w czasie działania aplikacji.</p><Button variant="outline" onClick={() => { const component = document.components.find(c => c.id === relation.sourceId); if (component) { onSource(component.projectFileId, relation.line); setRelation(undefined); } }}><FileCode2 size={15} /> Otwórz ProjectReference w kodzie</Button></div>
    </Dialog>}
  </>;
}
