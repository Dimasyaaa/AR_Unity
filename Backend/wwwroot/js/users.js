// Управление пользователями
let departmentsData = [];
let usersData = [];

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
  departmentsData.forEach(d => {
    const opt = document.createElement('option');
    opt.value = d; opt.textContent = d;
    select.appendChild(opt);
  });
  if (selected && !departmentsData.includes(selected)) {
    const opt = document.createElement('option');
    opt.value = selected; opt.textContent = selected;
    select.appendChild(opt);
  }
  const newOpt = document.createElement('option');
  newOpt.value = '__new__'; newOpt.textContent = '-- Новый отдел --';
  select.appendChild(newOpt);
  select.value = selected || (departmentsData.length ? departmentsData[0] : '__new__');
  toggleNewDepartment(select.value === '__new__');
}

function toggleNewDepartment(show) {
  const el = document.getElementById('user-department-new-group');
  if (el) el.style.display = show ? 'block' : 'none';
}

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
      <td>${u.isActive ? '<span class="badge badge-success">Активен</span>' : '<span class="badge badge-warning">Отключен</span>'}</td>
      <td>${formatDate(u.createdAt)}</td>
      <td>
        <div class="actions">
          <button class="btn btn-secondary btn-sm" onclick="toggleUser(${u.id})">${u.isActive ? 'Отключить' : 'Включить'}</button>
          <button class="btn btn-secondary btn-sm" onclick="editUser(${u.id})">Изменить</button>
          <button class="btn btn-danger btn-sm" onclick="deleteUser(${u.id})">Удалить</button>
        </td>
      </tr>
  `).join('');
}

async function openUserModal(user = null) {
  document.getElementById('user-modal-title').textContent = user ? 'Редактирование пользователя' : 'Новый пользователь';
  document.getElementById('user-id').value = user ? user.id : '';
  document.getElementById('user-fullName').value = user ? user.fullName : '';
  document.getElementById('user-password').value = '';
  document.getElementById('user-isActive').checked = user ? user.isActive : true;
  document.getElementById('user-department-new').value = '';
  await loadDepartments();
  fillDepartmentSelect(user ? user.department : null);
  document.getElementById('modal-user').classList.add('open');
}

async function saveUser() {
  const id = document.getElementById('user-id').value;
  const select = document.getElementById('user-department');
  const department = select.value === '__new__' ? document.getElementById('user-department-new').value.trim() : select.value;
  const body = {
    fullName: document.getElementById('user-fullName').value.trim(),
    department: department,
    password: document.getElementById('user-password').value,
    isActive: document.getElementById('user-isActive').checked
  };
  if (!body.fullName || !body.department) return toast('Заполните ФИО и отдел', 'error');
  if (!id && !body.password) return toast('Пароль обязателен при создании', 'error');

  const url = id ? `${API}/api/admin/users/${id}` : `${API}/api/admin/users`;
  try {
    const res = await fetch(url, { method: id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const data = await res.json();
    if (!res.ok) return toast(data.message || 'Ошибка', 'error');
    toast(data.message);
    closeModal('modal-user');
    loadUsers();
  } catch (e) { toast('Ошибка сети', 'error'); }
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

// Экспорт в глобальную область для HTML
window.usersData = usersData;
window.loadUsers = loadUsers;
window.editUser = id => { const u = usersData.find(x => x.id === id); if (u) openUserModal(u); };
window.saveUser = saveUser;
window.toggleUser = toggleUser;
window.deleteUser = deleteUser;
window.toggleNewDepartment = toggleNewDepartment;

document.getElementById('user-department')?.addEventListener('change', e => toggleNewDepartment(e.target.value === '__new__'));
document.getElementById('search-users')?.addEventListener('input', renderUsers);