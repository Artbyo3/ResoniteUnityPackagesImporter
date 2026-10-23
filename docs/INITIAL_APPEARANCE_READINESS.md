# Initial appearance readiness audit

Audit date: 2026-10-23. This records evidence and remaining gates, not full MA,
VRCFury, Unity, or VRChat parity.

## Source baseline

Fetched GitHub master: 8507c68. That commit is an ancestor of the local
implementation history. No master push or merge is part of this audit.

The semantic reviews used Modular Avatar commit
f21ad465bb623e841b532a66aa555dbcddf2db47 and VRCFury commit
6bd33f4cb453df816c943a335e47f64e6a37e188. Tests against different package
releases must identify those releases rather than imply exact-baseline coverage.

## Correctness findings addressed

- Avatar output uses one intended action for the label, enablement, authorization,
  and execution. A structurally ready avatar with insufficient native readiness
  cannot silently fall back to keeping an ordinary object and consume the item.
  Regression coverage includes every non-ready native state, ready avatars,
  ordinary props, and outfit exclusion.
- Blendshape synchronization uses the shared appearance transaction boundary,
  including verified avatar ownership, native destination preflight, driven-field
  checks, rollback, and the cross-pass assignment ledger. There is no staging-root
  fallback. Authored weights on engine-stripped source shapes remain readable;
  unavailable native destinations are diagnosed rather than silently omitted.
- MA Merge Animator references prevent the reactive evaluator from assuming
  external animation context is absent. A null controller does not block a
  snapshot merely because the component exists.

## Validation evidence

Required commands from AGENTS.md were run serially on the reviewed tree:

- Regression suite: 813/813 passed.
- Release mod build with --no-incremental: 0 errors, 76 warnings.
- Embedded UI audit: station, pre-import, and error-report bundles passed their
  binding, asset-hash, and privacy checks.
- git diff --check passed.

Independent source review found no remaining action-policy finding in its scoped
follow-up. This does not replace a native integration test.

An isolated Unity 2022.3.22f1 oracle was attempted with cached releases MA 1.18.7,
NDMF 1.14.8, VRCFury 1.1286.0, and VRChat SDK 3.10.5. Its final executed attempt
exited 1 during SDK compilation because SDK test sources could not resolve NUnit
references. No fixture preprocessing callbacks executed and no processed-state
observations were produced. This attempt is not conversion-correctness evidence.
Reproduction scripts and logs remain in gitignored scratch/unity-avatar-oracle;
original avatar projects and their packages were not modified.

Cached-package composition checks must not be presented as full imports: they
exercise composition, indexing, and planning, not engine deserialization,
renderer state, or native avatar creation. Missing override targets in stale
source libraries remain fail-closed errors; guessing identities or dropping an
override is not a repair. Existing signed-reference deserialization limitations
also remain distinct from new regressions.

## Remaining publication gate

The post-audit DLL has not yet been installed or exercised in Resonite. No live
ResoniteLink session was discoverable during the audit. Native asynchronous
material loading, rollback under engine rejection, renderer/rig results, and
successful Create Avatar still need end-to-end evidence after installation and
a full restart. A passing compiler suite or naked-eye appearance alone does not
establish those properties.

The implementation is ready for live test, not certified for merge. Runtime
menus, arbitrary Animator execution, saved parameters, shader parity, scene-wide
conversion, and destructive geometry/blendshape deletion are not claimed.
Destructive operations remain deferred as requested. No numerical support
percentage is established by this audit.
