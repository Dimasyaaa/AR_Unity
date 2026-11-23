// Управление коробками, фото, 3D-моделями и назначениями
let inventoryBoxes = [];
let inventoryCurrent = null;
const photosMarkedForRemoval = new Set();
let removeCurrentModel = false;
let inventoryPhotoDrafts = [];
let nextInventoryPhotoKey = 1;

function parseInventoryItemLines(value) {
  return value.split(/\r?\n/).map(line => {
    const trimmed = line.trim();
    if (!trimmed) return null;
    const [rawName, rawQuantity, rawModelType] = trimmed.split('|');
    const quantity = rawQuantity === undefined ? 1 : Number(rawQuantity.trim());
    const modelType = (rawModelType || 'cube').trim().toLowerCase();
    return {
      name: rawName.trim(),
      expectedQuantity: Number.isInteger(quantity) && quantity > 0 ? quantity : 0,
      modelType: ['cube', 'sphere', 'cylinder', 'cone', 'capsule'].includes(modelType) ? modelType : 'cube'
    };
  }).filter(Boolean);
}

function renderInventoryItemsPreview() {
  const preview = document.getElementById('inventory-items-preview');
  if (!preview) return;
  const items = parseInventoryItemLines(document.getElementById('inventory-items').value);
  preview.innerHTML = items.map(item => `<li>${escapeHtml(item.name)} <span class="hint">· по учёту: ${item.expectedQuantity} · 3D: ${escapeHtml(item.modelType)}</span></li>`).join('') || '<li class="checklist-preview-empty">Позиции появятся здесь в указанном порядке</li>';
}

async function loadInventoryBoxes() {
  try {
    const response = await fetch(`${API}/api/admin/inventory/boxes`);
    if (!response.ok) throw new Error('Сервер не вернул список шкафов');
    inventoryBoxes = await response.json();
    renderInventoryBoxes();
  } catch (error) {
    const tbody = document.getElementById('inventory-boxes-body');
    if (tbody) tbody.innerHTML = `<tr><td colspan="6" class="empty">Не удалось загрузить список: ${escapeHtml(error.message)}.</td></tr>`;
    toast('Не удалось загрузить шкафы: ' + error.message, 'error');
  }
}

function renderInventoryBoxes() {
  const tbody = document.getElementById('inventory-boxes-body');
  if (!tbody) return;
  const search = (document.getElementById('search-inventory').value || '').toLowerCase();
  const rows = inventoryBoxes.filter(item =>
    item.qrCode.toLowerCase().includes(search) || item.title.toLowerCase().includes(search) || item.objectName.toLowerCase().includes(search)
  );
  if (!rows.length) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty">Шкафы не найдены. Сначала создайте QR-код во вкладке «QR-коды».</td></tr>';
    return;
  }
  tbody.innerHTML = rows.map(item => `
    <tr>
      <td><code>${escapeHtml(item.qrCode)}</code><div class="hint">${escapeHtml(item.objectName)}</div></td>
      <td>${item.cabinetId ? escapeHtml(item.title) : '<span class="badge badge-warning">Не настроен</span>'}</td>
      <td>${item.photoAvailable ? '<span class="badge badge-success">Добавлено</span>' : '<span class="badge badge-warning">Нет фото</span>'}</td>
      <td>${item.itemCount}</td>
      <td><div class="row-actions">
        <button class="btn btn-secondary btn-sm" onclick="openInventoryBox(${item.qrCodeId})">${item.cabinetId ? 'Настроить' : 'Создать шкаф'}</button>
        ${item.cabinetId ? `<button class="btn btn-danger btn-sm" onclick="deleteInventoryBox(${item.qrCodeId})">Удалить шкаф</button>` : ''}
      </div></td>
    </tr>`).join('');
}

async function openNewInventoryBox() {
  try {
    await loadInventoryBoxes();
    const available = inventoryBoxes.filter(item => !item.cabinetId);
    const select = document.getElementById('new-inventory-qr');
    select.innerHTML = available.map(item => `<option value="${Number(item.qrCodeId)}">${escapeHtml(item.qrCode)}${item.objectName ? ` — ${escapeHtml(item.objectName)}` : ''}</option>`).join('');
    if (!available.length) {
      toast('Нет свободных QR-кодов. Сначала добавьте QR-код во вкладке «QR-коды».', 'error');
      return;
    }
    document.getElementById('modal-new-inventory-box').classList.add('open');
  } catch (error) { toast('Не удалось получить список QR-кодов: ' + error.message, 'error'); }
}

function createInventoryBoxFromQr() {
  const qrCodeId = Number(document.getElementById('new-inventory-qr').value);
  if (!qrCodeId) return toast('Выберите QR-код.', 'error');
  closeModal('modal-new-inventory-box');
  openInventoryBox(qrCodeId);
}

async function deleteInventoryBox(qrCodeId) {
  const box = inventoryBoxes.find(item => Number(item.qrCodeId) === Number(qrCodeId));
  if (!box?.cabinetId) return toast('Настроенный шкаф не найден.', 'error');
  const title = box.title || box.objectName || box.qrCode;
  if (!confirm(`Удалить шкаф «${title}» (${box.qrCode}) вместе с QR-кодом, фотографиями, перечнем и всей историей? Это действие нельзя отменить.`)) return;
  try {
    const response = await fetch(`${API}/api/admin/inventory/boxes/${qrCodeId}`, { method: 'DELETE' });
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Не удалось удалить шкаф');
    toast(data.message || 'Шкаф и его история удалены');
    await Promise.all([loadInventoryBoxes(), typeof loadInventoryRuns === 'function' && loadInventoryRuns()]);
  } catch (error) { toast('Ошибка удаления шкафа: ' + error.message, 'error'); }
}

async function openInventoryBox(qrCodeId) {
  try {
    const response = await fetch(`${API}/api/admin/inventory/boxes/${qrCodeId}`);
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Не удалось загрузить шкаф');
    inventoryCurrent = data;
    document.getElementById('inventory-qr-id').value = qrCodeId;
    document.getElementById('inventory-qr-code').value = data.qrCode;
    document.getElementById('inventory-title').value = data.title;
    document.getElementById('inventory-items').value = data.items.map(item => `${item.name} | ${Number(item.expectedQuantity) || 1} | ${item.modelType || 'cube'}`).join('\n');
    renderInventoryItemsPreview();
    photosMarkedForRemoval.clear();
    inventoryPhotoDrafts.forEach(draft => { if (draft.previewUrl) URL.revokeObjectURL(draft.previewUrl); });
    inventoryPhotoDrafts = (data.photos || []).filter(photo => Number(photo.pageNumber) > 0).map(photo => ({
      key: nextInventoryPhotoKey++, pageNumber: Number(photo.pageNumber), caption: photo.caption || `Изображение содержимого ${photo.pageNumber}`,
      photoUrl: photo.photoUrl, previewUrl: '', file: null, isExisting: true
    }));
    renderInventoryPhotoInputs();
    const modelInput = document.getElementById('inventory-model');
    if (modelInput) modelInput.value = '';
    removeCurrentModel = false;
    const hint = document.getElementById('inventory-model-hint');
    if (hint) hint.textContent = data.modelAvailable ? `${data.modelFileName || 'GLB-модель загружена'} · выберите файл для замены` : 'GLB не загружена. В карточке будут показаны тестовые примитивы.';
    const modelRemove = document.getElementById('inventory-model-remove');
    if (modelRemove) {
      modelRemove.hidden = !data.modelAvailable;
      modelRemove.classList.remove('is-selected');
      modelRemove.textContent = 'Удалить 3D-модель';
    }
    document.getElementById('modal-inventory').classList.add('open');
  } catch (error) { toast(error.message, 'error'); }
}

function toggleRemovePhoto(page, button) {
  if (photosMarkedForRemoval.has(page)) photosMarkedForRemoval.delete(page);
  else photosMarkedForRemoval.add(page);
  if (button) {
    button.classList.toggle('is-selected', photosMarkedForRemoval.has(page));
    button.textContent = photosMarkedForRemoval.has(page) ? 'Оставить фото' : 'Удалить фото';
  }
}

function addInventoryPhotoInput() {
  const allPages = [...(inventoryCurrent?.photos || []).map(photo => Number(photo.pageNumber) || 0), ...inventoryPhotoDrafts.map(draft => draft.pageNumber)];
  const nextPage = Math.max(0, ...allPages) + 1;
  inventoryPhotoDrafts.push({ key: nextInventoryPhotoKey++, pageNumber: nextPage, caption: `Изображение содержимого ${nextPage}`, photoUrl: '', previewUrl: '', file: null, isExisting: false });
  renderInventoryPhotoInputs();
}

function removeInventoryPhotoInput(key) {
  const draft = inventoryPhotoDrafts.find(item => item.key === key);
  if (!draft) return;
  if (draft.isExisting) {
    toggleRemovePhoto(draft.pageNumber, document.querySelector(`[data-photo-remove="${key}"]`));
  } else {
    if (draft.previewUrl) URL.revokeObjectURL(draft.previewUrl);
    inventoryPhotoDrafts = inventoryPhotoDrafts.filter(item => item.key !== key);
  }
  renderInventoryPhotoInputs();
}

function renderInventoryPhotoInputs() {
  const list = document.getElementById('inventory-photo-list');
  if (!list) return;
  list.innerHTML = inventoryPhotoDrafts.map(draft => {
    const marked = photosMarkedForRemoval.has(draft.pageNumber);
    const preview = draft.previewUrl || draft.photoUrl;
    return `<article class="photo-upload-card">
      <strong>Изображение ${draft.pageNumber}</strong>
      <div class="photo-preview">${preview ? `<img src="${escapeHtml(preview)}" alt="Фото ${draft.pageNumber}">` : 'Фото не загружено'}</div>
      <input type="file" accept="image/jpeg,image/png,image/webp" data-photo-input="${draft.key}">
      <div class="hint">${escapeHtml(draft.file?.name || draft.caption || 'Выберите изображение')}</div>
      <button type="button" class="btn btn-secondary btn-sm photo-remove ${marked ? 'is-selected' : ''}" data-photo-remove="${draft.key}">${draft.isExisting ? (marked ? 'Оставить фото' : 'Удалить фото') : 'Убрать строку'}</button>
    </article>`;
  }).join('');

  list.querySelectorAll('[data-photo-input]').forEach(input => {
    input.addEventListener('change', event => selectInventoryPhoto(Number(event.target.dataset.photoInput), event));
  });
  list.querySelectorAll('[data-photo-remove]').forEach(button => {
    button.addEventListener('click', () => removeInventoryPhotoInput(Number(button.dataset.photoRemove)));
  });
}

function selectInventoryPhoto(key, event) {
  const draft = inventoryPhotoDrafts.find(item => item.key === key);
  const file = event.target.files[0];
  if (!draft || !file) return;
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) { event.target.value = ''; return toast('Выберите JPEG, PNG или WebP', 'error'); }
  if (file.size > 8 * 1024 * 1024) { event.target.value = ''; return toast('Фотография должна быть не больше 8 МБ', 'error'); }
  if (draft.previewUrl) URL.revokeObjectURL(draft.previewUrl);
  draft.file = file;
  draft.previewUrl = URL.createObjectURL(file);
  photosMarkedForRemoval.delete(draft.pageNumber);
  renderInventoryPhotoInputs();
}

function toggleRemoveModel() {
  removeCurrentModel = !removeCurrentModel;
  const button = document.getElementById('inventory-model-remove');
  if (button) {
    button.classList.toggle('is-selected', removeCurrentModel);
    button.textContent = removeCurrentModel ? 'Модель будет удалена' : 'Удалить 3D-модель';
  }
}

async function saveInventoryBox() {
  const qrCodeId = document.getElementById('inventory-qr-id').value;
  const title = document.getElementById('inventory-title').value.trim();
  const items = parseInventoryItemLines(document.getElementById('inventory-items').value);
  if (!title) return toast('Укажите название шкафа', 'error');
  if (!items.length) return toast('Добавьте хотя бы одну позицию', 'error');
  if (items.some(item => !item.name || item.expectedQuantity < 1)) return toast('Для каждой позиции укажите название и целое количество > 0', 'error');
  if (new Set(items.map(item => item.name.toLocaleLowerCase('ru-RU'))).size !== items.length) return toast('Удалите повторяющиеся названия позиций', 'error');

  const previous = new Map((inventoryCurrent?.items || []).map(item => [item.name.toLocaleLowerCase('ru-RU'), item]));
  const body = {
    title, photos: [], removePhotoPages: Array.from(photosMarkedForRemoval), removeModel: removeCurrentModel,
    items: items.map((item, index) => ({
      id: previous.get(item.name.toLocaleLowerCase('ru-RU'))?.id || 0, name: item.name, expectedQuantity: item.expectedQuantity,
      modelType: item.modelType || previous.get(item.name.toLocaleLowerCase('ru-RU'))?.modelType || 'cube', sortOrder: (index + 1) * 10
    }))
  };
  for (const draft of inventoryPhotoDrafts) {
    if (!draft.file) continue;
    try { body.photos.push({ pageNumber: draft.pageNumber, caption: draft.caption, contentType: draft.file.type, imageBase64: await readImageAsBase64(draft.file) }); }
    catch (error) { return toast(error.message, 'error'); }
  }

  try {
    const modelFile = document.getElementById('inventory-model').files[0];
    if (modelFile) {
      if (!modelFile.name.toLocaleLowerCase().endsWith('.glb')) return toast('Выберите файл в формате GLB', 'error');
      if (modelFile.size === 0 || modelFile.size > 20 * 1024 * 1024) return toast('Размер GLB должен быть от 1 байта до 20 МБ', 'error');
      try {
        body.modelBase64 = await readFileAsBase64(modelFile);
        body.modelFileName = modelFile.name;
        body.removeModel = false;
      } catch (error) { return toast(error.message, 'error'); }
    }
    
    const response = await fetch(`${API}/api/admin/inventory/cabinets/${qrCodeId}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const data = await response.json();
    if (!response.ok) return toast(data.message || 'Не удалось сохранить шкаф', 'error');
    toast(data.message || 'Данные шкафа сохранены');
    closeModal('modal-inventory');
    await loadInventoryBoxes();
  } catch (error) { toast('Ошибка сети: ' + error.message, 'error'); }
}

// Экспорт в глобальную область
window.inventoryBoxes = inventoryBoxes;
window.loadInventoryBoxes = loadInventoryBoxes;
window.openNewInventoryBox = openNewInventoryBox;
window.createInventoryBoxFromQr = createInventoryBoxFromQr;
window.deleteInventoryBox = deleteInventoryBox;
window.openInventoryBox = openInventoryBox;
window.toggleRemovePhoto = toggleRemovePhoto;
window.addInventoryPhotoInput = addInventoryPhotoInput;
window.removeInventoryPhotoInput = removeInventoryPhotoInput;
window.selectInventoryPhoto = selectInventoryPhoto;
window.toggleRemoveModel = toggleRemoveModel;
window.saveInventoryBox = saveInventoryBox;
window.parseInventoryItemLines = parseInventoryItemLines;
window.renderInventoryItemsPreview = renderInventoryItemsPreview;

document.getElementById('search-inventory')?.addEventListener('input', renderInventoryBoxes);
document.getElementById('inventory-items')?.addEventListener('input', renderInventoryItemsPreview);
document.getElementById('inventory-model')?.addEventListener('change', event => {
  const file = event.target.files[0];
  if (!file) return;
  removeCurrentModel = false;
  const btn = document.getElementById('inventory-model-remove');
  if (btn) { btn.classList.remove('is-selected'); btn.textContent = 'Удалить 3D-модель'; }
  const hint = document.getElementById('inventory-model-hint');
  if (hint) hint.textContent = `${file.name} · ${Math.ceil(file.size / 1024)} КБ · будет загружена при сохранении`;
});