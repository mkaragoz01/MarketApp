const AUTH_API = "/api/auth";
const TOKEN_KEY = "marketapp_jwt";
const USERNAME_KEY = "marketapp_username";
const ROLE_KEY = "marketapp_role";
const ADMIN_ROLE = "Admin";

// Legacy panel elements (still used on login.html)
const authSection = document.getElementById("auth-section");
const sessionSection = document.getElementById("session-section");
const authManagementPanel = document.getElementById("management-panel");
const loginActions = document.getElementById("login-actions");
const displayUsername = document.getElementById("display-username");
const displayRole = document.getElementById("display-role");
const sessionDesc = document.getElementById("session-desc");
const headerSubtitle = document.getElementById("header-subtitle");
const guestHint = document.getElementById("guest-hint");
const adminOnlyElements = document.querySelectorAll("[data-admin-only]");
const authMessage = document.getElementById("auth-message");
const loginForm = document.getElementById("login-form");
const registerForm = document.getElementById("register-form");
const logoutBtn = document.getElementById("logout-btn");

// Top nav elements
const navUserInfo = document.getElementById("nav-user-info");
const navUsername = document.getElementById("nav-username");
const navRole = document.getElementById("nav-role");
const navLogoutBtn = document.getElementById("nav-logout-btn");
const navLoginBtn = document.getElementById("nav-login-btn");
const navLinks = document.querySelectorAll(".topnav-link[data-nav]");

const Auth = {
  getToken() {
    return localStorage.getItem(TOKEN_KEY);
  },

  getUsername() {
    return localStorage.getItem(USERNAME_KEY) || "";
  },

  getRole() {
    if (!this.isLoggedIn()) return "";
    return localStorage.getItem(ROLE_KEY) || "User";
  },

  isLoggedIn() {
    return !!this.getToken();
  },

  isAdminRole(role) {
    return (role || "").toLowerCase() === ADMIN_ROLE.toLowerCase();
  },

  isAdmin() {
    return this.isAdminRole(this.getRole());
  },

  getAuthHeaders(includeJson = true) {
    const headers = {};
    if (includeJson) headers["Content-Type"] = "application/json";
    const token = this.getToken();
    if (token) headers["Authorization"] = `Bearer ${token}`;
    return headers;
  },

  saveSession(token, username, role = "User") {
    localStorage.setItem(TOKEN_KEY, token);
    localStorage.setItem(USERNAME_KEY, username);
    localStorage.setItem(ROLE_KEY, role);
    this.updateUI();
    window.dispatchEvent(new CustomEvent("auth-changed", { detail: { loggedIn: true, role } }));
  },

  logout() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USERNAME_KEY);
    localStorage.removeItem(ROLE_KEY);
    this.updateUI();
    window.dispatchEvent(new CustomEvent("auth-changed", { detail: { loggedIn: false, role: "" } }));
  },

  updateUI() {
    const loggedIn = this.isLoggedIn();
    const role = this.getRole();
    const isAdmin = this.isAdmin();
    const currentPage = document.body.dataset.page || "";

    // ─── Legacy panel elements ───
    if (authSection) authSection.hidden = loggedIn;
    if (sessionSection) sessionSection.hidden = !loggedIn;
    if (authManagementPanel) authManagementPanel.hidden = !isAdmin;
    if (loginActions) loginActions.hidden = loggedIn;

    adminOnlyElements.forEach((el) => { el.hidden = !isAdmin; });

    if (guestHint) {
      guestHint.hidden = isAdmin;
      if (!loggedIn) {
        guestHint.textContent = "Bu liste herkese açıktır. Ürün eklemek veya düzenlemek için Admin rolü gerekir.";
      } else if (!isAdmin) {
        guestHint.textContent = "User rolüyle giriş yaptınız. Ürünleri görüntüleyebilirsiniz, yönetim işlemleri için Admin rolü gerekir.";
      }
    }

    if (loggedIn) {
      if (displayUsername) displayUsername.textContent = this.getUsername();
      if (displayRole) displayRole.textContent = role;
      if (sessionDesc) sessionDesc.textContent = isAdmin ? "Yönetim yetkisi aktif" : "Sadece görüntüleme yetkisi aktif";
      if (headerSubtitle && currentPage === "products") {
        headerSubtitle.textContent = isAdmin
          ? "Ürün yönetimi aktif — aşağıdaki panelden işlem yapın."
          : "User rolüyle ürünleri görüntüleyebilirsiniz.";
      }
    } else {
      if (displayRole) displayRole.textContent = "";
      if (sessionDesc) sessionDesc.textContent = "Oturum açık";
      if (headerSubtitle && currentPage === "products") {
        headerSubtitle.textContent = "Ürünleri görüntüleyin; yönetmek için giriş yapın.";
      }
    }

    // ─── Top nav ───
    if (navUserInfo) navUserInfo.hidden = !loggedIn;
    if (navUsername) navUsername.textContent = loggedIn ? this.getUsername() : "";
    if (navRole) navRole.textContent = loggedIn ? role : "";
    if (navLogoutBtn) navLogoutBtn.hidden = !loggedIn;

    if (navLoginBtn) {
      // Login sayfasında "Giriş Yap" butonunu gösterme
      if (currentPage === "login") {
        navLoginBtn.hidden = true;
      } else {
        navLoginBtn.hidden = loggedIn;
        const pagePath = window.location.pathname.split("/").pop() || "index.html";
        navLoginBtn.href = `login.html?returnUrl=${pagePath}`;
      }
    }

    // Aktif sayfa linkini işaretle
    navLinks.forEach((link) => {
      link.classList.toggle("active", link.dataset.nav === currentPage);
    });
  },

  showAuthMessage(text, type) {
    if (!authMessage) return;
    authMessage.textContent = text;
    authMessage.className = `toast ${type}`;
    authMessage.hidden = false;
  },

  hideAuthMessage() {
    if (!authMessage) return;
    authMessage.hidden = true;
  },
};

window.Auth = Auth;

function getReturnUrl() {
  const params = new URLSearchParams(window.location.search);
  const returnUrl = params.get("returnUrl");

  if (returnUrl && !returnUrl.includes("://") && !returnUrl.startsWith("//")) {
    return returnUrl;
  }

  return "index.html";
}

function redirectAfterAuth() {
  if (document.body.dataset.page !== "login") return;
  window.location.href = getReturnUrl();
}

document.querySelectorAll(".auth-tab").forEach((tab) => {
  tab.addEventListener("click", () => {
    document.querySelectorAll(".auth-tab").forEach((t) => t.classList.remove("active"));
    tab.classList.add("active");

    const isLogin = tab.dataset.authTab === "login";
    loginForm.hidden = !isLogin;
    registerForm.hidden = isLogin;
    Auth.hideAuthMessage();
  });
});

async function parseAuthError(response, fallback) {
  try {
    const data = await response.json();
    if (data.message) return data.message;
  } catch {
    /* ignore */
  }
  return fallback;
}

loginForm?.addEventListener("submit", async (e) => {
  e.preventDefault();
  Auth.hideAuthMessage();

  const body = {
    username: document.getElementById("login-username").value.trim(),
    password: document.getElementById("login-password").value,
  };

  try {
    const response = await fetch(`${AUTH_API}/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      const msg = await parseAuthError(response, "Giriş başarısız.");
      throw new Error(msg);
    }

    const data = await response.json();
    Auth.saveSession(data.token, data.username, data.role);
    loginForm.reset();
    redirectAfterAuth();
  } catch (err) {
    Auth.showAuthMessage(err.message, "error");
  }
});

registerForm?.addEventListener("submit", async (e) => {
  e.preventDefault();
  Auth.hideAuthMessage();

  const body = {
    username: document.getElementById("register-username").value.trim(),
    password: document.getElementById("register-password").value,
  };

  try {
    const response = await fetch(`${AUTH_API}/register`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      const msg = await parseAuthError(response, "Kayıt başarısız.");
      throw new Error(msg);
    }

    const data = await response.json();
    Auth.saveSession(data.token, data.username, data.role);
    registerForm.reset();
    redirectAfterAuth();
  } catch (err) {
    Auth.showAuthMessage(err.message, "error");
  }
});

// Logout — hem eski panel butonu (login.html) hem nav butonu
function handleLogout() {
  Auth.logout();
  if (document.body.dataset.page === "login") {
    Auth.showAuthMessage("Çıkış yapıldı.", "success");
  }
}

logoutBtn?.addEventListener("click", handleLogout);
navLogoutBtn?.addEventListener("click", handleLogout);

document.addEventListener("DOMContentLoaded", () => {
  Auth.updateUI();
});
