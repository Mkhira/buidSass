namespace BackendApi.Modules.Storage;

public sealed class StorageUploadBlockedException(string message) : Exception(message)
{
}
