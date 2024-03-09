namespace CacheProvider.Providers
{
    public enum GetFlags
    {
        ReturnNullIfNotFoundInCache,
        ForceMasterToRead,
        DoNotSetDataInCache
    }
}