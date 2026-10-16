# Contributing

Thanks for helping WinLaunch Enhanced. Bug reports, layout fixes and launcher ideas are all welcome.

## Ways to help

- Report pin, layout and launch bugs with the steps to reproduce them
- Send fixes for the WPF app and its tooling
- Improve the data checks and preview tooling

## Getting set up

WinLaunch Enhanced targets .NET 10. Build with:

```
dotnet build src/WinLaunch.Enhanced/WinLaunch.Enhanced.csproj
```

Run the app with:

```
dotnet run --project src/WinLaunch.Enhanced
```

Pass a folder argument so the app does not touch live data.

## Checks before a pull request

- Run the data check:

```
dotnet run --project tools/VerifyData
```

- Run the app with the `--smoke` flag for a quick start check
- Render previews with `tools/PreviewRender`, adding `-- --update-test` to refresh test images
- For packaging changes, run `pwsh packaging\build.ps1`

## Where to submit

Open issues and pull requests in this repository. Keep one change per pull request where you can.