// Общие помощники админ-панели. Этот файл подключается первым, чтобы остальные
// модули могли пользоваться API и функциями отображения.
const API = ''; // Пустой адрес означает, что API находится на том же хосте.

// Показывает короткое сообщение о результате действия.
function toast(msg, type = 'success') {
  const el = document.getElementById('toast');
  if (!el) return;
  el.textContent = msg;
  el.className = 'toast show ' + type;
  setTimeout(() => el.classList.remove('show'), 2500);
}

// Закрывает модальное окно и останавливает воспроизведение аудио в отчёте.
function closeModal(id) {
  const modal = document.getElementById(id);
  if (modal) {
    modal.classList.remove('open');
    if (id === 'modal-inventory-run') {
      modal.querySelector('audio')?.pause();
    }
  }
}

// Приводят даты сервера к формату, принятому в русском интерфейсе.
function formatDate(d) {
  if (!d) return '--';
  return new Date(d).toLocaleDateString('ru-RU');
}

function formatDateTime(d) {
  if (!d) return '--';
  return new Date(d).toLocaleString('ru-RU');
}

// Экранирует текст перед вставкой в HTML, чтобы данные не стали разметкой.
function escapeHtml(s) {
  if (s == null) return '';
  return String(s).replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  }[c]));
}

// Читает выбранный файл и возвращает только Base64-часть для API.
function readImageAsBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(',')[1]);
    reader.onerror = () => reject(new Error('Не удалось прочитать фотографию'));
    reader.readAsDataURL(file);
  });
}

// Читает GLB-файл для отправки в JSON-запросе.
function readFileAsBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(',')[1]);
    reader.onerror = () => reject(new Error('Не удалось прочитать GLB-модель'));
    reader.readAsDataURL(file);
  });
}

// Функции, вызываемые из HTML-разметки, должны быть доступны через window.
window.toast = toast;
window.closeModal = closeModal;
window.formatDate = formatDate;
window.formatDateTime = formatDateTime;
window.escapeHtml = escapeHtml;
