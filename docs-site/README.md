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
requests. It does not publish on pushes. Publishing is available only through
a manual workflow run with its `publish` input explicitly set to `true`; the
repository's Pages source must also be configured as **GitHub Actions**.

Review the onboarding content and replace remaining placeholders before the
first publication. Pushing this source does not publish the site; production
publication is a separate manual workflow run.
