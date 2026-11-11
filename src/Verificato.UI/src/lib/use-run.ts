import { useEffect, useState } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { api, message } from './api';
import type { EventPage, Observation, RunDetails } from './types';

export function useRun(id: string | undefined) {
  const [details, setDetails] = useState<RunDetails>();
  const [events, setEvents] = useState<Observation[]>([]);
  const [connection, setConnection] = useState('Łączenie');
  const [error, setError] = useState('');
  useEffect(() => {
    setDetails(undefined); setEvents([]); setError('');
    if (!id) { setConnection('Gotowy'); return; }
    let disposed = false, busy = false, cursor = 0;
    const controller = new AbortController();
    const hub = new HubConnectionBuilder().withUrl('/hubs/verification').withAutomaticReconnect()
      .configureLogging(LogLevel.None).build();
    async function sync() {
      if (disposed || busy) return;
      busy = true;
      try {
        let more = true;
        while (more && !disposed) {
          const page = await api<EventPage>(`/runs/${id}/events?afterSequence=${cursor}&limit=500`, { signal: controller.signal });
          cursor = page.nextSequence; more = page.hasMore;
          if (!disposed && page.items.length) setEvents(old => [...old, ...page.items].slice(-1000));
        }
        const result = await api<RunDetails>(`/runs/${id}`, { signal: controller.signal });
        if (!disposed) { setDetails(result); setError(''); }
      } catch (e) { if (!disposed) setError(message(e)); }
      finally { busy = false; }
    }
    async function connect() {
      try {
        await hub.start();
        if (disposed) { await hub.stop(); return; }
        await hub.invoke('Subscribe', id);
        setConnection('Na żywo');
        await sync();
      } catch { if (!disposed) setConnection('Odczyt REST'); }
    }
    hub.on('ObservationsAvailable', () => void sync());
    hub.on('RunStateChanged', () => void sync());
    hub.onreconnecting(() => { if (!disposed) setConnection('Ponowne łączenie'); });
    hub.onreconnected(() => { void hub.invoke('Subscribe', id).then(() => { if (!disposed) setConnection('Na żywo'); return sync(); }).catch(() => { if (!disposed) setConnection('Odczyt REST'); }); });
    hub.onclose(() => { if (!disposed) setConnection('Odczyt REST'); });
    void connect();
    void sync();
    const interval = setInterval(() => void sync(), 2000);
    return () => { disposed = true; controller.abort(); clearInterval(interval); void hub.stop(); };
  }, [id]);
  return { details, events, connection, error };
}
