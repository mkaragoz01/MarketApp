const API_URL = "/products";
const ADMIN_REQUIRED_MSG = "Bu işlem için Admin rolü gerekir.";
const DEFAULT_PAGE_SIZE = 10;

const form = document.getElementById("product-form");
const managementPanel = document.getElementById("management-panel");
const formTitle = document.getElementById("form-title");
const productsThead = document.getElementById("products-thead");
const submitBtnText = document.getElementById("submit-btn-text");
const cancelEditBtn = document.getElementById("cancel-edit-btn");
const productsBody = document.getElementById("products-body");
const formMessage = document.getElementById("form-message");
const loadError = document.getElementById("load-error");
const refreshBtn = document.getElementById("refresh-btn");
const productCount = document.getElementById("product-count");
const searchInput = document.getElementById("search-input");
const searchHint = document.getElementById("search-hint");
const paginationControls = document.getElementById("pagination-controls");
const prevPageBtn = document.getElementById("prev-page-btn");
const nextPageBtn = document.getElementById("next-page-btn");
const paginationInfo = document.getElementById("pagination-info");

let productsCache = [];
let editingId = null;
let searchQuery = "";
let draggedRow = null;
let currentPage = 1;
let pageSize = DEFAULT_PAGE_SIZE;
let totalProductsCount = 0;
let totalPages = 1;
let searchDebounceId = null;

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

function requireAdmin() {
  if (window.Auth?.isAdmin()) return true;
  showMessage(loadError, ADMIN_REQUIRED_MSG, "error");
  return false;
}

function getColSpan() {
  return window.Auth?.isAdmin() ? 7 : 5;
}

function updateTableHeader() {
  const isAdmin = window.Auth?.isAdmin() ?? false;

  if (isAdmin) {
    productsThead.innerHTML = `
      <tr>
        <th class="col-drag" aria-label="Sırala"></th>
        <th>#</th>
        <th>Ürün</th>
        <th>Birim</th>
        <th>Fiyat</th>
        <th>Stok</th>
        <th>İşlemler</th>
      </tr>`;
  } else {
    productsThead.innerHTML = `
      <tr>
        <th>#</th>
        <th>Ürün</th>
        <th>Birim</th>
        <th>Fiyat</th>
        <th>Stok</th>
      </tr>`;
  }
}

function authHeaders() {
  return window.Auth.getAuthHeaders();
}

function stockBadge(stock) {
  if (stock <= 0) return `<span class="badge badge-out">${stock}</span>`;
  if (stock < 10) return `<span class="badge badge-low">${stock}</span>`;
  return `<span class="badge badge-ok">${stock}</span>`;
}

function productToPayload(product) {
  return {
    name: product.name,
    price: product.price,
    stock: product.stock,
    unit: product.unit || "Adet",
  };
}

function getFormProduct() {
  return productToPayload({
    name: document.getElementById("name").value.trim(),
    price: parseFloat(document.getElementById("price").value),
    stock: parseInt(document.getElementById("stock").value, 10),
    unit: document.getElementById("unit").value,
  });
}

function getFilteredProducts() {
  return productsCache;
}

function updateCount(filtered, total) {
  if (searchQuery) {
    productCount.textContent =
      total === 0
        ? "Aramanızla eşleşen ürün yok"
        : `${total} sonuç bulundu`;
    return;
  }

  productCount.textContent =
    total === 0 ? "Henüz ürün yok" : `${total} ürün kayıtlı`;
}

function buildProductsUrl() {
  const params = new URLSearchParams({
    page: currentPage.toString(),
    pageSize: pageSize.toString(),
  });

  if (searchQuery) params.set("search", searchQuery);

  return `${API_URL}?${params}`;
}

function updatePaginationControls() {
  const hasProducts = totalProductsCount > 0;
  paginationControls.hidden = !hasProducts;
  paginationInfo.textContent = hasProducts
    ? `Sayfa ${currentPage} / ${totalPages}`
    : "Sayfa 0 / 0";

  prevPageBtn.disabled = !hasProducts || currentPage <= 1;
  nextPageBtn.disabled = !hasProducts || currentPage >= totalPages;
}

function setFormMode(editId = null) {
  editingId = editId;
  const isEdit = editingId !== null;

  formTitle.textContent = isEdit ? "Ürünü düzenle" : "Yeni ürün ekle";
  submitBtnText.textContent = isEdit ? "Güncelle" : "Ürün ekle";
  cancelEditBtn.hidden = !isEdit;
  managementPanel.classList.toggle("panel-edit-mode", isEdit);
}

function resetForm() {
  form.reset();
  document.getElementById("unit").value = "Adet";
  setFormMode(null);
  hideMessage(formMessage);
}

function startEdit(product) {
  document.getElementById("name").value = product.name;
  document.getElementById("unit").value = product.unit || "Adet";
  document.getElementById("price").value = product.price;
  document.getElementById("stock").value = product.stock;
  setFormMode(product.id);
  hideMessage(formMessage);
  managementPanel.scrollIntoView({ behavior: "smooth", block: "start" });
}

async function parseApiError(response, fallback) {
  try {
    const data = await response.json();
    if (typeof data === "string") return data;
    if (data.message) return data.message;
    if (data.errors) {
      const validationMessages = Object.values(data.errors)
        .flat()
        .filter(Boolean);

      if (validationMessages.length) {
        return validationMessages.join(" ");
      }
    }
    if (data.title) return data.title;
  } catch {
    // JSON yoksa varsayılan mesaj
  }
  return fallback;
}

async function apiPut(id, product) {
  const response = await fetch(`${API_URL}/${id}`, {
    method: "PUT",
    headers: authHeaders(),
    body: JSON.stringify(product),
  });
  if (!response.ok) {
    const message = await parseApiError(
      response,
      `Güncelleme başarısız (${response.status})`
    );
    throw new Error(message);
  }
}

async function apiReorder(productIds) {
  const response = await fetch(`${API_URL}/reorder`, {
    method: "PUT",
    headers: authHeaders(),
    body: JSON.stringify({ productIds }),
  });
  if (!response.ok) {
    throw new Error(`Sıralama kaydedilemedi (${response.status})`);
  }
}

async function apiDelete(id) {
  const response = await fetch(`${API_URL}/${id}`, {
    method: "DELETE",
    headers: authHeaders(false),
  });
  if (!response.ok) {
    throw new Error(`Silme başarısız (${response.status})`);
  }
}

async function loadProducts() {
  hideMessage(loadError);
  updateTableHeader();
  const colspan = getColSpan();
  productsBody.innerHTML = `
    <tr>
      <td colspan="${colspan}" class="empty">
        <span class="spinner" aria-hidden="true"></span>
        Yükleniyor…
      </td>
    </tr>`;

  let loaded = false;
  try {
    const response = await fetch(buildProductsUrl());
    if (!response.ok) {
      throw new Error(`Liste alınamadı (${response.status})`);
    }

    const pageData = await response.json();
    productsCache = pageData.items ?? pageData.Items ?? [];
    currentPage = pageData.page ?? pageData.Page ?? currentPage;
    pageSize = pageData.pageSize ?? pageData.PageSize ?? pageSize;
    totalProductsCount = pageData.totalCount ?? pageData.TotalCount ?? 0;
    totalPages = pageData.totalPages ?? pageData.TotalPages ?? 1;
    loaded = true;
    applySearchAndRender();
  } catch (err) {
    productsCache = [];
    totalProductsCount = 0;
    totalPages = 1;
    updateCount(0, totalProductsCount);
    updatePaginationControls();
    productsBody.innerHTML =
      `<tr><td colspan="${getColSpan()}" class="empty">Ürünler yüklenemedi.</td></tr>`;
    showMessage(loadError, err.message, "error");
  }

}

function applySearchAndRender() {
  updateTableHeader();
  const filtered = getFilteredProducts();
  searchHint.hidden = !searchQuery;
  updateCount(filtered.length, totalProductsCount);
  updatePaginationControls();
  renderProducts(filtered);
}

function renderProducts(products) {
  const isAdmin = window.Auth?.isAdmin() ?? false;
  const dragEnabled = isAdmin && !searchQuery;
  const colspan = getColSpan();

  if (!products.length) {
    const msg = searchQuery
      ? "Aramanızla eşleşen ürün yok."
      : isAdmin
        ? "Henüz ürün yok. Ürün yönetimi panelinden ekleyin."
        : "Henüz ürün yok.";
    productsBody.innerHTML = `<tr><td colspan="${colspan}" class="empty">${msg}</td></tr>`;
    return;
  }

  productsBody.innerHTML = products
    .map((p) => {
      if (!isAdmin) {
        return `
    <tr data-id="${p.id}">
      <td class="id-cell">${p.id}</td>
      <td>${escapeHtml(p.name)}</td>
      <td><span class="unit-tag">${escapeHtml(p.unit || "Adet")}</span></td>
      <td class="price-cell">${priceFormatter.format(p.price)}</td>
      <td>${stockBadge(p.stock)}</td>
    </tr>`;
      }

      const isEditingRow = editingId === p.id;
      const handleClass = dragEnabled ? "drag-handle" : "drag-handle drag-disabled";
      const draggable = dragEnabled ? "true" : "false";

      return `
    <tr class="${isEditingRow ? "row-editing" : ""}" data-id="${p.id}">
      <td class="col-drag">
        <span class="${handleClass}" draggable="${draggable}" title="${dragEnabled ? "Sürükleyerek sırala" : "Aramayı temizleyin"}">⠿</span>
      </td>
      <td class="id-cell">${p.id}</td>
      <td>${escapeHtml(p.name)}</td>
      <td><span class="unit-tag">${escapeHtml(p.unit || "Adet")}</span></td>
      <td class="price-cell">${priceFormatter.format(p.price)}</td>
      <td>
        <div class="stock-controls">
          <button type="button" class="btn-icon" data-action="stock-dec" data-id="${p.id}"
            aria-label="Stok azalt" ${p.stock <= 0 ? "disabled" : ""}>−</button>
          ${stockBadge(p.stock)}
          <button type="button" class="btn-icon" data-action="stock-inc" data-id="${p.id}"
            aria-label="Stok artır">+</button>
        </div>
      </td>
      <td>
        <div class="row-actions">
          <button type="button" class="btn btn-sm btn-edit" data-action="edit" data-id="${p.id}">Düzenle</button>
          <button type="button" class="btn btn-sm btn-danger" data-action="delete" data-id="${p.id}">Sil</button>
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

function findProduct(id) {
  return productsCache.find((p) => p.id === id);
}

async function adjustStock(id, delta) {
  if (!requireAdmin()) return;

  const product = findProduct(id);
  if (!product) return;

  const newStock = Math.max(0, product.stock + delta);
  if (newStock === product.stock) return;

  const response = await fetch(`${API_URL}/${id}/stock?delta=${delta}`, {
    method: "PATCH",
    headers: authHeaders(false),
  });

  if (!response.ok) {
    const message = await parseApiError(
      response,
      `Stok güncellenemedi (${response.status})`
    );
    throw new Error(message);
  }

  await loadProducts();
}

async function deleteProduct(id) {
  if (!requireAdmin()) return;

  const product = findProduct(id);
  if (!product) return;

  const ok = confirm(`"${product.name}" silinsin mi?`);
  if (!ok) return;

  await apiDelete(id);

  if (editingId === id) {
    resetForm();
  }

  await loadProducts();
}

async function saveReorderFromDom() {
  if (!requireAdmin()) return;

  const orderedIds = [...productsBody.querySelectorAll("tr[data-id]")].map(
    (tr) => parseInt(tr.dataset.id, 10)
  );

  if (!orderedIds.length) return;

  await apiReorder(orderedIds);
  await loadProducts();
}

productsBody.addEventListener("dragstart", (e) => {
  if (!window.Auth?.isAdmin() || searchQuery) {
    e.preventDefault();
    if (!window.Auth?.isAdmin()) requireAdmin();
    return;
  }

  const handle = e.target.closest(".drag-handle");
  if (!handle || handle.classList.contains("drag-disabled")) {
    e.preventDefault();
    return;
  }

  draggedRow = handle.closest("tr");
  draggedRow.classList.add("dragging");
  e.dataTransfer.effectAllowed = "move";
  e.dataTransfer.setData("text/plain", draggedRow.dataset.id);
});

productsBody.addEventListener("dragend", () => {
  if (draggedRow) draggedRow.classList.remove("dragging");
  draggedRow = null;
  productsBody.querySelectorAll(".drag-over").forEach((el) => {
    el.classList.remove("drag-over");
  });
});

productsBody.addEventListener("dragover", (e) => {
  if (!draggedRow || searchQuery) return;
  e.preventDefault();

  const row = e.target.closest("tr[data-id]");
  if (!row || row === draggedRow) return;

  e.dataTransfer.dropEffect = "move";
  productsBody.querySelectorAll(".drag-over").forEach((el) => {
    el.classList.remove("drag-over");
  });
  row.classList.add("drag-over");
});

productsBody.addEventListener("drop", async (e) => {
  e.preventDefault();
  if (!draggedRow || searchQuery || !window.Auth?.isAdmin()) return;

  const targetRow = e.target.closest("tr[data-id]");
  targetRow?.classList.remove("drag-over");

  if (!targetRow || targetRow === draggedRow) return;

  const rows = [...productsBody.querySelectorAll("tr[data-id]")];
  const fromIndex = rows.indexOf(draggedRow);
  const toIndex = rows.indexOf(targetRow);

  if (fromIndex < toIndex) {
    targetRow.after(draggedRow);
  } else {
    targetRow.before(draggedRow);
  }

  try {
    await saveReorderFromDom();
  } catch (err) {
    showMessage(loadError, err.message, "error");
    await loadProducts();
  }
});

productsBody.addEventListener("click", async (e) => {
  const btn = e.target.closest("[data-action]");
  if (!btn || btn.disabled) return;

  const action = btn.dataset.action;
  const id = parseInt(btn.dataset.id, 10);
  const product = findProduct(id);

  try {
    if (action === "edit" && product) {
      if (!requireAdmin()) return;
      startEdit(product);
      return;
    }

    if (action === "delete") {
      await deleteProduct(id);
      return;
    }

    if (action === "stock-inc") {
      await adjustStock(id, 1);
      return;
    }

    if (action === "stock-dec") {
      await adjustStock(id, -1);
    }
  } catch (err) {
    showMessage(loadError, err.message, "error");
  }
});

form.addEventListener("submit", async (e) => {
  e.preventDefault();
  if (!requireAdmin()) return;
  hideMessage(formMessage);

  const submitBtn = form.querySelector('button[type="submit"]');
  submitBtn.disabled = true;

  const product = getFormProduct();

  try {
    if (editingId !== null) {
      await apiPut(editingId, product);
      resetForm();
      showMessage(formMessage, "Ürün güncellendi.", "success");
    } else {
      const response = await fetch(API_URL, {
        method: "POST",
        headers: authHeaders(),
        body: JSON.stringify(product),
      });

      if (!response.ok) {
        const message = await parseApiError(
          response,
          `Ürün eklenemedi (${response.status})`
        );
        throw new Error(message);
      }

      resetForm();
      showMessage(formMessage, "Ürün başarıyla eklendi.", "success");
      await loadProducts();
      if (!searchQuery) {
        currentPage = totalPages;
        applySearchAndRender();
      }
      return;
    }

    await loadProducts();
  } catch (err) {
    showMessage(formMessage, err.message, "error");
  } finally {
    submitBtn.disabled = false;
  }
});

searchInput.addEventListener("input", () => {
  searchQuery = searchInput.value.trim();
  currentPage = 1;
  window.clearTimeout(searchDebounceId);
  searchDebounceId = window.setTimeout(loadProducts, 250);
});

cancelEditBtn.addEventListener("click", resetForm);
refreshBtn.addEventListener("click", loadProducts);
prevPageBtn.addEventListener("click", () => {
  if (currentPage <= 1) return;
  currentPage -= 1;
  loadProducts();
});
nextPageBtn.addEventListener("click", () => {
  if (currentPage >= totalPages) return;
  currentPage += 1;
  loadProducts();
});
document.addEventListener("DOMContentLoaded", loadProducts);

window.addEventListener("auth-changed", (e) => {
  if (!e.detail.loggedIn || !window.Auth?.isAdmin()) {
    resetForm();
  }

  applySearchAndRender();
});
