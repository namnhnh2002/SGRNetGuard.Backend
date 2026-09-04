const API_BASE = "";
let state = { expanded: false, status: null, deviceName: "" };

function getDeviceName() {
  const url = new URL(window.location.href);
  return (url.searchParams.get("device") || "").trim();
}

function escapeHtml(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/\"/g, "&quot;");
}

async function loadStatus() {
  const deviceName = getDeviceName();
  state.deviceName = deviceName;
  const url = deviceName ? `${API_BASE}/api/agent/status?deviceName=${encodeURIComponent(deviceName)}` : `${API_BASE}/api/agent/status`;
  try {
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error("Không tải được trạng thái");
    }
    state.status = await response.json();
    render();
  } catch (error) {
    document.getElementById("subtitle").textContent = "Không thể tải trạng thái";
    document.getElementById("body").innerHTML = `<div class="line">${escapeHtml(error.message)}</div>`;
  }
}

function render() {
  const body = document.getElementById("body");
  const subtitle = document.getElementById("subtitle");
  const status = state.status;
  if (!status) {
    body.innerHTML = `<div class="line">Đang tải...</div>`;
    return;
  }

  subtitle.textContent = status.deviceName || "Thiết bị chưa xác định";

  if (!state.expanded) {
    const hasWarning = status.hasExternalNetworkWarning || status.hasSecurityWarning || status.hasPerformanceWarning;
    const okText = `🟢 ${status.isOk ? "Tất cả đang ổn" : "Cần chú ý"}`;
    body.innerHTML = `
      <div class="mini">
        <div class="line ${hasWarning ? "status-warn" : "status-ok"}">
          <strong>${escapeHtml(status.isOk ? "Không có cảnh báo" : "Có cảnh báo cần xử lý")}</strong><br/>
          ${escapeHtml(status.summary)}
        </div>
        ${hasWarning ? `<div class="line">${escapeHtml(status.hasExternalNetworkWarning ? status.networkWarningMessage : status.hasSecurityWarning ? status.securityWarningMessage : status.performanceWarningMessage || "")}</div>` : ""}
        <div class="actions">
          <button id="expandBtn">MỞ RỘNG</button>
          ${status.hasExternalNetworkWarning ? `<button class="secondary small" data-action="dismiss">Đã hiểu</button><button class="secondary small" data-action="hide">Không hiển thị lại</button>` : ""}
        </div>
      </div>`;
    attachActionHandlers();
    return;
  }

  body.innerHTML = `
    <div class="mini">
      <div class="line ${status.isOk ? "status-ok" : "status-warn"}">
        <strong>${escapeHtml(status.summary)}</strong>
      </div>
      <div class="section">
        <h4>Overview</h4>
        <p>Thiết bị: ${escapeHtml(status.deviceName)}<br/>Người dùng: ${escapeHtml(status.currentUser || "-")}</p>
      </div>
      <div class="section">
        <h4>Hardware</h4>
        <p>Đang hiển thị sơ bộ cho IT/Admin trên web quản trị. Trên máy User chỉ hiển thị cảnh báo quan trọng.</p>
      </div>
      <div class="section">
        <h4>Network</h4>
        <p>${escapeHtml(status.connectionType || "-" )}<br/>SSID: ${escapeHtml(status.ssid || "-")}<br/>Signal: ${status.signalStrengthDbm == null ? "-" : `${status.signalStrengthDbm} dBm`}</p>
      </div>
      <div class="section">
        <h4>Security</h4>
        <p>${escapeHtml(status.securityWarningMessage || "Thiết bị đang ở trạng thái tuân thủ.")}</p>
      </div>
      <div class="section">
        <h4>Performance</h4>
        <p>${escapeHtml(status.performanceWarningMessage || "Hiệu năng ổn định.")}</p>
      </div>
      <div class="section">
        <h4>Software</h4>
        <p>Danh sách phần mềm sẽ được đồng bộ định kỳ và hiển thị trên web quản trị.</p>
      </div>
      <div class="actions">
        <button class="secondary" id="collapseBtn">THU NHỎ</button>
      </div>
    </div>`;
  document.getElementById("collapseBtn").addEventListener("click", () => {
    state.expanded = false;
    render();
  });
}

function attachActionHandlers() {
  document.getElementById("expandBtn")?.addEventListener("click", () => {
    state.expanded = true;
    render();
  });

  document.querySelectorAll("[data-action]").forEach((element) => {
    element.addEventListener("click", async () => {
      const action = element.getAttribute("data-action");
      await updatePreference(action);
    });
  });
}

async function updatePreference(action) {
  const payload = { deviceName: state.deviceName || state.status?.deviceName, action };
  const response = await fetch(`${API_BASE}/api/agent/preferences`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  if (!response.ok) {
    alert("Không cập nhật được tùy chọn.");
    return;
  }
  await loadStatus();
}

loadStatus();
