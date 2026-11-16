# Releasing DCP Tool v2

The source repository is private. An installed app cannot read a private repository's releases without
a token built into it, so installers and update packages live in a second, **public** repository that
contains nothing else. Installed apps check it on start and update themselves.

## One-time setup

1. **Create the releases repository.** On GitHub: New repository, owner `imdbere`, name `DCPTool-releases`,
   visibility **Public**, tick "Add a README" (an empty repository cannot hold releases). Put one line in the
   README, e.g. "Downloads for DCP Tool". No source code goes here.
   If you choose another name or owner, change it in two places: `REPO_URL` in `.github/workflows/v2.yml`
   and `RepoUrl` in `src/DcpTool.App/Services/UpdateService.cs`.
2. **Create a token that can write to it.** GitHub → Settings → Developer settings → Personal access tokens →
   Fine-grained tokens → Generate new token. Resource owner: the owner of the releases repository.
   Repository access: "Only select repositories" → `DCPTool-releases`. Permissions: **Contents: Read and write**.
   Pick an expiry (one year is the maximum) and note the date: releases fail once it has expired.
3. **Give the token to the source repository.** In the private `DCPTool` repository: Settings → Secrets and
   variables → Actions → New repository secret, name `RELEASES_TOKEN`, value = the token.

## Making a release

1. Make sure `master` (or the branch you release from) is green in GitHub Actions.
2. Tag and push:

   ```bash
   git tag v2.0.0
   ```

   ```bash
   git push origin v2.0.0
   ```

3. The "DCP Tool v2" workflow runs the tests, builds the Windows installer with Velopack and publishes a
   release in `DCPTool-releases`. Check that the release there has `DcpTool-win-Setup.exe`, a
   `-full.nupkg` and `releases.win.json`.
4. New users download `DcpTool-win-Setup.exe` from the releases page. Existing installations find the
   update on their next start, download it in the background and show a "restart" button.

A tag with a suffix (`v2.1.0-beta.1`) is published as a pre-release. Only users who ticked
"Include pre-releases" in the settings receive it. Use this to try a fix on one machine first.

## Pushing an urgent fix

Commit the fix, tag the next patch version (`v2.0.1`), push the tag. From push to "available to users" is
the workflow's run time, a few minutes. Version numbers must always increase: an installed app never
moves to a lower version.

## Things to know

- The builds are not code-signed yet, so Windows shows "Windows protected your PC" on first install
  (More info → Run anyway). Updates of an installed app are not affected.
- GitHub allows 60 anonymous API requests per hour per IP address. Several users behind one office
  network share that. A failed check is silent and is retried on the next start.
- To take a bad release back, delete it in `DCPTool-releases` and publish a fixed one with a higher
  version. Users who already updated stay on the bad version until the fixed one is out.
- The first release has never been run. Expect to adjust the workflow once.
