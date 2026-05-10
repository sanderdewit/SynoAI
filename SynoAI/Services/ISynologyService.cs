namespace SynoAI.Services
{
    public interface ISynologyService
    {
        Task InitialiseAsync();
        Task<byte[]?> TakeSnapshotAsync(string cameraName);
    }
}
