// Журнал событий
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
    const matchesSearch = !search || [session.userFullName, session.userDepartment, session.qrCode, session.qrObjectName, session.comment]
      .some(value => String(value || '').toLocaleLowerCase('ru-RU').includes(search));
    return matchesAction && matchesSearch;
  });

  if (!rows.length) {
    tbody.innerHTML = `<tr><td colspan="8" class="empty">${adminSessionsData.length ? 'По выбранному фильтру записей нет' : 'Нет событий'}</td></tr>`;
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
      </tr>`;
  }).join('');
}

async function deleteSession(id) {
  if (!confirm('Удалить эту запись из журнала?')) return;
  try {
    const res = await fetch(`${API}/api/admin/sessions/${id}`, { method: 'DELETE' });
    const data = await res.json();
    if (!res.ok) return toast(data.message || 'Ошибка', 'error');
    toast(data.message);
    loadSessions();
  } catch (e) { toast('Ошибка сети', 'error'); }
}

async function clearAllSessions() {
  if (!confirm('Удалить ВСЕ записи журнала? Действие необратимо.')) return;
  try {
    const res = await fetch(`${API}/api/admin/sessions`, { method: 'DELETE' });
    const data = await res.json();
    if (!res.ok) return toast(data.message || 'Ошибка', 'error');
    toast(data.message);
    loadSessions();
  } catch (e) { toast('Ошибка сети', 'error'); }
}

window.adminSessionsData = adminSessionsData;
window.loadSessions = loadSessions;
window.deleteSession = deleteSession;
window.clearAllSessions = clearAllSessions;

document.getElementById('sessions-action-filter')?.addEventListener('change', renderSessions);
document.getElementById('sessions-search')?.addEventListener('input', renderSessions);