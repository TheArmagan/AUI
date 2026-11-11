"""Build the VPM listing (index.json) and its landing page from the repo's GitHub releases.

Every published, non-draft release that carries a package.json asset (written by
build_release.py) becomes one version in the listing. Nothing is read from the working tree, so
the listing always matches what can actually be downloaded.

Usage: python .github/scripts/build_listing.py <owner/repo> <listing url> <out dir>
Needs GH_TOKEN (or GITHUB_TOKEN) in the environment.
"""

import html
import json
import os
import sys
import urllib.parse
import urllib.request

PACKAGE_ID = "rest.armagan.aui"


def api(url, token, accept="application/vnd.github+json"):
    req = urllib.request.Request(url, headers={"Accept": accept, "Authorization": "Bearer " + token, "X-GitHub-Api-Version": "2022-11-28"})
    with urllib.request.urlopen(req) as r:
        return r.read()


def main():
    repo, listing_url, out_dir = sys.argv[1], sys.argv[2], sys.argv[3]
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN") or ""
    versions = {}
    page = 1
    while True:
        releases = json.loads(api("https://api.github.com/repos/" + repo + "/releases?per_page=100&page=" + str(page), token))
        if not releases:
            break
        for rel in releases:
            if rel.get("draft"):
                continue
            asset = next((a for a in rel.get("assets", []) if a["name"] == "package.json"), None)
            if asset is None:
                continue
            entry = json.loads(api(asset["url"], token, "application/octet-stream"))
            if entry.get("name") != PACKAGE_ID or "url" not in entry:
                continue
            versions[entry["version"]] = entry
        page += 1

    listing = {
        "name": "AUI",
        "id": PACKAGE_ID,
        "url": listing_url,
        "author": "Armagan",
        "packages": {PACKAGE_ID: {"versions": versions}},
    }
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "index.json"), "w", encoding="utf-8", newline="\n") as out:
        json.dump(listing, out, indent=2)
        out.write("\n")

    # (0, n) for a number and (1, s) for text, so "1.2.0" and "1.2.0-beta" compare without a TypeError.
    newest = sorted(versions, key=lambda v: [(0, int(x)) if x.isdigit() else (1, x) for x in v.replace("-", ".").split(".")])
    latest = newest[-1] if newest else "none yet"
    vcc = "vcc://vpm/addRepo?url=" + urllib.parse.quote(listing_url, safe="")
    page_html = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>AUI VPM listing</title>
<style>
  :root { --bg: #ffffff; --fg: #1b2433; --muted: #5b6778; --line: #d5dbe3; --accent: #2563a8; }
  @media (prefers-color-scheme: dark) { :root { --bg: #1b2433; --fg: #eef4fb; --muted: #b4c4d8; --line: #46597a; --accent: #8cc4ff; } }
  body { margin: 0; background: var(--bg); color: var(--fg); font: 16px/1.5 system-ui, sans-serif; }
  main { max-width: 640px; margin: 0 auto; padding: 48px 16px; }
  h1 { margin: 0 0 8px; font-size: 28px; }
  p { color: var(--muted); }
  a { color: var(--accent); }
  code { display: block; padding: 10px 12px; border: 1px solid var(--line); border-radius: 6px; overflow-x: auto; color: var(--fg); }
  .button { display: inline-block; margin: 8px 0 24px; padding: 10px 16px; border-radius: 6px; background: var(--accent); color: var(--bg); text-decoration: none; font-weight: 600; }
</style>
</head>
<body>
<main>
  <h1>AUI</h1>
  <p>VPM listing for <strong>rest.armagan.aui</strong>, the UdonSharp UI engine. Latest version: <strong>{{LATEST}}</strong>.</p>
  <a class="button" href="{{VCC}}">Add to VCC</a>
  <p>Or add this URL in VCC / ALCOM under Settings, Packages, Add Repository:</p>
  <code>{{LISTING}}</code>
  <p>Release files (.zip, .unitypackage, the VS Code extension): <a href="https://github.com/{{REPO}}/releases">github.com/{{REPO}}/releases</a>.
  Documentation: <a href="https://auidocs.thearmagan.workers.dev">auidocs.thearmagan.workers.dev</a>.</p>
</main>
</body>
</html>
"""
    page_html = (page_html.replace("{{LATEST}}", html.escape(latest)).replace("{{VCC}}", html.escape(vcc))
                 .replace("{{LISTING}}", html.escape(listing_url)).replace("{{REPO}}", html.escape(repo)))
    with open(os.path.join(out_dir, "index.html"), "w", encoding="utf-8", newline="\n") as out:
        out.write(page_html)
    print("listing: " + str(len(versions)) + " version(s), latest " + latest)


if __name__ == "__main__":
    main()
