# Maintain the public project-status feed

## Publish an update

1. Open the **Public project update** issue form, or edit an existing issue.
2. Use a concise, reader-facing issue title. Include these exact sections:

   ```text
   ### Why it matters
   One or two short sentences about the impact on users.

   ### Next step
   The actual next action, without an invented date or progress claim.
   ```

3. Review the title and both fields for private names or identifying information.
   Other report sections and comments are not copied, but the issue itself is
   already public. Do not move private data into these editorial fields.
4. Add **exactly one** status label and the **project-status** approval label.

| GitHub label | Public status | Meaning |
| --- | --- | --- |
| `status:in-progress` | In progress | Someone is actively doing the work. |
| `status:known-issue` | Known issue | A maintainer has triaged the problem. |
| `status:testing` | Needs testing | A change needs verification; it is not a support guarantee. |
| `status:planned` | Planned | Intended work, without a delivery promise. |

The approval label is separate from work status. The issue form assigns only
`documentation`, so public contributors cannot automatically publish their own
news by submitting the form. The five required publication/status labels have
been created in the repository; no existing issue was relabelled or rewritten.
There are currently no approved updates. Existing open reports are not silently
classified as confirmed bugs or active work.

## Update or remove

- Edit the title or public fields to update the message.
- Replace the status label to move it between public categories.
- Close the issue to remove it from active updates; reopen it to restore it.
- Remove `project-status` to unpublish while keeping the issue open.
- Do not add two status labels. An invalid approved issue stops the sync, so
  incomplete or contradictory data is never silently published.

The docs workflow refreshes approved issue changes. The six-hour scheduled run
is a backup, not a real-time guarantee; GitHub may queue workflow runs. A manual
**Documentation site** run on `master` with `publish=true` refreshes the site too.
The new workflow and issue form must first be merged and deployed. Creating the
labels alone does not activate a public connection. No separate personal access
token, webhook server or recurring local process is required.

## What gets published

Only open, explicitly approved issues with one valid status and both editorial
fields. The adapter skips pull requests and closed/unapproved issues, follows
pagination and fails rather than truncating a large collection. Public copy is
plain text, limited to 140 characters for titles and 280 each for why/next.
Each item links to its original GitHub issue. Comments, raw logs, attachments,
author details and complete bodies are not copied. Body text is never executed.

The snapshot records the real sync time separately from each issue's update
time. After 24 hours the UI warns that activity may be out of date. Empty curated
results explicitly do not mean there are no open reports. The public GitHub link
shows the same approval-label scope.

## Failure handling and local checks

```powershell
python -m unittest discover -s docs-site/tests
python docs-site/build.py
python docs-site/sync_project_status.py
```

Run the sync after the build because the build regenerates the output directory.
For local use, public API access needs no token at ordinary rate limits. If using
an authenticated shell, supply `GH_TOKEN` through the environment, never through a
committed file or browser code. CI supplies its own read-only job token.

A network/rate-limit error, invalid approved issue, unexpected URL or pagination
failure exits nonzero without overwriting the previous snapshot. The workflow
stops before uploading/deploying, so stale data retains its original date instead
of being relabelled fresh. Correct the issue's public fields/status, wait for rate
limits to recover or rerun the workflow. Logs report an error type, not raw issue
bodies, credentials or user information.

## Boundaries

Activity and component compatibility are different data sources. Closing a bug
is not proof that its fix is released, live-tested, or fully compatible. Neither
the adapter nor its workflow updates `capabilities.json`, release metadata, mod
code, binaries or templates. The root video landing is unchanged.
