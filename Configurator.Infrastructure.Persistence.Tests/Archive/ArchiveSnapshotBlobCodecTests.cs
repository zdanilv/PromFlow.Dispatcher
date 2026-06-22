using System.Buffers.Binary;
using System.Text;
using Configurator.Infrastructure.Persistence.Archive;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class ArchiveSnapshotBlobCodecTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(1000)]
    public void EncodeDecodeCoils_CountsZeroOneSevenEightNineThousand_RoundTrip(int count)
    {
        var codec = new ArchiveSnapshotBlobCodec();
        var coils = Enumerable.Range(0, count)
            .Select(index => index % 3 == 0)
            .ToArray();

        var blob = codec.EncodeCoils(coils);
        var result = codec.DecodeCoils(blob);

        Assert.True(result.Succeeded, FormatFailure(result.ErrorCode, result.ErrorMessage, result.ErrorDetails));
        Assert.Equal(coils, result.Value);
    }

    [Fact]
    public void EncodeDecodeCoils_AllFalseAllTrueAlternating_RoundTrip()
    {
        var codec = new ArchiveSnapshotBlobCodec();
        var samples = new[]
        {
            Enumerable.Repeat(false, 17).ToArray(),
            Enumerable.Repeat(true, 17).ToArray(),
            Enumerable.Range(0, 17).Select(index => index % 2 == 0).ToArray()
        };

        foreach (var sample in samples)
        {
            var result = codec.DecodeCoils(codec.EncodeCoils(sample));

            Assert.True(result.Succeeded, FormatFailure(result.ErrorCode, result.ErrorMessage, result.ErrorDetails));
            Assert.Equal(sample, result.Value);
        }
    }

    [Fact]
    public void EncodeDecodeRegisters_ZeroOneMaxValue_RoundTrip()
    {
        var codec = new ArchiveSnapshotBlobCodec();
        var registers = new ushort[] { 0, 1, ushort.MaxValue };

        var result = codec.DecodeHoldingRegisters(codec.EncodeHoldingRegisters(registers));

        Assert.True(result.Succeeded, FormatFailure(result.ErrorCode, result.ErrorMessage, result.ErrorDetails));
        Assert.Equal(registers, result.Value);
    }

    [Fact]
    public void EncodeDecodeRegisters_IsLittleEndianAndDeterministic()
    {
        var codec = new ArchiveSnapshotBlobCodec();
        var registers = new ushort[] { 1, 0x1234, ushort.MaxValue };

        var blob = codec.EncodeHoldingRegisters(registers);

        Assert.Equal((byte)'P', blob[0]);
        Assert.Equal((byte)'F', blob[1]);
        Assert.Equal((byte)'S', blob[2]);
        Assert.Equal((byte)'1', blob[3]);
        Assert.Equal(1, blob[4]);
        Assert.Equal(0, blob[5]);
        Assert.Equal((byte)ArchiveSnapshotBlobItemType.HoldingRegisters, blob[6]);
        Assert.Equal(0, blob[7]);
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(8, 4)));
        Assert.Equal(new byte[] { 1, 0, 0x34, 0x12, 0xFF, 0xFF }, blob[12..]);
    }

    [Fact]
    public void Decode_BadMagic_ReturnsArchiveSnapshotBlobInvalidMagic()
    {
        var codec = new ArchiveSnapshotBlobCodec();
        var blob = codec.EncodeCoils([true]);
        blob[0] = (byte)'X';

        var result = codec.DecodeCoils(blob);

        Assert.False(result.Succeeded);
        Assert.Equal("ArchiveSnapshotBlobInvalidMagic", result.ErrorCode);
    }

    [Fact]
    public void Decode_UnsupportedVersion_ReturnsArchiveSnapshotBlobUnsupportedVersion()
    {
        var codec = new ArchiveSnapshotBlobCodec();
        var blob = codec.EncodeCoils([true]);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(4, 2), 2);

        var result = codec.DecodeCoils(blob);

        Assert.False(result.Succeeded);
        Assert.Equal("ArchiveSnapshotBlobUnsupportedVersion", result.ErrorCode);
    }

    [Fact]
    public void Decode_ReservedHeaderByte_ReturnsArchiveSnapshotBlobUnsupportedHeader()
    {
        var codec = new ArchiveSnapshotBlobCodec();
        var blob = codec.EncodeCoils([true]);
        blob[7] = 1;

        var result = codec.DecodeCoils(blob);

        Assert.False(result.Succeeded);
        Assert.Equal("ArchiveSnapshotBlobUnsupportedHeader", result.ErrorCode);
    }

    [Fact]
    public void Decode_TruncatedHeaderOrPayload_ReturnsArchiveSnapshotBlobTruncated()
    {
        var codec = new ArchiveSnapshotBlobCodec();
        var truncatedHeader = Encoding.ASCII.GetBytes("PFS");
        var truncatedPayload = codec.EncodeCoils(Enumerable.Repeat(true, 9).ToArray())[..^1];

        var headerResult = codec.DecodeCoils(truncatedHeader);
        var payloadResult = codec.DecodeCoils(truncatedPayload);

        Assert.False(headerResult.Succeeded);
        Assert.Equal("ArchiveSnapshotBlobTruncated", headerResult.ErrorCode);
        Assert.False(payloadResult.Succeeded);
        Assert.Equal("ArchiveSnapshotBlobTruncated", payloadResult.ErrorCode);
    }

    [Fact]
    public void Decode_NegativeCount_ReturnsArchiveSnapshotBlobInvalidCount()
    {
        var codec = new ArchiveSnapshotBlobCodec();
        var blob = codec.EncodeCoils([]);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(8, 4), -1);

        var result = codec.DecodeCoils(blob);

        Assert.False(result.Succeeded);
        Assert.Equal("ArchiveSnapshotBlobInvalidCount", result.ErrorCode);
    }

    [Fact]
    public void Decode_CountMismatch_ReturnsArchiveSnapshotBlobCountMismatch()
    {
        var codec = new ArchiveSnapshotBlobCodec();
        var extraPayload = codec.EncodeCoils([true]).Concat([byte.MinValue]).ToArray();
        var unusedBitSet = codec.EncodeCoils([false]);
        unusedBitSet[12] = 0b1000_0000;

        var extraResult = codec.DecodeCoils(extraPayload);
        var unusedBitResult = codec.DecodeCoils(unusedBitSet);

        Assert.False(extraResult.Succeeded);
        Assert.Equal("ArchiveSnapshotBlobCountMismatch", extraResult.ErrorCode);
        Assert.False(unusedBitResult.Succeeded);
        Assert.Equal("ArchiveSnapshotBlobCountMismatch", unusedBitResult.ErrorCode);
    }

    [Fact]
    public void Decode_WrongItemType_ReturnsArchiveSnapshotBlobUnsupportedItemType()
    {
        var codec = new ArchiveSnapshotBlobCodec();
        var registersBlob = codec.EncodeHoldingRegisters([1]);

        var result = codec.DecodeCoils(registersBlob);

        Assert.False(result.Succeeded);
        Assert.Equal("ArchiveSnapshotBlobUnsupportedItemType", result.ErrorCode);
    }

    private static string FormatFailure(string? code, string? message, string? details)
        => $"{code}: {message} {details}";
}
