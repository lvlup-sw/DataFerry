namespace MockCachingOperation.Configuration
{
    /// <summary>
    /// AppSettings class; used to configure the application settings.
    /// </summary>
    /// <remarks>
    /// This is just an example class and can really be anything you want it to be.
    /// </remarks>
    public class AppSettings
    {
        public string? CacheType { get; set; }

        public bool UseCache { get; set; }
    }
}