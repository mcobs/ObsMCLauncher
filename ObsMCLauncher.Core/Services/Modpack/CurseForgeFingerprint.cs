using System;
using System.Buffers;
using System.IO;
using System.Threading;

namespace ObsMCLauncher.Core.Services.Modpack;

/// <summary>
/// CurseForge 文件指纹（MurmurHash2，seed = 1，跳过空白字节 0x09/0x0A/0x0D/0x20）。
///
/// 与 PCL2 <c>LocalResourceFile.CurseForgeHash</c> 逐行等价，唯一区别是这里做成了可流式喂入的累加器。
/// ⚠️ MurmurHash2 的初值 <c>h = seed ^ length</c> 依赖<b>总长度</b>，因此在流式场景下必须
/// <b>先数一遍过滤后的字节数</b>再开始喂数据（见 <see cref="ComputeFromFile(string, CancellationToken)"/>）。
/// 全部运算按 <c>uint</c> 环绕，<b>不要加 checked</b>。
/// </summary>
public sealed class CurseForgeFingerprintAccumulator
{
    private const uint M = 0x5BD1E995;
    private const int BufferSize = 128 * 1024;

    private uint _h;
    private uint _pending;
    private int _pendingCount;

    public CurseForgeFingerprintAccumulator(long filteredLength)
    {
        _h = 1u ^ unchecked((uint)filteredLength);
    }

    /// <summary>喂入原始字节（内部过滤掉 4 个空白字节）。</summary>
    public void Append(ReadOnlySpan<byte> raw)
    {
        foreach (var b in raw)
        {
            if (IsSkipped(b))
                continue;

            _pending |= (uint)b << (_pendingCount * 8);
            if (++_pendingCount == 4)
            {
                Mix(_pending);
                _pending = 0;
                _pendingCount = 0;
            }
        }
    }

    public uint Finish()
    {
        // 尾 3/2/1 字节（对应 PCL 的 Select Case length - i）
        switch (_pendingCount)
        {
            case 3:
                _h ^= _pending & 0x0000FFFF;
                _h ^= _pending & 0x00FF0000;
                _h *= M;
                break;
            case 2:
                _h ^= _pending & 0x0000FFFF;
                _h *= M;
                break;
            case 1:
                _h ^= _pending & 0x000000FF;
                _h *= M;
                break;
        }

        _h ^= _h >> 13;
        _h *= M;
        _h ^= _h >> 15;
        return _h;
    }

    private void Mix(uint k)
    {
        k *= M;
        k ^= k >> 24;
        k *= M;
        _h *= M;
        _h ^= k;
    }

    private static bool IsSkipped(byte b) => b is 0x09 or 0x0A or 0x0D or 0x20;

    /// <summary>一次性计算已知内容（内存数据 / 单测用）。</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        long filteredLength = 0;
        foreach (var b in data)
        {
            if (!IsSkipped(b))
                filteredLength++;
        }

        var accumulator = new CurseForgeFingerprintAccumulator(filteredLength);
        accumulator.Append(data);
        return accumulator.Finish();
    }

    /// <summary>
    /// 计算文件的 CurseForge 指纹。两遍读取：第一遍只统计过滤后的字节数（初值需要长度），第二遍才真正混合数据。
    /// </summary>
    public static uint ComputeFromFile(string filePath, CancellationToken cancellationToken = default)
        => ComputeFromFile(filePath, CountFilteredBytes(filePath, cancellationToken), cancellationToken);

    /// <summary>已知过滤后长度时计算（供合并哈希复用第一遍的统计结果）。</summary>
    public static uint ComputeFromFile(string filePath, long filteredLength, CancellationToken cancellationToken = default)
    {
        var accumulator = new CurseForgeFingerprintAccumulator(filteredLength);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.SequentialScan);
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                accumulator.Append(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return accumulator.Finish();
    }

    /// <summary>统计文件中"去掉 4 个空白字节后"的长度。</summary>
    public static long CountFilteredBytes(string filePath, CancellationToken cancellationToken = default)
    {
        long count = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.SequentialScan);
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var i = 0; i < read; i++)
                {
                    if (!IsSkipped(buffer[i]))
                        count++;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return count;
    }
}
