# Documentation site scaffold

This folder contains the public documentation site source. The home, onboarding,
compatibility, and legal pages are drafts. Importing and troubleshooting pages
remain placeholders and are not in the site navigation. Keep private
development notes outside this source directory; the site build reads only
`pages/`.

## Preview locally

From this folder, install the pinned dependency and run:

```powershell
python -m pip install -r requirements.txt
zensical serve
```

To check a production build:

```powershell
zensical build --clean
```

The generated site is written to `site/` and is ignored by Git.

## GitHub Pages

The workflow at `.github/workflows/docs-site.yml` checks builds on pull
requests. It does not publish on pushes. Publishing is available only through
a manual workflow run with its `publish` input explicitly set to `true`; the
repository's Pages source must also be configured as **GitHub Actions**.

Review the onboarding content and replace remaining placeholders before the
first publication. Pushing this source does not publish the site; production
publication is a separate manual workflow run.
