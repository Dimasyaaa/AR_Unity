// Раздел создания и обслуживания QR-этикеток материальных ценностей.
let qrData = [];
let qrLegacyEdit = false;
let qrPreviewTimer = null;
let qrPreviewRequestId = 0;
let qrPreviewObjectUrl = null;

// Разбирает установленный формат QR: ЕНС/СКК#учётное_количество.
function parseInventoryQrPayload(value) {
  const match = String(value || '').trim().match(/^([^#\r\n]{1,100})#([1-9]\d{0,5})$/);
  if (!match) return null;
  const accountingQuantity = Number(match[2]);
  if (!Number.isSafeInteger(accountingQuantity) || accountingQuantity > 100000) return null;
  return { ensCode: match[1].trim(), accountingQuantity };
}

// Загружает QR-коды с сервера, чтобы показать их реквизиты и действия.
async function loadQr() {
  try {
    const response = await fetch(`${API}/api/admin/qr`);
    if (!response.ok) throw new Error('Сервер не вернул список QR-кодов.');
    qrData = await response.json();
    renderQr();
  } catch (error) {
    toast('Ошибка загрузки: ' + error.message, 'error');
  }
}

// Отображает код идентификации, учётное количество и содержимое этикетки.
function renderQr() {
  const search = (document.getElementById('search-qr')?.value || '').toLowerCase();
  const tbody = document.getElementById('qr-body');
  if (!tbody) return;

  const filtered = qrData.filter(qr =>
    String(qr.code || '').toLowerCase().includes(search) ||
    String(qr.objectName || '').toLowerCase().includes(search));

  if (filtered.length === 0) {
    tbody.innerHTML = '<tr><td colspan="7" class="empty">Нет QR-кодов</td></tr>';
    return;
  }

  tbody.innerHTML = filtered.map(qr => {
    const payload = parseInventoryQrPayload(qr.code);
    return `
      <tr>
        <td>${qr.id}</td>
        <td>${payload ? escapeHtml(payload.ensCode) : '<span class="badge badge-warning">Старый формат</span>'}</td>
        <td>${payload ? payload.accountingQuantity : '—'}</td>
        <td><code>${escapeHtml(qr.code)}</code></td>
        <td>${qr.isActive ? '<span class="badge badge-success">Активен</span>' : '<span class="badge badge-warning">Отключен</span>'}</td>
        <td>${formatDate(qr.createdAt)}</td>
        <td>
          <div class="actions">
          <button class="btn btn-secondary btn-sm" onclick="printQr(${qr.id})" ${qr.isActive ? '' : 'disabled title="Отключённый QR нельзя использовать"'}>Печать</button>
            <button class="btn btn-secondary btn-sm" onclick="editQr(${qr.id})">Изменить</button>
            <button class="btn btn-danger btn-sm" onclick="deleteQr(${qr.id})">Удалить</button>
          </div>
        </td>
      </tr>`;
  }).join('');
}

// Показывает поля новой этикетки или безопасный режим для старой записи.
function openQrModal(qr = null) {
  const payload = qr ? parseInventoryQrPayload(qr.code) : null;
  qrLegacyEdit = Boolean(qr && !payload);

  document.getElementById('qr-modal-title').textContent = qr
    ? (qrLegacyEdit ? 'Старая QR-запись' : 'Изменение QR-этикетки')
    : 'Новая QR-этикетка МТР';
  document.getElementById('qr-id').value = qr ? qr.id : '';
  document.getElementById('qr-inventory-fields').hidden = qrLegacyEdit;
  document.getElementById('qr-legacy-fields').hidden = !qrLegacyEdit;
  document.getElementById('qr-ens-code').value = payload?.ensCode || '';
  document.getElementById('qr-accounting-quantity').value = payload?.accountingQuantity || 1;
  document.getElementById('qr-legacy-code').value = qrLegacyEdit ? qr.code : '';
  document.getElementById('qr-legacy-object-name').value = qrLegacyEdit ? (qr.objectName || '') : '';
  document.getElementById('qr-isActive').checked = qr ? qr.isActive : true;
  updateQrPayloadPreview();
  document.getElementById('modal-qr').classList.add('open');
}

// Обновляет текст и запрашивает изображение этикетки для формы.
function updateQrPayloadPreview() {
  const ensCode = document.getElementById('qr-ens-code')?.value.trim() || '';
  const quantity = Number(document.getElementById('qr-accounting-quantity')?.value);
  const preview = document.getElementById('qr-payload-preview');
  const image = document.getElementById('qr-preview-image');
  const printButton = document.getElementById('qr-preview-print');
  if (!preview) return;

  const validEns = ensCode.length > 0 && ensCode.length <= 100 && !/[#\r\n]/.test(ensCode);
  const validQuantity = Number.isInteger(quantity) && quantity >= 1 && quantity <= 100000;
  const payload = validEns && validQuantity ? `${ensCode}#${quantity}` : '';
  preview.textContent = payload || 'Введите ЕНС/СКК и количество от 1 до 100000';

  clearTimeout(qrPreviewTimer);
  const requestId = ++qrPreviewRequestId;
  if (!payload) {
    if (image) {
      image.hidden = true;
      image.removeAttribute('src');
    }
    if (printButton) printButton.disabled = true;
    if (qrPreviewObjectUrl) {
      URL.revokeObjectURL(qrPreviewObjectUrl);
      qrPreviewObjectUrl = null;
    }
    return;
  }

  if (image) image.hidden = true;
  if (printButton) printButton.disabled = true;
  qrPreviewTimer = setTimeout(async () => {
    const query = new URLSearchParams({ ensCode, accountingQuantity: String(quantity) });
    try {
      const response = await fetch(`${API}/api/admin/qr/preview?${query}`);
      if (!response.ok) throw new Error('Сервер не смог сформировать QR-изображение.');
      const imageBlob = await response.blob();
      if (requestId !== qrPreviewRequestId) return;

      if (qrPreviewObjectUrl) URL.revokeObjectURL(qrPreviewObjectUrl);
      qrPreviewObjectUrl = URL.createObjectURL(imageBlob);
      if (image) {
        image.onload = () => {
          if (requestId === qrPreviewRequestId) image.hidden = false;
        };
        image.src = qrPreviewObjectUrl;
      }
      if (printButton) printButton.disabled = false;
    } catch (error) {
      if (requestId === qrPreviewRequestId) toast('Ошибка предпросмотра QR: ' + error.message, 'error');
    }
  }, 250);
}

// Сохраняет новую этикетку либо редактирует существующий QR-код.
async function saveQr() {
  const id = document.getElementById('qr-id').value;
  const isActive = document.getElementById('qr-isActive').checked;
  let url;
  let method;
  let body;

  if (qrLegacyEdit) {
    body = {
      code: document.getElementById('qr-legacy-code').value,
      objectName: document.getElementById('qr-legacy-object-name').value,
      isActive
    };
    url = `${API}/api/admin/qr/${id}`;
    method = 'PUT';
  } else {
    const ensCode = document.getElementById('qr-ens-code').value.trim();
    const accountingQuantity = Number(document.getElementById('qr-accounting-quantity').value);
    if (!ensCode || ensCode.length > 100 || /[#\r\n]/.test(ensCode))
      return toast('Укажите корректный ЕНС/СКК без символа #.', 'error');
    if (!Number.isInteger(accountingQuantity) || accountingQuantity < 1 || accountingQuantity > 100000)
      return toast('Количество по учёту должно быть от 1 до 100000.', 'error');

    body = { ensCode, accountingQuantity };
    url = id ? `${API}/api/admin/qr/${id}` : `${API}/api/admin/qr/inventory`;
    method = id ? 'PUT' : 'POST';
    if (id) {
      body = {
        code: `${ensCode}#${accountingQuantity}`,
        objectName: `МТР ${ensCode}`,
        isActive
      };
    }
  }

  try {
    const response = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
    const result = await response.json();
    if (!response.ok) return toast(result.message || 'Не удалось сохранить QR-код.', 'error');

    toast(result.message || 'QR-код сохранён.');
    closeModal('modal-qr');
    await loadQr();
  } catch (error) {
    toast('Ошибка сети: ' + error.message, 'error');
  }
}

// Открывает подготовленную этикетку в отдельном окне и передаёт её браузеру на печать.
function openQrPrintWindow({ imageUrl, identityText, quantityText, payload }) {
  const printWindow = window.open('', '_blank', 'width=480,height=560');
  if (!printWindow) return toast('Разрешите всплывающие окна для печати этикетки.', 'error');
  printWindow.opener = null;

  const documentToPrint = printWindow.document;
  documentToPrint.open();
  documentToPrint.write(`<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Этикетка МТР</title><style>
    *{box-sizing:border-box}body{margin:0;padding:24px;font:16px Arial,sans-serif;color:#17243a}.label{width:320px;margin:0 auto;padding:20px;border:1px solid #9aa8b8;border-radius:12px;text-align:center}.label img{display:block;width:220px;height:220px;object-fit:contain;margin:0 auto 12px}.identity{font-size:20px;font-weight:700;overflow-wrap:anywhere}.quantity{margin-top:6px;color:#40516a}.payload{margin-top:10px;font:13px monospace;overflow-wrap:anywhere}.print-button{display:block;margin:16px auto 0;padding:10px 18px;border:0;border-radius:8px;background:#1769aa;color:white;font-weight:700;cursor:pointer}@media print{body{padding:0}.label{border:0;margin:0 auto}.print-button{display:none}}
    </style></head><body></body></html>`);
  documentToPrint.close();

  const label = documentToPrint.createElement('main');
  label.className = 'label';
  const image = documentToPrint.createElement('img');
  image.alt = 'QR-код материальной ценности';
  image.src = imageUrl;
  const identity = documentToPrint.createElement('div');
  identity.className = 'identity';
  identity.textContent = identityText;
  const quantity = documentToPrint.createElement('div');
  quantity.className = 'quantity';
  quantity.textContent = quantityText;
  const rawPayload = documentToPrint.createElement('div');
  rawPayload.className = 'payload';
  rawPayload.textContent = payload;
  const button = documentToPrint.createElement('button');
  button.className = 'print-button';
  button.textContent = 'Печать';
  button.addEventListener('click', () => printWindow.print());
  label.append(image, identity, quantity, rawPayload, button);
  documentToPrint.body.append(label);
  image.addEventListener('load', () => printWindow.focus(), { once: true });
  image.addEventListener('error', () => toast('Сервер не смог создать изображение QR.', 'error'), { once: true });
}

// Печатает уже сохранённую этикетку из списка QR-кодов.
function printQr(id) {
  const qr = qrData.find(item => Number(item.id) === Number(id));
  if (!qr) return toast('QR-код не найден в списке.', 'error');

  const parsedPayload = parseInventoryQrPayload(qr.code);
  openQrPrintWindow({
    imageUrl: `${API}/api/admin/qr/${Number(qr.id)}/image`,
    identityText: parsedPayload ? parsedPayload.ensCode : (qr.objectName || 'Старая QR-запись'),
    quantityText: parsedPayload ? `Количество по учёту: ${parsedPayload.accountingQuantity}` : 'Старая этикетка',
    payload: qr.code
  });
}

// Формирует адрес предпросмотра из текущих полей и сразу открывает печатную этикетку.
function printQrPreview() {
  const ensCode = document.getElementById('qr-ens-code')?.value.trim() || '';
  const accountingQuantity = Number(document.getElementById('qr-accounting-quantity')?.value);
  const payload = parseInventoryQrPayload(`${ensCode}#${accountingQuantity}`);
  if (!payload) return toast('Сначала укажите корректные ЕНС/СКК и количество.', 'error');

  const query = new URLSearchParams({ ensCode: payload.ensCode, accountingQuantity: String(payload.accountingQuantity) });
  openQrPrintWindow({
    imageUrl: `${API}/api/admin/qr/preview?${query}`,
    identityText: payload.ensCode,
    quantityText: `Количество по учёту: ${payload.accountingQuantity}`,
    payload: `${payload.ensCode}#${payload.accountingQuantity}`
  });
}

// Удаляет QR-код и обновляет списки, которые зависят от него.
async function deleteQr(id) {
  if (!confirm('Удалить QR-код и связанные с ним записи? Действие может затронуть историю.')) return;
  try {
    const response = await fetch(`${API}/api/admin/qr/${id}`, { method: 'DELETE' });
    const result = await response.json();
    if (!response.ok) return toast(result.message || 'Ошибка удаления.', 'error');
    toast(result.message || 'QR-код удалён.');
    if (typeof loadInventoryBoxes === 'function') await loadInventoryBoxes();
    if (typeof loadInventoryRuns === 'function') await loadInventoryRuns();
    if (typeof loadSessions === 'function') await loadSessions();
    await loadQr();
  } catch (error) {
    toast('Не удалось удалить QR-код: ' + error.message, 'error');
  }
}

// Открывает ранее созданный QR; записи старого формата остаются доступными.
window.loadQr = loadQr;
window.editQr = id => {
  const qr = qrData.find(item => Number(item.id) === Number(id));
  if (qr) openQrModal(qr);
};
window.saveQr = saveQr;
window.printQr = printQr;
window.printQrPreview = printQrPreview;
window.deleteQr = deleteQr;

document.getElementById('search-qr')?.addEventListener('input', renderQr);
document.getElementById('qr-ens-code')?.addEventListener('input', updateQrPayloadPreview);
document.getElementById('qr-accounting-quantity')?.addEventListener('input', updateQrPayloadPreview);
window.addEventListener('beforeunload', () => {
  if (qrPreviewObjectUrl) URL.revokeObjectURL(qrPreviewObjectUrl);
});
