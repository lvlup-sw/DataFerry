namespace MockCachingOperation.Configuration
{
    /// <summary>
    /// AppSettings class; used to configure the application settings.
    /// </summary>
    /// <remarks>
    /// This is just an example class and can really be anything you want it to be. However, it is important to note that you must at least have a property for the cache type.
    /// </remarks>
    public class AppSettings
    {
        public required string CacheType { get; set; }
    }
}