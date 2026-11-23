// Назначение выбранных коробок сотруднику для последовательной инвентаризации.

// Загружает актуальные списки и показывает только настроенные коробки с позициями.
async function openInventoryAssignment() {
  await Promise.all([loadUsers(), loadInventoryBoxes()]);

  const activeUsers = getUsersData().filter(user => user.isActive);
  if (!activeUsers.length) return toast('Нет активных сотрудников для назначения.', 'error');

  const usersSelect = document.getElementById('assignment-user');
  usersSelect.innerHTML = activeUsers.map(user =>
    `<option value="${Number(user.id)}">${escapeHtml(user.fullName)} · ${escapeHtml(user.department || 'Без отдела')}</option>`
  ).join('');

  const configuredBoxes = getInventoryBoxes().filter(box => box.isActive && box.boxId && Number(box.itemCount) > 0);
  const list = document.getElementById('assignment-boxes');
  list.innerHTML = configuredBoxes.map(box => `
    <label class="assignment-box-option">
      <input type="checkbox" value="${Number(box.qrCodeId)}" onchange="updateAssignmentCount()">
      <span><strong>${escapeHtml(box.title || box.objectName)}</strong><small><code>${escapeHtml(box.qrCode)}</code> · ${Number(box.itemCount)} поз.</small></span>
    </label>`
  ).join('') || '<p class="empty">Нет активных настроенных коробок. Сначала создайте коробки и добавьте позиции.</p>';

  updateAssignmentCount();
  document.getElementById('modal-inventory-assignment').classList.add('open');
}

// Обновляет число отмеченных коробок рядом с заголовком списка.
function updateAssignmentCount() {
  const count = document.querySelectorAll('#assignment-boxes input[type="checkbox"]:checked').length;
  const label = document.getElementById('assignment-count');
  if (label) label.textContent = `Выбрано коробок: ${count}`;
}

// Отправляет серверу сотрудника и выбранные QR-коды как одно назначение.
async function createInventoryAssignment() {
  const userId = Number(document.getElementById('assignment-user').value);
  const qrCodeIds = Array.from(document.querySelectorAll('#assignment-boxes input[type="checkbox"]:checked'))
    .map(input => Number(input.value));

  if (!userId) return toast('Выберите сотрудника.', 'error');
  if (qrCodeIds.length === 0) return toast('Выберите хотя бы одну коробку.', 'error');

  try {
    const response = await fetch(`${API}/api/admin/inventory/assignments`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
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

// Кнопки в HTML и динамические отметки чек-листа вызывают эти обработчики.
window.openInventoryAssignment = openInventoryAssignment;
window.updateAssignmentCount = updateAssignmentCount;
window.createInventoryAssignment = createInventoryAssignment;
