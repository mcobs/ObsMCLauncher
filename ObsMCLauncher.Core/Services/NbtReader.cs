using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// 轻量级 NBT (Named Binary Tag) 解析器，仅支持读取所需字段。
/// 参考: https://minecraft.wiki/w/NBT_format
/// </summary>
public static class NbtReader
{
    /// <summary>
    /// 从 GZip 压缩的 level.dat 中读取游戏版本（Data.Version.Id）。
    /// 解析失败或字段不存在时返回空字符串，不抛异常。
    /// </summary>
    public static string ReadWorldVersionFromLevelDat(string levelDatPath)
    {
        if (!File.Exists(levelDatPath))
        {
            DebugLogger.Warn("NbtReader", $"level.dat 不存在: {levelDatPath}");
            return "";
        }

        try
        {
            using var fileStream = File.OpenRead(levelDatPath);
            // level.dat 通常是 GZip 压缩，但部分工具可能生成未压缩的，通过魔数 0x1F 0x8B 判断
            Stream dataStream;
            int b1 = fileStream.ReadByte();
            int b2 = fileStream.ReadByte();
            if (b1 < 0 || b2 < 0)
            {
                DebugLogger.Warn("NbtReader", "level.dat 为空");
                return "";
            }

            fileStream.Position = 0;
            if (b1 == 0x1F && b2 == 0x8B)
            {
                dataStream = new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: false);
            }
            else
            {
                // 未压缩的 NBT，直接使用原流
                dataStream = fileStream;
            }

            using var reader = new BinaryReader(dataStream, Encoding.UTF8, leaveOpen: false);
            return ReadWorldVersion(reader);
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("NbtReader", $"解析 level.dat 失败: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 从 BinaryReader 读取 NBT 并提取 Data.Version.Id。
    /// </summary>
    internal static string ReadWorldVersion(BinaryReader reader)
    {
        try
        {
            // 根标签必须是 TAG_Compound(10)
            var rootType = reader.ReadByte();
            if (rootType != 10)
            {
                DebugLogger.Warn("NbtReader", $"根标签类型错误: {rootType}，期望 10 (TAG_Compound)");
                return "";
            }
            ReadNbtString(reader); // 根标签名（通常为空）

            // 在根 compound 中查找 Data.Version
            return FindCompoundAndReadString(reader, "Data", "Version", "Id");
        }
        catch (EndOfStreamException ex)
        {
            DebugLogger.Warn("NbtReader", $"NBT 流意外结束: {ex.Message}");
            return "";
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("NbtReader", $"NBT 解析异常: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 逐层进入 compound：root -> keys[0] -> keys[1] -> ... -> 读取名为 valueKey 的 TAG_String
    /// </summary>
    private static string FindCompoundAndReadString(BinaryReader reader, params string[] keys)
    {
        // 逐层进入 compound
        for (int i = 0; i < keys.Length - 1; i++)
        {
            if (!EnterCompoundByKey(reader, keys[i], out var skipped))
            {
                // 没找到目标 compound，且 reader 已经跳过了一些标签
                // 此时流位置不可控，直接返回空
                return "";
            }
        }

        // 在最内层 compound 中查找 valueKey
        return FindStringByKey(reader, keys[^1]);
    }

    /// <summary>
    /// 在当前 compound 流位置读取所有子标签，进入名为 key 的子 compound。
    /// 成功进入后 reader 位于该子 compound 的第一个子标签前。
    /// 失败时 reader 位置不确定（已跳过部分内容）。
    /// </summary>
    private static bool EnterCompoundByKey(BinaryReader reader, string key, out bool skipped)
    {
        skipped = false;
        while (true)
        {
            byte type;
            try
            {
                type = reader.ReadByte();
            }
            catch (EndOfStreamException)
            {
                return false;
            }

            if (type == 0) // TAG_End
            {
                return false;
            }

            var name = ReadNbtString(reader);

            if (type == 10 && name == key)
            {
                // 成功进入目标 compound
                return true;
            }

            // 跳过非目标标签
            try
            {
                SkipNbtPayload(reader, type);
                skipped = true;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 在当前 compound 流位置读取所有子标签，查找指定名称的 TAG_String。
    /// 找到返回字符串值；遇到 TAG_End 或流结束时返回空字符串。
    /// </summary>
    internal static string FindStringByKey(BinaryReader reader, string key)
    {
        while (true)
        {
            byte type;
            try
            {
                type = reader.ReadByte();
            }
            catch (EndOfStreamException)
            {
                return "";
            }

            if (type == 0) // TAG_End
            {
                return "";
            }

            var name = ReadNbtString(reader);

            if (type == 8 && name == key) // TAG_String
            {
                return ReadNbtString(reader);
            }

            try
            {
                SkipNbtPayload(reader, type);
            }
            catch (EndOfStreamException)
            {
                return "";
            }
        }
    }

    /// <summary>
    /// 读取 NBT 字符串（前缀为 2 字节大端序长度）
    /// </summary>
    internal static string ReadNbtString(BinaryReader reader)
    {
        var lenBytes = reader.ReadBytes(2);
        if (lenBytes.Length < 2)
            throw new EndOfStreamException("读取字符串长度时流结束");

        var len = (ushort)((lenBytes[0] << 8) | lenBytes[1]);
        if (len == 0) return "";

        var bytes = reader.ReadBytes(len);
        if (bytes.Length < len)
            throw new EndOfStreamException("读取字符串内容时流结束");

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// 跳过指定类型标签的 payload。遇到流异常时抛出 EndOfStreamException 由调用方处理。
    /// </summary>
    internal static void SkipNbtPayload(BinaryReader reader, byte type)
    {
        switch (type)
        {
            case 1: // TAG_Byte
                reader.ReadByte();
                break;
            case 2: // TAG_Short
                reader.ReadBytes(2);
                break;
            case 3: // TAG_Int
                reader.ReadBytes(4);
                break;
            case 4: // TAG_Long
                reader.ReadBytes(8);
                break;
            case 5: // TAG_Float
                reader.ReadBytes(4);
                break;
            case 6: // TAG_Double
                reader.ReadBytes(8);
                break;
            case 7: // TAG_Byte_Array
                var baLen = ReadNbtInt(reader);
                reader.ReadBytes(baLen);
                break;
            case 8: // TAG_String
                ReadNbtString(reader);
                break;
            case 9: // TAG_List
                var listType = reader.ReadByte();
                var listLen = ReadNbtInt(reader);
                for (int i = 0; i < listLen; i++)
                    SkipNbtPayload(reader, listType);
                break;
            case 10: // TAG_Compound
                SkipCompound(reader);
                break;
            case 11: // TAG_Int_Array
                var iaLen = ReadNbtInt(reader);
                reader.ReadBytes(iaLen * 4);
                break;
            case 12: // TAG_Long_Array
                var laLen = ReadNbtInt(reader);
                reader.ReadBytes(laLen * 8);
                break;
            default:
                DebugLogger.Warn("NbtReader", $"未知的 NBT 标签类型: {type}，跳过失败");
                throw new InvalidDataException($"未知的 NBT 标签类型: {type}");
        }
    }

    private static void SkipCompound(BinaryReader reader)
    {
        while (true)
        {
            var type = reader.ReadByte();
            if (type == 0) break; // TAG_End
            ReadNbtString(reader);
            SkipNbtPayload(reader, type);
        }
    }

    private static int ReadNbtInt(BinaryReader reader)
    {
        var b = reader.ReadBytes(4);
        if (b.Length < 4)
            throw new EndOfStreamException("读取 int 时流结束");
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }
}
