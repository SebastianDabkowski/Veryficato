# REST i SignalR — v1

UI i API są serwowane z tego samego lokalnego originu. Dokument opisuje aktualny kod, nie wszystkie endpointy z planu.

## Dostęp

Launcher przekazuje sekret we fragmencie URL. UI wykonuje `POST /api/v1/auth/bootstrap` z JSON `{ "token": "..." }` i poprawnym nagłówkiem `Origin`. Odpowiedź `{ "csrf": "..." }` ustawia host-only cookie `verificato-session` (HttpOnly, SameSite=Strict, 12 h). Sekret bootstrap jest jednorazowy i wygasa po 10 min.

Każde API/hub poza bootstrap wymaga cookie. `GET /api/v1/auth/session` zwraca CSRF dla istniejącej sesji. Mutacje REST wymagają poprawnego `Origin` i `X-Verificato-CSRF`. `POST /api/v1/auth/logout` usuwa sesję. Nie przechowuj sekretów w localStorage ani query string.

Nie ma globalnego CORS ani anonimowego endpointu ze stanem instalacji. Host akceptuje tylko właściwy lokalny Host i zatwierdzony origin (w development również origin proxy Vite).

## Projekty

- `GET /api/v1/projects` → tablica projektów.
- `POST /api/v1/projects` → `201` i projekt.
- `GET /api/v1/projects/{id}` → projekt lub `404`.
- `PATCH /api/v1/projects/{id}` → zapis pełnego formularza lub `409`, gdy trwa wykonanie projektu.
- `DELETE /api/v1/projects/{id}` → `204`; usuwa też historię, ale nigdy źródła. Aktywny projekt → `409`.

Formularz:

```json
{
  "name": "Customer Portal",
  "sourcePath": "C:\\Projects\\CustomerPortal",
  "applicationUrl": "http://localhost:5173",
  "allowedOrigins": ["http://localhost:5000"]
}
```

Origin `applicationUrl` jest automatycznie dodawany do zakresu. `applicationUrl` nie może zawierać credentials, query ani fragmentu. Maks. 20 originów, nazwa 1–120 znaków. Ścieżka musi być istniejącym, bezwzględnym katalogiem bez przechodzenia przez reparse points/symlinki.

## Uruchomienia i raporty

- `POST /api/v1/projects/{id}/browser-sessions` → `202`, obiekt `Run`, nagłówek Location.
- `POST /api/v1/projects/{id}/analysis-runs` → analogicznie.
- Start przyjmuje opcjonalny `Idempotency-Key` do 128 znaków. Pamięć kluczy jest ograniczona do 1000 poleceń w bieżącej instancji; nie jest trwała między restartami.
- `GET /api/v1/projects/{id}/runs?limit=50` → tablica ostatnich uruchomień, maks. 200.
- `GET /api/v1/projects/{id}/dashboard` → `{ interactive, analysis }`, osobne ostatnie raporty.
- `GET /api/v1/runs/{id}` → `{ run, summary, issues, metrics }` lub `404`.
- `POST /api/v1/runs/{id}/stop` → idempotentne `202`, `404` dla nieistniejącego wykonania.
- `DELETE /api/v1/runs/{id}` → `204`; aktywne wykonanie → `409`.
- `GET /api/v1/runs/{id}/issues` → tablica zgrupowanych problemów.
- `GET /api/v1/runs/{id}/metrics` → `{ metrics, summary }`.
- `GET /api/v1/system/status` → wersja, dostępność Chromium, katalog danych i limity.

Nie ma jeszcze odrębnego endpointu plikowych dowodów obserwacji ani eksportu raportu. Dowody tekstowe są polami obserwacji/problemów.

`Run.kind`: `Interactive` / `CodeAnalysis`. `Run.state`: `Starting`, `Running`, `Stopping`, `Completed`, `Failed`, `Interrupted`. Błąd aplikacji docelowej tworzy problem, nie `Failed` silnika. `Run.outcome` i `Run.completeness` są niezależne od stanu; ograniczenia są w `limitations`, błąd silnika w `failure`, wersja w `engineVersion`. Konfiguracja uruchomienia jest migawką projektu.

## Architektura C4/C3

`POST /api/v1/projects/{id}/analysis-runs` oprócz diagnostyki buduje migawkę architektury. Jej utworzenie jest częścią tego samego anulowalnego wykonania. Obserwacje `architecture.started` i `architecture.completed` są informacyjne (`isIssue=false`). Poprzednia migawka pozostaje dostępna podczas nowej analizy lub po jej niepowodzeniu.

- `GET /api/v1/projects/{id}/architecture` → najnowszy `ArchitectureDocument` lub `404` (brak migawki).
- `GET /api/v1/projects/{id}/architecture?snapshotId={guid}` → konkretna migawka **tego** projektu lub `404`.
- `GET /api/v1/projects/{id}/architecture/{snapshotId}/files/{fileId}?line=1` → ograniczony `SourceCodePreview` lub `404` dla pliku/migawki spoza zakresu. `fileId` pochodzi z `components[].files[].id` albo `components[].projectFileId`; API nie akceptuje dowolnej ścieżki.

Dokument zawiera identyfikatory projektu/uruchomienia, `generatedAt`, `sourceRoot`, `sourceFingerprint`, `schemaVersion`, `generator`, `containers`, `components`, `relationships`, `unresolvedReferences` i `warnings`. Komponent zawiera technologię, opis z podstawą pochodzenia, pliki, deklaracje typów/członków i pakiety. Relacja ma `sourceId`, `targetId`, `description`, `technology`, `evidenceFile`, `line` i nieewaluowany `condition`.

Podgląd kodu zwraca `{ fileId, path, hash, totalLines, startLine, lines: [{ number, text }] }`, maks. 120 linii (do 4000 znaków/linię), od 10 linii przed wskazaną. `line` jest ograniczane do istniejącego zakresu. Hash dotyczy tekstu odczytanego/dekodowanego przez analizator, nie surowych bajtów kodowania pliku. Zmieniony/usunięty/niedostępny plik lub zmieniony katalog projektu → `409`, z prośbą o ponowną analizę. Próba odczytu przez link/poza zatwierdzonym korzeniem → `400`.

Pełny kod jest odczytywany tylko lokalnie na żądanie i nie jest utrwalany w migawce. Podgląd celowo zwraca oryginalny tekst, który może zawierać sekrety. Wszystkie endpointy podlegają tej samej autoryzacji Host/Origin/cookie co pozostałe API. UI generuje eksport Structurizr DSL lokalnie w przeglądarce; nie ma zewnętrznej usługi eksportującej. [Konwencja C4, limity i bezpieczeństwo](architecture-c4.md).

## Odtwarzanie zdarzeń

`GET /api/v1/runs/{id}/events?afterSequence=0&limit=200`

```json
{
  "items": [],
  "nextSequence": 0,
  "hasMore": false
}
```

Kursor jest monotoniczny w obrębie uruchomienia. Odczytuj kolejne strony, dopóki `hasMore`, przekazując `nextSequence`. Limit: 1–1000. To trwałe źródło danych, również po utracie połączenia SignalR. Ponowne dostarczenie tego samego identyfikatora obserwacji nie zwiększa wystąpień problemu.

Severity: `Info`, `Warning`, `Error`, `Critical`. `isIssue=false` oznacza zdarzenie/metrykę, które nie tworzy niezależnego problemu. Np. duplikujący wpis konsoli Chromium o błędzie HTTP pozostaje dowodem, ale nie podwaja liczby problemów.

`summary.requestCount` obejmuje zakończone żądania, a `measuredRequestCount` wyłącznie żądania z poprawnym timingiem. P95/mediana są `null`, gdy nie ma próby. `metrics` pokazuje ostatnią próbkę danego typu; wcześniejsze są w obserwacjach.

## SignalR

Hub: `/hubs/verification`. Wymagane cookie i poprawny Origin we wszystkich transportach.

Metody klienta: `Subscribe(runId)`, `Unsubscribe(runId)`.

Zdarzenia serwera:

- `ObservationsAvailable`: `{ runId, sequence }` po commit w SQLite.
- `RunStateChanged`: aktualny obiekt `Run`.

Hub nie ma metod uruchamiających procesy ani zmieniających projekt. Te operacje należą do REST z ochroną CSRF. Po reconnect klient ponownie subskrybuje uruchomienie i uzupełnia obserwacje po ostatnim kursorze. Okresowa synchronizacja REST uzupełnia także brak powiadomienia pomiędzy commit i awarią procesu.

## Błędy

Błędy walidacji/konfliktu mają format `ProblemDetails`: `400` dla konfiguracji, `401` dla braku sesji, `403` dla Host/Origin/CSRF, `404` dla brakujących zasobów, `409` dla konfliktu uruchomienia, `500` dla błędów infrastruktury. Przed logowaniem i utrwaleniem tekst przechodzi redakcję. Nie ujawniamy stosów backendu klientowi.
