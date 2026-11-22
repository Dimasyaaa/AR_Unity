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
    if (name === 'inventory') loadInventoryBoxes();
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
  if (id === 'modal-inventory-run')
    document.getElementById('inventory-run-media').querySelector('audio')?.pause();
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
  if (!confirm('Удалить QR-код? Если он обозначает коробку, вместе с ней будут удалены изображения, перечень и история инвентаризаций. У обычного QR события журнала сохранятся без связи с кодом. Действие необратимо.')) return;
  try {
    const res = await fetch(`${API}/api/admin/qr/${id}`, { method: 'DELETE' });
    const data = await res.json();
    if (!res.ok) return toast(data.message || 'Ошибка', 'error');
    toast(data.message);
    await Promise.all([loadQr(), loadInventoryBoxes(), loadInventoryRuns(), loadSessions()]);
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
let inventoryBatchesData = [];

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
    loadInventoryBatches(true);
  } catch (error) {
    tbody.innerHTML = `<tr><td colspan="6" class="empty">${escapeHtml(error.message)}</td></tr>`;
    if (!silent) toast('Не удалось загрузить результаты инвентаризации', 'error');
  } finally {
    inventoryRunsLoadInFlight = false;
  }
}

async function loadInventoryBatches(silent = false) {
  const tbody = document.getElementById('inventory-batches-body');
  try {
    const res = await fetch(`${API}/api/admin/inventory/batches`);
    if (!res.ok) throw new Error('Сервер не смог загрузить общие проверки');
    const data = await res.json();
    inventoryBatchesData = Array.isArray(data) ? data : (data.items || []);
    renderInventoryBatches();
  } catch (error) {
    tbody.innerHTML = `<tr><td colspan="6" class="empty">${escapeHtml(error.message)}</td></tr>`;
    if (!silent) toast('Не удалось загрузить общие проверки', 'error');
  }
}

function renderInventoryBatches() {
  const tbody = document.getElementById('inventory-batches-body');
  if (!inventoryBatchesData.length) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty">Пока нет общих инвентаризаций коробок.</td></tr>';
    return;
  }

  tbody.innerHTML = inventoryBatchesData.map(batch => {
    const completed = Number(batch.completedBoxCount) || 0;
    const expected = Number(batch.expectedBoxCount) || 0;
    const done = batch.status === 'completed';
    const assigned = batch.status === 'assigned';
    return `<tr>
      <td>${formatDateTime(batch.completedAt || batch.startedAt)}</td>
      <td>${escapeHtml(batch.userFullName || '--')}</td>
      <td>${escapeHtml(batch.userDepartment || '--')}</td>
      <td>${completed} / ${expected}</td>
      <td><span class="badge ${done ? 'badge-success' : 'badge-warning'}">${done ? 'Завершена' : assigned ? 'Назначена' : 'В процессе'}</span></td>
      <td class="row-actions">
        <button class="btn btn-secondary btn-sm" onclick="showInventoryBatch(${Number(batch.id)})">Сводка</button>
        <a class="btn btn-primary btn-sm" href="${API}/api/admin/inventory/batches/${Number(batch.id)}/excel">Excel</a>
      </td>
    </tr>`;
  }).join('');
}

function renderInventoryRuns() {
  const search = document.getElementById('search-runs').value.trim().toLocaleLowerCase('ru-RU');
  const groupBy = document.getElementById('group-runs-by').value;
  const tbody = document.getElementById('inventory-runs-body');
  const rows = inventoryRunsData.filter(run =>
    [run.userFullName, run.userDepartment, run.boxTitle, run.qrCode]
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
      <td><strong>${escapeHtml(run.boxTitle || '--')}</strong><div class="sub-cell"><code>${escapeHtml(run.qrCode || '--')}</code></div></td>
      <td><span class="badge badge-success">Есть: ${Number(run.presentCount) || 0}</span> <span class="badge badge-warning">Частично: ${Number(run.partialCount) || 0}</span> <span class="badge badge-danger">Нет: ${Number(run.missingCount) || 0}</span></td>
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
      title = run.boxTitle || 'Коробка не указана';
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
  const items = parseInventoryItemLines(inventoryItemsInput.value);
  preview.innerHTML = items.map(item => `<li>${escapeHtml(item.name)} <span class="hint">· по учёту: ${item.expectedQuantity} · 3D: ${escapeHtml(item.modelType)}</span></li>`).join('') || '<li class="checklist-preview-empty">Позиции появятся здесь в указанном порядке</li>';
}

// Разбирает название, учётное количество и тип тестовой 3D-фигуры.
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

async function showInventoryRun(id) {
  try {
    const res = await fetch(`${API}/api/admin/inventory/runs/${id}`);
    const run = await res.json();
    if (!res.ok) return toast(run.message || 'Не удалось открыть проверку', 'error');
    document.getElementById('inventory-run-title').textContent = run.boxTitle || 'Результат инвентаризации';
    document.getElementById('inventory-run-meta').innerHTML = `
      <div><span>Сотрудник</span><strong>${escapeHtml(run.userFullName || '--')}</strong></div>
      <div><span>Отдел</span><strong>${escapeHtml(run.userDepartment || '--')}</strong></div>
      <div><span>QR-код</span><strong><code>${escapeHtml(run.qrCode || '--')}</code></strong></div>
      <div><span>Завершена</span><strong>${formatDateTime(run.completedAt)}</strong></div>`;
    document.getElementById('inventory-run-items').innerHTML = (run.items || []).map((item, index) => {
      const actual = Number(item.actualQuantity) || 0;
      const expected = Number(item.expectedQuantity) || 0;
      const status = actual === 0 ? 'Нет' : actual < expected ? 'Частично' : 'Есть';
      const badge = actual === 0 ? 'badge-danger' : actual < expected ? 'badge-warning' : 'badge-success';
      return `<tr><td class="run-item-number">${index + 1}</td><td>${escapeHtml(item.itemName || '--')}</td><td>${actual} / ${expected}</td><td><span class="badge ${badge}">${status}</span></td><td>${escapeHtml(item.comment || '—')}</td></tr>`;
    }).join('') || '<tr><td colspan="5" class="empty">В этой проверке нет пунктов.</td></tr>';
    const media = document.getElementById('inventory-run-media');
    const mediaUrl = `${API}/api/admin/inventory/runs/${Number(run.id)}/media`;
    const photoUrls = run.photoUrls?.length ? run.photoUrls : (run.photoUrl ? [`${mediaUrl}/photo`] : []);
    const reportNote = String(run.reportNote || '').trim();
    media.innerHTML = [
      reportNote ? `<section class="run-note"><strong>Заметка сотрудника</strong>
        <p>${escapeHtml(reportNote).replace(/\r?\n/g, '<br>')}</p></section>` : '',
      ...photoUrls.map((url, index) => `<figure><figcaption>Фото ${index + 1}</figcaption>
        <a href="${API}${escapeHtml(url)}" target="_blank" rel="noopener"><img src="${API}${escapeHtml(url)}" alt="Фото ${index + 1} отчёта"></a>
        </figure>`),
      run.voiceUrl ? `<div><strong>Голосовое сообщение</strong>
        <audio controls preload="none" src="${mediaUrl}/voice"></audio>
        </div>` : ''
    ].join('');
    document.getElementById('modal-inventory-run').classList.add('open');
  } catch (error) {
    toast('Ошибка загрузки проверки: ' + error.message, 'error');
  }
}

async function showInventoryBatch(id) {
  try {
    const batch = inventoryBatchesData.find(item => Number(item.id) === Number(id));
    const res = await fetch(`${API}/api/admin/inventory/batches/${Number(id)}/rows`);
    const rows = await res.json();
    if (!res.ok) return toast(rows.message || 'Не удалось открыть сводную таблицу', 'error');

    document.getElementById('inventory-batch-title').textContent = `Инвентаризация коробок · ${batch?.completedBoxCount || 0} / ${batch?.expectedBoxCount || 0}`;
    document.getElementById('inventory-batch-meta').innerHTML = `
      <div><span>Сотрудник</span><strong>${escapeHtml(batch?.userFullName || rows[0]?.userFullName || '--')}</strong></div>
      <div><span>Отдел</span><strong>${escapeHtml(batch?.userDepartment || rows[0]?.department || '--')}</strong></div>
      <div><span>Начата</span><strong>${formatDateTime(batch?.startedAt)}</strong></div>
      <div><span>Состояние</span><strong>${batch?.status === 'completed' ? 'Завершена' : 'В процессе'}</strong></div>`;
    document.getElementById('inventory-batch-rows').innerHTML = rows.map(row => {
      const actual = Number(row.actualQuantity) || 0;
      const expected = Number(row.expectedQuantity) || 0;
      const status = actual === 0 ? 'Нет' : actual < expected ? 'Частично' : 'Есть';
      const badge = actual === 0 ? 'badge-danger' : actual < expected ? 'badge-warning' : 'badge-success';
      return `<tr><td>${escapeHtml(row.boxTitle || '--')}</td><td><code>${escapeHtml(row.qrCode || '--')}</code></td><td>${escapeHtml(row.itemName || '--')}</td><td>${expected}</td><td>${actual}</td><td><span class="badge ${badge}">${status}</span></td><td>${escapeHtml(row.comment || '—')}</td></tr>`;
    }).join('');
    document.getElementById('modal-inventory-batch').classList.add('open');
  } catch (error) {
    toast('Ошибка загрузки сводной таблицы: ' + error.message, 'error');
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

// Коробки, изображения содержимого и тестовые 3D-модели
let inventoryBoxes = [];
let inventoryCurrent = null;
const photosMarkedForRemoval = new Set();
let removeCurrentModel = false;
let inventoryPhotoDrafts = [];
let nextInventoryPhotoKey = 1;

async function loadInventoryBoxes() {
  try {
    const response = await fetch(`${API}/api/admin/inventory/boxes`);
    if (!response.ok) throw new Error('Сервер не вернул список коробок');
    inventoryBoxes = await response.json();
    renderInventoryBoxes();
  } catch (error) {
    document.getElementById('inventory-boxes-body').innerHTML = `<tr><td colspan="6" class="empty">Не удалось загрузить список: ${escapeHtml(error.message)}. Проверьте, что сервер запущен.</td></tr>`;
    toast('Не удалось загрузить коробки: ' + error.message, 'error');
  }
}

async function openNewInventoryBox() {
  try {
    await loadInventoryBoxes();
    const available = inventoryBoxes.filter(item => !item.boxId);
    const select = document.getElementById('new-inventory-qr');
    select.innerHTML = available.map(item => `<option value="${Number(item.qrCodeId)}">${escapeHtml(item.qrCode)}${item.objectName ? ` — ${escapeHtml(item.objectName)}` : ''}</option>`).join('');
    if (!available.length) {
      toast('Нет свободных QR-кодов. Сначала добавьте QR-код во вкладке «QR-коды».', 'error');
      return;
    }
    document.getElementById('modal-new-inventory-box').classList.add('open');
  } catch (error) {
    toast('Не удалось получить список QR-кодов: ' + error.message, 'error');
  }
}

function createInventoryBoxFromQr() {
  const qrCodeId = Number(document.getElementById('new-inventory-qr').value);
  if (!qrCodeId) return toast('Выберите QR-код.', 'error');
  closeModal('modal-new-inventory-box');
  openInventoryBox(qrCodeId);
}

function renderInventoryBoxes() {
  const tbody = document.getElementById('inventory-boxes-body');
  const search = (document.getElementById('search-inventory').value || '').toLowerCase();
    const rows = inventoryBoxes.filter(item =>
    item.qrCode.toLowerCase().includes(search) || item.title.toLowerCase().includes(search) ||
    item.objectName.toLowerCase().includes(search));
  if (!rows.length) {
    tbody.innerHTML = '<tr><td colspan="6" class="empty">QR-коды не найдены. Сначала создайте код во вкладке «QR-коды».</td></tr>';
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
        <button class="btn btn-secondary btn-sm" onclick="openInventoryBox(${item.qrCodeId})">${item.boxId ? 'Настроить' : 'Создать коробку'}</button>
        ${item.boxId ? `<button class="btn btn-danger btn-sm" onclick="deleteInventoryBox(${item.qrCodeId})">Удалить коробку</button>` : ''}
      </div></td>
    </tr>`).join('');
}

async function deleteInventoryBox(qrCodeId) {
  const box = inventoryBoxes.find(item => Number(item.qrCodeId) === Number(qrCodeId));
  if (!box?.boxId) return toast('Настроенная коробка не найдена.', 'error');
  const title = box.title || box.objectName || box.qrCode;
  if (!confirm(`Удалить коробку «${title}» (${box.qrCode}) вместе с QR-кодом, фотографиями, перечнем и всей историей инвентаризаций? Это действие нельзя отменить.`)) return;

  try {
    const response = await fetch(`${API}/api/admin/inventory/boxes/${qrCodeId}`, { method: 'DELETE' });
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Не удалось удалить коробку');
    toast(data.message || 'Коробка и её история удалены');
    await Promise.all([loadInventoryBoxes(), loadInventoryRuns()]);
  } catch (error) {
    toast('Ошибка удаления коробки: ' + error.message, 'error');
  }
}

document.getElementById('search-inventory').oninput = renderInventoryBoxes;

async function openInventoryBox(qrCodeId) {
  try {
    const response = await fetch(`${API}/api/admin/inventory/boxes/${qrCodeId}`);
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || 'Не удалось загрузить коробку');
    inventoryCurrent = data;
    document.getElementById('inventory-qr-id').value = qrCodeId;
    document.getElementById('inventory-qr-code').value = data.qrCode;
    document.getElementById('inventory-title').value = data.title;
    document.getElementById('inventory-items').value = data.items.map(item => `${item.name} | ${Number(item.expectedQuantity) || 1} | ${item.modelType || 'cube'}`).join('\n');
    renderInventoryItemsPreview();
    photosMarkedForRemoval.clear();
    inventoryPhotoDrafts.forEach(draft => {
      if (draft.previewUrl) URL.revokeObjectURL(draft.previewUrl);
    });
    inventoryPhotoDrafts = (data.photos || [])
      .filter(photo => Number(photo.pageNumber) > 0)
      .map(photo => ({
        key: nextInventoryPhotoKey++,
        pageNumber: Number(photo.pageNumber),
        caption: photo.caption || `Изображение содержимого ${photo.pageNumber}`,
        photoUrl: photo.photoUrl,
        previewUrl: '',
        file: null,
        isExisting: true
      }));
    renderInventoryPhotoInputs();
    const modelInput = document.getElementById('inventory-model');
    modelInput.value = '';
    removeCurrentModel = false;
    document.getElementById('inventory-model-hint').textContent = data.modelAvailable
      ? `${data.modelFileName || 'GLB-модель загружена'} · выберите файл для замены`
      : 'GLB не загружена. В карточке будут показаны тестовые примитивы из перечня.';
    const modelRemove = document.getElementById('inventory-model-remove');
    modelRemove.hidden = !data.modelAvailable;
    modelRemove.classList.remove('is-selected');
    modelRemove.textContent = 'Удалить 3D-модель';
    document.getElementById('modal-inventory').classList.add('open');
  } catch (error) {
    toast(error.message, 'error');
  }
}

function toggleRemovePhoto(page, button) {
  if (photosMarkedForRemoval.has(page)) photosMarkedForRemoval.delete(page);
  else photosMarkedForRemoval.add(page);
  const selected = photosMarkedForRemoval.has(page);
  if (button) {
    button.classList.toggle('is-selected', selected);
    button.textContent = selected ? 'Оставить фото' : 'Удалить фото';
  }
}

// Добавляет строку загрузки следующего изображения, не ограничивая их число.
function addInventoryPhotoInput() {
  const allPages = [
    ...(inventoryCurrent?.photos || []).map(photo => Number(photo.pageNumber) || 0),
    ...inventoryPhotoDrafts.map(draft => draft.pageNumber)
  ];
  inventoryPhotoDrafts.push({
    key: nextInventoryPhotoKey++,
    pageNumber: Math.max(0, ...allPages) + 1,
    caption: `Изображение содержимого ${Math.max(0, ...allPages) + 1}`,
    photoUrl: '',
    previewUrl: '',
    file: null,
    isExisting: false
  });
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
  list.innerHTML = inventoryPhotoDrafts.map(draft => {
    const markedForRemoval = photosMarkedForRemoval.has(draft.pageNumber);
    const preview = draft.previewUrl || draft.photoUrl;
    return `<article class="photo-upload-card">
      <strong>Изображение ${draft.pageNumber}</strong>
      <div class="photo-preview">${preview ? `<img src="${escapeHtml(preview)}" alt="Изображение содержимого ${draft.pageNumber}">` : 'Фото не загружено'}</div>
      <input type="file" accept="image/jpeg,image/png,image/webp" data-photo-input="${draft.key}">
      <div class="hint">${escapeHtml(draft.file?.name || draft.caption || 'Выберите изображение')}</div>
      <button type="button" class="btn btn-secondary btn-sm photo-remove ${markedForRemoval ? 'is-selected' : ''}" data-photo-remove="${draft.key}">${draft.isExisting ? (markedForRemoval ? 'Оставить фото' : 'Удалить фото') : 'Убрать строку'}</button>
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
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
    event.target.value = '';
    return toast('Выберите JPEG, PNG или WebP', 'error');
  }
  if (file.size > 8 * 1024 * 1024) {
    event.target.value = '';
    return toast('Фотография должна быть не больше 8 МБ', 'error');
  }
  if (draft.previewUrl) URL.revokeObjectURL(draft.previewUrl);
  draft.file = file;
  draft.previewUrl = URL.createObjectURL(file);
  photosMarkedForRemoval.delete(draft.pageNumber);
  renderInventoryPhotoInputs();
}

function toggleRemoveModel() {
  removeCurrentModel = !removeCurrentModel;
  const button = document.getElementById('inventory-model-remove');
  button.classList.toggle('is-selected', removeCurrentModel);
  button.textContent = removeCurrentModel ? 'Модель будет удалена' : 'Удалить 3D-модель';
}

function readImageAsBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(',')[1]);
    reader.onerror = () => reject(new Error('Не удалось прочитать фотографию'));
    reader.readAsDataURL(file);
  });
}

function readFileAsBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(',')[1]);
    reader.onerror = () => reject(new Error('Не удалось прочитать GLB-модель'));
    reader.readAsDataURL(file);
  });
}

async function saveInventoryBox() {
  const qrCodeId = document.getElementById('inventory-qr-id').value;
  const title = document.getElementById('inventory-title').value.trim();
  const items = parseInventoryItemLines(document.getElementById('inventory-items').value);
  if (!title) return toast('Укажите название коробки', 'error');
  if (!items.length) return toast('Добавьте хотя бы одну позицию', 'error');
  if (items.some(item => !item.name || item.expectedQuantity < 1))
    return toast('Для каждой позиции укажите название и целое количество больше нуля', 'error');
  if (new Set(items.map(item => item.name.toLocaleLowerCase('ru-RU'))).size !== items.length)
    return toast('Удалите повторяющиеся названия позиций', 'error');

  const previous = new Map((inventoryCurrent?.items || []).map(item => [item.name.toLocaleLowerCase('ru-RU'), item]));
  const body = {
    title,
    photos: [],
    removePhotoPages: Array.from(photosMarkedForRemoval),
    removeModel: removeCurrentModel,
    items: items.map((item, index) => ({
      id: previous.get(item.name.toLocaleLowerCase('ru-RU'))?.id || 0,
      name: item.name,
      expectedQuantity: item.expectedQuantity,
      modelType: item.modelType || previous.get(item.name.toLocaleLowerCase('ru-RU'))?.modelType || 'cube',
      sortOrder: (index + 1) * 10
    }))
  };
  for (const draft of inventoryPhotoDrafts) {
    if (!draft.file) continue;
    try {
      body.photos.push({ pageNumber: draft.pageNumber, caption: draft.caption, contentType: draft.file.type, imageBase64: await readImageAsBase64(draft.file) });
    } catch (error) { return toast(error.message, 'error'); }
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

    const response = await fetch(`${API}/api/admin/inventory/boxes/${qrCodeId}`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
    });
    const data = await response.json();
    if (!response.ok) return toast(data.message || 'Не удалось сохранить коробку', 'error');
    toast(data.message || 'Данные коробки сохранены');
    closeModal('modal-inventory');
    await loadInventoryBoxes();
  } catch (error) {
    toast('Ошибка сети: ' + error.message, 'error');
  }
}

document.getElementById('inventory-model').addEventListener('change', event => {
  const file = event.target.files[0];
  if (!file) return;
  removeCurrentModel = false;
  document.getElementById('inventory-model-remove').classList.remove('is-selected');
  document.getElementById('inventory-model-remove').textContent = 'Удалить 3D-модель';
  document.getElementById('inventory-model-hint').textContent = `${file.name} · ${Math.ceil(file.size / 1024)} КБ · будет загружена при сохранении`;
});

// Назначение выбранного списка коробок конкретному сотруднику.
async function openInventoryAssignment() {
  await loadInventoryBoxes();
  const activeUsers = usersData.filter(user => user.isActive);
  if (!activeUsers.length) return toast('Нет активных сотрудников для назначения.', 'error');
  const usersSelect = document.getElementById('assignment-user');
  usersSelect.innerHTML = activeUsers.map(user => `<option value="${Number(user.id)}">${escapeHtml(user.fullName)} · ${escapeHtml(user.department || 'Без отдела')}</option>`).join('');
  const configuredBoxes = inventoryBoxes.filter(box => box.isActive && box.boxId && Number(box.itemCount) > 0);
  const list = document.getElementById('assignment-boxes');
  list.innerHTML = configuredBoxes.map(box => `
    <label class="assignment-box-option"><input type="checkbox" value="${Number(box.qrCodeId)}" onchange="updateAssignmentCount()">
      <span><strong>${escapeHtml(box.title || box.objectName)}</strong><small><code>${escapeHtml(box.qrCode)}</code> · ${Number(box.itemCount)} поз.</small></span>
    </label>`).join('') || '<p class="empty">Нет активных настроенных коробок. Сначала создайте коробки и добавьте позиции.</p>';
  updateAssignmentCount();
  document.getElementById('modal-inventory-assignment').classList.add('open');
}

function updateAssignmentCount() {
  const count = document.querySelectorAll('#assignment-boxes input[type="checkbox"]:checked').length;
  document.getElementById('assignment-count').textContent = `Выбрано коробок: ${count}`;
}

async function createInventoryAssignment() {
  const userId = Number(document.getElementById('assignment-user').value);
  const qrCodeIds = Array.from(document.querySelectorAll('#assignment-boxes input[type="checkbox"]:checked'))
    .map(input => Number(input.value));
  if (!userId) return toast('Выберите сотрудника.', 'error');
  if (qrCodeIds.length === 0) return toast('Выберите хотя бы одну коробку.', 'error');

  try {
    const response = await fetch(`${API}/api/admin/inventory/assignments`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userId, qrCodeIds })
    });
    const data = await response.json();
    if (!response.ok) return toast(data.message || 'Не удалось назначить инвентаризацию', 'error');
    closeModal('modal-inventory-assignment');
    toast(data.message || 'Инвентаризация назначена сотруднику');
    await loadInventoryBatches();
  } catch (error) {
    toast('Ошибка назначения: ' + error.message, 'error');
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
