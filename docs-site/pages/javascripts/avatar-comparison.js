(() => {
  const initializeComparisons = () => {
    document.querySelectorAll("[data-avatar-comparison]").forEach((comparison) => {
      const control = comparison.querySelector(".avatar-comparison__control");
      const frame = comparison.querySelector(".avatar-comparison__frame");

      if (!control || !frame) return;

      const updatePosition = () => {
        const position = Number(control.value);
        frame.style.setProperty("--comparison-position", `${position}%`);
        control.setAttribute("aria-valuetext", `${position} percent of VRChat original revealed`);
      };

      control.addEventListener("input", updatePosition);
      updatePosition();
    });
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeComparisons, { once: true });
  } else {
    initializeComparisons();
  }
})();
