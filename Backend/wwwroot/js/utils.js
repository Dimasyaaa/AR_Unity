// объявление подключения и обработкич всех необходимых данных
const API = ''; // тот же хост, что и страница

function toast(msg, type = 'success') {
  const el = document.getElementById('toast');
  if (!el) return;
  el.textContent = msg;
  el.className = 'toast show ' + type;
  setTimeout(() => el.classList.remove('show'), 2500);
}

function closeModal(id) {
  const modal = document.getElementById(id);
  if (modal) {
    modal.classList.remove('open');
    if (id === 'modal-inventory-run') {
      modal.querySelector('audio')?.pause();
    }
  }
}

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

// Делаем функции доступными глобально для HTML onclick
window.toast = toast;
window.closeModal = closeModal;
window.formatDate = formatDate;
window.formatDateTime = formatDateTime;
window.escapeHtml = escapeHtml;