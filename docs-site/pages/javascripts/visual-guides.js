(() => {
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
      const frames = [...guide.querySelectorAll("[data-visual-frame]")];
      const controls = [...guide.querySelectorAll("[data-visual-select]")];
      const select = index => {
        frames.forEach((frame, position) => {
          frame.hidden = position !== index;
          if (frame.hidden) frame.querySelector("video")?.pause();
        });
        controls.forEach((control, position) => {
          if (position === index) control.setAttribute("aria-current", "true");
          else control.removeAttribute("aria-current");
          control.setAttribute("aria-controls", frames[position].id);
        });
      };
      controls.forEach((control, index) => control.addEventListener("click", event => {
        if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        select(index);
      }));
      if (frames.length) select(0);
    }
  };
  window.initializeVisualGuides();
})();
