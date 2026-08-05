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
}