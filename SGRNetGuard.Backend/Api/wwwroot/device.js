const API_BASE = "";

function getDeviceName() {
  const url = new URL(window.location.href);
  return (url.searchParams.get("device") || "").trim();
}

function escapeHtml(str) {
  const div = document.createElement("div");
  div.textContent = str ?? "";
  return div.innerHTML;
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

function renderRows(detail, softwareItems = []) {
  const windowsValue = (detail.windowsVersion || "").trim();
  const windowsParts = windowsValue.split("|").map(part => part.trim()).filter(Boolean);

  const rows = [
    ["Tên máy", detail.deviceName],
    ["User hiện tại", detail.currentUser || detail.loggedInUser],
    ["MAC", detail.macAddress],
    ["IP LAN", detail.lanIp],
    ["IP Public", detail.publicIp],
    ["Domain", detail.domain],
    ["Edition", windowsParts.length >= 2 ? windowsParts[0] : (windowsValue || "-")],
    ["Version", windowsParts.length >= 2 ? windowsParts[1] : (windowsParts.length === 1 ? windowsParts[0] : "-")],
    ["Serial Number", detail.serialNumber],
    ["CPU", detail.cpuModel],
    ["RAM", detail.ramTotal],
    ["Ổ cứng", detail.diskTotal],
    ["Mainboard", detail.mainboard],
    ["Uptime", detail.uptime],
    ["Agent Version", detail.appVersion],
    ["Cập nhật gần nhất", fmtTime(detail.detailUpdatedUtc || detail.lastSeenUtc)],
    ["Trạng thái", detail.isOnline ? "Online" : "Offline"]
  ];

  const softwareHtml = softwareItems.length > 0
    ? `<tr id="softwareRow" style="cursor:pointer;">
        <th style="user-select:none;">
          <span id="softwareToggle" style="display:inline-block;width:16px;text-align:center;font-weight:bold;">▼</span> Phần mềm đã cài 
          <span style="color:#666;font-weight:normal;font-size:0.9em;">(${softwareItems.length})</span>
        </th>
        <td>
          <ul class="software-list" id="softwareList" style="display:block;">
            ${softwareItems.map(item => `<li>${escapeHtml(item.softwareName || "Unknown")}${item.version ? ` - <span style="color:#666;">${escapeHtml(item.version)}</span>` : ""}</li>`).join("")}
          </ul>
        </td>
      </tr>`
    : `<tr><th>Phần mềm đã cài</th><td>Không có dữ liệu phần mềm.</td></tr>`;

  const tbody = document.getElementById("deviceDetailBody");
  tbody.innerHTML = `${rows.map(([label, value]) => `
    <tr>
      <th>${escapeHtml(label)}</th>
      <td>${escapeHtml(value || "-")}</td>
    </tr>`).join("")}${softwareHtml}`;

  // Add click handler for software row
  if (softwareItems.length > 0) {
    const softwareRow = document.getElementById("softwareRow");
    const softwareList = document.getElementById("softwareList");
    const softwareToggle = document.getElementById("softwareToggle");
    
    let isExpanded = true;
    softwareRow.addEventListener("click", () => {
      isExpanded = !isExpanded;
      softwareList.style.display = isExpanded ? "block" : "none";
      softwareToggle.textContent = isExpanded ? "▼" : "▶";
    });
  }
}

async function loadSoftware(deviceName) {
  try {
    const url = `${API_BASE}/api/devices/${encodeURIComponent(deviceName)}/software`;
    console.log("Loading software from:", url);
    const response = await fetch(url);
    console.log("Software response status:", response.status);
    
    if (!response.ok) {
      console.error("Software API error:", response.status, response.statusText);
      return [];
    }
    
    const data = await response.json();
    console.log("Software data loaded:", data.length, "items");
    return Array.isArray(data) ? data : [];
  } catch (error) {
    console.error("Không tải được phần mềm máy:", error);
    return [];
  }
}

async function loadDetail() {
  const deviceName = getDeviceName();
  const status = document.getElementById("detailStatus");
  const tbody = document.getElementById("deviceDetailBody");

  if (!deviceName) {
    status.textContent = "Thiếu tên máy";
    tbody.innerHTML = `<tr><td colspan="2" class="empty-row">Thiếu tham số device.</td></tr>`;
    return;
  }

  try {
    const response = await fetch(`${API_BASE}/api/devices/${encodeURIComponent(deviceName)}`);
    if (!response.ok) {
      status.textContent = "Không tải được dữ liệu";
      tbody.innerHTML = `<tr><td colspan="2" class="empty-row">Không tìm thấy dữ liệu máy ${escapeHtml(deviceName)}.</td></tr>`;
      return;
    }

    const detail = await response.json();
    const software = await loadSoftware(deviceName);
    renderRows(detail, software);
    status.textContent = detail.isOnline ? "Online" : "Offline";
    status.classList.toggle("disconnected", !detail.isOnline);
  } catch (error) {
    console.error("Không tải được chi tiết máy:", error);
    status.textContent = "Lỗi tải dữ liệu";
    tbody.innerHTML = `<tr><td colspan="2" class="empty-row">Không tải được dữ liệu máy.</td></tr>`;
  }
}

loadDetail();
