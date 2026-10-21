# Repo Tasks

A desktop task board for a git repository that uses [OpenSpec](https://github.com/Fission-AI/OpenSpec). It has no database: work items, subtasks, features and notes are the files in the repo's `openspec/` folder. Every change you make in the app is a normal file edit you can review and commit with git.

- **Board and list views:** group by status, feature or contributor. Drag cards between columns, add sub-categories, merge items, and show subtasks as cards.
- **Work items:** each OpenSpec change is a work item. Edit the title, description (`proposal.md`), status, priority, features, contributors and subtasks (`tasks.md`). Moving an item to Completed archives the change with the OpenSpec CLI.
- **Notes:** markdown notes stored next to their work item. `[[doc]]`, `#wi123` and `#n123` links resolve as you type.
- **Git history:** each item's change log, plus commits that mention `#wi123`.

Built with .NET 10 and Blazor, in a [Photino](https://www.tryphotino.io/) window.

## 1. Install the prerequisites

The commands below are for **Windows 10/11**, using `winget`, which ships with Windows. Open a new terminal after installing, so `PATH` picks up the new tools.

| Tool | Needed for | Install |
|---|---|---|
| .NET 10 SDK | building and running the app | `winget install Microsoft.DotNet.SDK.10` |
| Microsoft Edge WebView2 Runtime | the app window (already on Windows 11) | `winget install Microsoft.EdgeWebView2Runtime` |
| Git | change log and linked commits | `winget install Git.Git` |
| Node.js LTS | moving items to Completed (`openspec archive`), creating `openspec/` in a repo, and the OpenSpec CLI test | `winget install OpenJS.NodeJS.LTS` |

The app still runs without Git or Node.js. Without Git, history panels say "Not a git repository". Without Node.js, archiving to Completed fails with a message saying the OpenSpec CLI couldn't be run.

Check everything is on `PATH`:

```powershell
dotnet --version   # 10.0.x
git --version
node --version
npx --version
```

## 2. Get the code and build it

```powershell
git clone <this repo's URL> repo-task-manager
cd repo-task-manager
dotnet build RepoTaskManager.slnx
```

The first build downloads the NuGet packages (Photino.Blazor, YamlDotNet, Markdig, xunit, bUnit).

Run the tests:

```powershell
# everything except the slow OpenSpec CLI check (no Node.js needed)
dotnet test TaskManager.Tests --filter Category!=OpenSpecCli

# everything, including `openspec validate --strict` on a repo the app has edited (needs Node.js, ~10 s)
dotnet test TaskManager.Tests
```

## 3. Have a repository with an `openspec/` folder

The app opens a repo that already contains `openspec/`. This repo has one, so you can try the app on itself.

To set up OpenSpec in another repo:

```powershell
cd path\to\your-repo
npx -y @fission-ai/openspec@1.14.3 init
```

## 4. Run the app

From the project root, pass the repo to open:

```powershell
dotnet run --project TaskManager -- "C:\path\to\your-repo"
```

Without a path, the app reopens the last repo you used, or else the current folder. You can switch repos any time in **Settings → Repository** (Browse… or paste a path, then Open).

If you make your own Start Menu or desktop shortcut for quicker access, point it at `TaskManager\bin\Release\net10.0\TaskManager.exe` and build with `dotnet build TaskManager -c Release` (the default `dotnet build`/`dotnet run` produce a separate `bin\Debug\` copy). A shortcut left pointing at a `Debug` build, or at an old `dist\` exe from step 5, will keep launching whatever was there last, not your latest change.

## 5. Build a standalone exe (optional)

```powershell
dotnet publish TaskManager -p:PublishProfile=dist
```

- This creates `dist\TaskManager.exe` with a `dist\wwwroot\` folder next to it. Keep them together.
- The exe is self-contained and compressed (about 37 MB, because it includes the .NET runtime), so the target PC doesn't need .NET installed. It still needs WebView2, and Git or Node.js for the features above.
- Run it with `dist\TaskManager.exe [repo path]`.
- Both profiles target Windows x64 only.

For a much smaller exe (about 3.5 MB) that uses the .NET runtime installed on the PC:

```powershell
winget install Microsoft.DotNet.DesktopRuntime.10   # once, on the target PC
dotnet publish TaskManager -p:PublishProfile=dist-small
```

This adds `dist\TaskManager-small.exe`, which shares the same `dist\wwwroot\`.

## Where things are stored

| Data | Location | In git? |
|---|---|---|
| Work items, subtasks, specs | standard OpenSpec files in `openspec/` | yes |
| Item metadata (number, title, status, priority, contributors, lanes, hidden) | `x-rtm:` block in each change's `.openspec.yaml` | yes |
| Board sub-categories, new feature sub-groups | `x-rtm:` block in `openspec/config.yaml` | yes |
| Notes | `openspec/changes/<change>/notes/n<id>.md` (they move with the change when it's archived) | yes |
| Your settings (theme, last repo, name, board view per repo) | `%APPDATA%\RepoTaskManager\settings.json` | no |

Set the `RTM_SETTINGS` environment variable to a file path to use a different settings file, for example for demos.

## Project layout

A modular monolith with one static class library per domain entity, plus the app host:

- `OpenSpecStore/`: repo folder, safe file reads and writes, the `x-rtm` YAML block, the file watcher
- `GitHistory/`: git CLI wrapper
- `Features/`, `WorkItems/`, `Notes/`: the domain modules
- `TaskManager/`: the Photino + Blazor app, the only project with UI
- `TaskManager.Tests/`: xunit and bUnit tests, plus a sample repo in `Fixtures/`
- `openspec/`: this project's own specs and changes
- `design/mockup.html`: the design system mockup

## Notes

Deleting a work item doesn't remove it from OpenSpec. It only hides it from the app (`x-rtm.hidden: true`) until the workflow matures. Tick "show hidden" under **view** on the board and use **Restore** on the item's page to bring it back.

Only Windows has been tested so far. Photino also supports macOS and Linux (Linux needs WebKitGTK), but the app hasn't been run there.

## Acknowledgements

The design system was inspired by Pharrell Tan's work: https://dribbble.com/shots/16333822-Personal-Task-Management-App. Please keep this acknowledgement if you clone this project, unless you make your own theme.
