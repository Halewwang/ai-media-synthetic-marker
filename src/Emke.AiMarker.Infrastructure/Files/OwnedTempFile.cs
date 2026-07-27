using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Emke.AiMarker.Infrastructure.Files;

internal sealed class OwnedTempFile : IDisposable
{
    private const string Prefix = ".emke-ai-marker-";
    private FileIdentity _ownedIdentity;
    private FileIdentity? _verifiedIdentity;
    private FileStream? _reservation;

    private OwnedTempFile(
        string path,
        string finalPath,
        string token,
        FileStream reservation,
        FileIdentity identity)
    {
        Path = path;
        FinalPath = finalPath;
        Token = token;
        _reservation = reservation;
        _ownedIdentity = identity;
    }

    public string Path { get; }

    public string FinalPath { get; }

    public string Token { get; }

    public Stream Destination =>
        _reservation
        ?? throw new InvalidOperationException("临时文件预留流已经关闭。");

    public static OwnedTempFile Reserve(string path, string finalPath)
    {
        if (!HasOwnedPathShape(path, finalPath))
        {
            throw new IOException(
                "临时文件不属于计划目标目录，已拒绝复制事务。");
        }

        var reservation = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.ReadWrite,
                Options = FileOptions.SequentialScan,
            });

        try
        {
            FileIdentity identity = CaptureIdentity(reservation.SafeFileHandle);
            return new(
                path,
                finalPath,
                Guid.NewGuid().ToString("N"),
                reservation,
                identity);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    public void CompleteCopy()
    {
        FileStream reservation = _reservation
            ?? throw new InvalidOperationException("临时文件预留流已经关闭。");
        reservation.Flush(flushToDisk: true);
        reservation.Dispose();
        _reservation = null;
    }

    public bool Matches(
        string sourcePath,
        string workingPath,
        string finalPath,
        string ownershipToken) =>
        !IsSamePath(workingPath, sourcePath)
        && !IsSamePath(workingPath, finalPath)
        && string.Equals(Token, ownershipToken, StringComparison.Ordinal)
        && IsSamePath(Path, workingPath)
        && IsSamePath(FinalPath, finalPath);

    public bool StillOwnsCurrentPath()
    {
        FileIdentity? currentIdentity = TryCaptureCurrentIdentity();
        return currentIdentity is not null
            && currentIdentity.Value == _ownedIdentity;
    }

    public void SealVerifiedPath()
    {
        FileIdentity currentIdentity = TryCaptureCurrentIdentity()
            ?? throw new IOException(
                "严格验证后的临时文件不存在或无法读取所有权标识。");
        _ownedIdentity = currentIdentity;
        _verifiedIdentity = currentIdentity;
    }

    public bool StillOwnsVerifiedPath()
    {
        if (_verifiedIdentity is not FileIdentity verifiedIdentity)
        {
            return false;
        }

        FileIdentity? currentIdentity = TryCaptureCurrentIdentity();
        return currentIdentity is not null
            && currentIdentity.Value == verifiedIdentity;
    }

    private FileIdentity? TryCaptureCurrentIdentity()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            using var current = new FileStream(
                Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return CaptureIdentity(current.SafeFileHandle);
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return null;
        }
    }

    public void DeleteIfStillOwned()
    {
        CompleteReservationWithoutFlush();
        if (StillOwnsCurrentPath())
        {
            File.Delete(Path);
        }
    }

    public void Dispose() => CompleteReservationWithoutFlush();

    public static bool HasOwnedPathShape(string candidatePath, string finalPath)
    {
        try
        {
            string candidateFullPath = System.IO.Path.GetFullPath(candidatePath);
            string finalFullPath = System.IO.Path.GetFullPath(finalPath);
            string? candidateDirectory =
                System.IO.Path.GetDirectoryName(candidateFullPath);
            string? destinationDirectory =
                System.IO.Path.GetDirectoryName(finalFullPath);
            string candidateName = System.IO.Path.GetFileName(candidateFullPath);

            return candidateDirectory is not null
                && destinationDirectory is not null
                && string.Equals(
                    candidateDirectory,
                    destinationDirectory,
                    StringComparison.OrdinalIgnoreCase)
                && candidateName.StartsWith(Prefix, StringComparison.Ordinal);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    public static bool IsSamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                System.IO.Path.GetFullPath(left),
                System.IO.Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    private void CompleteReservationWithoutFlush()
    {
        _reservation?.Dispose();
        _reservation = null;
    }

    private static FileIdentity CaptureIdentity(SafeFileHandle handle)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetFileInformationByHandle(handle, out ByHandleFileInformation info))
            {
                throw new IOException(
                    $"无法读取临时文件所有权标识，Windows 错误 {Marshal.GetLastPInvokeError()}。");
            }

            ulong fileIndex = ((ulong)info.FileIndexHigh << 32)
                | info.FileIndexLow;
            return new(info.VolumeSerialNumber, fileIndex);
        }

        const int bufferSize = 512;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (int offset = 0; offset < bufferSize; offset += sizeof(long))
            {
                Marshal.WriteInt64(buffer, offset, 0);
            }

            int descriptor = handle.DangerousGetHandle().ToInt32();
            if (FStat(descriptor, buffer) != 0)
            {
                throw new IOException(
                    $"无法读取临时文件所有权标识，系统错误 {Marshal.GetLastPInvokeError()}。");
            }

            ulong device = unchecked((ulong)Marshal.ReadInt64(buffer, 0));
            ulong inode = unchecked((ulong)Marshal.ReadInt64(buffer, 8));
            return new(device, inode);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation information);

    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int FStat(int descriptor, IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private readonly record struct FileIdentity(ulong Device, ulong FileIndex);
}
