# Third-party components

Verificato itself is licensed under MIT. Dependencies retain their own licenses.
The lockfile and .csproj files identify the exact versions used.

| Component | License / notices |
| --- | --- |
| .NET / ASP.NET Core / Microsoft.Data.Sqlite | MIT; see the corresponding NuGet package license files |
| SQLite | Public domain; bundled SQLitePCLRaw components retain their respective licenses |
| SQLitePCLRaw | Apache-2.0; native SQLite is public domain |
| Microsoft.Playwright | Apache-2.0; license is included in the NuGet package and driver distribution |
| Chromium / Chrome for Testing | BSD-style project license plus third-party notices included in the downloaded browser distribution |
| Microsoft.CodeAnalysis.CSharp (Roslyn) | MIT |
| React, Vite, Tailwind CSS, Radix UI, shadcn/ui patterns | MIT |
| SignalR JavaScript client | MIT |
| @xyflow/react (React Flow), @dagrejs/dagre and graphlib | MIT; upstream notices included in wwwroot/UI-THIRD-PARTY-LICENSES.txt in packaged builds |
| lucide-react | ISC |
| xUnit, Vitest, Playwright Test | Their upstream package licenses apply; development/test tools are not part of the frontend runtime |

Do not strip license/notice files from `.playwright`, native libraries, or bundled
browser directories when packaging. The downloaded browser distributions include
additional notices for their third-party components; this summary is not a replacement
for those notices. Review upstream redistribution conditions before publishing a binary release.
