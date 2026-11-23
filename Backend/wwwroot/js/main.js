// AR Inventory — панель администратора
// Навигация по вкладкам
document.querySelectorAll('.tab').forEach(tab => {
  tab.onclick = () => {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    tab.classList.add('active');
    const name = tab.dataset.tab;
    ['users', 'qr', 'inventory', 'runs', 'sessions'].forEach(p => {
      const panel = document.getElementById('panel-' + p);
      if (panel) panel.style.display = (p === name) ? 'block' : 'none';
    });
    
    // Загружаем данные при переключении вкладки
    if (name === 'users' && typeof loadUsers === 'function') loadUsers();
    if (name === 'qr' && typeof loadQr === 'function') loadQr();
    if (name === 'inventory' && typeof loadInventoryBoxes === 'function') loadInventoryBoxes();
    if (name === 'runs' && typeof loadInventoryRuns === 'function') loadInventoryRuns();
    if (name === 'sessions' && typeof loadSessions === 'function') loadSessions();
  };
});

// Проверка статуса API при загрузке
async function checkApiStatus() {
  const pill = document.getElementById('api-status');
  if (!pill) return;
  try {
    const response = await fetch(`${API}/api/health`, { cache: 'no-store', signal: AbortSignal.timeout(8000) });
    if (!response.ok) throw new Error('offline');
    pill.classList.add('is-online');
    pill.classList.remove('is-offline');
    pill.querySelector('span:last-child').textContent = 'API подключён';
  } catch {
    pill.classList.add('is-offline');
    pill.classList.remove('is-online');
    pill.querySelector('span:last-child').textContent = 'API недоступен';
  }
}

// Фоновое обновление данных на активных вкладках
function refreshVisibleAdminPanel() {
  if (document.visibilityState !== 'visible') return;
  if (document.getElementById('panel-sessions')?.style.display !== 'none' && typeof loadSessions === 'function') {
    loadSessions(true);
  } else if (document.getElementById('panel-runs')?.style.display !== 'none' && typeof loadInventoryRuns === 'function') {
    loadInventoryRuns(true);
  }
}

// Инициализация
document.addEventListener('DOMContentLoaded', () => {
  checkApiStatus();
  if (typeof loadUsers === 'function') loadUsers(); // Загружаем пользователей по умолчанию
  
  setInterval(refreshVisibleAdminPanel, 10000);
  window.addEventListener('focus', refreshVisibleAdminPanel);
  document.addEventListener('visibilitychange', refreshVisibleAdminPanel);
});