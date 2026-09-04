// ============================================================
// SGR NetGuard - IT Dashboard logic
// ============================================================

const API_BASE = ""; // cùng origin với trang này (API và Dashboard chung 1 site IIS)

let allDevices = [];
let isDemoMode = false;
const selectedDeviceNames = new Set();
let lastFilteredDeviceNames = [];
let dashboardSummary = null;
let summaryFilter = "all";

const demoDevices = [
  {
    deviceName: "HNI-SCG-WS01",
    lastSiteName: "Sun City Hà Nội",
    lastRegion: "VMB",
    isInternal: true,
    cpuPercent: 21,
    ramPercent: 48,
    diskPercent: 63,
    adJoined: true,
    trellixInstalled: true,
    desktopCentralInstalled: true,
    warningsToday: 0,
    lastSeenUtc: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
    isOnline: true
  },
  {
    deviceName: "THO-HRM-LT02",
    lastSiteName: "VP Thành Phố Thanh Hóa",
    lastRegion: "VMB",
    isInternal: true,
    cpuPercent: 83,
    ramPercent: 72,
    diskPercent: 68,
    adJoined: true,
    trellixInstalled: false,
    desktopCentralInstalled: true,
    warningsToday: 3,
    lastSeenUtc: new Date(Date.now() - 2 * 60 * 1000).toISOString(),
    isOnline: true
  },
  {
    deviceName: "YEN-LUYN-PC03",
    lastSiteName: "PMU Lương Yên",
    lastRegion: "VMB",
    isInternal: true,
    cpuPercent: 45,
    ramPercent: 61,
    diskPercent: 72,
    adJoined: true,
    trellixInstalled: true,
    desktopCentralInstalled: false,
    warningsToday: 2,
    lastSeenUtc: new Date(Date.now() - 9 * 60 * 1000).toISOString(),
    isOnline: true
  },
  {
    deviceName: "BDS-DNGL-IT04",
    lastSiteName: "Văn Phòng BDS Đông Bắc",
    lastRegion: "VMB",
    isInternal: true,
    cpuPercent: 78,
    ramPercent: 55,
    diskPercent: 49,
    adJoined: true,
    trellixInstalled: false,
    desktopCentralInstalled: true,
    warningsToday: 1,
    lastSeenUtc: new Date(Date.now() - 18 * 60 * 1000).toISOString(),
    isOnline: true
  },
  {
    deviceName: "SGN-EXT-ADMIN03",
    lastSiteName: "",
    lastRegion: "VMN",
    isInternal: false,
    cpuPercent: 12,
    ramPercent: 34,
    diskPercent: 41,
    adJoined: false,
    trellixInstalled: false,
    desktopCentralInstalled: false,
    warningsToday: 1,
    lastSeenUtc: new Date(Date.now() - 18 * 60 * 1000).toISOString(),
    isOnline: false
  }
];

function setDemoBanner(visible) {
  const banner = document.getElementById("demoBanner");
  const button = document.getElementById("loadDemoBtn");
  if (banner) banner.hidden = true;
  if (button) button.hidden = true;
}

function loadDemoData() {
  isDemoMode = true;
  allDevices = demoDevices;
  renderTable("Dữ liệu demo đang được hiển thị.");
  renderStats();
  setConnStatus(false);
  setDemoBanner(true);
}

async function loadDatabaseHealth() {
  const banner = document.getElementById("dbBanner");
  if (banner) {
    banner.hidden = true;
  }
  return;
}

// ---------------- Load danh sách máy ----------------

async function loadDevices() {
  try {
    const res = await fetch(`${API_BASE}/api/devices`);
    if (res.status === 401) {
      // Nếu chưa đăng nhập, chuyển sang trang login để người dùng nhập thông tin
      window.location.href = '/login';
      return;
    }
    if (!res.ok) {
      const payload = await res.json().catch(() => null);
      allDevices = [];
      isDemoMode = false;
      renderTable(payload?.message || "Database chưa sẵn sàng. Bấm 'Load demo data' để xem dữ liệu mẫu.");
      renderStats();
      setConnStatus(false);
      setDemoBanner(true);
      return;
    }
    const data = await res.json();
    if (!Array.isArray(data) || data.length === 0) {
      allDevices = [];
      isDemoMode = false;
      renderTable("Chưa có máy nào trong database. Bấm 'Load demo data' để xem dữ liệu mẫu.");
      renderStats();
      setConnStatus(false);
      setDemoBanner(true);
      return;
    }

    allDevices = data;
    const currentNames = new Set(allDevices.map(d => d.deviceName));
    for (const selectedName of Array.from(selectedDeviceNames)) {
      if (!currentNames.has(selectedName)) {
        selectedDeviceNames.delete(selectedName);
      }
    }
    isDemoMode = false;
    renderTable();
    renderStats();
    renderNetworkDashboard();
    setDemoBanner(false);
  } catch (err) {
    console.error("Không tải được danh sách máy:", err);
    allDevices = [];
    isDemoMode = false;
    renderTable("Không tải được dữ liệu dashboard. Bấm 'Load demo data' để xem dữ liệu mẫu.");
    renderStats();
    setConnStatus(false);
    setDemoBanner(true);
  }
}

async function loadDashboardSummary() {
  try {
    const response = await fetch(`${API_BASE}/api/dashboard/summary`);
    if (response.status === 401) {
      window.location.href = "/login";
      return;
    }
    if (!response.ok) throw new Error(`Summary API returned ${response.status}`);
    dashboardSummary = await response.json();
    renderNetworkDashboard();
  } catch (error) {
    console.error("Không tải được tổng quan dashboard:", error);
    dashboardSummary = null;
    renderNetworkDashboard();
  }
}

function setSummaryFilter(filter) {
  summaryFilter = filter;
  document.getElementById("searchBox").value = "";
  document.getElementById("regionFilter").value = ["VMB", "VMT", "VMN"].includes(filter) ? filter : "";
  document.getElementById("statusFilter").value = "";
  renderTable();
  renderNetworkDashboard();
  document.getElementById("deviceTable").scrollIntoView({ behavior: "smooth", block: "start" });
}

function renderNetworkDashboard() {
  const fallback = {
    totalComputers: allDevices.length,
    regions: {
      VMB: allDevices.filter(device => device.lastRegion === "VMB").length,
      VMT: allDevices.filter(device => device.lastRegion === "VMT").length,
      VMN: allDevices.filter(device => device.lastRegion === "VMN").length
    },
    compliant: allDevices.filter(device => !isNonCompliant(device)).length,
    nonCompliant: allDevices.filter(isNonCompliant).length,
    internalNetwork: allDevices.filter(device => device.isInternal).length,
    externalNetwork: allDevices.filter(device => !device.isInternal).length
  };
  const summary = dashboardSummary || fallback;
  const regions = summary.regions || {};
  const total = Number(summary.totalComputers || 0);
  const values = {
    VMB: Number(regions.VMB ?? regions.vmb ?? 0),
    VMT: Number(regions.VMT ?? regions.vmt ?? 0),
    VMN: Number(regions.VMN ?? regions.vmn ?? 0)
  };

  [
    ["summaryTotal", total], ["cardTotal", total], ["summaryVmb", values.VMB], ["cardVmb", values.VMB],
    ["summaryVmt", values.VMT], ["cardVmt", values.VMT], ["summaryVmn", values.VMN], ["cardVmn", values.VMN],
    ["cardCompliant", Number(summary.compliant || 0)], ["cardNonCompliant", Number(summary.nonCompliant || 0)],
    ["cardInternal", Number(summary.internalNetwork || 0)], ["cardExternal", Number(summary.externalNetwork || 0)]
  ].forEach(([id, value]) => {
    const element = document.getElementById(id);
    if (element) element.textContent = value;
  });

  const circumference = 2 * Math.PI * 82;
  let offset = 0;
  ["VMB", "VMT", "VMN"].forEach(region => {
    const segment = document.querySelector(`.donut-${region.toLowerCase()}`);
    if (!segment) return;
    const length = total > 0 ? (values[region] / total) * circumference : 0;
    segment.style.strokeDasharray = `${length} ${circumference - length}`;
    segment.style.strokeDashoffset = `${-offset}`;
    offset += length;
    segment.classList.toggle("is-selected", summaryFilter === region);
  });

  document.querySelectorAll("[data-summary-filter]").forEach(element => {
    element.classList.toggle("is-selected", element.dataset.summaryFilter === summaryFilter);
  });
}

function renderStats() {
  const total = allDevices.length;
  const online = allDevices.filter(d => d.isOnline).length;
  const warnToday = allDevices.reduce((sum, d) => sum + (d.warningsToday || 0), 0);
  const nonCompliant = allDevices.filter(isNonCompliant).length;
  const external = allDevices.filter(d => d.externalNetworkStatus === "External" || d.isInternal === false).length;

  document.getElementById("statTotal").textContent = total;
  document.getElementById("statOnline").textContent = online;
  document.getElementById("statWarnToday").textContent = warnToday;
  document.getElementById("statNonCompliant").textContent = nonCompliant;
  document.getElementById("statExternal").textContent = external;
}

function openTodayWarningsModal() {
  const modal = document.getElementById("todayWarningsModal");
  if (!modal) return;
  modal.classList.remove("hidden");
  modal.setAttribute("aria-hidden", "false");
  loadTodayWarnings();
}

function closeTodayWarningsModal() {
  const modal = document.getElementById("todayWarningsModal");
  if (!modal) return;
  modal.classList.add("hidden");
  modal.setAttribute("aria-hidden", "true");
}

async function loadTodayWarnings() {
  const content = document.getElementById("todayWarningsContent");
  if (!content) return;
  content.textContent = "Đang tải cảnh báo...";

  try {
    const response = await fetch(`${API_BASE}/api/alerts/today`);
    if (!response.ok) throw new Error("Không tải được cảnh báo");
    const warnings = await response.json();
    if (!Array.isArray(warnings) || warnings.length === 0) {
      content.innerHTML = '<div class="today-warnings-empty">Không có cảnh báo hôm nay.</div>';
      return;
    }

    content.innerHTML = `
      <div class="today-warnings-count">${warnings.length} cảnh báo</div>
      <div class="today-warnings-table-wrap">
        <table class="today-warnings-table">
          <thead><tr><th>Thiết bị</th><th>Loại</th><th>Giá trị</th><th>Site / vùng</th><th>Thời gian</th></tr></thead>
          <tbody>${warnings.map(warning => `
            <tr>
              <td>${escapeHtml(warning.deviceName)}</td>
              <td>${escapeHtml(warning.metricType)}</td>
              <td>${Number(warning.metricValue).toFixed(0)}%</td>
              <td>${escapeHtml(warning.siteName || "Mạng ngoài")} / ${escapeHtml(warning.region || "-")}</td>
              <td>${fmtTime(warning.warnedAtUtc)}</td>
            </tr>
          `).join("")}</tbody>
        </table>
      </div>`;
  } catch (error) {
    console.error("Không tải được cảnh báo hôm nay:", error);
    content.innerHTML = '<div class="today-warnings-empty">Không tải được cảnh báo hôm nay.</div>';
  }
}

function isNonCompliant(d) {
  return d.adJoined === false || d.trellixInstalled === false || d.desktopCentralInstalled === false;
}

function metricClass(value) {
  if (value == null) return "";
  if (value >= 85) return "metric-bad";
  if (value >= 70) return "metric-warn";
  return "metric-ok";
}

function fmtPercent(v) {
  return v == null ? "-" : `${Number(v).toFixed(0)}%`;
}

function fmtTime(iso) {
  if (!iso) return "-";
  const normalized = /z$|[+-]\d{2}:\d{2}$/i.test(iso) ? iso : `${iso}Z`;
  const d = new Date(normalized);
  return d.toLocaleString("vi-VN", {
    hour: "2-digit",
    minute: "2-digit",
    day: "2-digit",
    month: "2-digit",
    year: "numeric"
  });
}

function anbmIcon(ok) {
  if (ok === null || ok === undefined) return '<span class="anbm-icon" title="Chưa có dữ liệu">⚪</span>';
  return ok
    ? '<span class="anbm-icon anbm-ok" title="Đã tuân thủ">✅</span>'
    : '<span class="anbm-icon anbm-bad" title="Chưa tuân thủ">❌</span>';
}

// ---------------- Render bảng ----------------

function renderTable(emptyMessage) {
  const search = document.getElementById("searchBox").value.trim().toLowerCase();
  const region = document.getElementById("regionFilter").value;
  const status = document.getElementById("statusFilter").value;

  let filtered = allDevices.filter(d => {
    if (search && !(`${d.deviceName} ${d.lastSiteName ?? ""}`.toLowerCase().includes(search))) return false;
    if (region && d.lastRegion !== region) return false;
    if (status === "online" && !d.isOnline) return false;
    if (status === "offline" && d.isOnline) return false;
    if (status === "noncompliant" && !isNonCompliant(d)) return false;
    if (["VMB", "VMT", "VMN"].includes(summaryFilter) && d.lastRegion !== summaryFilter) return false;
    if (summaryFilter === "compliant" && isNonCompliant(d)) return false;
    if (summaryFilter === "noncompliant" && !isNonCompliant(d)) return false;
    if (summaryFilter === "internal" && !d.isInternal) return false;
    if (summaryFilter === "external" && d.isInternal) return false;
    return true;
  });

  const tbody = document.getElementById("deviceTableBody");
  lastFilteredDeviceNames = filtered.map(d => d.deviceName);

  const demoHint = isDemoMode
    ? `<div class="demo-hint">Dữ liệu demo đang được hiển thị để IT xem trước giao diện. Khi DB có dữ liệu thật, bảng sẽ tự thay đổi.</div>`
    : "";

  if (filtered.length === 0) {
    tbody.innerHTML = `${demoHint}<tr><td colspan="13" class="empty-row">${escapeHtml(emptyMessage || "Không có máy nào khớp bộ lọc.")}</td></tr>`;
    updateSelectAllCheckbox();
    updateSelectedExportButton();
    return;
  }

  tbody.innerHTML = `${demoHint}${filtered.map(d => `
    <tr class="${isNonCompliant(d) ? "row-noncompliant" : ""}">
      <td class="row-checkbox-cell">
        <input type="checkbox" class="row-checkbox" data-device="${encodeURIComponent(d.deviceName)}"
          ${selectedDeviceNames.has(d.deviceName) ? "checked" : ""} />
      </td>
      <td>
        <span class="badge ${d.isOnline ? "badge-online" : "badge-offline"}">
          <span class="badge-dot"></span>${d.isOnline ? "Online" : "Offline"}
        </span>
      </td>
      <td><a class="device-link" href="/device.html?device=${encodeURIComponent(d.deviceName)}"><strong>${escapeHtml(d.deviceName)}</strong></a></td>
      <td>${escapeHtml(displaySiteName(d.lastSiteName))}</td>
      <td>${escapeHtml(d.lastRegion || "-")}</td>
      <td>${d.isInternal ? "🟢 Nội bộ" : "🔴 Ngoài"}</td>
      <td>${formatConnection(d)}</td>
      <td class="metric ${metricClass(d.cpuPercent)}">${fmtPercent(d.cpuPercent)}</td>
      <td class="metric ${metricClass(d.ramPercent)}">${fmtPercent(d.ramPercent)}</td>
      <td class="metric ${metricClass(d.diskPercent)}">${fmtPercent(d.diskPercent)}</td>
      <td>
        <div class="anbm-icons">
          ${anbmIcon(d.adJoined)}${anbmIcon(d.trellixInstalled)}${anbmIcon(d.desktopCentralInstalled)}
        </div>
      </td>
      <td>${d.warningsToday > 0 ? `<span class="metric metric-bad">${d.warningsToday}</span>` : "0"}</td>
      <td>${fmtTime(d.lastSeenUtc)}</td>
    </tr>
  `).join("")}`;
  updateSelectAllCheckbox();
  updateSelectedExportButton();
}

function formatConnection(device) {
  if (device.networkType === "WiFi") {
    return device.wifiSignalDbm == null ? "-" : `📶 ${escapeHtml(device.wifiSignalDbm)} dBm`;
  }
  if (device.networkType === "LAN") {
    return `🔌 ${escapeHtml(device.lanLinkSpeed || "-")}`;
  }
  return "-";
}

function escapeHtml(str) {
  const div = document.createElement("div");
  div.textContent = str ?? "";
  return div.innerHTML;
}

function displaySiteName(siteName) {
  if (!siteName) return "-";
  let value = siteName;
  for (let attempt = 0; attempt < 2 && /[\u00c3\u00c2\u00e2\ufffd]/.test(value); attempt++) {
    try {
      const windows1252Bytes = {
        "\u20ac": 0x80, "\u201a": 0x82, "\u0192": 0x83, "\u201e": 0x84,
        "\u2026": 0x85, "\u2020": 0x86, "\u2021": 0x87, "u02c6": 0x88,
        "\u2030": 0x89, "\u0160": 0x8a, "\u2039": 0x8b, "\u0152": 0x8c,
        "\u017d": 0x8e, "\u2018": 0x91, "\u2019": 0x92, "\u201c": 0x93,
        "\u201d": 0x94, "\u2022": 0x95, "\u2013": 0x96, "\u2014": 0x97,
        "\u02dc": 0x98, "\u2122": 0x99, "\u0161": 0x9a, "\u203a": 0x9b,
        "\u0153": 0x9c, "\u017e": 0x9e, "\u0178": 0x9f
      };
      const bytes = Uint8Array.from(value, character => windows1252Bytes[character] ?? character.charCodeAt(0));
      const decoded = new TextDecoder("utf-8", { fatal: true }).decode(bytes);
      if (decoded === value) break;
      value = decoded;
    } catch {
      break;
    }
  }
  return value;
}

async function loadSettings() {
  try {
    const response = await fetch(`${API_BASE}/api/settings`);
    if (!response.ok) return null;
    const settings = await response.json();
    settingsCache = settings;
    populateSettingsForm(settings);
    return settings;
  } catch (error) {
    console.error("Không tải được cài đặt hệ thống:", error);
    return null;
  }
}

function populateSettingsForm(settings) {
  const form = document.getElementById("settingsForm");
  if (!form || !settings) return;

  const values = {
    systemName: settings.systemName ?? "SGR NetGuard",
    companyName: settings.companyName ?? "Sun Group",
    apiServerUrl: settings.apiServerUrl ?? "",
    timezone: settings.timezone ?? "Asia/Ho_Chi_Minh",
    language: settings.language ?? "vi",
    realtimeEnabled: Boolean(settings.realtimeEnabled),
    dashboardUsername: settings.dashboardUsername ?? "admin",
    dashboardPassword: ""
  };

  for (const [key, value] of Object.entries(values)) {
    const field = form.elements.namedItem(key);
    if (!field) continue;
    if (field.type === "checkbox") field.checked = !!value;
    else field.value = value;
  }
}

async function saveSettings(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const data = Object.fromEntries(new FormData(form).entries());

  const payload = {
    systemName: String(data.systemName || "SGR NetGuard").trim(),
    companyName: String(data.companyName || "Sun Group").trim(),
    apiServerUrl: String(data.apiServerUrl || "").trim(),
    timezone: String(data.timezone || "Asia/Ho_Chi_Minh").trim(),
    language: String(data.language || "vi").trim(),
    realtimeEnabled: Boolean(data.realtimeEnabled),
    dashboardUsername: String(data.dashboardUsername || "admin").trim(),
    dashboardPassword: String(data.dashboardPassword || "").trim()
  };

  if (!payload.dashboardPassword) {
    const current = settingsCache || await loadSettings();
    payload.dashboardPassword = current?.dashboardPassword || "Sun@2026";
  }

  if (payload.dashboardPassword && payload.dashboardPassword.length < 3) {
    alert("Mật khẩu Dashboard phải có ít nhất 3 ký tự.");
    return;
  }

  const response = await fetch(`${API_BASE}/api/settings`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    const message = await response.text().catch(() => "Không lưu được cài đặt.");
    alert(message || "Không lưu được cài đặt.");
    return;
  }

  settingsCache = payload;
  populateSettingsForm(payload);
  alert("Cài đặt hệ thống đã được lưu.");
  closeSettingsModal();
}

function openSettingsModal() {
  const modal = document.getElementById("settingsModal");
  if (!modal) return;
  modal.classList.remove("hidden");
  modal.setAttribute("aria-hidden", "false");
}

function closeSettingsModal() {
  const modal = document.getElementById("settingsModal");
  if (!modal) return;
  modal.classList.add("hidden");
  modal.setAttribute("aria-hidden", "true");
}

function bindSettingsModal() {
  const settingsButton = document.getElementById("settingsButton");
  const closeSettingsBtn = document.getElementById("closeSettingsBtn");
  const modal = document.getElementById("settingsModal");
  const form = document.getElementById("settingsForm");
  const logoutButton = document.getElementById("logoutSettingsBtn");

  settingsButton?.addEventListener("click", () => {
    loadSettings();
    openSettingsModal();
  });

  closeSettingsBtn?.addEventListener("click", closeSettingsModal);
  modal?.addEventListener("click", (event) => {
    if (event.target instanceof HTMLElement && event.target.dataset.close === "settings") {
      closeSettingsModal();
    }
  });
  form?.addEventListener("submit", saveSettings);
  logoutButton?.addEventListener("click", () => {
    window.location.href = "/logout";
  });
}

// ---------------- Realtime qua SignalR ----------------

function showToast(data) {
  const container = document.getElementById("toastContainer");
  const toast = document.createElement("div");
  toast.className = "toast";
  toast.innerHTML = `
    <strong>⚠️ ${escapeHtml(data.deviceName)} - Cảnh báo ${escapeHtml(data.metricType)}</strong>
    <span>${escapeHtml(data.siteName || "Mạng ngoài")} • ${Number(data.metricValue).toFixed(0)}% • ${new Date(data.timestamp).toLocaleTimeString("vi-VN")}</span>
  `;
  container.appendChild(toast);
  setTimeout(() => toast.remove(), 8000);
}

function initSignalR() {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${API_BASE}/hubs/alerts`)
    .withAutomaticReconnect()
    .build();

  connection.on("NewWarning", (data) => {
    showToast(data);
    loadDevices(); // cập nhật lại bảng + số liệu ngay khi có cảnh báo mới
  });

  connection.onreconnecting(() => setConnStatus(false));
  connection.onreconnected(() => setConnStatus(true));
  connection.onclose(() => setConnStatus(false));

  connection.start()
    .then(() => setConnStatus(true))
    .catch(err => {
      console.error("Không kết nối được SignalR:", err);
      setConnStatus(false);
    });
}

function setConnStatus(connected) {
  const el = document.getElementById("connStatus");
  if (connected) {
    el.textContent = "● Realtime đang hoạt động";
    el.classList.remove("disconnected");
  } else {
    el.textContent = "● Mất kết nối realtime, đang thử lại...";
    el.classList.add("disconnected");
  }
}

function updateSelectAllCheckbox() {
  const selectAll = document.getElementById("selectAllRows");
  if (!selectAll) return;
  if (lastFilteredDeviceNames.length === 0) {
    selectAll.checked = false;
    selectAll.indeterminate = false;
    return;
  }

  const selectedInView = lastFilteredDeviceNames.filter(name => selectedDeviceNames.has(name)).length;
  selectAll.checked = selectedInView === lastFilteredDeviceNames.length;
  selectAll.indeterminate = selectedInView > 0 && selectedInView < lastFilteredDeviceNames.length;
}

function updateSelectedExportButton() {
  const btn = document.getElementById("exportSelectedBtn");
  if (!btn) return;
  const count = selectedDeviceNames.size;
  btn.textContent = `Xuất báo cáo (${count})`;
  btn.disabled = count === 0 || isDemoMode;
}

function downloadBlob(blob, fileName) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.style.display = "none";
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function parseFileNameFromHeader(contentDisposition, fallback) {
  if (!contentDisposition) return fallback;
  const match = contentDisposition.match(/filename=\"?([^\";]+)\"?/i);
  return match ? match[1] : fallback;
}

async function exportSelectedDevices() {
  if (selectedDeviceNames.size === 0 || isDemoMode) return;
  const btn = document.getElementById("exportSelectedBtn");
  if (btn) btn.disabled = true;

  try {
    const response = await fetch(`${API_BASE}/api/reports/devices/excel`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ deviceNames: Array.from(selectedDeviceNames) })
    });

    if (!response.ok) {
      alert("Không xuất được báo cáo máy đã chọn.");
      return;
    }

    const blob = await response.blob();
    const fileName = parseFileNameFromHeader(
      response.headers.get("content-disposition"),
      "selected-devices-report.xlsx"
    );
    downloadBlob(blob, fileName);
  } catch (error) {
    console.error("Lỗi xuất báo cáo đã chọn:", error);
    alert("Không xuất được báo cáo máy đã chọn.");
  } finally {
    updateSelectedExportButton();
  }
}

// ---------------- Init ----------------

document.getElementById("searchBox").addEventListener("input", renderTable);
document.getElementById("regionFilter").addEventListener("change", () => {
  summaryFilter = "all";
  renderTable();
  renderNetworkDashboard();
});
document.getElementById("statusFilter").addEventListener("change", () => {
  summaryFilter = "all";
  renderTable();
  renderNetworkDashboard();
});
document.querySelectorAll("[data-summary-filter]").forEach(element => {
  element.addEventListener("click", () => setSummaryFilter(element.dataset.summaryFilter));
});
document.getElementById("loadDemoBtn").addEventListener("click", loadDemoData);
document.getElementById("exportSelectedBtn").addEventListener("click", exportSelectedDevices);
document.getElementById("statWarnToday").addEventListener("click", openTodayWarningsModal);
document.getElementById("closeTodayWarningsBtn").addEventListener("click", closeTodayWarningsModal);
document.getElementById("todayWarningsModal").addEventListener("click", (event) => {
  if (event.target instanceof HTMLElement && event.target.dataset.close === "today-warnings") {
    closeTodayWarningsModal();
  }
});
document.getElementById("selectAllRows").addEventListener("change", (event) => {
  const target = event.target;
  if (!(target instanceof HTMLInputElement)) return;
  for (const deviceName of lastFilteredDeviceNames) {
    if (target.checked) {
      selectedDeviceNames.add(deviceName);
    } else {
      selectedDeviceNames.delete(deviceName);
    }
  }
  renderTable();
});
document.getElementById("deviceTableBody").addEventListener("change", (event) => {
  const target = event.target;
  if (!(target instanceof HTMLInputElement)) return;
  if (!target.classList.contains("row-checkbox")) return;
  const encodedDevice = target.getAttribute("data-device");
  if (!encodedDevice) return;
  const deviceName = decodeURIComponent(encodedDevice);

  if (target.checked) {
    selectedDeviceNames.add(deviceName);
  } else {
    selectedDeviceNames.delete(deviceName);
  }
  updateSelectAllCheckbox();
  updateSelectedExportButton();
});
updateSelectedExportButton();
bindSettingsModal();
loadSettings();
loadDatabaseHealth();
loadDevices();
loadDashboardSummary();
initSignalR();
setInterval(loadDevices, 30000); // vẫn poll định kỳ làm nền, phòng khi SignalR bị rớt kết nối tạm thời
setInterval(loadDashboardSummary, 30000);
setInterval(loadDatabaseHealth, 30000);
