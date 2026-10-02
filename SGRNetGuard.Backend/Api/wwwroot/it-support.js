(() => {
  const apiPath = "/api/admin/it-support";
  const regions = ["VMB", "VMT", "VMN"];
  const status = document.getElementById("supportStatus");
  const rows = document.getElementById("supportRows");
  const dialog = document.getElementById("supportDialog");
  const form = document.getElementById("supportForm");
  const formError = document.getElementById("formError");
  const siteField = document.getElementById("siteField");
  const contactsByKey = new Map();
  const siteMap = new Map();
  let sites = [];

  const fields = {
    id: document.getElementById("supportId"),
    type: document.getElementById("supportType"),
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

  function mappingKey(contact) {
    return contact.site
      ? `site:${siteKey(contact.site)}`
      : `region:${String(contact.region || "").trim().toUpperCase()}`;
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
    contactsByKey.clear();
    for (const contact of [...contacts].sort((left, right) =>
      Number(right.isActive) - Number(left.isActive) ||
      Date.parse(right.updatedAt || "") - Date.parse(left.updatedAt || "") ||
      right.id - left.id)) {
      const key = mappingKey(contact);
      if (!contactsByKey.has(key)) contactsByKey.set(key, contact);
    }

    if (sites.length === 0 && regions.length === 0) {
      rows.innerHTML = '<tr><td colspan="9" class="support-empty">Chưa có Region hoặc Site.</td></tr>';
      return;
    }

    const mappings = [
      ...regions.map(region => ({ region, site: null, type: "Region" })),
      ...sites.map(site => ({ region: site.region, site: site.site, type: "Site" }))
    ];

    rows.innerHTML = mappings.map(mapping => {
      const key = mapping.site
        ? `site:${siteKey(mapping.site)}`
        : `region:${mapping.region}`;
      const contact = contactsByKey.get(key);
      const teamsUrl = safeHttpUrl(contact?.teamsUrl);
      const teams = teamsUrl
        ? `<a class="support-link" href="${escapeHtml(teamsUrl)}" target="_blank" rel="noopener noreferrer">Mở Teams</a>`
        : '<span class="support-muted">Chưa cấu hình</span>';
      const stateClass = contact?.isActive ? "support-state-active" : "support-state-inactive";
      const stateText = contact?.isActive ? "Đang dùng" : contact ? "Đã ngừng" : "Chưa cấu hình";
      const scope = mapping.site ? "site" : "region";
      const escapedRegion = escapeHtml(mapping.region);
      const escapedSite = escapeHtml(mapping.site || "");
      const actionButtons = contact
        ? `<button type="button" class="secondary-button" data-action="edit" data-scope="${scope}" data-region="${escapedRegion}" data-site="${escapedSite}">Sửa</button>${contact.isActive
          ? `<button type="button" class="secondary-button" data-action="deactivate" data-scope="${scope}" data-region="${escapedRegion}" data-site="${escapedSite}">Ngừng</button>`
          : `<button type="button" class="secondary-button" data-action="activate" data-scope="${scope}" data-region="${escapedRegion}" data-site="${escapedSite}">Kích hoạt</button>`}`
        : `<button type="button" class="secondary-button" data-action="add" data-scope="${scope}" data-region="${escapedRegion}" data-site="${escapedSite}">Thêm</button>`;

      return `<tr>
        <td>${mapping.type}</td>
        <td><strong>${escapedRegion}</strong></td>
        <td>${mapping.site ? `<strong>${escapedSite}</strong>` : '<span class="support-muted">-</span>'}</td>
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
      status.textContent = `${regions.length} Region defaults · ${sites.length} Site contacts`;
    } catch (error) {
      rows.innerHTML = `<tr><td colspan="9" class="support-empty">${escapeHtml(error.message)}</td></tr>`;
      status.textContent = "Không tải được dữ liệu";
    }
  }

  function updateMappingFields() {
    const isSite = fields.type.value === "site";
    siteField.hidden = !isSite;
    fields.site.required = isSite;
    fields.region.disabled = isSite;
    if (isSite)
      fields.region.value = siteMap.get(siteKey(fields.site.value))?.region ?? "";
  }

  function openDialog(mapping = null, contact = null) {
    form.reset();
    formError.textContent = "";
    document.getElementById("dialogTitle").textContent = contact ? "Sửa IT Support" : "Thêm IT Support";
    fields.id.value = contact?.id ?? "";
    fields.type.value = (contact?.site ?? mapping?.site) ? "site" : "region";
    fields.site.value = contact?.site ?? mapping?.site ?? "";
    fields.region.value = contact?.region ?? mapping?.region ?? "";
    updateMappingFields();
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

  document.getElementById("addSupportButton").addEventListener("click", () => openDialog());
  fields.type.addEventListener("change", updateMappingFields);
  fields.site.addEventListener("change", updateMappingFields);
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
    const isSite = button.dataset.scope === "site";
    const site = isSite ? siteMap.get(siteKey(button.dataset.site)) : null;
    const region = button.dataset.region;
    if (isSite && !site) return;
    const mapping = { site: site?.site ?? null, region: site?.region ?? region };
    const contact = contactsByKey.get(isSite
      ? `site:${siteKey(mapping.site)}`
      : `region:${String(mapping.region).toUpperCase()}`);

    if (button.dataset.action === "edit") {
      openDialog(mapping, contact);
      return;
    }

    if (button.dataset.action === "add") {
      openDialog(mapping);
      return;
    }

    try {
      if (button.dataset.action === "deactivate") {
        const label = mapping.site || mapping.region;
        if (!contact || !window.confirm(`Ngừng IT Support của ${label}?`)) return;
        await request(`${apiPath}/${contact.id}`, { method: "DELETE" });
      } else {
        if (!contact) return;
        await request(`${apiPath}/${contact.id}`, {
          method: "PUT",
          body: JSON.stringify({ ...contact, ...mapping, isActive: true })
        });
      }
      await loadContacts();
    } catch (error) {
      status.textContent = error.message;
    }
  });

  loadContacts();
})();