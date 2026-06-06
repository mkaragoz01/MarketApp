const USERS_API = "/api/users";
const USERS_PAGE_URL = "users.html";
const USERS_LOGIN_URL = `login.html?returnUrl=${USERS_PAGE_URL}`;
const USERS_ACCESS_DENIED_URL = "index.html?unauthorized=users";

const accessDenied = document.getElementById("access-denied");
const userManagementPanel = document.getElementById("user-management-panel");
const usersListPanel = document.getElementById("users-list-panel");
const userForm = document.getElementById("user-form");
const userFormTitle = document.getElementById("user-form-title");
const userSubmitText = document.getElementById("user-submit-text");
const cancelUserEditBtn = document.getElementById("cancel-user-edit-btn");
const userFormMessage = document.getElementById("user-form-message");
const usersLoadError = document.getElementById("users-load-error");
const usersBody = document.getElementById("users-body");
const userCount = document.getElementById("user-count");
const refreshUsersBtn = document.getElementById("refresh-users-btn");
const userUsername = document.getElementById("user-username");
const userPassword = document.getElementById("user-password");
const userRole = document.getElementById("user-role");

let usersCache = [];
let editingUserId = null;

function showMessage(element, text, type) {
  element.textContent = text;
  element.className = `toast ${type}`;
  element.hidden = false;
}

function hideMessage(element) {
  element.hidden = true;
}

function showAdminContent() {
  window.Auth?.updateUI();
  accessDenied.hidden = true;
  userManagementPanel.hidden = false;
  usersListPanel.hidden = false;
}

function showAccessDenied() {
  accessDenied.hidden = false;
  userManagementPanel.hidden = true;
  usersListPanel.hidden = true;
}

function redirectToLogin() {
  window.location.replace(USERS_LOGIN_URL);
}

function redirectToAccessDenied() {
  window.location.replace(USERS_ACCESS_DENIED_URL);
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
    if (typeof data === "string") return data;
    if (data.message) return data.message;
    if (data.errors) {
      const validationMessages = Object.values(data.errors).flat().filter(Boolean);
      if (validationMessages.length) return validationMessages.join(" ");
    }
    if (data.title) return data.title;
  } catch {
    /* ignore */
  }

  return fallback;
}

function getUserPayload() {
  const payload = {
    username: userUsername.value.trim(),
    role: userRole.value,
  };

  if (editingUserId === null || userPassword.value.trim()) {
    payload.password = userPassword.value;
  }

  return payload;
}

function setFormMode(user = null) {
  editingUserId = user?.id ?? null;
  const isEdit = editingUserId !== null;

  userFormTitle.textContent = isEdit ? "Kullanıcıyı düzenle" : "Yeni kullanıcı ekle";
  userSubmitText.textContent = isEdit ? "Güncelle" : "Kullanıcı ekle";
  cancelUserEditBtn.hidden = !isEdit;
  userPassword.required = !isEdit;
  userPassword.placeholder = isEdit ? "Boş bırakılırsa değişmez" : "";
  userManagementPanel.classList.toggle("panel-edit-mode", isEdit);

  if (user) {
    userUsername.value = user.username;
    userRole.value = user.role;
    userPassword.value = "";
  }
}

function resetForm() {
  userForm.reset();
  userRole.value = "User";
  setFormMode(null);
  hideMessage(userFormMessage);
}

async function loadUsers() {
  if (!guardAdminPage()) return;

  hideMessage(usersLoadError);
  usersBody.innerHTML = `
    <tr>
      <td colspan="4" class="empty">
        <span class="spinner" aria-hidden="true"></span>
        Yükleniyor...
      </td>
    </tr>`;

  try {
    const response = await fetch(USERS_API, {
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

      const message = await parseApiError(response, `Kullanıcılar alınamadı (${response.status})`);
      throw new Error(message);
    }

    usersCache = await response.json();
    showAdminContent();
    renderUsers();
  } catch (err) {
    usersCache = [];
    userCount.textContent = "Kullanıcılar alınamadı";
    usersBody.innerHTML = `<tr><td colspan="4" class="empty">Kullanıcılar yüklenemedi.</td></tr>`;
    showMessage(usersLoadError, err.message, "error");
  }
}

function renderUsers() {
  userCount.textContent = usersCache.length === 0
    ? "Henüz kullanıcı yok"
    : `${usersCache.length} kullanıcı kayıtlı`;

  if (!usersCache.length) {
    usersBody.innerHTML = `<tr><td colspan="4" class="empty">Henüz kullanıcı yok.</td></tr>`;
    return;
  }

  const currentUsername = window.Auth.getUsername().toLowerCase();

  usersBody.innerHTML = usersCache
    .map((user) => {
      const isEditing = editingUserId === user.id;
      const isCurrentUser = user.username.toLowerCase() === currentUsername;

      return `
        <tr class="${isEditing ? "row-editing" : ""}" data-id="${user.id}">
          <td class="id-cell">${user.id}</td>
          <td>
            ${escapeHtml(user.username)}
            ${isCurrentUser ? '<span class="role-chip role-chip-inline">Siz</span>' : ""}
          </td>
          <td><span class="role-chip role-chip-inline">${escapeHtml(user.role)}</span></td>
          <td>
            <div class="row-actions">
              <button type="button" class="btn btn-sm btn-edit" data-action="edit" data-id="${user.id}">Düzenle</button>
              <button type="button" class="btn btn-sm btn-danger" data-action="delete" data-id="${user.id}" ${isCurrentUser ? "disabled" : ""}>Sil</button>
            </div>
          </td>
        </tr>`;
    })
    .join("");
}

function escapeHtml(text) {
  const div = document.createElement("div");
  div.textContent = text;
  return div.innerHTML;
}

function findUser(id) {
  return usersCache.find((user) => user.id === id);
}

async function deleteUser(id) {
  const user = findUser(id);
  if (!user) return;

  const ok = confirm(`"${user.username}" kullanıcısı silinsin mi?`);
  if (!ok) return;

  const response = await fetch(`${USERS_API}/${id}`, {
    method: "DELETE",
    headers: window.Auth.getAuthHeaders(false),
  });

  if (!response.ok) {
    const message = await parseApiError(response, `Kullanıcı silinemedi (${response.status})`);
    throw new Error(message);
  }

  if (editingUserId === id) resetForm();
  await loadUsers();
}

userForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  if (!guardAdminPage()) return;

  hideMessage(userFormMessage);
  const submitBtn = userForm.querySelector('button[type="submit"]');
  submitBtn.disabled = true;

  try {
    const isEdit = editingUserId !== null;
    const response = await fetch(isEdit ? `${USERS_API}/${editingUserId}` : USERS_API, {
      method: isEdit ? "PUT" : "POST",
      headers: window.Auth.getAuthHeaders(),
      body: JSON.stringify(getUserPayload()),
    });

    if (!response.ok) {
      const message = await parseApiError(
        response,
        isEdit ? `Kullanıcı güncellenemedi (${response.status})` : `Kullanıcı eklenemedi (${response.status})`
      );
      throw new Error(message);
    }

    resetForm();
    showMessage(userFormMessage, isEdit ? "Kullanıcı güncellendi." : "Kullanıcı eklendi.", "success");
    await loadUsers();
  } catch (err) {
    showMessage(userFormMessage, err.message, "error");
  } finally {
    submitBtn.disabled = false;
  }
});

usersBody.addEventListener("click", async (e) => {
  const btn = e.target.closest("[data-action]");
  if (!btn || btn.disabled) return;

  const id = parseInt(btn.dataset.id, 10);
  const user = findUser(id);
  if (!user) return;

  try {
    if (btn.dataset.action === "edit") {
      setFormMode(user);
      hideMessage(userFormMessage);
      userManagementPanel.scrollIntoView({ behavior: "smooth", block: "start" });
      return;
    }

    if (btn.dataset.action === "delete") {
      await deleteUser(id);
    }
  } catch (err) {
    showMessage(usersLoadError, err.message, "error");
  }
});

cancelUserEditBtn.addEventListener("click", resetForm);
refreshUsersBtn.addEventListener("click", loadUsers);

window.addEventListener("auth-changed", (e) => {
  if (!e.detail.loggedIn) {
    redirectToLogin();
    return;
  }

  if (!window.Auth?.isAdmin()) {
    resetForm();
    redirectToAccessDenied();
    return;
  }

  loadUsers();
});

document.addEventListener("DOMContentLoaded", loadUsers);
