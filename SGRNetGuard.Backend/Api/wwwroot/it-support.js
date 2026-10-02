(() => {
  const apiPath = "/api/admin/it-support";
  const regionOrder = { VMB: 0, VMT: 1, VMN: 2 };
  const status = document.getElementById("supportStatus");
  const rows = document.getElementById("supportRows");
  const dialog = document.getElementById("supportDialog");
  const form = document.getElementById("supportForm");
  const formError = document.getElementById("formError");
  const contactsById = new Map();

  const fields = {
    id: document.getElementById("supportId"),
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

  function render(contacts) {
    contactsById.clear();
    const sorted = [...contacts].sort((left, right) =>
      (regionOrder[left.region] ?? 99) - (regionOrder[right.region] ?? 99) ||
      left.sortOrder - right.sortOrder || left.id - right.id);

    for (const contact of sorted) contactsById.set(String(contact.id), contact);
    if (sorted.length === 0) {
      rows.innerHTML = '<tr><td colspan="7" class="support-empty">Chưa có thông tin IT Support.</td></tr>';
      return;
    }

    rows.innerHTML = sorted.map(contact => {
      const teamsUrl = safeHttpUrl(contact.teamsUrl);
      const teams = teamsUrl
        ? `<a class="support-link" href="${escapeHtml(teamsUrl)}" target="_blank" rel="noopener noreferrer">Mở Teams</a>`
        : '<span class="support-muted">Chưa cấu hình</span>';
      const stateClass = contact.isActive ? "support-state-active" : "support-state-inactive";
      const stateText = contact.isActive ? "Đang dùng" : "Đã ngừng";
      const statusAction = contact.isActive
        ? `<button type="button" class="secondary-button" data-action="deactivate" data-id="${contact.id}">Ngừng</button>`
        : `<button type="button" class="secondary-button" data-action="activate" data-id="${contact.id}">Kích hoạt</button>`;

      return `<tr>
        <td><strong>${escapeHtml(contact.region)}</strong></td>
        <td><span class="support-name">${escapeHtml(contact.displayName)}</span><br><span class="support-muted">${escapeHtml(contact.username || "")}</span></td>
        <td>${contact.email ? `<a class="support-link" href="mailto:${escapeHtml(contact.email)}">${escapeHtml(contact.email)}</a>` : '<span class="support-muted">-</span>'}</td>
        <td>${teams}</td>
        <td>${escapeHtml(contact.phone || "-")}</td>
        <td><span class="support-state ${stateClass}">${stateText}</span></td>
        <td><div class="support-row-actions"><button type="button" class="secondary-button" data-action="edit" data-id="${contact.id}">Sửa</button>${statusAction}</div></td>
      </tr>`;
    }).join("");
  }

  async function loadContacts() {
    status.textContent = "Đang tải dữ liệu...";
    try {
      const contacts = await request(apiPath);
      render(Array.isArray(contacts) ? contacts : []);
      status.textContent = `${contacts.length} liên hệ`;
    } catch (error) {
      rows.innerHTML = `<tr><td colspan="7" class="support-empty">${escapeHtml(error.message)}</td></tr>`;
      status.textContent = "Không tải được dữ liệu";
    }
  }

  function openDialog(contact = null) {
    form.reset();
    formError.textContent = "";
    document.getElementById("dialogTitle").textContent = contact ? "Sửa IT Support" : "Thêm IT Support";
    fields.id.value = contact?.id ?? "";
    fields.region.value = contact?.region ?? "";
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
    const contact = contactsById.get(button.dataset.id);
    if (!contact) return;

    if (button.dataset.action === "edit") {
      openDialog(contact);
      return;
    }

    try {
      if (button.dataset.action === "deactivate") {
        if (!window.confirm(`Ngừng sử dụng IT Support ${contact.displayName}?`)) return;
        await request(`${apiPath}/${contact.id}`, { method: "DELETE" });
      } else {
        await request(`${apiPath}/${contact.id}`, {
          method: "PUT",
          body: JSON.stringify({ ...contact, isActive: true })
        });
      }
      await loadContacts();
    } catch (error) {
      status.textContent = error.message;
    }
  });

  loadContacts();
})();