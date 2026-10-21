# Documentation site source

This folder contains the public documentation site source for the Unity Package Importer.
All documentation pages, compatibility guides, importing workflows, and legal notices
are maintained here. Keep private development notes outside this source directory; the
site build reads only `pages/`.

## Preview locally

From this folder, install the pinned dependency and run:

```powershell
python -m pip install -r requirements.txt
python serve.py
```

This builds both the English and Japanese sites and serves them at `http://localhost:8000/`.

To run a production build only:

```powershell
python build.py
```

The generated site is written to `site/` (with English at `/` and Japanese at `/ja/`) and is ignored by Git.


## GitHub Pages

The workflow at `.github/workflows/docs-site.yml` checks builds on pull
requests. Documentation changes merged into `master` are built and published
to GitHub Pages automatically. A manual workflow run can also publish when
its `publish` input is `true`; the repository's Pages source must be configured
as **GitHub Actions**.

Both landing pages and installation guides link to GitHub's latest-release ZIP.
Keep the release asset name `UnityPackageImporter-testing-build.zip` consistent
so these links continue to follow future releases.
