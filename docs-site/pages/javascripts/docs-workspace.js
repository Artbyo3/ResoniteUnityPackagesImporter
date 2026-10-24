(() => {
  const workspace = document.querySelector("[data-doc-workspace]");
  if (!workspace) return;
  const base = new URL("../", document.currentScript.src);
  const reader = workspace.querySelector("[data-doc-reader]");
  const context = workspace.querySelector("[data-doc-context]");
  const announcement = workspace.querySelector("[data-doc-announcement]");
  const find = workspace.querySelector("[data-doc-find]");
  const input = find.querySelector("input");
  const results = find.querySelector("[id=docs-find-results]");
  const searchStatus = find.querySelector("[data-doc-search-status]");
  const positions = new Map();
  let activeId = workspace.dataset.docInitial;
  let data;
  let topics;
  let groups;
  let searchIndex;

  const urlFor = topic => {
    const url = new URL(topic.route, base);
    if (topic.anchor) url.hash = topic.anchor;
    return url;
  };
  const makeLink = (topic, label = topic.label) => {
    const link = document.createElement("a");
    link.href = urlFor(topic).href;
    link.dataset.docTopic = topic.id;
    link.textContent = label;
    return link;
  };
  const clearSearch = (close = true) => {
    input.value = "";
    input.setAttribute("aria-expanded", "false");
    results.hidden = true;
    results.replaceChildren();
    searchStatus.textContent = "";
    if (close) find.hidden = true;
  };
  const resolveTopic = url => {
    const candidates = data.topics.filter(topic => new URL(topic.route, base).pathname === url.pathname);
    return candidates.find(topic => url.hash && (topic.anchor === url.hash.slice(1) || topic.anchors.has(url.hash.slice(1)))) || candidates[0];
  };
  const answerFragment = topic => {
    const template = document.createElement("template");
    template.innerHTML = topic.html;
    const origin = new URL(topic.route, base);
    for (const element of template.content.querySelectorAll("[href], [src], [poster]")) {
      for (const attribute of ["href", "src", "poster"]) {
        const value = element.getAttribute(attribute);
        if (value) element.setAttribute(attribute, new URL(value, origin).href);
      }
      if (element.tagName === "IMG") {
        element.loading = "lazy";
        element.decoding = "async";
      }
    }
    return template.content;
  };
  const renderAnswer = topic => {
    for (const video of reader.querySelectorAll("video")) video.pause();
    reader.replaceChildren(answerFragment(topic));
  };
  const revealTarget = target => {
    for (let parent = target?.parentElement; parent; parent = parent.parentElement) {
      if (parent.tagName === "DETAILS") parent.open = true;
    }
    const frame = target?.closest("[data-visual-frame]");
    if (frame?.hidden) {
      const guide = frame.closest("[data-visual-guide]");
      const index = [...guide.querySelectorAll("[data-visual-frame]")].indexOf(frame);
      guide.querySelectorAll("[data-visual-select]")[index]?.click();
    }
  };
  const showTopic = (identifier, { push = true, restore = false, url = null, focus = true } = {}) => {
    const topic = topics.get(identifier);
    if (!topic) return;
    if (activeId !== identifier) positions.set(activeId, scrollY);
    activeId = identifier;
    const group = groups.get(topic.group);
    renderAnswer(topic);
    window.initializeSupportMaps?.(reader);
    window.initializeVisualGuides?.(reader);
    context.textContent = group.label;
    for (const item of document.querySelectorAll('.md-sidebar--primary .md-nav__item--active')) item.classList.remove("md-nav__item--active");
    for (const toggle of document.querySelectorAll('.md-sidebar--primary input.md-nav__toggle')) toggle.checked = false;
    for (const link of document.querySelectorAll('.md-sidebar--primary .md-nav__link[href]')) {
      const target = new URL(link.href);
      const selected = target.pathname === urlFor(topic).pathname && target.hash === urlFor(topic).hash;
      link.classList.toggle("md-nav__link--active", selected);
      if (selected) {
        link.setAttribute("aria-current", "page");
        for (let item = link.closest(".md-nav__item"); item; item = item.parentElement?.closest(".md-nav__item")) {
          item.classList.add("md-nav__item--active");
          const toggle = item.querySelector(":scope > input.md-nav__toggle");
          if (toggle) toggle.checked = true;
        }
      } else link.removeAttribute("aria-current");
    }
    const drawer = document.getElementById("__drawer");
    if (drawer) drawer.checked = false;
    document.title = `${topic.title} - Unity Package Importer`;
    const headerTopic = document.querySelector('[data-md-component="header-topic"] .md-ellipsis');
    if (headerTopic) headerTopic.textContent = "Documentation";
    clearSearch();
    if (push) history.pushState({ docTopic: identifier }, "", url || urlFor(topic));
    announcement.textContent = `${topic.title}. ${group.label}.`;
    if (focus) {
      reader.querySelector("h1").focus({ preventScroll: true });
      reader.classList.remove("docs-reader--enter");
      void reader.offsetWidth;
      reader.classList.add("docs-reader--enter");
      const fragment = (url || urlFor(topic)).hash.slice(1);
      const target = fragment ? document.getElementById(fragment) : null;
      if (target && target !== reader.firstElementChild) {
        revealTarget(target);
        target.scrollIntoView({ block: "start" });
      }
      else window.scrollTo({ top: restore ? positions.get(identifier) || 0 : 0, behavior: "auto" });
    }
  };
  const snippet = (text, terms) => {
    const lower = text.toLocaleLowerCase();
    const matches = terms.map(term => lower.indexOf(term)).filter(index => index >= 0);
    const start = Math.max(0, (matches.length ? Math.min(...matches) : 0) - 45);
    return `${start ? "..." : ""}${text.slice(start, start + 180).trim()}${text.length > start + 180 ? "..." : ""}`;
  };
  const addHighlightedText = (element, text, terms) => {
    const lower = text.toLocaleLowerCase();
    let position = 0;
    while (position < text.length) {
      const matches = terms.map(term => ({ index: lower.indexOf(term, position), length: term.length }))
        .filter(match => match.index >= 0).sort((first, second) => first.index - second.index);
      if (!matches.length) {
        element.append(document.createTextNode(text.slice(position)));
        break;
      }
      const match = matches[0];
      element.append(document.createTextNode(text.slice(position, match.index)));
      const mark = document.createElement("mark");
      mark.textContent = text.slice(match.index, match.index + match.length);
      element.append(mark);
      position = match.index + match.length;
    }
  };
  const highlightAnswer = terms => {
    const walker = document.createTreeWalker(reader, NodeFilter.SHOW_TEXT);
    const nodes = [];
    while (walker.nextNode()) nodes.push(walker.currentNode);
    for (const node of nodes) {
      if (!terms.some(term => node.textContent.toLocaleLowerCase().includes(term))) continue;
      const fragment = document.createDocumentFragment();
      addHighlightedText(fragment, node.textContent, terms);
      node.replaceWith(fragment);
    }
    const match = reader.querySelector("mark");
    revealTarget(match);
    match?.scrollIntoView({ block: "center" });
  };
  const search = () => {
    const query = input.value.trim().toLocaleLowerCase();
    const terms = query.split(/\s+/).filter(Boolean);
    if (!terms.length) { clearSearch(false); return; }
    const matches = searchIndex.filter(item => terms.every(term => item.search.includes(term)))
      .sort((first, second) => Number(second.title.includes(query)) - Number(first.title.includes(query)));
    results.replaceChildren();
    const heading = document.createElement("p");
    heading.className = "docs-find__count";
    heading.textContent = matches.length ? `${matches.length} ${matches.length === 1 ? "answer" : "answers"}` : "No documented answer found. Try the asset, shader, or symptom.";
    results.append(heading);
    for (const match of matches) {
      const link = makeLink(match.topic, "");
      link.className = "docs-find__result";
      const title = document.createElement("strong");
      title.textContent = match.topic.title;
      const group = document.createElement("span");
      group.className = "docs-find__group";
      group.textContent = groups.get(match.topic.group).label;
      const excerpt = document.createElement("p");
      addHighlightedText(excerpt, snippet(match.text, terms), terms);
      link.append(title, group, excerpt);
      results.append(link);
    }
    results.hidden = false;
    input.setAttribute("aria-expanded", "true");
    searchStatus.textContent = heading.textContent;
  };

  fetch(new URL("assets/docs-workspace.json", base)).then(response => {
    if (!response.ok) throw new Error(`Documentation navigation: HTTP ${response.status}`);
    return response.json();
  }).then(payload => {
    data = payload;
    topics = new Map(data.topics.map(topic => [topic.id, topic]));
    groups = new Map(data.groups.map(group => [group.id, group]));
    searchIndex = data.topics.map(topic => {
      const template = document.createElement("template");
      template.innerHTML = topic.html;
      const text = template.content.textContent.replace(/\s+/g, " ").trim();
      topic.anchors = new Set([...template.content.querySelectorAll("[id]")].map(element => element.id));
      return { topic, text, title: topic.title.toLocaleLowerCase(), search: `${topic.title} ${topic.keywords || ""} ${text}`.toLocaleLowerCase() };
    });
    for (const link of document.querySelectorAll("a[href]")) link.href = new URL(link.getAttribute("href"), location.href).href;
    const initial = resolveTopic(new URL(location.href)) || topics.get(activeId);
    showTopic(initial.id, { push: false, focus: false });
    if (location.hash) {
      const target = document.getElementById(decodeURIComponent(location.hash.slice(1)));
      revealTarget(target);
      target?.scrollIntoView({ block: "start" });
    }
    history.replaceState({ ...history.state, docTopic: initial.id }, "", location.href);
    workspace.dataset.docEnhanced = "true";
    input.setAttribute("aria-expanded", "false");
    document.addEventListener("click", event => {
      const link = event.target.closest("a[href]");
      if (!link || event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || link.hasAttribute("download") || link.target) return;
      const url = new URL(link.href);
      if (url.origin !== location.origin) return;
      let topic = topics.get(link.dataset.docTopic) || resolveTopic(url);
      if (!topic) return;
      if (topic.id === activeId && url.hash && document.getElementById(url.hash.slice(1)) && !link.dataset.docTopic) {
        revealTarget(document.getElementById(url.hash.slice(1)));
        return;
      }
      event.preventDefault();
      const terms = link.classList.contains("docs-find__result") ? input.value.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean) : [];
      if (topic.id === activeId && !terms.length) { clearSearch(); return; }
      showTopic(topic.id, { url });
      if (terms.length) highlightAnswer(terms);
    });
    input.addEventListener("input", search);
    find.addEventListener("keydown", event => {
      if (event.key === "Escape") { clearSearch(); document.querySelector(".md-search__button")?.focus(); }
      if (event.key === "Enter" && event.target === input && !results.hidden) {
        event.preventDefault(); results.querySelector("a")?.click();
      }
      if (event.key === "ArrowDown" && event.target === input && !results.hidden) {
        event.preventDefault(); results.querySelector("a")?.focus();
      }
    });
    const openSearch = () => {
      find.hidden = false;
      input.focus();
      find.scrollIntoView({ block: "nearest" });
    };
    document.addEventListener("click", event => {
      if (event.target.closest('.md-search__button, .md-header [for="__search"]')) {
        event.preventDefault(); event.stopImmediatePropagation(); openSearch();
      }
    }, true);
    document.addEventListener("keydown", event => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLocaleLowerCase() === "k") {
        event.preventDefault(); event.stopImmediatePropagation(); openSearch();
      }
    }, true);
    window.addEventListener("popstate", () => {
      const topic = resolveTopic(new URL(location.href));
      if (topic) showTopic(topic.id, { push: false, restore: true, url: new URL(location.href) });
    });
    window.addEventListener("pagehide", () => positions.set(activeId, scrollY));
  }).catch(error => {
    console.warn("Instant documentation navigation is unavailable; standard documentation links remain available.", error);
  });
})();
