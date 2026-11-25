// Дополнительные изображения, GLB-модель и реквизиты позиции МТР по QR-коду.
let inventoryBoxes = [];
let inventoryCurrent = null;
const photosMarkedForRemoval = new Set();
let removeCurrentModel = false;
let inventoryPhotoDrafts = [];
let nextInventoryPhotoKey = 1;
const inventoryModelTypes = ['cube', 'sphere', 'cylinder', 'cone', 'capsule'];
let inventoryFormOptions = {
  structuralDivisions: ['СКЛ_147_ЦАП'],
  warehouses: ['0147']
};

// Рисует редактируемые строки бланка; ID остаётся скрытым ключом при изменении названия.
function renderInventoryItemRows(items = []) {
  const body = document.getElementById('inventory-items-body');
  if (!body) return;
  body.innerHTML = '';
  const rows = items.length ? items : [{}];
  rows.forEach(item => addInventoryItemRow(item));
}

// Добавляет одну строку с реквизитами позиции и тестовой 3D-фигурой.
function addInventoryItemRow(item = {}) {
  const body = document.getElementById('inventory-items-body');
  if (!body) return;
  const modelType = inventoryModelTypes.includes(item.modelType) ? item.modelType : 'cube';
  const modelOptions = inventoryModelTypes.map(type =>
    `<option value="${type}" ${type === modelType ? 'selected' : ''}>${type}</option>`).join('');
  body.insertAdjacentHTML('beforeend', `
    <tr data-inventory-item data-id="${Number(item.id) || 0}">
      <td><input data-field="structuralDivision" type="text" list="inventory-division-options" value="${escapeHtml(item.structuralDivision || '')}" placeholder="СКЛ_147_ЦАП" maxlength="200"></td>
      <td><input data-field="warehouse" type="text" list="inventory-warehouse-options" value="${escapeHtml(item.warehouse || '')}" placeholder="0147" maxlength="200"></td>
      <td><input data-field="name" type="text" value="${escapeHtml(item.name || '')}" placeholder="Наименование" maxlength="200"></td>
      <td><input data-field="ensCode" type="text" value="${escapeHtml(item.ensCode || '')}" placeholder="ЕНС" maxlength="200"></td>
      <td><input data-field="accountCode" type="text" value="${escapeHtml(item.accountCode || '')}" placeholder="Счёт" maxlength="200"></td>
      <td><input data-field="mol" type="text" value="${escapeHtml(item.mol || '')}" placeholder="МОЛ" maxlength="200"></td>
      <td><input data-field="storageLocation" type="text" value="${escapeHtml(item.storageLocation || '')}" placeholder="1СТЕЛЛАЖ_2ПОЛКА" maxlength="200"></td>
      <td><input data-field="unit" type="text" value="${escapeHtml(item.unit || '')}" placeholder="ШТ" maxlength="200"></td>
      <td><input data-field="expectedQuantity" type="number" min="1" max="100000" step="1" value="${Number(item.expectedQuantity) || 1}"></td>
      <td><button type="button" class="btn btn-danger btn-sm" data-remove-inventory-item aria-label="Удалить позицию">×</button></td>
    </tr>`);
}

// Загружает сохранённые значения и объединяет их с начальными подсказками формы.
async function loadInventoryFormOptions() {
  try {
    const response = await fetch(`${API}/api/admin/inventory/form-options`);
    if (!response.ok) throw new Error('Не удалось загрузить подсказки');
    const data = await response.json();
    inventoryFormOptions = {
      structuralDivisions: [...new Set(['СКЛ_147_ЦАП', ...(data.structuralDivisions || [])])],
      warehouses: [...new Set(['0147', ...(data.warehouses || [])])]
    };
  } catch {
    // Начальные значения доступны и при временной ошибке загрузки справочника.
  }
  renderInventoryFormOptions();
}

// Обновляет подсказки в полях; свободный ввод остаётся разрешённым.
function renderInventoryFormOptions() {
  const setOptions = (id, values) => {
    const list = document.getElementById(id);
    if (list) list.innerHTML = values.map(value => `<option value="${escapeHtml(value)}"></option>`).join('');
  };
  setOptions('inventory-division-options', inventoryFormOptions.structuralDivisions);
  setOptions('inventory-warehouse-options', inventoryFormOptions.warehouses);
}

// Считывает значения из таблицы формы перед отправкой карточки на сервер.
function readInventoryItemRows() {
  const body = document.getElementById('inventory-items-body');
  if (!body) return [];
  return [...body.querySelectorAll('[data-inventory-item]')].map((row, index) => {
    const value = field => row.querySelector(`[data-field="${field}"]`)?.value?.trim() || '';
    return {
      id: Number(row.dataset.id) || 0,
      name: value('name'),
      expectedQuantity: Number(value('expectedQuantity')),
      structuralDivision: value('structuralDivision'),
      warehouse: value('warehouse'),
      ensCode: value('ensCode'),
      accountCode: value('accountCode'),
      mol: value('mol'),
      storageLocation: value('storageLocation'),
      unit: value('unit'),
      modelType: value('modelType') || 'cube',
      sortOrder: (index + 1) * 10
    };
  });
}

// Загружает список QR-кодов и связанные с ними позиции материальных ценностей.
async function loadInventoryBoxes() {
  try {
    const response = await fetch(`${API}/api/admin/inventory/boxes`);
    if (!response.ok) throw new Error('Сервер не вернул список позиций МТР');
    inventoryBoxes = await response.json();
    renderInventoryBoxes();
  } catch (error) {
    const tbody = document.getElementById('inventory-boxes-body');
    if (tbody) tbody.innerHTML = `<tr><td colspan="6" class="empty">Не удалось загрузить список: ${escapeHtml(error.message)}.</td></tr>`;
    toast('Не удалось загрузить позиции: ' + error.message, 'error');
  }
}

// Показывает QR-коды, позиции и действия для настройки медиафайлов.
function renderInventoryBoxes() {
  const tbody = document.getElementById('inventory-boxes-body');
  if (!tbody) return;
  const search = (document.getElementById('search-inventory').value || '').toLowerCase();
  const rows = inventoryBoxes.filter(item =>
    String(item.qrCode || '').toLowerCase().includes(search) || String(item.title || '').toLowerCase().includes(search) || String(item.objectName || '').toLowerCase().includes(search)
  );
  if (!rows.length) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty">Позиции не найдены. Сначала создайте QR-код во вкладке «QR-коды» или загрузите опись.</td></tr>';
    return;
  }
  tbody.innerHTML = rows.map(item => `
    <tr>
      <td><code>${escapeHtml(item.qrCode)}</code><div class="hint">${escapeHtml(item.objectName)}</div></td>
      <td>${item.boxId ? escapeHtml(item.title) : '<span class="badge badge-warning">Не настроена</span>'}</td>
      <td>${item.photoAvailable ? '<span class="badge badge-success">Добавлено</span>' : '<span class="badge badge-warning">Нет фото</span>'}</td>
      <td>${item.modelAvailable ? '<span class="badge badge-success">GLB добавлена</span>' : '<span class="badge badge-warning">Примитивы</span>'}</td>
      <td>${item.itemCount}</td>
      <td><div class="row-actions">
        <button class="btn btn-secondary btn-sm" onclick="openInventoryBox(${item.qrCodeId})">${item.boxId ? 'Настроить' : 'Добавить данные'}</button>
        ${item.boxId ? `<button class="btn btn-danger btn-sm" onclick="deleteInventoryBox(${item.qrCodeId})">Удалить позицию</button>` : ''}
      </div></td>
    </tr>`).join('');
}

// Предлагает QR-коды, для которых ещё не настроены дополнительные данные.
async function openNewInventoryBox() {
  try {
    await loadInventoryBoxes();
    const available = inventoryBoxes.filter(item => !item.boxId);
    const select = document.getElementById('new-inventory-qr');
    select.innerHTML = available.map(item => `<option value="${Number(item.qrCodeId)}">${escapeHtml(item.qrCode)}${item.objectName ? ` — ${escapeHtml(item.objectName)}` : ''}</option>`).join('');
    if (!available.length) {
      toast('Нет QR-кодов без дополнительных данных. Сначала добавьте QR-код или загрузите опись.', 'error');
      return;
    }
    document.getElementById('modal-new-inventory-box').classList.add('open');
  } catch (error) { toast('Не удалось получить список QR-кодов: ' + error.message, 'error'); }
}

// Открывает форму дополнительных данных для выбранного QR-кода.
function createInventoryBoxFromQr() {
  const qrCodeId = Number(document.getElementById('new-inventory-qr').value);
  if (!qrCodeId) return toast('Выберите QR-код.', 'error');
  closeModal('modal-new-inventory-box');
  openInventoryBox(qrCodeId);
}

// Удаляет позицию вместе с QR-кодом и связанными данными после подтверждения.
async function deleteInventoryBox(qrCodeId) {
  const box = inventoryBoxes.find(item => Number(item.qrCodeId) === Number(qrCodeId));
  if (!box?.boxId) return toast('Настроенная позиция не найдена.', 'error');
  const title = box.title || box.objectName || box.qrCode;
  if (!confirm(`Удалить материальную ценность «${title}» (${box.qrCode}) вместе с QR-кодом, фотографиями и историей? Это действие нельзя отменить.`)) return;
  try {
    const response = await fetch(`${API}/api/admin/inventory/boxes/${qrCodeId}`, { method: 'DELETE' });
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Не удалось удалить позицию');
    toast(data.message || 'Позиция и её история удалены');
    await Promise.all([loadInventoryBoxes(), typeof loadInventoryRuns === 'function' && loadInventoryRuns()]);
  } catch (error) { toast('Ошибка удаления позиции: ' + error.message, 'error'); }
}

// Загружает данные выбранной материальной ценности и заполняет форму редактирования.
async function openInventoryBox(qrCodeId) {
  try {
    const response = await fetch(`${API}/api/admin/inventory/boxes/${qrCodeId}`);
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Не удалось загрузить позицию');
    await loadInventoryFormOptions();
    inventoryCurrent = data;
    document.getElementById('inventory-qr-id').value = qrCodeId;
    document.getElementById('inventory-qr-code').value = data.qrCode;
    document.getElementById('inventory-title').value = data.title;
    renderInventoryItemRows(data.items || []);
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

// Отмечает существующую фотографию для удаления или отменяет это действие.
function toggleRemovePhoto(page, button) {
  if (photosMarkedForRemoval.has(page)) photosMarkedForRemoval.delete(page);
  else photosMarkedForRemoval.add(page);
  if (button) {
    button.classList.toggle('is-selected', photosMarkedForRemoval.has(page));
    button.textContent = photosMarkedForRemoval.has(page) ? 'Оставить фото' : 'Удалить фото';
  }
}

// Добавляет в форму поле для ещё одного изображения МТР.
function addInventoryPhotoInput() {
  const allPages = [...(inventoryCurrent?.photos || []).map(photo => Number(photo.pageNumber) || 0), ...inventoryPhotoDrafts.map(draft => draft.pageNumber)];
  const nextPage = Math.max(0, ...allPages) + 1;
  inventoryPhotoDrafts.push({ key: nextInventoryPhotoKey++, pageNumber: nextPage, caption: `Изображение содержимого ${nextPage}`, photoUrl: '', previewUrl: '', file: null, isExisting: false });
  renderInventoryPhotoInputs();
}

// Удаляет черновое поле или переключает признак удаления сохранённой фотографии.
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

// Отрисовывает фото, поля выбора файлов и обработчики этих полей.
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

// Проверяет выбранное изображение и сохраняет его как черновик загрузки.
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

// Переключает признак удаления текущей GLB-модели.
function toggleRemoveModel() {
  removeCurrentModel = !removeCurrentModel;
  const button = document.getElementById('inventory-model-remove');
  if (button) {
    button.classList.toggle('is-selected', removeCurrentModel);
    button.textContent = removeCurrentModel ? 'Модель будет удалена' : 'Удалить 3D-модель';
  }
}

// Проверяет форму, кодирует новые файлы и отправляет данные МТР в API.
async function saveInventoryBox() {
  const qrCodeId = document.getElementById('inventory-qr-id').value;
  const title = document.getElementById('inventory-title').value.trim();
  const items = readInventoryItemRows();
  if (!title) return toast('Укажите наименование МТР', 'error');
  if (!items.length) return toast('Добавьте хотя бы одну позицию', 'error');
  if (items.some(item => !item.name || item.expectedQuantity < 1)) return toast('Для каждой позиции укажите название и целое количество > 0', 'error');
  if (new Set(items.map(item => item.name.toLocaleLowerCase('ru-RU'))).size !== items.length) return toast('Удалите повторяющиеся названия позиций', 'error');

  const body = {
    title, photos: [], removePhotoPages: Array.from(photosMarkedForRemoval), removeModel: removeCurrentModel,
    items
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
    const response = await fetch(`${API}/api/admin/inventory/boxes/${qrCodeId}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const data = await response.json();
    if (!response.ok) return toast(data.message || 'Не удалось сохранить данные МТР', 'error');
    toast(data.message || 'Данные МТР сохранены');
    await loadInventoryFormOptions();
    closeModal('modal-inventory');
    await loadInventoryBoxes();
  } catch (error) { toast('Ошибка сети: ' + error.message, 'error'); }
}

// Экспортируем только функции, к которым обращаются кнопки и другие модули.
window.loadInventoryBoxes = loadInventoryBoxes;
window.getInventoryBoxes = () => inventoryBoxes;
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
window.addInventoryItemRow = addInventoryItemRow;
window.loadInventoryFormOptions = loadInventoryFormOptions;

document.getElementById('search-inventory')?.addEventListener('input', renderInventoryBoxes);
document.getElementById('inventory-items-body')?.addEventListener('click', event => {
  const removeButton = event.target.closest('[data-remove-inventory-item]');
  if (removeButton) removeButton.closest('[data-inventory-item]')?.remove();
});
document.getElementById('inventory-model')?.addEventListener('change', event => {
  const file = event.target.files[0];
  if (!file) return;
  removeCurrentModel = false;
  const btn = document.getElementById('inventory-model-remove');
  if (btn) { btn.classList.remove('is-selected'); btn.textContent = 'Удалить 3D-модель'; }
  const hint = document.getElementById('inventory-model-hint');
  if (hint) hint.textContent = `${file.name} · ${Math.ceil(file.size / 1024)} КБ · будет загружена при сохранении`;
});
