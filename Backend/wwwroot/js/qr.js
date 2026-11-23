// Управление QR-кодами
let qrData = [];

async function loadQr() {
  try {
    const res = await fetch(`${API}/api/admin/qr`);
    qrData = await res.json();
    renderQr();
  } catch (e) { toast('Ошибка загрузки: ' + e.message, 'error'); }
}

function renderQr() {
  const search = document.getElementById('search-qr').value.toLowerCase();
  const tbody = document.getElementById('qr-body');
  const filtered = qrData.filter(q => q.code.toLowerCase().includes(search) || q.objectName.toLowerCase().includes(search));
  
  if (filtered.length === 0) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty">Нет QR-кодов</td></tr>';
    return;
  }
  tbody.innerHTML = filtered.map(q => `
    <tr>
      <td>${q.id}</td>
      <td><code>${escapeHtml(q.code)}</code></td>
      <td>${escapeHtml(q.objectName)}</td>
      <td>${q.isActive ? '<span class="badge badge-success">Активен</span>' : '<span class="badge badge-warning">Отключен</span>'}</td>
      <td>${formatDate(q.createdAt)}</td>
      <td>
        <div class="actions">
          <button class="btn btn-secondary btn-sm" onclick="editQr(${q.id})">Изменить</button>
          <button class="btn btn-danger btn-sm" onclick="deleteQr(${q.id})">Удалить</button>
        </div>
      </td>
    </tr>
  `).join('');
}

function openQrModal(qr = null) {
  document.getElementById('qr-modal-title').textContent = qr ? 'Редактирование QR-кода' : 'Новый QR-код';
  document.getElementById('qr-id').value = qr ? qr.id : '';
  document.getElementById('qr-code').value = qr ? qr.code : '';
  document.getElementById('qr-objectName').value = qr ? qr.objectName : '';
  document.getElementById('qr-isActive').checked = qr ? qr.isActive : true;
  document.getElementById('modal-qr').classList.add('open');
}

async function saveQr() {
  const id = document.getElementById('qr-id').value;
  const body = {
    code: document.getElementById('qr-code').value.trim(),
    objectName: document.getElementById('qr-objectName').value.trim(),
    isActive: document.getElementById('qr-isActive').checked
  };
  if (!body.code || !body.objectName) return toast('Заполните все поля', 'error');
  const url = id ? `${API}/api/admin/qr/${id}` : `${API}/api/admin/qr`;
  try {
    const res = await fetch(url, { method: id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const data = await res.json();
    if (!res.ok) return toast(data.message || 'Ошибка', 'error');
    toast(data.message);
    closeModal('modal-qr');
    loadQr();
  } catch (e) { toast('Ошибка сети', 'error'); }
}

async function deleteQr(id) {
  if (!confirm('Удалить QR-код? Если он обозначает коробку, вместе с ней будут удалены изображения, перечень и история. Действие необратимо.')) return;
  try {
    const res = await fetch(`${API}/api/admin/qr/${id}`, { method: 'DELETE' });
    const data = await res.json();
    if (!res.ok) return toast(data.message || 'Ошибка', 'error');
    toast(data.message);
    // Обновляем зависимые данные
    if (typeof loadInventoryBoxes === 'function') await loadInventoryBoxes();
    if (typeof loadInventoryRuns === 'function') await loadInventoryRuns();
    if (typeof loadSessions === 'function') await loadSessions();
    loadQr();
  } catch (error) { toast('Не удалось удалить QR-код: ' + error.message, 'error'); }
}

window.qrData = qrData;
window.loadQr = loadQr;
window.editQr = id => { const q = qrData.find(x => x.id === id); if (q) openQrModal(q); };
window.saveQr = saveQr;
window.deleteQr = deleteQr;

document.getElementById('search-qr')?.addEventListener('input', renderQr);