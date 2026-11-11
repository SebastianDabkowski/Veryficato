import { useState } from 'react';
import { ArrowDownLeft, ArrowUpRight, Box, Braces, FileCode2, FileText, GitBranch, Package, Search } from 'lucide-react';
import type { ArchitectureComponent, ArchitectureDocument, ArchitectureRelationship } from './types';
import { Button } from '../../components/ui/button';

type Tab = 'overview' | 'types' | 'files' | 'dependencies';
export function ComponentInspector({ component, document, onSelect, onSource, onRelation }: {
  component?: ArchitectureComponent; document: ArchitectureDocument; onSelect: (id: string) => void;
  onSource: (fileId: string, line: number) => void; onRelation: (relation: ArchitectureRelationship) => void;
}) {
  const [tab, setTab] = useState<Tab>('overview');
  const [query, setQuery] = useState('');
  if (!component) return <aside className="panel architecture-inspector"><div className="inspector-empty"><Box size={34} /><h2>Od architektury do kodu</h2><p>Wybierz komponent na diagramie. Zobaczysz jego projekty, typy, pliki i zależności — z dowodami w kodzie.</p><span>1 projekt C# = 1 komponent</span></div></aside>;
  const types = component.files.flatMap(file => file.types.map(type => ({ file, type })));
  const outgoing = document.relationships.filter(r => r.sourceId === component.id);
  const incoming = document.relationships.filter(r => r.targetId === component.id);
  const test = (value: string) => value.toLowerCase().includes(query.toLowerCase());
  return <aside className="panel architecture-inspector" aria-label={`Szczegóły komponentu ${component.name}`}>
    <div className="inspector-heading"><div className="inspector-icon"><Box size={20} /></div><span>[Component · {component.kind}]</span><h2>{component.name}</h2><p>{component.technology}</p></div>
    <div className="inspector-tabs" role="tablist" aria-label="Szczegóły komponentu">{([['overview', 'Opis'], ['types', `Typy (${types.length})`], ['files', `Pliki (${component.files.length})`], ['dependencies', 'Zależności']] as const).map(([id, title]) => <button role="tab" aria-selected={tab === id} onClick={() => setTab(id)} key={id}>{title}</button>)}</div>
    <div className="inspector-body" role="tabpanel">
      {tab === 'overview' ? <>
        <section><h3>Co wiemy z kodu</h3><p>{component.description}</p><small className="inspector-basis">Źródło opisu: {component.descriptionBasis}. Bez AI i zgadywania odpowiedzialności biznesowej.</small></section>
        <div className="inspector-metrics"><div><strong>{component.files.length}</strong><span>plików C#</span></div><div><strong>{types.length}</strong><span>typów</span></div><div><strong>{component.files.reduce((n, f) => n + f.lines, 0)}</strong><span>linii</span></div></div>
        <section><h3>Projekt źródłowy</h3><button className="inspector-file-link" onClick={() => onSource(component.projectFileId, 1)}><FileText size={15} />{component.projectFile}<ArrowUpRight size={13} /></button></section>
        <section><h3>Przestrzenie nazw</h3><div className="namespace-list">{component.namespaces.length ? component.namespaces.map(name => <code key={name}>{name}</code>) : <p>Globalna przestrzeń nazw lub brak deklaracji.</p>}</div></section>
        <section><h3>Kluczowe deklaracje</h3>{types.slice(0, 5).map(({ file, type }) => <button className="inspector-item" key={`${file.id}-${type.line}`} onClick={() => onSource(file.id, type.line)}><Braces size={15} /><span><strong>{type.name}</strong><small>{type.accessibility} {type.kind} · linia {type.line}</small></span><ArrowUpRight size={13} /></button>)}{!types.length && <p>Projekt może używać top-level statements lub kodu generowanego.</p>}</section>
      </> : <>
        <div className="inspector-search"><Search size={14} /><input aria-label="Szukaj w komponencie" value={query} onChange={e => setQuery(e.target.value)} placeholder={tab === 'types' ? 'Typ lub metoda…' : tab === 'files' ? 'Nazwa pliku…' : 'Projekt lub pakiet…'} /></div>
        {tab === 'types' && types.filter(({ type }) => test(`${type.fullName} ${type.members.map(m => m.name).join(' ')}`)).map(({ file, type }) => <details className="type-detail" key={`${file.id}-${type.line}`}><summary><Braces size={14} /><span><strong>{type.name}</strong><small>{type.accessibility} {type.kind} · {type.members.length} członków</small></span></summary><div className="type-content"><code>{type.fullName}</code>{type.summary && <p>{type.summary}</p>}{type.baseTypes.length > 0 && <small>Dziedziczy / implementuje: {type.baseTypes.join(', ')}</small>}<Button variant="outline" size="sm" onClick={() => onSource(file.id, type.line)}><FileCode2 size={13} /> Otwórz deklarację · L{type.line}</Button>{type.members.map((member, index) => <button className="member-link" key={`${member.line}-${index}`} onClick={() => onSource(file.id, member.line)} title={member.summary}><code>{member.signature}</code><span>L{member.line}</span></button>)}</div></details>)}
        {tab === 'files' && component.files.filter(file => test(file.path)).map(file => <button className="inspector-item" key={file.id} onClick={() => onSource(file.id, 1)}><FileCode2 size={16} /><span><strong>{file.path}</strong><small>{file.lines} linii · {file.types.length} deklaracji typów</small></span><ArrowUpRight size={13} /></button>)}
        {tab === 'dependencies' && <>
          <section><h3><ArrowUpRight size={13} /> Zależy od ({outgoing.length})</h3>{outgoing.map(r => <Dependency key={r.id} relation={r} targetId={r.targetId} document={document} test={test} onSelect={onSelect} onRelation={onRelation} />)}{!outgoing.length && <p>Brak jawnych ProjectReference.</p>}</section>
          <section><h3><ArrowDownLeft size={13} /> Używany przez ({incoming.length})</h3>{incoming.map(r => <Dependency key={r.id} relation={r} targetId={r.sourceId} document={document} test={test} onSelect={onSelect} onRelation={onRelation} />)}{!incoming.length && <p>Brak deklarowanych referencji przychodzących.</p>}</section>
          <section><h3><Package size={13} /> Pakiety NuGet ({component.packages.length})</h3>{component.packages.filter(p => test(p.name)).map((p, i) => <div className="package-item" key={`${p.name}-${i}`}><code>{p.name}</code><span>{p.version || 'wersja zarządzana / nieznana'}</span>{p.condition && <small>Warunkowo: {p.condition}</small>}</div>)}{!component.packages.length && <p>Brak jawnych PackageReference w tym .csproj.</p>}<small className="inspector-basis">Pakiety nie są komponentami C4. Brak ewaluacji centralnego zarządzania wersjami i importów.</small></section>
          {document.unresolvedReferences.filter(r => r.sourceId === component.id).map((r, i) => <div className="unresolved-reference" key={i}><strong>{r.include}</strong><p>{r.reason}</p><button onClick={() => onSource(component.projectFileId, r.line)}>Zobacz deklarację · L{r.line}</button></div>)}
        </>}
      </>}
    </div><div className="inspector-footer"><FileCode2 size={13} /> Tylko odczyt · migawka {document.id.slice(0, 8)}</div>
  </aside>;
}
function Dependency({ relation, targetId, document, test, onSelect, onRelation }: { relation: ArchitectureRelationship; targetId: string; document: ArchitectureDocument; test: (value: string) => boolean; onSelect: (id: string) => void; onRelation: (r: ArchitectureRelationship) => void }) {
  const name = document.components.find(c => c.id === targetId)?.name ?? targetId;
  if (!test(name)) return null;
  return <div className="dependency-item"><button onClick={() => onSelect(targetId)}><GitBranch size={14} /><span>{name}</span>{relation.condition && <small>warunkowo</small>}</button><button onClick={() => onRelation(relation)} aria-label={`Dowód zależności ${name}`}><FileCode2 size={14} /></button></div>;
}
