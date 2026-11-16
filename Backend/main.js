// AR Inventory — панель администратора
const API = ''; // тот же хост, что и страница

// Навигация по вкладкам
document.querySelectorAll('.tab').forEach(tab => {
  tab.onclick = () => {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    tab.classList.add('active');
    const name = tab.dataset.tab;
    ['users', 'qr', 'inventory', 'runs', 'sessions'].forEach(p => {
      document.getElementById('panel-' + p).style.display = (p === name) ? 'block' : 'none';
    });
    if (name === 'users') loadUsers();
    if (name === 'qr') loadQr();
    if (name === 'inventory') loadInventoryCabinets();
    if (name === 'runs') loadInventoryRuns();
    if (name === 'sessions') loadSessions();
  };
});

// Уведомления
function toast(msg, type = 'success') {
  const el = document.getElementById('toast');
  el.textContent = msg;
  el.className = 'toast show ' + type;
  setTimeout(() => el.classList.remove('show'), 2500);
}

// Модальные окна
function closeModal(id) {
  document.getElementById(id).classList.remove('open');
}

// ОТДЕЛЫ (выпадающий список)
let departmentsData = [];

async function loadDepartments() {
  try {
    const res = await fetch(`${API}/api/admin/departments`);
    departmentsData = await res.json();
  } catch (e) {
    departmentsData = [];
  }
}

function fillDepartmentSelect(selected = null) {
  const select = document.getElementById('user-department');
  select.innerHTML = '';

  // Существующие отделы
  departmentsData.forEach(d => {
    const opt = document.createElement('option');
    opt.value = d;
    opt.textContent = d;
    select.appendChild(opt);
  });

  // Если редактируемый отдел отсутствует в списке — добавляем его
  if (selected && !departmentsData.includes(selected)) {
    const opt = document.createElement('option');
    opt.value = selected;
    opt.textContent = selected;
    select.appendChild(opt);
  }

  // Пункт "новый отдел"
  const newOpt = document.createElement('option');
  newOpt.value = '__new__';
  newOpt.textContent = '-- Новый отдел --';
  select.appendChild(newOpt);

  select.value = selected || (departmentsData.length ? departmentsData[0] : '__new__');
  toggleNewDepartment(select.value === '__new__');
}

function toggleNewDepartment(show) {
  document.getElementById('user-department-new-group').style.display = show ? 'block' : 'none';
}

document.getElementById('user-department').addEventListener('change', e => {
  toggleNewDepartment(e.target.value === '__new__');
});

// USERS
let usersData = [];

async function loadUsers() {
  try {
    const res = await fetch(`${API}/api/admin/users`);
    usersData = await res.json();
    renderUsers();
  } catch (e) {
    toast('Ошибка загрузки: ' + e.message, 'error');
  }
}

function renderUsers() {
  const search = document.getElementById('search-users').value.toLowerCase();
  const tbody = document.getElementById('users-body');
  const filtered = usersData.filter(u =>
    u.fullName.toLowerCase().includes(search) || u.department.toLowerCase().includes(search)
  );

  if (filtered.length === 0) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty">Нет пользователей</td></tr>';
    return;
  }

  tbody.innerHTML = filtered.map(u => `
    <tr>
      <td>${u.id}</td>
      <td>${escapeHtml(u.fullName)}</td>
      <td>${escapeHtml(u.department)}</td>
      <td>${u.isActive
          ? '<span class="badge badge-success">Активен</span>'
          : '<span class="badge badge-warning">Отключен</span>'}</td>
      <td>${formatDate(u.createdAt)}</td>
      <td>
        <div class="actions">
          <button class="btn btn-secondary btn-sm" onclick="toggleUser(${u.id})">
            ${u.isActive ? 'Отключить' : 'Включить'}
          </button>
          <button class="btn btn-secondary btn-sm" onclick="editUser(${u.id})">Изменить</button>
          <button class="btn btn-danger btn-sm" onclick="deleteUser(${u.id})">Удалить</button>
        </div>
      </td>
    </tr>
  `).join('');
}
document.getElementById('search-users').oninput = renderUsers;

async function openUserModal(user = null) {
  document.getElementById('user-modal-title').textContent =
    user ? 'Редактирование пользователя' : 'Новый пользователь';
  document.getElementById('user-id').value = user ? user.id : '';
  document.getElementById('user-fullName').value = user ? user.fullName : '';
  document.getElementById('user-password').value = '';
  document.getElementById('user-isActive').checked = user ? user.isActive : true;
  document.getElementById('user-department-new').value = '';

  await loadDepartments();
  fillDepartmentSelect(user ? user.department : null);

  document.getElementById('modal-user').classList.add('open');
}

function editUser(id) {
  const user = usersData.find(u => u.id === id);
  if (user) openUserModal(user);
}

async function saveUser() {
  const id = document.getElementById('user-id').value;
  const select = document.getElementById('user-department');
  const department = select.value === '__new__'
    ? document.getElementById('user-department-new').value.trim()
    : select.value;

  const body = {
    fullName: document.getElementById('user-fullName').value.trim(),
    department: department,
    password: document.getElementById('user-password').value,
    isActive: document.getElementById('user-isActive').checked
  };

  if (!body.fullName || !body.department) return toast('Заполните ФИО и отдел', 'error');
  if (!id && !body.password) return toast('Пароль обязателен при создании', 'error');

  const url = id ? `${API}/api/admin/users/${id}` : `${API}/api/admin/users`;
  const method = id ? 'PUT' : 'POST';

  try {
    const res = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
    const data = await res.json();
    if (!res.ok) return toast(data.message || 'Ошибка', 'error');
    toast(data.message);
    closeModal('modal-user');
    loadUsers();
  } catch (e) {
    toast('Ошибка сети', 'error');
  }
}

async function toggleUser(id) {
  const res = await fetch(`${API}/api/admin/users/${id}/toggle`, { method: 'PATCH' });
  const data = await res.json();
  if (!res.ok) return toast(data.message || 'Ошибка', 'error');
  loadUsers();
}

async function deleteUser(id) {
  if (!confirm('Удалить пользователя? Все его сессии будут удалены каскадно.')) return;
  const res = await fetch(`${API}/api/admin/users/${id}`, { method: 'DELETE' });
  const data = await res.json();
  if (!res.ok) return toast(data.message || 'Ошибка', 'error');
  toast(data.message);
  loadUsers();
}

// QR
let qrData = [];

async function loadQr() {
  try {
    const res = await fetch(`${API}/api/admin/qr`);
    qrData = await res.json();
    renderQr();
  } catch (e) {
    toast('Ошибка загрузки: ' + e.message, 'error');
  }
}

function renderQr() {
  const search = document.getElementById('search-qr').value.toLowerCase();
  const tbody = document.getElementById('qr-body');
  const filtered = qrData.filter(q =>
    q.code.toLowerCase().includes(search) || q.objectName.toLowerCase().includes(search)
  );

  if (filtered.length === 0) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty">Нет QR-кодов</td></tr>';
    return;
  }

  tbody.innerHTML = filtered.map(q => `
    <tr>
      <td>${q.id}</td>
      <td><code>${escapeHtml(q.code)}</code></td>
      <td>${escapeHtml(q.objectName)}</td>
      <td>${q.isActive
          ? '<span class="badge badge-success">Активен</span>'
          : '<span class="badge badge-warning">Отключен</span>'}</td>
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
document.getElementById('search-qr').oninput = renderQr;

function openQrModal(qr = null) {
  document.getElementById('qr-modal-title').textContent =
    qr ? 'Редактирование QR-кода' : 'Новый QR-код';
  document.getElementById('qr-id').value = qr ? qr.id : '';
  document.getElementById('qr-code').value = qr ? qr.code : '';
  document.getElementById('qr-objectName').value = qr ? qr.objectName : '';
  document.getElementById('qr-isActive').checked = qr ? qr.isActive : true;
  document.getElementById('modal-qr').classList.add('open');
}

function editQr(id) {
  const qr = qrData.find(q => q.id === id);
  if (qr) openQrModal(qr);
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
  const method = id ? 'PUT' : 'POST';

  try {
    const res = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
    const data = await res.json();
    if (!res.ok) return toast(data.message || 'Ошибка', 'error');
    toast(data.message);
    closeModal('modal-qr');
    loadQr();
  } catch (e) {
    toast('Ошибка сети', 'error');
  }
}

async function deleteQr(id) {
  if (!confirm('Удалить QR-код? Если он привязан к шкафу, вместе с ним будут удалены шкаф, фотографии, чек-лист и вся история инвентаризаций. У обычного QR события журнала сохранятся без связи с кодом. Действие необратимо.')) return;
  try {
    const res = await fetch(`${API}/api/admin/qr/${id}`, { method: 'DELETE' });
    const data = await res.json();
    if (!res.ok) return toast(data.message || 'Ошибка', 'error');
    toast(data.message);
    await Promise.all([loadQr(), loadInventoryCabinets(), loadInventoryRuns(), loadSessions()]);
  } catch (error) {
    toast('Не удалось удалить QR-код: ' + error.message, 'error');
  }
}

// SESSIONS
let adminSessionsData = [];
let sessionsLoadInFlight = false;
const actionPresentation = {
  login: { label: 'Вход', badge: 'badge-info' },
  logout: { label: 'Выход', badge: 'badge-info' },
  calc: { label: 'Расчёт', badge: 'badge-info' },
  scan: { label: 'Сканирование QR', badge: 'badge-success' },
  inventory: { label: 'Инвентаризация', badge: 'badge-inventory' },
  error: { label: 'Ошибка', badge: 'badge-danger' }
};

async function loadSessions(silent = false) {
  if (sessionsLoadInFlight) return;
  sessionsLoadInFlight = true;
  try {
    const res = await fetch(`${API}/api/admin/sessions`);
    if (!res.ok) throw new Error('Сервер не смог загрузить журнал');
    const data = await res.json();
    adminSessionsData = Array.isArray(data) ? data : [];

    // Обновляем состояние кнопки очистки
    const clearBtn = document.getElementById('clear-sessions-btn');
    if (clearBtn) clearBtn.disabled = adminSessionsData.length === 0;
    renderSessions();
  } catch (e) {
    if (!silent) toast('Ошибка загрузки журнала: ' + e.message, 'error');
  } finally {
    sessionsLoadInFlight = false;
  }
}

function renderSessions() {
  const tbody = document.getElementById('sessions-body');
  const actionFilter = document.getElementById('sessions-action-filter').value;
  const search = document.getElementById('sessions-search').value.trim().toLocaleLowerCase('ru-RU');
  const rows = adminSessionsData.filter(session => {
    const action = String(session.action || '').toLowerCase();
    const matchesAction = actionFilter === 'all' || action === actionFilter;
    const matchesSearch = !search || [session.userFullName, session.userDepartment, session.qrCode,
      session.qrObjectName, session.comment].some(value => String(value || '').toLocaleLowerCase('ru-RU').includes(search));
    return matchesAction && matchesSearch;
  });

  if (!rows.length) {
    const message = adminSessionsData.length ? 'По выбранному фильтру записей нет' : 'Нет событий';
    tbody.innerHTML = `<tr><td colspan="8" class="empty">${message}</td></tr>`;
    return;
  }

  tbody.innerHTML = rows.map(s => {
    const action = String(s.action || '').toLowerCase();
    const presentation = actionPresentation[action] || { label: s.action || 'Неизвестное действие', badge: 'badge-warning' };
    return `
      <tr>
        <td>${formatDateTime(s.actionTime)}</td>
        <td>${escapeHtml(s.userFullName || '--')}</td>
        <td>${escapeHtml(s.userDepartment || '--')}</td>
        <td><span class="badge ${presentation.badge}">${escapeHtml(presentation.label)}</span></td>
        <td>${s.qrCode ? '<code>' + escapeHtml(s.qrCode) + '</code>' : '--'}</td>
        <td>${s.qrObjectName ? escapeHtml(s.qrObjectName) : '--'}</td>
        <td>${s.comment ? escapeHtml(s.comment) : '--'}</td>
        <td><div class="actions"><button class="btn btn-danger btn-sm" onclick="deleteSession(${Number(s.id)})">Удалить</button></div></td>
      </tr>
    `;
  }).join('');
}

document.getElementById('sessions-action-filter').addEventListener('change', renderSessions);
document.getElementById('sessions-search').addEventListener('input', renderSessions);

// Результаты инвентаризации, отправленные приложением через API
let inventoryRunsData = [];
let inventoryRunsLoadInFlight = false;

async function loadInventoryRuns(silent = false) {
  if (inventoryRunsLoadInFlight) return;
  inventoryRunsLoadInFlight = true;
  const tbody = document.getElementById('inventory-runs-body');
  try {
    const res = await fetch(`${API}/api/admin/inventory/runs`);
    if (!res.ok) throw new Error('Сервер не смог загрузить проверки');
    const data = await res.json();
    inventoryRunsData = Array.isArray(data) ? data : (data.items || []);
    renderInventoryRuns();
  } catch (error) {
    tbody.innerHTML = `<tr><td colspan="6" class="empty">${escapeHtml(error.message)}</td></tr>`;
    if (!silent) toast('Не удалось загрузить результаты инвентаризации', 'error');
  } finally {
    inventoryRunsLoadInFlight = false;
  }
}

function renderInventoryRuns() {
  const search = document.getElementById('search-runs').value.trim().toLocaleLowerCase('ru-RU');
  const groupBy = document.getElementById('group-runs-by').value;
  const tbody = document.getElementById('inventory-runs-body');
  const rows = inventoryRunsData.filter(run =>
    [run.userFullName, run.userDepartment, run.cabinetTitle, run.qrCode]
      .some(value => String(value || '').toLocaleLowerCase('ru-RU').includes(search))
  );
  if (!rows.length) {
    tbody.innerHTML = `<tr><td colspan="6" class="empty">${inventoryRunsData.length ? 'По запросу ничего не найдено' : 'Пока нет завершённых проверок. Отправьте инвентаризацию из приложения — она появится здесь.'}</td></tr>`;
    return;
  }

  const renderRunRow = run => `
    <tr>
      <td>${formatDateTime(run.completedAt)}</td>
      <td>${escapeHtml(run.userFullName || '--')}</td>
      <td>${escapeHtml(run.userDepartment || '--')}</td>
      <td><strong>${escapeHtml(run.cabinetTitle || '--')}</strong><div class="sub-cell"><code>${escapeHtml(run.qrCode || '--')}</code></div></td>
      <td><span class="badge badge-success">Есть: ${Number(run.presentCount) || 0}</span> <span class="badge badge-danger">Нет: ${Number(run.missingCount) || 0}</span></td>
      <td><button class="btn btn-secondary btn-sm" onclick="showInventoryRun(${Number(run.id)})">Подробнее</button></td>
    </tr>
  `;

  if (groupBy === 'none') {
    tbody.innerHTML = rows.map(renderRunRow).join('');
    return;
  }

  const groups = new Map();
  rows.forEach(run => {
    let key;
    let title;
    let subtitle = '';
    if (groupBy === 'date') {
      key = title = formatDate(run.completedAt);
    } else if (groupBy === 'user') {
      title = run.userFullName || 'Пользователь не указан';
      subtitle = run.userDepartment || '';
      key = `${title}\u0000${subtitle}`;
    } else {
      title = run.cabinetTitle || 'Шкаф не указан';
      subtitle = run.qrCode || '';
      key = `${title}\u0000${subtitle}`;
    }

    if (!groups.has(key)) groups.set(key, { title, subtitle, runs: [] });
    groups.get(key).runs.push(run);
  });

  tbody.innerHTML = Array.from(groups.values()).map(group => `
    <tr class="run-group-row"><td colspan="6">
      <div class="run-group-heading"><strong>${escapeHtml(group.title)}</strong><span>${formatRunCount(group.runs.length)}</span></div>
      ${group.subtitle ? `<div class="run-group-subtitle">${escapeHtml(group.subtitle)}</div>` : ''}
    </td></tr>
    ${group.runs.map(renderRunRow).join('')}
  `).join('');
}

function formatRunCount(count) {
  const lastTwo = count % 100;
  const lastOne = count % 10;
  const word = lastTwo >= 11 && lastTwo <= 14 ? 'проверок' :
    lastOne === 1 ? 'проверка' :
    lastOne >= 2 && lastOne <= 4 ? 'проверки' : 'проверок';
  return `${count} ${word}`;
}

document.getElementById('search-runs').addEventListener('input', renderInventoryRuns);
document.getElementById('group-runs-by').addEventListener('change', renderInventoryRuns);

const inventoryItemsInput = document.getElementById('inventory-items');
inventoryItemsInput.addEventListener('input', renderInventoryItemsPreview);

function renderInventoryItemsPreview() {
  const preview = document.getElementById('inventory-items-preview');
  const names = inventoryItemsInput.value.split(/\r?\n/).map(name => name.trim()).filter(Boolean);
  preview.innerHTML = names.map(name => `<li>${escapeHtml(name)}</li>`).join('') || '<li class="checklist-preview-empty">Предметы появятся здесь в указанном порядке</li>';
}

async function showInventoryRun(id) {
  try {
    const res = await fetch(`${API}/api/admin/inventory/runs/${id}`);
    const run = await res.json();
    if (!res.ok) return toast(run.message || 'Не удалось открыть проверку', 'error');
    document.getElementById('inventory-run-title').textContent = run.cabinetTitle || 'Результат инвентаризации';
    document.getElementById('inventory-run-meta').innerHTML = `
      <div><span>Сотрудник</span><strong>${escapeHtml(run.userFullName || '--')}</strong></div>
      <div><span>Отдел</span><strong>${escapeHtml(run.userDepartment || '--')}</strong></div>
      <div><span>QR-код</span><strong><code>${escapeHtml(run.qrCode || '--')}</code></strong></div>
      <div><span>Завершена</span><strong>${formatDateTime(run.completedAt)}</strong></div>`;
    document.getElementById('inventory-run-items').innerHTML = (run.items || []).map((item, index) => `
      <tr><td class="run-item-number">${index + 1}</td><td>${escapeHtml(item.itemName || '--')}</td><td><span class="badge ${item.status === 'present' ? 'badge-success' : 'badge-danger'}">${item.status === 'present' ? 'Есть' : 'Нет'}</span></td><td>${escapeHtml(item.comment || '—')}</td></tr>
    `).join('') || '<tr><td colspan="4" class="empty">В этой проверке нет пунктов.</td></tr>';
    document.getElementById('modal-inventory-run').classList.add('open');
  } catch (error) {
    toast('Ошибка загрузки проверки: ' + error.message, 'error');
  }
}

// Удаление одной записи журнала
async function deleteSession(id) {
  if (!confirm('Удалить эту запись из журнала?')) return;
  try {
    const res = await fetch(`${API}/api/admin/sessions/${id}`, { method: 'DELETE' });
    const data = await res.json();
    if (!res.ok) return toast(data.message || 'Ошибка', 'error');
    toast(data.message);
    loadSessions();
  } catch (e) {
    toast('Ошибка сети', 'error');
  }
}

// Очистка всего журнала
async function clearAllSessions() {
  if (!confirm('Удалить ВСЕ записи журнала? Действие необратимо.')) return;
  try {
    const res = await fetch(`${API}/api/admin/sessions`, { method: 'DELETE' });
    const data = await res.json();
    if (!res.ok) return toast(data.message || 'Ошибка', 'error');
    toast(data.message);
    loadSessions();
  } catch (e) {
    toast('Ошибка сети', 'error');
  }
}

// Утилиты
function formatDate(d) {
  if (!d) return '--';
  return new Date(d).toLocaleDateString('ru-RU');
}

function formatDateTime(d) {
  if (!d) return '--';
  return new Date(d).toLocaleString('ru-RU');
}

function escapeHtml(s) {
  if (s == null) return '';
  return String(s).replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  }[c]));
}

// ШКАФЫ И ЧЕК-ЛИСТЫ
let inventoryCabinets = [];
let inventoryCurrent = null;
const photosMarkedForRemoval = new Set();

async function loadInventoryCabinets() {
  try {
    const response = await fetch(`${API}/api/admin/inventory/cabinets`);
    if (!response.ok) throw new Error('Сервер не вернул список шкафов');
    inventoryCabinets = await response.json();
    renderInventoryCabinets();
  } catch (error) {
    document.getElementById('inventory-cabinets-body').innerHTML = `<tr><td colspan="5" class="empty">Не удалось загрузить список: ${escapeHtml(error.message)}. Проверьте, что сервер запущен.</td></tr>`;
    toast('Не удалось загрузить шкафы: ' + error.message, 'error');
  }
}

async function openNewInventoryCabinet() {
  try {
    await loadInventoryCabinets();
    const available = inventoryCabinets.filter(item => !item.cabinetId);
    const select = document.getElementById('new-inventory-qr');
    select.innerHTML = available.map(item => `<option value="${Number(item.qrCodeId)}">${escapeHtml(item.qrCode)}${item.objectName ? ` — ${escapeHtml(item.objectName)}` : ''}</option>`).join('');
    if (!available.length) {
      toast('Нет свободных QR-кодов. Сначала добавьте QR-код во вкладке «QR-коды».', 'error');
      return;
    }
    document.getElementById('modal-new-inventory-cabinet').classList.add('open');
  } catch (error) {
    toast('Не удалось получить список QR-кодов: ' + error.message, 'error');
  }
}

function createInventoryCabinetFromQr() {
  const qrCodeId = Number(document.getElementById('new-inventory-qr').value);
  if (!qrCodeId) return toast('Выберите QR-код.', 'error');
  closeModal('modal-new-inventory-cabinet');
  openInventoryCabinet(qrCodeId);
}

function renderInventoryCabinets() {
  const tbody = document.getElementById('inventory-cabinets-body');
  const search = (document.getElementById('search-inventory').value || '').toLowerCase();
  const rows = inventoryCabinets.filter(item =>
    item.qrCode.toLowerCase().includes(search) || item.title.toLowerCase().includes(search) ||
    item.objectName.toLowerCase().includes(search));
  if (!rows.length) {
    tbody.innerHTML = '<tr><td colspan="5" class="empty">QR-коды не найдены. Сначала создайте код во вкладке «QR-коды».</td></tr>';
    return;
  }
  tbody.innerHTML = rows.map(item => `
    <tr>
      <td><code>${escapeHtml(item.qrCode)}</code><div class="hint">${escapeHtml(item.objectName)}</div></td>
      <td>${item.cabinetId ? escapeHtml(item.title) : '<span class="badge badge-warning">Не настроен</span>'}</td>
      <td>${item.photoAvailable ? '<span class="badge badge-success">Добавлено</span>' : '<span class="badge badge-warning">Нет фото</span>'}</td>
      <td>${item.itemCount}</td>
      <td><div class="row-actions">
        <button class="btn btn-secondary btn-sm" onclick="openInventoryCabinet(${item.qrCodeId})">${item.cabinetId ? 'Настроить' : 'Создать шкаф'}</button>
        ${item.cabinetId ? `<button class="btn btn-danger btn-sm" onclick="deleteInventoryCabinet(${item.qrCodeId})">Удалить шкаф</button>` : ''}
      </div></td>
    </tr>`).join('');
}

async function deleteInventoryCabinet(qrCodeId) {
  const cabinet = inventoryCabinets.find(item => Number(item.qrCodeId) === Number(qrCodeId));
  if (!cabinet?.cabinetId) return toast('Настроенный шкаф не найден.', 'error');
  const title = cabinet.title || cabinet.objectName || cabinet.qrCode;
  if (!confirm(`Удалить шкаф «${title}» (${cabinet.qrCode}) вместе с QR-кодом, фотографиями, чек-листом и всей историей инвентаризаций? Это действие нельзя отменить.`)) return;

  try {
    const response = await fetch(`${API}/api/admin/inventory/cabinets/${qrCodeId}`, { method: 'DELETE' });
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Не удалось удалить шкаф');
    toast(data.message || 'Шкаф и его история удалены');
    await Promise.all([loadInventoryCabinets(), loadInventoryRuns()]);
  } catch (error) {
    toast('Ошибка удаления шкафа: ' + error.message, 'error');
  }
}

document.getElementById('search-inventory').oninput = renderInventoryCabinets;

async function openInventoryCabinet(qrCodeId) {
  try {
    const response = await fetch(`${API}/api/admin/inventory/cabinets/${qrCodeId}`);
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Не удалось загрузить шкаф');
    inventoryCurrent = data;
    document.getElementById('inventory-qr-id').value = qrCodeId;
    document.getElementById('inventory-qr-code').value = data.qrCode;
    document.getElementById('inventory-title').value = data.title;
    document.getElementById('inventory-items').value = data.items.map(item => item.name).join('\n');
    renderInventoryItemsPreview();
    photosMarkedForRemoval.clear();
    [1, 2].forEach(page => {
      document.getElementById(`inventory-photo-${page}`).value = '';
      const current = (data.photos || []).find(photo => Number(photo.pageNumber) === page);
      const preview = document.getElementById(`inventory-photo-preview-${page}`);
      preview.innerHTML = current ? `<img src="${escapeHtml(current.photoUrl)}" alt="Лист ${page} шкафа">` : 'Фото не загружено';
      document.getElementById(`inventory-photo-hint-${page}`).textContent = current
        ? `${current.caption || `Лист ${page}`} · выберите файл, чтобы заменить`
        : page === 1 ? 'Основной вид шкафа' : 'Дополнительный ракурс';
      const remove = document.getElementById(`inventory-photo-remove-${page}`);
      remove.hidden = !current;
      remove.classList.remove('is-selected');
      remove.textContent = 'Удалить фото';
    });
    document.getElementById('modal-inventory').classList.add('open');
  } catch (error) {
    toast(error.message, 'error');
  }
}

function toggleRemovePhoto(page) {
  const button = document.getElementById(`inventory-photo-remove-${page}`);
  if (photosMarkedForRemoval.has(page)) photosMarkedForRemoval.delete(page);
  else photosMarkedForRemoval.add(page);
  const selected = photosMarkedForRemoval.has(page);
  button.classList.toggle('is-selected', selected);
  button.textContent = selected ? 'Фото будет удалено' : 'Удалить фото';
}

[1, 2].forEach(page => {
  document.getElementById(`inventory-photo-${page}`).addEventListener('change', event => {
    const file = event.target.files[0];
    if (!file) return;
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      event.target.value = '';
      return toast('Выберите JPEG, PNG или WebP', 'error');
    }
    if (file.size > 8 * 1024 * 1024) {
      event.target.value = '';
      return toast('Фотография должна быть не больше 8 МБ', 'error');
    }
    photosMarkedForRemoval.delete(page);
    const remove = document.getElementById(`inventory-photo-remove-${page}`);
    remove.classList.remove('is-selected');
    remove.textContent = 'Удалить фото';
    document.getElementById(`inventory-photo-preview-${page}`).innerHTML = `<img src="${URL.createObjectURL(file)}" alt="Предпросмотр листа ${page}">`;
    document.getElementById(`inventory-photo-hint-${page}`).textContent = file.name;
  });
});

function readImageAsBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(',')[1]);
    reader.onerror = () => reject(new Error('Не удалось прочитать фотографию'));
    reader.readAsDataURL(file);
  });
}

async function saveInventoryCabinet() {
  const qrCodeId = document.getElementById('inventory-qr-id').value;
  const title = document.getElementById('inventory-title').value.trim();
  const names = document.getElementById('inventory-items').value
    .split(/\r?\n/).map(name => name.trim()).filter(Boolean);
  if (!title) return toast('Укажите название шкафа', 'error');
  if (!names.length) return toast('Добавьте хотя бы один предмет', 'error');
  if (new Set(names.map(name => name.toLocaleLowerCase('ru-RU'))).size !== names.length)
    return toast('Удалите повторяющиеся названия предметов', 'error');

  const previous = new Map((inventoryCurrent?.items || []).map(item => [item.name.toLocaleLowerCase('ru-RU'), item.id]));
  const body = {
    title,
    photos: [],
    removePhotoPages: Array.from(photosMarkedForRemoval),
    items: names.map((name, index) => ({
      id: previous.get(name.toLocaleLowerCase('ru-RU')) || 0,
      name,
      sortOrder: (index + 1) * 10
    }))
  };
  for (const pageNumber of [1, 2]) {
    const photo = document.getElementById(`inventory-photo-${pageNumber}`).files[0];
    if (!photo) continue;
    try {
      body.photos.push({ pageNumber, caption: `Лист ${pageNumber}`, contentType: photo.type, imageBase64: await readImageAsBase64(photo) });
    } catch (error) { return toast(error.message, 'error'); }
  }

  try {
    const response = await fetch(`${API}/api/admin/inventory/cabinets/${qrCodeId}`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
    });
    const data = await response.json();
    if (!response.ok) return toast(data.message || 'Не удалось сохранить шкаф', 'error');
    toast(data.message || 'Данные шкафа сохранены');
    closeModal('modal-inventory');
    await loadInventoryCabinets();
  } catch (error) {
    toast('Ошибка сети: ' + error.message, 'error');
  }
}

// Инициализация
async function checkApiStatus() {
  const pill = document.getElementById('api-status');
  try {
    const response = await fetch(`${API}/api/health`, { cache: 'no-store', signal: AbortSignal.timeout(8000) });
    if (!response.ok) throw new Error('offline');
    pill.classList.add('is-online');
    pill.querySelector('span:last-child').textContent = 'API подключён';
  } catch {
    pill.classList.add('is-offline');
    pill.querySelector('span:last-child').textContent = 'API недоступен';
  }
}

checkApiStatus();
loadUsers();

function refreshVisibleAdminPanel() {
  if (document.visibilityState !== 'visible') return;
  if (document.getElementById('panel-sessions').style.display !== 'none') loadSessions(true);
  else if (document.getElementById('panel-runs').style.display !== 'none') loadInventoryRuns(true);
}

setInterval(refreshVisibleAdminPanel, 10000);
window.addEventListener('focus', refreshVisibleAdminPanel);
document.addEventListener('visibilitychange', refreshVisibleAdminPanel);
