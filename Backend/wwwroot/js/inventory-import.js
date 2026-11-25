// Загрузка описи, проверка распознанных строк и запуск общей проверки в AR-приложении.
let selectedInventoryFile = null;
let inventoryPreviewRows = [];
let currentInventory = null;
let inventoryImportInProgress = false;

// Загружает состояние общей описи и блокирует повторный запуск, пока она не завершена.
async function loadCurrentInventory() {
  const title = document.getElementById('current-inventory-title');
  const details = document.getElementById('current-inventory-details');
  const startButton = document.getElementById('open-inventory-import');
  if (!title || !details) return;

  try {
    const response = await fetch(`${API}/api/admin/inventory/current`, { cache: 'no-store' });
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Сервер не вернул состояние описи.');

    currentInventory = data.current || null;
    if (!currentInventory) {
      title.textContent = 'Активной описи пока нет';
      details.textContent = 'Прикрепите файл Excel, чтобы передать список материальных ценностей в AR-приложение.';
      if (startButton) {
        startButton.disabled = false;
        startButton.textContent = 'Начать инвентаризацию';
      }
      return;
    }

    const done = Number(currentInventory.completedPositionCount) || 0;
    const total = Number(currentInventory.expectedPositionCount) || 0;
    title.textContent = currentInventory.title || 'Инвентаризация выполняется';
    details.textContent = `${currentInventory.fileName || 'Excel-опись'} · Проверено ${done} из ${total} позиций`;
    if (startButton) {
      startButton.disabled = true;
      startButton.textContent = 'Опись уже передана в AR-приложение';
    }
  } catch (error) {
    currentInventory = null;
    title.textContent = 'Не удалось проверить состояние описи';
    details.textContent = error.message;
    if (startButton) startButton.disabled = true;
  }
}

// Открывает шаг выбора файла, только если другой общей инвентаризации ещё нет.
function showInventoryImport() {
  if (currentInventory) {
    toast('Сначала завершите текущую инвентаризацию.', 'error');
    return;
  }

  const workflow = document.getElementById('inventory-import-workflow');
  if (workflow) {
    workflow.hidden = false;
    workflow.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
}

// Скрывает выбор описи без изменений в базе данных.
function hideInventoryImport() {
  const workflow = document.getElementById('inventory-import-workflow');
  if (workflow) workflow.hidden = true;
  clearInventoryFile();
}

// Отправляет файл на предварительную проверку и показывает распознанные строки.
async function previewInventoryFile(file) {
  if (!file) return;
  if (!file.name.toLowerCase().endsWith('.xlsx')) {
    toast('Выберите опись Excel в формате .xlsx.', 'error');
    clearInventoryFile();
    return;
  }
  if (file.size > 20 * 1024 * 1024) {
    toast('Размер файла не должен превышать 20 МБ.', 'error');
    clearInventoryFile();
    return;
  }

  selectedInventoryFile = file;
  inventoryPreviewRows = [];
  const status = document.getElementById('inventory-import-status');
  const preview = document.getElementById('inventory-import-preview');
  if (status) status.textContent = 'Проверяем файл и читаем позиции…';
  if (preview) preview.hidden = true;

  const form = new FormData();
  form.append('file', file);
  try {
    const response = await fetch(`${API}/api/admin/inventory/import/preview`, { method: 'POST', body: form });
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Не удалось прочитать Excel-файл.');

    inventoryPreviewRows = Array.isArray(data.rows) ? data.rows : [];
    document.getElementById('inventory-import-file-name').textContent = data.sourceFileName || file.name;
    document.getElementById('inventory-import-position-count').textContent = `Найдено строк МТР: ${inventoryPreviewRows.length}`;
    renderInventoryImportRows();
    if (preview) preview.hidden = false;
    if (updateInventoryImportValidation()) {
      if (status) status.textContent = 'Опись проверена. Передаём её в AR-приложение…';
      await startInventoryFromFile();
    }
  } catch (error) {
    if (status) status.textContent = error.message;
    toast('Не удалось загрузить опись: ' + error.message, 'error');
  }
}

// Рисует таблицу для проверки: строки остаются такими, как они были в Excel.
function renderInventoryImportRows() {
  const tbody = document.getElementById('inventory-import-rows');
  if (!tbody) return;

  tbody.innerHTML = inventoryPreviewRows.map(row => {
    const quantity = row.accountingQuantity;
    const hasQuantity = Number.isInteger(quantity) && quantity > 0 && quantity <= 100000;
    const hasEns = String(row.ensCode || '').trim().length > 0;
    const qrPayload = hasEns && hasQuantity ? `${row.ensCode}#${quantity}` : 'Нужны ЕНС и количество';
    return `<tr data-import-row="${Number(row.sourceRowNumber)}">
      <td>${Number(row.sourceRowNumber)}</td>
      <td>${escapeHtml(row.structuralDivision)}</td>
      <td>${escapeHtml(row.warehouse)}</td>
      <td><strong>${escapeHtml(row.itemName)}</strong></td>
      <td><code>${escapeHtml(row.ensCode)}</code></td>
      <td>${escapeHtml(row.accountCode)}</td>
      <td>${escapeHtml(row.mol)}</td>
      <td>${escapeHtml(row.storageLocation)}</td>
      <td>${escapeHtml(row.unit)}</td>
      <td class="import-quantity-cell ${hasQuantity ? '' : 'is-invalid'}">${hasQuantity ? quantity : 'Не указано'}</td>
      <td><code class="import-qr-value ${hasEns && hasQuantity ? '' : 'is-invalid'}">${escapeHtml(qrPayload)}</code></td>
    </tr>`;
  }).join('');
}

// Не запускает опись, если по строке нельзя однозначно собрать содержимое QR.
function updateInventoryImportValidation() {
  const status = document.getElementById('inventory-import-status');

  const payloads = inventoryPreviewRows.map(row => {
    const quantity = row.accountingQuantity;
    return String(row.ensCode || '').trim() && Number.isInteger(quantity) && quantity > 0 && quantity <= 100000
      ? `${String(row.ensCode).trim()}#${quantity}`
      : null;
  });
  const counts = new Map();
  payloads.filter(Boolean).forEach(payload => counts.set(payload, (counts.get(payload) || 0) + 1));
  const duplicatePayloads = new Set([...counts].filter(([, count]) => count > 1).map(([payload]) => payload));

  let invalidRows = 0;
  inventoryPreviewRows.forEach((row, index) => {
    const payload = payloads[index];
    const isDuplicate = payload && duplicatePayloads.has(payload);
    const rowElement = document.querySelector(`[data-import-row="${Number(row.sourceRowNumber)}"]`);
    const qrElement = rowElement?.querySelector('.import-qr-value');
    if (isDuplicate) {
      invalidRows++;
      if (qrElement) {
        qrElement.textContent = 'Повтор QR в описи';
        qrElement.classList.add('is-invalid');
      }
    } else if (!payload) {
      invalidRows++;
    }
  });

  const isValid = inventoryPreviewRows.length > 0 && invalidRows === 0 && !currentInventory && !inventoryImportInProgress;
  if (invalidRows > 0 && status)
    status.textContent = `Исправьте опись: обязательные данные отсутствуют или QR повторяется в ${invalidRows} строках.`;
  return isValid;
}

// Создаёт общую инвентаризацию и передаёт её список QR-кодов приложению.
async function startInventoryFromFile() {
  if (!selectedInventoryFile || inventoryImportInProgress) return;
  if (!updateInventoryImportValidation()) return;

  inventoryImportInProgress = true;
  const status = document.getElementById('inventory-import-status');
  if (status) status.textContent = 'Передаём опись в AR-приложение…';
  const form = new FormData();
  form.append('file', selectedInventoryFile);
  try {
    const response = await fetch(`${API}/api/admin/inventory/import/start`, { method: 'POST', body: form });
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Сервер не смог запустить инвентаризацию.');

    toast(data.message || 'Инвентаризация передана в AR-приложение.');
    const workflow = document.getElementById('inventory-import-workflow');
    if (workflow) workflow.hidden = true;
    clearInventoryFile();
    await loadCurrentInventory();
  } catch (error) {
    if (status) status.textContent = error.message;
    toast('Не удалось запустить инвентаризацию: ' + error.message, 'error');
  } finally {
    inventoryImportInProgress = false;
    updateInventoryImportValidation();
  }
}

// Сбрасывает выбранный файл и его предварительный просмотр.
function clearInventoryFile() {
  selectedInventoryFile = null;
  inventoryPreviewRows = [];
  const input = document.getElementById('inventory-excel-file');
  const preview = document.getElementById('inventory-import-preview');
  const status = document.getElementById('inventory-import-status');
  if (input) input.value = '';
  if (preview) preview.hidden = true;
  if (status) status.textContent = 'Опись ещё не выбрана.';
}

// Обработчики, вызываемые кнопками и полем выбора в HTML.
window.loadCurrentInventory = loadCurrentInventory;
window.showInventoryImport = showInventoryImport;
window.hideInventoryImport = hideInventoryImport;
window.previewInventoryFile = previewInventoryFile;
window.startInventoryFromFile = startInventoryFromFile;
window.clearInventoryFile = clearInventoryFile;
