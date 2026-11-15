using SQLite;

namespace ArInventory.Local
{
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

    [Table("qr_codes")]
    public class LocalQrCode
    {
        [PrimaryKey, AutoIncrement]
        public long id { get; set; }

        public string code { get; set; }
        public string object_name { get; set; }
        public bool is_active { get; set; }
    }

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

        public bool synced {  get; set; }
    }

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
    }

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
        public string started_at { get; set; }
        public string completed_at { get; set; }
        public bool synced { get; set; }
    }

    [Table("local_inventory_run_items")]
    public class LocalInventoryRunItem
    {
        [PrimaryKey, AutoIncrement]
        public long id { get; set; }

        public long run_id { get; set; }
        public long checklist_item_id { get; set; }
        public string item_name_snapshot { get; set; }
        public string status { get; set; }
        public string comment { get; set; }
    }

    public class LocalInventoryItemResult
    {
        public long itemId;
        public string itemName;
        public string status;
        public string comment;
    }
}
