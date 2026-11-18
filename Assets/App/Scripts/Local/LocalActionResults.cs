namespace ArInventory.Local
{
    /// <summary>Данные, которые форма входа получает после проверки учётных данных.</summary>
    public sealed class LocalLoginResult
    {
        public int userId;
        public string fullName;
        public string department;
    }

    /// <summary>Краткий результат сохранения QR-сканирования в локальный журнал.</summary>
    public sealed class LocalScanResult
    {
        public string objectName;
        public bool wasRepeated;
    }
}
