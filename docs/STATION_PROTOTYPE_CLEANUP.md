# Approved prototype cleanup

Completed 2026-09-30 after the user approved the UI and requested removal only
when current appearance and behavior would be preserved.

The audit covered the current station and new pre-import prompt, including
hidden pages and their serialized references. Incoming references to removal
candidates were also checked in the other 23 top-level world objects, including
the reference prompt and inspectors.

Four components were removed from the station:

- Two UI_UnlitMaterial providers on the disclosure card's Canvas and Border.
  Neither provider nor any of its fields had consumers. The visible Border
  already referenced a different material, which remains unchanged.
- The BooleanValueDriver and its one-way ValueCopy that formerly controlled
  the removed outfit placeholder. The driver's output was null, the copy had
  WriteBack disabled, and neither branch had any other consumers.

No components were removed from the new pre-import prompt. Hidden pages,
text-fitting graphs, normalization, scrolling, animation, and assets with
consumers were retained. In particular, the disconnected loading visibility
copy was retained because the existing demo still selects and releases it;
removing that copy alone would change how the demo resolves its controls.

After removal, the component set differed by exactly those four components.
No slots were removed. Stable layout, graphics, font, canvas, button palette,
and button action fields were compared across 1,437 UI-related components;
they matched. No references to removed components or fields remained in either
prototype. These are reference and field checks, not a new rendered visual pass.
Other components were not declared removable merely because they were hidden
or lacked a direct reference: automatic behavior and dynamic-variable lookup
can still require them.

Local inventory, before/after snapshots, removed component definitions, and
verification are under `scratch/station-prototype/cleanup-audit/`. Helpers are
`AuditUnusedPrototype.py`, `CheckCleanupReferences.py`, and
`CleanConfirmedUnused.py`. Their live IDs are specific to this session.
The inventory is diagnostic data rather than a portable saved Resonite item.

Save the updated station item in Resonite before closing the world. This cleanup
does not integrate, build, install, or publish the mod, and creates no automation.
