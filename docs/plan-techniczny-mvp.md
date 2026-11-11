# Verificato — plan techniczny i funkcjonalny MVP

**Status:** propozycja do zatwierdzenia; bez implementacji.  
**Nazwa produktu:** Verificato. Obecna nazwa katalogu repozytorium `Veryficato` nie musi być zmieniana.  
**Cel:** lokalna weryfikacja aplikacji podczas ręcznego testowania oraz bezpieczna analiza kodu C#/.NET.

## 1. Zakres i najważniejsze decyzje

Główny scenariusz MVP:

> Uruchamiam Verificato, dodaję projekt z lokalnym katalogiem i adresem aplikacji, rozpoczynam sesję, ręcznie korzystam z aplikacji w osobnym oknie Chromium i widzę zdarzenia, problemy oraz metryki na żywo. Po zakończeniu mogę wrócić do zapisanych wyników.

### Decyzje bazowe

| Obszar | Decyzja |
| --- | --- |
| Uruchomienie | Jedna lokalna aplikacja-host ASP.NET Core, serwująca API, SignalR i zbudowane UI |
| Architektura | Modularny monolit; osobne moduły i granice zależności, bez mikroserwisów |
| UI | React, TypeScript, Vite, Tailwind CSS, shadcn/ui |
| Backend | C# / .NET 10, ASP.NET Core, EF Core i SQLite |
| Przeglądarka | Playwright .NET, widoczne okno Chromium, nowy niepersistentny kontekst na każdą sesję |
| Przechowywanie | Lokalna baza SQLite oraz katalog artefaktów poza repozytorium badanego projektu |
| Komunikacja | REST do poleceń i odczytu; SignalR do powiadomień o nowych, już zapisanych danych |
| Analiza kodu | Rozszerzalne API analizatorów; pierwszy analizator C# oparty na Roslyn, bez wykonywania badanego kodu |
| Konta i chmura | Brak konta, obowiązkowej telemetrii, zewnętrznego backendu oraz zdalnych modeli AI |
| Współbieżność MVP | Maksymalnie jedna aktywna sesja przeglądarki i jedna analiza kodu w instancji |
| Pierwsza platforma | Windows 11 x64; kolejne pakiety dopiero po testach na danej platformie |

Program nie uruchamia automatycznie badanej aplikacji. Użytkownik uruchamia ją we własnym środowisku i podaje jej URL. Verificato nie wymaga Dockera ani uprawnień administratora do normalnej pracy.

### Poza MVP

- Automatyczne scenariusze E2E, nagrywanie i odtwarzanie kliknięć.
- Architecture fitness functions, skanowanie bezpieczeństwa i podatności zależności.
- Agenci AI, automatyczna naprawa i modyfikowanie źródeł.
- Pełne pokrycie testami, analiza wszystkich możliwych wariantów MSBuild oraz uniwersalny wynik jakości 0–100.
- Zespoły, konta, zdalny dostęp, synchronizacja i usługi chmurowe.
- Zagnieżdżona przeglądarka w UI, streaming obrazu, tracing oraz nagrywanie wideo.
- Inne silniki przeglądarek i analizatory innych języków.

## 2. Architektura rozwiązania

```text
Przeglądarka użytkownika
└── Verificato UI: React + TypeScript
    ├── REST: projekty, polecenia, historia, raporty
    └── SignalR: nowe zdarzenia, metryki, stan wykonania
              │ ten sam lokalny origin
              ▼
Verificato.Host: ASP.NET Core / .NET 10
├── Lokalne uwierzytelnienie, ochrona Origin/Host/CSRF
├── Projects: konfiguracja projektów
├── Verification: cykl życia uruchomień i polityki monitorowania
├── Reporting: problemy, metryki, historia i agregaty
├── Core: kontrakty, reguły, koordynacja analizatorów
├── Browser: adapter Playwright
│   └── osobne procesy Chromium / izolowany kontekst
├── Analysis.CSharp: adapter Roslyn
└── Infrastructure: SQLite, artefakty, kolejka i publikacja zdarzeń
```

### Odpowiedzialności trzech głównych komponentów

**Verificato UI**

- Zarządzanie projektami i konfiguracją zakresu monitorowania.
- Start/stop sesji oraz analiz kodu.
- Widok zdarzeń na żywo, dashboard i historia.
- Prezentacja dowodów, filtrów i ograniczeń analizy.
- Brak bezpośredniego dostępu do SQLite, systemu plików i Playwright.

**Verificato Core**

- Model projektu, uruchomienia, problemu, metryki i dowodu.
- Walidacja, polityki zakresu, maszyna stanów i obsługa anulowania.
- Deduplikacja problemów i obliczanie agregatów.
- Kontrakty rozszerzeń: `ICodeAnalyzer`, `IBrowserSessionRunner`, `IFindingRule`, `IMetricCollector`, `IArtifactStore`.
- Nie zależy od React, Playwright, Roslyn ani EF Core.

**Verificato Browser**

- Uruchomienie i zamknięcie Chromium oraz kontekstu sesji.
- Podpięcie obserwatorów przed pierwszą nawigacją.
- Normalizacja zdarzeń Playwright do kontraktów Core.
- Obsługa dokumentów, ramek i nowych kart zgodnie z zakresem originów.
- Brak bezpośredniego zapisu do bazy i brak reguł biznesowych dashboardu.

### Granice modułów

- Projekty domenowe i aplikacyjne komunikują się przez jawne interfejsy oraz zdarzenia wewnętrzne.
- Host jest composition root; rejestruje adaptery przez dependency injection.
- Nie tworzymy osobnej usługi sieciowej dla każdego modułu.
- Jeden plik SQLite i początkowo jeden `DbContext` są wystarczające. Konfiguracje encji oraz operacje zapisu pozostają przypisane do modułów; nie udostępniamy `IQueryable` jako kontraktu między nimi.
- REST i SignalR korzystają z wersjonowanych DTO, a nie z encji EF.
- Testy architektury sprawdzają kierunki zależności.

### Przetwarzanie w tle i dostarczanie zdarzeń

1. REST waliduje polecenie i zapisuje uruchomienie w stanie `Starting`.
2. Zarządzany `BackgroundService` uruchamia odpowiedni adapter. Odpowiedź `202 Accepted` nie czeka na zakończenie pracy.
3. Obserwatory przekazują znormalizowane dane do ograniczonego `Channel<T>`; nie wykonują operacji SQLite w callbackach Playwright.
4. Konsument filtruje zakres, usuwa dane wrażliwe, stosuje reguły i zapisuje paczki danych w krótkich transakcjach.
5. Dopiero po commit emituje przez SignalR powiadomienie z numerem sekwencji.
6. Klient może uzupełnić brakujące zdarzenia przez REST. SignalR nie jest trwałą kolejką ani źródłem prawdy.

Przeciążenie musi być widoczne: ograniczona kolejka, priorytet problemów nad informacyjnymi zdarzeniami, licznik pominiętych obserwacji i oznaczenie niepełności raportu. Nie obiecujemy bezstratności przy dowolnym ruchu. Metryki o dużej częstotliwości są agregowane, a strumień UI paczkowany.

## 3. Struktura repozytorium

Docelowa struktura, nie lista plików do utworzenia od razu:

```text
Verificato.slnx
Directory.Build.props
Directory.Packages.props
global.json
README.md
LICENSE
CONTRIBUTING.md
SECURITY.md
.editorconfig
.gitignore
docs/
  plan-techniczny-mvp.md
  architecture/
    adr/                         # decyzje i uzasadnienia
  api/
  running-locally.md
  security-model.md
src/
  Verificato.Host/                # API, SignalR, bootstrap, hostowanie UI
  Verificato.Core/
    Projects/
    Verification/
    Reporting/
    Analysis/
    Abstractions/
  Verificato.Infrastructure/
    Persistence/
    Migrations/
    Artifacts/
    BackgroundJobs/
  Verificato.Browser/
    Playwright/
    Monitoring/
    Scope/
  Verificato.Analysis.CSharp/
    Loading/
    Diagnostics/
    Metrics/
  Verificato.UI/
    src/
      app/
      components/ui/             # komponenty shadcn/ui
      features/projects/
      features/sessions/
      features/analysis/
      features/dashboard/
      lib/api/
      lib/realtime/
tests/
  Verificato.Core.Tests/
  Verificato.Api.IntegrationTests/
  Verificato.Browser.IntegrationTests/
  Verificato.Analysis.Tests/
  Verificato.Architecture.Tests/
  Verificato.UI.Tests/
  Fixtures/
    BrowserTarget/               # deterministyczna aplikacja z kontrolowanymi błędami
    DotNetProjects/              # poprawne i błędne projekty, nietypowe MSBuild
scripts/
  dev.ps1
  dev.sh
  install-browser.ps1
  install-browser.sh
  package.ps1
```

Moduły funkcjonalne pozostają katalogami w Core, dopóki nie ma powodu do wydzielenia kolejnych assembly. `Infrastructure` zależy od Core; Browser i Analysis.CSharp zależą od kontraktów Core; Host składa wszystkie komponenty. UI ma osobny proces budowania, ale w dystrybucji jest serwowane przez Host.

Proponowana licencja: Apache-2.0, z weryfikacją licencji zależności i informacjami o komponentach Chromium. Brak klucza licencyjnego lub aktywacji online.

## 4. Model danych

### Główne encje

| Encja | Najważniejsze pola | Znaczenie |
| --- | --- | --- |
| `Project` | `Id`, `Name`, `SourcePath`, `ApplicationUrl`, `CreatedAt`, `UpdatedAt`, `ArchivedAt` | Konfiguracja projektu; `ApplicationUrl` bez hasła i tokenów |
| `ProjectSettings` | `ProjectId`, `AllowedOrigins`, `SlowResponseThresholdMs`, `CapturePolicy`, `AnalysisOptions` | Wersjonowane ustawienia, początkowo małe dokumenty JSON |
| `VerificationRun` | `Id`, `ProjectId`, `Kind`, `State`, `Outcome`, `StartedAt`, `EndedAt`, `ConfigSnapshot`, `EngineVersion`, `Completeness`, `FailureCode` | Wspólna historia wykonania typu `Interactive` albo `CodeAnalysis` |
| `BrowserSession` | `RunId`, `BrowserVersion`, `EntryUrl`, `FinalUrl`, `StopReason` | Relacja 1:1 z uruchomieniem interaktywnym; `RunId` jest identyfikatorem sesji |
| `AnalysisRunDetails` | `RunId`, `AnalyzerId`, `AnalyzerVersion`, `TargetFramework`, `SourceFingerprint`, `Limitations` | Kontekst i zakres analizy źródeł |
| `Observation` | `Id`, `RunId`, `Sequence`, `OccurredAt`, `ReceivedAt`, `Type`, `Severity`, `PageId`, `PayloadVersion`, `PayloadJson`, `IssueId?` | Oczyszczone zdarzenie, np. HTTP 500 lub komunikat konsoli |
| `Issue` | `Id`, `RunId`, `Fingerprint`, `Category`, `Severity`, `RuleId`, `Title`, `Description`, `FirstSeenAt`, `LastSeenAt`, `OccurrenceCount`, `FilePath?`, `Line?`, `Column?` | Zgrupowany problem w danym uruchomieniu |
| `MetricSample` | `Id`, `RunId`, `Name`, `Value`, `Unit`, `MeasuredAt`, `DimensionsJson` | Próbka lub agregat, np. czas żądania albo FCP |
| `Evidence` | `Id`, `RunId`, `IssueId?`, `Kind`, `RelativeStoragePath?`, `ContentType`, `Size`, `Hash?`, `MetadataJson` | Dowód tekstowy albo odniesienie do lokalnego artefaktu |
| `RunSummary` | `RunId`, `CountsBySeverity`, `CountsByCategory`, `RequestCount`, `ResponseTimeSummary`, `DroppedObservationCount` | Utrwalony agregat odtwarzalny z danych uruchomienia |

Identyfikatory to UUID, daty są zapisywane w UTC i prezentowane lokalnie. Czasy trwania mierzymy zegarem monotonicznym, nie różnicą czasu ściennego. Ścieżki źródeł w problemach są względne wobec katalogu projektu. Sekrety lokalnego uwierzytelnienia nie trafiają do `ConfigSnapshot`.

### Relacje i integralność

- `Project` 1:N `VerificationRun`.
- `VerificationRun` 1:N `Observation`, `Issue`, `MetricSample`, `Evidence`.
- `Issue` 1:N `Observation` i opcjonalne dowody.
- `VerificationRun` ma dokładnie jeden właściwy rekord szczegółowy dla swojego `Kind`.
- Zmiana konfiguracji projektu nie modyfikuje historycznych ustawień uruchomień.
- Usunięcie historii usuwa zależne dane i artefakty; archiwizacja projektu zachowuje historię.

### Statusy i wynik

Stan wykonania jest niezależny od wyniku jakości:

```text
Starting → Running → Stopping → Completed
     └──────────┴──────────┴──→ Failed
aktywny stan znaleziony po restarcie → Interrupted
```

Analiza bez ręcznego zatrzymania może przejść z `Running` bezpośrednio do `Completed`. Stop jest idempotentny. Błąd wewnętrzny silnika daje `Failed`; błąd JavaScript badanej aplikacji tworzy problem i nie przerywa sesji. Zamknięcie ostatniej karty przez użytkownika kończy sesję z odpowiednim `StopReason`.

`Outcome`: `NoIssuesDetected`, `IssuesDetected`, `Inconclusive`. Dodatkowo `Completeness`: `CompleteWithinScope`, `Partial`, `Unavailable`. Brak wykrytych problemów nie oznacza certyfikatu poprawności ani pokrycia całej aplikacji. Niepełna analiza nie może otrzymać mylącego zielonego statusu.

### Kategorie, istotność i deduplikacja

- Kategorie: `JavaScript`, `Console`, `Http`, `Resource`, `Performance`, `Compilation`, `Analyzer`, `CodeQuality`.
- Istotność: `Info`, `Warning`, `Error`, `Critical`; domyślne reguły MVP zwykle używają pierwszych trzech.
- Osobne pole `RuleId` pozwala rozróżnić regułę od ogólnej kategorii.
- Fingerprint jest liczony z reguły, znormalizowanego komunikatu i lokalizacji. Usuwamy sekrety przed jego wyliczeniem.
- Liczba problemów i liczba wystąpień są prezentowane oddzielnie.
- Korelacja zapobiega podwójnemu zgłoszeniu tego samego HTTP 404 jako niezależnego błędu HTTP i zasobu. 404 może jednak być poprawnym zachowaniem aplikacji — MVP raportuje obserwację, nie rozstrzyga automatycznie intencji biznesowej.
- Nie łączymy bezwarunkowo `console.error` z `pageerror`; to mogą być dwa różne problemy.

### SQLite i retencja

- WAL, foreign keys, busy timeout, krótkie transakcje i kontrolowany zapis wsadowy.
- Indeksy: `(ProjectId, StartedAt)`, unikalny `(RunId, Sequence)`, unikalny `(RunId, Fingerprint)`, `(RunId, Severity)`, `(RunId, Category)`, `(RunId, Name, MeasuredAt)`.
- Sekwencja jest monotoniczna w obrębie uruchomienia. Powtórne dostarczenie nie tworzy kolejnych wystąpień tego samego zdarzenia.
- Duże pliki nie są przechowywane jako BLOB w SQLite. Ścieżki artefaktów muszą pozostać wewnątrz katalogu danych Verificato.
- Domyślny limit sesji: 60 minut, 100 000 obserwacji i 100 MB danych obserwacji/artefaktów; wartości do potwierdzenia testami obciążeniowymi. Osiągnięcie limitu jest jawne i kończy zbieranie w kontrolowany sposób z oznaczeniem `Partial`.
- Retencja początkowo ręczna: użytkownik widzi zajęte miejsce i usuwa wybrane sesje. Nie kasujemy historii bez wyraźnej polityki.
- Kopia bazy przez SQLite Backup API lub przy wyłączonym hoście, nie przez kopiowanie samego pliku aktywnej bazy z pominięciem WAL.

## 5. Interaktywna weryfikacja aplikacji

### Uruchomienie sesji

1. Backend sprawdza projekt, adres, zakres i dostępność Chromium; brak binariów kończy się instrukcją instalacji.
2. Tworzy uruchomienie wraz z migawką ustawień.
3. Otwiera Chromium w trybie headed z pustym, niepersistentnym kontekstem. Nie używa profilu ani cookies zwykłej przeglądarki użytkownika.
4. Rejestruje obsługę nowych stron, błędów, konsoli, żądań, odpowiedzi i nawigacji.
5. Otwiera wskazany URL, pokazuje stan `Running` i zaczyna przesyłać wyniki.
6. Użytkownik loguje się i wykonuje operacje ręcznie; Verificato nie zna ani nie zapisuje haseł formularzy.
7. Stop powoduje anulowanie zbierania, końcowy odczyt dostępnych metryk, zapis bufora, podsumowanie i zamknięcie kontekstu.

Niedostępny adres, nieprawidłowy certyfikat i przekroczenie czasu nawigacji muszą dawać czytelny komunikat. Nie wyłączamy domyślnie weryfikacji TLS. Nie zabijamy wszystkich procesów Chromium, wyłącznie zasoby własnej sesji.

### Zbierane informacje

| Źródło | Obserwacja / metryka | Ograniczenia |
| --- | --- | --- |
| Playwright `PageError` | Nieobsłużony błąd JavaScript, komunikat, stos, lokalizacja | Nie obejmuje błędów przechwyconych i niewypisanych przez aplikację |
| `Console` | `warning` i `error`, lokalizacja, oczyszczony tekst | Nie serializujemy dowolnych obiektów ani pełnych danych aplikacji |
| `Response` | Status 4xx/5xx, metoda, bezpieczny URL | HTTP 4xx/5xx nie jest `RequestFailed` |
| `RequestFailed` | Błędy sieci, DNS, TLS, przerwane ładowanie zasobów | Świadome anulowanie przy nawigacji nie musi być błędem; osobna reguła |
| `RequestFinished` i timing | Czas żądania, opcjonalnie TTFB, typ zasobu | Brakujące timingi są niedostępne, a nie równe zero |
| Navigation/Performance API | DOMContentLoaded, load, FCP; LCP i CLS, gdy dostępne | Przypisane do dokumentu; SPA nie tworzy automatycznie nowej pełnej nawigacji |
| Agregacja | Liczba żądań, błędów, mediana i p95 czasu odpowiedzi | Tylko w zadanym zakresie i dla żądań z dostępnym pomiarem; UI pokazuje próbę |

LCP/CLS są metrykami obserwowanej sesji laboratoryjnej, nie wynikami terenowymi ani audytem Lighthouse. Odczyt może zmieniać się do końca życia dokumentu. INP, profilowanie CPU/pamięci i pełne Core Web Vitals pozostają poza MVP. Progi wolnych żądań są konfigurowalne, a przekroczenie progu tworzy ostrzeżenie, nie automatyczny błąd aplikacji.

### Zakres monitorowania

- Domyślnie dokładny origin wskazanej aplikacji: schemat, host i port. `localhost` i `127.0.0.1` to różne originy.
- Osobny backend, np. `http://localhost:5000`, wymaga jawnego dodania do listy originów projektu.
- Nawigacja dokumentu lub popup poza zakresem jest blokowana przed kontynuacją i sygnalizowana. Zewnętrzne logowanie wymaga świadomego rozszerzenia zakresu.
- Dla zasobów zewnętrznych dostępny jest jawny wybór: zezwalaj na ładowanie bez zapisywania szczegółów albo blokuj. Tryb blokowania może zmieniać zachowanie aplikacji i musi być oznaczony w raporcie.
- Ramki i nowe karty w dozwolonym zakresie są monitorowane; pozostałe nie dostarczają treści do raportu.
- Korelacja błędów konsoli bez lokalizacji jest przypisana do aktywnego dokumentu i oznaczona jako niepewna. Nie deklarujemy idealnego przypisania każdego komunikatu do biblioteki lub skryptu.
- Service workers są domyślnie blokowane w MVP dla przewidywalnego zakresu przechwytywania. UI ostrzega o wpływie na aplikacje PWA. Pełne monitorowanie service workers, workerów, WebSocket frames oraz innych kanałów nie jest częścią MVP.

Nie da się zapewnić całkowitego odłączenia od Internetu, jednocześnie pozwalając badanej aplikacji dowolnie pobierać jej zasoby. Verificato nie wysyła wyników do usług zewnętrznych, ale sama aplikacja docelowa może wykonywać połączenia. Twarde odcięcie sieci wymaga dodatkowo polityki systemowej lub środowiska izolowanego i nie jest obietnicą MVP.

### Dowody

W MVP: czas wystąpienia, bezpieczny URL, metoda/status, tekst błędu, stos wywołań, plik i pozycja oraz wybrane metadane żądania. Bez zapisywania treści żądań/odpowiedzi, cookies, nagłówków uwierzytelniających i nagrywania formularzy. Screenshoty są opcjonalnym rozszerzeniem, domyślnie wyłączonym, ponieważ mogą zawierać dane osobowe.

## 6. Analiza C#/.NET bez modyfikacji projektu

### Ważne rozróżnienie

„Tylko do odczytu” nie może oznaczać bezwarunkowego `dotnet build` uruchomionego w katalogu użytkownika. Build/restore zapisuje pliki i może wykonywać targety MSBuild, skrypty, source generatory oraz analizatory. Również standardowe ładowanie przez `MSBuildWorkspace` może uruchamiać design-time build. Kopia projektu sama w sobie nie izoluje złośliwych targetów od reszty systemu.

Dlatego MVP ma **bezpieczny, ograniczony tryb Roslyn**, a nie pełny build dowolnego repozytorium.

### Kontrakt pierwszego analizatora

`ICodeAnalyzer` otrzymuje zakres plików, konfigurację, token anulowania i interfejs raportowania. Zwraca: diagnostykę, metryki, listę ograniczeń, informację o pokryciu oraz status kompletności. Rozpoznawanie obsługiwanych projektów odbywa się przed właściwą analizą.

### Obsługiwany zakres MVP

- Projekty SDK-style C# dla .NET 10, z jednym wybranym TFM; w pierwszej kolejności proste biblioteki i aplikacje konsolowe.
- Składnia i diagnostyka kompilatora Roslyn dla plików C# oraz wspieranego kontekstu referencji.
- Referencje platformowe z zaufanego lokalnego targeting packa; istniejące referencje z lokalnego cache można wykorzystać wyłącznie jako metadane, bez ładowania ich kodu do wykonania.
- Jawnie ograniczony odczyt `.csproj` i istniejących danych zależności, bez ewaluacji MSBuild, sieci i restore. Walidacja dostępności pakietów i zakresów ścieżek przed odczytem.
- Obsługa prostych referencji między projektami jest osobnym, testowalnym zakresem; cykle lub konfiguracje poza kontraktem dają ograniczenie analizy.
- Wyłącznie analizatory dostarczone i zaufane przez Verificato. Pakietowe analizatory i source generatory badanego projektu nie są wykonywane.
- Proste ostrzeżenia jakościowe, np. puste `catch`, wysoka złożoność metod, nadmierna długość metody; reguły mają identyfikator i opis ograniczeń.
- Metryki: liczba plików, fizyczne linie kodu z jawną definicją, liczba typów i metod, złożoność cyklomatyczna według udokumentowanego algorytmu. Bez pozorowania pokrycia testami.

Dla Razor, WPF/XAML, wielotargetowania, niestandardowych importów/warunków MSBuild, brakujących referencji lub kodu generowanego UI pokazuje `Partial` i opis pominiętego zakresu. Nie generujemy lawiny pewnych „błędów kompilacji” z samego faktu niepełnego kontekstu: diagnostyka zależna od brakujących referencji jest oznaczona jako niepewna lub pomijana na rzecz ostrzeżenia o konfiguracji. Dane diagnostyczne mówią, czy pochodzą ze składni, kompletnej kompilacji wspieranego projektu czy analizy częściowej.

### Pipeline

1. Kanonizacja katalogu i potwierdzenie dostępu tylko do wskazanego zakresu.
2. Wybór `.sln`/`.slnx`/`.csproj` przez użytkownika, jeśli znaleziono więcej niż jeden punkt wejścia; plik rozwiązania jest indeksem, nie pretekstem do wykonywania MSBuild.
3. Ograniczona enumeracja z wykluczeniem `.git`, `bin`, `obj`, `node_modules`, katalogów Verificato i linków wychodzących poza zakres. Istniejące metadane w `obj` mogą być odczytane osobno, nigdy jako źródła do skanowania.
4. Limit liczby/rozmiaru plików, bezpieczne parsowanie XML bez DTD, anulowanie i limity czasu.
5. Wczytanie plików do pamięci lub prywatnej migawki w katalogu danych Verificato; zapis skrótów i identyfikatora konfiguracji.
6. Roslyn: składnia, kompilacja wspieranego zakresu, zaufane reguły i metryki.
7. Zapis wyników wraz z listą ograniczeń i informacją, które pliki/TFM przeanalizowano.

Nie zapisujemy niczego w źródłowym katalogu projektu; nie następuje automatyczne restore, build ani wykonanie aplikacji. Migawka nie jest atomowa względem edycji użytkownika: wykryte zmiany podczas odczytu powodują ostrzeżenie lub ponowne wczytanie. Gwarancja dotyczy treści plików i braku zapisów inicjowanych przez Verificato, nie metadanych dostępu kontrolowanych przez system operacyjny.

**Rozszerzenie po MVP:** osobny, świadomie włączany tryb zaufanego builda albo rzeczywiście izolowany worker. Dopiero wtedy można deklarować szerszą zgodność z pełnym procesem kompilacji. Tryb wykonywania kodu wymaga osobnego modelu zagrożeń.

## 7. Główne ekrany i przepływy

### Układ i styl

- Lewy panel: Projekty, historia bieżącego projektu, ustawienia lokalne.
- Górny pasek: nazwa projektu, stan local-only, przełącznik motywu, akcje kontekstowe.
- Jasny i ciemny motyw oraz tryb systemowy; dostępny kontrast, obsługa klawiatury i widoczny focus.
- Neutralna typografia i oszczędne kolory. Statusy mają tekst i ikonę, nie tylko kolor.
- Monospace do ścieżek, stosów i szczegółów technicznych. Długie listy są stronicowane lub wirtualizowane.

### Ekrany

| Ekran | Zawartość i najważniejsze akcje |
| --- | --- |
| Pierwsze uruchomienie | Stan SDK/Chromium, katalog danych, opis prywatności, utworzenie projektu |
| Lista projektów | Nazwa, ścieżka, URL, ostatni wynik i czas; wyszukiwarka; utwórz/archiwizuj |
| Formularz projektu | Nazwa, bezwzględna ścieżka, URL, dodatkowe originy; walidacja po stronie backendu; potwierdzenie zakresu |
| Dashboard projektu | Ostatnia sesja i ostatnia analiza osobno, problemy według kategorii/istotności, metryki, historia; „Rozpocznij sesję”, „Analizuj kod” |
| Sesja na żywo | Stan i czas, liczniki, timeline zdarzeń, filtry, metryki, stan połączenia SignalR, przycisk Stop |
| Szczegóły problemu | Opis, reguła, liczba wystąpień, lokalizacja, stos/HTTP, dowody i ograniczenia |
| Analiza kodu | Wybór projektu/TFM, zakres, postęp etapów, anulowanie, diagnostyka plik:linia:kolumna, ograniczenia |
| Historia i raport | Stronicowana lista wykonań, wynik i kompletność, podsumowanie i szczegóły; usunięcie po potwierdzeniu |
| Ustawienia lokalne | Katalog danych i użycie dysku, limity, motyw, diagnostyka instalacji przeglądarki; brak konta |

Dashboard nie sumuje bezwarunkowo różnych wykonań. Widoki ostatniej sesji i ostatniej analizy mają własne znaczniki czasu. Karta łączna wyraźnie podaje, z których uruchomień pochodzą dane. W MVP nie ma arbitralnego „quality score”.

W formularzu MVP ścieżka jest wpisywana/wklejana. Standardowy input przeglądarkowy nie udostępnia wiarygodnie pełnej lokalnej ścieżki do użycia przez backend; nie obiecujemy systemowego wyboru katalogu bez dodatkowej integracji desktopowej.

### Przepływ A: pierwsza weryfikacja

1. Użytkownik uruchamia Verificato; launcher otwiera bezpiecznie uwierzytelnione UI.
2. Tworzy projekt i potwierdza katalog oraz origin aplikacji.
3. Jeśli aplikacja nie działa, dostaje instrukcję jej samodzielnego uruchomienia.
4. Wybiera „Rozpocznij sesję”; Chromium otwiera się w oddzielnym oknie.
5. Użytkownik przełącza się między badaną aplikacją i panelem Verificato, najlepiej ustawiając je obok siebie.
6. Błędy i metryki pojawiają się bez odświeżania. Kliknięcie zdarzenia otwiera szczegóły.
7. „Wstrzymaj przewijanie” dotyczy tylko widoku, nie zbierania danych.
8. Użytkownik zatrzymuje sesję albo zamyka ostatnią kartę; raport pozostaje w historii.

### Przepływ B: analiza źródeł

1. Użytkownik wybiera „Analizuj kod”.
2. Wybiera punkt wejścia i TFM, jeśli to potrzebne; widzi możliwości oraz ograniczenia loadera.
3. Analiza działa niezależnie od ręcznej sesji, z osobnym postępem i możliwością anulowania.
4. Lista diagnostyki ma filtry istotności, reguły i pliku. Dowód zawiera lokalizację i oczyszczony fragment, nie edytor zapisujący pliki.
5. Raport pokazuje rozróżnienie pełnego wspieranego kontekstu i analizy częściowej.

### Przepływ C: utrata połączenia i restart

- Po odłączeniu SignalR UI pokazuje stan reconnecting; sesja nadal działa w hoście.
- Po powrocie klient synchronizuje się od ostatniej sekwencji, usuwa duplikaty i odświeża agregaty.
- Zamknięcie zakładki Verificato nie zatrzymuje sesji. Zamknięcie hosta próbuje ją zakończyć; po awarii aktywne rekordy są oznaczane `Interrupted`.
- Backend nie wznawia automatycznie starego logowania ani sesji przeglądarki.

## 8. REST API i SignalR

Przykładowy kontrakt wersji `v1`:

| Metoda i ścieżka | Cel |
| --- | --- |
| `GET /api/v1/projects` | Lista projektów |
| `POST /api/v1/projects` | Utworzenie projektu |
| `GET /api/v1/projects/{id}` | Szczegóły i konfiguracja |
| `PATCH /api/v1/projects/{id}` | Aktualizacja konfiguracji |
| `DELETE /api/v1/projects/{id}` | Usunięcie z jawnym potwierdzeniem polityki historii |
| `GET /api/v1/projects/{id}/dashboard` | Ostatnie wyniki i agregaty |
| `POST /api/v1/projects/{id}/browser-sessions` | Start; `202` z `runId` i adresem statusu |
| `POST /api/v1/projects/{id}/analysis-runs` | Start analizy; `202` z `runId` |
| `GET /api/v1/projects/{id}/runs?cursor=...` | Historia z paginacją |
| `GET /api/v1/runs/{id}` | Stan, wynik i kompletność |
| `POST /api/v1/runs/{id}/stop` | Idempotentny stop/anulowanie |
| `GET /api/v1/runs/{id}/events?afterSequence=...&limit=...` | Odtworzenie zdarzeń |
| `GET /api/v1/runs/{id}/issues` | Filtrowane problemy |
| `GET /api/v1/runs/{id}/metrics` | Metryki i serie |
| `GET /api/v1/issues/{id}` | Szczegóły i dowody |
| `GET /api/v1/evidence/{id}` | Uwierzytelniony odczyt dowodu, nie dowolnej ścieżki |
| `DELETE /api/v1/runs/{id}` | Usunięcie zakończonego wykonania |
| `GET /api/v1/system/status` | Stan instalacji i limity bez ujawniania sekretów |

Walidacja i błędy w formacie `ProblemDetails`. `409 Conflict` dla startu drugiej sesji lub konfliktu konfiguracji, `400` dla błędnej konfiguracji. Ponowienie startu z tym samym kluczem idempotencji nie otwiera kolejnego Chromium. Nie tworzymy endpointu pozwalającego pobrać dowolny plik z dysku ani uruchomić dowolne polecenie.

Hub `/hubs/verification`:

- `RunStateChanged`, `ObservationsAvailable`, `IssuesChanged`, `MetricsUpdated`, `RunCompleted`.
- Każde powiadomienie zawiera `runId`, wersję kontraktu i odpowiedni kursor/revizję.
- Klient subskrybuje wybrane uruchomienie po uwierzytelnieniu; nie dostaje automatycznie całej historii.
- Podczas reconnect klient najpierw zakłada subskrypcję, buforuje powiadomienia, uzupełnia REST do ustalonego kursora, a potem deduplikuje bufor. Okresowy odczyt stanu wykrywa także brak końcowego powiadomienia po awarii między commit i wysyłką.
- Stan uruchomienia i rewizja podsumowania są synchronizowane niezależnie od sekwencji obserwacji.
- W produkcji UI, API i hub mają ten sam origin. W development Vite proxy przekazuje `/api`, `/hubs` i bootstrap do hosta.

## 9. Model bezpieczeństwa

### Lokalne API

Samo zbindowanie do localhost nie jest uwierzytelnieniem: złośliwa strona internetowa może próbować kontaktować się z usługami lokalnymi.

- Host słucha tylko na loopback, domyślnie `127.0.0.1`, na przydzielonym lokalnym porcie. Brak domyślnego `0.0.0.0` i zdalnego dostępu.
- Ścisła kontrola `Host` i `Origin`, brak wildcard CORS, ochrona przed DNS rebinding.
- Launcher generuje kryptograficznie losowy, krótkotrwały, jednorazowy sekret bootstrap. Przekazuje go do UI we fragmencie adresu, nie w query string. UI natychmiast usuwa fragment z historii i wymienia sekret przez POST na sesję.
- Cookie sesji: host-only, `HttpOnly`, `SameSite=Strict`, ograniczona ważność. Na lokalnym HTTP nie deklarujemy nieistniejącej ochrony TLS ani bezwarunkowego `Secure`; lokalne HTTPS można dodać jako odrębny tryb. Nie stosujemy niezabezpieczonego stałego hasła ani tokenu w `localStorage`.
- Dodatkowy token anty-CSRF i walidacja originu dla operacji zmieniających stan. Hub nie przyjmuje poleceń start/stop — te idą przez chroniony REST. Handshake i połączenia hub wymagają sesji i kontroli originu.
- Brak sekretów w logach, refererach, query stringach i komunikatach SignalR. Bootstrap jest przypięty do konkretnej instancji hosta i wygasa po użyciu.
- Middleware odrzuca nieautoryzowane żądania niezależnie od transportu SignalR. Zabezpieczenia nie mogą bazować tylko na CORS.
- Ten sam użytkownik systemu z dostępem do procesu i profilu nie jest przeciwnikiem, przed którym aplikacja zapewnia pełną izolację. Cookies localhost nie są izolowane portem; HTTPS i standardowy sandbox przeglądarki też nie rozwiązują wszystkich zagrożeń ze strony lokalnego złośliwego procesu.

### Dane i system plików

- Katalog danych i jego uprawnienia ograniczone do użytkownika systemu.
- Walidacja, kanonizacja i kontrola linków/reparse points; bez automatycznego przechodzenia do sąsiednich katalogów.
- Redakcja query stringów, fragmentów URL i rozpoznanych sekretów przed zapisem. Konfigurowalne reguły dla dodatkowych nazw parametrów.
- Tekst konsoli/stosu też może zawierać dane wrażliwe: ograniczamy długość, stosujemy redakcję i pokazujemy ostrzeżenie. Nie obiecujemy doskonałego wykrycia wszystkich sekretów.
- Logi operacyjne nie zawierają pełnego kodu, danych formularzy ani wyników nieuwzględnionych w polityce przechwytywania.
- Dane z badanej aplikacji renderujemy jako tekst, nigdy jako niezaufany HTML. UI ma CSP, lokalne assety i brak zdalnych fontów/skryptów.
- Bez wysyłania analiz, dumpów awarii i telemetrii do zewnętrznych usług. Aktualizacje i instalacja zależności są jawne.

### Granice izolacji

Kontekst Playwright izoluje profil, ale nie jest pełną maszyną wirtualną ani barierą przed złośliwym kodem wykorzystującym lukę Chromium. Nie używamy `--no-sandbox` jako domyślnego obejścia. Analizatory uruchomione w procesie hosta są zaufanym kodem Verificato, nie dowolnymi wtyczkami repozytorium użytkownika.

## 10. Etapy implementacji

Każdy etap powinien mieścić się w małym PR lub krótkiej serii PR-ów z osobnym testowalnym rezultatem. Najpierw realizujemy pionowy przepływ przeglądarkowy, potem poszerzamy dashboard i analizę statyczną.

| Etap | Zakres | Zależności | Warunek zakończenia |
| --- | --- | --- | --- |
| E0. Kontrakty i ADR | Stany, model danych, zakres monitorowania, threat model, wspierany kontrakt Roslyn | — | Zatwierdzone decyzje i fixture testowej aplikacji |
| E1. Szkielet lokalny | Projekty .NET/UI, DI, hostowanie SPA, loopback auth, jasny/ciemny motyw | E0 | UI działa lokalnie; obcy origin i brak sesji są odrzucane |
| E2. Projekty i baza | Migracje SQLite, CRUD, formularz i walidacja ścieżki/URL | E1 | Projekt pozostaje po restarcie; brak dostępu poza zakresem |
| E3. Pierwsza sesja | Start/stop Playwright, jedna sesja, pageerror, zapis i odczyt REST | E2 | Ręczny klik w fixture generuje zapisany problem; stop sprząta Chromium |
| E4. Monitoring i live | Konsola, HTTP, request failed, zakres originów, SignalR, replay i redakcja | E3 | Wszystkie źródła mają testy; reconnect nie gubi widoczności utrwalonych zdarzeń |
| E5. Wyniki i metryki | Dashboard, deduplikacja, historia, szczegóły, timingi i metryki dokumentów | E4 | Raport dostępny po restarcie; liczniki nie podwajają wystąpień |
| E6a. Loader C# | Interfejs analizatora, bezpieczne ładowanie, capability detection, składnia | E2 + kontrakty E0 | Brak wykonania MSBuild; nietypowy projekt dostaje jawne ograniczenia |
| E6b. Roslyn i jakość | Kompilacja wspieranego kontekstu, zaufane reguły, metryki, ekran analizy | E6a, E5 do wspólnego dashboardu | Znane błędy C# mają plik i pozycję, źródła niezmienione |
| E7. Odporność | Limity, batching, anulowanie, awarie, Interrupted, mało miejsca, retencja | E4–E6 | Testy obciążenia i restartu; niepełność danych jest widoczna |
| E8. Dystrybucja | Publish Windows x64, lokalne UI, instalacja Chromium, launcher, instrukcje i licencje | E7 | Pakiet uruchomiony na czystym wspieranym systemie |

E6a może powstawać równolegle z E3–E5 po ustabilizowaniu kontraktów. Nie wymaga działania przeglądarki. Bezpieczeństwo i testy są warunkiem każdego etapu, nie zadaniem odkładanym w całości na E7.

## 11. Kryteria akceptacji MVP

### Projekt i lokalność

- [ ] Użytkownik uruchamia dystrybucję bez konta i usług chmurowych.
- [ ] Dodaje projekt z nazwą, lokalną ścieżką i URL; niepoprawne dane dają czytelne błędy.
- [ ] Konfiguracja i historia przetrwają restart. Zmiana URL nie zmienia starych raportów.
- [ ] Po instalacji zależności produkt działa bez dostępu do Internetu, o ile sama badana aplikacja i użyte referencje także są dostępne lokalnie.

### Główny scenariusz przeglądarkowy

- [ ] Start otwiera widoczne Chromium z czystym profilem; użytkownik może ręcznie nawigować, logować się i wysyłać formularze.
- [ ] Fixture wywołuje osobno: nieobsłużony błąd JS, `console.warn`, `console.error`, HTTP 404, HTTP 500, błąd ładowania zasobu i wolną odpowiedź. Każdy trafia do właściwej kategorii.
- [ ] Dla obciążenia referencyjnego 20 obserwacji/s, z krótkimi pikami 200/s, 95% zdarzeń jest widocznych w UI w ciągu 1 sekundy od odebrania przez host. Profil maszyny i procedura pomiaru są zapisane w raporcie testu.
- [ ] Czasy odpowiedzi są weryfikowane na fixture z kontrolowanym opóźnieniem, z tolerancją określoną w teście; brak pomiaru nie daje wartości zero.
- [ ] Popup i backend w jawnie dozwolonym originie są obsługiwane; niedozwolona nawigacja jest blokowana, a dane z innego originu nie trafiają do raportu.
- [ ] Stop, zamknięcie ostatniej karty i normalne zamknięcie hosta kończą sesję i sprzątają własne zasoby.
- [ ] Po rozłączeniu i ponownym połączeniu UI uzupełnia zapisane zdarzenia bez podwajania wystąpień.
- [ ] Po awarii hosta historia pozostaje czytelna, a aktywne wykonanie ma `Interrupted`, nie `Completed`.

### Analiza C#

- [ ] Wspierany projekt fixture ma wykryty celowo wprowadzony błąd składni oraz semantyczny błąd kompilacji z poprawnym plikiem i pozycją.
- [ ] Zaufana reguła jakości wykrywa przygotowany problem, a poprawiona wersja fixture nie generuje tego zgłoszenia.
- [ ] Dashboard pokazuje przynajmniej liczbę plików, zdefiniowane LOC i metrykę złożoności.
- [ ] Brak referencji, nietypowy MSBuild lub zależność od generatora daje jawne `Partial`/`Unavailable`, nigdy udawany pełny sukces.
- [ ] Analiza nie uruchamia targetu przygotowanego w złośliwej fixture, generatorów ani analizatorów z badanego projektu i nie wykonuje restore.
- [ ] Testy porównują treści źródeł przed/po i kontrolują brak utworzonych przez Verificato plików w badanym drzewie.

### UI, bezpieczeństwo i jakość operacyjna

- [ ] Dashboard rozdziela wynik sesji, wynik analizy, problemy, wystąpienia i niepełność danych.
- [ ] Działają motyw ciemny, podstawowa obsługa klawiaturą, filtry i czytelne puste/błędne stany.
- [ ] Nieautoryzowany klient REST/SignalR, obcy origin, niewłaściwy Host i próba CSRF nie uruchamiają sesji ani nie ujawniają danych.
- [ ] Tokeny z query, przykładowy `Authorization`, cookies i treść formularza nie znajdują się w raporcie ani logach fixture.
- [ ] Godzinna sesja obciążenia nie powoduje nieograniczonego wzrostu kolejki/RAM. Osiągnięte limity i odrzucone obserwacje są pokazane użytkownikowi.
- [ ] Pakiet uruchamia się na czystym Windows 11 x64 bez zainstalowanego runtime .NET; brak Chromium lub SDK potrzebnego do analizy jest wyjaśniony w diagnostyce.

## 12. Testy i diagnostyka

- **Unit:** przejścia stanów, fingerprinty, redakcja, zakres originów, progi, agregaty, algorytm złożoności.
- **Integracyjne API/SQLite:** migracje, FK, paginacja, idempotencja, transakcje i odtwarzanie po restarcie; rzeczywista tymczasowa baza zamiast zastępowania SQLite providerem in-memory.
- **Browser:** lokalny serwer fixture z deterministycznymi endpointami i popupami; testy błędów, przekierowań, zakresu, anulowania i zamykania. Automatyzacja testów może działać headless, ale osobny smoke test musi obejmować docelowy tryb headed.
- **Roslyn:** zestaw projektów wspieranych i niewspieranych, brakujące referencje, linki wychodzące poza katalog, duże pliki i złośliwe targety, których nie wolno wykonać.
- **UI/E2E:** utworzenie projektu → sesja → problem → stop → raport oraz reconnect i motywy.
- **Bezpieczeństwo:** brak autoryzacji, CSRF, Origin/Host, path traversal, redakcja, bezpieczne renderowanie komunikatów i brak niejawnego uruchamiania kodu.
- **Obciążenie:** opóźnienie end-to-end, wzrost bazy, pamięć hosta/kolejki, przeciążenie i odzyskanie po awarii.
- **CI:** .NET build/test, TypeScript/lint/test, UI build i smoke test dystrybucji. CI korzysta z kontrolowanych fixtures, nie z prywatnych katalogów użytkownika.

Lokalne logi strukturalne zawierają identyfikatory uruchomień, stan pipeline i kody błędów infrastruktury. Logowanie nie jest dodatkowym kanałem kopiowania nieoczyszczonych dowodów. Eksport diagnostyczny jest ręczny i podlega podglądowi/redakcji.

## 13. Planowana instrukcja lokalnego uruchomienia

Poniższe polecenia opisują **docelowy interfejs repozytorium**. Skrypty i projekty nie istnieją jeszcze; to nie jest instrukcja uruchomienia obecnego szkieletu.

### Tryb deweloperski

Wymagania:

- .NET 10 SDK w wersji przypiętej przez `global.json`.
- Wspierane Node.js LTS, proponowane 24 LTS, oraz npm z lockfile.
- Chromium dostarczone przez wersję Microsoft.Playwright przypiętą w repozytorium.
- PowerShell 7 dla proponowanych skryptów `.ps1`; odpowiedniki bash dla testowanych platform Unix.
- Uruchomiona niezależnie aplikacja do weryfikacji.

```powershell
# 1. Pobierz repozytorium i przejdź do katalogu głównego.
dotnet restore .\Verificato.slnx
npm --prefix .\src\Verificato.UI ci

# 2. Zbuduj backend i zainstaluj zgodne Chromium.
dotnet build .\Verificato.slnx
pwsh .\scripts\install-browser.ps1

# 3. Uruchom host + Vite proxy i bezpieczny bootstrap UI.
pwsh .\scripts\dev.ps1
```

Skrypt instalacji przeglądarki wywołuje skrypt `playwright.ps1 install chromium` wygenerowany przez odpowiedni build Playwright .NET, bez twardego założenia, że jego lokalizacja jest identyczna dla build i publish. Skrypt developerski zestawia proxy, drukuje niesekretny adres/stan i otwiera bootstrap w przeglądarce. Nie wyłącza autoryzacji w development.

Kolejne czynności użytkownika:

1. Uruchom własną aplikację, np. pod `http://localhost:5173`.
2. W Verificato dodaj nazwę, ścieżkę i URL; dopisz origin API, jeśli jest inny.
3. Wybierz „Rozpocznij sesję” i ręcznie używaj okna Chromium.
4. Zatrzymaj sesję, przejrzyj dashboard; opcjonalnie rozpocznij analizę kodu.

Testy po implementacji:

```powershell
dotnet test .\Verificato.slnx
npm --prefix .\src\Verificato.UI test -- --run
npm --prefix .\src\Verificato.UI run build
```

Pierwsze pobranie pakietów i Chromium wymaga sieci lub wcześniej przygotowanego lokalnego cache. Instalacja zależności nie jest częścią analizowania źródeł i nie powinna uruchamiać się bez pytania podczas sesji.

## 14. Dystrybucja i utrzymanie

### Pakiet MVP

- Paczka Windows x64: self-contained publish hosta .NET, zbudowane assety UI, launcher, zgodna wersja Chromium lub oddzielna jawna instalacja, instrukcje i licencje.
- Preferowany pełny pakiet offline zawiera binaria przeglądarki w wersji zgodnej z Playwright. Lekki wariant może pobierać je raz za zgodą użytkownika.
- Self-contained .NET nie obejmuje automatycznie Chromium ani SDK/targeting packów potrzebnych do analizy kodu. Diagnostics screen odróżnia te zależności.
- Brak wymogu single-file; zwykły katalog publish jest prostszy dla skryptów i sterowników Playwright.
- Bez trimming/AOT w pierwszej wersji, dopóki kompatybilność Playwright/Roslyn/EF nie zostanie osobno potwierdzona.
- Launcher wybiera port, inicjuje sesję lokalną i otwiera UI. Po zamknięciu hosta zapisuje stan i zamyka własne procesy.

Planowane polecenie przygotowania paczki:

```powershell
pwsh .\scripts\package.ps1 -Runtime win-x64 -IncludeChromium
```

Skrypt kolejno buduje UI, uruchamia testy, publikuje host, dodaje kompatybilne pliki Playwright/Chromium, informacje licencyjne i generuje sumy kontrolne. Pakiet jest sprawdzany na czystej maszynie. Podpisywanie binariów jest zalecane dla publicznych wydań; bez podpisu należy uczciwie opisać ostrzeżenia systemowe.

### Dane użytkownika

Proponowane katalogi:

- Windows: `%LOCALAPPDATA%\Verificato\`.
- Linux: `$XDG_DATA_HOME/verificato/` lub `~/.local/share/verificato/`.
- macOS: `~/Library/Application Support/Verificato/`.

Wewnątrz: `verificato.db`, `artifacts/`, `logs/`, `work/` i ewentualny cache przeglądarki. Zmiana katalogu wymaga zatrzymania aktywnych wykonań i ponownego uruchomienia. Pliki te nie są zapisywane w repozytorium użytkownika.

Aktualizacje są ręczne: zatrzymanie hosta, kopia danych, wymiana binariów, kontrolowana migracja. Nowa wersja nie może otworzyć bazy o nieobsługiwanej nowszej wersji schematu. Przed migracją tworzymy lokalną kopię. Nie wykonujemy cichego automatycznego downgrade bazy. Usunięcie programu domyślnie nie usuwa wyników.

Linux/macOS są kierunkiem rozwoju, nie domyślną gwarancją pierwszego wydania. Osobne pakiety muszą uwzględnić zależności systemowe Playwright, architekturę CPU i testy headed. Kontener nie jest domyślnym sposobem dystrybucji ze względu na interaktywną przeglądarkę i dostęp do lokalnych źródeł.

## 15. Rozszerzalność po MVP

| Rozszerzenie | Punkt wpięcia | Warunek bezpieczeństwa / kompatybilności |
| --- | --- | --- |
| Inne języki | `ICodeAnalyzer` + identyfikator i wersja | Jawny zakres odczytu, limity i model kompletności |
| Fitness functions | Nowy typ wykonania i zestaw `IFindingRule` | Wersjonowanie reguł, deterministyczne wejście |
| Testy automatyczne | Osobny runner współdzielący model wyników | Rozdzielenie ręcznej sesji i wykonania skryptu |
| Analiza bezpieczeństwa | Analizator generujący standardowe problemy/dowody | Brak automatycznego wykonywania niezaufanych skanerów |
| Agenci AI | Adapter raportujący sugestie do istniejącego modelu | Domyślnie wyłączony; lokalny model lub jawna zgoda na dane opuszczające urządzenie |
| Eksport raportu | Adapter odczytujący utrwalone wyniki | Redakcja, brak sekretów, wersjonowany format |
| Izolowane buildy | Worker poza hostem i osobny execution policy | Rzeczywista izolacja OS/sieci/plików, nie tylko katalog tymczasowy |

Nie wprowadzamy w MVP ogólnego loadera dowolnych assembly ani rozproszonej magistrali. Stabilność kontraktów, jawny zakres, wersjonowane wyniki i rozdzielenie adapterów wystarczą do przyszłej rozbudowy.

## 16. Ryzyka i decyzje do zatwierdzenia

1. **Dokładność Roslyn bez MSBuild:** akceptujemy ograniczony, bezpieczny kontrakt MVP. Jeśli wymagane jest pełne budowanie dowolnej aplikacji ASP.NET/Razor/WPF, trzeba rozszerzyć zakres o zaufany lub izolowany worker.
2. **Zakres aplikacji:** domyślnie jeden origin, dodatkowe originy tylko po zgodzie. To wymaga konfiguracji dla osobnego API i SSO.
3. **Zasoby zewnętrzne:** wybór między wiernym zachowaniem aplikacji a blokadą połączeń; raport zapisuje używaną politykę.
4. **Systemy operacyjne:** Windows 11 x64 jako pierwszy formalnie wspierany pakiet; priorytet Linux/macOS do ustalenia.
5. **Dane w dowodach:** bez body, nagrań i screenshotów w bazowym MVP; nawet lokalne logi mogą zawierać dane wrażliwe.
6. **Wielkość sesji:** startowe limity i cele opóźnień wymagają walidacji na określonym sprzęcie; pod presją raport ma jawnie deklarować niepełność.
7. **Dystrybucja Chromium:** pełny pakiet offline jest większy, ale odpowiada obietnicy local-first; wymagane utrzymanie aktualizacji bezpieczeństwa i zgodności wersji.

**Rekomendowana kolejność:** zatwierdzić powyższe granice → zrealizować E1–E4 jako pierwszy użyteczny pionowy przepływ → dołożyć trwałe raporty i metryki → analizę Roslyn → testy odporności i dystrybucję. Najważniejszym kamieniem milowym pozostaje ręczna sesja Chromium z wiarygodnym monitoringiem na żywo, a nie rozbudowany silnik statyczny czy syntetyczny ranking jakości.
