import { useEffect, useState } from 'react';
import { AlertTriangle, ChevronLeft, ChevronRight, FileCode2, LoaderCircle } from 'lucide-react';
import { Dialog } from '../../components/ui/dialog';
import { Button } from '../../components/ui/button';
import { api, message } from '../../lib/api';
import type { ArchitectureDocument, SourceCodePreview } from './types';

export function SourcePreview({ document, fileId, line, onClose }: { document: ArchitectureDocument; fileId: string; line: number; onClose: () => void }) {
  const [preview, setPreview] = useState<SourceCodePreview>();
  const [target, setTarget] = useState(line);
  const [error, setError] = useState('');
  useEffect(() => {
    const controller = new AbortController();
    setPreview(undefined); setError('');
    void api<SourceCodePreview>(`/projects/${document.projectId}/architecture/${document.id}/files/${encodeURIComponent(fileId)}?line=${target}`, { signal: controller.signal })
      .then(data => { if (!controller.signal.aborted) setPreview(data); })
      .catch(e => { if (!controller.signal.aborted) setError(message(e)); });
    return () => controller.abort();
  }, [document.id, document.projectId, fileId, target]);
  return <Dialog open onOpenChange={open => { if (!open) onClose(); }} title="Kod źródłowy" description={preview?.path ?? 'Lokalny, ograniczony podgląd pliku z migawki architektury.'} wide>
    <div className="source-privacy"><AlertTriangle size={14} /> Oryginalny kod może zawierać sekrety. Jest odczytywany lokalnie na żądanie; nie jest wysyłany do usług zewnętrznych.</div>
    {error ? <div className="inline-error" role="alert">{error}</div> : !preview ? <div className="source-loading"><LoaderCircle className="spin" size={22} /> Odczyt pliku…</div> : <>
      <div className="source-toolbar"><span><FileCode2 size={14} /> Linie {preview.startLine}–{preview.startLine + preview.lines.length - 1} / {preview.totalLines}</span><div><Button variant="ghost" size="icon" aria-label="Poprzednie linie" disabled={preview.startLine <= 1} onClick={() => setTarget(Math.max(1, preview.startLine - 110))}><ChevronLeft size={16} /></Button><Button variant="ghost" size="icon" aria-label="Następne linie" disabled={preview.startLine + preview.lines.length > preview.totalLines} onClick={() => setTarget(preview.startLine + 130)}><ChevronRight size={16} /></Button></div></div>
      <div className="source-code" aria-label="Podgląd kodu źródłowego"><pre>{preview.lines.map(row => <span className={`source-line ${row.number === line ? 'highlighted' : ''}`} key={row.number}><span className="source-line-number">{row.number}</span><code>{row.text || ' '}</code></span>)}</pre></div>
      <p className="source-fingerprint">SHA-256: {preview.hash.slice(0, 24)}… · zgodny z migawką · brak możliwości edycji</p>
    </>}
  </Dialog>;
}
