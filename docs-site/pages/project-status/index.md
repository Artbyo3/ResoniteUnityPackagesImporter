---
title: Project status
hide:
  - toc
---

# Project status

<section class="project-activity" data-project-activity aria-label="Project status">
<p class="project-status-notice" data-activity-notice role="status">Project activity is not connected yet. This page does not currently report live work or known issues.</p>
<div data-activity-tools hidden>
<label class="project-activity__search"><span class="project-activity__search-label">Search project status</span><input type="search" data-activity-search placeholder="Search" autocomplete="off"></label>
<div class="project-activity__filters" role="group" aria-label="Filter by status">
<button type="button" data-activity-filter="all" aria-pressed="true">All</button>
<button type="button" data-activity-filter="working" aria-pressed="false">In progress</button>
<button type="button" data-activity-filter="problem" aria-pressed="false">Known issues</button>
<button type="button" data-activity-filter="testing" aria-pressed="false">Needs testing</button>
<button type="button" data-activity-filter="planned" aria-pressed="false">Planned</button>
</div>
</div>
<div class="project-activity__list" data-activity-list></div>

<nav class="project-activity__pagination" aria-label="Status pages" data-activity-pagination hidden>
<button type="button" data-activity-previous>Previous</button><span data-activity-results role="status" aria-live="polite" aria-atomic="true"></span><button type="button" data-activity-next>Next</button>
</nav>
</section>
