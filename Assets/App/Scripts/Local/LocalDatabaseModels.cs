using SQLite;

namespace ArInventory.Local
{
    /// <summary>Копия учётной записи сотрудника для входа без сети.</summary>
    [Table("users")]
    public class LocalUser
    {
        [PrimaryKey, AutoIncrement]
        public long id { get; set; }

        public string full_name { get; set; }
        public string department { get; set; }
        public string password_hash { get; set; }
        public bool is_active { get; set; }
    }

    /// <summary>QR-код и подпись объекта из локального справочника.</summary>
    [Table("qr_codes")]
    public class LocalQrCode
    {
        [PrimaryKey, AutoIncrement]
        public long id { get; set; }

        public string code { get; set; }
        public string object_name { get; set; }
        public bool is_active { get; set; }
    }

    /// <summary>Событие пользователя, которое ожидает отправки в общий журнал сервера.</summary>
    [Table("inventory_sessions")]
    public class LocalInventorySession
    {
        [PrimaryKey, AutoIncrement]
        public long id { get; set; }

        public long user_id { get; set; }
        public long? qr_code_id { get; set; }
        public string action { get; set; }
        public System.DateTime action_time { get; set; }
        public string comment { get; set; }

        public bool synced { get; set; }
    }

    /// <summary>Сведения о шкафе, привязанном к QR-коду.</summary>
    [Table("cabinet_profiles")]
    public class LocalCabinetProfile
    {
        [PrimaryKey, AutoIncrement]
        public long id { get; set; }

        public long qr_code_id { get; set; }
        public string title { get; set; }
        public byte[] reference_photo { get; set; }
        public string reference_photo_content_type { get; set; }
        public bool is_active { get; set; }
    }

    /// <summary>Фотографии и подписи изображений содержимого коробки.</summary>
    [Table("cabinet_photos")]
    public class LocalCabinetPhoto
    {
        [PrimaryKey, AutoIncrement]
        public long id { get; set; }

        public long cabinet_id { get; set; }
        public int page_number { get; set; }
        public byte[] image_data { get; set; }
        public string content_type { get; set; }
        public string caption { get; set; }
    }

    /// <summary>Необязательная GLB-модель содержимого коробки для работы без сети.</summary>
    [Table("box_models")]
    public class LocalBoxModel
    {
        [PrimaryKey, AutoIncrement]
        public long id { get; set; }

        [Unique]
        public long cabinet_id { get; set; }
        public string file_name { get; set; }
        public string content_type { get; set; }
        public byte[] model_data { get; set; }
    }

    /// <summary>Пункты чек-листа, которые сотрудник отмечает во время проверки.</summary>
    [Table("cabinet_checklist_items")]
    public class LocalCabinetChecklistItem
    {
        [PrimaryKey, AutoIncrement]
        public long id { get; set; }

        public long cabinet_id { get; set; }
        public string item_name { get; set; }
        public string description { get; set; }
        public int sort_order { get; set; }
        public bool is_active { get; set; }
        public int expected_quantity { get; set; } = 1;
        public string model_type { get; set; } = "cube";
    }

    /// <summary>Локально сохранённая проверка, ожидающая синхронизации с сервером.</summary>
    [Table("local_inventory_runs")]
    public class LocalInventoryRun
    {
        [PrimaryKey, AutoIncrement]
        public long id { get; set; }

        [Unique]
        public string client_run_id { get; set; }
        public long cabinet_id { get; set; }
        public long qr_code_id { get; set; }
        public long user_id { get; set; }
        public string qr_code { get; set; }
        public string box_title { get; set; }
        public string client_batch_id { get; set; }
        public int expected_box_count { get; set; }
        public string started_at { get; set; }
        public string completed_at { get; set; }
        public bool synced { get; set; }
        public string report_payload { get; set; }
    }

    /// <summary>Локальная запись общего запуска, который объединяет проверки нескольких коробок.</summary>
    [Table("local_inventory_batches")]
    public class LocalInventoryBatch
    {
        [PrimaryKey, AutoIncrement]
        public long id { get; set; }

        [Unique]
        public string client_batch_id { get; set; }
        public long user_id { get; set; }
        public int expected_box_count { get; set; }
        // QR-коды, которые назначил администратор; храним их вместе с локальной партией.
        public string assigned_qr_codes { get; set; }
        public string started_at { get; set; }
        public string completed_at { get; set; }
        public string status { get; set; }
    }

    /// <summary>Строка сводки на Android: одна позиция из проверки конкретной коробки.</summary>
    public sealed class LocalInventoryBatchSummaryRow
    {
        public string boxTitle { get; set; }
        public string qrCode { get; set; }
        public string itemName { get; set; }
        public int expectedQuantity { get; set; }
        public int actualQuantity { get; set; }
        public string status { get; set; }
        public string completedAt { get; set; }
    }

    /// <summary>Отметка по пункту проверки с копией названия предмета на момент проверки.</summary>
    [Table("local_inventory_run_items")]
    public class LocalInventoryRunItem
    {
        [PrimaryKey, AutoIncrement]
        public long id { get; set; }

        public long run_id { get; set; }
        public long checklist_item_id { get; set; }
        public string item_name_snapshot { get; set; }
        public string status { get; set; }
        public int expected_quantity { get; set; } = 1;
        public int actual_quantity { get; set; }
        public string comment { get; set; }
    }

}
