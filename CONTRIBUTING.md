# Contributing to PulseCheck

Thanks for helping. Bug reports, translations and new checks are all welcome.

## Build and test

Requires Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet test PulseCheck.sln -c Release   # unit tests
cd src; dotnet run -c Release           # run the app
```

Useful flags: `--lang fr`, `--theme dark`, `--screenshot out.png [--speed] [--scroll 800]`.

## Principles

- **Read-only.** A check must never change the user's system.
- **Local.** No telemetry, no uploads. Any new network request must be added to the privacy policy.
- **Plain language.** Every warning needs a short, concrete fix.

## Add a check

Checks live in `src/Scanner.cs`. Add a method, call it from `RunAsync`, and use message keys
(`n.*` name, `d.*` detail, `f.*` fix) that you add to **every** file in `src/Lang`.
The tests fail if a key is missing in any language or a `{0}` placeholder is lost.

## Add a language

1. Copy `src/Lang/en.json` to `src/Lang/<code>.json` and translate the values.
2. Add the code and its name to `Loc.Codes` / `Loc.Names` in `src/Models.cs`.
3. For the website, copy `site/lang/en.json` to `site/lang/<code>.json`, translate it, and add the code
   to `$langs` and `$locales` in `site/build.ps1`.
4. Run `dotnet test`, then `powershell -File site/build.ps1`.

## Website

`docs/` is generated. Edit `site/templates`, `site/lang` or `site/style.css`, then run
`powershell -File site/build.ps1` and commit the result. Preview with `powershell -File site/serve.ps1`.

## Releases

Maintainers push a tag like `v1.1.0`. GitHub Actions builds, tests, hashes and attests the executable.
