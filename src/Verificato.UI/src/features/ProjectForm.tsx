import { useState, type FormEvent } from 'react';
import { Folder, Globe, LoaderCircle, ShieldCheck } from 'lucide-react';
import type { Project, ProjectInput } from '../lib/types';
import { message } from '../lib/api';
import { Button } from '../components/ui/button';
import { Dialog } from '../components/ui/dialog';

export function ProjectForm({ project, onClose, onSave }: { project?: Project; onClose: () => void; onSave: (input: ProjectInput) => Promise<void> }) {
  const [name, setName] = useState(project?.name ?? '');
  const [sourcePath, setPath] = useState(project?.sourcePath ?? '');
  const [applicationUrl, setUrl] = useState(project?.applicationUrl ?? 'http://localhost:5173');
  const [origins, setOrigins] = useState(project?.allowedOrigins.filter(x => x !== new URL(project.applicationUrl).origin).join('\n') ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function submit(e: FormEvent) {
    e.preventDefault(); setBusy(true); setError('');
    try { await onSave({ name, sourcePath, applicationUrl, allowedOrigins: origins.split(/[\n,]/).map(x => x.trim()).filter(Boolean) }); onClose(); }
    catch (e) { setError(message(e)); }
    finally { setBusy(false); }
  }
  return <Dialog open onOpenChange={open => { if (!open && !busy) onClose(); }} title={project ? 'Ustawienia projektu' : 'Nowy projekt'} description="Wskaż źródła i uruchomioną aplikację. Wszystkie wyniki pozostaną na tym urządzeniu.">
    <form onSubmit={e => void submit(e)} className="project-form">
      <label>Nazwa projektu<input autoFocus required maxLength={120} value={name} onChange={e => setName(e.target.value)} placeholder="np. Customer Portal" /></label>
      <label><span><Folder size={14} /> Katalog kodu źródłowego</span><input required value={sourcePath} onChange={e => setPath(e.target.value)} placeholder="C:\\Projects\\CustomerPortal" className="mono" /><small>Bezwzględna ścieżka na komputerze hosta. Analiza nie zmienia plików.</small></label>
      <label><span><Globe size={14} /> Adres uruchomionej aplikacji</span><input required type="url" value={applicationUrl} onChange={e => setUrl(e.target.value)} className="mono" /><small>Uruchom aplikację samodzielnie. URL bez tokenów i parametrów.</small></label>
      <label>Dodatkowe dozwolone originy <span className="optional">opcjonalnie</span><textarea value={origins} onChange={e => setOrigins(e.target.value)} rows={2} placeholder="http://localhost:5000" className="mono" /><small>Osobny backend lub logowanie: jeden origin w wierszu (schemat, host, port).</small></label>
      <div className="privacy-note"><ShieldCheck size={17} /><p>Izolowany profil Chromium. Bez konta i wysyłania wyników do chmury. Zewnętrzne zasoby aplikacji mogą korzystać z sieci.</p></div>
      {error && <div role="alert" className="inline-error">{error}</div>}
      <div className="dialog-actions"><Button type="button" variant="outline" onClick={onClose} disabled={busy}>Anuluj</Button><Button disabled={busy}>{busy && <LoaderCircle size={16} className="spin" />}{project ? 'Zapisz zmiany' : 'Utwórz projekt'}</Button></div>
    </form>
  </Dialog>;
}
