(() => {
  const video = document.querySelector("[data-landing-background-video]");
  if (!video) return;

  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
  const shouldPlay = () =>
    !reducedMotion.matches && !document.hidden && !navigator.connection?.saveData;

  const updatePlayback = () => {
    if (shouldPlay()) {
      video.play().catch(() => {
        // Keep the static theme background if autoplay is unavailable.
      });
    } else {
      video.pause();
    }
  };

  updatePlayback();
  document.addEventListener("visibilitychange", updatePlayback);
  reducedMotion.addEventListener("change", updatePlayback);
  navigator.connection?.addEventListener?.("change", updatePlayback);
})();
