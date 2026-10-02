(() => {
  const apiPath = "/api/admin/it-support";
  const status = document.getElementById("supportStatus");
  const rows = document.getElementById("supportRows");
  const dialog = document.getElementById("supportDialog");
  const form = document.getElementById("supportForm");
  const formError = document.getElementById("formError");
  const contactsBySite = new Map();
  const siteMap = new Map();
  let sites = [];

  const fields = {
    id: document.getElementById("supportId"),
    site: document.getElementById("siteInput"),
    region: document.getElementById("regionInput"),
    displayName: document.getElementById("displayNameInput"),
    username: document.getElementById("usernameInput"),
    email: document.getElementById("emailInput"),
    teamsUrl: document.getElementById("teamsUrlInput"),
    phone: document.getElementById("phoneInput"),
    sortOrder: document.getElementById("sortOrderInput"),
    isActive: document.getElementById("activeInput")
  };

  function escapeHtml(value) {
    return String(value ?? "").replace(/[&<>"']/g, character => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    })[character]);
  }

  function safeHttpUrl(value) {
    try {
      const url = new URL(value);
      return url.protocol === "https:" || url.protocol === "http:" ? url.href : null;
    } catch {
      return null;
    }
  }

  function siteKey(value) {
    return String(value ?? "").trim().toLocaleLowerCase();
  }

  async function request(url, options = {}) {
    const response = await fetch(url, {
      credentials: "same-origin",
      ...options,
      headers: { "Content-Type": "application/json", ...(options.headers || {}) }
    });
    if (response.status === 401) {
      window.location.assign("/login");
      throw new Error("Phiên đăng nhập đã hết hạn.");
    }
    if (!response.ok) {
      const result = await response.json().catch(() => null);
      throw new Error(result?.message || `Yêu cầu thất bại (${response.status}).`);
    }
    return response.status === 204 ? null : response.json();
  }

  function populateSiteOptions() {
    siteMap.clear();
    fields.site.innerHTML = '<option value="">Chọn Site</option>' + sites.map(site => {
      siteMap.set(siteKey(site.site), site);
      return `<option value="${escapeHtml(site.site)}">${escapeHtml(site.site)} (${escapeHtml(site.region)})</option>`;
    }).join("");
  }

  function render(contacts) {
    contactsBySite.clear();
    for (const contact of [...contacts].sort((left, right) =>
      Number(right.isActive) - Number(left.isActive) ||
      Date.parse(right.updatedAt || "") - Date.parse(left.updatedAt || "") ||
      right.id - left.id)) {
      const key = siteKey(contact.site);
      if (key && !contactsBySite.has(key)) contactsBySite.set(key, contact);
    }

    if (sites.length === 0) {
      rows.innerHTML = '<tr><td colspan="8" class="support-empty">Chưa có Site đang hoạt động.</td></tr>';
      return;
    }

    rows.innerHTML = sites.map(site => {
      const contact = contactsBySite.get(siteKey(site.site));
      const teamsUrl = safeHttpUrl(contact?.teamsUrl);
      const teams = teamsUrl
        ? `<a class="support-link" href="${escapeHtml(teamsUrl)}" target="_blank" rel="noopener noreferrer">Mở Teams</a>`
        : '<span class="support-muted">Chưa cấu hình</span>';
      const stateClass = contact?.isActive ? "support-state-active" : "support-state-inactive";
      const stateText = contact?.isActive ? "Đang dùng" : contact ? "Đã ngừng" : "Chưa cấu hình";
      const actionButtons = contact
        ? `<button type="button" class="secondary-button" data-action="edit" data-site="${escapeHtml(site.site)}">Sửa</button>${contact.isActive
          ? `<button type="button" class="secondary-button" data-action="deactivate" data-site="${escapeHtml(site.site)}">Ngừng</button>`
          : `<button type="button" class="secondary-button" data-action="activate" data-site="${escapeHtml(site.site)}">Kích hoạt</button>`}`
        : `<button type="button" class="secondary-button" data-action="add" data-site="${escapeHtml(site.site)}">Thêm</button>`;

      return `<tr>
        <td><strong>${escapeHtml(site.site)}</strong></td>
        <td>${escapeHtml(site.region)}</td>
        <td>${contact ? `<span class="support-name">${escapeHtml(contact.displayName)}</span><br><span class="support-muted">${escapeHtml(contact.username || "")}</span>` : '<span class="support-muted">-</span>'}</td>
        <td>${contact?.email ? `<a class="support-link" href="mailto:${escapeHtml(contact.email)}">${escapeHtml(contact.email)}</a>` : '<span class="support-muted">-</span>'}</td>
        <td>${teams}</td>
        <td>${escapeHtml(contact?.phone || "-")}</td>
        <td><span class="support-state ${stateClass}">${stateText}</span></td>
        <td><div class="support-row-actions">${actionButtons}</div></td>
      </tr>`;
    }).join("");
  }

  async function loadContacts() {
    status.textContent = "Đang tải dữ liệu...";
    try {
      const [contacts, config] = await Promise.all([request(apiPath), request("/api/config")]);
      const configuredSites = Array.isArray(config.sites) ? config.sites : [];
      const uniqueSites = new Map();
      for (const site of configuredSites) {
        if (site.site && site.region && !uniqueSites.has(siteKey(site.site)))
          uniqueSites.set(siteKey(site.site), site);
      }
      sites = [...uniqueSites.values()];
      populateSiteOptions();
      render(Array.isArray(contacts) ? contacts : []);
      status.textContent = `${sites.length} Site`;
    } catch (error) {
      rows.innerHTML = `<tr><td colspan="8" class="support-empty">${escapeHtml(error.message)}</td></tr>`;
      status.textContent = "Không tải được dữ liệu";
    }
  }

  function openDialog(site = null, contact = null) {
    form.reset();
    formError.textContent = "";
    document.getElementById("dialogTitle").textContent = contact ? "Sửa IT Support" : "Thêm IT Support";
    fields.id.value = contact?.id ?? "";
    fields.site.value = contact?.site ?? site?.site ?? "";
    updateRegionFromSite();
    fields.displayName.value = contact?.displayName ?? "";
    fields.username.value = contact?.username ?? "";
    fields.email.value = contact?.email ?? "";
    fields.teamsUrl.value = contact?.teamsUrl ?? "";
    fields.phone.value = contact?.phone ?? "";
    fields.sortOrder.value = contact?.sortOrder ?? 0;
    fields.isActive.checked = contact?.isActive ?? true;
    dialog.showModal();
    fields.region.focus();
  }

  function readForm() {
    return {
      site: fields.site.value,
      region: fields.region.value,
      displayName: fields.displayName.value.trim(),
      username: fields.username.value.trim() || null,
      email: fields.email.value.trim() || null,
      teamsUrl: fields.teamsUrl.value.trim() || null,
      phone: fields.phone.value.trim() || null,
      sortOrder: Number(fields.sortOrder.value),
      isActive: fields.isActive.checked
    };
  }

  function updateRegionFromSite() {
    fields.region.value = siteMap.get(siteKey(fields.site.value))?.region ?? "";
  }

  document.getElementById("addSupportButton").addEventListener("click", () => openDialog());
  fields.site.addEventListener("change", updateRegionFromSite);
  document.getElementById("closeDialogButton").addEventListener("click", () => dialog.close());
  document.getElementById("cancelDialogButton").addEventListener("click", () => dialog.close());

  form.addEventListener("submit", async event => {
    event.preventDefault();
    formError.textContent = "";
    if (!form.reportValidity()) return;

    const id = fields.id.value;
    try {
      await request(id ? `${apiPath}/${encodeURIComponent(id)}` : apiPath, {
        method: id ? "PUT" : "POST",
        body: JSON.stringify(readForm())
      });
      dialog.close();
      await loadContacts();
    } catch (error) {
      formError.textContent = error.message;
    }
  });

  rows.addEventListener("click", async event => {
    const button = event.target.closest("button[data-action]");
    if (!button) return;
    const site = siteMap.get(siteKey(button.dataset.site));
    if (!site) return;
    const contact = contactsBySite.get(siteKey(site.site));

    if (button.dataset.action === "edit") {
      openDialog(site, contact);
      return;
    }

    if (button.dataset.action === "add") {
      openDialog(site);
      return;
    }

    try {
      if (button.dataset.action === "deactivate") {
        if (!contact || !window.confirm(`Ngừng IT Support của ${site.site}?`)) return;
        await request(`${apiPath}/${contact.id}`, { method: "DELETE" });
      } else {
        if (!contact) return;
        await request(`${apiPath}/${contact.id}`, {
          method: "PUT",
          body: JSON.stringify({ ...contact, site: site.site, region: site.region, isActive: true })
        });
      }
      await loadContacts();
    } catch (error) {
      status.textContent = error.message;
    }
  });

  loadContacts();
})();