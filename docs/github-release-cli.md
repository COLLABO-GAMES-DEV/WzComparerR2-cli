# GitHub Release Guide for WzComparerR2 CLI

This guide uploads the Windows x64 self-contained CLI zip to GitHub Releases.

## Artifact

Current local artifact:

```text
artifacts/wcr2-win-x64-self-contained.zip
```

SHA-256:

```text
f19329f66a5941b85de62cf9e7b23825533056d4263364ad81e28a95561f2257
```

## Before Release

Make sure the CLI code and docs are committed and pushed before creating the release tag.

```bash
git status --short
git add README.md WzComparerR2.sln azure-pipelines.yml docs samples WzComparerR2.Cli.Tests research.md todo.md
git commit -m "Prepare CLI preview release"
git push origin HEAD
```

Use the repository you want to publish to:

```bash
gh repo view COLLABO-GAMES-DEV/WzComparerR2-cli
```

If authentication is missing:

```bash
gh auth login
```

## Recommended Tag

Use a preview tag while the CLI is still being validated against real MapleStory clients:

```text
wcr2-cli-v0.1.0-preview
```

## Upload With GitHub CLI

Create a draft pre-release first:

```bash
gh release create wcr2-cli-v0.1.0-preview \
  artifacts/wcr2-win-x64-self-contained.zip#WzComparerR2.Cli-win-x64-self-contained.zip \
  --repo COLLABO-GAMES-DEV/WzComparerR2-cli \
  --target "$(git rev-parse HEAD)" \
  --title "WzComparerR2 CLI v0.1.0 Preview" \
  --notes-file docs/release-notes/wcr2-cli-v0.1.0-preview.md \
  --draft \
  --prerelease
```

Open the draft, confirm the asset is attached, then publish it:

```bash
gh release view wcr2-cli-v0.1.0-preview --repo COLLABO-GAMES-DEV/WzComparerR2-cli --web
```

If you already created the release and only need to upload or replace the zip:

```bash
gh release upload wcr2-cli-v0.1.0-preview \
  artifacts/wcr2-win-x64-self-contained.zip#WzComparerR2.Cli-win-x64-self-contained.zip \
  --repo COLLABO-GAMES-DEV/WzComparerR2-cli \
  --clobber
```

## Upload From GitHub Web UI

1. Go to `https://github.com/COLLABO-GAMES-DEV/WzComparerR2-cli/releases/new`.
2. Set tag to `wcr2-cli-v0.1.0-preview`.
3. Set title to `WzComparerR2 CLI v0.1.0 Preview`.
4. Paste `docs/release-notes/wcr2-cli-v0.1.0-preview.md`.
5. Attach `artifacts/wcr2-win-x64-self-contained.zip`.
6. Rename the uploaded asset in the UI, if desired, to `WzComparerR2.Cli-win-x64-self-contained.zip`.
7. Check `Set as a pre-release`.
8. Save as draft first, verify the asset, then publish.

## Post-Release Smoke Test

On Windows:

```powershell
$Root = "C:\Wcr2CliReleaseTest"
New-Item -ItemType Directory -Force $Root | Out-Null
Expand-Archive "$Root\WzComparerR2.Cli-win-x64-self-contained.zip" -DestinationPath $Root -Force

$Wcr2 = "$Root\wcr2-win-x64-self-contained\wcr2.exe"
& $Wcr2 --help
& $Wcr2 version
```

With a real MapleStory client folder:

```powershell
.\samples\cli\windows-maple-smoke.ps1 `
  -Wcr2 "$Root\wcr2-win-x64-self-contained\wcr2.exe" `
  -Maple "C:\Nexon\Maple" `
  -Out "$Root\out"
```
