# Reports and public project updates

## For users: report the problem, not our roadmap

Choose **Report a problem** on GitHub. Only two answers are required:
what you expected versus what happened, and the actions that led to it.
The reviewed importer report and versions are optional; do not repeat versions
already included in the report. Screenshots help for visual problems.
Never attach private/paid assets, textures or unfiltered game logs.

If the importer already opened a draft, keep it and add the expected result
and steps. Do not start over in the form. Blank issue drafts remain available
for the existing in-game report flow; no mod-code change is required.

Choose **Suggest an improvement** for a missing capability. Only the task you
want to accomplish and what blocks it are required. A proposed solution is
optional. Users never need to write roadmap copy or choose work-status labels.

## For maintainers: triage first, publish intentionally

- `bug` identifies a reported failure, not a verified root cause.
- `enhancement` identifies a requested improvement, not a delivery commitment.
- `area:*` routes reports to the relevant part of the product.
- `triage:needs-reproduction` or `triage:needs-information` keeps uncertainty explicit.
- These labels do not publish anything to the docs.

An open issue becomes a public update when a maintainer reviews its title for
safe, reader-facing wording and adds **project-status** plus exactly one status:

| GitHub label | Public status | Use only when |
| --- | --- | --- |
| `status:in-progress` | In progress | Someone is actively doing the work. |
| `status:known-issue` | Known issue | A maintainer has triaged the problem. |
| `status:testing` | Needs testing | A change needs verification. |
| `status:planned` | Planned | The work is intended, without a delivery promise. |

**A clear title and status are enough.** Existing bug reports are eligible;
there is no need to create a duplicate roadmap issue or rewrite their body.
The public-update form is an optional maintainer convenience, not a requirement.
It does not grant publication approval.

Add these sections only when they help readers:

```text
### Why it matters
Short user impact, without private names or raw diagnostics.

### Next step
The actual next action, without an invented date or progress claim.
```

Missing or blank optional fields are not shown. No placeholder, guessed impact
or invented next step is generated. Other report text, comments, attachments and
author details are never copied. The original linked GitHub issue remains public;
review it separately before sharing private information anywhere on GitHub.

## Updates and refresh timing

Edit the title or optional copy to update it. Replace the status label to change
its category. Close the issue or remove `project-status` to remove it from active
updates. Closing does not claim a fix is released or a component is supported.

This is a build-time snapshot, not a browser-to-GitHub live connection. The docs
workflow rebuilds and deploys on approved issue changes; a six-hour schedule is
a backup. Changes appear after deployment completes, not immediately. A manual
**Documentation site** run on `master` with `publish=true` also refreshes it.
Visitors need no GitHub login. CI uses its automatic read-only job token.

## Incomplete reports must not block other updates

- Missing or conflicting status labels: skip only that issue and log its number.
- Invalid public title: skip only that issue and log its number.
- Duplicate or unsafe optional sections: omit that section and log its number.
- Headings inside fenced diagnostic reports are not treated as public copy.
- API, pagination, identity, URL or timestamp failures: stop publication and keep
  the previously deployed snapshot rather than claim a complete fresh feed.

Warnings contain issue numbers and fixed reasons, never raw reports or tokens.
Review the workflow log to correct skipped entries. Titles are limited to 140
characters and optional fields to 280 each. The real sync time is shown separately
from issue update times; the UI warns after 24 hours without a fresh snapshot.
An empty curated feed is not a claim that there are no open reports.

## Local checks

```powershell
python -m unittest discover -s docs-site/tests
python docs-site/build.py
python docs-site/sync_project_status.py
```

Run sync after build because build regenerates the output directory. Local
public API reads can run without a token at ordinary rate limits. If using
`GH_TOKEN`, supply it through the environment, never a committed file or browser
code. The adapter never edits issues, compatibility evidence, mod code or the
root landing. New forms and behavior require publication before they are public.
