(() => {
  const initializeContributors = () => {
    const section = document.querySelector("[data-github-contributors]");
    if (!section) return;

    const list = section.querySelector("[data-contributors-list]");
    const clone = section.querySelector("[data-contributors-clone]");
    const track = section.querySelector("[data-contributors-track]");
    const viewport = section.querySelector("[data-contributors-viewport]");
    const status = section.querySelector("[data-contributors-status]");
    const [owner, repository] = (section.dataset.repository || "").split("/");
    if (!list || !clone || !track || !viewport || !status || !owner || !repository) return;
    let contributorItems = [];
    let lastViewportWidth = -1;
    let rebuildScheduled = false;

    const setStatus = (message) => {
      status.textContent = message;
      status.hidden = !message;
    };

    const createContributor = (contributor) => {
      if (!contributor || !/^[A-Za-z0-9-]{1,39}$/.test(contributor.login || "")) return null;

      const item = document.createElement("li");
      item.className = "github-contributors__card";

      const profile = document.createElement("span");
      profile.className = "github-contributors__profile";

      if (typeof contributor.avatar_url === "string") {
        try {
          const avatarUrl = new URL(contributor.avatar_url);
          if (avatarUrl.protocol === "https:" && avatarUrl.hostname === "avatars.githubusercontent.com") {
            const avatar = document.createElement("img");
            avatar.className = "github-contributors__avatar";
            avatar.src = avatarUrl.href;
            avatar.alt = "";
            avatar.loading = "lazy";
            avatar.decoding = "async";
            profile.append(avatar);
          }
        } catch {
          // Keep the contributor name if GitHub returns an invalid avatar URL.
        }
      }

      const login = document.createElement("span");
      login.className = "github-contributors__login";
      login.textContent = contributor.login;
      profile.append(login);
      item.append(profile);
      return item;
    };

    const makeVisualCopy = (item) => {
      const copy = item.cloneNode(true);
      copy.setAttribute("aria-hidden", "true");
      return copy;
    };

    const buildLoop = () => {
      if (!contributorItems.length) return;

      list.replaceChildren(...contributorItems);
      lastViewportWidth = viewport.clientWidth;
      if (list.scrollWidth === 0) return;

      // Keep one full cycle at least as wide as the viewport. With only two
      // copies of a short list, the track can expose empty space near the loop
      // point even though the animation itself is continuous.
      while (list.scrollWidth < lastViewportWidth) {
        list.append(...contributorItems.map(makeVisualCopy));
      }

      clone.replaceChildren(...Array.from(list.children, makeVisualCopy));
      const seconds = Math.max(24, list.scrollWidth / 28);
      track.style.setProperty("--contributor-duration", `${seconds}s`);
    };

    const scheduleLoopRebuild = () => {
      if (viewport.clientWidth === lastViewportWidth || rebuildScheduled) return;
      rebuildScheduled = true;
      requestAnimationFrame(() => {
        rebuildScheduled = false;
        buildLoop();
      });
    };

    window.addEventListener("resize", scheduleLoopRebuild, { passive: true });

    const loadContributors = async () => {
      section.setAttribute("aria-busy", "true");
      setStatus("Loading contributors…");

      try {
        const endpoint = `https://api.github.com/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repository)}/contributors?per_page=20`;
        const response = await fetch(endpoint, {
          headers: { Accept: "application/vnd.github+json" },
        });
        if (!response.ok) throw new Error(`GitHub returned ${response.status}`);

        const results = await response.json();
        contributorItems = Array.isArray(results)
          ? results.map(createContributor).filter(Boolean)
          : [];

        if (contributorItems.length === 0) {
          section.classList.add("is-static");
          setStatus("No contributors were returned. The full list is on GitHub.");
          return;
        }

        section.classList.add("is-ready");
        setStatus("");
        requestAnimationFrame(buildLoop);
        if (document.fonts?.ready) document.fonts.ready.then(() => requestAnimationFrame(buildLoop));
      } catch {
        section.classList.add("is-static");
        setStatus("Contributors could not be loaded. The full list is on GitHub.");
      } finally {
        section.removeAttribute("aria-busy");
      }
    };

    loadContributors();
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeContributors, { once: true });
  } else {
    initializeContributors();
  }
})();
