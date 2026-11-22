(() => {
  const post = (msg) => {
    try {
      window.chrome?.webview?.postMessage(msg);
    } catch {
      // ignore
    }
  };

  const els = {
    app: document.getElementById("app"),
    collapsedView: document.getElementById("collapsedView"),
    folderBox: document.getElementById("folderBox"),
    miniGrid: document.getElementById("miniGrid"),
    customFolderIcon: document.getElementById("customFolderIcon"),
    folderLabel: document.getElementById("folderLabel"),

    expandedView: document.getElementById("expandedView"),
    pinBadge: document.getElementById("pinBadge"),
    titleInput: document.getElementById("titleInput"),
    countPill: document.getElementById("countPill"),
    btnAddFiles: document.getElementById("btnAddFiles"),
    btnSettings: document.getElementById("btnSettings"),

    settingsDrawer: document.getElementById("settingsDrawer"),
    accentSwatches: document.getElementById("accentSwatches"),
    btnPickCustomIcon: document.getElementById("btnPickCustomIcon"),
    btnClearCustomIcon: document.getElementById("btnClearCustomIcon"),
    btnOpenExplorer: document.getElementById("btnOpenExplorer"),
    btnDeleteFolder: document.getElementById("btnDeleteFolder"),
    deleteConfirm: document.getElementById("deleteConfirm"),
    btnDeleteRestore: document.getElementById("btnDeleteRestore"),
    btnDeleteWipe: document.getElementById("btnDeleteWipe"),
    btnDeleteCancel: document.getElementById("btnDeleteCancel"),
    animDurationRange: document.getElementById("animDurationRange"),
    animDurationValue: document.getElementById("animDurationValue"),
    hoverDelayRange: document.getElementById("hoverDelayRange"),
    hoverDelayValue: document.getElementById("hoverDelayValue"),

    searchWrap: document.getElementById("searchWrap"),
    searchInput: document.getElementById("searchInput"),
    itemsGrid: document.getElementById("itemsGrid"),
    emptyState: document.getElementById("emptyState"),
    dropOverlayExpanded: document.getElementById("dropOverlayExpanded"),
    statusText: document.getElementById("statusText"),
    modeHint: document.getElementById("modeHint"),
  };

  const I18N = {
    tr: {
      collapsedTitle: "Üzerinde dur: aç · Tıkla: sabitle · Sürükle: taşı",
      defaultFolderName: "Yeni Klasör",
      titlePlaceholder: "İsimsiz",
      titleAria: "Klasör adı",
      addFilesTitle: "Dosya / kısayol ekle (masaüstünden taşır)",
      settingsTitle: "Klasör ayarları",
      accentLabel: "Aksan",
      swatchIceBlue: "Buz Mavisi",
      swatchAmber: "Amber",
      swatchEmerald: "Zümrüt",
      swatchOrange: "Turuncu",
      swatchCoral: "Mercan",
      swatchSilver: "Gümüş",
      previewLabel: "Önizleme",
      customIconLabel: "Özel İkon",
      chooseIcon: "İkon Seç",
      resetIcon: "Sıfırla",
      openExplorer: "Explorer'da Aç",
      deleteFolder: "Klasörü Sil",
      deleteConfirmPrompt: "İçindekiler masaüstüne geri çıkarılsın mı?",
      deleteRestore: "Geri Çıkar & Sil",
      deleteWipe: "Hepsini Sil",
      cancel: "İptal",
      globalSection: "GENEL",
      animDurationLabel: "Animasyon Süresi",
      hoverDelayLabel: "Hover Gecikmesi",
      searchPlaceholder: "Klasör içinde ara...",
      emptyTitle: "Klasör boş",
      emptySub:
        "Masaüstündeki oyunları veya dosyaları buraya sürükleyip bırak.<br />Attığın öğeler masaüstünden bu klasöre taşınır.",
      dropTitle: "Klasöre taşı",
      dropSub: "Masaüstünden gizlenip buraya eklenir",
      statusDefault: "Sürükle bırak · Masaüstünden taşınır",
      pinTitleMode0: "Sabitle",
      pinTitleMode1: "Sabit (tekrar tıkla: masaüstüne kilitle)",
      pinTitleMode2: "Masaüstüne kilitli (tıkla: kapat)",
      modeHintMode0: "Tıkla: sabitle",
      modeHintMode1: "Sabit · 2. tık: kilitle",
      modeHintMode2: "Masaüstüne kilitli",
      dragOutBadge: "Masaüstüne çıkar",
      movedToFolder: (count) => `${count} öğe klasöre taşındı`,
    },
    en: {
      collapsedTitle: "Hover: open · Click: pin · Drag: move",
      defaultFolderName: "New Folder",
      titlePlaceholder: "Untitled",
      titleAria: "Folder name",
      addFilesTitle: "Add files / shortcuts (moves from desktop)",
      settingsTitle: "Folder settings",
      accentLabel: "Accent",
      swatchIceBlue: "Ice Blue",
      swatchAmber: "Amber",
      swatchEmerald: "Emerald",
      swatchOrange: "Orange",
      swatchCoral: "Coral",
      swatchSilver: "Silver",
      previewLabel: "Preview",
      customIconLabel: "Custom Icon",
      chooseIcon: "Choose Icon",
      resetIcon: "Reset",
      openExplorer: "Open in Explorer",
      deleteFolder: "Delete Folder",
      deleteConfirmPrompt: "Restore items back to the desktop?",
      deleteRestore: "Restore & Delete",
      deleteWipe: "Delete All",
      cancel: "Cancel",
      globalSection: "GLOBAL",
      animDurationLabel: "Animation",
      hoverDelayLabel: "Hover Delay",
      searchPlaceholder: "Search inside folder...",
      emptyTitle: "Folder is empty",
      emptySub:
        "Drag and drop desktop games or files here.<br />Dropped items are moved from the desktop into this folder.",
      dropTitle: "Move to folder",
      dropSub: "Hidden from desktop and added here",
      statusDefault: "Drag & drop · Moves from desktop",
      pinTitleMode0: "Pin",
      pinTitleMode1: "Pinned (click again: lock to desktop)",
      pinTitleMode2: "Locked to desktop (click: close)",
      modeHintMode0: "Click: pin",
      modeHintMode1: "Pinned · 2nd click: lock",
      modeHintMode2: "Locked to desktop",
      dragOutBadge: "Move to desktop",
      movedToFolder: (count) => `${count} item(s) moved to folder`,
    },
  };

  const state = {
    locale: (navigator.language || "").toLowerCase().startsWith("tr") ? "tr" : "en",
    folder: { id: "", name: "", accentColor: "#a9f1fe", previewGrid: 2, customIconUrl: null },
    settings: { hoverDelayMs: 400, animationDurationMs: 400, glassOpacity: 90 },
    items: [],
    expanded: false,
    pinned: false,
    pinMode: 0,
    searchQuery: "",
    draggingWindow: false,
    dragInternalId: null,
  };

  function t(key, ...args) {
    const dict = I18N[state.locale] || I18N.en;
    const val = dict[key] ?? I18N.en[key] ?? key;
    return typeof val === "function" ? val(...args) : val;
  }

  function applyLocale() {
    const locale = state.locale === "tr" ? "tr" : "en";
    document.documentElement.lang = locale;

    document.querySelectorAll("[data-i18n]").forEach((el) => {
      el.textContent = t(el.dataset.i18n);
    });
    document.querySelectorAll("[data-i18n-html]").forEach((el) => {
      el.innerHTML = t(el.dataset.i18nHtml);
    });
    document.querySelectorAll("[data-i18n-placeholder]").forEach((el) => {
      el.placeholder = t(el.dataset.i18nPlaceholder);
    });
    document.querySelectorAll("[data-i18n-title]").forEach((el) => {
      el.title = t(el.dataset.i18nTitle);
    });
    document.querySelectorAll("[data-i18n-aria]").forEach((el) => {
      el.setAttribute("aria-label", t(el.dataset.i18nAria));
    });

    if (!statusResetTimer) {
      els.statusText.textContent = t("statusDefault");
    }
    updatePinUI();
  }

  let hoverOpenTimer = null;
  let hoverCloseTimer = null;
  let collapseTimer = null;
  let collapseRaf1 = 0;
  let collapseRaf2 = 0;
  let fadeOutTimer = null;
  let fadeOutRaf1 = 0;
  let fadeOutRaf2 = 0;
  let fadingOut = false;
  let fadeOutPinned = false;
  let pendingOpenSettings = false;
  let statusResetTimer = null;
  let globalSettingsTimer = null;
  let externalDragDepth = 0;
  let pointerDrag = null;

  els.expandedView.inert = true;

  // =========================================================================
  // THEME & SETTINGS HELPERS
  // =========================================================================
  function hexToRgb(hex) {
    const clean = (hex || "#a9f1fe").replace("#", "").trim();
    if (clean.length !== 6) return "169, 241, 254";
    const r = parseInt(clean.slice(0, 2), 16);
    const g = parseInt(clean.slice(2, 4), 16);
    const b = parseInt(clean.slice(4, 6), 16);
    if (Number.isNaN(r) || Number.isNaN(g) || Number.isNaN(b)) return "169, 241, 254";
    return `${r}, ${g}, ${b}`;
  }

  function applyTheme() {
    const root = document.documentElement;
    const animMs = Number(state.settings.animationDurationMs) || 400;
    const hoverMs = Number(state.settings.hoverDelayMs) || 400;

    root.style.setProperty("--accent", state.folder.accentColor || "#a9f1fe");
    root.style.setProperty("--accent-rgb", hexToRgb(state.folder.accentColor));
    root.style.setProperty("--glass-opacity", String((state.settings.glassOpacity || 90) / 100));
    root.style.setProperty("--anim-duration", `${animMs}ms`);

    els.accentSwatches.querySelectorAll(".swatch").forEach((btn) => {
      const active = btn.dataset.color?.toLowerCase() === (state.folder.accentColor || "").toLowerCase();
      btn.classList.toggle("active", active);
    });

    document.querySelectorAll(".seg-btn").forEach((btn) => {
      btn.classList.toggle("active", Number(btn.dataset.grid) === state.folder.previewGrid);
    });

    els.btnClearCustomIcon.hidden = !state.folder.customIconUrl;

    if (document.activeElement !== els.animDurationRange) {
      els.animDurationRange.value = String(animMs);
    }
    els.animDurationValue.textContent = `${animMs} ms`;

    if (document.activeElement !== els.hoverDelayRange) {
      els.hoverDelayRange.value = String(hoverMs);
    }
    els.hoverDelayValue.textContent = `${hoverMs} ms`;
  }

  function showStatus(text, highlight = false) {
    clearTimeout(statusResetTimer);
    els.statusText.textContent = text;
    els.statusText.classList.toggle("highlight", highlight);
    statusResetTimer = setTimeout(() => {
      statusResetTimer = null;
      els.statusText.textContent = t("statusDefault");
      els.statusText.classList.remove("highlight");
    }, 3200);
  }

  // =========================================================================
  // POINTER DRAG HELPERS (Reorder + Drag Out to Desktop)
  // =========================================================================
  function createDragGhost(item) {
    const ghost = document.createElement("div");
    ghost.className = "drag-ghost";

    const iconWrap = document.createElement("div");
    iconWrap.className = "tile-icon-wrap";
    if (item.iconUrl) {
      const img = document.createElement("img");
      img.className = "tile-icon";
      img.src = item.iconUrl;
      img.alt = "";
      img.draggable = false;
      iconWrap.appendChild(img);
    } else {
      const fb = document.createElement("div");
      fb.className = "tile-fallback";
      fb.textContent = (item.name || "?").slice(0, 2).toUpperCase();
      iconWrap.appendChild(fb);
    }

    const label = document.createElement("div");
    label.className = "tile-name";
    label.textContent = item.name;

    const badge = document.createElement("div");
    badge.className = "drag-out-badge";
    badge.textContent = t("dragOutBadge");

    ghost.appendChild(iconWrap);
    ghost.appendChild(label);
    ghost.appendChild(badge);
    return ghost;
  }

  function positionDragGhost(ghost, clientX, clientY) {
    if (!ghost) return;
    ghost.style.transform = `translate3d(${clientX}px, ${clientY}px, 0) translate(-50%, -50%)`;
  }

  function clearTargetHighlight() {
    els.itemsGrid.querySelectorAll(".drag-target").forEach((node) => {
      node.classList.remove("drag-target");
    });
  }

  function clearTileDrag() {
    if (!pointerDrag) return;
    pointerDrag.tile?.classList.remove("dragging-tile");
    pointerDrag.ghost?.remove();
    clearTargetHighlight();
    state.dragInternalId = null;
    pointerDrag = null;
  }

  function findDropTargetTile(clientX, clientY, sourceId) {
    const stack = document.elementsFromPoint(clientX, clientY);
    for (const el of stack) {
      const tile = el.closest?.(".app-tile");
      if (tile && tile.dataset.id && tile.dataset.id !== sourceId) {
        return tile;
      }
    }
    return null;
  }

  // =========================================================================
  // RENDERING
  // =========================================================================
  function renderCollapsed() {
    const labelText = (state.folder.name || "").trim();
    els.folderLabel.textContent = labelText;
    els.folderLabel.hidden = labelText.length === 0;
    els.folderBox.dataset.grid = String(state.folder.previewGrid || 2);

    if (state.folder.customIconUrl) {
      els.miniGrid.hidden = true;
      els.miniGrid.innerHTML = "";
      els.customFolderIcon.src = state.folder.customIconUrl;
      els.customFolderIcon.alt = labelText;
      els.customFolderIcon.hidden = false;
      return;
    }

    els.customFolderIcon.hidden = true;
    els.customFolderIcon.removeAttribute("src");
    els.miniGrid.hidden = false;

    const maxIcons = state.folder.previewGrid === 2 ? 4 : 9;
    const preview = state.items.slice(0, maxIcons);
    els.miniGrid.innerHTML = "";

    if (preview.length === 0) {
      const empty = document.createElement("div");
      empty.className = "mini-empty";
      empty.textContent = "+";
      els.miniGrid.appendChild(empty);
      return;
    }

    for (const item of preview) {
      if (item.iconUrl) {
        const img = document.createElement("img");
        img.className = "mini-icon";
        img.src = item.iconUrl;
        img.alt = item.name;
        img.draggable = false;
        els.miniGrid.appendChild(img);
      } else {
        const fb = document.createElement("div");
        fb.className = "mini-fallback";
        fb.textContent = (item.name || "?").slice(0, 2).toUpperCase();
        els.miniGrid.appendChild(fb);
      }
    }
  }

  function renderExpanded() {
    if (document.activeElement !== els.titleInput) {
      els.titleInput.value = state.folder.name || "";
    }
    els.countPill.textContent = String(state.items.length);
    els.searchWrap.hidden = state.items.length <= 8;

    const q = state.searchQuery.trim().toLowerCase();
    const filtered = q
      ? state.items.filter((it) => it.name.toLowerCase().includes(q))
      : state.items;

    clearTileDrag();
    els.itemsGrid.innerHTML = "";
    els.emptyState.hidden = filtered.length > 0;

    for (const item of filtered) {
      const tile = document.createElement("div");
      tile.className = "app-tile";
      tile.dataset.id = item.id;
      tile.title = item.name;

      const iconWrap = document.createElement("div");
      iconWrap.className = "tile-icon-wrap";
      if (item.iconUrl) {
        const img = document.createElement("img");
        img.className = "tile-icon";
        img.src = item.iconUrl;
        img.alt = item.name;
        img.draggable = false;
        iconWrap.appendChild(img);
      } else {
        const fb = document.createElement("div");
        fb.className = "tile-fallback";
        fb.textContent = (item.name || "?").slice(0, 2).toUpperCase();
        iconWrap.appendChild(fb);
      }

      const label = document.createElement("div");
      label.className = "tile-name";
      label.textContent = item.name;

      tile.appendChild(iconWrap);
      tile.appendChild(label);

      // Pointer-based click / internal reorder / drag-out-to-desktop
      tile.addEventListener("pointerdown", (e) => {
        if (e.button !== 0 || !state.expanded) return;
        e.preventDefault();
        clearTileDrag();
        pointerDrag = {
          item,
          tile,
          pointerId: e.pointerId,
          startX: e.clientX,
          startY: e.clientY,
          started: false,
          draggedOut: false,
          ghost: null,
          targetId: null,
        };
        try {
          tile.setPointerCapture(e.pointerId);
        } catch {
          // ignore
        }
      });

      // Native Windows Shell right-click menu
      tile.addEventListener("contextmenu", (e) => {
        e.preventDefault();
        e.stopPropagation();
        if (state.pinMode === 0) {
          post({ type: "folder.pin" });
        }
        post({
          type: "folder.showShellContextMenu",
          path: item.path,
          screenX: Math.round(e.screenX),
          screenY: Math.round(e.screenY),
        });
      });

      els.itemsGrid.appendChild(tile);
    }
  }

  function reorderItems(sourceId, targetId) {
    const ids = state.items.map((x) => x.id);
    const fromIdx = ids.indexOf(sourceId);
    const toIdx = ids.indexOf(targetId);
    if (fromIdx < 0 || toIdx < 0 || fromIdx === toIdx) return;
    ids.splice(fromIdx, 1);
    ids.splice(toIdx, 0, sourceId);

    const byId = new Map(state.items.map((x) => [x.id, x]));
    state.items = ids.map((id) => byId.get(id)).filter(Boolean);
    renderCollapsed();
    renderExpanded();
    post({ type: "folder.reorderItems", order: ids });
  }

  function updatePinUI() {
    els.pinBadge.classList.toggle("pinned", state.pinMode === 1);
    els.pinBadge.classList.toggle("desktop-locked", state.pinMode === 2);
    els.pinBadge.title =
      state.pinMode === 2
        ? t("pinTitleMode2")
        : state.pinMode === 1
          ? t("pinTitleMode1")
          : t("pinTitleMode0");
    els.modeHint.textContent =
      state.pinMode === 2
        ? t("modeHintMode2")
        : state.pinMode === 1
          ? t("modeHintMode1")
          : t("modeHintMode0");
  }

  window.addEventListener("pointermove", (e) => {
    if (!pointerDrag || e.pointerId !== pointerDrag.pointerId) return;
    const dx = e.clientX - pointerDrag.startX;
    const dy = e.clientY - pointerDrag.startY;

    if (!pointerDrag.started) {
      if (Math.hypot(dx, dy) <= 6) return;
      pointerDrag.started = true;
      state.dragInternalId = pointerDrag.item.id;
      pointerDrag.tile.classList.add("dragging-tile");
      pointerDrag.ghost = createDragGhost(pointerDrag.item);
      document.body.appendChild(pointerDrag.ghost);
    }

    positionDragGhost(pointerDrag.ghost, e.clientX, e.clientY);

    const isOutside =
      e.clientX < 6 ||
      e.clientY < 6 ||
      e.clientX > window.innerWidth - 6 ||
      e.clientY > window.innerHeight - 6;

    if (isOutside) {
      clearTargetHighlight();
      pointerDrag.targetId = null;
      pointerDrag.ghost.classList.add("drag-out");
      if (!pointerDrag.draggedOut) {
        pointerDrag.draggedOut = true;
        post({ type: "folder.beginDragOutToDesktop", itemId: pointerDrag.item.id });
      }
      return;
    }

    if (pointerDrag.draggedOut) {
      return;
    }

    pointerDrag.ghost.classList.remove("drag-out");
    const hitTile = findDropTargetTile(e.clientX, e.clientY, pointerDrag.item.id);
    const nextTargetId = hitTile ? hitTile.dataset.id : null;
    if (nextTargetId !== pointerDrag.targetId) {
      clearTargetHighlight();
      if (hitTile) {
        hitTile.classList.add("drag-target");
      }
      pointerDrag.targetId = nextTargetId;
    }
  });

  window.addEventListener("pointerup", (e) => {
    if (!pointerDrag || e.pointerId !== pointerDrag.pointerId) return;
    const drag = pointerDrag;
    try {
      if (drag.tile.hasPointerCapture?.(drag.pointerId)) {
        drag.tile.releasePointerCapture(drag.pointerId);
      }
    } catch {
      // ignore
    }
    clearTileDrag();

    if (!drag.started) {
      if (e.button === 0 && state.expanded) {
        post({ type: "folder.launchItem", path: drag.item.path });
      }
      return;
    }

    if (!drag.draggedOut && drag.targetId && drag.targetId !== drag.item.id) {
      reorderItems(drag.item.id, drag.targetId);
    }
  });

  window.addEventListener("pointercancel", (e) => {
    if (pointerDrag && e.pointerId === pointerDrag.pointerId) {
      clearTileDrag();
    }
  });

  window.addEventListener("blur", () => {
    clearTileDrag();
  });

  // =========================================================================
  // COLLAPSED INTERACTION (Hover open, Click pin, Drag move, Right-click)
  // =========================================================================
  let pointerDownPos = null;
  let movedEnoughForDrag = false;

  function clearPendingFadeOut() {
    clearTimeout(fadeOutTimer);
    fadeOutTimer = null;
    if (fadeOutRaf1) {
      cancelAnimationFrame(fadeOutRaf1);
      fadeOutRaf1 = 0;
    }
    if (fadeOutRaf2) {
      cancelAnimationFrame(fadeOutRaf2);
      fadeOutRaf2 = 0;
    }
    fadingOut = false;
    fadeOutPinned = false;
  }

  function clearPendingCollapse() {
    clearTimeout(collapseTimer);
    collapseTimer = null;
    if (collapseRaf1) {
      cancelAnimationFrame(collapseRaf1);
      collapseRaf1 = 0;
    }
    if (collapseRaf2) {
      cancelAnimationFrame(collapseRaf2);
      collapseRaf2 = 0;
    }
  }

  function cancelExpandSequence() {
    const wasFadingOut = fadingOut;
    clearPendingFadeOut();
    if (wasFadingOut) {
      pendingOpenSettings = false;
      if (!state.expanded) {
        els.app.dataset.state = "collapsed";
      }
    }
  }

  function requestExpandSequence(pinned) {
    if (state.expanded || state.draggingWindow) return;
    if (fadingOut) {
      if (pinned) fadeOutPinned = true;
      return;
    }

    clearTimeout(hoverOpenTimer);
    clearTimeout(collapseTimer);
    fadingOut = true;
    fadeOutPinned = Boolean(pinned);
    els.app.dataset.state = "fading-out";

    fadeOutTimer = setTimeout(() => {
      fadeOutTimer = null;
      if (!fadingOut || state.expanded || state.draggingWindow) return;
      fadeOutRaf1 = requestAnimationFrame(() => {
        fadeOutRaf1 = 0;
        if (!fadingOut || state.expanded || state.draggingWindow) return;
        fadeOutRaf2 = requestAnimationFrame(() => {
          fadeOutRaf2 = 0;
          if (!fadingOut || state.expanded || state.draggingWindow) return;
          const finalPinned = fadeOutPinned;
          fadingOut = false;
          fadeOutPinned = false;
          post({ type: "folder.readyToExpand", pinned: finalPinned });
        });
      });
    }, 130);
  }

  els.collapsedView.addEventListener("mouseenter", () => {
    if (state.expanded || state.draggingWindow || fadingOut) return;
    clearTimeout(hoverOpenTimer);
    hoverOpenTimer = setTimeout(() => {
      if (!state.expanded && !state.draggingWindow) {
        requestExpandSequence(false);
      }
    }, Number(state.settings.hoverDelayMs) || 400);
  });

  els.collapsedView.addEventListener("mouseleave", () => {
    clearTimeout(hoverOpenTimer);
    if (fadingOut && !fadeOutPinned) {
      cancelExpandSequence();
    }
  });

  els.collapsedView.addEventListener("mousedown", (e) => {
    if (e.button !== 0) return;
    clearTimeout(hoverOpenTimer);
    pointerDownPos = { x: e.screenX, y: e.screenY };
    movedEnoughForDrag = false;
  });

  window.addEventListener("mousemove", (e) => {
    if (!pointerDownPos || state.expanded || state.draggingWindow) return;
    const dx = Math.abs(e.screenX - pointerDownPos.x);
    const dy = Math.abs(e.screenY - pointerDownPos.y);
    if (dx > 6 || dy > 6) {
      movedEnoughForDrag = true;
      state.draggingWindow = true;
      pointerDownPos = null;
      clearTimeout(hoverOpenTimer);
      cancelExpandSequence();
      post({ type: "folder.dragStart" });
    }
  });

  window.addEventListener("mouseup", (e) => {
    if (e.button !== 0) return;
    if (pointerDownPos && !movedEnoughForDrag && !state.expanded) {
      clearTimeout(hoverOpenTimer);
      requestExpandSequence(true);
    }
    pointerDownPos = null;
    movedEnoughForDrag = false;
  });

  els.collapsedView.addEventListener("contextmenu", (e) => {
    e.preventDefault();
    clearTimeout(hoverOpenTimer);
    pendingOpenSettings = true;
    requestExpandSequence(true);
  });

  // =========================================================================
  // EXPANDED INTERACTION (Hover leave collapse, Click to pin, Esc to close)
  // =========================================================================
  els.expandedView.addEventListener("mouseenter", () => {
    clearTimeout(hoverCloseTimer);
  });

  els.expandedView.addEventListener("mouseleave", () => {
    if (!state.expanded || state.pinMode > 0 || pointerDrag?.started) return;
    clearTimeout(hoverCloseTimer);
    hoverCloseTimer = setTimeout(() => {
      if (state.expanded && state.pinMode === 0 && !pointerDrag?.started) {
        post({ type: "folder.requestCollapse" });
      }
    }, 240);
  });

  els.expandedView.addEventListener("mousedown", () => {
    if (state.expanded && state.pinMode === 0) {
      post({ type: "folder.pin" });
    }
  });

  els.pinBadge.addEventListener("mousedown", (e) => {
    e.stopPropagation();
  });

  els.pinBadge.addEventListener("click", (e) => {
    e.stopPropagation();
    if (state.pinMode === 0) {
      post({ type: "folder.setPinMode", mode: 1 });
    } else if (state.pinMode === 1) {
      post({ type: "folder.setPinMode", mode: 2 });
    } else {
      post({ type: "folder.setPinMode", mode: 0 });
    }
  });

  window.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && state.expanded && state.pinMode !== 2) {
      post({ type: "folder.requestCollapse" });
    }
  });

  // =========================================================================
  // HEADER & SETTINGS CONTROLS
  // =========================================================================
  function commitTitle() {
    const val = els.titleInput.value.trim();
    const current = state.folder.name || "";
    if (val !== current) {
      state.folder.name = val;
      els.titleInput.value = val;
      renderCollapsed();
      post({ type: "folder.updateMeta", name: val });
    } else {
      els.titleInput.value = current;
    }
  }

  els.titleInput.addEventListener("focus", () => {
    if (state.pinMode === 0) post({ type: "folder.pin" });
    els.titleInput.select();
  });

  els.titleInput.addEventListener("blur", commitTitle);
  els.titleInput.addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      els.titleInput.blur();
    }
  });

  els.btnAddFiles.addEventListener("click", (e) => {
    e.stopPropagation();
    if (state.pinMode === 0) post({ type: "folder.pin" });
    post({ type: "folder.addFilesDialog" });
  });

  els.btnSettings.addEventListener("click", (e) => {
    e.stopPropagation();
    const opening = els.settingsDrawer.hidden;
    els.settingsDrawer.hidden = !opening;
    els.btnSettings.classList.toggle("active", opening);
    els.deleteConfirm.hidden = true;
    if (opening && state.pinMode === 0) {
      post({ type: "folder.pin" });
    }
  });

  els.accentSwatches.addEventListener("click", (e) => {
    const btn = e.target.closest(".swatch");
    if (!btn) return;
    const color = btn.dataset.color;
    state.folder.accentColor = color;
    applyTheme();
    post({ type: "folder.updateMeta", accentColor: color });
  });

  document.querySelectorAll(".seg-btn").forEach((btn) => {
    btn.addEventListener("click", () => {
      const grid = Number(btn.dataset.grid);
      state.folder.previewGrid = grid;
      applyTheme();
      renderCollapsed();
      post({ type: "folder.updateMeta", previewGrid: grid });
    });
  });

  els.btnPickCustomIcon.addEventListener("click", (e) => {
    e.stopPropagation();
    if (state.pinMode === 0) post({ type: "folder.pin" });
    post({ type: "folder.pickCustomIcon" });
  });

  els.btnClearCustomIcon.addEventListener("click", (e) => {
    e.stopPropagation();
    post({ type: "folder.clearCustomIcon" });
  });

  function pushGlobalSettings(immediate = false) {
    const animationDurationMs = Number(els.animDurationRange.value) || 400;
    const hoverDelayMs = Number(els.hoverDelayRange.value) || 400;
    state.settings.animationDurationMs = animationDurationMs;
    state.settings.hoverDelayMs = hoverDelayMs;
    applyTheme();

    clearTimeout(globalSettingsTimer);
    const send = () => {
      post({
        type: "folder.updateGlobalSettings",
        animationDurationMs,
        hoverDelayMs,
      });
    };

    if (immediate) {
      send();
    } else {
      globalSettingsTimer = setTimeout(send, 80);
    }
  }

  els.animDurationRange.addEventListener("input", () => pushGlobalSettings(false));
  els.animDurationRange.addEventListener("change", () => pushGlobalSettings(true));
  els.hoverDelayRange.addEventListener("input", () => pushGlobalSettings(false));
  els.hoverDelayRange.addEventListener("change", () => pushGlobalSettings(true));

  els.btnOpenExplorer.addEventListener("click", () => {
    post({ type: "folder.openInExplorer" });
  });

  els.btnDeleteFolder.addEventListener("click", () => {
    if (state.items.length === 0) {
      post({ type: "folder.deleteFolder", restoreItems: false });
    } else {
      els.deleteConfirm.hidden = false;
    }
  });

  els.btnDeleteRestore.addEventListener("click", () => {
    post({ type: "folder.deleteFolder", restoreItems: true });
  });

  els.btnDeleteWipe.addEventListener("click", () => {
    post({ type: "folder.deleteFolder", restoreItems: false });
  });

  els.btnDeleteCancel.addEventListener("click", () => {
    els.deleteConfirm.hidden = true;
  });

  els.searchInput.addEventListener("input", () => {
    state.searchQuery = els.searchInput.value;
    renderExpanded();
  });

  // =========================================================================
  // EXTERNAL FILE DRAG & DROP (WebView2 postMessageWithAdditionalObjects)
  // =========================================================================
  function isExternalFileDrag(e) {
    if (state.dragInternalId) return false;
    const types = e.dataTransfer?.types;
    return types && Array.from(types).includes("Files");
  }

  function setDropHighlight(active) {
    els.folderBox.classList.toggle("drag-over", active && !state.expanded);
    els.dropOverlayExpanded.hidden = !(active && state.expanded);
  }

  window.addEventListener("dragenter", (e) => {
    if (!isExternalFileDrag(e)) return;
    e.preventDefault();
    externalDragDepth++;
    setDropHighlight(true);
  });

  window.addEventListener("dragover", (e) => {
    if (!isExternalFileDrag(e)) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = "move";
    setDropHighlight(true);
  });

  window.addEventListener("dragleave", (e) => {
    if (!isExternalFileDrag(e)) return;
    externalDragDepth = Math.max(0, externalDragDepth - 1);
    if (externalDragDepth === 0) {
      setDropHighlight(false);
    }
  });

  window.addEventListener("drop", (e) => {
    if (!isExternalFileDrag(e)) return;
    e.preventDefault();
    externalDragDepth = 0;
    setDropHighlight(false);

    const files = Array.from(e.dataTransfer?.files || []);
    if (files.length === 0) return;

    if (window.chrome?.webview?.postMessageWithAdditionalObjects) {
      window.chrome.webview.postMessageWithAdditionalObjects(
        { type: "folder.dropFiles" },
        files
      );
    }
  });

  // =========================================================================
  // HOST MESSAGES
  // =========================================================================
  window.chrome?.webview?.addEventListener("message", (event) => {
    const msg = event.data;
    if (!msg || typeof msg !== "object") return;

    switch (msg.type) {
      case "host.data":
        if (typeof msg.locale === "string") {
          state.locale = msg.locale.toLowerCase().startsWith("tr") ? "tr" : "en";
        }
        if (msg.folder) state.folder = msg.folder;
        if (msg.settings) state.settings = msg.settings;
        if (Array.isArray(msg.items)) state.items = msg.items;
        applyLocale();
        applyTheme();
        renderCollapsed();
        renderExpanded();
        break;

      case "host.state": {
        if (msg.morphOrigin) {
          document.documentElement.style.setProperty("--morph-origin", msg.morphOrigin);
        }
        const nextPinMode =
          typeof msg.pinMode === "number" ? msg.pinMode : msg.pinned ? 1 : 0;
        state.pinMode = nextPinMode;
        state.pinned = nextPinMode > 0;
        updatePinUI();

        if (msg.expanded) {
          clearTimeout(hoverCloseTimer);
          clearPendingCollapse();
          clearPendingFadeOut();

          const wasExpanded = state.expanded;
          state.expanded = true;
          els.expandedView.setAttribute("aria-hidden", "false");

          if (pendingOpenSettings) {
            pendingOpenSettings = false;
            els.settingsDrawer.hidden = false;
            els.btnSettings.classList.add("active");
          }

          if (!wasExpanded) {
            els.app.dataset.state = "preparing";
            els.expandedView.inert = true;
            void els.expandedView.offsetWidth;
            requestAnimationFrame(() => {
              if (!state.expanded) return;
              requestAnimationFrame(() => {
                if (!state.expanded) return;
                els.app.dataset.state = "expanded";
                els.expandedView.inert = false;
                if (msg.focusTitle) {
                  window.focus();
                  els.titleInput.focus();
                  els.titleInput.select();
                }
              });
            });
          } else {
            els.app.dataset.state = "expanded";
            els.expandedView.inert = false;
            if (msg.focusTitle) {
              requestAnimationFrame(() => {
                window.focus();
                els.titleInput.focus();
                els.titleInput.select();
              });
            }
          }
        } else {
          cancelExpandSequence();
          state.expanded = false;
          clearTileDrag();
          els.settingsDrawer.hidden = true;
          els.btnSettings.classList.remove("active");
          els.deleteConfirm.hidden = true;
          els.app.dataset.state = "collapsing";
          els.expandedView.inert = true;
          els.expandedView.setAttribute("aria-hidden", "true");
          const animMs = Number(state.settings.animationDurationMs) || 400;
          const collapseMs = Math.round(animMs * 0.8);
          clearPendingCollapse();
          collapseTimer = setTimeout(() => {
            collapseTimer = null;
            if (state.expanded) return;
            collapseRaf1 = requestAnimationFrame(() => {
              collapseRaf1 = 0;
              if (state.expanded) return;
              collapseRaf2 = requestAnimationFrame(() => {
                collapseRaf2 = 0;
                if (!state.expanded) {
                  post({ type: "folder.collapsed" });
                }
              });
            });
          }, collapseMs);
        }
        break;
      }

      case "host.pinState": {
        const nextPinMode =
          typeof msg.pinMode === "number" ? msg.pinMode : msg.pinned ? 1 : 0;
        state.pinMode = nextPinMode;
        state.pinned = nextPinMode > 0;
        updatePinUI();
        break;
      }

      case "host.focusTitle":
        if (state.expanded) {
          els.expandedView.inert = false;
          window.focus();
          els.titleInput.focus();
          els.titleInput.select();
        }
        break;

      case "host.collapsedReady":
        clearTimeout(hoverOpenTimer);
        clearTimeout(hoverCloseTimer);
        clearPendingCollapse();
        cancelExpandSequence();
        state.expanded = false;
        state.pinned = false;
        state.pinMode = 0;
        els.expandedView.inert = true;
        els.expandedView.setAttribute("aria-hidden", "true");
        els.app.dataset.state = "collapsed";
        updatePinUI();
        break;

      case "host.cancelHover":
        clearTimeout(hoverOpenTimer);
        cancelExpandSequence();
        break;

      case "host.dragOverTrash":
        els.folderBox.classList.toggle("over-trash", Boolean(msg.overTrash));
        break;

      case "host.dragEnded":
        state.draggingWindow = false;
        els.folderBox.classList.remove("over-trash");
        break;

      case "host.dropResult":
        if (msg.movedCount > 0) {
          showStatus(t("movedToFolder", msg.movedCount), true);
        }
        break;
    }
  });

  // Handshake
  applyLocale();
  post({ type: "folder.ready" });
})();
