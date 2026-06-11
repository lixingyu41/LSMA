(async () => {
  if (document.body.dataset.page !== "download") {
    return;
  }

  const manifestUrl = `/download/manifest.json?t=${Date.now()}`;
  try {
    const response = await fetch(manifestUrl, { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    const manifest = await response.json();
    applyManifest(manifest);
  } catch (error) {
    console.warn("Unable to load LSMA release manifest.", error);
  }

  function applyManifest(manifest) {
    setText("latestVersion", manifest.latestVersion || manifest.version || "1.0.3");
    setText("releaseDate", manifest.releaseDate || "2026-06-11");
    setText("sizeBytes", formatBytes(manifest.sizeBytes));
    setText("fileName", manifest.fileName || "LSMA-Setup-x64.exe");
    setText("sha256", manifest.sha256 || "");

    const button = document.getElementById("downloadButton");
    if (button && manifest.downloadUrl) {
      button.href = manifest.downloadUrl;
      button.textContent = `下载 ${manifest.fileName || "LSMA-Setup-x64.exe"}`;
    }

    const notes = document.getElementById("releaseNotes");
    if (notes && Array.isArray(manifest.notes) && manifest.notes.length > 0) {
      notes.replaceChildren(...manifest.notes.map((note) => {
        const item = document.createElement("li");
        item.textContent = note;
        return item;
      }));
    }
  }

  function setText(field, value) {
    document.querySelectorAll(`[data-field="${field}"]`).forEach((element) => {
      element.textContent = value || "-";
    });
  }

  function formatBytes(value) {
    const bytes = Number(value);
    if (!Number.isFinite(bytes) || bytes <= 0) {
      return "-";
    }

    const mib = bytes / 1024 / 1024;
    return `${mib.toFixed(1)} MiB`;
  }
})();
