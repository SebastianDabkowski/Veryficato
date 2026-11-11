import { useState } from 'react';
import { Activity, AlertTriangle, ArrowUpRight, Check, CircleAlert, Clock3, FileCode2, Info, ListFilter, Pause, Play, Search, Terminal, X } from 'lucide-react';
import type { Issue, Observation, RunDetails, Severity } from '../lib/types';
import { categoryLabel, number, severityLabel, time } from '../lib/format';
import { Button } from '../components/ui/button';
import { Dialog } from '../components/ui/dialog';

type Selected = { title: string; message: string; category: string; severity: Severity; ruleId: string; url?: string; filePath?: string; line?: number; column?: number; stack?: string; occurrenceCount?: number };
export function SeverityIcon({ severity }: { severity: Severity }) {
  return severity === 'Warning' ? <AlertTriangle size={16} /> : severity === 'Error' || severity === 'Critical' ? <CircleAlert size={16} /> : <Info size={16} />;
}
export function RunPanel({ details, events, connection }: { details?: RunDetails; events: Observation[]; connection: string }) {
  const [tab, setTab] = useState<'events' | 'issues' | 'metrics'>('events');
  const [severity, setSeverity] = useState('all');
  const [category, setCategory] = useState('all');
  const [query, setQuery] = useState('');
  const [frozen, setFrozen] = useState<Observation[] | null>(null);
  const [selected, setSelected] = useState<Selected>();
  const shownEvents = frozen ?? events;
  const test = (item: Observation | Issue) => (severity === 'all' || item.severity === severity) &&
    (category === 'all' || item.category === category) &&
    `${item.title} ${item.message} ${item.url ?? ''} ${item.filePath ?? ''}`.toLowerCase().includes(query.toLowerCase());
  const filteredEvents = shownEvents.filter(test).slice().reverse();
  const issues = details?.issues.filter(test) ?? [];
  return <>
    <section className="panel event-panel" aria-label="Wyniki weryfikacji">
      <div className="panel-heading"><div className="panel-title"><Activity size={18} /><h2>Centrum weryfikacji</h2></div><span className="connection"><span className={details?.run.isActive ? 'live-dot' : 'status-dot'} />{details?.run.isActive ? connection : 'Zapisane wyniki'}</span></div>
      <div className="tabs" role="tablist" aria-label="Rodzaj wyników">
        <button role="tab" aria-selected={tab === 'events'} onClick={() => setTab('events')}>Zdarzenia <span>{details?.summary.eventCount ?? 0}</span></button>
        <button role="tab" aria-selected={tab === 'issues'} onClick={() => setTab('issues')}>Problemy <span>{details?.summary.issueCount ?? 0}</span></button>
        <button role="tab" aria-selected={tab === 'metrics'} onClick={() => setTab('metrics')}>Metryki <span>{details?.metrics.length ?? 0}</span></button>
      </div>
      {tab !== 'metrics' && <div className="event-toolbar"><div className="search-input"><Search size={15} /><input aria-label="Szukaj w wynikach" placeholder="Szukaj w wynikach…" value={query} onChange={e => setQuery(e.target.value)} />{query && <button aria-label="Wyczyść wyszukiwanie" onClick={() => setQuery('')}><X size={13} /></button>}</div>
        <div className="filter-controls"><ListFilter size={15} /><select aria-label="Istotność" value={severity} onChange={e => setSeverity(e.target.value)}><option value="all">Wszystkie poziomy</option>{Object.entries(severityLabel).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select>
          <select aria-label="Kategoria" value={category} onChange={e => setCategory(e.target.value)}><option value="all">Kategorie</option>{Object.entries(categoryLabel).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select>
          {tab === 'events' && <Button size="icon" variant="ghost" title={frozen ? 'Wznów widok' : 'Wstrzymaj widok (zbieranie trwa)'} aria-label={frozen ? 'Wznów widok' : 'Wstrzymaj widok'} onClick={() => setFrozen(frozen ? null : [...events])}>{frozen ? <Play size={15} /> : <Pause size={15} />}</Button>}
        </div></div>}
      {frozen && tab === 'events' && <div className="paused-note">Widok wstrzymany. Zbieranie danych trwa nadal.</div>}
      <div className="event-body" role="tabpanel">
        {tab === 'events' && (filteredEvents.length ? filteredEvents.map(item => <button className="event-row" key={item.id} onClick={() => setSelected(item)}>
          <span className={`severity-icon ${item.severity.toLowerCase()}`}><SeverityIcon severity={item.severity} /></span><span className="event-content"><span className="event-line"><strong>{item.title}</strong><span className="category-tag">{categoryLabel[item.category]}</span>{item.value != null && <span className="metric-inline">{number(item.value, item.unit === 'ms' ? ' ms' : '')}</span>}</span><span className="event-message">{item.url || item.filePath || item.message}</span></span><time>{time(item.occurredAt)}</time><ArrowUpRight size={14} className="row-arrow" />
        </button>) : <EmptyResults hasRun={!!details} filtered={!!query || severity !== 'all' || category !== 'all'} />)}
        {tab === 'issues' && (issues.length ? issues.map(item => <button className="event-row" key={item.id} onClick={() => setSelected(item)}><span className={`severity-icon ${item.severity.toLowerCase()}`}><SeverityIcon severity={item.severity} /></span><span className="event-content"><span className="event-line"><strong>{item.title}</strong><span className="category-tag">{item.ruleId}</span></span><span className="event-message">{item.filePath ? `${item.filePath}:${item.line ?? 1}` : item.message}</span></span><span className="occurrences">×{item.occurrenceCount}</span><ArrowUpRight size={14} /></button>) : <EmptyResults hasRun={!!details} filtered={!!query || severity !== 'all' || category !== 'all'} issues />)}
        {tab === 'metrics' && <div className="metrics-grid">{details?.metrics.length ? details.metrics.map(metric => <div className="metric-tile" key={metric.ruleId}><div><Activity size={15} />{metric.title}</div><strong>{number(metric.value)} <small>{metric.unit}</small></strong><p>{metric.url || metric.message}</p></div>) : <div className="empty-results"><Activity size={28} /><h3>Jeszcze bez pomiarów</h3><p>Metryki pojawią się po załadowaniu dokumentu lub zakończeniu analizy.</p></div>}</div>}
      </div>
      <div className="panel-footer"><span><Terminal size={13} />{tab === 'events' ? 'Ostatnie 1000 zdarzeń w widoku; pełna historia w lokalnej bazie' : tab === 'issues' ? 'Problemy zgrupowane według reguły i lokalizacji' : 'Pomiary laboratoryjne w zakresie tego wykonania'}</span><span>SQLite · lokalnie</span></div>
    </section>
    {selected && <Dialog open onOpenChange={open => { if (!open) setSelected(undefined); }} title={selected.title} description={`${categoryLabel[selected.category]} · ${severityLabel[selected.severity]} · ${selected.ruleId}`} wide>
      <div className="evidence-details"><div className={`evidence-severity ${selected.severity.toLowerCase()}`}><SeverityIcon severity={selected.severity} />{severityLabel[selected.severity]}{selected.occurrenceCount && <span>· {selected.occurrenceCount} wystąpień</span>}</div><pre>{selected.message}</pre>
        {selected.url && <div className="evidence-location"><GlobeIcon />{selected.url}</div>}
        {selected.filePath && <div className="evidence-location"><FileCode2 size={16} />{selected.filePath}:{selected.line ?? 1}:{selected.column ?? 1}</div>}
        {selected.stack && selected.stack !== selected.message && <><h3>Stos wywołań</h3><pre>{selected.stack}</pre></>}
        <p className="muted">Dowód został zapisany lokalnie po redakcji rozpoznanych sekretów. Treści żądań i formularzy nie są rejestrowane.</p>
      </div>
    </Dialog>}
  </>;
}
function GlobeIcon() { return <ArrowUpRight size={16} />; }
function EmptyResults({ hasRun, filtered, issues }: { hasRun: boolean; filtered: boolean; issues?: boolean }) {
  return <div className="empty-results">{filtered ? <Search size={28} /> : issues && hasRun ? <Check size={28} /> : <Clock3 size={28} />}<h3>{filtered ? 'Brak pasujących wyników' : !hasRun ? 'Gotowy na pierwszą weryfikację' : issues ? 'Brak wykrytych problemów' : 'Czekamy na zdarzenia'}</h3><p>{filtered ? 'Zmień wyszukiwanie lub wybrane filtry.' : !hasRun ? 'Rozpocznij sesję i korzystaj z aplikacji w kontrolowanym Chromium.' : issues ? 'Dotyczy wyłącznie zakresu tego wykonania, nie całej aplikacji.' : 'Korzystaj z aplikacji. Błędy, żądania i pomiary pojawią się tutaj.'}</p></div>;
}
