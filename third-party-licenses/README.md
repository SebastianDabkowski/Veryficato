# Supplemental upstream UI license notices

Some installed npm packages omit their repository license file. These unmodified
upstream license texts supplement the notices collected by `scripts/ui-notices.mjs`.
The build itself performs no downloads.

- `signalr-LICENSE.txt`: @microsoft/signalr 10.0.0, https://github.com/dotnet/aspnetcore/blob/v10.0.0/LICENSE.txt
- `react-remove-scroll-bar-LICENSE.txt`: react-remove-scroll-bar 2.3.8 declares MIT in npm metadata and README; current upstream notice preserved from https://github.com/theKashey/react-remove-scroll-bar/blob/master/LICENSE
- `tr46-LICENSE.txt`: tr46 0.0.3 declares MIT in npm metadata; upstream notice preserved from https://github.com/Sebmaster/tr46.js/blob/master/LICENSE.md (also https://github.com/jsdom/tr46/blob/main/LICENSE.md).

Radix packages without their own copy use the shared WorkOS MIT notice already
installed with `@radix-ui/react-dialog` from the same Radix Primitives monorepo.
Original installed license files take precedence. Supplemental fallbacks are
version-scoped; review them when updating dependencies.
