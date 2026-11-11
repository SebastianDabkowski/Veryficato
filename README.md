# Verificato

**Open-source, local-first software verification.** Ręcznie testuj aplikację w kontrolowanym Chromium, obserwuj problemy na żywo i analizuj kod C# bez wykonywania badanego projektu.

Pierwsza działająca wersja: **0.1.0**. Interfejs jest w języku polskim. Brak konta, aktywacji, zewnętrznego backendu i telemetrii.

## Co działa

- Tworzenie, edycja i usuwanie projektów: nazwa, lokalny katalog, URL i dozwolone originy.
- Widoczna przeglądarka Chromium z nowym izolowanym kontekstem na każdą sesję; użytkownik obsługuje aplikację samodzielnie.
- Błędy JavaScript, ostrzeżenia/błędy konsoli, HTTP 4xx/5xx, błędy sieci i zasobów, czasy żądań oraz ostrzeżenia dla odpowiedzi > 1000 ms.
- Aktualizacje SignalR, odczyt REST z kursorem i synchronizacja po rozłączeniu. Zamknięcie panelu nie kończy pracy hosta.
- Dashboard, filtry, szczegóły dowodów, deduplikacja problemów, historia w SQLite i układ mobilny. Domyślnie ciemnogranatowy motyw z niebieskimi akcentami; opcjonalny jasny wariant zachowuje tę samą niebieską paletę.
- Metryki dokumentu: DOMContentLoaded, load, FCP oraz dostępne LCP/CLS. Mediana i p95 czasów zakończonych żądań z dostępnym timingiem.
- Bezpieczna analiza Roslyn: składnia, ograniczona diagnostyka semantyczna, puste `catch`, złożoność metod, liczba plików, typów, metod i fizycznych linii.
- Interaktywna **Architektura C4 / C3**: projekt C# = komponent, kierunkowe `ProjectReference`, wywnioskowane granice kontenera, inspektor typów/metod/plików i lokalny podgląd kodu z liniami. Trwałe migawki, filtrowanie testów i eksport Structurizr DSL.
- Stop/anulowanie, limity kolejki i sesji, jawna niepełność wyników oraz oznaczanie przerwanych wykonań po awarii hosta.

**To nie jest certyfikat jakości.** Wyniki dotyczą odwiedzonych widoków, obserwowanych żądań i jawnie obsługiwanego zakresu analizy.

## Szybki start — Windows

Wymagane: **.NET 10 SDK**, **Node.js 24 LTS**, **PowerShell 7**. Wersja docelowa aplikacji to `net10.0`; `global.json` pozwala również użyć nowszego zainstalowanego SDK. Aplikację do testowania uruchom samodzielnie.

Z katalogu głównego repozytorium:

```powershell
# Instalacja zależności UI.
npm --prefix src/Verificato.UI ci

# Jednorazowa instalacja Chromium zgodnego z Playwright .NET.
pwsh scripts/install-browser.ps1

# Budowanie UI i hosta, uruchomienie oraz automatyczne otwarcie panelu.
pwsh scripts/start.ps1
```

Launcher otwiera panel pod `http://127.0.0.1:5078` z jednorazowym sekretem we fragmencie URL. UI usuwa sekret z historii adresu i ustanawia lokalną sesję. **Otwarcie samego adresu bez launchera nie ustanawia nowego dostępu.** Nie zamieniaj `127.0.0.1` na `localhost` w adresie panelu.

Następnie:

1. Uruchom badaną aplikację, np. pod `http://localhost:5173`.
2. Utwórz projekt: nazwa, bezwzględna ścieżka źródeł i adres aplikacji bez parametrów/tokenów.
3. Jeśli aplikacja używa osobnego API, dodaj jego origin, np. `http://localhost:5000`.
4. Wybierz **Rozpocznij sesję**. Korzystaj z osobnego okna Chromium; panel Verificato pokazuje zdarzenia na żywo.
5. Zatrzymaj sesję i wróć do raportu w historii. Osobno możesz wybrać **Analizuj kod**.

6. Otwórz **Architektura C4 → Zbuduj diagram architektury**. Klikaj komponenty, typy i metody, aby przechodzić do kodu. Po zmianach w źródłach wybierz **Odśwież diagram**. [Opis konwencji C4 i ograniczeń](docs/architecture-c4.md).

Zatrzymanie hosta: `Ctrl+C`. Tylko procesy należące do sesji Verificato są zamykane. Sesja dostępu do panelu wygasa po 12 godzinach lub restarcie hosta; link startowy jest jednorazowy i ważny 10 minut.

Opcje:

```powershell
pwsh scripts/start.ps1 -Port 5088 -DataDirectory C:\VerificatoData

# Tylko przy ręcznym otwieraniu panelu: jawnie wyświetla sekret w terminalu!
pwsh scripts/start.ps1 -NoOpen -PrintBootstrap
```

Nie publikuj linku bootstrap ani terminala z tym linkiem. Domyślnie sekret nie jest drukowany.

## Lokalna aplikacja demonstracyjna

W osobnym terminalu:

```powershell
node tests/Fixtures/BrowserTarget/server.mjs
```

Utwórz projekt z URL `http://127.0.0.1:5199` i np. katalogiem `tests/Fixtures/DotNetProjects/Simple` jako **bezwzględną** ścieżką. Przez sesję Verificato otwórz fixture i klikaj przyciski: wyjątek JS, konsola, HTTP 404/500, wolna odpowiedź, błąd sieci, popup i niedozwolona nawigacja.

Przykładowy projekt C# zawiera celowe problemy. Ponieważ repozytorium ma `Directory.Build.props`, analizator prawidłowo oznaczy ten kontekst jako częściowy. Aby sprawdzić diagnostykę semantyczną prostego projektu, skopiuj sam katalog `Simple` do oddzielnego katalogu poza drzewem z plikami `Directory.Build.*`.

## Development i inne platformy

```powershell
pwsh scripts/dev.ps1
```

Vite: `http://127.0.0.1:5180`, host: `http://127.0.0.1:5078`. Skrypt czeka na Vite, uruchamia host, otwiera uwierzytelnione UI i sprząta własny proces Vite. Proxy przekazuje `/api` i `/hubs`. Autoryzacja nie jest wyłączana w development.

Odpowiedniki bash:

```bash
npm --prefix src/Verificato.UI ci
bash scripts/install-browser.sh     # wymaga PowerShell 7
bash scripts/start.sh
# albo: bash scripts/dev.sh
```

Linux może wymagać instalacji zależności systemowych: `bash scripts/install-browser.sh --with-deps`. Pierwszą zweryfikowaną platformą jest **Windows x64**. Skrypty Unix nie są deklaracją przetestowania dystrybucji na Linux/macOS.

## Granice analizy Roslyn

Analizator **nie uruchamia `dotnet build`, restore, MSBuildWorkspace, source generatorów ani analizatorów z badanego repozytorium**. Nie zapisuje plików w analizowanym drzewie. Referencje odczytuje tylko jako metadane z lokalnego targeting packa .NET 10.

Diagnostyka semantyczna jest dostępna dla dokładnie jednego prostego projektu w katalogu głównym:

- `Sdk="Microsoft.NET.Sdk"`, pojedyncze `TargetFramework=net10.0`;
- wyłącznie wspierane właściwości: `TargetFramework`, `ImplicitUsings`, `Nullable`, `OutputType`;
- bez pakietów, referencji między projektami, niestandardowych importów/targetów/warunków i nadrzędnych `Directory.Build.props/targets`;
- kompilacja w zdefiniowanym kontekście Release/C# 14, bez wykonywania kodu.

Projekty ASP.NET/Razor, WPF, wieloprojektowe i niestandardowe MSBuild otrzymują **analizę składni, zaufanych reguł i metryk z oznaczeniem `Partial`**, bez lawiny błędów wynikających z brakujących referencji. Brak targeting packa również oznacza analizę częściową, nie automatyczne pobieranie zależności. Lista ograniczeń i fingerprint źródeł są zapisywane w raporcie.

LOC to fizyczne linie Roslyn `SourceText.Lines`, w tym puste i komentarze. Złożoność to 1 plus liczba jawnie liczonych rozgałęzień: `if`, pętle, `catch`, `?:`, etykiety `case`, `&&`, `||`, `??`. To podstawowa metryka, nie pełen audyt jakości.

## Prywatność i ograniczenia monitorowania

- Host jest dostępny tylko na loopback; sprawdza Host/Origin, lokalną sesję i CSRF. Nie ma zdalnego dostępu ani obowiązkowego konta.
- Domyślny zakres to dokładny origin aplikacji. Inny port, schemat lub `localhost` zamiast `127.0.0.1` wymaga osobnego wpisu.
- Nawigacje sieciowe/popupy poza zakresem są blokowane. Zewnętrzne zasoby mogą się ładować, ale szczegóły ich żądań nie trafiają do raportu. To nie jest firewall ani całkowity tryb offline dla testowanej aplikacji.
- Service workers są blokowane dla przewidywalnego monitorowania. Aplikacje PWA mogą zachowywać się inaczej. Brak monitorowania ramek WebSocket i pełnej instrumentacji workerów.
- Nie zbieramy body żądań/odpowiedzi, nagrań formularzy, cookies, haseł z formularzy, screenshotów ani wideo.
- URL-e są pozbawiane credentials, query i fragmentu. Tekst konsoli i stosów przechodzi ograniczanie długości i redakcję rozpoznanych sekretów; nie gwarantuje to wykrycia wszystkich danych wrażliwych.
- Migawki architektury zawierają nazwy, ścieżki, sygnatury i fragmenty dokumentacji, nie pełne pliki źródłowe. Podgląd oryginalnego kodu jest lokalny, na żądanie i może zawierać sekrety. Sprawdź metadane/eksport DSL przed udostępnieniem.
- Czysty profil przeglądarki nie jest izolacją VM. Nie analizuj złośliwych aplikacji w środowisku z poufnymi danymi; lokalny złośliwy proces działający jako ten sam użytkownik pozostaje poza gwarancjami ochrony.
- Metryki wydajności są laboratoryjne i dotyczą konkretnego dokumentu, nie pełnego Lighthouse ani danych terenowych Core Web Vitals.

## Dane i limity

Domyślny katalog danych: `%LOCALAPPDATA%\Verificato` na Windows lub odpowiednik `LocalApplicationData` danego systemu. SQLite używa WAL i kluczy obcych. Baza pozostaje poza źródłami badanego projektu. Blokada instancji nie pozwala dwóm hostom otworzyć tego samego katalogu danych.

- Jedna aktywna sesja Chromium oraz jedna analiza kodu na instancję.
- Sesja do 60 minut, analiza do 10 minut.
- Maksymalnie 100 000 obserwacji i budżet 100 MB zserializowanych obserwacji na wykonanie. To **nie** jest gwarancja rozmiaru pliku bazy: indeksy, problemy i WAL zajmują dodatkowe miejsce.
- Kolejka 4096 pozycji z rezerwą dla błędów/ostrzeżeń. Pominięte obserwacje oznaczają raport jako niepełny.
- Analiza: do 10 000 plików C#, 2 MB na plik, 50 MB źródeł, głębokość do 64 katalogów.
- Architektura: do 200 projektów, 10 MB metadanych `.csproj`, 5000 referencji, 25 MB JSON/migawkę. [Pełne limity i zakres](docs/architecture-c4.md).
- UI pokazuje ostatnie 1000 zdarzeń bieżącego wykonania i ostatnie 100 uruchomień projektu. Pełne zapisane zdarzenia można odczytać przez API z kursorem.

Schemat SQLite v2 dodaje migawki architektury. Przy migracji z v1 powstaje spójna kopia `verificato.pre-v2-<guid>.db` przez SQLite Backup API (łącznie z WAL). Jest lokalna; usunięcie raportów nie usuwa automatycznie kopii migracyjnej.

Historia jest usuwana wyłącznie na żądanie. Nie ma jeszcze automatycznej retencji ani compact/VACUUM; usunięcie rekordów pozwala SQLite ponownie użyć miejsca, ale nie musi zmniejszyć pliku. Kopię danych wykonuj przy wyłączonym hoście, uwzględniając pliki bazy/WAL. Nie kopiuj wyłącznie aktywnego `verificato.db`.

## Testy

Po zbudowaniu UI i instalacji Chromium:

```powershell
dotnet test Verificato.slnx
npm --prefix src/Verificato.UI test
npm --prefix src/Verificato.UI run test:e2e
```

Testy .NET obejmują prawdziwe SQLite, redakcję, originy, CSRF, idempotencję, cykl życia uruchomień, analizę tylko do odczytu i rzeczywisty Chromium. E2E uruchamiają izolowany host i fixture na lokalnych portach, tworzą projekt, sprawdzają live monitoring, raport po odświeżeniu, Roslyn, motywy, układ mobilny oraz diagram C4 z dowodami referencji, nawigacją do kodu, eksportem DSL i konfliktem po zmianie pliku. Testowa przeglądarka jest headless; sesje użytkownika są headed.

Tylko testy bez Chromium:

```powershell
dotnet test Verificato.slnx --filter 'Category!=Browser'
```

Weryfikacja zależności:

```powershell
npm --prefix src/Verificato.UI audit
dotnet list Verificato.slnx package --vulnerable --include-transitive
```

Workflow CI: `.github/workflows/ci.yml`.

## Dystrybucja

```powershell
# Zwykła paczka self-contained .NET.
pwsh scripts/package.ps1 -Runtime win-x64

# Większy wariant z Chromium, do uruchamiania bez pobierania przeglądarki.
pwsh scripts/package.ps1 -Runtime win-x64 -IncludeChromium
```

Wynik: `artifacts/verificato-win-x64`. Uruchom `launch.cmd`. Bez dołączonego Chromium najpierw uruchom w paczce `install-packaged-browser.ps1` przez PowerShell 7. Sam runtime .NET i Node.js nie są potrzebne do uruchomienia paczki; **targeting pack .NET 10 pozostaje wymagany dla diagnostyki semantycznej źródeł**.

Test funkcjonalny gotowej paczki na lokalnym Windows (wymaga developerskich zależności UI/Playwright):

```powershell
node tests/smoke-package.mjs --headed
```

Pełny pakiet zachowuje pliki Playwright i zawartość dystrybucji przeglądarki. Nie używamy trimming/AOT. Aktualizacje są ręczne; przed wymianą wersji zatrzymaj host i wykonaj kopię danych. Baza o nowszej wersji schematu jest odrzucana. Publiczne wydania wymagają osobnej weryfikacji licencji/redystrybucji i podpisywania binariów.

## Architektura i dokumentacja

```text
React UI ── REST + SignalR ── ASP.NET Core Host
                                  ├── Core (kontrakty i modele)
                                  ├── Infrastructure (SQLite)
                                  ├── Browser (Playwright)
                                  └── Analysis.CSharp (Roslyn)
```

- [Plan techniczny i funkcjonalny](docs/plan-techniczny-mvp.md)
- [Stan implementacji i dalsze prace](docs/implementation-status.md)
- [Architektura C4/C3: obsługa, konwencje i bezpieczeństwo](docs/architecture-c4.md)
- [Kontrakty API](docs/api.md)
- [Zgłaszanie problemów bezpieczeństwa](SECURITY.md)
- [Współtworzenie](CONTRIBUTING.md)

Licencja: [MIT](LICENSE). Zależności: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
