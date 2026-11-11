# Verificato 0.1 — stan implementacji

Ten dokument opisuje rzeczywisty zakres kodu. `plan-techniczny-mvp.md` pozostaje docelowym planem, nie deklaracją wykonania każdego punktu.

## Dostarczony pionowy przepływ

Projekt → sesja Playwright → ręczna interakcja → obserwacje zapisane w SQLite → SignalR/REST → problemy i metryki → stop → trwały raport. Osobny runner dostarcza analizę Roslyn bez wykonywania kodu projektu. UI zawiera dashboard, historię, filtry, dowody tekstowe, ustawienia i dwa motywy. Dodatkowo analiza kodu tworzy trwałą strukturalną projekcję C4/C3: projekt C# = komponent, referencje z dowodami i interaktywny inspektor z bezpiecznym podglądem kodu. Szczegóły: [Architektura C4](architecture-c4.md).

Kod jest podzielony na Host, Core, Infrastructure, Browser i Analysis.CSharp. Kontrakty analizatora i runnera przeglądarki umożliwiają kolejne adaptery. Nie ma dynamicznego loadera niezaufanych wtyczek.

## Świadome uproszczenia względem planu

- SQLite obsługiwane bezpośrednio przez `Microsoft.Data.Sqlite`, bez EF Core. Schemat v2 ma jawne `user_version`, FK, indeksy i transakcyjny zapis obserwacji/problemu. Migracja v1 → v2 dodaje `ArchitectureSnapshots` i wykonuje spójną kopię SQLite Backup API przed zmianą.
- `Projects`, `Runs`, `Observations`, `Issues` to tabele z identyfikatorami/kolumnami indeksowanymi i wersjonowanym na poziomie silnika JSON DTO. Metryki są typowanymi obserwacjami; dowody tekstowe są częścią obserwacji/problemów. Brak tabeli plików i magazynu screenshotów.
- Bufor grupuje powiadomienia, ale każda obserwacja jest osobną krótką transakcją SQLite. Pełny zapis wsadowy i szersze testy obciążeniowe są dalszą pracą.
- Brak archiwizacji, automatycznej retencji, wskazywania katalogu natywnym dialogiem, eksportu raportów i automatycznych aktualizacji. Eksport pojedynczego diagramu C3 do Structurizr DSL jest dostępny.
- UI pokazuje 100 ostatnich uruchomień i 1000 ostatnich zdarzeń. API stronicuje zdarzenia; lista uruchomień ma limit (maks. 200), bez pełnej paginacji historii.
- Próg wolnego żądania jest stały: 1000 ms. Polityka zewnętrznych zasobów: zezwalaj na ładowanie bez raportowania szczegółów. Przełączniki tych polityk pozostają do dodania.
- Roslyn celowo wspiera semantycznie tylko prosty, pojedynczy SDK-style projekt .NET 10 bez referencji/pakietów i niestandardowego kontekstu. Pozostałe projekty mają składnię/reguły/metryki i jawne `Partial`. Nie ma MSBuildWorkspace, restore ani builda.
- Diagram jest projekcją struktury, nie dowodem architektury runtime. Granice kontenerów są wywnioskowane; brak ręcznej edycji odpowiedzialności/granic, ewaluacji MSBuild, analizy reflection/call graph i adapterów innych języków. Podgląd kodu wymaga zgodności hasha z migawką, nie przechowuje pełnych historycznych źródeł.
- Parametry kompilacji wspieranego kontekstu są ograniczone do opisanych w README; nie symulujemy dowolnych właściwości MSBuild.
- Bieżące metryki FCP/LCP/CLS są zbierane po load i podczas kończenia otwartych stron. Zamknięcie lub przejście do kolejnego dokumentu może uniemożliwić finalny pomiar poprzedniego. Wszystkie wartości to pomiary laboratoryjne, nie pełne Web Vitals.
- Pierwsza platforma testowana lokalnie to Windows x64. Linux/macOS, podpisane wydania, pełny pakiet offline i długotrwałe obciążenie wymagają odrębnego release qualification.
- Produkt ma licencję MIT; wcześniejsze Apache-2.0 w planie było propozycją.

## Zabezpieczenia obecne w kodzie

- Loopback, sprawdzanie Host/Origin, HttpOnly/SameSite cookie, jednorazowy bootstrap, CSRF dla mutacji REST, uwierzytelnienie wszystkich transportów huba.
- Brak sekretów w URL query i domyślnych logach; drukowanie bootstrap jest odrębną jawną opcją.
- CSP, renderowanie dowodów jako tekst, bez body żądań/odpowiedzi i formularzy.
- Redakcja credentials, query/fragmentów URL i rozpoznanych sekretów z tekstu.
- Izolowany kontekst Chromium i blokada service workers. Ścisły zakres originów; nawigacje sieciowe poza nim są blokowane.
- Pomijanie symlinków/reparse points w analizie, brak zapisu do źródeł i wykonywania MSBuild/generatorów/analyzatorów repozytorium.
- Ograniczona kolejka, limit liczby/rozmiaru obserwacji, czasu sesji i źródeł. Niepełne wykonanie nie dostaje pełnego zielonego wyniku.
- Koordynator serializuje start/stop, edycję/usunięcie projektu i usuwanie uruchomień. Blokada katalogu danych zapobiega dwóm hostom otwierającym tę samą bazę.

To nie jest pełny sandbox systemowy ani ochrona przed procesem działającym jako ten sam użytkownik. Ograniczenia opisano w README.

## Weryfikacja

Do uruchomienia przed wydaniem:

1. Build UI i rozwiązania .NET.
2. Testy .NET z rzeczywistym SQLite, Roslyn oraz Chromium.
3. Testy formatowania UI i E2E pełnego przepływu oraz lokalnej autoryzacji.
4. Audyt zależności npm i NuGet.
5. Smoke test publish na wspieranym Windows, w tym osobno tryb headed.
6. Przed oznaczeniem wydania jako stabilne: godzinny test obciążenia, kontrola pamięci/dysku, odłączenie sieci, awaria hosta z aktywnym Chromium, weryfikacja redystrybucji Chromium i zależności.

W pierwszej lokalnej weryfikacji na Windows x64 przeszły: build UI/.NET, testy .NET (w tym Chromium), testy UI, oba scenariusze E2E, audyty npm/NuGet oraz przygotowanie paczki self-contained win-x64 i jej smoke test z rzeczywistym headed Chromium. Środowisko miało SDK 11 RC i runtime/targeting pack .NET 10; projekty i paczka są skierowane do .NET 10. Nie był to test na czystej maszynie ani walidacja pakietu z dołączonym Chromium.

Sam pozytywny build/test jednostkowy nie oznacza spełnienia wszystkich docelowych kryteriów wydajności i dystrybucji z planu.

## Najbliższe niezależne rozszerzenia

1. Pełna paginacja historii i serwerowe filtry problemów, bez przesyłania całego raportu przy każdym odświeżeniu.
2. Rzeczywisty zapis paczek zdarzeń i benchmark godzinnej sesji.
3. Konfigurowalne progi, retencja, użycie dysku i backup przez SQLite Backup API.
4. Dokładniejsze przechwytywanie metryk przy opuszczaniu dokumentów i rozszerzone przypadki SPA/PWA.
5. Ograniczony loader referencji między projektami/pakietów jako metadanych, z zachowaniem braku wykonania kodu.
6. Osobny, wyraźnie włączany i faktycznie izolowany worker dla pełnych buildów.
