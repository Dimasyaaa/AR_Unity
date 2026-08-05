// AR Inventory — панель администратора
const API = ''; // тот же хост, что и страница

// Навигация по вкладкам
document.querySelectorAll('.tab').forEach(tab => {
  tab.onclick = () => {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    tab.classList.add('active');
    const name = tab.dataset.tab;
    ['users', 'qr', 'sessions'].forEach(p => {
      document.getElementById('panel-' + p).style.display = (p === name) ? 'block' : 'none';
    });
    if (name === 'users') loadUsers();
    if (name === 'qr') loadQr();
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
  if (!confirm('Удалить QR-код? Связанные сессии сохранятся, но поле qr_code_id станет NULL.')) return;
  const res = await fetch(`${API}/api/admin/qr/${id}`, { method: 'DELETE' });
  const data = await res.json();
  if (!res.ok) return toast(data.message || 'Ошибка', 'error');
  toast(data.message);
  loadQr();
}

// SESSIONS
async function loadSessions() {
  try {
    const res = await fetch(`${API}/api/admin/sessions`);
    const data = await res.json();
    const tbody = document.getElementById('sessions-body');

    // Обновляем состояние кнопки очистки
    const clearBtn = document.getElementById('clear-sessions-btn');
    if (clearBtn) clearBtn.disabled = data.length === 0;

    if (data.length === 0) {
      tbody.innerHTML = '<tr><td colspan="8" class="empty">Нет событий</td></tr>';
      return;
    }

    tbody.innerHTML = data.map(s => {
      const actionBadge =
        s.action === 'login' ? 'badge-info' :
        s.action === 'scan' ? 'badge-success' :
        s.action === 'error' ? 'badge-danger' : 'badge-warning';
      return `
        <tr>
          <td>${formatDateTime(s.actionTime)}</td>
          <td>${escapeHtml(s.userFullName || '--')}</td>
          <td>${escapeHtml(s.userDepartment || '--')}</td>
          <td><span class="badge ${actionBadge}">${s.action}</span></td>
          <td>${s.qrCode ? '<code>' + escapeHtml(s.qrCode) + '</code>' : '--'}</td>
          <td>${s.qrObjectName ? escapeHtml(s.qrObjectName) : '--'}</td>
          <td>${s.comment ? escapeHtml(s.comment) : '--'}</td>
          <td>
            <div class="actions">
              <button class="btn btn-danger btn-sm" onclick="deleteSession(${s.id})">Удалить</button>
            </div>
          </td>
        </tr>
      `;
    }).join('');
  } catch (e) {
    toast('Ошибка загрузки: ' + e.message, 'error');
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

// Инициализация
loadUsers();