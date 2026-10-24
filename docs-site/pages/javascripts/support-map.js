(() => {
  window.initializeSupportMaps = (root = document) => {
    for (const map of root.querySelectorAll("[data-support-map]")) {
      if (map.dataset.enhanced) continue;
      map.dataset.enhanced = "true";
      const rows = [...map.querySelectorAll("[data-support-row]")];
      const search = map.querySelector("[data-support-search]");
      const framework = map.querySelector("[data-support-framework]");
      const status = map.querySelector("[data-support-status]");
      const count = map.querySelector("[data-support-count]");
      const index = new Map(rows.map(row => [row, row.textContent.toLocaleLowerCase()]));
      const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
      const animations = new Map();
      for (const row of rows) {
        row.querySelector(":scope > summary").addEventListener("click", event => {
          event.preventDefault();
          const visible = rows.filter(item => !item.hidden);
          const before = new Map(visible.map(item => [item, item.getBoundingClientRect()]));
          for (const animation of animations.values()) animation.cancel();
          animations.clear();
          const opening = !row.open;
          if (opening) {
            for (const other of rows) {
              if (other !== row) other.open = false;
            }
          }
          row.open = opening;
          if (reducedMotion.matches || !row.animate) return;
          for (const item of visible) {
            const start = before.get(item);
            const end = item.getBoundingClientRect();
            if (!start.width || !end.width || !end.height) continue;
            if (start.x === end.x && start.y === end.y && start.width === end.width && start.height === end.height) continue;
            const animation = item.animate([
              { transform: `translate(${start.x - end.x}px, ${start.y - end.y}px) scale(${start.width / end.width}, ${start.height / end.height})` },
              { transform: "none" }
            ], { duration: 280, easing: "cubic-bezier(.2,.7,.2,1)" });
            animations.set(item, animation);
            animation.onfinish = () => animations.delete(item);
          }
        });
      }
      const update = () => {
        for (const animation of animations.values()) animation.cancel();
        animations.clear();
        const applicable = rows.filter(row => row.closest('[data-support-group="collection"]') && (!framework || framework.value === "all" || row.dataset.framework === framework.value));
        const supported = applicable.filter(row => row.dataset.status === "supported").length;
        const partial = applicable.filter(row => row.dataset.status === "limited").length;
        map.querySelector("[data-support-complete]").textContent = supported;
        map.querySelector("[data-support-total]").textContent = applicable.length;
        map.querySelector("[data-support-partial]").textContent = partial;
        const slots = map.querySelector("[data-support-slots]");
        slots.style.setProperty("--support-slot-count", Math.max(1, applicable.length));
        slots.setAttribute("aria-label", `${supported} supported, ${partial} partial, ${applicable.length - supported - partial} not supported`);
        for (const segment of slots.querySelectorAll("[data-support-segment]")) segment.hidden = framework && framework.value !== "all" && segment.dataset.framework !== framework.value;
        const terms = search.value.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
        const matches = rows.filter(row => {
          const selected = status.value;
          const hasStatus = selected === "all" || (selected === "current" ? row.dataset.status !== "legacy" : selected === "scheduled" ? row.dataset.work !== "unplanned" : row.dataset.status === selected);
          const includeLegacy = selected === "current" && terms.length ? true : hasStatus;
          return includeLegacy && (!framework || framework.value === "all" || row.dataset.framework === framework.value) && terms.every(term => index.get(row).includes(term));
        });
        const visible = new Set(matches);
        for (const row of rows) row.hidden = !visible.has(row);
        for (const group of map.querySelectorAll("[data-support-group]")) group.hidden = !group.querySelector("[data-support-row]:not([hidden])");
        count.hidden = !terms.length && status.value === "current";
        count.textContent = matches.length ? `${matches.length} components${terms.length ? " matching your search" : ""}` : status.value === "scheduled" ? "No scheduled work is recorded in this local snapshot." : "No matching component. Try its source name or another filter.";
      };
      for (const control of [search, framework, status].filter(Boolean)) {
        control.addEventListener(control === search ? "input" : "change", update);
      }
      map.querySelector("[data-support-tools]").hidden = false;
      update();
    }
  };
  window.initializeSupportMaps();
})();
