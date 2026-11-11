# Architektura aplikacji — C4, poziom C3

## Używanie

1. Wybierz projekt Verificato, którego katalog źródeł obejmuje aplikację C# i jej biblioteki.
2. Otwórz **Architektura C4 → Zbuduj diagram architektury**. Zwykłe **Analizuj kod** również tworzy migawkę diagramu.
3. Wybierz pojedynczy kontener z listy. Kliknij komponent, aby otworzyć opis, typy i członków, pliki, zależności przychodzące/wychodzące oraz pakiety NuGet.
4. Kliknij deklarację typu/metody lub plik, aby zobaczyć kod z numerami linii. Kliknięcie relacji lub przycisku dowodu zależności wskazuje konkretny `ProjectReference` i otwiera jego XML.
5. Użyj kółka, przeciągania tła, kontrolek zoom/dopasowania, minimapy lub pełnego ekranu. Wyszukiwanie przygasza niedopasowane komponenty, zachowując kontekst grafu.
6. Po zmianach w repozytorium wybierz **Odśwież diagram**. Samo ponowne wejście do widoku odczytuje ostatnią migawkę, nie skanuje źródeł automatycznie.

Motyw ciemnogranatowy z niebieskimi akcentami pozostaje domyślny. Widok działa też w motywie jasnym i na mniejszych ekranach; inspektor trafia wtedy pod diagram.

## Konwencja C4

Punktem odniesienia jest [Component diagram w C4 Model](https://c4model.com/diagrams/component). To **strukturalna projekcja C3**, nie automatyczne odtworzenie wszystkich odpowiedzialności i zależności runtime:

- **1 projekt `.csproj` = 1 komponent.** Każdy ma nazwę, stereotyp Component, technologię i opis. Opis pochodzi z dokumentacji XML publicznych deklaracji albo jawnej inwentaryzacji plików/typów/przestrzeni nazw — bez AI i wymyślania roli biznesowej.
- Diagram C3 pokazuje **jeden kontener**. Projekty `Microsoft.NET.Sdk.Web` oraz `OutputType=Exe/WinExe` wyznaczają kandydatów na punkty startowe; ich osiągalne, nietestowe referencje tworzą wywnioskowaną granicę. Projekt startowy sam jest komponentem kompozycji. Współdzielone biblioteki mogą wystąpić w kilku takich projekcjach.
- Gdy brak punktu startowego, używana jest jawnie oznaczona granica logiczna. Wszystkie granice są przerywane i **wymagają potwierdzenia przez autora aplikacji**; deklaracje projektowe nie dowodzą sposobu wdrożenia.
- Strzałka **od projektu referującego do referowanego**, z opisem „Odwołuje się do”, oznacza `C# / ProjectReference`. To deklarowana zależność kompilacyjna, nie odtworzone wywołanie HTTP/RPC/metody. Dowód zawiera plik i linię.
- Warunki `Condition`, również z nadrzędnych grup, pozostają nieewaluowanym tekstem. Relacje warunkowe mają przerywaną linię i osobne oznaczenie w legendzie.
- Projekty testowe są domyślnie wyłączone. Ich włączenie przełącza zakres na **projekcję repozytorium**, a nie fałszywy pojedynczy kontener C3. To samo dotyczy ręcznego wyboru „Wszystkie projekty”.
- NuGet nie jest automatycznie komponentem C4. Pakiety są szczegółami komponentu. Nierozwiązane referencje są opisane osobno, bez fikcyjnych węzłów i krawędzi.

**Eksport C4 DSL** pobiera lokalny plik Structurizr DSL dla wybranego pojedynczego kontenera. Jest wyłączony dla zbiorczej projekcji/testów. Eksport nie otwiera ani nie wysyła danych do Structurizr lub innych usług; decyzja o późniejszym udostępnieniu pliku należy do użytkownika.

## Bezpieczna ekstrakcja

`ArchitectureAnalyzer` używa bezpiecznego parsera XML (DTD i resolver zewnętrzny wyłączone) i składni Roslyn. Nie wywołuje MSBuild, restore, build, generatorów ani kodu analizowanego repozytorium. Nazwy deklaracji, sygnatury, komentarze i pakiety nie są wykonywane. UI renderuje je jako tekst.

Pliki C# są przypisywane do najbliższego projektu nadrzędnego według fizycznego katalogu. Kilka projektów w tym samym katalogu otrzymuje te same źródła z ostrzeżeniem. Nie ewaluujemy `Compile Include/Remove`, importów, linked files, centralnego zarządzania pakietami, symboli preprocesora, frameworków warunkowych ani source generatorów. Deklaracje są czytane jako C# 14. Dynamika, refleksja i integracje sieciowe nie wynikają z `ProjectReference`.

Pomijane są linki/reparse points, `.git`, `bin`, `obj`, `node_modules`, `dist`, `artifacts`, `.vs`, `.idea`, `packages` i `.data`. Ścieżki referencji poza zatwierdzonym korzeniem oraz cele wymagające ewaluacji MSBuild nie są odczytywane.

### Limity

- 200 projektów, 1 MB na `.csproj`, 10 MB metadanych `.csproj` łącznie;
- 10 000 plików C#, 2 MB/plik i 50 MB źródeł;
- 60 000 odwiedzonych wpisów systemu plików i głębokość 64;
- 30 000 deklaracji typów i 60 000 zinwentaryzowanych członków;
- podgląd inwentarza: 200 typów/plik i 200 członków/typ; ucięcia są oznaczane;
- do 500 pakietów/projekt i 5000 referencji projektowych;
- 25 MB JSON na migawkę, limit egzekwowany podczas serializacji;
- maks. 120 linii kodu na odpowiedź i 4000 znaków na linię; dłuższe linie są jawnie ucięte;
- wspólny limit analizy: 10 minut i możliwość anulowania.

Nieobsługiwany/pominięty zakres ma ostrzeżenie. Limity krytyczne kończą wykonanie błędem; ostatnia zapisana migawka pozostaje dostępna. Nie powstaje fikcyjny „kompletny” diagram z uciętymi krawędziami.

## Migawki, kod i prywatność

Migawka ma identyfikator projektu/uruchomienia, datę, generator, wersję schematu, fingerprint źródeł i hashe plików. Jest zapisywana w `ArchitectureSnapshots` w SQLite; usunięcie projektu/uruchomienia usuwa jego migawki kaskadowo. API może odczytać najnowszą lub wskazaną migawkę.

**Pełne treści plików nie są archiwizowane w bazie.** Migawka zawiera jednak wrażliwe metadane źródeł: nazwy, lokalizacje, sygnatury i fragmenty dokumentacji XML (z redakcją best-effort). Przed udostępnieniem bazy, eksportu lub zrzutu ekranu sprawdź te dane.

Podgląd oryginalnego kodu jest lokalnym odczytem na żądanie i może zawierać sekrety. API przyjmuje wyłącznie identyfikator pliku należącego do migawki, nie dowolną ścieżkę. Ponownie sprawdza korzeń/linki oraz hash treści. Po zmianie/usunięciu pliku albo zmianie katalogu projektu zwraca konflikt i prosi o ponowną analizę. To nie jest historyczny VCS viewer: stary kod nie będzie odtwarzany, jeżeli zmienił się na dysku.

Schemat SQLite **v2** dodaje migawki. Migracja z v1 wykonuje najpierw spójną kopię przez SQLite Backup API, także z zawartością WAL: `verificato.pre-v2-<guid>.db`. Kopia jest lokalna i nie jest automatycznie usuwana przy kasowaniu raportów; również wymaga ochrony i świadomej retencji.

## Implementacja i testy

- `src/Verificato.Core/Architecture.cs` — kontrakty i model migawki.
- `src/Verificato.Analysis.CSharp/ArchitectureAnalyzer.cs` — ekstrakcja i kontrolowany podgląd.
- `src/Verificato.Infrastructure/SqliteVerificationStore.cs` — schemat v2, kopia migracyjna i trwałość.
- `src/Verificato.Host/Program.cs`, `RunCoordinator.cs` — chronione API i wykonanie w cyklu analizy.
- `src/Verificato.UI/src/features/architecture/` — React Flow, layout/routing Dagre, inspektor, viewer i eksport DSL. Moduł jest ładowany dopiero po wejściu do widoku.
- `tests/Verificato.Tests/ArchitectureTests.cs` — ekstrakcja, kierunek/dowody, cykle, brak wykonywania MSBuild, DTD, zgodność hasha, limity, migracja i kaskady.
- `src/Verificato.UI/src/features/architecture/graph.test.ts`, `e2e/workspace.spec.ts` — zakresy kontenera/testów, layout, eksport, nawigacja do kodu, tekst zamiast HTML, konflikt po zmianie pliku i dopasowanie do ekranu.

Dalszy rozwój: ręczne potwierdzanie/edycja granic i odpowiedzialności, adaptery innych języków, bezpieczne wzbogacanie zależności semantycznych oraz porównywanie migawek. Nie deklarujemy wykrywania pełnej architektury wdrożeniowej na podstawie samych projektów.
