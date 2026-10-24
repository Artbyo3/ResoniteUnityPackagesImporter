(() => {
  const compactWalkthrough = window.matchMedia("(max-width: 900px)");
  const fragmentSelections = new WeakMap();
  window.addEventListener("hashchange", () => {
    document.querySelectorAll("[data-visual-guide][data-enhanced]").forEach(guide => {
      fragmentSelections.get(guide)?.();
    });
  });
  const placeWalkthroughMedia = guide => {
    const current = guide.querySelector(".import-step[aria-current]");
    const media = guide.querySelector(".visual-media");
    if (!current || !media) return;
    if (compactWalkthrough.matches) current.closest("li").append(media);
    else guide.querySelector(".import-walkthrough__steps").after(media);
  };
  compactWalkthrough.addEventListener("change", () => {
    document.querySelectorAll("[data-import-walkthrough][data-enhanced]").forEach(placeWalkthroughMedia);
  });
  window.initializeVisualGuides = (root = document) => {
    for (const comparison of root.querySelectorAll("[data-field-comparison]")) {
      if (comparison.dataset.fieldReady) continue;
      const input = comparison.querySelector('input[type="range"]');
      if (!input) continue;
      const update = () => {
        comparison.style.setProperty("--field-position", `${input.value}%`);
        input.setAttribute("aria-valuetext", `${input.value} percent source appearance`);
      };
      comparison.dataset.fieldReady = "true";
      input.hidden = false;
      input.addEventListener("input", update);
      update();
    }
    for (const guide of root.querySelectorAll("[data-visual-guide]")) {
      if (guide.dataset.enhanced || guide.querySelector("[data-visual-paired]")) continue;
      guide.dataset.enhanced = "true";
      const demo = guide.querySelector(".import-demo");
      demo?.addEventListener("toggle", () => {
        if (!demo.open) demo.querySelector("video")?.pause();
      });
      const frames = [...guide.querySelectorAll("[data-visual-frame]")];
      const controls = [...guide.querySelectorAll("[data-visual-select]")];
      const select = (index, ensureVisible = false) => {
        frames.forEach((frame, position) => {
          frame.hidden = position !== index;
          if (frame.hidden) frame.querySelector("video")?.pause();
        });
        controls.forEach((control, position) => {
          if (position === index) control.setAttribute("aria-current", guide.hasAttribute("data-import-walkthrough") ? "step" : "true");
          else control.removeAttribute("aria-current");
          control.setAttribute("aria-controls", frames[position].id);
        });
        if (guide.hasAttribute("data-import-walkthrough")) {
          placeWalkthroughMedia(guide);
          if (ensureVisible && compactWalkthrough.matches) controls[index].scrollIntoView({ block: "nearest", behavior: "instant" });
        }
      };
      controls.forEach((control, index) => control.addEventListener("click", event => {
        if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        select(index, true);
      }));
      if (guide.hasAttribute("data-import-walkthrough")) {
        controls.forEach((control, index) => control.addEventListener("keydown", event => {
          const next = { ArrowDown: (index + 1) % controls.length, ArrowUp: (index + controls.length - 1) % controls.length, Home: 0, End: controls.length - 1 }[event.key];
          if (next === undefined || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
          event.preventDefault();
          controls[next].focus();
          select(next, true);
        }));
      }
      if (frames.length) {
        const selectFragment = () => {
          const linkedFrame = frames.findIndex(frame => window.location.hash === `#${frame.id}`);
          if (linkedFrame >= 0) select(linkedFrame);
        };
        fragmentSelections.set(guide, selectFragment);
        select(0);
        selectFragment();
      }
    }
  };
  window.initializeVisualGuides();
})();
