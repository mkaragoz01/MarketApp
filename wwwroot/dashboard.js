const DASHBOARD_API = "/dashboard/summary";
const DASHBOARD_PAGE_URL = "dashboard.html";
const DASHBOARD_LOGIN_URL = `login.html?returnUrl=${DASHBOARD_PAGE_URL}`;
const DASHBOARD_ACCESS_DENIED_URL = "index.html?unauthorized=dashboard";

const accessDenied = document.getElementById("access-denied");
const dashboardPanel = document.getElementById("dashboard-panel");
const refreshDashboardBtn = document.getElementById("refresh-dashboard-btn");
const dashboardTotalProducts = document.getElementById("dashboard-total-products");
const dashboardTotalStock = document.getElementById("dashboard-total-stock");
const dashboardTotalValue = document.getElementById("dashboard-total-value");
const dashboardMostExpensive = document.getElementById("dashboard-most-expensive");
const dashboardMostExpensiveDetail = document.getElementById("dashboard-most-expensive-detail");
const dashboardLowStockThreshold = document.getElementById("dashboard-low-stock-threshold");
const dashboardLowStockList = document.getElementById("dashboard-low-stock-list");
const dashboardMessage = document.getElementById("dashboard-message");

const priceFormatter = new Intl.NumberFormat("tr-TR", {
  style: "currency",
  currency: "TRY",
});

const numberFormatter = new Intl.NumberFormat("tr-TR");

function showMessage(element, text, type) {
  element.textContent = text;
  element.className = `toast ${type}`;
  element.hidden = false;
}

function hideMessage(element) {
  element.hidden = true;
}

function setDashboardLoading() {
  dashboardTotalProducts.textContent = "—";
  dashboardTotalStock.textContent = "—";
  dashboardTotalValue.textContent = "—";
  dashboardMostExpensive.textContent = "—";
  dashboardMostExpensiveDetail.textContent = "—";
  dashboardLowStockThreshold.textContent = "—";
  dashboardLowStockList.innerHTML = `<p class="dashboard-empty">Yükleniyor...</p>`;
}

function hideAdminContent() {
  accessDenied.hidden = true;
  dashboardPanel.hidden = true;
  refreshDashboardBtn.disabled = false;
  hideMessage(dashboardMessage);
  setDashboardLoading();
}

function redirectToLogin() {
  hideAdminContent();
  window.location.replace(DASHBOARD_LOGIN_URL);
}

function redirectToAccessDenied() {
  hideAdminContent();
  window.location.replace(DASHBOARD_ACCESS_DENIED_URL);
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

async function loadDashboard() {
  if (!guardAdminPage()) return;

  window.Auth.updateUI();
  accessDenied.hidden = true;
  dashboardPanel.hidden = false;
  refreshDashboardBtn.disabled = true;
  hideMessage(dashboardMessage);
  setDashboardLoading();

  try {
    const response = await fetch(DASHBOARD_API, {
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

      const message = await parseApiError(response, `Dashboard alınamadı (${response.status})`);
      throw new Error(message);
    }

    renderDashboard(await response.json());
  } catch (err) {
    showMessage(dashboardMessage, err.message, "error");
  } finally {
    refreshDashboardBtn.disabled = false;
  }
}

function renderDashboard(summary) {
  dashboardTotalProducts.textContent = numberFormatter.format(summary.totalProductCount ?? 0);
  dashboardTotalStock.textContent = numberFormatter.format(summary.totalStock ?? 0);
  dashboardTotalValue.textContent = priceFormatter.format(summary.totalStockValue ?? 0);

  if (summary.mostExpensiveProduct) {
    dashboardMostExpensive.textContent = summary.mostExpensiveProduct.name;
    dashboardMostExpensiveDetail.textContent =
      `${priceFormatter.format(summary.mostExpensiveProduct.price)} / ${summary.mostExpensiveProduct.unit || "Adet"}`;
  } else {
    dashboardMostExpensive.textContent = "Kayıt yok";
    dashboardMostExpensiveDetail.textContent = "—";
  }

  dashboardLowStockThreshold.textContent =
    `${numberFormatter.format(summary.lowStockThreshold ?? 10)} adedin altı`;

  const lowStockProducts = summary.lowStockProducts || [];
  if (!lowStockProducts.length) {
    dashboardLowStockList.innerHTML = `<p class="dashboard-empty">Stoğu azalan ürün yok.</p>`;
    return;
  }

  dashboardLowStockList.innerHTML = lowStockProducts
    .map((product) => `
      <div class="dashboard-low-stock-item">
        <span>${escapeHtml(product.name)}</span>
        <strong>${numberFormatter.format(product.stock)} ${escapeHtml(product.unit || "Adet")}</strong>
      </div>`)
    .join("");
}

function escapeHtml(text) {
  const div = document.createElement("div");
  div.textContent = text;
  return div.innerHTML;
}

refreshDashboardBtn.addEventListener("click", loadDashboard);

window.addEventListener("auth-changed", (e) => {
  if (!e.detail.loggedIn) {
    redirectToLogin();
    return;
  }

  if (!window.Auth?.isAdmin()) {
    redirectToAccessDenied();
    return;
  }

  loadDashboard();
});

document.addEventListener("DOMContentLoaded", loadDashboard);
