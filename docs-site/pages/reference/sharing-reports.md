---
title: Sharing reports
---

# Share a report, not your assets

## First, check whether it is a documented limit

A visual approximation is not the same as a failed import. Unity shaders do not run directly in Resonite; their supported properties are converted to native materials. Missing source assets, a structural blocker, and an unsupported effect need different reports. Include the item's actual warnings and the result you expected.



## Use the report panel when it is available

When an import or station action fails, or an item is structurally blocked, the importer can show a separate report panel in private userspace.

1. Use **Copy report** to copy the filtered diagnostic report.
2. Use **Open GitHub issue** to open a draft. It does not submit an issue for you.
3. Review the draft. Add what you expected, what actually happened, and the steps that led to the result.
4. For long reports, paste the full copied report where the draft asks for it.

The report already includes version and diagnostic context when available. Add a screenshot only if it is useful and safe to share. Do not attach private packages or textures.

## If the panel is unavailable

Reports are also saved locally in `reports/UnityPackageImporter`, beside the `DecompressedUnityPrefabs` cache folder. The newest 20 are retained. The existing failure card and the local file remain available when a report panel cannot be opened.

Cancelling an import, closing the station, and ordinary compatibility warnings alone do not create failure reports. For a visual limitation, describe the result and use the item's warnings or import receipt.


**Nothing is uploaded automatically.** Reports omit package, asset, and object names by default and filter personal data. Technical details can still be identifying: always review before sharing. Ordinary game logs are not the same as these filtered reports and may include private information.


## Names and material-warning receipts

Keep `IncludeNamesInReports` off when sharing an anonymous report. Enabling it deliberately includes explicitly collected content names, even though personal-data filtering remains active.

Material-warning receipts filter recognized local paths, but do not assume arbitrary text or ordinary game logs are safe to publish. Review the exact material you plan to share.
