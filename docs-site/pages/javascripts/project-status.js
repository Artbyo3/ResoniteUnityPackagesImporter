(async () => {
  const explorer = document.querySelector("[data-project-activity]");
  const pulse = document.querySelector("[data-project-pulse]");
  if (!explorer && !pulse) return;

  const statusNames = {
    working: "In progress",
    problem: "Known issue",
    testing: "Needs testing",
    planned: "Planned",
  };
  const source = new URL("../data/project-status.json", document.currentScript.src);
  const unavailableMessage = "Project activity is not connected yet. This page does not currently report live work or known issues.";
  const showUnavailable = message => {
    if (pulse) {
      pulse.querySelector("[data-pulse-lanes]").hidden = true;
      const notice = pulse.querySelector("[data-pulse-notice]");
      notice.hidden = false;
      notice.textContent = message;
    }
    if (explorer) {
      explorer.querySelector("[data-activity-notice]").textContent = message;
      explorer.querySelector("[data-activity-tools]").hidden = true;
      explorer.querySelector("[data-activity-list]").replaceChildren();
      explorer.querySelector("[data-activity-pagination]").hidden = true;
    }
  };
  let issues;
  try {
    const response = await fetch(source);
    if (!response.ok) throw new Error(`Status data unavailable: ${response.status}`);
    const data = await response.json();
    if (!["not-connected", "available"].includes(data.state) || !Array.isArray(data.issues)) throw new Error("Invalid status state");
    if (data.state === "not-connected") {
      if (data.issues.length || data.updatedAt !== null) throw new Error("Disconnected status cannot contain activity");
      showUnavailable(unavailableMessage);
      return;
    }
    if (!Number.isFinite(Date.parse(data.updatedAt)) || data.issues.some(item =>
      !item || !Object.hasOwn(statusNames, item.status) ||
      ![item.id, item.title, item.why, item.next].every(value => typeof value === "string" && value.trim()) ||
      !Number.isFinite(item.priority) || !Number.isFinite(Date.parse(item.updatedAt))
    )) throw new Error("Invalid status data");
    if (new Set(data.issues.map(item => item.id)).size !== data.issues.length) throw new Error("Duplicate status record");
    issues = data.issues.toSorted((first, second) => first.priority - second.priority);
    if (explorer) {
      explorer.querySelector("[data-activity-tools]").hidden = false;
      explorer.querySelector("[data-activity-notice]").textContent = "Last updated " + new Date(data.updatedAt).toLocaleString() + ".";
    }
  } catch (error) {
    showUnavailable("Project updates could not be loaded. Current activity is unknown; please try again later.");
    console.error("Project status:", error);
    return;
  }
  const element = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text) node.textContent = text;
    return node;
  };
  if (pulse) {
    const groups = [];
    const lanes = ["working", "problem", "testing"].map(status => {
      const items = issues.filter(item => item.status === status)
        .toSorted((first, second) => Date.parse(second.updatedAt) - Date.parse(first.updatedAt));
      const lane = element("div", "project-pulse__lane");
      const heading = element("div", "project-pulse__lane-heading");
      const badge = element("span", "project-badge", status === "problem" ? "Known issues" : statusNames[status]);
      badge.dataset.status = status;
      heading.append(badge, element("span", "project-pulse__count", String(items.length)));
      lane.append(heading);
      if (items.length) {
        const viewport = element("div", "project-pulse__window");
        viewport.setAttribute("aria-live", "off");
        const slides = items.slice(0, 3).map((item, index) => {
          const slide = element("a", "project-pulse__slide");
          slide.href = `../project-status/?status=${status}`;
          slide.dataset.issueId = item.id;
          slide.classList.toggle("is-active", index === 0);
          slide.inert = index !== 0;
          slide.setAttribute("aria-hidden", String(index !== 0));
          slide.append(element("h3", "", item.title), element("p", "", item.why));
          viewport.append(slide);
          return slide;
        });
        const more = element("a", "project-pulse__more", `View all ${items.length}`);
        more.href = `../project-status/?status=${status}`;
        lane.append(viewport, more);
        groups.push(slides);
      } else {
        lane.append(element("p", "", "No open items."));
      }
      return lane;
    });
    pulse.querySelector("[data-pulse-lanes]").replaceChildren(...lanes);
    pulse.querySelector("[data-pulse-lanes]").hidden = false;
    pulse.querySelector("[data-pulse-notice]").hidden = true;
    const total = Math.max(0, ...groups.map(slides => slides.length));
    if (total > 1) {
      const controls = element("div", "project-pulse__playback");
      const caption = element("span", "", "Latest updates");
      const buttons = element("div", "project-pulse__buttons");
      const previous = element("button", "", "Previous");
      const playback = element("button");
      const next = element("button", "", "Next");
      for (const button of [previous, playback, next]) button.type = "button";
      previous.setAttribute("aria-label", "Show previous updates");
      next.setAttribute("aria-label", "Show next updates");
      buttons.append(previous, playback, next);
      controls.append(caption, buttons);
      pulse.append(controls);
      const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
      let paused = reducedMotion.matches;
      let hovering = false;
      let visible = false;
      let position = 0;
      let timer;
      const advance = direction => {
        position = (position + direction + total) % total;
        for (const slides of groups) slides.forEach((slide, index) => {
          const active = index === position % slides.length;
          const wasActive = slide.classList.contains("is-active");
          for (const animation of slide.getAnimations()) animation.cancel();
          slide.classList.toggle("is-active", active);
          slide.inert = !active;
          slide.setAttribute("aria-hidden", String(!active));
          if (!reducedMotion.matches && active !== wasActive) {
            slide.animate([
              { transform: `translateY(${active ? direction * 100 : 0}%)`, opacity: active ? 0 : 1, visibility: "visible" },
              { transform: `translateY(${active ? 0 : -direction * 100}%)`, opacity: active ? 1 : 0, visibility: "visible" },
            ], { duration: 450, easing: "ease" });
          }
        });
      };
      const schedule = () => {
        window.clearTimeout(timer);
        playback.textContent = paused ? "Play" : "Pause";
        playback.setAttribute("aria-label", paused ? "Play automatic updates" : "Pause automatic updates");
        if (!paused && !hovering && visible && !document.hidden && !pulse.contains(document.activeElement)) {
          timer = window.setTimeout(() => { advance(1); schedule(); }, 8000);
        }
      };
      playback.addEventListener("click", () => { paused = !paused; schedule(); });
      previous.addEventListener("click", () => { advance(-1); schedule(); });
      next.addEventListener("click", () => { advance(1); schedule(); });
      pulse.addEventListener("mouseenter", () => { hovering = true; schedule(); });
      pulse.addEventListener("mouseleave", () => { hovering = false; schedule(); });
      pulse.addEventListener("focusin", schedule);
      pulse.addEventListener("focusout", () => queueMicrotask(schedule));
      document.addEventListener("visibilitychange", schedule);
      reducedMotion.addEventListener("change", () => { paused = reducedMotion.matches; schedule(); });
      const observer = new IntersectionObserver(entries => {
        visible = entries[0].isIntersecting;
        schedule();
      });
      observer.observe(pulse);
      window.addEventListener("pagehide", () => window.clearTimeout(timer));
      window.addEventListener("pageshow", schedule);
      schedule();
    }
  }
  if (!explorer) return;
  const parameters = new URLSearchParams(window.location.search);
  const dataset = issues;
  const search = explorer.querySelector("[data-activity-search]");
  const filters = [...explorer.querySelectorAll("[data-activity-filter]")];
  const list = explorer.querySelector("[data-activity-list]");
  const results = explorer.querySelector("[data-activity-results]");
  const pagination = explorer.querySelector("[data-activity-pagination]");
  const previous = explorer.querySelector("[data-activity-previous]");
  const next = explorer.querySelector("[data-activity-next]");
  const requestedStatus = parameters.get("status");
  let activeStatus = Object.hasOwn(statusNames, requestedStatus) ? requestedStatus : "all";
  let page = 1;
  const pageSize = 6;

  const renderItem = (item) => {
    const details = element("details", "project-activity__item");
    const summary = element("summary");
    const title = element("span", "project-activity__item-title", item.title);
    const badge = element("span", "project-badge", statusNames[item.status]);
    badge.dataset.status = item.status;
    summary.append(title, badge);
    const context = element("div", "project-activity__context");
    for (const [label, text] of [["Why", item.why], ["Next", item.next]]) {
      const paragraph = element("p");
      paragraph.append(element("strong", "", `${label}: `), document.createTextNode(text));
      context.append(paragraph);
    }
    details.append(summary, context);
    return details;
  };

  const render = () => {
    const query = search.value.trim().toLocaleLowerCase();
    const matches = dataset.filter(item =>
      (activeStatus === "all" || item.status === activeStatus) &&
      [item.title, item.why, item.next, statusNames[item.status]].join(" ").toLocaleLowerCase().includes(query)
    );
    const pageCount = Math.max(1, Math.ceil(matches.length / pageSize));
    page = Math.min(page, pageCount);
    const start = (page - 1) * pageSize;
    const visible = matches.slice(start, start + pageSize);
    list.replaceChildren(...visible.map(renderItem));
    if (!matches.length) list.append(element("p", "project-activity__empty", "No results."));
    results.textContent = matches.length ? `${start + 1}-${start + visible.length} of ${matches.length}` : "0 items";
    for (const filter of filters) filter.setAttribute("aria-pressed", String(filter.dataset.activityFilter === activeStatus));
    previous.disabled = page === 1;
    next.disabled = page === pageCount;
    pagination.hidden = pageCount <= 1;
  };

  for (const filter of filters) filter.addEventListener("click", () => {
    activeStatus = filter.dataset.activityFilter;
    page = 1;
    render();
  });
  search.addEventListener("input", () => {
    page = 1;
    render();
  });
  const changePage = (offset) => {
    page += offset;
    render();
    const firstItem = list.querySelector("summary");
    if (firstItem) firstItem.focus();
  };
  previous.addEventListener("click", () => changePage(-1));
  next.addEventListener("click", () => changePage(1));
  render();
})();
