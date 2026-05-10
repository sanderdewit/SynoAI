namespace SynoAI.Notifiers.Pushbullet
{
    internal class PushbulletError
    {
        public string Code { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Cat { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Code} - {Message}";
        }
    }
}
