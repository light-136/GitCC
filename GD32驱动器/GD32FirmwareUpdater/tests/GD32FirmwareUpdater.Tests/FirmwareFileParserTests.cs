// 固件文件解析器单元测试
// 验证文件加载、校验逻辑

using System.IO;
using GD32FirmwareUpdater.Services;

namespace GD32FirmwareUpdater.Tests;

/// <summary>
/// 固件文件解析器测试
/// </summary>
public class FirmwareFileParserTests
{
    [Fact]
    public void LoadFirmware_ValidBinFile_LoadsSuccessfully()
    {
        // 创建临时bin文件
        string tempFile = Path.Combine(Path.GetTempPath(), "test_firmware.bin");
        byte[] testData = new byte[1024];
        Random.Shared.NextBytes(testData);
        File.WriteAllBytes(tempFile, testData);

        try
        {
            var firmware = FirmwareFileParser.LoadFirmware(tempFile);

            Assert.Equal(tempFile, firmware.FilePath);
            Assert.Equal("test_firmware.bin", firmware.FileName);
            Assert.Equal(testData.Length, firmware.Data.Length);
            Assert.Equal(testData, firmware.Data);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFirmware_NonExistentFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() =>
            FirmwareFileParser.LoadFirmware("C:\\nonexistent\\firmware.bin"));
    }

    [Fact]
    public void LoadFirmware_WrongExtension_ThrowsInvalidOperationException()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "test_firmware.hex");
        File.WriteAllBytes(tempFile, new byte[] { 0x01, 0x02 });

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                FirmwareFileParser.LoadFirmware(tempFile));
            Assert.Contains(".hex", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFirmware_EmptyFile_ThrowsInvalidOperationException()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "empty_firmware.bin");
        File.WriteAllBytes(tempFile, Array.Empty<byte>());

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                FirmwareFileParser.LoadFirmware(tempFile));
            Assert.Contains("为空", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void FirmwareInfo_FileSizeText_FormatsCorrectly()
    {
        var info = new Models.FirmwareInfo
        {
            Data = new byte[1500] // 1500 B ≈ 1.5 KB
        };

        Assert.Contains("KB", info.FileSizeText);
    }

    [Fact]
    public void FirmwareInfo_FileSizeText_BytesForSmallFiles()
    {
        var info = new Models.FirmwareInfo
        {
            Data = new byte[500] // 500 B
        };

        Assert.Contains("B", info.FileSizeText);
        Assert.Equal("500 B", info.FileSizeText);
    }
}
