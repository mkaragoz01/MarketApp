const LOGS_API = "/api/logs";
const LOGS_PAGE_URL = "logs.html";
const LOGS_LOGIN_URL = `login.html?returnUrl=${LOGS_PAGE_URL}`;
const LOGS_ACCESS_DENIED_URL = "index.html?unauthorized=logs";

const accessDenied = document.getElementById("access-denied");
const logsPanel = document.getElementById("logs-panel");
const logsBody = document.getElementById("logs-body");
const logsLoadError = document.getElementById("logs-load-error");
const logCount = document.getElementById("log-count");
const logFilterForm = document.getElementById("log-filter-form");
const logLevel = document.getElementById("log-level");
const logSearch = document.getElementById("log-search");
const logFrom = document.getElementById("log-from");
const logTo = document.getElementById("log-to");
const refreshLogsBtn = document.getElementById("refresh-logs-btn");
const clearLogFiltersBtn = document.getElementById("clear-log-filters-btn");
const exportLogsBtn = document.getElementById("export-logs-btn");

let logsCache = [];

function showMessage(element, text, type) {
  element.textContent = text;
  element.className = `toast ${type}`;
  element.hidden = false;
}

function hideMessage(element) {
  element.hidden = true;
}

function hideAdminContent() {
  accessDenied.hidden = true;
  logsPanel.hidden = true;
  exportLogsBtn.hidden = true;
  exportLogsBtn.disabled = false;
}

function redirectToLogin() {
  hideAdminContent();
  window.location.replace(LOGS_LOGIN_URL);
}

function redirectToAccessDenied() {
  hideAdminContent();
  window.location.replace(LOGS_ACCESS_DENIED_URL);
}

function guardAdminPage() {
  if (!window.Auth?.isLoggedIn()) {
    redirectToLogin();
    return false;
  }

  if (!window.Auth.isAdmin()) {
    redirectToAccessDenied();
    return false;
  }

  return true;
}

function showAdminContent() {
  if (!window.Auth?.isAdmin()) {
    hideAdminContent();
    return;
  }

  window.Auth.updateUI();
  accessDenied.hidden = true;
  logsPanel.hidden = false;
  exportLogsBtn.hidden = false;
}

function buildQuery() {
  const params = new URLSearchParams();

  if (logLevel.value) params.set("level", logLevel.value);
  if (logSearch.value.trim()) params.set("search", logSearch.value.trim());
  if (logFrom.value) params.set("from", new Date(logFrom.value).toISOString());
  if (logTo.value) params.set("to", new Date(logTo.value).toISOString());

  return params.toString();
}

async function parseApiError(response, fallback) {
  try {
    const data = await response.json();
    if (data.message) return data.message;
    if (data.title) return data.title;
  } catch {
    /* ignore */
  }

  return fallback;
}

async function loadLogs() {
  if (!guardAdminPage()) return;

  hideMessage(logsLoadError);
  logsBody.innerHTML = `
    <tr>
      <td colspan="5" class="empty">
        <span class="spinner" aria-hidden="true"></span>
        Yükleniyor...
      </td>
    </tr>`;

  try {
    const query = buildQuery();
    const response = await fetch(`${LOGS_API}${query ? `?${query}` : ""}`, {
      headers: window.Auth.getAuthHeaders(false),
    });

    if (!response.ok) {
      if (response.status === 401) {
        redirectToLogin();
        return;
      }

      if (response.status === 403) {
        redirectToAccessDenied();
        return;
      }

      const message = await parseApiError(response, `Log kayıtları alınamadı (${response.status})`);
      throw new Error(message);
    }

    logsCache = await response.json();
    showAdminContent();
    renderLogs();
  } catch (err) {
    logsCache = [];
    logCount.textContent = "Log kayıtları alınamadı";
    logsBody.innerHTML = `<tr><td colspan="5" class="empty">Log kayıtları yüklenemedi.</td></tr>`;
    showMessage(logsLoadError, err.message, "error");
  }
}

function renderLogs() {
  logCount.textContent = logsCache.length === 0
    ? "Henüz log kaydı yok"
    : `${logsCache.length} kayıt gösteriliyor`;

  if (!logsCache.length) {
    logsBody.innerHTML = `<tr><td colspan="5" class="empty">Filtreye uygun log kaydı yok.</td></tr>`;
    return;
  }

  logsBody.innerHTML = logsCache
    .map((log) => `
      <tr>
        <td class="id-cell">${formatDate(log.createdAtUtc)}</td>
        <td><span class="log-level ${getLevelClass(log.level)}">${escapeHtml(log.level)}</span></td>
        <td>
          <div class="log-message">${escapeHtml(log.message)}</div>
          <div class="log-category">${escapeHtml(log.category)}</div>
        </td>
        <td>${escapeHtml(log.username || "-")}</td>
        <td>${escapeHtml(log.path || "-")}</td>
      </tr>`)
    .join("");
}

async function exportLogs() {
  if (!guardAdminPage()) return;

  exportLogsBtn.disabled = true;

  try {
    const query = buildQuery();
    const response = await fetch(`${LOGS_API}/export${query ? `?${query}` : ""}`, {
      headers: window.Auth.getAuthHeaders(false),
    });

    if (!response.ok) {
      const message = await parseApiError(response, `Log kayıtları dışa aktarılamadı (${response.status})`);
      throw new Error(message);
    }

    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = getExportFileName(response);
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  } catch (err) {
    showMessage(logsLoadError, err.message, "error");
  } finally {
    exportLogsBtn.disabled = false;
  }
}

function getExportFileName(response) {
  const disposition = response.headers.get("Content-Disposition") || "";
  const match = disposition.match(/filename="?([^"]+)"?/i);
  return match?.[1] || "marketapp-logs.csv";
}

function formatDate(value) {
  return new Intl.DateTimeFormat("tr-TR", {
    dateStyle: "short",
    timeStyle: "medium",
  }).format(new Date(value));
}

function getLevelClass(level) {
  if (level === "Error") return "log-level-error";
  if (level === "Warning") return "log-level-warning";
  return "log-level-info";
}

function escapeHtml(text) {
  const div = document.createElement("div");
  div.textContent = text;
  return div.innerHTML;
}

logFilterForm.addEventListener("submit", (e) => {
  e.preventDefault();
  loadLogs();
});

logFilterForm.addEventListener("reset", () => {
  // values are cleared by browser before this fires; reload without filters
  setTimeout(loadLogs, 0);
});

refreshLogsBtn.addEventListener("click", loadLogs);
exportLogsBtn.addEventListener("click", exportLogs);

window.addEventListener("auth-changed", (e) => {
  if (!e.detail.loggedIn) {
    redirectToLogin();
    return;
  }

  if (!window.Auth?.isAdmin()) {
    redirectToAccessDenied();
    return;
  }

  loadLogs();
});

document.addEventListener("DOMContentLoaded", loadLogs);
