// Просмотр активных описей, результатов проверок и итоговых Excel-файлов.
let inventoryRunsData = [];
let inventoryBatchesData = [];
let inventoryRunsLoadInFlight = false;

// Загружает результаты по позициям МТР и обновляет сводки описи.
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
    if (typeof loadInventoryBatches === 'function') await loadInventoryBatches(true);
  } catch (error) {
    if (tbody) tbody.innerHTML = `<tr><td colspan="4" class="empty">${escapeHtml(error.message)}</td></tr>`;
    if (!silent) toast('Не удалось загрузить результаты инвентаризации', 'error');
  } finally {
    inventoryRunsLoadInFlight = false;
  }
}

// Загружает историю инвентаризаций, загруженных в AR-приложение.
async function loadInventoryBatches(silent = false) {
  const tbody = document.getElementById('inventory-batches-body');
  if (!tbody) return; // Если мы не на этой вкладке
  try {
    const res = await fetch(`${API}/api/admin/inventory/batches`);
    if (!res.ok) throw new Error('Сервер не смог загрузить общие проверки');
    const data = await res.json();
    inventoryBatchesData = Array.isArray(data) ? data : (data.items || []);
    renderInventoryBatches();
  } catch (error) {
    tbody.innerHTML = `<tr><td colspan="5" class="empty">${escapeHtml(error.message)}</td></tr>`;
    if (!silent) toast('Не удалось загрузить общие проверки', 'error');
  }
}

// Показывает прогресс каждой описи и ссылку на итоговый Excel.
function renderInventoryBatches() {
  const tbody = document.getElementById('inventory-batches-body');
  if (!inventoryBatchesData.length) {
    tbody.innerHTML = '<tr><td colspan="5" class="empty">Пока нет загруженных описей.</td></tr>';
    return;
  }
  tbody.innerHTML = inventoryBatchesData.map(batch => {
    const completed = Number(batch.completedPositionCount) || 0;
    const expected = Number(batch.expectedPositionCount) || 0;
    const done = batch.status === 'completed';
    const assigned = batch.status === 'assigned';
    return `<tr>
      <td>${formatDateTime(batch.completedAt || batch.startedAt)}</td>
      <td><strong>${escapeHtml(batch.title || 'Опись')}</strong></td>
      <td>${completed} / ${expected}</td>
      <td><span class="badge ${done ? 'badge-success' : 'badge-warning'}">${done ? 'Завершена' : assigned ? 'Готова к работе в приложении' : 'В процессе'}</span></td>
      <td class="row-actions">
        <button class="btn btn-secondary btn-sm" onclick="showInventoryBatch(${Number(batch.id)})">Сводка</button>
        ${done ? `<a class="btn btn-primary btn-sm" href="${API}/api/admin/inventory/batches/${Number(batch.id)}/excel">Excel</a>` : ''}
      </td>
    </tr>`;
  }).join('');
}

// Фильтрует результаты и при необходимости группирует их по дате или позиции МТР.
function renderInventoryRuns() {
  const search = document.getElementById('search-runs')?.value.trim().toLocaleLowerCase('ru-RU') || '';
  const groupBy = document.getElementById('group-runs-by')?.value || 'none';
  const tbody = document.getElementById('inventory-runs-body');
  if (!tbody) return;

  const rows = inventoryRunsData.filter(run =>
    [run.boxTitle, run.qrCode].some(value => String(value || '').toLocaleLowerCase('ru-RU').includes(search))
  );

  if (!rows.length) {
    tbody.innerHTML = `<tr><td colspan="4" class="empty">${inventoryRunsData.length ? 'По запросу ничего не найдено' : 'Пока нет завершённых проверок.'}</td></tr>`;
    return;
  }

  const renderRunRow = run => `
    <tr>
      <td>${formatDateTime(run.completedAt)}</td>
      <td><strong>${escapeHtml(run.boxTitle || '--')}</strong><div class="sub-cell"><code>${escapeHtml(run.qrCode || '--')}</code></div></td>
      <td><span class="badge badge-success">Есть: ${Number(run.presentCount) || 0}</span> <span class="badge badge-warning">Частично: ${Number(run.partialCount) || 0}</span> <span class="badge badge-danger">Нет: ${Number(run.missingCount) || 0}</span></td>
      <td><button class="btn btn-secondary btn-sm" onclick="showInventoryRun(${Number(run.id)})">Подробнее</button></td>
    </tr>`;

  if (groupBy === 'none') {
    tbody.innerHTML = rows.map(renderRunRow).join('');
    return;
  }

  const groups = new Map();
  rows.forEach(run => {
    let key, title, subtitle = '';
    if (groupBy === 'date') { key = title = formatDate(run.completedAt); }
    else { title = run.boxTitle || 'Материальная ценность не указана'; subtitle = run.qrCode || ''; key = `${title}\u0000${subtitle}`; }
    if (!groups.has(key)) groups.set(key, { title, subtitle, runs: [] });
    groups.get(key).runs.push(run);
  });

  tbody.innerHTML = Array.from(groups.values()).map(group => `
    <tr class="run-group-row"><td colspan="4">
      <div class="run-group-heading"><strong>${escapeHtml(group.title)}</strong><span>${formatRunCount(group.runs.length)}</span></div>
      ${group.subtitle ? `<div class="run-group-subtitle">${escapeHtml(group.subtitle)}</div>` : ''}
    </td></tr>
    ${group.runs.map(renderRunRow).join('')}
  `).join('');
}

// Возвращает корректную русскую форму слова «проверка» для счётчика.
function formatRunCount(count) {
  const lastTwo = count % 100, lastOne = count % 10;
  const word = lastTwo >= 11 && lastTwo <= 14 ? 'проверок' : lastOne === 1 ? 'проверка' : lastOne >= 2 && lastOne <= 4 ? 'проверки' : 'проверок';
  return `${count} ${word}`;
}

// Загружает подробности одной проверки, включая позиции, заметку и медиафайлы.
async function showInventoryRun(id) {
  try {
    const res = await fetch(`${API}/api/admin/inventory/runs/${id}`);
    const run = await res.json();
    if (!res.ok) return toast(run.message || 'Не удалось открыть проверку', 'error');

    document.getElementById('inventory-run-title').textContent = run.boxTitle || 'Результат инвентаризации';
    document.getElementById('inventory-run-meta').innerHTML = `
      <div><span>ЕНС / СКК и количество</span><strong><code>${escapeHtml(run.qrCode || '--')}</code></strong></div>
      <div><span>Завершена</span><strong>${formatDateTime(run.completedAt)}</strong></div>`;

    document.getElementById('inventory-run-items').innerHTML = (run.items || []).map((item, index) => {
      const actual = Number(item.actualQuantity) || 0, expected = Number(item.expectedQuantity) || 0;
      const status = actual === 0 ? 'Нет' : actual < expected ? 'Частично' : 'Есть';
      const badge = actual === 0 ? 'badge-danger' : actual < expected ? 'badge-warning' : 'badge-success';
      return `<tr><td class="run-item-number">${index + 1}</td><td>${escapeHtml(item.itemName || '--')}</td><td>${actual} / ${expected}</td><td><span class="badge ${badge}">${status}</span></td><td>${escapeHtml(item.comment || '—')}</td></tr>`;
    }).join('') || '<tr><td colspan="5" class="empty">В этой проверке нет пунктов.</td></tr>';

    const media = document.getElementById('inventory-run-media');
    const mediaUrl = `${API}/api/admin/inventory/runs/${Number(run.id)}/media`;
    const photoUrls = run.photoUrls?.length ? run.photoUrls : (run.photoUrl ? [`${mediaUrl}/photo`] : []);
    const reportNote = String(run.reportNote || '').trim();

    media.innerHTML = [
      reportNote ? `<section class="run-note"><strong>Примечание</strong><p>${escapeHtml(reportNote).replace(/\r?\n/g, '<br>')}</p></section>` : '',
      ...photoUrls.map((url, index) => `<figure><figcaption>Фото ${index + 1}</figcaption><a href="${API}${escapeHtml(url)}" target="_blank" rel="noopener"><img src="${API}${escapeHtml(url)}" alt="Фото ${index + 1}"></a></figure>`),
      run.voiceUrl ? `<div><strong>Голосовое сообщение</strong><audio controls preload="none" src="${mediaUrl}/voice"></audio></div>` : ''
    ].join('');
    document.getElementById('modal-inventory-run').classList.add('open');
  } catch (error) { toast('Ошибка загрузки проверки: ' + error.message, 'error'); }
}

// Загружает сводные строки общего назначения для просмотра в модальном окне.
async function showInventoryBatch(id) {
  try {
    const batch = inventoryBatchesData.find(item => Number(item.id) === Number(id));
    const res = await fetch(`${API}/api/admin/inventory/batches/${Number(id)}/rows`);
    const rows = await res.json();
    if (!res.ok) return toast(rows.message || 'Не удалось открыть сводную таблицу', 'error');

    document.getElementById('inventory-batch-title').textContent = `Инвентаризация · ${batch?.completedPositionCount || 0} / ${batch?.expectedPositionCount || 0} позиций`;
    document.getElementById('inventory-batch-meta').innerHTML = `
      <div><span>Опись</span><strong>${escapeHtml(batch?.title || 'Общая опись')}</strong></div>
      <div><span>Начата</span><strong>${formatDateTime(batch?.startedAt)}</strong></div>
      <div><span>Состояние</span><strong>${batch?.status === 'completed' ? 'Завершена' : 'В процессе'}</strong></div>`;

    document.getElementById('inventory-batch-rows').innerHTML = rows.map(row => `
      <tr>
        <td>${escapeHtml(row.structuralDivision || '')}</td>
        <td>${escapeHtml(row.warehouse || '')}</td>
        <td>${escapeHtml(row.itemName || '--')}</td>
        <td>${escapeHtml(row.ensCode || '')}</td>
        <td>${escapeHtml(row.accountCode || '')}</td>
        <td>${escapeHtml(row.mol || '')}</td>
        <td>${escapeHtml(row.storageLocation || '')}</td>
        <td>${escapeHtml(row.unit || '')}</td>
        <td>${Number(row.actualQuantity) || 0}</td>
        <td>${escapeHtml(row.comment || '')}</td>
      </tr>`).join('');
    document.getElementById('modal-inventory-batch').classList.add('open');
  } catch (error) { toast('Ошибка загрузки сводной таблицы: ' + error.message, 'error'); }
}

// Экспортируем обработчики интерфейса; массивы остаются внутренним состоянием модуля.
window.loadInventoryRuns = loadInventoryRuns;
window.loadInventoryBatches = loadInventoryBatches;
window.showInventoryRun = showInventoryRun;
window.showInventoryBatch = showInventoryBatch;

document.getElementById('search-runs')?.addEventListener('input', renderInventoryRuns);
document.getElementById('group-runs-by')?.addEventListener('change', renderInventoryRuns);
