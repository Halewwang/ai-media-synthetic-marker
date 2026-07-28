using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Emke.AiMarker.Infrastructure.Files;

internal sealed class OwnedTempFile : IDisposable
{
    private const string Prefix = ".emke-ai-marker-";
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint DeleteAccess = 0x00010000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint CreateNew = 1;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagSequentialScan = 0x08000000;
    private const int FileRenameInfo = 3;
    private const int FileDispositionInfo = 4;

    private readonly FileIdentity _identity;
    private FileStream? _lease;
    private bool _leaseCanDelete;
    private bool _verified;

    private OwnedTempFile(
        string path,
        string finalPath,
        string token,
        FileStream lease,
        FileIdentity identity)
    {
        Path = path;
        FinalPath = finalPath;
        Token = token;
        _lease = lease;
        _leaseCanDelete = OperatingSystem.IsWindows();
        _identity = identity;
    }

    public string Path { get; }

    public string FinalPath { get; }

    public string Token { get; }

    public Stream Destination => RequireLease();

    public static OwnedTempFile Reserve(
        string path,
        string finalPath,
        string requiredNamePrefix = Prefix)
    {
        if (!HasOwnedPathShape(path, finalPath, requiredNamePrefix))
        {
            throw new IOException(
                "临时文件不属于计划目标目录，已拒绝复制事务。");
        }

        FileStream lease = OpenCreateNewLease(path);
        try
        {
            FileIdentity identity = CaptureIdentity(lease.SafeFileHandle);
            return new(
                path,
                finalPath,
                Guid.NewGuid().ToString("N"),
                lease,
                identity);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public void CompleteCopy()
    {
        FileStream lease = RequireLease();
        lease.Flush(flushToDisk: true);
        if (OperatingSystem.IsWindows())
        {
            ReleaseLeaseForExifTool();
            return;
        }

        lease.Position = 0;
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
            && currentIdentity.Value == _identity;
    }

    public void SealVerifiedPath()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!TryLockWindowsFinalizationLease())
            {
                throw new IOException(
                    "严格验证后的临时文件所有权无法证明，可能已被替换。");
            }

            _verified = true;
            return;
        }

        if (!StillOwnsCurrentPath())
        {
            throw new IOException(
                "严格验证后的临时文件所有权无法证明，可能已被替换。");
        }

        _verified = true;
    }

    public bool StillOwnsVerifiedPath() =>
        _verified && StillOwnsCurrentPath();

    public bool LockOwnedPathForFinalization() =>
        OperatingSystem.IsWindows()
            ? TryLockWindowsFinalizationLease()
            : StillOwnsCurrentPath();

    public void RenameVerifiedTo(string finalPath)
    {
        if (!_verified)
        {
            throw new IOException(
                "临时文件尚未封存严格验证身份，已拒绝提交。");
        }

        if (OperatingSystem.IsWindows())
        {
            RenameWindowsLease(finalPath);
            return;
        }

        if (!StillOwnsCurrentPath())
        {
            throw new IOException(
                "临时文件所有权无法证明，可能已在提交边界被替换。");
        }

        File.Move(Path, finalPath, overwrite: false);
    }

    public void DeleteOwnedLease()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!TryLockWindowsFinalizationLease())
            {
                return;
            }

            DeleteWindowsLease();
            return;
        }

        if (StillOwnsCurrentPath())
        {
            File.Delete(Path);
        }
    }

    public void Dispose()
    {
        _lease?.Dispose();
        _lease = null;
        _leaseCanDelete = false;
    }

    public static bool HasOwnedPathShape(
        string candidatePath,
        string finalPath,
        string requiredNamePrefix = Prefix)
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
                && candidateName.StartsWith(
                    requiredNamePrefix,
                    StringComparison.Ordinal);
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

    private static FileStream OpenCreateNewLease(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.ReadWrite,
                    Options = FileOptions.SequentialScan,
                });
        }

        SafeFileHandle handle = CreateFile(
            ToExtendedWindowsPath(path),
            GenericRead | GenericWrite | DeleteAccess,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            CreateNew,
            FileAttributeNormal | FileFlagSequentialScan,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException(
                $"无法原子预留临时文件，Windows 错误 {error}：{path}");
        }

        return new FileStream(
            handle,
            FileAccess.ReadWrite,
            bufferSize: 4096,
            isAsync: false);
    }

    private void ReleaseLeaseForExifTool()
    {
        _lease?.Dispose();
        _lease = null;
        _leaseCanDelete = false;
    }

    private bool TryLockWindowsFinalizationLease()
    {
        if (_leaseCanDelete)
        {
            return CaptureIdentity(RequireLease().SafeFileHandle) == _identity;
        }

        _lease?.Dispose();
        _lease = null;

        FileStream? lease = TryOpenWindowsFinalizationLease();
        if (lease is null)
        {
            return false;
        }

        _lease = lease;
        _leaseCanDelete = true;
        return true;
    }

    private FileStream? TryOpenWindowsFinalizationLease()
    {
        SafeFileHandle handle = CreateFile(
            ToExtendedWindowsPath(Path),
            GenericRead | DeleteAccess,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error is 2 or 3)
            {
                return null;
            }

            throw new IOException(
                $"无法锁定已验证临时文件，Windows 错误 {error}：{Path}");
        }

        var lease = new FileStream(
            handle,
            FileAccess.Read,
            bufferSize: 4096,
            isAsync: false);
        if (CaptureIdentity(lease.SafeFileHandle) != _identity)
        {
            lease.Dispose();
            return null;
        }

        return lease;
    }

    private void RenameWindowsLease(string finalPath)
    {
        if (!_leaseCanDelete)
        {
            throw new IOException(
                "临时文件未锁定到可提交句柄，已拒绝重命名。");
        }

        string absoluteFinalPath = System.IO.Path.GetFullPath(finalPath);
        byte[] fileName = Encoding.Unicode.GetBytes(absoluteFinalPath);
        int rootOffset = IntPtr.Size == 8 ? 8 : 4;
        int lengthOffset = IntPtr.Size == 8 ? 16 : 8;
        int nameOffset = IntPtr.Size == 8 ? 20 : 12;
        int bufferLength = checked(
            nameOffset
            + fileName.Length
            + sizeof(char));
        IntPtr buffer = Marshal.AllocHGlobal(bufferLength);
        try
        {
            for (int offset = 0; offset < bufferLength; offset++)
            {
                Marshal.WriteByte(buffer, offset, 0);
            }

            Marshal.WriteByte(buffer, 0, 0);
            Marshal.WriteIntPtr(buffer, rootOffset, IntPtr.Zero);
            Marshal.WriteInt32(buffer, lengthOffset, fileName.Length);
            Marshal.Copy(fileName, 0, IntPtr.Add(buffer, nameOffset), fileName.Length);

            if (!SetFileInformationByHandle(
                    RequireLease().SafeFileHandle,
                    FileRenameInfo,
                    buffer,
                    bufferLength))
            {
                throw new IOException(
                    $"无法原子提交已验证临时文件，Windows 错误 {Marshal.GetLastPInvokeError()}。");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string ToExtendedWindowsPath(string path)
    {
        string fullPath = System.IO.Path
            .GetFullPath(path)
            .Replace('/', '\\');
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return fullPath;
        }

        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return $@"\\?\UNC\{fullPath[2..]}";
        }

        return $@"\\?\{fullPath}";
    }

    private void DeleteWindowsLease()
    {
        if (!_leaseCanDelete)
        {
            throw new IOException(
                "临时文件未锁定到可删除句柄，已拒绝回滚。");
        }

        IntPtr disposition = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(disposition, 0, 1);
            if (!SetFileInformationByHandle(
                    RequireLease().SafeFileHandle,
                    FileDispositionInfo,
                    disposition,
                    1))
            {
                throw new IOException(
                    $"无法按句柄回滚临时文件，Windows 错误 {Marshal.GetLastPInvokeError()}。");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(disposition);
        }
    }

    private FileStream RequireLease() =>
        _lease
        ?? throw new IOException("临时文件所有权 lease 已释放。");

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        IntPtr fileInformation,
        int bufferSize);

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
